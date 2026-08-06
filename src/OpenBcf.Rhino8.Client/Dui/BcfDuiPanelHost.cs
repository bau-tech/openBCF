using System.IO;
using OpenBcf.Dui.Bridge;
using OpenBcf.Dui.WebView;
using OpenBcf.Rhino8.Client.Bindings;

namespace OpenBcf.Rhino8.Client.Dui;

/// <summary>
/// Hosts <see cref="BcfDuiWebView"/> (the shared control every host eventually embeds) inside a
/// real Rhino panel. Rhino's panel framework requires the registered type to implement
/// <see cref="System.Windows.Forms.IWin32Window"/> - a plain WPF <c>FrameworkElement</c> like
/// <see cref="BcfDuiWebView"/> doesn't do that on its own, so this derives from
/// <c>RhinoWindows.Controls.WpfElementHost</c>, the official bridge class for exactly this case
/// (confirmed against McNeel's own SampleCsWpfPanel: https://github.com/Zhangxiaomin/SampleCsWpfPanel).
/// Panel classes must have a public parameterless constructor and a <see cref="GuidAttribute"/>
/// with a stable, unique ID - both required by <c>Rhino.UI.Panels.RegisterPanel</c>.
/// </summary>
[System.Runtime.InteropServices.Guid("6C6B6E1C-2B2E-4C9E-9C1A-6B6A6C1E7A02")]
public sealed class BcfDuiPanelHost : RhinoWindows.Controls.WpfElementHost
{
    public BcfDuiPanelHost() : base(CreateWebView(), null)
    {
    }

    private static BcfDuiWebView CreateWebView()
    {
        BcfDuiWebView? webView = null;
        var executor = new DeferredScriptExecutor(() => webView!);
        var pingBinding = new PingBinding(new BrowserBridge(executor));
        var sessionBinding = new BcfSessionBinding(new BrowserBridge(executor));
        var issueBinding = new BcfIssueBinding(new BrowserBridge(executor));
        var archiveBinding = new BcfArchiveBinding(new BrowserBridge(executor));

        var assemblyDir = Path.GetDirectoryName(typeof(BcfDuiPanelHost).Assembly.Location)!;
        var distPath = Path.Combine(assemblyDir, "wwwroot");

        webView = new BcfDuiWebView([pingBinding, sessionBinding, issueBinding, archiveBinding], distPath);
        return webView;
    }
}
