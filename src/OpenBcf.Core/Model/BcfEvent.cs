namespace OpenBcf.Core.Model;

public sealed record BcfEvent(
    Guid TopicGuid,
    DateTimeOffset Date,
    string Author,
    string EventType,
    Guid? CommentGuid = null,
    string? Action = null);
