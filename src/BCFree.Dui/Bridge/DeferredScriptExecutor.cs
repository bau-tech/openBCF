namespace BCFree.Dui.Bridge;

/// <summary>
/// Breaks the construction cycle between a host control (e.g. <c>BcfDuiWebView</c>, which needs the
/// full binding list up front) and its bindings' <see cref="BrowserBridge"/>s (which need an
/// <see cref="IBrowserScriptExecutor"/> - the control itself - to deliver results). Construct
/// bindings/bridges with a <see cref="DeferredScriptExecutor"/> first, then resolve it once the host
/// control exists; by the time <see cref="ExecuteScript"/> actually runs, the real executor is set.
/// </summary>
public sealed class DeferredScriptExecutor : IBrowserScriptExecutor
{
    private readonly Func<IBrowserScriptExecutor> _resolve;

    public DeferredScriptExecutor(Func<IBrowserScriptExecutor> resolve)
    {
        _resolve = resolve;
    }

    public void ExecuteScript(string script) => _resolve().ExecuteScript(script);
}
