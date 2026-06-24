namespace BCFree.Core.Model.Visualization;

public sealed record BcfComponents
{
    public BcfViewSetupHints? ViewSetupHints { get; init; }
    public IList<BcfComponent> Selection { get; init; } = new List<BcfComponent>();
    public IList<BcfColoring> Coloring { get; init; } = new List<BcfColoring>();
    public BcfComponentVisibility? Visibility { get; init; }
}
