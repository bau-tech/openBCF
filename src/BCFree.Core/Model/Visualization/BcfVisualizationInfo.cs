namespace BCFree.Core.Model.Visualization;

public sealed record BcfVisualizationInfo(Guid Guid)
{
    public BcfComponents? Components { get; init; }
    public BcfCamera? Camera { get; init; }
    public IList<BcfLine> Lines { get; init; } = new List<BcfLine>();
    public IList<BcfClippingPlane> ClippingPlanes { get; init; } = new List<BcfClippingPlane>();
    public IList<BcfBitmap> Bitmaps { get; init; } = new List<BcfBitmap>();
}
