namespace BCFree.Core.Model.Visualization;

public sealed record BcfColoring(string ColorHex)
{
    public IList<BcfComponent> Components { get; init; } = new List<BcfComponent>();
}
