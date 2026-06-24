namespace BCFree.Core.Model;

public sealed record BcfMarkup(BcfTopic Topic)
{
    public IList<BcfFileReference> Files { get; init; } = new List<BcfFileReference>();
    public IList<BcfComment> Comments { get; init; } = new List<BcfComment>();
    public IList<BcfViewpointReference> Viewpoints { get; init; } = new List<BcfViewpointReference>();
}
