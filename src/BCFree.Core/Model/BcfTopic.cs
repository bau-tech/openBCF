namespace BCFree.Core.Model;

public sealed record BcfTopic(
    Guid Guid,
    string Title,
    string? TopicType = null,
    string? TopicStatus = null,
    string? Priority = null,
    int? Index = null,
    DateTimeOffset? CreationDate = null,
    string? CreationAuthor = null,
    DateTimeOffset? ModifiedDate = null,
    string? ModifiedAuthor = null,
    DateTimeOffset? DueDate = null,
    string? AssignedTo = null,
    string? Stage = null,
    string? Description = null,
    BcfBimSnippet? BimSnippet = null)
{
    public IList<string> Labels { get; init; } = new List<string>();
    public IList<string> ReferenceLinks { get; init; } = new List<string>();
    public IList<BcfDocumentReference> DocumentReferences { get; init; } = new List<BcfDocumentReference>();
    public IList<Guid> RelatedTopics { get; init; } = new List<Guid>();
}
