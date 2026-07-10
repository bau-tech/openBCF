using OpenBcf.Core.Model.Visualization;
using Tekla.Structures;
using Tekla.Structures.Model.UI;
// UseWindowsForms's implicit global usings pull in System.Windows.Forms and System.Drawing,
// whose View/Point types collide with Tekla.Structures.Model.UI.View and
// Tekla.Structures.Geometry3d.Point - alias instead of fully qualifying every reference.
using View = Tekla.Structures.Model.UI.View;
using Model = Tekla.Structures.Model.Model;

namespace OpenBcf.Tekla2025.Client;

/// <summary>
/// Restores a BCF viewpoint into Tekla's active view - the inverse of
/// <see cref="BcfViewpointCapture"/>: moves the camera back to the saved location/direction/up
/// (and field of view or zoom, depending on projection type), and re-selects whichever model
/// objects were selected when the viewpoint was captured.
/// </summary>
public static class BcfViewpointApply
{
    private const double MetersToMillimeters = 1000;

    public static void Apply(BcfVisualizationInfo viewpoint)
    {
        var hasCamera = viewpoint.Camera is not null;
        var hasSelection = viewpoint.Components?.Selection is { Count: > 0 };
        if (!hasCamera && !hasSelection)
        {
            // Without this, a viewpoint whose BCF data never actually carried a camera/selection
            // (e.g. one captured before this feature existed, or one the server stored
            // incompletely) just does nothing when applied - no exception, no visible change at
            // all, which is indistinguishable from a real bug from the user's side. Surfacing it
            // turns "silently did nothing" into a diagnosable error.
            throw new InvalidOperationException("This viewpoint has no camera or selected parts to apply.");
        }

        var view = ViewHandler.GetActiveView()
            ?? throw new InvalidOperationException("Open a view in Tekla Structures before applying a viewpoint.");

        if (viewpoint.Camera is { } camera)
        {
            ApplyCamera(view, camera);
        }

        if (hasSelection)
        {
            ApplySelection(viewpoint.Components!);
        }

        ViewHandler.RedrawView(view);
    }

    private static void ApplyCamera(View view, BcfCamera camera)
    {
        // Select() first, mirroring BcfViewpointCapture's read side - a freshly-constructed
        // ViewCamera with only View set has nothing telling Modify() which camera to update, so
        // it can silently no-op (return false, no exception) instead of actually moving the view.
        var viewCamera = new ViewCamera { View = view };
        viewCamera.Select();

        viewCamera.Location = new Tekla.Structures.Geometry3d.Point(
            camera.ViewPoint.X * MetersToMillimeters,
            camera.ViewPoint.Y * MetersToMillimeters,
            camera.ViewPoint.Z * MetersToMillimeters);
        viewCamera.DirectionVector = new Tekla.Structures.Geometry3d.Vector(camera.Direction.X, camera.Direction.Y, camera.Direction.Z);
        viewCamera.UpVector = new Tekla.Structures.Geometry3d.Vector(camera.UpVector.X, camera.UpVector.Y, camera.UpVector.Z);

        if (camera.Type == BcfCameraType.Perspective && camera.FieldOfView is { } fieldOfView)
        {
            viewCamera.FieldOfView = fieldOfView;
        }
        else if (camera.ViewToWorldScale is { } scale)
        {
            viewCamera.ZoomFactor = scale;
        }

        if (!viewCamera.Modify())
            throw new InvalidOperationException("Tekla Structures rejected the camera change for this viewpoint.");
    }

    private static void ApplySelection(BcfComponents components)
    {
        var model = new Model();
        var objects = new System.Collections.ArrayList();
        var missing = 0;

        foreach (var component in components.Selection)
        {
            if (Guid.TryParse(component.IfcGuid, out var guid) && model.SelectModelObject(new Identifier(guid)) is { } modelObject)
            {
                objects.Add(modelObject);
            }
            else
            {
                missing++;
            }
        }

        if (objects.Count > 0 && !new ModelObjectSelector().Select(objects))
            throw new InvalidOperationException("Tekla Structures rejected the part selection for this viewpoint.");

        if (objects.Count == 0 && missing > 0)
            throw new InvalidOperationException("None of this viewpoint's parts could be found in the current model.");
    }
}
