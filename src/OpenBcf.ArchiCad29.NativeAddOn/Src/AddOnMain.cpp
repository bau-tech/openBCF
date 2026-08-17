// openBCF ArchiCAD 29 Add-On - native entry point.
//
// Written and reconciled against the real, public ArchiCAD 29 API DevKit
// (https://github.com/GRAPHISOFT/archicad-api-devkit, release 29.3100 - no registration needed,
// contrary to what this project's own README originally assumed) - every ACAPI_* function name,
// struct field, and the CheckEnvironment/RegisterInterface/Initialize/FreeData signatures below
// were checked against Support/Inc/*.h and the Selection_Manager/Browser_Control example Add-Ons
// in that DevKit, not guessed. One thing remains genuinely unverified because no DevKit example
// covers it: the exact 3D camera math in GetCamera/ApplyCamera below, since API_PerspPars/
// API_AxonoPars aren't documented precisely enough to know axis/sign conventions without a live
// ArchiCAD to test against - see the comments inline. Everything else here - including
// BcfPalette.cpp's DG::Browser hosting - matches real DevKit code or real, confirmed-shipping
// third-party ArchiCAD add-ons (see HelperProcess.h/BcfPalette.h's header comments).

#include "APIEnvir.h"
#include "ACAPinc.h"
#include "APIdefs.h"

// IO::fileSystem (used in CaptureSnapshotPng to delete the temp render) isn't pulled in
// transitively by ACAPinc.h/APIdefs.h - caught building against the real DevKit (C2039 "fileSystem
// is not a member of IO") on the remote test machine.
#include "FileSystem.hpp"

#include <cmath>
#include <cstring>
#include <cstdio>

#include "Interop.h"
#include "HelperProcess.h"
#include "BcfPalette.h"

namespace
{
	// Matches openBCFFix.grc's 'STR#'/'GDLG' 32500 resources (menu + palette) and 'MDID' 32500.
	constexpr short kPaletteMenuResId = 32500;
	constexpr short kPaletteMenuItemIndex = 1;

	// Poll interval for HelperProcess::PollCallbacks - see that function's comment: cheap no-op
	// when nothing is pending, so a fast interval just means lower callback latency, not real cost.
	constexpr UINT kCallbacksPollIntervalMs = 50;

	HelperProcess g_helperProcess;
	bool g_helperProcessReady = false;
	bool g_pollTimerRegistered = false;

	void CALLBACK CallbacksPollTimerProc(HWND /*hwnd*/, UINT /*uMsg*/, UINT_PTR /*idEvent*/, DWORD /*dwTime*/)
	{
		g_helperProcess.PollCallbacks();
	}

	// --- small vector helpers for GetCamera/ApplyCamera's world-space math ---------------------

	struct Vec3 { double x, y, z; };

	Vec3 operator-(const Vec3& a, const Vec3& b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
	Vec3 operator+(const Vec3& a, const Vec3& b) { return { a.x + b.x, a.y + b.y, a.z + b.z }; }
	Vec3 operator*(const Vec3& a, double s) { return { a.x * s, a.y * s, a.z * s }; }
	double Dot(const Vec3& a, const Vec3& b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
	Vec3 Cross(const Vec3& a, const Vec3& b)
	{
		return { a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x };
	}
	Vec3 Normalized(const Vec3& v)
	{
		double len = std::sqrt(Dot(v, v));
		return len > 1e-9 ? Vec3{ v.x / len, v.y / len, v.z / len } : Vec3{ 0, 0, 1 };
	}

	std::wstring GetAddOnDirectory()
	{
		// ACAPI_GetOwnLocation is the real, documented way an Add-On finds its own .apx path
		// (ACAPinc.h) - DeleteLastLocalName () (used the same way in the DevKit's own
		// Communication_Manager example) turns that file location into its containing folder.
		IO::Location ownLoc;
		ACAPI_GetOwnLocation(&ownLoc);
		ownLoc.DeleteLastLocalName();

		GS::UniString folderPath = ownLoc.ToDisplayText();
		// GS::uchar_t is UTF-16 on Windows, layout-identical to wchar_t - the same reinterpret
		// every DevKit example implicitly relies on when passing UniString::ToUStr() into Win32
		// wide-string APIs.
		return std::wstring(reinterpret_cast<const wchar_t*>(folderPath.ToUStr().Get()));
	}

	// DIAGNOSTIC ONLY (temporary - see BcfPalette.cpp's matching LogDiag for why this is a
	// hardcoded path via plain Win32 calls rather than ACAPI_WriteReport or an iostream).
	void LogDiagMain(const char* message)
	{
		HANDLE fileHandle = ::CreateFileW(L"C:\\openBCF-build\\diag.log", FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
			nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (fileHandle == INVALID_HANDLE_VALUE)
			return;
		std::string line = std::string("[AddOnMain] ") + message + "\r\n";
		DWORD written = 0;
		::WriteFile(fileHandle, line.c_str(), static_cast<DWORD>(line.size()), &written, nullptr);
		::CloseHandle(fileHandle);
	}

	// --- HostCallbacks implementation: everything ACAPI-specific that managed code (BcfViewpoint
	// Capture/Apply in OpenBcf.ArchiCad29.NativeClient) needs, since it cannot call ACAPI itself.

	bool GetCamera(BcfCameraData* outCamera)
	{
		API_3DProjectionInfo proj{};
		if (ACAPI_View_Get3DProjectionSets(&proj) != NoError)
			return false;

		// ArchiCAD's model units are already meters (confirmed by APIdefs_Base.h's API_Coord/
		// API_Coord3D doc comments describing plan coordinates as meters), unlike Rhino/Tekla -
		// no unit conversion needed here, unlike those clients' BcfViewpointCapture.
		if (proj.isPersp) {
			const API_PerspPars& persp = proj.u.persp;

			// API_PerspPars gives both a polar form (azimuth/distance around target) and an
			// already-resolved Cartesian form (pos/target, each with a separate camera Z/target Z)
			// - the Cartesian fields are used directly here since they need no reconstruction.
			Vec3 eye{ persp.pos.x, persp.pos.y, persp.cameraZ };
			Vec3 target{ persp.target.x, persp.target.y, persp.targetZ };
			Vec3 direction = Normalized(target - eye);

			// UNVERIFIED: rollAngle's sign/reference and viewCone's axis (horizontal, vertical, or
			// diagonal) aren't specified precisely enough in the DevKit's doc comments to be
			// certain without a live ArchiCAD to test roll/FOV round-trips against - assumed here:
			// roll rotates the "natural" up vector (world +Z projected perpendicular to view
			// direction) counterclockwise around the view direction by rollAngle radians, and
			// viewCone is already a full vertical angle in degrees, matching BCF's FieldOfView
			// directly.
			Vec3 worldUp{ 0, 0, 1 };
			Vec3 right = Normalized(Cross(direction, worldUp));
			Vec3 naturalUp = Cross(right, direction);
			double cosRoll = std::cos(persp.rollAngle);
			double sinRoll = std::sin(persp.rollAngle);
			Vec3 up = Normalized((naturalUp * cosRoll) + (Cross(direction, naturalUp) * sinRoll));

			outCamera->Kind = BcfCameraKind::Perspective;
			outCamera->ViewPoint[0] = eye.x; outCamera->ViewPoint[1] = eye.y; outCamera->ViewPoint[2] = eye.z;
			outCamera->Direction[0] = direction.x; outCamera->Direction[1] = direction.y; outCamera->Direction[2] = direction.z;
			outCamera->UpVector[0] = up.x; outCamera->UpVector[1] = up.y; outCamera->UpVector[2] = up.z;
			outCamera->FieldOfViewDegrees = persp.viewCone;
			outCamera->ViewToWorldScale = 0;
		} else {
			// UNVERIFIED (see this file's header comment): API_AxonoPars carries a 3x4
			// world-to-view transform (API_Tranmat, row-major per APIdefs_Base.h's documented
			// formula) instead of explicit eye/target fields. For an orthonormal rotation+
			// translation transform, the matrix's row vectors are the camera's local axes
			// expressed in world space, and the camera position is recovered from the translation
			// column via posWorld = -R^T * t (R being the 3x3 part) - standard extrinsic-camera-
			// matrix algebra, but ArchiCAD's own sign/row convention for tmx here has not been
			// confirmed against a live session.
			const double* m = proj.u.axono.tranmat.tmx;
			Vec3 right{ m[0], m[1], m[2] };
			Vec3 up{ m[4], m[5], m[6] };
			Vec3 direction{ m[8], m[9], m[10] };
			Vec3 t{ m[3], m[7], m[11] };
			// posWorld = -R^T * t
			Vec3 eye{
				-(right.x * t.x + up.x * t.y + direction.x * t.z),
				-(right.y * t.x + up.y * t.y + direction.y * t.z),
				-(right.z * t.x + up.z * t.y + direction.z * t.z),
			};

			outCamera->Kind = BcfCameraKind::Orthogonal;
			outCamera->ViewPoint[0] = eye.x; outCamera->ViewPoint[1] = eye.y; outCamera->ViewPoint[2] = eye.z;
			outCamera->Direction[0] = direction.x; outCamera->Direction[1] = direction.y; outCamera->Direction[2] = direction.z;
			outCamera->UpVector[0] = up.x; outCamera->UpVector[1] = up.y; outCamera->UpVector[2] = up.z;
			outCamera->FieldOfViewDegrees = 0;
			// TODO(real DevKit): API_AxonoPars's full field list past tranmat/invtranmat wasn't
			// fully enumerated against the header in this session - if it carries a separate
			// zoom/scale field, ViewToWorldScale should be derived from that instead of left at 0.
			outCamera->ViewToWorldScale = 0;
		}

		return true;
	}

	int32_t GetSelectionGuids(BcfElementGuid* outGuids, int32_t outCapacity)
	{
		API_SelectionInfo selectionInfo{};
		GS::Array<API_Neig> selNeigs;
		GSErrCode err = ACAPI_Selection_Get(&selectionInfo, &selNeigs, false);
		if (selectionInfo.marquee.coords != nullptr)
			BMKillHandle(reinterpret_cast<GSHandle*>(&selectionInfo.marquee.coords));
		if (err != NoError)
			return 0;

		auto count = static_cast<int32_t>(selNeigs.GetSize());
		if (outGuids == nullptr || outCapacity < count)
			return count;

		// NOTE: APIGuidToString produces ArchiCAD's own GUID string (its API_Guid, "{xxxxxxxx-...}"
		// form), not the compressed base64 IFC GlobalId BCF's Component.IfcGuid conventionally
		// holds on the other clients (Rhino/Tekla/Revit all use a real IFC GlobalId there). Real
		// IFC GlobalId conversion goes through the separate IFCAPI:: subsystem
		// (IFCAPI::GetObjectAccessor(), confirmed real via Examples/IFC_Test/Src/IFCAPI_Test.cpp
		// in the DevKit) rather than a simple one-line guid-format helper, and wiring that up was
		// out of scope for this pass - viewpoint round-trips within openBCF itself still work
		// correctly with ArchiCAD's own GUID format, but cross-tool component matching against a
		// GUID captured by another BCF client will not match until this is replaced with a real
		// IFCAPI GlobalId lookup.
		for (int32_t i = 0; i < count; ++i) {
			GS::UniString guidStr = APIGuidToString(selNeigs[i].guid);
			auto* buffer = new char16_t[guidStr.GetLength() + 1];
			std::memcpy(buffer, guidStr.ToUStr().Get(), guidStr.GetLength() * sizeof(char16_t));
			buffer[guidStr.GetLength()] = 0;
			outGuids[i] = buffer;
		}

		return count;
	}

	bool CaptureSnapshotPng(uint8_t** outBuffer, int32_t* outLength)
	{
		API_3DProjectionInfo proj{};
		if (ACAPI_View_Get3DProjectionSets(&proj) != NoError)
			return false; // no active 3D window

		API_SpecFolderID folderID = API_TemporaryFolderID;
		IO::Location tempFolder;
		if (ACAPI_ProjectSettings_GetSpecFolder(&folderID, &tempFolder) != NoError)
			return false;

		IO::Location fileLoc(tempFolder, IO::Name("openBCF_snapshot.png"));

		API_PhotoRenderPars renderPars{};
		renderPars.fileTypeID = APIFType_PNGFile;
		renderPars.file = &fileLoc;
		renderPars.colorDepth = APIColorDepth_TC32;
		if (ACAPI_Rendering_PhotoRender(&renderPars) != NoError)
			return false;

		// ACAPI_Rendering_PhotoRender only writes to disk (no in-memory rendering API in this
		// DevKit - confirmed by API_PhotoRenderPars requiring an IO::Location* file field with no
		// buffer alternative) - read the file back and delete it, same round-about-but-real
		// approach Plan_Dump's own temp-file usage in the DevKit examples takes for similar cases.
		IO::File file(fileLoc, IO::File::Fail);
		if (file.Open(IO::File::ReadMode) != NoError)
			return false;

		UInt64 fileSize = 0;
		file.GetDataLength(&fileSize);
		auto* buffer = new uint8_t[fileSize];
		USize readBytes = 0;
		GSErrCode readErr = file.ReadBin(reinterpret_cast<char*>(buffer), static_cast<USize>(fileSize), &readBytes);
		file.Close();
		IO::fileSystem.Delete(fileLoc);

		if (readErr != NoError) {
			delete[] buffer;
			return false;
		}

		*outBuffer = buffer;
		*outLength = static_cast<int32_t>(readBytes);
		return true;
	}

	void FreeSnapshotBuffer(uint8_t* buffer)
	{
		delete[] buffer;
	}

	bool ApplyCamera(const BcfCameraData* camera)
	{
		API_3DProjectionInfo proj{};
		// Preserves camGuid/actCamSet and whichever union member ArchiCAD isn't about to overwrite,
		// matching the DevKit's own documented read-modify-write pattern for Change3DProjectionSets.
		if (ACAPI_View_Get3DProjectionSets(&proj) != NoError)
			return false;

		Vec3 eye{ camera->ViewPoint[0], camera->ViewPoint[1], camera->ViewPoint[2] };
		Vec3 direction = Normalized(Vec3{ camera->Direction[0], camera->Direction[1], camera->Direction[2] });
		Vec3 up = Normalized(Vec3{ camera->UpVector[0], camera->UpVector[1], camera->UpVector[2] });

		if (camera->Kind == BcfCameraKind::Perspective) {
			proj.isPersp = true;
			API_PerspPars& persp = proj.u.persp;

			// BCF carries no explicit eye-target distance - 10m is an arbitrary but harmless
			// placeholder, since target only needs to lie somewhere along `direction` from eye for
			// the resulting view direction to be correct; ArchiCAD's own "distance" field is set
			// to match so the two representations (see GetCamera's comment) stay consistent.
			constexpr double kAssumedTargetDistance = 10.0;
			Vec3 target = eye + (direction * kAssumedTargetDistance);

			persp.pos.x = eye.x; persp.pos.y = eye.y; persp.cameraZ = eye.z;
			persp.target.x = target.x; persp.target.y = target.y; persp.targetZ = target.z;
			persp.distance = kAssumedTargetDistance;
			// UNVERIFIED azimuth convention - see GetCamera's comment; kept consistent with the
			// horizontal component of (eye - target) using the same reference this file assumes
			// when reading azimuth back.
			Vec3 horizontal = eye - target;
			persp.azimuth = std::atan2(horizontal.y, horizontal.x);
			persp.viewCone = camera->FieldOfViewDegrees;

			Vec3 worldUp{ 0, 0, 1 };
			Vec3 right = Normalized(Cross(direction, worldUp));
			Vec3 naturalUp = Cross(right, direction);
			persp.rollAngle = std::atan2(Dot(up, Cross(direction, naturalUp)), Dot(up, naturalUp));
		} else {
			proj.isPersp = false;
			API_AxonoPars& axono = proj.u.axono;

			Vec3 right = Normalized(Cross(direction, up));
			Vec3 correctedUp = Cross(right, direction);
			// t = -R * eye, matching GetCamera's inverse (posWorld = -R^T * t) for the same matrix
			// layout - see that function's comment on the row convention being unverified.
			double* m = axono.tranmat.tmx;
			m[0] = right.x; m[1] = right.y; m[2] = right.z; m[3] = -Dot(right, eye);
			m[4] = correctedUp.x; m[5] = correctedUp.y; m[6] = correctedUp.z; m[7] = -Dot(correctedUp, eye);
			m[8] = direction.x; m[9] = direction.y; m[10] = direction.z; m[11] = -Dot(direction, eye);
		}

		bool switchOnlyAxonoOrPersp = false;
		return ACAPI_View_Change3DProjectionSets(&proj, &switchOnlyAxonoOrPersp) == NoError;
	}

	void ApplySelection(const BcfElementGuid* guids, int32_t count)
	{
		// NOTE: mirrors GetSelectionGuids's caveat - these guid strings are compared as ArchiCAD's
		// own API_Guid format (via APIGuidToString's inverse), not a real IFC GlobalId, so
		// selections captured by another BCF tool won't match here until GetSelectionGuids/
		// ApplySelection both go through a real IFCAPI GlobalId conversion instead.
		ACAPI_Selection_DeselectAll();

		GS::Array<API_Neig> toSelect;
		for (int32_t i = 0; i < count; ++i) {
			GS::UniString guidStr(reinterpret_cast<const GS::uchar_t*>(guids[i]));
			// API_Guid is layout-compatible with GS::Guid but has no constructors of its own (see
			// API_Guid.hpp) - GS::Guid's own string constructor plus GSGuid2APIGuid is the
			// documented conversion path, the exact inverse of APIGuidToString used in
			// GetSelectionGuids above.
			GS::Guid gsGuid(guidStr.ToCStr().Get());
			API_Guid guid = GSGuid2APIGuid(gsGuid);
			if (guid == APINULLGuid)
				continue;

			API_Neig neig(guid);
			toSelect.Push(neig);
		}

		if (toSelect.GetSize() > 0)
			ACAPI_Selection_Select(toSelect, true);
	}

	void GetActiveProjectName(char16_t* outBuffer, int32_t outCapacity)
	{
		API_ProjectInfo projectInfo;
		ACAPI_ProjectOperation_Project(&projectInfo);

		GS::UniString name = (projectInfo.projectName != nullptr && projectInfo.projectName->GetLength() > 0)
			? *projectInfo.projectName
			: GS::UniString("Untitled");

		auto length = static_cast<int32_t>(name.GetLength());
		auto copyLength = length < outCapacity - 1 ? length : outCapacity - 1;
		std::memcpy(outBuffer, name.ToUStr().Get(), static_cast<size_t>(copyLength) * sizeof(char16_t));
		outBuffer[copyLength] = 0;
	}

	void ExecuteJs(const char16_t* script)
	{
		// Only meaningful while a palette (and its DG::Browser) actually exists - the helper may
		// ask to push an event (BrowserBridge.Send) at any time, including moments when the user
		// has closed/hasn't yet opened the palette this Initialize()/FreeData() cycle.
		if (BcfPalette::HasInstance())
			BcfPalette::GetInstance().ExecuteJS(GS::UniString(reinterpret_cast<const GS::uchar_t*>(script)));
	}

	const HostCallbacks kHostCallbacks{
		&GetCamera,
		&GetSelectionGuids,
		&CaptureSnapshotPng,
		&FreeSnapshotBuffer,
		&ApplyCamera,
		&ApplySelection,
		&GetActiveProjectName,
		&ExecuteJs,
	};

	// Real, documented ACAPI notification (Support/Inc/ACAPinc.h, matches the official
	// Browser_Control DevKit example's own NotificationHandler exactly) - the actual, confirmed fix
	// for ArchiCAD never being able to close normally with this Add-On loaded (the remote test machine,
	// 2026-08-12): without ever calling HelperProcess::Shutdown, its callbacks-pipe background
	// thread (usually blocked inside ConnectNamedPipe) ran forever, with nothing to stop it when
	// ArchiCAD tried to exit.
	GSErrCode NotificationHandler(API_NotifyEventID notifID, Int32 /*param*/)
	{
		if (notifID == APINotify_Quit) {
			LogDiagMain("NotificationHandler - APINotify_Quit, shutting down HelperProcess");
			g_helperProcess.Shutdown();
		}
		return NoError;
	}

	GSErrCode MenuCommandHandler(const API_MenuParams* params)
	{
		char buf[128];
		sprintf_s(buf, "MenuCommandHandler entered, itemIndex=%d (expected %d)", (int) params->menuItemRef.itemIndex, (int) kPaletteMenuItemIndex);
		LogDiagMain(buf);

		if (params->menuItemRef.itemIndex != kPaletteMenuItemIndex)
			return NoError;

		// g_helperProcess.Initialize (starting the callbacks pipe server) already happened in
		// Initialize() below - unlike the old in-process DotNetHost, this is cheap (no .NET runtime
		// cold start on this thread), so there's no lazy-init-on-click concern here anymore. The
		// helper .exe itself is launched eagerly from Initialize() too, with a lazy fallback launch
		// in HelperProcess::CallBinding if it isn't up yet by the time a bridge call needs it.
		if (!g_helperProcessReady) {
			ACAPI_WriteReport(
				"openBCF: the helper process's callbacks pipe server failed to start - see "
				"diag.log for details.",
				true);
			return NoError;
		}

		if (!BcfPalette::HasInstance()) {
			BcfPalette::CreateInstance(g_helperProcess);
			LogDiagMain("BcfPalette::CreateInstance done");
		}

		if (BcfPalette::HasInstance()) {
			bool wasVisible = BcfPalette::GetInstance().IsVisible();
			sprintf_s(buf, "BcfPalette instance exists, IsVisible()=%d, about to %s", (int) wasVisible, wasVisible ? "Hide" : "Show");
			LogDiagMain(buf);
			if (wasVisible)
				BcfPalette::GetInstance().Hide();
			else
				BcfPalette::GetInstance().Show();
		}
		else
		{
			LogDiagMain("BcfPalette::HasInstance() is false - this should not happen");
		}

		return NoError;
	}
}

API_AddonType CheckEnvironment(API_EnvirParams* envir)
{
	LogDiagMain("CheckEnvironment called");
	RSGetIndString(&envir->addOnInfo.name, 32000, 1, ACAPI_GetOwnResModule());
	RSGetIndString(&envir->addOnInfo.description, 32000, 2, ACAPI_GetOwnResModule());

	return APIAddon_Normal;
}

GSErrCode RegisterInterface(void)
{
	LogDiagMain("RegisterInterface called");
	// MenuCode_UserDef, matching what RFIX/openBCFFix.grc's 'STR#' 32500 was actually written for
	// (see that file's own header comment: a single header string with no [1] sub-header makes it
	// the title of a brand new top-level main menu - "openBCF > openBCF Panel", positioned among
	// ArchiCAD's own main menu titles like Window, not folded into one of them). A 2026-08-12
	// session temporarily swapped this to MenuCode_Palettes (inserting into ArchiCAD's existing
	// Window > Palettes submenu instead) only to test an unrelated bug theory - real menu commands
	// have worked reliably since the out-of-process helper/pipe redesign, so that theory is moot;
	// reverted per real user feedback (2026-08-17) that the icon belonged in the main menu bar next
	// to Window, not buried in the Palettes submenu, which is exactly what this resource file's own
	// comment already documented as the intended shape.
	ACAPI_MenuItem_RegisterMenu(kPaletteMenuResId, 0, MenuCode_UserDef, MenuFlag_Default);

	return NoError;
}

GSErrCode Initialize(void)
{
	LogDiagMain("Initialize called");
	GSErrCode err = ACAPI_MenuItem_InstallMenuHandler(kPaletteMenuResId, MenuCommandHandler);
	if (err != NoError)
		ACAPI_WriteReport("openBCF: ACAPI_MenuItem_InstallMenuHandler failed", true);

	// Initialize()/FreeData() are NOT a simple "once at Add-On load, once at ArchiCAD exit" pair -
	// real, observed behaviour on the remote test machine (2026-08-12) is that ArchiCAD calls them in a
	// repeating Initialize->FreeData->Initialize->... cycle *without* RegisterInterface repeating,
	// most likely a full unload/reload of this .apx DLL module rather than a once-per-process
	// pair. That's exactly what HelperProcess/HelperProcess.h exists to survive: the out-of-process
	// helper's own lifetime is independent of this DLL being reloaded, so HelperProcess::Initialize
	// only has to restart this DLL's own side (the callbacks pipe server) each cycle, which is
	// cheap - no .NET runtime cold start happens on this thread at all anymore.
	g_helperProcessReady = g_helperProcess.Initialize(&kHostCallbacks);
	LogDiagMain(g_helperProcessReady ? "HelperProcess::Initialize OK" : "HelperProcess::Initialize FAILED");

	// SetTimer(NULL, ...) - a window-less timer whose callback fires via ArchiCAD's own message
	// pump (DispatchMessage) - is what drives HelperProcess::PollCallbacks on ArchiCAD's main
	// thread; no ACAPI-native idle/timer notification exists (checked exhaustively against the
	// DevKit headers). Guarded so a DLL-reload Initialize() cycle doesn't stack up duplicate
	// timers - once registered, a single timer keeps firing for the OS process's lifetime, which
	// covers every subsequent HelperProcess instance's polling needs too.
	if (!g_pollTimerRegistered) {
		g_pollTimerRegistered = ::SetTimer(nullptr, 0, kCallbacksPollIntervalMs, CallbacksPollTimerProc) != 0;
		LogDiagMain(g_pollTimerRegistered ? "Callbacks poll timer registered" : "SetTimer FAILED");
	}

	// Guarded the same way as the poll timer above - ACAPI_ProjectOperation_CatchProjectEvent only
	// needs to run once per process, not once per Initialize()/FreeData() cycle.
	static bool quitHandlerRegistered = false;
	if (!quitHandlerRegistered) {
		quitHandlerRegistered = ACAPI_ProjectOperation_CatchProjectEvent(APINotify_Quit, NotificationHandler) == NoError;
		LogDiagMain(quitHandlerRegistered ? "APINotify_Quit handler registered" : "ACAPI_ProjectOperation_CatchProjectEvent FAILED");
	}

	return err;
}

GSErrCode FreeData(void)
{
	LogDiagMain("FreeData called - destroying BcfPalette only, NOT touching the helper process (see HelperProcess.h - its whole point is surviving this DLL being unloaded/reloaded)");
	if (BcfPalette::HasInstance())
		BcfPalette::DestroyInstance();

	// Deliberately not telling the helper to hide/close anything here, and not un-registering the
	// poll timer - both are meant to persist across this DLL's Initialize()/FreeData() cycling.

	return NoError;
}
