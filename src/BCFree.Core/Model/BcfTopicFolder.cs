using BCFree.Core.Model.Visualization;

namespace BCFree.Core.Model;

/// <summary>
/// One topic's contents inside a .bcfzip archive: its markup, the viewpoint files it
/// references (by filename, e.g. "viewpoint.bcfv"), and any other attachments such as snapshots.
/// </summary>
public sealed record BcfTopicFolder(BcfMarkup Markup)
{
    public IDictionary<string, BcfVisualizationInfo> Viewpoints { get; init; } = new Dictionary<string, BcfVisualizationInfo>();
    public IDictionary<string, byte[]> Attachments { get; init; } = new Dictionary<string, byte[]>();
}
