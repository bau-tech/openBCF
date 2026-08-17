#pragma once

#include "APIEnvir.h"
#include "ACAPinc.h"		// also includes APIdefs.h
#include "DGModule.hpp"
#include "DGBrowser.hpp"

#include "HelperProcess.h"

// Owns the floating openBCF palette window and the native DG::Browser control that hosts the DUI3
// frontend directly - no foreign process's window is ever embedded (see HelperProcess.h's header
// comment for the real, live-confirmed deadlock that approach caused).
//
// Modeled directly on the real ArchiCAD API DevKit 29 "Browser_Control" example
// (Examples/Browser_Control/Src/BrowserPalette.cpp/.hpp in
// https://github.com/GRAPHISOFT/archicad-api-devkit) and cross-checked against Speckle's actual
// ArchiCAD connector (github.com/specklesystems/speckle-cpp-connectors), which uses the exact same
// DG::Browser + JS::Object/RegisterAsynchJSObject pattern for its own docked panel - both real,
// shipping code, not guessed. The one piece neither of those examples needs (since their bridge
// methods run entirely in-process): every registered JS::Function here forwards to the
// out-of-process Helper over HelperProcess::CallBinding rather than doing ACAPI work directly,
// since all the real BCF/business logic lives in OpenBcf.Core (C#), reached the same way
// AddOnMain.cpp's HostCallbacks already reach ACAPI from the other direction.
class BcfPalette final : public DG::Palette, public DG::PanelObserver
{
public:
	explicit BcfPalette(HelperProcess& helperProcess);
	virtual ~BcfPalette();

	void Show();
	void Hide();

	// Runs script in this palette's browser - called from AddOnMain.cpp's ExecuteJs HostCallback,
	// which HelperProcess::PollCallbacks invokes on ArchiCAD's main thread when the helper asks to
	// deliver a BrowserBridge.Send push event (window.__openbcfDuiReceiveEvent(...)) into the page.
	void ExecuteJS(const GS::UniString& script);

	static bool HasInstance();
	static void CreateInstance(HelperProcess& helperProcess);
	static BcfPalette& GetInstance();
	static void DestroyInstance();

protected:
	void PanelResized(const DG::PanelResizeEvent& ev) override;
	void PanelCloseRequested(const DG::PanelCloseRequestEvent& ev, bool* accepted) override;

private:
	// Matches openBCFFix.grc's 'GDLG' 32500 palette resource: item [1] is the Browser item this
	// control binds to (DG::Browser's Panel&,item constructor resolves it, same as the official
	// Browser_Control example's BrowserId).
	enum { kBrowserItemId = 1 };

	HelperProcess& m_helperProcess;
	DG::Browser m_browser;

	void InitBrowserControl();
	void RegisterBridgeObjects();
	void SetMenuItemCheckedState(bool isChecked);

	static GS::Ref<BcfPalette> s_instance;
};
