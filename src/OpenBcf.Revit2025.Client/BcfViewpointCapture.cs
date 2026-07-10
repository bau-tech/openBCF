using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.Revit2025.Client;

/// <summary>
/// Captures the active 3D view as a BCF viewpoint: camera, current selection, and a rendered
/// snapshot. Revit elements have no true IFC GUID without a real IFC export correlation, so
/// selected components use <see cref="Element.UniqueId"/> as a stable per-element identifier -
/// not IFC-spec-compliant, but good enough to round-trip inside OpenBcf/non-strict BCF tools.
/// </summary>
public static class BcfViewpointCapture
{
    private const double FeetToMeters = 0.3048;
    private const double DefaultFieldOfViewDegrees = 60;

    public static (BcfVisualizationInfo Viewpoint, byte[] SnapshotPng) Capture(UIDocument uiDocument)
    {
        var view = uiDocument.ActiveView as View3D
            ?? throw new InvalidOperationException("Switch to a 3D view before attaching a viewpoint.");

        var viewpoint = new BcfVisualizationInfo(Guid.NewGuid())
        {
            Camera = BuildCamera(view),
            Components = BuildComponents(uiDocument),
        };

        var snapshot = RenderSnapshot(uiDocument.Document, view);
        return (viewpoint, snapshot);
    }

    private static BcfCamera BuildCamera(View3D view)
    {
        var orientation = view.GetOrientation();
        var eye = orientation.EyePosition;
        var forward = orientation.ForwardDirection;
        var up = orientation.UpDirection;

        var viewPoint = new Point3D(eye.X * FeetToMeters, eye.Y * FeetToMeters, eye.Z * FeetToMeters);
        var direction = new Point3D(forward.X, forward.Y, forward.Z);
        var upVector = new Point3D(up.X, up.Y, up.Z);

        if (view.IsPerspective)
            return new BcfCamera(BcfCameraType.Perspective, viewPoint, direction, upVector, FieldOfView: DefaultFieldOfViewDegrees);

        var scale = view.CropBoxActive ? (view.CropBox.Max.Y - view.CropBox.Min.Y) * FeetToMeters : 1;
        return new BcfCamera(BcfCameraType.Orthogonal, viewPoint, direction, upVector, ViewToWorldScale: scale);
    }

    private static BcfComponents BuildComponents(UIDocument uiDocument)
    {
        var selection = uiDocument.Selection.GetElementIds()
            .Select(id => uiDocument.Document.GetElement(id))
            .Where(element => element is not null)
            .Select(element => new BcfComponent(element!.UniqueId, OriginatingSystem: "Autodesk Revit", AuthoringToolId: element.UniqueId))
            .ToList();

        return new BcfComponents { Selection = selection };
    }

    private static byte[] RenderSnapshot(Document document, View3D view)
    {
        // Revit's exact output filename (whether it honors FilePath's file name verbatim, or
        // derives one from the view name instead) isn't reliable enough to hardcode. Exporting
        // into a dedicated, freshly-created, otherwise-empty directory sidesteps that entirely -
        // whatever single file Revit puts there afterward is the snapshot, regardless of name.
        var tempDir = Path.Combine(Path.GetTempPath(), $"openbcf-viewpoint-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var options = new ImageExportOptions
            {
                FilePath = Path.Combine(tempDir, "viewpoint"),
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1024,
                ImageResolution = ImageResolution.DPI_150,
                ExportRange = ExportRange.CurrentView,
                HLRandWFViewsFileType = ImageFileType.PNG,
            };

            document.ExportImage(options);

            var generatedPath = Directory.EnumerateFiles(tempDir).FirstOrDefault()
                ?? throw new InvalidOperationException("Revit did not produce a snapshot image for the current view.");
            return File.ReadAllBytes(generatedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
