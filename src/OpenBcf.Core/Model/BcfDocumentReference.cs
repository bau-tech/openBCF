namespace OpenBcf.Core.Model;

public sealed record BcfDocumentReference(
    Guid Guid,
    string? ReferencedDocument = null,
    string? Description = null,
    bool IsExternal = false,
    Uri? Url = null,
    Guid? DocumentGuid = null);
