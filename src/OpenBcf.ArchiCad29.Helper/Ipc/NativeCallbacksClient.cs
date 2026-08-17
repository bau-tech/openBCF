using System.IO;
using System.IO.Pipes;
using System.Text;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Calls back into the native Add-On for everything ACAPI-specific (camera, selection, snapshot,
/// active project name) - this process has no way to call ACAPI itself (see
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/Interop.h), so every call here connects fresh as a
/// client to the native side's callbacks pipe (native is the server - see HelperProcess.h's header
/// comment for why the two pipes have opposite client/server roles). Replaces the old
/// OpenBcf.ArchiCad29.NativeClient's HostContext.Callbacks function-pointer table entirely - a
/// plain named-pipe round trip instead of an in-process delegate* call, since this process is no
/// longer hosted inside ArchiCAD's own address space.
/// </summary>
internal static class NativeCallbacksClient
{
    public static int ArchiCadPid { get; set; }

    private static string PipeName => $"openbcf-{ArchiCadPid}-cb";

    public static BcfCamera? TryGetCamera()
    {
        var (type, payload) = SendRequest(MessageTypes.CbGetCamera, ReadOnlySpan<byte>.Empty);
        return type == MessageTypes.RespCameraData ? CameraWire.Unpack(payload) : null;
    }

    public static List<string> GetSelectionGuids()
    {
        var (type, payload) = SendRequest(MessageTypes.CbGetSelectionGuids, ReadOnlySpan<byte>.Empty);
        return type == MessageTypes.RespGuidList ? GuidListWire.Unpack(payload) : [];
    }

    public static byte[] CaptureSnapshotPng()
    {
        var (type, payload) = SendRequest(MessageTypes.CbCaptureSnapshotPng, ReadOnlySpan<byte>.Empty);
        if (type != MessageTypes.RespSnapshotData)
            throw new InvalidOperationException("Open a 3D window in ArchiCAD before attaching a viewpoint.");

        return payload;
    }

    public static bool ApplyCamera(BcfCamera camera)
    {
        var (type, _) = SendRequest(MessageTypes.CbApplyCamera, CameraWire.Pack(camera));
        return type == MessageTypes.RespAck;
    }

    public static void ApplySelection(IReadOnlyList<string> guids) =>
        SendRequest(MessageTypes.CbApplySelection, GuidListWire.Pack(guids));

    public static string GetActiveProjectName()
    {
        var (type, payload) = SendRequest(MessageTypes.CbGetActiveProjectName, ReadOnlySpan<byte>.Empty);
        var name = type == MessageTypes.RespProjectName ? Encoding.Unicode.GetString(payload) : string.Empty;
        return string.IsNullOrEmpty(name) ? "Untitled" : name;
    }

    /// <summary>
    /// Asks the native Add-On to run script in the active BcfPalette's DG::Browser, if one exists -
    /// this is how <see cref="Bindings.BcfSessionBinding"/>'s proactive push events
    /// (window.__openbcfDuiReceiveEvent, delivered via BrowserBridge.Send) reach the page, since
    /// only native code can touch the DG::Browser instance.
    /// </summary>
    public static void ExecuteJs(string script) =>
        SendRequest(MessageTypes.CbExecuteJs, Encoding.Unicode.GetBytes(script));

    // DIAGNOSTIC ONLY - see NativeScriptExecutor's matching pattern; a plain file logger since this
    // process has no console attached when launched by CreateProcessW.
    private static void LogDiag(string message)
    {
        try
        {
            // Same hardcoded, outside-Program-Files location the native side's diag.log uses (see
            // BcfPalette.cpp/AddOnMain.cpp/HelperProcess.cpp's matching LogDiag helpers) - simple
            // and reliably inspectable over SSH, no relative-path guessing about where this exe
            // actually runs from.
            File.AppendAllText(@"C:\openBCF-build\diag_helper.log", $"[{DateTime.Now:O}] [NativeCallbacksClient] {message}\r\n");
        }
        catch
        {
            // Logging itself must never throw.
        }
    }

    private static (byte Type, byte[] Payload) SendRequest(byte requestType, ReadOnlySpan<byte> payload)
    {
        LogDiag($"SendRequest type=0x{requestType:X2} - connecting to pipe '{PipeName}'");
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(5000);
        }
        catch (Exception ex)
        {
            LogDiag($"SendRequest type=0x{requestType:X2} - Connect FAILED: {ex}");
            throw;
        }

        LogDiag($"SendRequest type=0x{requestType:X2} - connected, writing frame (payload {payload.Length} bytes)");
        try
        {
            PipeFraming.WriteFrame(pipe, requestType, payload);
        }
        catch (Exception ex)
        {
            LogDiag($"SendRequest type=0x{requestType:X2} - WriteFrame FAILED: {ex}");
            throw;
        }

        LogDiag($"SendRequest type=0x{requestType:X2} - wrote frame, reading response");
        try
        {
            var result = PipeFraming.ReadFrame(pipe);
            LogDiag($"SendRequest type=0x{requestType:X2} - got response type=0x{result.MessageType:X2}, payload {result.Payload.Length} bytes");
            return result;
        }
        catch (Exception ex)
        {
            LogDiag($"SendRequest type=0x{requestType:X2} - ReadFrame FAILED: {ex}");
            throw;
        }
    }
}
