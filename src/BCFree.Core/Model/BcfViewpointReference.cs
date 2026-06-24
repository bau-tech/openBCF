namespace BCFree.Core.Model;

public sealed record BcfViewpointReference(
    Guid Guid,
    string? ViewpointFile = null,
    string? SnapshotFile = null,
    int? Index = null);
