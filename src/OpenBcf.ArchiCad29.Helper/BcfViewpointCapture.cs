using OpenBcf.ArchiCad29.Helper.Ipc;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.ArchiCad29.Helper;

/// <summary>
/// Captures ArchiCAD's active 3D window as a BCF viewpoint: camera, current selection, and a
/// rendered snapshot. Unlike every other client, none of this touches a host SDK directly - every
/// piece is fetched over the callbacks pipe via <see cref="NativeCallbacksClient"/>, since this
/// process cannot call ACAPI itself (see ../OpenBcf.ArchiCad29.NativeAddOn/Src/Interop.h for why).
/// Camera/selection field shapes mirror OpenBcf.Rhino8.Client.BcfViewpointCapture's conventions
/// exactly (meters, IFC GUID strings).
/// </summary>
public static class BcfViewpointCapture
{
    public static (BcfVisualizationInfo Viewpoint, byte[] SnapshotPng) Capture()
    {
        var viewpoint = new BcfVisualizationInfo(Guid.NewGuid())
        {
            Camera = NativeCallbacksClient.TryGetCamera(),
            Components = new BcfComponents { Selection = GetSelection() },
        };

        return (viewpoint, NativeCallbacksClient.CaptureSnapshotPng());
    }

    private static List<BcfComponent> GetSelection() =>
        NativeCallbacksClient.GetSelectionGuids()
            .Select(guid => new BcfComponent(guid, OriginatingSystem: "GRAPHISOFT ARCHICAD", AuthoringToolId: guid))
            .ToList();
}
