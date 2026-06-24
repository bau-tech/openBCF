namespace BCFree.Core.Model;

public sealed record BcfProjectExtensions
{
    public IReadOnlyList<string> TopicTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TopicStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TopicLabels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SnippetTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Priorities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Users { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProjectActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TopicActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CommentActions { get; init; } = Array.Empty<string>();
}
