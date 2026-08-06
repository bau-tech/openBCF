using System.IO;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;
using Rhino;
using Rhino.Display;

namespace OpenBcf.Rhino8.Client;

/// <summary>
/// Captures Rhino's active view as a BCF viewpoint: camera, current selection, and a rendered
/// snapshot. Every member used here (RhinoViewport.CameraLocation/CameraDirection/CameraUp/
/// GetCameraAngle, RhinoView.CaptureToBitmap, ObjectTable.GetSelectedObjects, RhinoObject.Id) was
/// confirmed via .NET reflection directly against the installed RhinoCommon.dll (Rhino 8.30), not
/// just documentation - see project memory for the verification session. Rhino model objects
/// carry a real <see cref="System.Guid"/> (RhinoObject.Id) - like Tekla, and unlike Revit's
/// Element.UniqueId, no stand-in identifier is needed here.
/// </summary>
public static class BcfViewpointCapture
{
    private const double DefaultFieldOfViewDegrees = 60;

    public static (BcfVisualizationInfo Viewpoint, byte[] SnapshotPng) Capture()
    {
        var doc = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("Open a Rhino document before attaching a viewpoint.");
        var view = doc.Views.ActiveView
            ?? throw new InvalidOperationException("Open a view in Rhino before attaching a viewpoint.");

        var viewpoint = new BcfVisualizationInfo(Guid.NewGuid())
        {
            Camera = BuildCamera(doc, view.ActiveViewport),
            Components = BuildComponents(doc),
        };

        var snapshot = RenderSnapshot(view);
        return (viewpoint, snapshot);
    }

    private static BcfCamera BuildCamera(RhinoDoc doc, RhinoViewport viewport)
    {
        // BCF's camera coordinates are meters (matching IFC); Rhino models can be in any unit
        // system, so every point/vector below is scaled explicitly rather than assuming meters.
        var toMeters = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);

        var location = viewport.CameraLocation;
        var direction = viewport.CameraDirection;
        var up = viewport.CameraUp;

        var viewPoint = new Point3D(location.X * toMeters, location.Y * toMeters, location.Z * toMeters);
        var directionPoint = new Point3D(direction.X, direction.Y, direction.Z);
        var upPoint = new Point3D(up.X, up.Y, up.Z);

        if (viewport.IsPerspectiveProjection)
        {
            // GetCameraAngle's three out-angles are half-angles in radians - the universal
            // RhinoCommon convention for angle-returning members (confirmed consistent across the
            // whole reflected API surface); BCF's FieldOfView is the full vertical angle, in degrees.
            var fieldOfView = viewport.GetCameraAngle(out _, out var halfVerticalAngle, out _)
                ? 2 * halfVerticalAngle * (180.0 / Math.PI)
                : DefaultFieldOfViewDegrees;

            return new BcfCamera(BcfCameraType.Perspective, viewPoint, directionPoint, upPoint, FieldOfView: fieldOfView);
        }

        // Rhino has no single "zoom factor" for parallel projection the way Tekla's ViewCamera
        // does - frustum half-height (in meters) is the closest equivalent to BCF's
        // ViewToWorldScale (defined as half the world-space height visible in the view).
        viewport.GetFrustum(out _, out _, out var frustumBottom, out var frustumTop, out _, out _);
        var viewToWorldScale = Math.Max((frustumTop - frustumBottom) / 2.0 * toMeters, 0.001);

        return new BcfCamera(BcfCameraType.Orthogonal, viewPoint, directionPoint, upPoint, ViewToWorldScale: viewToWorldScale);
    }

    private static BcfComponents BuildComponents(RhinoDoc doc)
    {
        var selection = new List<BcfComponent>();

        foreach (var rhinoObject in doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false))
        {
            var guid = rhinoObject.Id.ToString();
            selection.Add(new BcfComponent(guid, OriginatingSystem: "Robert McNeel & Associates Rhinoceros", AuthoringToolId: guid));
        }

        return new BcfComponents { Selection = selection };
    }

    private static byte[] RenderSnapshot(RhinoView view)
    {
        // CaptureToBitmap() returns a real System.Drawing.Bitmap of the current viewport directly
        // - no temp file needed, unlike Tekla/ARCHICAD's snapshot APIs.
        using var bitmap = view.CaptureToBitmap()
            ?? throw new InvalidOperationException("Rhino did not produce a snapshot image for the current view.");

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
