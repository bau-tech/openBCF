namespace BCFree.Core.Model;

public sealed record BcfComment(
    Guid Guid,
    DateTimeOffset Date,
    string Author,
    string Comment,
    Guid? ViewpointGuid = null,
    DateTimeOffset? ModifiedDate = null,
    string? ModifiedAuthor = null);
