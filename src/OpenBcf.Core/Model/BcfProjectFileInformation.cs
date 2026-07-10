namespace OpenBcf.Core.Model;

public sealed record BcfProjectFileInformation(BcfFileReference File)
{
    public IReadOnlyList<BcfDisplayInformation> DisplayInformation { get; init; } = Array.Empty<BcfDisplayInformation>();
}

public sealed record BcfDisplayInformation(string FieldDisplayName, string FieldValue);
