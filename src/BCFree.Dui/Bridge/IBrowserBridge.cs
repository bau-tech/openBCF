using BCFree.Dui.Bindings;

namespace BCFree.Dui.Bridge;

public interface IBrowserBridge
{
    string FrontendBoundName { get; }

    void AssociateWithBinding(IBinding binding);

    /// <summary>
    /// Called from JS: <c>chrome.webview.hostObjects.sync.&lt;bindingName&gt;.RunMethod(...)</c>.
    /// Looks up <paramref name="methodName"/> on the bound <see cref="IBinding"/> by reflection,
    /// invokes it with the JSON-decoded <paramref name="argsJson"/> array, and - once the (possibly
    /// async) result is ready - delivers it back into the page via <see cref="IBrowserScriptExecutor"/>,
    /// keyed by <paramref name="requestId"/> so the frontend can resolve the right pending promise.
    /// </summary>
    void RunMethod(string methodName, string requestId, string argsJson);

    /// <summary>
    /// Proactively pushes an event into the page (e.g. "session changed") rather than responding
    /// to a JS-initiated call. Delivered to <c>window.__bcfreeDuiReceiveEvent</c>.
    /// </summary>
    void Send(string eventName, object? payload);
}
