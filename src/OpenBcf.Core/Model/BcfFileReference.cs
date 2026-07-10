namespace OpenBcf.Core.Model;

public sealed record BcfFileReference(
    string? IfcProject = null,
    string? IfcSpatialStructureElement = null,
    string? Filename = null,
    DateTimeOffset? Date = null,
    string? Reference = null,
    bool IsExternal = false);
