using System.IO;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;
using Tekla.Structures.Model.UI;
// UseWindowsForms's implicit global usings pull in System.Windows.Forms, whose ListView-related
// View enum collides with Tekla.Structures.Model.UI.View - alias it instead of fully qualifying
// every reference.
using View = Tekla.Structures.Model.UI.View;

namespace OpenBcf.Tekla2026.Client;

/// <summary>
/// Captures Tekla's active view as a BCF viewpoint: camera, current selection, and a rendered
/// snapshot. Tekla model objects carry a real <see cref="System.Guid"/> via
/// <see cref="Tekla.Structures.Identifier.GUID"/> - unlike Revit's <c>Element.UniqueId</c>, no
/// stand-in identifier is needed here.
/// </summary>
public static class BcfViewpointCapture
{
    private const double MillimetersToMeters = 0.001;
    private const double DefaultFieldOfViewDegrees = 60;

    public static (BcfVisualizationInfo Viewpoint, byte[] SnapshotPng) Capture()
    {
        var view = ViewHandler.GetActiveView()
            ?? throw new InvalidOperationException("Open a view in Tekla Structures before attaching a viewpoint.");

        var viewpoint = new BcfVisualizationInfo(Guid.NewGuid())
        {
            Camera = BuildCamera(view),
            Components = BuildComponents(),
        };

        var snapshot = RenderSnapshot(view);
        return (viewpoint, snapshot);
    }

    private static BcfCamera BuildCamera(View view)
    {
        var camera = new ViewCamera { View = view };
        camera.Select();

        var location = camera.Location;
        var direction = camera.DirectionVector;
        var up = camera.UpVector;

        var viewPoint = new Point3D(location.X * MillimetersToMeters, location.Y * MillimetersToMeters, location.Z * MillimetersToMeters);
        var directionPoint = new Point3D(direction.X, direction.Y, direction.Z);
        var upPoint = new Point3D(up.X, up.Y, up.Z);

        return view.IsPerspectiveViewProjection()
            ? new BcfCamera(BcfCameraType.Perspective, viewPoint, directionPoint, upPoint, FieldOfView: camera.FieldOfView > 0 ? camera.FieldOfView : DefaultFieldOfViewDegrees)
            : new BcfCamera(BcfCameraType.Orthogonal, viewPoint, directionPoint, upPoint, ViewToWorldScale: camera.ZoomFactor);
    }

    private static BcfComponents BuildComponents()
    {
        var selection = new List<BcfComponent>();

        // Tekla.Structures.Model.UI.ModelObjectSelector (the UI-facing selection, not
        // Model.GetModelObjectSelector(), which queries the whole model by filter instead).
        var enumerator = new ModelObjectSelector().GetSelectedObjects();
        while (enumerator.MoveNext())
        {
            var guid = enumerator.Current.Identifier.GUID.ToString();
            selection.Add(new BcfComponent(guid, OriginatingSystem: "Trimble Tekla Structures", AuthoringToolId: guid));
        }

        return new BcfComponents { Selection = selection };
    }

    private static byte[] RenderSnapshot(View view)
    {
        // Tekla's exact output filename is the one we pass in (unlike Revit's ImageExportOptions,
        // which doesn't reliably honor FilePath verbatim), but the snapshot still lands in a
        // dedicated, freshly-created temp directory so cleanup is a single recursive delete.
        var tempDir = Path.Combine(Path.GetTempPath(), $"openbcf-viewpoint-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, "viewpoint.png");
            var settings = new SnapshotSettings { DPI = 150, Width = 1024, Height = 768, UseSmoothLines = 1, LineWidth = 1, WhiteBG = 1 };

            if (!view.CreateSnapshot(path, settings, false) || !File.Exists(path))
                throw new InvalidOperationException("Tekla Structures did not produce a snapshot image for the current view.");

            return File.ReadAllBytes(path);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
