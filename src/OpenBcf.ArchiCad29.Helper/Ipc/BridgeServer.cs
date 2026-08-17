using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenBcf.Dui.Bindings;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Serves the bridge pipe - this process is the server here (the opposite role from
/// <see cref="NativeCallbacksClient"/>) since it must be discoverable by a native Add-On DLL that
/// gets unloaded/reloaded and has no memory of anything between cycles (see
/// ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.h). Each request carries one JS::Function
/// call - {bindingName, methodName, argsJson} - one registered JS::Function per binding *method*
/// (not one shared "RunMethod" per binding) on the native side, since a real, confirmed-live ACAPI
/// limitation means a second call into the same registered JS::Function cannot get through while an
/// earlier call to it is still pending (e.g. Connect blocking on a project pick while
/// ResolveProjectPick tries to answer it - see BcfPalette.cpp's kBindingMethods comment). Accepts
/// connections in a loop, handing each off to a background Task so a slow binding call (e.g. a
/// network request) never blocks other concurrent calls.
/// </summary>
internal sealed class BridgeServer
{
    private readonly int _archiCadPid;
    private readonly IReadOnlyDictionary<string, IBinding> _bindingsByName;

    public BridgeServer(int archiCadPid, IReadOnlyList<IBinding> bindings)
    {
        _archiCadPid = archiCadPid;
        _bindingsByName = bindings.ToDictionary(b => b.Name);
    }

    public void Start()
    {
        var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "openBCF bridge pipe server" };
        thread.Start();
    }

    private void AcceptLoop()
    {
        var pipeName = $"openbcf-{_archiCadPid}-bridge";
        while (true)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);
                pipe.WaitForConnection();
            }
            catch
            {
                continue;
            }

            _ = HandleConnectionAsync(pipe);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        try
        {
            var (type, payload) = PipeFraming.ReadFrame(pipe);
            if (type != MessageTypes.BridgeCall)
            {
                return;
            }

            var (bindingName, methodName, argsJson) = DecodeBridgeCall(payload);
            var resultJson = _bindingsByName.TryGetValue(bindingName, out var binding)
                ? await BridgeDispatcher.InvokeAsync(binding, methodName, argsJson).ConfigureAwait(false)
                : JsonSerializer.Serialize(new { isError = true, message = $"Unknown binding '{bindingName}'." });

            PipeFraming.WriteFrame(pipe, MessageTypes.BridgeResult, Encoding.Unicode.GetBytes(resultJson));

            // Real Win32 named-pipe gotcha (confirmed live on the matching native-side callbacks
            // pipe, REDACTED-internal-ip, 2026-08-12 - see HelperProcess.cpp's CallbacksServerThreadProc
            // for the full explanation): disposing this server pipe right after WriteFrame can race
            // ahead of the client actually reading that data, making the client see a broken pipe
            // instead of the response. WaitForPipeDrain is PipeStream's equivalent of Win32's
            // FlushFileBuffers for a server pipe handle - it blocks until the client has read
            // everything written above.
            pipe.WaitForPipeDrain();
        }
        catch
        {
            // A client (the native Add-On) disconnecting mid-request, or any other transient pipe
            // error, must not take this server down - the accept loop keeps running regardless.
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private static (string BindingName, string MethodName, string ArgsJson) DecodeBridgeCall(byte[] payload)
    {
        var offset = 0;
        string ReadLengthPrefixedString()
        {
            var charCount = BitConverter.ToInt32(payload, offset);
            offset += 4;
            var byteCount = charCount * sizeof(char);
            var text = Encoding.Unicode.GetString(payload, offset, byteCount);
            offset += byteCount;
            return text;
        }

        var bindingName = ReadLengthPrefixedString();
        var methodName = ReadLengthPrefixedString();
        var argsJson = Encoding.Unicode.GetString(payload, offset, payload.Length - offset);
        return (bindingName, methodName, argsJson);
    }
}
