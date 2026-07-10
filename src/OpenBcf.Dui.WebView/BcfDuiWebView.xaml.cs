using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.Dui.WebView;

/// <summary>
/// The one control every host (Revit now, Tekla eventually) embeds. The compiled frontend (Vite
/// build output) is served from <paramref name="frontendDistPath"/> via a virtual host mapping
/// rather than a remote URL, since openBCF has no public hosting infrastructure - everything stays
/// local and offline-capable.
/// </summary>
public partial class BcfDuiWebView : UserControl, IBrowserScriptExecutor, IDisposable
{
    private const string VirtualHostName = "openbcf.local";

    // The first time the control becomes visible (e.g. right after a document opens), Chromium
    // can decide it's occluded/not-yet-visible and simply stops submitting frames - the surface
    // stays solid black until something flips WebView2's own visibility state, which is what
    // happens internally when a host calls CoreWebView2Controller.IsVisible. The Wpf wrapper
    // doesn't expose that controller publicly (confirmed via reflection against the installed
    // SDK - "CoreWebView2Controller" only exists on the internal WebView2Base type), so it's
    // reached here through the one non-public property that holds it.
    private static readonly PropertyInfo? s_controllerProperty = typeof(Microsoft.Web.WebView2.Wpf.WebView2)
        .BaseType?
        .GetProperty("CoreWebView2Controller", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly IReadOnlyList<IBinding> _bindings;
    private readonly string _frontendDistPath;

    public BcfDuiWebView(IEnumerable<IBinding> bindings, string frontendDistPath)
    {
        _bindings = bindings.ToList();
        _frontendDistPath = frontendDistPath;
        InitializeComponent();

        // Hosts like Revit's DockablePane don't reliably raise the WPF Loaded event the way a
        // normal top-level WPF window does, and the WebView2 control's *implicit* auto-init
        // (triggered by Loaded or by setting Source) depends on that. Triggering initialization
        // explicitly here means it starts regardless of whether this control ever sees Loaded.
        _ = InitializeCoreWebView2Async();
    }

    /// <summary>
    /// The pane can stay a solid black rectangle even once the page has loaded - observed
    /// specifically right after a document opens, when Revit may auto-restore a previously-open
    /// pane without the WPF control's own IsVisible ever toggling false-&gt;true (so there's no
    /// reliable WPF-level event to hook). What reliably fixes it is whatever Revit's own
    /// DockablePane.Show() does internally, which callers should pair with invoking this -
    /// once after the pane is shown/restored, and once a page load completes while already
    /// visible.
    /// </summary>
    public void RefreshVisibility()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        if (s_controllerProperty?.GetValue(Browser) is CoreWebView2Controller controller)
        {
            Dispatcher.BeginInvoke(() =>
            {
                controller.IsVisible = false;
                controller.IsVisible = true;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private async Task InitializeCoreWebView2Async()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenBcf",
                "WebView2");

            // Chromium's GPU compositor frequently renders as a solid black rectangle inside
            // Revit's DockablePane host when the session is running over RDP/VDI (a very common
            // setup for AEC firms), or when the host process's GPU adapter is virtualized/blocked.
            // Disabling GPU acceleration trades a little rendering performance for actually
            // showing the page; this is the standard mitigation Microsoft documents for WebView2
            // "black screen" reports.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-gpu --disable-gpu-compositing",
            };

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: options);

            await Browser.EnsureCoreWebView2Async(environment);

            // Tekla's WinForms/ElementHost hosting has been observed producing a WebView2 whose
            // effective zoom factor isn't exactly 1.0, unlike Revit's native WPF hosting - and
            // Chromium's canvas text rasterization is sensitive to the page's effective zoom in a
            // way plain vector path strokes (arcs/lines) aren't. That's what made the markup
            // editor's text labels render visibly smaller than its arrows/clouds despite both
            // being drawn onto the exact same canvas with the same pixel-based sizes, but only
            // inside Tekla. Forcing 1.0 explicitly - rather than trusting whatever each host
            // reports - and disabling the ctrl+scroll zoom gesture keeps every host's rendering
            // byte-for-byte comparable instead of silently drifting apart per host.
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            if (s_controllerProperty?.GetValue(Browser) is CoreWebView2Controller zoomController)
            {
                zoomController.ZoomFactor = 1.0;
            }

            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                _frontendDistPath,
                CoreWebView2HostResourceAccessKind.Allow);

            foreach (var binding in _bindings)
            {
                binding.Parent.AssociateWithBinding(binding);
                Browser.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
            }

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                Browser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

                if (!e.IsSuccess)
                {
                    ShowError($"Navigation failed: {e.WebErrorStatus}");
                    return;
                }

                // Covers the case where the page finishes loading while the pane is already
                // visible.
                RefreshVisibility();
            }

            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            Browser.Source = new Uri($"https://{VirtualHostName}/index.html");
        }
        catch (Exception ex)
        {
            ShowError($"WebView2 failed to initialize:\n{ex}\n\nfrontendDistPath = {_frontendDistPath}");
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = System.Windows.Visibility.Visible;
    }

    /// <inheritdoc/>
    public void ExecuteScript(string script)
    {
        // WebView2's own state (CoreWebView2 and everything under it) is thread-affine to the
        // thread that created it and throws if touched from any other thread - including just
        // reading CoreWebView2 to null-check it. Bindings can call this from a background thread
        // (e.g. after a ConfigureAwait(false) continuation), so the cross-thread check has to
        // come first, via Dispatcher.CheckAccess() (safe from any thread), before anything here
        // touches Browser.CoreWebView2 at all.
        if (!Browser.Dispatcher.CheckAccess())
        {
            Browser.Dispatcher.BeginInvoke(() => ExecuteScript(script));
            return;
        }

        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void ShowDevTools() => Browser.CoreWebView2?.OpenDevToolsWindow();

    public void Dispose() => Browser.Dispose();
}
