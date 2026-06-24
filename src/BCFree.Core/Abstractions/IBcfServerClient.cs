using BCFree.Core.Model;
using BCFree.Core.Model.Visualization;
using BCFree.Core.Protocol;

namespace BCFree.Core.Abstractions;

/// <summary>
/// Talks to a buildingSMART BCF REST API server. Implementations must work against any
/// compliant server, not just a specific deployment such as https://REDACTED-server.invalid.
/// </summary>
public interface IBcfServerClient
{
    Uri BaseUrl { get; }

    Task<IReadOnlyList<BcfServerVersion>> GetServerVersionsAsync(CancellationToken cancellationToken = default);

    Task<BcfServerAuthOptions> GetAuthOptionsAsync(string versionId, CancellationToken cancellationToken = default);

    Task<string> AuthenticateWithPasswordAsync(string versionId, string username, string password, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfProject>> GetProjectsAsync(string versionId, CancellationToken cancellationToken = default);

    Task<BcfProject> GetProjectAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<BcfProject> UpdateProjectAsync(string versionId, BcfProject project, CancellationToken cancellationToken = default);

    Task<BcfProjectExtensions> GetProjectExtensionsAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfTopic>> GetTopicsAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<BcfTopic> GetTopicAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<BcfTopic> CreateTopicAsync(string versionId, string projectId, BcfTopic topic, CancellationToken cancellationToken = default);

    Task<BcfTopic> UpdateTopicAsync(string versionId, string projectId, BcfTopic topic, CancellationToken cancellationToken = default);

    Task DeleteTopicAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<BcfBimSnippet?> GetBimSnippetAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task PutBimSnippetAsync(string versionId, string projectId, Guid topicGuid, BcfBimSnippet snippet, byte[]? content = null, string? fileName = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfProjectFileInformation>> GetProjectFilesInformationAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfFileReference>> GetFilesAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfFileReference>> PutFilesAsync(string versionId, string projectId, Guid topicGuid, IReadOnlyList<BcfFileReference> files, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfComment>> GetCommentsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<BcfComment> GetCommentAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default);

    Task<BcfComment> CreateCommentAsync(string versionId, string projectId, Guid topicGuid, BcfComment comment, CancellationToken cancellationToken = default);

    Task<BcfComment> UpdateCommentAsync(string versionId, string projectId, Guid topicGuid, BcfComment comment, CancellationToken cancellationToken = default);

    Task DeleteCommentAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfVisualizationInfo>> GetViewpointsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<BcfVisualizationInfo> GetViewpointAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task<BcfVisualizationInfo> CreateViewpointAsync(string versionId, string projectId, Guid topicGuid, BcfVisualizationInfo viewpoint, byte[]? snapshotPngBytes = null, CancellationToken cancellationToken = default);

    Task<byte[]> GetSnapshotAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task<byte[]> GetBitmapAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, string bitmapReference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfComponent>> GetSelectedComponentsAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfColoring>> GetColoredComponentsAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task<BcfComponentVisibility?> GetVisibilityAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task DeleteViewpointAsync(string versionId, string projectId, Guid topicGuid, Guid viewpointGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetRelatedTopicsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> PutRelatedTopicsAsync(string versionId, string projectId, Guid topicGuid, IReadOnlyList<Guid> relatedTopicGuids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfDocumentReference>> GetDocumentReferencesAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<BcfDocumentReference> CreateDocumentReferenceAsync(string versionId, string projectId, Guid topicGuid, BcfDocumentReference reference, CancellationToken cancellationToken = default);

    Task<BcfDocumentReference> UpdateDocumentReferenceAsync(string versionId, string projectId, Guid topicGuid, BcfDocumentReference reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfServerDocument>> GetDocumentsAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<BcfServerDocument> CreateDocumentAsync(string versionId, string projectId, string fileName, byte[] content, CancellationToken cancellationToken = default);

    Task<byte[]> GetDocumentAsync(string versionId, string projectId, Guid documentGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfEvent>> GetTopicEventsAsync(string versionId, string projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfEvent>> GetTopicEventsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfEvent>> GetCommentEventsAsync(string versionId, string projectId, Guid topicGuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BcfEvent>> GetCommentEventsAsync(string versionId, string projectId, Guid topicGuid, Guid commentGuid, CancellationToken cancellationToken = default);
}
