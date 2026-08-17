using OpenBcf.Dui.Bridge;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// The <see cref="IBrowserScriptExecutor"/> every binding's <see cref="BrowserBridge"/> uses here -
/// forwards script text to the native Add-On (over the callbacks pipe, via
/// <see cref="NativeCallbacksClient.ExecuteJs"/>) instead of calling into an in-process WebView2
/// control the way every other client's host does, since this process owns no browser/window of
/// its own (see ../../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.h). Only
/// <see cref="BrowserBridge.Send"/> ever calls this - the actual method-call/result round trip for
/// JS::Function calls is handled entirely by BridgeDispatcher, bypassing BrowserBridge.RunMethod's
/// WebView2-specific fire-and-forget delivery pattern, since ACAPI's RegisterAsynchJSObject already
/// gives a real native Promise with the result delivered synchronously - see BridgeServer.
/// </summary>
internal sealed class NativeScriptExecutor : IBrowserScriptExecutor
{
    public void ExecuteScript(string script) => NativeCallbacksClient.ExecuteJs(script);
}
