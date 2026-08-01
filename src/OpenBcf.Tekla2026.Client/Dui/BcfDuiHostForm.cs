using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using OpenBcf.Dui.Bridge;
using OpenBcf.Dui.WebView;
using OpenBcf.Tekla2026.Client.Bindings;
using Tekla.Structures.Dialog;
using Tekla.Structures.Model.Operations;

namespace OpenBcf.Tekla2026.Client.Dui;

/// <summary>
/// Hosts <see cref="BcfDuiWebView"/> (the shared control every host eventually embeds) in a
/// Tekla plugin dialog (see <see cref="TeklaPlugin"/>) - the closest equivalent Tekla's Open API
/// offers to Revit's dockable pane. <see cref="PluginFormBase"/> only supports floating windows;
/// there is no public docking API. Rather than a blanket <c>TopMost</c> flag (which floats above
/// every other application on the desktop, not just Tekla), this window is made an owned child
/// of Tekla's main frame via <c>SetWindowLongPtr</c>/<c>GWL_HWNDPARENT</c> - the same technique
/// Speckle's Tekla connector (SpeckleTeklaPanelHost) uses - so it stays above Tekla specifically
/// and minimizes/restores together with it.
/// </summary>
public sealed class BcfDuiHostForm : PluginFormBase
{
    [DllImport("user32.dll", SetLastError = true)]
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    private const int GwlHwndParent = -8;

    private static BcfDuiHostForm? s_instance;

    // Tekla instantiates the PluginUserInterface form twice on its first invocation per session -
    // building the WebView on that first pass renders it at the wrong size, so nothing is built
    // until the second (confirmed necessary by Speckle's Tekla connector hitting the same quirk).
    private static bool s_isFirstInvocation = true;
    private static bool s_isInitialized;

    public BcfDuiHostForm()
    {
        if (s_isFirstInvocation)
        {
            s_isFirstInvocation = false;
            Close();
            return;
        }

        if (s_isInitialized)
        {
            s_instance?.BringToFront();
            Close();
            return;
        }

        s_isInitialized = true;
        s_instance = this;

        Text = "openBCF";
        // Wider than the old 420px default: the markup editor's canvas always fills its
        // container width (see MarkupEditor.vue), so a fixed-pixel stroke/font drawn onto the
        // ~1024px snapshot renders visibly smaller on screen in a narrow window than it does in
        // a Revit dockable pane the user has widened - this window has no such pane to inherit a
        // width from, so its own default needs to be generous. Still a plain resizable Form, so
        // the user can make it wider still.
        Width = 640;
        Height = 860;
        ShowIcon = false;

        BcfDuiWebView? webView = null;
        var executor = new DeferredScriptExecutor(() => webView!);
        var pingBinding = new PingBinding(new BrowserBridge(executor));
        var sessionBinding = new BcfSessionBinding(new BrowserBridge(executor));
        var issueBinding = new BcfIssueBinding(new BrowserBridge(executor));
        var archiveBinding = new BcfArchiveBinding(new BrowserBridge(executor));

        var assemblyDir = Path.GetDirectoryName(typeof(BcfDuiHostForm).Assembly.Location)!;
        var distPath = Path.Combine(assemblyDir, "wwwroot");

        webView = new BcfDuiWebView([pingBinding, sessionBinding, issueBinding, archiveBinding], distPath);

        var host = new ElementHost { Dock = DockStyle.Fill, Child = webView };
        Controls.Add(host);

        TopLevel = true;
        SetWindowLongPtr(Handle, GwlHwndParent, MainWindow.Frame.Handle);
        // Do not call Show()/Activate() here: Tekla's own plugin loader calls ShowDialog() on
        // this instance right after construction (that's how PluginFormBase dialogs are meant to
        // be displayed). Form.ShowDialog() starts with "if (Visible) throw new
        // InvalidOperationException(...)", so making the form visible ourselves first causes
        // Tekla's own display call to fail with "Laden des Plug-ins fehlgeschlagen" even though
        // our own Show() already made the window appear and work.
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
            s_isInitialized = false;
        }
    }
}
