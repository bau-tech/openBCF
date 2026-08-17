#include "HelperProcess.h"

#include <windows.h>
#include <vector>
#include <thread>
#include <atomic>
#include <functional>

// ACAPI_GetOwnLocation - same real, DevKit-confirmed pattern AddOnMain.cpp's GetAddOnDirectory
// uses - needed here too, to find OpenBcf.ArchiCad29.Helper.exe next to this Add-On's own .apx.
#include "APIEnvir.h"
#include "ACAPinc.h"

namespace
{
	// DIAGNOSTIC ONLY - see BcfPalette.cpp/AddOnMain.cpp's matching LogDiag/LogDiagMain for why
	// this is a hardcoded path via plain Win32 calls. Separate copy here (rather than sharing a
	// header) matches this project's established per-file convention.
	void LogDiagHelperProcess(const char* message)
	{
		HANDLE fileHandle = ::CreateFileW(L"C:\\openBCF-build\\diag.log", FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
			nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (fileHandle == INVALID_HANDLE_VALUE)
			return;
		std::string line = std::string("[HelperProcess] ") + message + "\r\n";
		DWORD written = 0;
		::WriteFile(fileHandle, line.c_str(), static_cast<DWORD>(line.size()), &written, nullptr);
		::CloseHandle(fileHandle);
	}

	// --- Wire protocol -----------------------------------------------------------------------
	// Every message on both pipes: [4-byte LE payload length][1-byte message type][payload].
	// Bridge pipe (helper = server, native = client, reconnects fresh per call):
	constexpr uint8_t kBridgeCall = 0x01;      // payload: int32 bindingNameCharCount, bindingName
	                                            // (UTF-16LE), int32 methodNameCharCount, methodName
	                                            // (UTF-16LE), then argsJson (UTF-16LE, rest of frame)
	constexpr uint8_t kBridgeResult = 0x82;    // payload: resultJson (UTF-16LE) - the JSON envelope
	                                            // {"isError":...} verbatim; native never interprets it.
	// Callbacks pipe (native = server, helper = client, connects fresh per call):
	constexpr uint8_t kCbGetCamera = 0x10;             // payload: (empty)
	constexpr uint8_t kCbGetSelectionGuids = 0x11;     // payload: (empty)
	constexpr uint8_t kCbCaptureSnapshotPng = 0x12;    // payload: (empty)
	constexpr uint8_t kCbApplyCamera = 0x13;           // payload: BcfCameraData wire form
	constexpr uint8_t kCbApplySelection = 0x14;        // payload: guid list wire form
	constexpr uint8_t kCbGetActiveProjectName = 0x15;  // payload: (empty)
	constexpr uint8_t kCbExecuteJs = 0x16;             // payload: script (UTF-16LE)
	// Shared response types:
	constexpr uint8_t kRespAck = 0x80;
	constexpr uint8_t kRespNack = 0x81;
	constexpr uint8_t kRespCameraData = 0x90;
	constexpr uint8_t kRespNoCamera = 0x91;
	constexpr uint8_t kRespGuidList = 0x92;
	constexpr uint8_t kRespSnapshotData = 0x93;
	constexpr uint8_t kRespNoSnapshot = 0x94;
	constexpr uint8_t kRespProjectName = 0x95;

	std::wstring GetArchiCadPidPipeSuffix()
	{
		return std::to_wstring(::GetCurrentProcessId());
	}

	std::wstring BridgePipeName()
	{
		return L"\\\\.\\pipe\\openbcf-" + GetArchiCadPidPipeSuffix() + L"-bridge";
	}

	std::wstring CallbacksPipeName()
	{
		return L"\\\\.\\pipe\\openbcf-" + GetArchiCadPidPipeSuffix() + L"-cb";
	}

	// --- Frame I/O helpers ---------------------------------------------------------------------

	bool WriteFrame(HANDLE pipe, uint8_t messageType, const uint8_t* payload, uint32_t payloadSize)
	{
		DWORD written = 0;
		if (!::WriteFile(pipe, &payloadSize, sizeof(payloadSize), &written, nullptr) || written != sizeof(payloadSize))
			return false;
		if (!::WriteFile(pipe, &messageType, sizeof(messageType), &written, nullptr) || written != sizeof(messageType))
			return false;
		if (payloadSize > 0 && (!::WriteFile(pipe, payload, payloadSize, &written, nullptr) || written != payloadSize))
			return false;
		return true;
	}

	bool ReadExact(HANDLE pipe, void* buffer, DWORD size)
	{
		DWORD totalRead = 0;
		while (totalRead < size) {
			DWORD read = 0;
			if (!::ReadFile(pipe, static_cast<uint8_t*>(buffer) + totalRead, size - totalRead, &read, nullptr) || read == 0)
				return false;
			totalRead += read;
		}
		return true;
	}

	bool ReadFrame(HANDLE pipe, uint8_t* outMessageType, std::vector<uint8_t>* outPayload)
	{
		uint32_t payloadSize = 0;
		if (!ReadExact(pipe, &payloadSize, sizeof(payloadSize)))
			return false;
		if (!ReadExact(pipe, outMessageType, sizeof(*outMessageType)))
			return false;
		outPayload->resize(payloadSize);
		if (payloadSize > 0 && !ReadExact(pipe, outPayload->data(), payloadSize))
			return false;
		return true;
	}

	// UTF-16LE text <-> byte payload helpers (used for both bindingName/payloadJson/resultJson and
	// ExecuteJs's script text - every text value on these pipes is plain UTF-16LE, no length prefix
	// needed beyond the frame's own payload length when it's the only/last field).

	// DIAGNOSTIC ONLY - lossy wstring->string for LogDiagHelperProcess (which only takes narrow
	// strings); explicit truncating cast avoids /WX turning the implicit-narrowing warning the
	// std::string(iterator,iterator) constructor would otherwise trigger into a build error.
	std::string NarrowForLog(const std::wstring& text)
	{
		std::string narrow(text.size(), '\0');
		for (size_t i = 0; i < text.size(); ++i)
			narrow[i] = static_cast<char>(text[i]);
		return narrow;
	}

	void AppendWString(std::vector<uint8_t>* buffer, const std::wstring& text)
	{
		const uint8_t* p = reinterpret_cast<const uint8_t*>(text.data());
		buffer->insert(buffer->end(), p, p + text.size() * sizeof(wchar_t));
	}

	std::wstring BytesToWString(const uint8_t* data, size_t byteCount)
	{
		return std::wstring(reinterpret_cast<const wchar_t*>(data), byteCount / sizeof(wchar_t));
	}

	// --- BcfCameraData <-> wire form -------------------------------------------------------------
	// Fixed layout: int32 kind, 9x double (viewPoint[3], direction[3], upVector[3]), double fov,
	// double viewToWorldScale. 4 + 9*8 + 8 + 8 = 92 bytes.

	std::vector<uint8_t> PackCamera(const BcfCameraData& camera)
	{
		std::vector<uint8_t> buffer(4 + 9 * 8 + 8 + 8);
		uint8_t* p = buffer.data();
		int32_t kind = static_cast<int32_t>(camera.Kind);
		memcpy(p, &kind, 4); p += 4;
		memcpy(p, camera.ViewPoint, 24); p += 24;
		memcpy(p, camera.Direction, 24); p += 24;
		memcpy(p, camera.UpVector, 24); p += 24;
		memcpy(p, &camera.FieldOfViewDegrees, 8); p += 8;
		memcpy(p, &camera.ViewToWorldScale, 8);
		return buffer;
	}

	bool UnpackCamera(const std::vector<uint8_t>& buffer, BcfCameraData* outCamera)
	{
		if (buffer.size() != 4 + 9 * 8 + 8 + 8)
			return false;
		const uint8_t* p = buffer.data();
		int32_t kind = 0;
		memcpy(&kind, p, 4); p += 4;
		outCamera->Kind = static_cast<BcfCameraKind>(kind);
		memcpy(outCamera->ViewPoint, p, 24); p += 24;
		memcpy(outCamera->Direction, p, 24); p += 24;
		memcpy(outCamera->UpVector, p, 24); p += 24;
		memcpy(&outCamera->FieldOfViewDegrees, p, 8); p += 8;
		memcpy(&outCamera->ViewToWorldScale, p, 8);
		return true;
	}

	// --- Cross-thread request/response handoff --------------------------------------------------
	// The callbacks pipe server runs on a background thread (blocking ConnectNamedPipe/ReadFile
	// calls are fine there), but every HostCallbacks function must run on ArchiCAD's main thread
	// (the same restriction every ACAPI_* call has, and now also DG::Browser::ExecuteJs). Helper
	// Process::PollCallbacks, invoked from a SetTimer callback on the main thread, is what actually
	// executes the request.

	struct PendingCallback
	{
		uint8_t requestType = 0;
		std::vector<uint8_t> requestPayload;
		uint8_t responseType = kRespNack;
		std::vector<uint8_t> responsePayload;
	};

	HANDLE g_requestReadyEvent = nullptr;
	HANDLE g_responseReadyEvent = nullptr;
	PendingCallback g_pendingCallback;
	std::atomic<bool> g_callbacksThreadRunning{ false };

	void CallbacksServerThreadProc()
	{
		std::wstring pipeName = CallbacksPipeName();

		while (g_callbacksThreadRunning) {
			HANDLE pipe = ::CreateNamedPipeW(
				pipeName.c_str(),
				PIPE_ACCESS_DUPLEX,
				PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
				PIPE_UNLIMITED_INSTANCES,
				64 * 1024, 64 * 1024, 0, nullptr);
			if (pipe == INVALID_HANDLE_VALUE)
				return;

			BOOL connected = ::ConnectNamedPipe(pipe, nullptr) ? TRUE : (::GetLastError() == ERROR_PIPE_CONNECTED);
			if (connected) {
				uint8_t requestType = 0;
				std::vector<uint8_t> requestPayload;
				if (ReadFrame(pipe, &requestType, &requestPayload)) {
					char buf[128];
					sprintf_s(buf, "CallbacksServerThreadProc - request received, type=0x%02X, payloadSize=%zu", requestType, requestPayload.size());
					LogDiagHelperProcess(buf);

					g_pendingCallback.requestType = requestType;
					g_pendingCallback.requestPayload = std::move(requestPayload);
					g_pendingCallback.responseType = kRespNack;
					g_pendingCallback.responsePayload.clear();

					::ResetEvent(g_responseReadyEvent);
					::SetEvent(g_requestReadyEvent);

					// 5s timeout for everything except kCbCaptureSnapshotPng: generous for a
					// UI-driven call, but bounded so a missed timer tick (e.g. ArchiCAD busy in a
					// modal dialog) can't hang this thread forever. kCbCaptureSnapshotPng needs its
					// own much longer budget - PollCallbacks' case for it calls
					// ACAPI_Rendering_PhotoRender (see AddOnMain.cpp), a real photorealistic render
					// to a temp file, not a cheap viewport grab - confirmed live (REDACTED-internal-ip,
					// 2026-08-16) to reliably exceed 5s on a real project (Snowdon Towers/hvac),
					// which made every "Create New Issue" snapshot step fail with exactly this
					// thread's disconnect-without-responding path, surfacing client-side as "Pipe
					// closed while reading a frame." even though the render itself was still
					// running, not stuck.
					DWORD timeoutMs = (g_pendingCallback.requestType == kCbCaptureSnapshotPng) ? 120000 : 5000;
					DWORD waitResult = ::WaitForSingleObject(g_responseReadyEvent, timeoutMs);
					if (waitResult == WAIT_OBJECT_0) {
						LogDiagHelperProcess("CallbacksServerThreadProc - response ready, writing back");
						if (WriteFrame(pipe, g_pendingCallback.responseType, g_pendingCallback.responsePayload.data(),
							static_cast<uint32_t>(g_pendingCallback.responsePayload.size()))) {
							// Real, documented Win32 named-pipe gotcha (and the actual, confirmed root
							// cause of an intermittent "Pipe closed while reading a frame" failure on
							// the client - REDACTED-internal-ip, 2026-08-12): DisconnectNamedPipe tears down
							// the server's end of the pipe immediately, and any data WriteFile just
							// queued that the client hasn't finished reading yet can be lost - the
							// client's ReadFile then sees a broken pipe instead of the response.
							// FlushFileBuffers on a server pipe handle blocks until the client has
							// actually read everything that was written, which is exactly the
							// synchronization needed here before disconnecting.
							::FlushFileBuffers(pipe);
						}
					} else {
						LogDiagHelperProcess("CallbacksServerThreadProc - TIMED OUT waiting for PollCallbacks to service this request");
					}
				} else {
					LogDiagHelperProcess("CallbacksServerThreadProc - ReadFrame(request) FAILED");
				}
			}

			::DisconnectNamedPipe(pipe);
			::CloseHandle(pipe);
		}
	}

	std::thread g_callbacksThread;
}

bool HelperProcess::Initialize(const HostCallbacks* callbacks)
{
	m_callbacks = callbacks;

	if (m_callbacksServerStarted)
		return true;

	g_requestReadyEvent = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
	g_responseReadyEvent = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
	if (g_requestReadyEvent == nullptr || g_responseReadyEvent == nullptr)
		return false;

	g_callbacksThreadRunning = true;
	g_callbacksThread = std::thread(CallbacksServerThreadProc);

	m_callbacksServerStarted = true;

	// Kick the helper off eagerly rather than waiting for the first bridge call, so its HTTP static
	// file server (see HttpPort) has a head start before BcfPalette's constructor calls
	// browser.LoadURL against it. Not required for correctness (LaunchHelperProcess also runs
	// lazily inside CallBinding on demand) - just shaves the likelihood of the very first palette
	// open hitting a connection-refused page before Helper.exe finishes starting.
	LaunchHelperProcess();

	return true;
}

void HelperProcess::Shutdown()
{
	if (!m_callbacksServerStarted)
		return;

	LogDiagHelperProcess("Shutdown - stopping callbacks server thread");
	g_callbacksThreadRunning = false;

	// CallbacksServerThreadProc is most likely blocked inside ConnectNamedPipe right now, waiting
	// for a connection that may never come - a plain flag flip alone would leave it stuck there
	// forever. Connecting a throwaway client to our own pipe is the standard, portable way to
	// unblock that wait: once ConnectNamedPipe returns (satisfied by this connection), the loop
	// re-checks g_callbacksThreadRunning, sees false, and exits cleanly.
	std::wstring pipeName = CallbacksPipeName();
	HANDLE dummyClient = ::CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
	if (dummyClient != INVALID_HANDLE_VALUE)
		::CloseHandle(dummyClient);

	if (g_callbacksThread.joinable())
		g_callbacksThread.join();

	m_callbacksServerStarted = false;
	LogDiagHelperProcess("Shutdown - callbacks server thread stopped");
}

int HelperProcess::HttpPort()
{
	// Deterministic, derived independently by both this Add-On and Helper.exe from the same
	// ArchiCAD PID (passed to Helper.exe via --archicad-pid) - no discovery round-trip needed
	// before the very first DG::Browser::LoadURL call.
	return 20000 + (static_cast<int>(::GetCurrentProcessId()) % 20000);
}

void HelperProcess::PollCallbacks()
{
	if (!m_callbacksServerStarted || m_callbacks == nullptr)
		return;

	if (::WaitForSingleObject(g_requestReadyEvent, 0) != WAIT_OBJECT_0)
		return;
	::ResetEvent(g_requestReadyEvent);

	{
		char buf[64];
		sprintf_s(buf, "PollCallbacks - servicing request type=0x%02X", g_pendingCallback.requestType);
		LogDiagHelperProcess(buf);
	}

	switch (g_pendingCallback.requestType) {
	case kCbGetCamera: {
		BcfCameraData camera{};
		if (m_callbacks->GetCamera != nullptr && m_callbacks->GetCamera(&camera)) {
			g_pendingCallback.responseType = kRespCameraData;
			g_pendingCallback.responsePayload = PackCamera(camera);
		} else {
			g_pendingCallback.responseType = kRespNoCamera;
			g_pendingCallback.responsePayload.clear();
		}
		break;
	}
	case kCbGetSelectionGuids: {
		g_pendingCallback.responseType = kRespGuidList;
		g_pendingCallback.responsePayload.clear();
		if (m_callbacks->GetSelectionGuids != nullptr) {
			int32_t required = m_callbacks->GetSelectionGuids(nullptr, 0);
			std::vector<BcfElementGuid> guids(required > 0 ? required : 0);
			int32_t count = required > 0 ? m_callbacks->GetSelectionGuids(guids.data(), required) : 0;

			std::vector<uint8_t>& out = g_pendingCallback.responsePayload;
			auto appendInt32 = [&out](int32_t value) {
				const uint8_t* p = reinterpret_cast<const uint8_t*>(&value);
				out.insert(out.end(), p, p + 4);
			};
			appendInt32(count);
			for (int32_t i = 0; i < count; ++i) {
				size_t charCount = 0;
				while (guids[i][charCount] != 0) ++charCount;
				appendInt32(static_cast<int32_t>(charCount));
				const uint8_t* p = reinterpret_cast<const uint8_t*>(guids[i]);
				out.insert(out.end(), p, p + charCount * sizeof(char16_t));
			}
		}
		break;
	}
	case kCbCaptureSnapshotPng: {
		uint8_t* buffer = nullptr;
		int32_t length = 0;
		if (m_callbacks->CaptureSnapshotPng != nullptr && m_callbacks->CaptureSnapshotPng(&buffer, &length)) {
			g_pendingCallback.responseType = kRespSnapshotData;
			g_pendingCallback.responsePayload.assign(buffer, buffer + length);
			if (m_callbacks->FreeSnapshotBuffer != nullptr)
				m_callbacks->FreeSnapshotBuffer(buffer);
		} else {
			g_pendingCallback.responseType = kRespNoSnapshot;
			g_pendingCallback.responsePayload.clear();
		}
		break;
	}
	case kCbApplyCamera: {
		BcfCameraData camera{};
		bool ok = UnpackCamera(g_pendingCallback.requestPayload, &camera)
			&& m_callbacks->ApplyCamera != nullptr && m_callbacks->ApplyCamera(&camera);
		g_pendingCallback.responseType = ok ? kRespAck : kRespNack;
		g_pendingCallback.responsePayload.clear();
		break;
	}
	case kCbApplySelection: {
		const uint8_t* p = g_pendingCallback.requestPayload.data();
		const uint8_t* end = p + g_pendingCallback.requestPayload.size();
		int32_t count = 0;
		if (p + 4 <= end) { memcpy(&count, p, 4); p += 4; }

		std::vector<std::u16string> owned(count);
		std::vector<BcfElementGuid> guids(count);
		for (int32_t i = 0; i < count && p + 4 <= end; ++i) {
			int32_t charCount = 0;
			memcpy(&charCount, p, 4); p += 4;
			owned[i].assign(reinterpret_cast<const char16_t*>(p), charCount);
			p += charCount * sizeof(char16_t);
			guids[i] = owned[i].c_str();
		}

		if (m_callbacks->ApplySelection != nullptr)
			m_callbacks->ApplySelection(guids.data(), count);
		g_pendingCallback.responseType = kRespAck;
		g_pendingCallback.responsePayload.clear();
		break;
	}
	case kCbGetActiveProjectName: {
		char16_t nameBuffer[260]{};
		if (m_callbacks->GetActiveProjectName != nullptr)
			m_callbacks->GetActiveProjectName(nameBuffer, 260);
		size_t charCount = 0;
		while (nameBuffer[charCount] != 0) ++charCount;
		g_pendingCallback.responseType = kRespProjectName;
		const uint8_t* p = reinterpret_cast<const uint8_t*>(nameBuffer);
		g_pendingCallback.responsePayload.assign(p, p + charCount * sizeof(char16_t));
		break;
	}
	case kCbExecuteJs: {
		std::wstring script = BytesToWString(g_pendingCallback.requestPayload.data(), g_pendingCallback.requestPayload.size());
		if (m_callbacks->ExecuteJs != nullptr)
			m_callbacks->ExecuteJs(reinterpret_cast<const char16_t*>(script.c_str()));
		g_pendingCallback.responseType = kRespAck;
		g_pendingCallback.responsePayload.clear();
		break;
	}
	default:
		g_pendingCallback.responseType = kRespNack;
		g_pendingCallback.responsePayload.clear();
		break;
	}

	::SetEvent(g_responseReadyEvent);
}

bool HelperProcess::ConnectToBridgePipe(void** outPipeHandle)
{
	std::wstring pipeName = BridgePipeName();
	// FILE_FLAG_OVERLAPPED - required for WriteFramePumped/ReadFramePumped's overlapped I/O (see
	// this class's header comment on why a plain blocking read here would deadlock ArchiCAD).
	HANDLE pipe = ::CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
	if (pipe == INVALID_HANDLE_VALUE)
		return false;

	DWORD mode = PIPE_READMODE_BYTE;
	::SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);
	*outPipeHandle = pipe;
	return true;
}

namespace
{
	// Shared by WriteFramePumped/ReadFramePumped - issues an overlapped I/O request and pumps
	// pollCallbacks on every short-timeout wait until it completes, fails, or the pipe closes.
	bool OverlappedIoExact(HANDLE pipe, void* buffer, DWORD size, bool isWrite, const std::function<void()>& pollCallbacks)
	{
		DWORD totalDone = 0;
		while (totalDone < size) {
			OVERLAPPED overlapped{};
			overlapped.hEvent = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
			if (overlapped.hEvent == nullptr)
				return false;

			uint8_t* cursor = static_cast<uint8_t*>(buffer) + totalDone;
			DWORD remaining = size - totalDone;
			BOOL immediate = isWrite
				? ::WriteFile(pipe, cursor, remaining, nullptr, &overlapped)
				: ::ReadFile(pipe, cursor, remaining, nullptr, &overlapped);

			if (!immediate && ::GetLastError() != ERROR_IO_PENDING) {
				::CloseHandle(overlapped.hEvent);
				return false;
			}

			DWORD bytesDone = 0;
			bool succeeded = false;
			for (;;) {
				DWORD waitResult = ::WaitForSingleObject(overlapped.hEvent, 10);
				if (waitResult == WAIT_OBJECT_0) {
					succeeded = ::GetOverlappedResult(pipe, &overlapped, &bytesDone, FALSE) != FALSE;
					break;
				}
				if (waitResult != WAIT_TIMEOUT) {
					break;
				}

				// Real, confirmed-live finding (REDACTED-internal-ip, 2026-08-12): a call like Connect can
				// legitimately block for as long as the user takes to respond to a project-pick
				// prompt (see BcfSessionBinding.PickProjectAsync) - pollCallbacks() alone kept
				// ArchiCAD's ACAPI callbacks serviced during that wait, but never gave ArchiCAD's own
				// window a chance to repaint or process other input, so the whole app appeared to
				// hang with a spinning-wheel cursor for as long as this loop ran. Pumping the message
				// queue here too - the same technique modal dialogs themselves rely on for a nested
				// wait - keeps ArchiCAD genuinely responsive for the whole, potentially long, wait.
				MSG msg;
				while (::PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
					::TranslateMessage(&msg);
					::DispatchMessageW(&msg);
				}
				pollCallbacks();
			}

			::CloseHandle(overlapped.hEvent);
			if (!succeeded || bytesDone == 0)
				return false;

			totalDone += bytesDone;
		}
		return true;
	}
}

bool HelperProcess::WriteFramePumped(void* pipe, uint8_t messageType, const uint8_t* payload, uint32_t payloadSize)
{
	auto pump = [this]() { PollCallbacks(); };
	HANDLE handle = static_cast<HANDLE>(pipe);

	if (!OverlappedIoExact(handle, &payloadSize, sizeof(payloadSize), true, pump))
		return false;
	if (!OverlappedIoExact(handle, &messageType, sizeof(messageType), true, pump))
		return false;
	if (payloadSize > 0 && !OverlappedIoExact(handle, const_cast<uint8_t*>(payload), payloadSize, true, pump))
		return false;
	return true;
}

bool HelperProcess::ReadFramePumped(void* pipe, uint8_t* outMessageType, std::vector<uint8_t>* outPayload)
{
	auto pump = [this]() { PollCallbacks(); };
	HANDLE handle = static_cast<HANDLE>(pipe);

	uint32_t payloadSize = 0;
	if (!OverlappedIoExact(handle, &payloadSize, sizeof(payloadSize), false, pump))
		return false;
	if (!OverlappedIoExact(handle, outMessageType, sizeof(*outMessageType), false, pump))
		return false;
	outPayload->resize(payloadSize);
	if (payloadSize > 0 && !OverlappedIoExact(handle, outPayload->data(), payloadSize, false, pump))
		return false;
	return true;
}

bool HelperProcess::LaunchHelperProcess()
{
	IO::Location ownLoc;
	ACAPI_GetOwnLocation(&ownLoc);
	ownLoc.DeleteLastLocalName();
	GS::UniString folderPath = ownLoc.ToDisplayText();
	std::wstring addOnDirectory(reinterpret_cast<const wchar_t*>(folderPath.ToUStr().Get()));

	std::wstring exePath = addOnDirectory + L"\\OpenBcf.ArchiCad29.Helper.exe";
	std::wstring commandLine = L"\"" + exePath + L"\" --archicad-pid " + GetArchiCadPidPipeSuffix();

	STARTUPINFOW startupInfo{ sizeof(startupInfo) };
	PROCESS_INFORMATION processInfo{};
	// Mutable buffer required by CreateProcessW's lpCommandLine parameter.
	std::vector<wchar_t> commandLineBuffer(commandLine.begin(), commandLine.end());
	commandLineBuffer.push_back(L'\0');

	BOOL created = ::CreateProcessW(exePath.c_str(), commandLineBuffer.data(), nullptr, nullptr, FALSE,
		DETACHED_PROCESS, nullptr, addOnDirectory.c_str(), &startupInfo, &processInfo);
	if (!created)
		return false;

	::CloseHandle(processInfo.hThread);
	::CloseHandle(processInfo.hProcess);
	return true;
}

std::wstring HelperProcess::CallBinding(const std::wstring& bindingName, const std::wstring& methodName, const std::wstring& argsJson)
{
	HANDLE pipe = nullptr;
	bool connected = ConnectToBridgePipe(reinterpret_cast<void**>(&pipe));

	if (!connected) {
		if (!LaunchHelperProcess())
			return L"{\"isError\":true,\"message\":\"Failed to launch OpenBcf.ArchiCad29.Helper.exe\"}";

		// The just-launched helper needs a moment to start its pipe server - retry connecting for
		// up to ~5s rather than failing immediately. Safe to block here (unlike the earlier
		// foreign-HWND design this replaces) since nothing on the other end needs ArchiCAD's own
		// message loop to make progress - see this class's header comment.
		for (int attempt = 0; attempt < 50 && !connected; ++attempt) {
			::Sleep(100);
			connected = ConnectToBridgePipe(reinterpret_cast<void**>(&pipe));
		}
		if (!connected)
			return L"{\"isError\":true,\"message\":\"OpenBcf.ArchiCad29.Helper.exe did not respond in time\"}";
	}

	std::vector<uint8_t> payload;
	auto appendLengthPrefixed = [&payload](const std::wstring& text) {
		int32_t charCount = static_cast<int32_t>(text.size());
		const uint8_t* countBytes = reinterpret_cast<const uint8_t*>(&charCount);
		payload.insert(payload.end(), countBytes, countBytes + 4);
		AppendWString(&payload, text);
	};
	appendLengthPrefixed(bindingName);
	appendLengthPrefixed(methodName);
	AppendWString(&payload, argsJson);

	{
		std::string logLine = "CallBinding - connected, binding=" + NarrowForLog(bindingName) + " method=" + NarrowForLog(methodName) + " args=" + NarrowForLog(argsJson);
		LogDiagHelperProcess(logLine.c_str());
	}

	std::wstring result = L"{\"isError\":true,\"message\":\"No response from OpenBcf.ArchiCad29.Helper.exe\"}";
	if (WriteFramePumped(pipe, kBridgeCall, payload.data(), static_cast<uint32_t>(payload.size()))) {
		LogDiagHelperProcess("CallBinding - WriteFramePumped OK, about to ReadFramePumped");
		uint8_t responseType = 0;
		std::vector<uint8_t> responsePayload;
		if (ReadFramePumped(pipe, &responseType, &responsePayload) && responseType == kBridgeResult) {
			result = BytesToWString(responsePayload.data(), responsePayload.size());
			LogDiagHelperProcess(("CallBinding - ReadFramePumped OK, result=" + NarrowForLog(result)).c_str());
		} else {
			LogDiagHelperProcess("CallBinding - ReadFramePumped FAILED or unexpected responseType");
		}
	} else {
		LogDiagHelperProcess("CallBinding - WriteFramePumped FAILED");
	}

	::CloseHandle(pipe);
	return result;
}
