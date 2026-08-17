using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OpenBcf.Core.Model;
using OpenBcf.Core.Protocol;
using OpenBcf.Core.Serialization;
using OpenBcf.Core.Sync;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.ArchiCad29.Helper.Bindings;

/// <summary>
/// Offline .bcfzip exchange - mirrors OpenBcf.Rhino8.Client.Bindings.BcfArchiveBinding, but uses
/// WPF's own <see cref="Microsoft.Win32.SaveFileDialog"/>/<see cref="Microsoft.Win32.OpenFileDialog"/>
/// instead of System.Windows.Forms', since this project only enables UseWPF (there is no WinForms
/// ElementHost/WpfElementHost bridging need here the way Tekla/Rhino have, so UseWindowsForms was
/// never turned on).
/// </summary>
public sealed class BcfArchiveBinding : IBinding
{
    public BcfArchiveBinding(IBrowserBridge parent)
    {
        Parent = parent;
    }

    public string Name => "bcfArchiveBinding";

    public IBrowserBridge Parent { get; }

    public async Task<object?> ExportToFile()
    {
        var (client, versionId, project) = RequireSession();

        var fileName = ShowDialogOnStaThread(() => new SaveFileDialog
        {
            Filter = "BCF archive (*.bcfzip)|*.bcfzip",
            FileName = SuggestFileName(project),
        });
        if (fileName is null)
            return null;

        var document = await BcfArchiveSync.ExportProjectAsync(client, versionId, project).ConfigureAwait(false);
        BcfArchive.Write(document, fileName);

        return new { path = fileName, topicCount = document.Topics.Count };
    }

    public async Task<object?> ImportFromFile()
    {
        var (client, versionId, project) = RequireSession();

        var fileName = ShowDialogOnStaThread(() => new OpenFileDialog { Filter = "BCF archive (*.bcfzip)|*.bcfzip" });
        if (fileName is null)
            return null;

        var document = BcfArchive.Read(fileName);
        await BcfArchiveSync.ImportDocumentAsync(client, versionId, project.ProjectId, document).ConfigureAwait(false);

        return new { path = fileName, topicCount = document.Topics.Count };
    }

    /// <summary>
    /// <see cref="FileDialog.ShowDialog()"/> wraps the Vista+ native <c>IFileDialog</c> COM API,
    /// which requires a genuine STA thread - unlike WinForms' file dialogs, it doesn't throw a
    /// friendly .NET exception when that's violated, it just hangs indefinitely with no visible
    /// window and no response (confirmed live, REDACTED-internal-ip, 2026-08-17: the bridge pipe request
    /// for ExportToFile never got a response at all - not even an error - and Alt+Tab showed no
    /// dialog anywhere). This process's own entry point is `[STAThread]` (see Program.cs), but that
    /// only applies to the thread that runs Main - every request here actually executes on
    /// BridgeServer's own plain `new Thread(AcceptLoop)`, which defaults to MTA. Running the dialog
    /// itself on a dedicated, freshly-created STA thread (joined synchronously before returning) is
    /// the standard fix for showing an STA-only COM dialog from code that otherwise runs on MTA.
    ///
    /// That alone fixed the hang but left a second, real problem (confirmed live, same session):
    /// the dialog opened behind ArchiCAD's own window instead of on top of it. This process is a
    /// background helper, not the foreground app, so Windows denies it SetForegroundWindow/
    /// activation rights for any window it creates - an unowned dialog just gets stacked wherever
    /// normal z-order puts it. The fix is an invisible owner window with WindowStyle=None (no
    /// caption/hit-testable button hidden behind it) and Topmost=true: Topmost is a plain z-order
    /// flag (SetWindowPos(HWND_TOPMOST)), not an activation request, so it works across processes
    /// without needing foreground rights - and a modal dialog is always stacked directly above its
    /// owner, so the file dialog inherits that topmost placement and actually becomes visible.
    /// </summary>
    private static string? ShowDialogOnStaThread(Func<FileDialog> createDialog)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            var owner = new Window
            {
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Width = 0,
                Height = 0,
                Left = -10000,
                Top = -10000,
                Topmost = true,
            };
            owner.Show();

            var dialog = createDialog();
            if (dialog.ShowDialog(owner) == true)
                result = dialog.FileName;

            owner.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string SuggestFileName(BcfProject project)
    {
        var baseName = project.Name is { Length: > 0 } name ? name : project.ProjectId;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalidChar, '_');

        return $"{baseName}.bcfzip";
    }

    private static (BcfServerClient Client, string VersionId, BcfProject Project) RequireSession()
    {
        var session = BcfSession.Current
            ?? throw new InvalidOperationException("Connect to a BCF server first.");
        var client = BcfSession.Client
            ?? throw new InvalidOperationException("Connect to a BCF server first.");

        return (client, session.VersionId, session.Project);
    }
}
