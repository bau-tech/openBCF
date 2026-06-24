using System.IO;
using Autodesk.Revit.UI;
using BCFree.Dui.Bridge;
using BCFree.Dui.WebView;
using BCFree.Revit2025.Client.Bindings;

namespace BCFree.Revit2025.Client.Dui;

/// <summary>
/// Hosts <see cref="BcfDuiWebView"/> (the shared control every host eventually embeds) in a real
/// Revit <see cref="DockablePane"/>, wired to <see cref="PingBinding"/> for now.
/// </summary>
public sealed class BcfDuiPaneProvider : IDockablePaneProvider
{
    public static readonly DockablePaneId PaneId = new(new Guid("a248980b-ee75-4642-80ae-56da14a982cc"));

    private static BcfDuiPaneProvider? s_instance;

    public static BcfDuiWebView? Control => s_instance?._control;

    private BcfDuiWebView? _control;

    public BcfDuiPaneProvider()
    {
        s_instance = this;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = GetOrCreateControl();
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }

    private BcfDuiWebView GetOrCreateControl()
    {
        if (_control is not null)
        {
            return _control;
        }

        BcfDuiWebView? webView = null;
        var executor = new DeferredScriptExecutor(() => webView!);
        var bridge = new BrowserBridge(executor);
        var pingBinding = new PingBinding(bridge);

        var assemblyDir = Path.GetDirectoryName(typeof(BcfDuiPaneProvider).Assembly.Location)!;
        var distPath = Path.Combine(assemblyDir, "wwwroot");

        webView = new BcfDuiWebView([pingBinding], distPath);
        _control = webView;
        return webView;
    }
}
