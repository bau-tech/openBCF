namespace OpenBcf.Core.Model;

public sealed record BcfServerDocument(
    Guid Guid,
    string FileName,
    DateTimeOffset? CreationDate = null,
    string? CreationAuthor = null,
    DateTimeOffset? ModifiedDate = null,
    string? ModifiedAuthor = null);
