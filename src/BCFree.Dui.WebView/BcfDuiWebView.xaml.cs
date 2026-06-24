using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using BCFree.Dui.Bindings;
using BCFree.Dui.Bridge;

namespace BCFree.Dui.WebView;

/// <summary>
/// The one control every host (Revit now, Tekla eventually) embeds. The compiled frontend (Vite
/// build output) is served from <paramref name="frontendDistPath"/> via a virtual host mapping
/// rather than a remote URL, since BCFree has no public hosting infrastructure - everything stays
/// local and offline-capable.
/// </summary>
public partial class BcfDuiWebView : UserControl, IBrowserScriptExecutor, IDisposable
{
    private const string VirtualHostName = "bcfree.local";

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

    private async Task InitializeCoreWebView2Async()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BCFree",
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

            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                _frontendDistPath,
                CoreWebView2HostResourceAccessKind.Allow);

            foreach (var binding in _bindings)
            {
                binding.Parent.AssociateWithBinding(binding);
                Browser.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
            }

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
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        if (!Browser.Dispatcher.CheckAccess())
        {
            Browser.Dispatcher.BeginInvoke(() => Browser.CoreWebView2.ExecuteScriptAsync(script));
            return;
        }

        Browser.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void ShowDevTools() => Browser.CoreWebView2?.OpenDevToolsWindow();

    public void Dispose() => Browser.Dispose();
}
