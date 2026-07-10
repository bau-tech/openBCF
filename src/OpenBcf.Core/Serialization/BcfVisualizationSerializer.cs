using System.Globalization;
using System.Xml.Linq;
using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.Core.Serialization;

public static class BcfVisualizationSerializer
{
    public static BcfVisualizationInfo Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("viewpoint.bcfv is empty.");

        var guid = Guid.Parse(root.Attribute("Guid")?.Value
            ?? throw new InvalidDataException("VisualizationInfo element is missing Guid."));

        return new BcfVisualizationInfo(guid)
        {
            Components = root.Element("Components") is { } components ? ReadComponents(components) : null,
            Camera = ReadCamera(root),
            Lines = root.Element("Lines")?.Elements("Line").Select(ReadLine).ToList() ?? new List<BcfLine>(),
            ClippingPlanes = root.Element("ClippingPlanes")?.Elements("ClippingPlane").Select(ReadClippingPlane).ToList() ?? new List<BcfClippingPlane>(),
            Bitmaps = root.Element("Bitmaps")?.Elements("Bitmap").Select(ReadBitmap).ToList() ?? new List<BcfBitmap>(),
        };
    }

    public static void Write(BcfVisualizationInfo info, Stream stream)
    {
        var root = new XElement("VisualizationInfo", new XAttribute("Guid", info.Guid));

        if (info.Components is { } components)
            root.Add(WriteComponents(components));

        if (info.Camera is { } camera)
            root.Add(WriteCamera(camera));

        if (info.Lines.Count > 0)
            root.Add(new XElement("Lines", info.Lines.Select(WriteLine)));

        if (info.ClippingPlanes.Count > 0)
            root.Add(new XElement("ClippingPlanes", info.ClippingPlanes.Select(WriteClippingPlane)));

        if (info.Bitmaps.Count > 0)
            root.Add(new XElement("Bitmaps", info.Bitmaps.Select(WriteBitmap)));

        new XDocument(root).Save(stream);
    }

    private static BcfComponents ReadComponents(XElement element) => new()
    {
        ViewSetupHints = element.Element("ViewSetupHints") is { } hints
            ? new BcfViewSetupHints(
                ParseBool(hints.Attribute("SpacesVisible")?.Value),
                ParseBool(hints.Attribute("SpaceBoundariesVisible")?.Value),
                ParseBool(hints.Attribute("OpeningsVisible")?.Value))
            : null,
        Selection = element.Element("Selection")?.Elements("Component").Select(ReadComponent).ToList() ?? new List<BcfComponent>(),
        Coloring = element.Element("Coloring")?.Elements("Color").Select(ReadColoring).ToList() ?? new List<BcfColoring>(),
        Visibility = element.Element("Visibility") is { } visibility
            ? new BcfComponentVisibility(ParseBool(visibility.Attribute("DefaultVisibility")?.Value, defaultValue: true))
            {
                Exceptions = visibility.Element("Exceptions")?.Elements("Component").Select(ReadComponent).ToList() ?? new List<BcfComponent>(),
            }
            : null,
    };

    private static XElement WriteComponents(BcfComponents components)
    {
        var element = new XElement("Components");

        if (components.ViewSetupHints is { } hints)
            element.Add(new XElement("ViewSetupHints",
                new XAttribute("SpacesVisible", hints.SpacesVisible),
                new XAttribute("SpaceBoundariesVisible", hints.SpaceBoundariesVisible),
                new XAttribute("OpeningsVisible", hints.OpeningsVisible)));

        if (components.Selection.Count > 0)
            element.Add(new XElement("Selection", components.Selection.Select(WriteComponent)));

        if (components.Coloring.Count > 0)
            element.Add(new XElement("Coloring", components.Coloring.Select(WriteColoring)));

        if (components.Visibility is { } visibility)
        {
            var visibilityElement = new XElement("Visibility", new XAttribute("DefaultVisibility", visibility.DefaultVisibility));
            if (visibility.Exceptions.Count > 0)
                visibilityElement.Add(new XElement("Exceptions", visibility.Exceptions.Select(WriteComponent)));
            element.Add(visibilityElement);
        }

        return element;
    }

    private static BcfComponent ReadComponent(XElement element) => new(
        IfcGuid: element.Attribute("IfcGuid")?.Value ?? throw new InvalidDataException("Component element is missing IfcGuid."),
        OriginatingSystem: element.Attribute("OriginatingSystem")?.Value,
        AuthoringToolId: element.Attribute("AuthoringToolId")?.Value);

    private static XElement WriteComponent(BcfComponent component)
    {
        var element = new XElement("Component", new XAttribute("IfcGuid", component.IfcGuid));
        if (component.OriginatingSystem is not null) element.Add(new XAttribute("OriginatingSystem", component.OriginatingSystem));
        if (component.AuthoringToolId is not null) element.Add(new XAttribute("AuthoringToolId", component.AuthoringToolId));
        return element;
    }

    private static BcfColoring ReadColoring(XElement element)
    {
        var colorHex = element.Attribute("Color")?.Value
            ?? throw new InvalidDataException("Color element is missing Color.");

        return new BcfColoring(colorHex)
        {
            Components = element.Elements("Component").Select(ReadComponent).ToList(),
        };
    }

    private static XElement WriteColoring(BcfColoring coloring) =>
        new("Color", new XAttribute("Color", coloring.ColorHex), coloring.Components.Select(WriteComponent));

    private static BcfCamera? ReadCamera(XElement root)
    {
        if (root.Element("PerspectiveCamera") is { } perspective)
            return new BcfCamera(
                BcfCameraType.Perspective,
                ReadPoint(perspective.Element("CameraViewPoint")),
                ReadPoint(perspective.Element("CameraDirection")),
                ReadPoint(perspective.Element("CameraUpVector")),
                FieldOfView: double.Parse(perspective.Element("FieldOfView")?.Value ?? "60", CultureInfo.InvariantCulture));

        if (root.Element("OrthogonalCamera") is { } orthogonal)
            return new BcfCamera(
                BcfCameraType.Orthogonal,
                ReadPoint(orthogonal.Element("CameraViewPoint")),
                ReadPoint(orthogonal.Element("CameraDirection")),
                ReadPoint(orthogonal.Element("CameraUpVector")),
                ViewToWorldScale: double.Parse(orthogonal.Element("ViewToWorldScale")?.Value ?? "1", CultureInfo.InvariantCulture));

        return null;
    }

    private static XElement WriteCamera(BcfCamera camera)
    {
        var elementName = camera.Type == BcfCameraType.Perspective ? "PerspectiveCamera" : "OrthogonalCamera";
        var element = new XElement(elementName,
            WritePoint("CameraViewPoint", camera.ViewPoint),
            WritePoint("CameraDirection", camera.Direction),
            WritePoint("CameraUpVector", camera.UpVector));

        if (camera.Type == BcfCameraType.Perspective)
            element.Add(new XElement("FieldOfView", camera.FieldOfView ?? 60));
        else
            element.Add(new XElement("ViewToWorldScale", camera.ViewToWorldScale ?? 1));

        return element;
    }

    private static BcfLine ReadLine(XElement element) =>
        new(ReadPoint(element.Element("StartPoint")), ReadPoint(element.Element("EndPoint")));

    private static XElement WriteLine(BcfLine line) =>
        new("Line", WritePoint("StartPoint", line.Start), WritePoint("EndPoint", line.End));

    private static BcfClippingPlane ReadClippingPlane(XElement element) =>
        new(ReadPoint(element.Element("Location")), ReadPoint(element.Element("Direction")));

    private static XElement WriteClippingPlane(BcfClippingPlane plane) =>
        new("ClippingPlane", WritePoint("Location", plane.Location), WritePoint("Direction", plane.Direction));

    private static BcfBitmap ReadBitmap(XElement element) => new(
        Type: (BcfBitmapType)Enum.Parse(typeof(BcfBitmapType), element.Element("Bitmap")?.Value ?? nameof(BcfBitmapType.Bitmap), ignoreCase: true),
        Reference: element.Element("Reference")?.Value ?? throw new InvalidDataException("Bitmap element is missing Reference."),
        Location: ReadPoint(element.Element("Location")),
        Normal: ReadPoint(element.Element("Normal")),
        Up: ReadPoint(element.Element("Up")),
        Height: double.Parse(element.Element("Height")?.Value ?? "1", CultureInfo.InvariantCulture));

    private static XElement WriteBitmap(BcfBitmap bitmap) => new("Bitmap",
        new XElement("Bitmap", bitmap.Type.ToString()),
        new XElement("Reference", bitmap.Reference),
        WritePoint("Location", bitmap.Location),
        WritePoint("Normal", bitmap.Normal),
        WritePoint("Up", bitmap.Up),
        new XElement("Height", bitmap.Height));

    private static Point3D ReadPoint(XElement? element)
    {
        if (element is null)
            throw new InvalidDataException("Expected a point element with X, Y, Z children.");

        return new Point3D(
            double.Parse(element.Element("X")?.Value ?? "0", CultureInfo.InvariantCulture),
            double.Parse(element.Element("Y")?.Value ?? "0", CultureInfo.InvariantCulture),
            double.Parse(element.Element("Z")?.Value ?? "0", CultureInfo.InvariantCulture));
    }

    private static XElement WritePoint(string elementName, Point3D point) => new(elementName,
        new XElement("X", point.X),
        new XElement("Y", point.Y),
        new XElement("Z", point.Z));

    private static bool ParseBool(string? value, bool defaultValue = false) =>
        value is null ? defaultValue : bool.Parse(value);
}
