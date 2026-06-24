using BCFree.Core.Abstractions;
using BCFree.Core.Model;
using BCFree.Core.Protocol;

namespace BCFree.Core.Sync;

/// <summary>
/// Bridges the local .bcfzip archive format (<see cref="Serialization.BcfArchive"/>) with the
/// BCF REST API, so a project on the server can be exported to a file and a file can be
/// imported back into a project. Both directions go through the same write DTOs as normal
/// publishing (server assigns guid/dates/authors on import), and viewpoint guids get remapped
/// since the server issues new ones that won't match the ones recorded in the archive.
/// </summary>
public static class BcfArchiveSync
{
    public static async Task<BcfDocument> ExportProjectAsync(
        IBcfServerClient client, string versionId, BcfProject project, CancellationToken cancellationToken = default)
    {
        var topics = await client.GetTopicsAsync(versionId, project.ProjectId, cancellationToken);
        var topicFolders = new List<BcfTopicFolder>();

        foreach (var topic in topics)
        {
            var comments = await client.GetCommentsAsync(versionId, project.ProjectId, topic.Guid, cancellationToken);
            var viewpoints = await client.GetViewpointsAsync(versionId, project.ProjectId, topic.Guid, cancellationToken);

            var markup = new BcfMarkup(topic) { Comments = comments.ToList() };
            var folder = new BcfTopicFolder(markup);

            var index = 0;
            foreach (var viewpoint in viewpoints)
            {
                var viewpointFile = index == 0 ? "viewpoint.bcfv" : $"viewpoint_{index}.bcfv";
                string? snapshotFile = null;

                try
                {
                    var snapshotBytes = await client.GetSnapshotAsync(versionId, project.ProjectId, topic.Guid, viewpoint.Guid, cancellationToken);
                    snapshotFile = index == 0 ? "snapshot.png" : $"snapshot_{index}.png";
                    folder.Attachments[snapshotFile] = snapshotBytes;
                }
                catch
                {
                    // No snapshot for this viewpoint - not every viewpoint has one.
                }

                folder.Viewpoints[viewpointFile] = viewpoint;
                markup.Viewpoints.Add(new BcfViewpointReference(viewpoint.Guid, viewpointFile, snapshotFile, index));
                index++;
            }

            topicFolders.Add(folder);
        }

        return new BcfDocument(new BcfVersion(versionId)) { Project = project, Topics = topicFolders };
    }

    public static async Task ImportDocumentAsync(
        IBcfServerClient client, string versionId, string projectId, BcfDocument document, CancellationToken cancellationToken = default)
    {
        foreach (var topicFolder in document.Topics)
        {
            var createdTopic = await client.CreateTopicAsync(versionId, projectId, topicFolder.Markup.Topic, cancellationToken);

            var viewpointGuidMap = new Dictionary<Guid, Guid>();
            foreach (var viewpointEntry in topicFolder.Viewpoints)
            {
                var oldGuid = viewpointEntry.Value.Guid;
                var viewpointRef = topicFolder.Markup.Viewpoints.FirstOrDefault(v => v.Guid == oldGuid);

                byte[]? snapshotBytes = null;
                if (viewpointRef?.SnapshotFile is { Length: > 0 } snapshotFile)
                    topicFolder.Attachments.TryGetValue(snapshotFile, out snapshotBytes);

                var createdViewpoint = await client.CreateViewpointAsync(
                    versionId, projectId, createdTopic.Guid, viewpointEntry.Value, snapshotBytes, cancellationToken);

                viewpointGuidMap[oldGuid] = createdViewpoint.Guid;
            }

            foreach (var comment in topicFolder.Markup.Comments)
            {
                var remapped = comment.ViewpointGuid is { } oldGuid && viewpointGuidMap.TryGetValue(oldGuid, out var newGuid)
                    ? comment with { ViewpointGuid = newGuid }
                    : comment with { ViewpointGuid = null };

                await client.CreateCommentAsync(versionId, projectId, createdTopic.Guid, remapped, cancellationToken);
            }
        }
    }
}
