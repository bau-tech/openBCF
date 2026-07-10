namespace OpenBcf.Core.Model.Visualization;

public sealed record BcfComponent(
    string IfcGuid,
    string? OriginatingSystem = null,
    string? AuthoringToolId = null);
