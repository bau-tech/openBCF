using OpenBcf.ArchiCad29.Helper.Ipc;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.ArchiCad29.Helper;

/// <summary>
/// Restores a BCF viewpoint into ArchiCAD's active 3D window - the inverse of
/// <see cref="BcfViewpointCapture"/>, crossing back over the same callbacks pipe via
/// <see cref="NativeCallbacksClient"/>.
/// </summary>
public static class BcfViewpointApply
{
    public static void Apply(BcfVisualizationInfo viewpoint)
    {
        var hasCamera = viewpoint.Camera is not null;
        var hasSelection = viewpoint.Components?.Selection is { Count: > 0 };
        if (!hasCamera && !hasSelection)
            throw new InvalidOperationException("This viewpoint has no camera or selected objects to apply.");

        if (viewpoint.Camera is { } camera && !NativeCallbacksClient.ApplyCamera(camera))
            throw new InvalidOperationException("Open a 3D window in ArchiCAD before applying a viewpoint's camera.");

        if (hasSelection)
            NativeCallbacksClient.ApplySelection(viewpoint.Components!.Selection.Select(c => c.IfcGuid).ToList());
    }
}
