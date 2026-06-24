namespace BCFree.Dui.Bridge;

/// <summary>
/// Implemented by the host WPF control (<c>BcfDuiWebView</c>) so <see cref="BrowserBridge"/> can push
/// results/events into the page without depending on WebView2 types directly.
/// </summary>
public interface IBrowserScriptExecutor
{
    void ExecuteScript(string script);
}
