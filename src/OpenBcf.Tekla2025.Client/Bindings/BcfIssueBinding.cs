using OpenBcf.Core.Configuration;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;
// Aliased rather than `using Tekla.Structures.Model;` - that namespace also has a Task class,
// which collides with System.Threading.Tasks.Task used throughout this file.
using Model = Tekla.Structures.Model.Model;

namespace OpenBcf.Tekla2025.Client.Bindings;

/// <summary>
/// Drives topic/comment/viewpoint browsing and authoring against the project picked during
/// Connect (see <see cref="BcfSession"/>). Every method assumes a successful Connect already
/// happened - there is no implicit reconnect.
/// </summary>
public sealed class BcfIssueBinding : IBinding
{
    // Holds the camera/component state captured by CaptureCurrentViewpointSnapshot until the
    // frontend finishes annotating the snapshot and calls SaveViewpointSnapshot - the markup
    // editor only ever touches the 2D image, so there is nothing to re-derive from it.
    private (string TopicGuid, BcfVisualizationInfo Viewpoint)? _pendingCapture;

    public BcfIssueBinding(IBrowserBridge parent)
    {
        Parent = parent;
    }

    public string Name => "bcfIssueBinding";

    public IBrowserBridge Parent { get; }

    public async Task<object> GetExtensions()
    {
        var (client, versionId, project) = RequireSession();
        var extensions = await client.GetProjectExtensionsAsync(versionId, project.ProjectId).ConfigureAwait(false);

        return new
        {
            topicTypes = extensions.TopicTypes,
            topicStatuses = extensions.TopicStatuses,
            priorities = extensions.Priorities,
            users = extensions.Users,
            stages = extensions.Stages,
        };
    }

    public async Task<object[]> ListTopics()
    {
        var (client, versionId, project) = RequireSession();
        var topics = await client.GetTopicsAsync(versionId, project.ProjectId).ConfigureAwait(false);

        return topics.Select(ToListItem).ToArray<object>();
    }

    public async Task<object> GetTopic(string topicGuid)
    {
        var (client, versionId, project) = RequireSession();
        var guid = Guid.Parse(topicGuid);

        var topic = await client.GetTopicAsync(versionId, project.ProjectId, guid).ConfigureAwait(false);
        var comments = await client.GetCommentsAsync(versionId, project.ProjectId, guid).ConfigureAwait(false);
        var viewpoints = await client.GetViewpointsAsync(versionId, project.ProjectId, guid).ConfigureAwait(false);

        return new
        {
            guid = topic.Guid.ToString(),
            title = topic.Title,
            topicType = topic.TopicType,
            topicStatus = topic.TopicStatus,
            priority = topic.Priority,
            assignedTo = topic.AssignedTo,
            dueDate = topic.DueDate,
            description = topic.Description,
            creationDate = topic.CreationDate,
            creationAuthor = topic.CreationAuthor,
            comments = comments
                .OrderBy(c => c.Date)
                .Select(c => new
                {
                    guid = c.Guid.ToString(),
                    date = c.Date,
                    author = c.Author,
                    comment = c.Comment,
                }),
            viewpoints = viewpoints.Select(v => new { guid = v.Guid.ToString() }),
        };
    }

    public async Task<object> CreateTopic(
        string title,
        string? topicType,
        string? topicStatus,
        string? priority,
        string? description,
        string? assignedTo,
        DateTimeOffset? dueDate)
    {
        var (client, versionId, project) = RequireSession();

        var topic = new BcfTopic(
            Guid: Guid.NewGuid(),
            Title: title,
            TopicType: topicType,
            TopicStatus: topicStatus,
            Priority: priority,
            DueDate: dueDate,
            AssignedTo: assignedTo,
            Description: description,
            CreationAuthor: ResolveAuthor());

        var created = await client.CreateTopicAsync(versionId, project.ProjectId, topic).ConfigureAwait(false);
        return ToListItem(created);
    }

    public async Task<object> UpdateTopicStatus(string topicGuid, string topicStatus)
    {
        var (client, versionId, project) = RequireSession();
        var guid = Guid.Parse(topicGuid);

        var topic = await client.GetTopicAsync(versionId, project.ProjectId, guid).ConfigureAwait(false);
        var updated = await client.UpdateTopicAsync(versionId, project.ProjectId, topic with { TopicStatus = topicStatus }).ConfigureAwait(false);
        return ToListItem(updated);
    }

    public async Task<object> CreateComment(string topicGuid, string comment)
    {
        var (client, versionId, project) = RequireSession();
        var guid = Guid.Parse(topicGuid);

        var created = await client.CreateCommentAsync(versionId, project.ProjectId, guid, new BcfComment(Guid.NewGuid(), DateTimeOffset.UtcNow, ResolveAuthor(), comment)).ConfigureAwait(false);

        return new
        {
            guid = created.Guid.ToString(),
            date = created.Date,
            author = created.Author,
            comment = created.Comment,
        };
    }

    // Captures the camera/geometry state and a raw snapshot, but does not upload anything yet -
    // the frontend shows the snapshot in its markup editor first (see MarkupEditor.vue) and then
    // calls SaveViewpointSnapshot with the annotated PNG once the user confirms.
    public Task<string> CaptureCurrentViewpointSnapshot(string topicGuid)
    {
        if (!new Model().GetConnectionStatus())
            throw new InvalidOperationException("Open a Tekla Structures model before attaching a viewpoint.");

        var (viewpoint, snapshotPng) = BcfViewpointCapture.Capture();
        _pendingCapture = (topicGuid, viewpoint);

        return Task.FromResult($"data:image/png;base64,{Convert.ToBase64String(snapshotPng)}");
    }

    public async Task<object> SaveViewpointSnapshot(string topicGuid, string snapshotBase64)
    {
        var (client, versionId, project) = RequireSession();
        var guid = Guid.Parse(topicGuid);

        if (_pendingCapture is not { } pending || pending.TopicGuid != topicGuid)
            throw new InvalidOperationException("Capture a viewpoint before saving its snapshot.");

        var snapshotPng = Convert.FromBase64String(snapshotBase64);
        var created = await client.CreateViewpointAsync(versionId, project.ProjectId, guid, pending.Viewpoint, snapshotPng).ConfigureAwait(false);
        _pendingCapture = null;

        return new
        {
            guid = created.Guid.ToString(),
            snapshotDataUrl = $"data:image/png;base64,{snapshotBase64}",
        };
    }

    public async Task<string> GetSnapshotDataUrl(string topicGuid, string viewpointGuid)
    {
        var (client, versionId, project) = RequireSession();
        var bytes = await client.GetSnapshotAsync(versionId, project.ProjectId, Guid.Parse(topicGuid), Guid.Parse(viewpointGuid)).ConfigureAwait(false);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    public async Task ApplyViewpoint(string topicGuid, string viewpointGuid)
    {
        var (client, versionId, project) = RequireSession();
        var viewpoint = await client.GetViewpointAsync(versionId, project.ProjectId, Guid.Parse(topicGuid), Guid.Parse(viewpointGuid)).ConfigureAwait(false);
        BcfViewpointApply.Apply(viewpoint);
    }

    private static object ToListItem(BcfTopic topic) => new
    {
        guid = topic.Guid.ToString(),
        title = topic.Title,
        topicType = topic.TopicType,
        topicStatus = topic.TopicStatus,
        priority = topic.Priority,
        assignedTo = topic.AssignedTo,
        creationDate = topic.CreationDate,
        dueDate = topic.DueDate,
    };

    // bcf.chladny.de rejects topic creation/comments with a 422 if creation_author/author is
    // null - always send something rather than relying on the server to fill it in from auth.
    private static string ResolveAuthor() =>
        BcfSettings.Load().Username is { Length: > 0 } username ? username : Environment.UserName;

    private static (Core.Protocol.BcfServerClient Client, string VersionId, BcfProject Project) RequireSession()
    {
        var session = BcfSession.Current
            ?? throw new InvalidOperationException("Connect to a BCF server first.");
        var client = BcfSession.Client
            ?? throw new InvalidOperationException("Connect to a BCF server first.");

        return (client, session.VersionId, session.Project);
    }
}
