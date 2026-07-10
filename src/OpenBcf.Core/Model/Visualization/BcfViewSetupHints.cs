namespace OpenBcf.Core.Model.Visualization;

public sealed record BcfViewSetupHints(
    bool SpacesVisible = false,
    bool SpaceBoundariesVisible = false,
    bool OpeningsVisible = false);
