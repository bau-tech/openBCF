namespace OpenBcf.Core.Model;

/// <summary>The full contents of a .bcfzip archive.</summary>
public sealed record BcfDocument(BcfVersion Version)
{
    public BcfProject? Project { get; init; }
    public IList<BcfTopicFolder> Topics { get; init; } = new List<BcfTopicFolder>();
}
