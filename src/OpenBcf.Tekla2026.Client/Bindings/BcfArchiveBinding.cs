using System.IO;
using System.Windows.Forms;
using OpenBcf.Core.Model;
using OpenBcf.Core.Protocol;
using OpenBcf.Core.Serialization;
using OpenBcf.Core.Sync;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.Tekla2026.Client.Bindings;

/// <summary>
/// Offline .bcfzip exchange: lets a user export the connected project's topics to a local file
/// (to hand to someone without server access) and import a .bcfzip received that way back into
/// the project. Both directions still go through the connected server (see
/// <see cref="BcfArchiveSync"/>) - this only adds the local file on either end of that round trip.
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

        var dialog = new SaveFileDialog
        {
            Filter = "BCF archive (*.bcfzip)|*.bcfzip",
            FileName = SuggestFileName(project),
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return null;

        var document = await BcfArchiveSync.ExportProjectAsync(client, versionId, project).ConfigureAwait(false);
        BcfArchive.Write(document, dialog.FileName);

        return new { path = dialog.FileName, topicCount = document.Topics.Count };
    }

    public async Task<object?> ImportFromFile()
    {
        var (client, versionId, project) = RequireSession();

        var dialog = new OpenFileDialog { Filter = "BCF archive (*.bcfzip)|*.bcfzip" };
        if (dialog.ShowDialog() != DialogResult.OK)
            return null;

        var document = BcfArchive.Read(dialog.FileName);
        await BcfArchiveSync.ImportDocumentAsync(client, versionId, project.ProjectId, document).ConfigureAwait(false);

        return new { path = dialog.FileName, topicCount = document.Topics.Count };
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
