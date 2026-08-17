#include "BcfPalette.h"

#include <windows.h>
#include <string>
#include <array>

namespace
{
	// Matches openBCFFix.grc's 'STR#'/'GDLG' 32500 resources - see that file for the menu
	// string/palette layout this ID refers to. A fixed GUID (like BrowserPaletteGuid in the real
	// Browser_Control example) identifies this palette to ArchiCAD across sessions (docking state,
	// position) - generated once for this project, must not change afterwards.
	constexpr short kPaletteResId = 32500;
	constexpr short kMenuItemIndex = 1;
	const GS::Guid kPaletteGuid("{4B1E9E3D-7C2B-4C9A-9E5A-2E1B7E8B3B7A}");

	// Every IBinding method OpenBcf.ArchiCad29.Helper's bindings expose (Bindings/*.cs there) -
	// mirrors OpenBcf.Dui.Bridge.BrowserBridge's own reflection-based method list exactly, just
	// enumerated explicitly here since JS::Object needs each method registered as its own
	// JS::Function up front (see this file's header comment on why: a real, confirmed-live ACAPI
	// limitation - REDACTED-internal-ip, 2026-08-12 - where a second call into the SAME registered
	// JS::Function cannot get through while an earlier call to it is still pending, e.g. Connect
	// blocking on the user's project pick while ResolveProjectPick tries to answer it through what
	// used to be the same shared "RunMethod" function. Giving every method its own JS::Function
	// makes them independently callable no matter what else is in flight.
	struct BindingMethod { const wchar_t* bindingName; const wchar_t* methodName; };
	constexpr std::array<BindingMethod, 20> kBindingMethods = { {
		{ L"pingBinding", L"Ping" },
		{ L"pingBinding", L"GetHostName" },
		{ L"bcfSessionBinding", L"GetSettings" },
		{ L"bcfSessionBinding", L"TryAutoConnect" },
		{ L"bcfSessionBinding", L"Disconnect" },
		{ L"bcfSessionBinding", L"Connect" },
		{ L"bcfSessionBinding", L"CompleteConnect" },
		{ L"bcfSessionBinding", L"ResolveProjectPick" },
		{ L"bcfIssueBinding", L"GetExtensions" },
		{ L"bcfIssueBinding", L"ListTopics" },
		{ L"bcfIssueBinding", L"GetTopic" },
		{ L"bcfIssueBinding", L"CreateTopic" },
		{ L"bcfIssueBinding", L"UpdateTopicStatus" },
		{ L"bcfIssueBinding", L"CreateComment" },
		{ L"bcfIssueBinding", L"CaptureCurrentViewpointSnapshot" },
		{ L"bcfIssueBinding", L"SaveViewpointSnapshot" },
		{ L"bcfIssueBinding", L"GetSnapshotDataUrl" },
		{ L"bcfIssueBinding", L"ApplyViewpoint" },
		{ L"bcfArchiveBinding", L"ExportToFile" },
		{ L"bcfArchiveBinding", L"ImportFromFile" },
	} };

	// DIAGNOSTIC ONLY (kept from the in-process debugging session - still useful for verifying
	// real Initialize()/FreeData() cycling and the new DG::Browser handoff on REDACTED-internal-ip).
	// ACAPI_WriteReport goes to ArchiCAD's in-app Report window, not a file, which turned out to be
	// much less convenient to check than expected - this appends plain text next to the Add-On's
	// own .apx instead, so it can be inspected directly over SSH.
	void LogDiag(const char* message)
	{
		const wchar_t* logPath = L"C:\\openBCF-build\\diag.log";

		HANDLE fileHandle = ::CreateFileW(logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
			nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (fileHandle == INVALID_HANDLE_VALUE)
			return;

		std::string line = std::string(message) + "\r\n";
		DWORD written = 0;
		::WriteFile(fileHandle, line.c_str(), static_cast<DWORD>(line.size()), &written, nullptr);
		::CloseHandle(fileHandle);
	}

	// GS::uchar_t is UTF-16 on Windows, layout-identical to wchar_t - the same reinterpret every
	// DevKit example implicitly relies on when crossing UniString <-> Win32 wide-string APIs.
	GS::UniString ToUniString(const std::wstring& text)
	{
		return GS::UniString(reinterpret_cast<const GS::uchar_t*>(text.c_str()));
	}

	std::wstring ToWString(const GS::UniString& text)
	{
		return std::wstring(reinterpret_cast<const wchar_t*>(text.ToUStr().Get()));
	}

	// Matches the official Browser_Control example's own GetStringFromJavaScriptVariable exactly -
	// every RunMethod call here is invoked with exactly one JSON-string argument (see this file's
	// header comment for why: it sidesteps any ambiguity in how ACAPI bundles multiple JS call
	// arguments into a single JS::Base, since we never pass more than one).
	GS::UniString GetStringFromJavaScriptVariable(GS::Ref<JS::Base> jsVariable)
	{
		GS::Ref<JS::Value> jsValue = GS::DynamicCast<JS::Value>(jsVariable);
		if (jsValue != nullptr && jsValue->GetType() == JS::Value::STRING)
			return jsValue->GetString();
		return GS::EmptyUniString;
	}
}

GS::Ref<BcfPalette> BcfPalette::s_instance;

BcfPalette::BcfPalette(HelperProcess& helperProcess) :
	DG::Palette(ACAPI_GetOwnResModule(), kPaletteResId, ACAPI_GetOwnResModule(), kPaletteGuid),
	m_helperProcess(helperProcess),
	m_browser(GetReference(), kBrowserItemId)
{
	LogDiag("BcfPalette constructor called");
	Attach(*this);
	BeginEventProcessing();
	InitBrowserControl();
	LogDiag("BcfPalette constructor finished (browser control initialized)");
}

BcfPalette::~BcfPalette()
{
	LogDiag("~BcfPalette() destructor called");
	EndEventProcessing();
}

bool BcfPalette::HasInstance()
{
	return s_instance != nullptr;
}

void BcfPalette::CreateInstance(HelperProcess& helperProcess)
{
	DBASSERT(!HasInstance());
	s_instance = new BcfPalette(helperProcess);

	// Real, documented ACAPI call (Support/Inc/ACAPinc.h, matches the official Browser_Control
	// DevKit example's CreateInstance exactly) - and the actual, confirmed root cause of this
	// entire out-of-process rewrite's remaining symptom (palette flashing then disappearing, see
	// diag.log on REDACTED-internal-ip, 2026-08-12): ACAPinc.h's own doc comment for
	// ACAPI_KeepInMemory states plainly that "when in memory, the Initialize and the FreeData
	// functions of your add-on are not called" - i.e. without this call, ArchiCAD unloads/reloads
	// this Add-On (calling FreeData, which destroys this very instance via DestroyInstance) shortly
	// after almost any interaction, independent of anything the Add-On itself does - exactly the
	// behaviour observed and worked around (but never actually fixed at the source) throughout this
	// session via HelperProcess's whole out-of-process design. That workaround remains valuable
	// defense-in-depth (this call only takes effect once a palette actually exists), but this is
	// the real fix for the disappearing-palette symptom itself.
	ACAPI_KeepInMemory(true);
}

BcfPalette& BcfPalette::GetInstance()
{
	DBASSERT(HasInstance());
	return *s_instance;
}

void BcfPalette::DestroyInstance()
{
	LogDiag("DestroyInstance() called - about to release s_instance");
	s_instance = nullptr;
}

void BcfPalette::Show()
{
	LogDiag("Show() called");
	DG::Palette::Show();
	SetMenuItemCheckedState(true);
}

void BcfPalette::Hide()
{
	LogDiag("Hide() called");
	DG::Palette::Hide();
	SetMenuItemCheckedState(false);
}

void BcfPalette::ExecuteJS(const GS::UniString& script)
{
	m_browser.ExecuteJS(script);
}

void BcfPalette::InitBrowserControl()
{
	// HelperProcess::Initialize (Add-On load time, well before any menu click could get here)
	// already launched the helper eagerly, so its HTTP static file server has a head start on this
	// LoadURL - but if the very first palette open still loses that race (e.g. right after
	// ArchiCAD's own startup), DG::Browser shows a connection-refused error page rather than
	// retrying on its own; toggling the palette off/on (which reconstructs this class and calls
	// LoadURL again) recovers. Not worth a dedicated retry-with-backoff for a one-time race that
	// self-resolves within milliseconds in the overwhelmingly common case.
	wchar_t urlBuffer[64];
	swprintf_s(urlBuffer, L"http://127.0.0.1:%d/", HelperProcess::HttpPort());
	LogDiag("InitBrowserControl - LoadURL");
	m_browser.LoadURL(ToUniString(urlBuffer));

	RegisterBridgeObjects();
}

void BcfPalette::RegisterBridgeObjects()
{
	// One JS::Object per binding name, fully built (every method added) before registering it -
	// matches the official Browser_Control example's own build-then-register order exactly, rather
	// than assuming RegisterAsynchJSObject tolerates items being added afterward. Critically, one
	// independent JS::Function per METHOD rather than a single shared "RunMethod" dispatcher - see
	// kBindingMethods' comment for why.
	JS::Object* currentObject = nullptr;
	std::wstring currentBindingName;

	auto flushCurrentObject = [this, &currentObject]() {
		if (currentObject != nullptr)
			m_browser.RegisterAsynchJSObject(currentObject);
	};

	for (const BindingMethod& entry : kBindingMethods) {
		if (currentObject == nullptr || currentBindingName != entry.bindingName) {
			flushCurrentObject();
			currentBindingName = entry.bindingName;
			currentObject = new JS::Object(ToUniString(currentBindingName));
		}

		std::wstring bindingName = currentBindingName;
		std::wstring methodName = entry.methodName;
		currentObject->AddItem(new JS::Function(ToUniString(methodName),
			[this, bindingName, methodName](GS::Ref<JS::Base> param) -> GS::Ref<JS::Base> {
				std::wstring argsJson = ToWString(GetStringFromJavaScriptVariable(param));
				std::wstring resultJson = m_helperProcess.CallBinding(bindingName, methodName, argsJson);
				return new JS::Value(ToUniString(resultJson));
			}));
	}
	flushCurrentObject();

	LogDiag("RegisterBridgeObjects - all binding objects registered");
}

void BcfPalette::PanelResized(const DG::PanelResizeEvent& ev)
{
	BeginMoveResizeItems();
	m_browser.Resize(ev.GetHorizontalChange(), ev.GetVerticalChange());
	EndMoveResizeItems();
}

void BcfPalette::PanelCloseRequested(const DG::PanelCloseRequestEvent& /*ev*/, bool* accepted)
{
	LogDiag("PanelCloseRequested event fired - forcing accepted=false and calling Hide()");
	// Same behaviour as ArchiCAD's own tool palettes: the close box hides the palette rather than
	// destroying it, so its docking position/visibility is remembered for next time.
	*accepted = false;
	Hide();
}

void BcfPalette::SetMenuItemCheckedState(bool isChecked)
{
	API_MenuItemRef itemRef{};
	itemRef.menuResID = kPaletteResId;
	itemRef.itemIndex = kMenuItemIndex;

	GSFlags itemFlags{};
	ACAPI_MenuItem_GetMenuItemFlags(&itemRef, &itemFlags);
	if (isChecked)
		itemFlags |= API_MenuItemChecked;
	else
		itemFlags &= ~API_MenuItemChecked;
	ACAPI_MenuItem_SetMenuItemFlags(&itemRef, &itemFlags);
}
