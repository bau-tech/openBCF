using OpenBcf.Core.Model.Visualization;
using Rhino;
using Rhino.Geometry;

namespace OpenBcf.Rhino8.Client;

/// <summary>
/// Restores a BCF viewpoint into Rhino's active view - the inverse of
/// <see cref="BcfViewpointCapture"/>: moves the camera back to the saved location/direction/up
/// (field of view or frustum half-height, depending on projection type), and re-selects whichever
/// model objects were selected when the viewpoint was captured.
/// </summary>
public static class BcfViewpointApply
{
    public static void Apply(BcfVisualizationInfo viewpoint)
    {
        var hasCamera = viewpoint.Camera is not null;
        var hasSelection = viewpoint.Components?.Selection is { Count: > 0 };
        if (!hasCamera && !hasSelection)
        {
            throw new InvalidOperationException("This viewpoint has no camera or selected objects to apply.");
        }

        var doc = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("Open a Rhino document before applying a viewpoint.");
        var view = doc.Views.ActiveView
            ?? throw new InvalidOperationException("Open a view in Rhino before applying a viewpoint.");

        if (viewpoint.Camera is { } camera)
        {
            ApplyCamera(doc, view.ActiveViewport, camera);
        }

        if (hasSelection)
        {
            ApplySelection(doc, viewpoint.Components!);
        }

        doc.Views.Redraw();
    }

    private static void ApplyCamera(RhinoDoc doc, Rhino.Display.RhinoViewport viewport, BcfCamera camera)
    {
        var toModelUnits = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);

        var location = new Point3d(
            camera.ViewPoint.X * toModelUnits,
            camera.ViewPoint.Y * toModelUnits,
            camera.ViewPoint.Z * toModelUnits);
        var direction = new Vector3d(camera.Direction.X, camera.Direction.Y, camera.Direction.Z);
        var up = new Vector3d(camera.UpVector.X, camera.UpVector.Y, camera.UpVector.Z);

        if (camera.Type == BcfCameraType.Perspective && !viewport.IsPerspectiveProjection)
            viewport.ChangeToPerspectiveProjection(symmetricFrustum: true, lensLength: 50.0);
        else if (camera.Type == BcfCameraType.Orthogonal && !viewport.IsParallelProjection)
            viewport.ChangeToParallelProjection(symmetricFrustum: true);

        // SetCameraLocation/SetCameraDirection's updateTargetLocation:true keeps the target on
        // the same ray as the new direction, matching how BcfViewpointCapture derives direction
        // without relying on Rhino's separate "target" concept at all.
        viewport.SetCameraLocation(location, updateTargetLocation: true);
        viewport.SetCameraDirection(direction, updateTargetLocation: true);
        viewport.CameraUp = up;

        if (camera.Type == BcfCameraType.Perspective && camera.FieldOfView is { } fieldOfView)
        {
            // RhinoViewport has no direct "set field of view in degrees" - Camera35mmLensLength
            // (confirmed via reflection against the installed RhinoCommon.dll - there is no
            // SetFrustum method, unlike some other 3D SDKs) is the only absolute perspective
            // strength setter it exposes. Its own XML doc comment ("assumes the camera is
            // horizontal ... when the aspect of the frustum is not 36/24") confirms the standard
            // photographic convention: lens length relates to the HORIZONTAL angle of a 36mm-wide
            // frame, so BCF's vertical FieldOfView is converted to horizontal via the viewport's
            // own aspect ratio before converting to a lens length.
            var halfVerticalAngleRadians = fieldOfView * (Math.PI / 180.0) / 2.0;
            var halfHorizontalAngleRadians = Math.Atan(Math.Tan(halfVerticalAngleRadians) * viewport.FrustumAspect);
            viewport.Camera35mmLensLength = 18.0 / Math.Tan(halfHorizontalAngleRadians);
        }
        else if (camera.Type == BcfCameraType.Orthogonal && camera.ViewToWorldScale is { } scale)
        {
            // Same absolute-setter gap for parallel projection: RhinoViewport has no direct
            // "set frustum half-height" method either, only Magnify's *relative* scale factor
            // (its own XML doc: "Zooms ... to scale the viewport projection of observed
            // objects"). Compute the factor needed to go from the current half-height to the
            // saved one, rather than pretending Rhino offers an absolute setter it doesn't.
            viewport.GetFrustum(out _, out _, out var currentBottom, out var currentTop, out _, out _);
            var currentHalfHeight = (currentTop - currentBottom) / 2.0;
            var targetHalfHeight = Math.Max(scale * toModelUnits, 0.001);
            if (currentHalfHeight > 0)
                viewport.Magnify(currentHalfHeight / targetHalfHeight, mode: true);
        }
    }

    private static void ApplySelection(RhinoDoc doc, BcfComponents components)
    {
        doc.Objects.UnselectAll();

        var matched = 0;
        var missing = 0;

        foreach (var component in components.Selection)
        {
            if (Guid.TryParse(component.IfcGuid, out var guid) && doc.Objects.Find(guid) is not null)
            {
                doc.Objects.Select(guid);
                matched++;
            }
            else
            {
                missing++;
            }
        }

        if (matched == 0 && missing > 0)
            throw new InvalidOperationException("None of this viewpoint's objects could be found in the current document.");
    }
}
