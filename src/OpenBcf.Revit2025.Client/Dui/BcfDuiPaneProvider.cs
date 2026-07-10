using System.IO;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using OpenBcf.Dui.Bridge;
using OpenBcf.Dui.WebView;
using OpenBcf.Revit2025.Client.Bindings;

namespace OpenBcf.Revit2025.Client.Dui;

/// <summary>
/// Hosts <see cref="BcfDuiWebView"/> (the shared control every host eventually embeds) in a real
/// Revit <see cref="DockablePane"/>, wired to <see cref="PingBinding"/> (debugging aid) and
/// <see cref="BcfSessionBinding"/> (real BCF server "Connect").
/// </summary>
public sealed class BcfDuiPaneProvider : IDockablePaneProvider
{
    public static readonly DockablePaneId PaneId = new(new Guid("a248980b-ee75-4642-80ae-56da14a982cc"));

    private static BcfDuiPaneProvider? s_instance;

    public static BcfDuiWebView? Control => s_instance?._control;

    // Revit calls SetupDockablePane synchronously during RegisterDockablePane (i.e. during
    // OnStartup), before its main window/message loop is fully up. Constructing BcfDuiWebView
    // there crashes Revit natively, since WebView2's HwndHost needs a real parent HWND. So this
    // placeholder is returned immediately, and the real control is built lazily via
    // EnsureControlCreated, the first time the user actually opens the pane (see
    // ShowBcfDuiPaneCommand).
    private readonly ContentControl _host = new();

    private BcfDuiWebView? _control;

    public BcfDuiPaneProvider()
    {
        s_instance = this;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = _host;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }

    public static void EnsureControlCreated() => s_instance?.EnsureControlCreatedCore();

    private void EnsureControlCreatedCore()
    {
        if (_control is not null)
        {
            return;
        }

        BcfDuiWebView? webView = null;
        var executor = new DeferredScriptExecutor(() => webView!);
        var pingBinding = new PingBinding(new BrowserBridge(executor));
        var sessionBinding = new BcfSessionBinding(new BrowserBridge(executor), RevitContext.Current);
        var issueBinding = new BcfIssueBinding(new BrowserBridge(executor), RevitContext.Current);
        var archiveBinding = new BcfArchiveBinding(new BrowserBridge(executor));

        var assemblyDir = Path.GetDirectoryName(typeof(BcfDuiPaneProvider).Assembly.Location)!;
        var distPath = Path.Combine(assemblyDir, "wwwroot");

        webView = new BcfDuiWebView([pingBinding, sessionBinding, issueBinding, archiveBinding], distPath);
        _control = webView;
        _host.Content = webView;
    }

    public static void RefreshVisibility() => s_instance?._control?.RefreshVisibility();
}
