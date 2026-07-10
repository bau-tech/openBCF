namespace OpenBcf.Core.Model.Visualization;

public sealed record BcfComponentVisibility(bool DefaultVisibility = true)
{
    public IList<BcfComponent> Exceptions { get; init; } = new List<BcfComponent>();
}
