#pragma once

#include <cstdint>
#include <string>
#include <vector>
#include "Interop.h"

// Manages the out-of-process helper (OpenBcf.ArchiCad29.Helper.exe) that owns .NET/OpenBcf.Core -
// see this file's header comment history for why .NET cannot be hosted in-process: ArchiCAD cycles
// Initialize()/FreeData() on this Add-On repeatedly and independently of anything it does (most
// likely a full .apx DLL unload/reload, confirmed on the remote test machine), which a hosted CoreCLR
// runtime cannot survive.
//
// Real, hard-won pivot (the remote test machine, 2026-08-12): the first version of this class had the
// helper own its own WPF/WebView2 window, reparented (cross-process SetParent) into this Add-On's
// native DG::Palette. That deadlocked ArchiCAD outright: creating a child window whose parent HWND
// belongs to a *different process's* thread requires a synchronous cross-process SendMessage to
// attach input queues, which blocked forever because ArchiCAD's main thread was itself blocked
// waiting on this class's IPC call to finish (confirmed live: palette appeared empty, then
// ArchiCAD froze solid). The real fix - matching how Speckle's actual ArchiCAD connector works
// (github.com/specklesystems/speckle-cpp-connectors) - is to never embed a foreign-process HWND at
// all: BcfPalette now hosts ArchiCAD's own native DG::Browser control directly (real ACAPI/DGLib
// API - see BcfPalette.cpp and the official Browser_Control DevKit example), which lives entirely
// within ArchiCAD's own process/thread, so no cross-process window relationship is ever created.
//
// With no foreign window involved, this class's only remaining job is a plain request/response RPC
// channel to the helper process - no async queueing or deadlock-avoidance tricks needed:
//   - "bridge" pipe (\\.\pipe\openbcf-<ArchiCAD PID>-bridge) - the HELPER is the server (it must be
//     discoverable by a freshly-reloaded native DLL that has no memory of anything), this Add-On
//     connects as a client for each JS::Function call BcfPalette's registered bridge objects
//     receive (see CallBinding) - forwarding {methodName, argsJson} and blocking for the JSON
//     result, exactly mirroring what OpenBcf.Dui.Bridge.BrowserBridge already does for the
//     WebView2-hosted clients, just over a pipe instead of an in-process COM call.
//   - "callbacks" pipe (\\.\pipe\openbcf-<ArchiCAD PID>-cb) - this Add-On is the server, the helper
//     connects as a client on demand: for ACAPI-specific data (camera/selection/snapshot/active
//     project name, unchanged from before) and now also to ask this Add-On to run script in the
//     browser (ExecuteJs, for BrowserBridge.Send's proactive push events).
class HelperProcess
{
public:
	// Starts the callbacks pipe server (on a background thread - see HelperProcess.cpp) and
	// registers the main-thread poller (via SetTimer) that dispatches queued callback requests
	// through the real HostCallbacks implementation in AddOnMain.cpp. Safe to call multiple times
	// (idempotent) - AddOnMain.cpp calls this from Initialize(), which can run more than once.
	bool Initialize(const HostCallbacks* callbacks);

	// Ensures a helper process is running (launching Helper.exe next to this Add-On's own .apx if
	// no existing one answers on the bridge pipe), then forwards a single binding method call to it
	// and blocks for the JSON result envelope ({"isError":false,"result":...} or
	// {"isError":true,"message":...}), which the caller (a bridge JS::Function lambda in
	// BcfPalette.cpp) returns directly as the resolved value of the real native Promise ACAPI's
	// RegisterAsynchJSObject gives that JS call. Returns an {"isError":true,...} envelope itself if
	// the helper couldn't be reached at all.
	std::wstring CallBinding(const std::wstring& bindingName, const std::wstring& methodName, const std::wstring& argsJson);

	// The local HTTP port the helper's static file server (serving the DUI3 frontend's wwwroot) and
	// this Add-On's DG::Browser::LoadURL both derive independently from the same ArchiCAD PID, so
	// no discovery round-trip is needed before the very first LoadURL call.
	static int HttpPort();

	// Called from the SetTimer callback on ArchiCAD's main thread (~20x/sec) - see
	// HelperProcess.cpp's PollCallbacks. Cheap no-op when no request is pending.
	void PollCallbacks();

	// Stops the callbacks pipe server's background thread cleanly - call this from an
	// APINotify_Quit handler (see AddOnMain.cpp). Without this, that thread runs forever, most of
	// the time blocked inside ConnectNamedPipe waiting for a connection that may never come; a real,
	// confirmed-live symptom of never calling this (the remote test machine, 2026-08-12) is ArchiCAD being
	// unable to exit normally, requiring a force-close every time. Safe to call even if Initialize
	// was never called or already shut down.
	void Shutdown();

private:
	bool m_callbacksServerStarted = false;
	const HostCallbacks* m_callbacks = nullptr;

	bool ConnectToBridgePipe(void** outPipeHandle);
	bool LaunchHelperProcess();

	// Real, confirmed-live finding (the remote test machine, 2026-08-12): ACAPI's RegisterAsynchJSObject
	// callbacks run ON ArchiCAD's main thread despite the "Asynch" name (contrary to this class's
	// original assumption) - so a plain blocking WriteFile/ReadFile pair in CallBinding deadlocks
	// the moment the helper's own binding method needs to call back into native itself (e.g.
	// GetActiveProjectName during Connect): that callback can only be serviced by PollCallbacks,
	// which can only run via the SetTimer callback on this same main thread - which is blocked.
	// These pump the pipe's overlapped I/O with a short timeout loop, calling PollCallbacks on every
	// timeout, so ArchiCAD's main thread keeps servicing its own ACAPI callbacks *while* it waits
	// for the helper's top-level response - breaking the deadlock without needing SetTimer/message-
	// loop re-entrancy at all.
	bool WriteFramePumped(void* pipe, uint8_t messageType, const uint8_t* payload, uint32_t payloadSize);
	bool ReadFramePumped(void* pipe, uint8_t* outMessageType, std::vector<uint8_t>* outPayload);
};
