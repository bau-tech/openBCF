#pragma once

// Shared ABI for the openBCF ArchiCAD 29 Add-On's native side.
//
// ArchiCAD's Add-On API (ACAPI) is native C/C++ only - there is no managed hosting story the way
// Revit/Tekla/Rhino provide - so unlike every other client in this repo, OpenBcf.Core/Dui/
// Dui.WebView cannot be referenced directly by ACAPI code, and this native Add-On (a plain .apx
// built from this folder) does not host .NET itself. Real, hard-won finding on the remote test
// machine (2026-08-12): ArchiCAD cycles Initialize()/FreeData() on this Add-On
// repeatedly and independently of anything it does (most likely a full .apx DLL unload/reload),
// which a hosted CoreCLR runtime cannot survive - see HelperProcess.h for the full story. Instead,
// a separate process (src/OpenBcf.ArchiCad29.Helper, a normal .NET apphost referencing
// OpenBcf.Core/Dui/Dui.WebView exactly like the other clients do) owns the WPF/WebView2 panel
// entirely on its own, launched/reconnected to by HelperProcess and talked to over two named pipes
// (see HelperProcess.cpp's wire format).
//
// HostCallbacks below is used purely on the native side now: AddOnMain.cpp builds the real table
// of ACAPI-calling functions once (kHostCallbacks) and hands it to HelperProcess::Initialize;
// HelperProcess::PollCallbacks (running on ArchiCAD's main thread, the only thread ACAPI calls are
// safe from) calls straight through it when a request arrives from the helper process over the
// callbacks pipe - camera/selection/snapshot/active-project-name are exactly the ACAPI-specific
// operations the helper (running out-of-process, with no ACAPI access of its own) cannot perform
// itself.
//
// Checked against the real, public ArchiCAD 29 API DevKit
// (https://github.com/GRAPHISOFT/archicad-api-devkit) - every ACAPI call in AddOnMain.cpp/
// BcfPalette.cpp uses real DevKit headers, not guessed signatures.

#include <cstdint>

extern "C" {

enum class BcfCameraKind : int32_t
{
	Perspective = 0,
	Orthogonal = 1,
};

// Mirrors OpenBcf.Core.Model.Visualization.BcfCamera - meters, matching BCF/IFC convention (same
// unit contract as every other client's viewpoint capture/apply).
struct BcfCameraData
{
	BcfCameraKind Kind;
	double ViewPoint[3];
	double Direction[3];
	double UpVector[3];
	double FieldOfViewDegrees;   // valid when Kind == Perspective
	double ViewToWorldScale;     // valid when Kind == Orthogonal
};

// A single selected element's IFC GUID, UTF-16 (matches ACAPI_Goodies's IFC GUID conversion
// helpers, which already produce a wchar_t/UniString-compatible IFC-format GUID string).
using BcfElementGuid = const char16_t*;

// Function pointer table native hands to managed once, via SetHostCallbacks. All calls happen on
// ArchiCAD's main thread only (WebView2/WPF dispatch takes care of getting managed code back onto
// the STA thread that owns the panel before invoking any of these) - none of this is safe to call
// off-thread, same restriction ACAPI itself imposes on every ACAPI_* call.
struct HostCallbacks
{
	// Fills outCamera from the active 3D window's current camera. Returns false if there is no
	// active 3D window (e.g. a 2D floor plan window is active).
	bool (*GetCamera)(BcfCameraData* outCamera);

	// Fills outGuids (capacity outCapacity) with the IFC GUIDs of the current 3D selection and
	// returns the actual count. If outCapacity is too small, returns the required count and
	// writes nothing - the same "ask twice" pattern Win32 APIs use, so managed code can size its
	// buffer exactly.
	int32_t (*GetSelectionGuids)(BcfElementGuid* outGuids, int32_t outCapacity);

	// Renders the active 3D window to a PNG and hands ownership of the buffer to the caller;
	// managed code must call FreeSnapshotBuffer exactly once when done with it. Returns false if
	// there is no active 3D window.
	bool (*CaptureSnapshotPng)(uint8_t** outBuffer, int32_t* outLength);
	void (*FreeSnapshotBuffer)(uint8_t* buffer);

	// Moves the active 3D window's camera to match camera. Returns false if there is no active 3D
	// window.
	bool (*ApplyCamera)(const BcfCameraData* camera);

	// Replaces the current 3D selection with the elements matching the given IFC GUIDs (best
	// effort - GUIDs with no matching element are skipped, mirroring every other client's
	// "matched/missing" tolerance).
	void (*ApplySelection)(const BcfElementGuid* guids, int32_t count);

	// Writes the active plan file's display name (no extension) into outBuffer (capacity
	// outCapacity, UTF-16, null-terminated) - the model key BcfSessionBinding.ResolveModelKey
	// uses, mirroring RhinoDoc.Path/Tekla's model path on the other clients.
	void (*GetActiveProjectName)(char16_t* outBuffer, int32_t outCapacity);

	// Runs script (null-terminated UTF-16) in the active BcfPalette's DG::Browser, if one exists -
	// a no-op otherwise. This is how OpenBcf.ArchiCad29.Helper (which owns no browser/window of its
	// own now - see HelperProcess.h) delivers BrowserBridge.Send's proactive push events
	// (window.__openbcfDuiReceiveEvent(...)) into the actual page, since only native code can touch
	// the DG::Browser instance.
	void (*ExecuteJs)(const char16_t* script);
};

} // extern "C"
