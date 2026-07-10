using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.Revit2025.Client;

/// <summary>
/// Restores a BCF viewpoint into Revit's active 3D view - the inverse of
/// <see cref="BcfViewpointCapture"/>: moves the camera back to the saved location/direction/up,
/// and re-selects whichever elements were selected when the viewpoint was captured (matched by
/// <see cref="Element.UniqueId"/>, the same stand-in identifier capture uses).
/// </summary>
public static class BcfViewpointApply
{
    private const double MetersToFeet = 1 / 0.3048;

    public static void Apply(BcfVisualizationInfo viewpoint, UIDocument uiDocument)
    {
        var hasCamera = viewpoint.Camera is not null;
        var hasSelection = viewpoint.Components?.Selection is { Count: > 0 };
        if (!hasCamera && !hasSelection)
        {
            // Without this, a viewpoint whose BCF data never actually carried a camera/selection
            // just does nothing when applied - no exception, no visible change at all, which is
            // indistinguishable from a real bug from the user's side. Surfacing it turns "silently
            // did nothing" into a diagnosable error.
            throw new InvalidOperationException("This viewpoint has no camera or selected parts to apply.");
        }

        var view = uiDocument.ActiveView as View3D
            ?? throw new InvalidOperationException("Switch to a 3D view before applying a viewpoint.");

        if (viewpoint.Camera is { } camera)
        {
            ApplyCamera(uiDocument, view, camera);
        }

        if (hasSelection)
        {
            ApplySelection(uiDocument, viewpoint.Components!);
        }
    }

    private static void ApplyCamera(UIDocument uiDocument, View3D view, BcfCamera camera)
    {
        var eye = new XYZ(camera.ViewPoint.X * MetersToFeet, camera.ViewPoint.Y * MetersToFeet, camera.ViewPoint.Z * MetersToFeet);
        var forward = new XYZ(camera.Direction.X, camera.Direction.Y, camera.Direction.Z);
        var up = new XYZ(camera.UpVector.X, camera.UpVector.Y, camera.UpVector.Z);

        using var transaction = new Transaction(uiDocument.Document, "Apply BCF viewpoint camera");
        transaction.Start();
        view.SetOrientation(new ViewOrientation3D(eye, up, forward));
        transaction.Commit();

        // Field of view / zoom scale restoration has no public Revit API equivalent to Tekla's
        // ViewCamera.FieldOfView/ZoomFactor setters - the closest available is fitting the active
        // UIView to the new camera so the view actually redraws at the new orientation.
        var uiView = uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
        uiView?.ZoomToFit();
    }

    private static void ApplySelection(UIDocument uiDocument, BcfComponents components)
    {
        var document = uiDocument.Document;
        var elementIds = new List<ElementId>();
        var missing = 0;

        foreach (var component in components.Selection)
        {
            if (document.GetElement(component.IfcGuid) is { } element)
            {
                elementIds.Add(element.Id);
            }
            else
            {
                missing++;
            }
        }

        if (elementIds.Count > 0)
        {
            uiDocument.Selection.SetElementIds(elementIds);
        }

        if (elementIds.Count == 0 && missing > 0)
        {
            throw new InvalidOperationException("None of this viewpoint's parts could be found in the current model.");
        }
    }
}
