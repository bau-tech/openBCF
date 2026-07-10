using OpenBcf.Core.Model;
using OpenBcf.Core.Model.Visualization;

namespace OpenBcf.Core.Protocol.Dto;

internal static class BcfRestMapper
{
    public static BcfProject ToDomain(ProjectDto dto) => new(dto.ProjectId, dto.Name);

    public static BcfProjectExtensions ToDomain(ExtensionDto dto) => new()
    {
        TopicTypes = dto.TopicTypes ?? new List<string>(),
        TopicStatuses = dto.TopicStatuses ?? new List<string>(),
        TopicLabels = dto.TopicLabels ?? new List<string>(),
        SnippetTypes = dto.SnippetTypes ?? new List<string>(),
        Priorities = dto.Priorities ?? new List<string>(),
        Users = dto.Users ?? new List<string>(),
        Stages = dto.Stages ?? new List<string>(),
        ProjectActions = dto.ProjectActions ?? new List<string>(),
        TopicActions = dto.TopicActions ?? new List<string>(),
        CommentActions = dto.CommentActions ?? new List<string>(),
    };

    public static BcfTopic ToDomain(TopicDto dto) => new(
        Guid: Guid.Parse(dto.Guid),
        Title: dto.Title,
        TopicType: dto.TopicType,
        TopicStatus: dto.TopicStatus,
        Priority: dto.Priority,
        Index: dto.Index,
        CreationDate: dto.CreationDate,
        CreationAuthor: dto.CreationAuthor,
        ModifiedDate: dto.ModifiedDate,
        ModifiedAuthor: dto.ModifiedAuthor,
        DueDate: dto.DueDate,
        AssignedTo: dto.AssignedTo,
        Stage: dto.Stage,
        Description: dto.Description)
    {
        Labels = dto.Labels ?? new List<string>(),
    };

    public static TopicWriteDto ToWriteDto(BcfTopic topic) => new()
    {
        TopicType = topic.TopicType,
        TopicStatus = topic.TopicStatus,
        Title = topic.Title,
        CreationAuthor = topic.CreationAuthor,
        Priority = topic.Priority,
        Index = topic.Index,
        Labels = topic.Labels.ToList(),
        DueDate = topic.DueDate,
        AssignedTo = topic.AssignedTo,
        Stage = topic.Stage,
        Description = topic.Description,
    };

    public static BcfComment ToDomain(CommentDto dto) => new(
        Guid: Guid.Parse(dto.Guid),
        Date: dto.Date,
        Author: dto.Author,
        Comment: dto.Comment,
        ViewpointGuid: dto.ViewpointGuid is { } viewpointGuid ? Guid.Parse(viewpointGuid) : null,
        ModifiedDate: dto.ModifiedDate,
        ModifiedAuthor: dto.ModifiedAuthor);

    public static CommentWriteDto ToWriteDto(BcfComment comment) => new()
    {
        Comment = comment.Comment,
        Author = comment.Author,
        ViewpointGuid = comment.ViewpointGuid?.ToString(),
    };

    public static BcfFileReference ToDomain(FileDto dto) => new(
        IfcProject: dto.IfcProject,
        IfcSpatialStructureElement: dto.IfcSpatialStructureElement,
        Filename: dto.Filename,
        Date: dto.Date,
        Reference: dto.Reference,
        IsExternal: dto.IsExternal ?? false);

    public static FileDto ToDto(BcfFileReference file) => new()
    {
        IfcProject = file.IfcProject,
        IfcSpatialStructureElement = file.IfcSpatialStructureElement,
        Filename = file.Filename,
        Date = file.Date,
        Reference = file.Reference,
        IsExternal = file.IsExternal,
    };

    public static BcfProjectFileInformation ToDomain(ProjectFileInformationDto dto) => new(ToDomain(dto.File))
    {
        DisplayInformation = dto.DisplayInformation?.Select(ToDomain).ToList() ?? new List<BcfDisplayInformation>(),
    };

    public static BcfDisplayInformation ToDomain(DisplayInformationDto dto) => new(dto.FieldDisplayName, dto.FieldValue);

    public static BcfDocumentReference ToDomain(DocumentReferenceDto dto) => new(
        Guid: TryParseGuid(dto.Guid) ?? Guid.Empty,
        ReferencedDocument: dto.DocumentGuid ?? dto.Url,
        Description: dto.Description,
        IsExternal: dto.Url is { Length: > 0 },
        Url: ParseOptionalUri(dto.Url),
        DocumentGuid: TryParseGuid(dto.DocumentGuid));

    public static DocumentReferenceDto ToDto(BcfDocumentReference reference) => new()
    {
        Guid = reference.Guid == Guid.Empty ? null : reference.Guid.ToString(),
        Url = reference.Url?.ToString() ?? (reference.IsExternal ? reference.ReferencedDocument : null),
        DocumentGuid = reference.DocumentGuid?.ToString() ?? (!reference.IsExternal ? reference.ReferencedDocument : null),
        Description = reference.Description,
    };

    public static BcfServerDocument ToDomain(DocumentDto dto) => new(
        Guid: Guid.Parse(dto.Guid),
        FileName: dto.FileName,
        CreationDate: dto.CreationDate,
        CreationAuthor: dto.CreationAuthor,
        ModifiedDate: dto.ModifiedDate,
        ModifiedAuthor: dto.ModifiedAuthor);

    public static BcfEvent ToDomain(EventDto dto) => new(
        TopicGuid: Guid.Parse(dto.TopicGuid),
        Date: dto.Date,
        Author: dto.Author,
        EventType: dto.EventType,
        CommentGuid: TryParseGuid(dto.CommentGuid),
        Action: dto.Action);

    public static BcfVisualizationInfo ToDomain(ViewpointDto dto) => new(Guid.Parse(dto.Guid))
    {
        Components = dto.Components is { } components ? ToDomain(components) : ToDomainFlatComponents(dto),
        Camera = ToDomainCamera(dto),
        Lines = dto.Lines?.Select(ToDomain).ToList() ?? new List<BcfLine>(),
        ClippingPlanes = dto.ClippingPlanes?.Select(ToDomain).ToList() ?? new List<BcfClippingPlane>(),
        Bitmaps = dto.Bitmaps?.Select(ToDomain).ToList() ?? new List<BcfBitmap>(),
    };

    public static ViewpointWriteDto ToWriteDto(BcfVisualizationInfo info, byte[]? snapshotPngBytes = null)
    {
        var dto = new ViewpointWriteDto
        {
            Components = info.Components is { } components ? ToDto(components) : null,
            // Some BCF servers declare these as plain (non-nullable) arrays defaulting to [] -
            // sending null fails their schema validation with a 422, so always send a list.
            Lines = info.Lines.Select(ToDto).ToList(),
            ClippingPlanes = info.ClippingPlanes.Select(ToDto).ToList(),
            Bitmaps = info.Bitmaps.Select(ToDto).ToList(),
            Snapshot = snapshotPngBytes is { Length: > 0 }
                ? new SnapshotDto { SnapshotType = "png", SnapshotData = Convert.ToBase64String(snapshotPngBytes) }
                : null,
            SnapshotBase64 = snapshotPngBytes is { Length: > 0 } ? Convert.ToBase64String(snapshotPngBytes) : null,
        };

        if (info.Camera is { } camera)
        {
            var cameraDto = ToDto(camera);
            if (camera.Type == BcfCameraType.Perspective)
                dto.PerspectiveCamera = cameraDto;
            else
                dto.OrthogonalCamera = cameraDto;

            dto.IsOrthogonal = camera.Type == BcfCameraType.Orthogonal;
            dto.CameraViewPoint = ToDto(camera.ViewPoint);
            dto.CameraDirection = ToDto(camera.Direction);
            dto.CameraUpVector = ToDto(camera.UpVector);
            dto.FlatFieldOfView = camera.FieldOfView;
            dto.FlatViewToWorldScale = camera.ViewToWorldScale;
        }

        if (info.Components is { } flatComponents)
        {
            dto.FlatSelection = flatComponents.Selection.Select(c => c.IfcGuid).ToList();
            dto.DefaultVisibility = flatComponents.Visibility?.DefaultVisibility ?? true;
            dto.VisibilityExceptions = flatComponents.Visibility?.Exceptions.Select(c => c.IfcGuid).ToList() ?? new List<string>();
            dto.FlatColoring = flatComponents.Coloring
                .Select(c => new FlatColoringDto { Color = c.ColorHex, Components = c.Components.Select(x => x.IfcGuid).ToList() })
                .ToList();
        }

        return dto;
    }

    private static BcfCamera? ToDomainCamera(ViewpointDto dto)
    {
        if (dto.PerspectiveCamera is { } perspective)
            return new BcfCamera(
                BcfCameraType.Perspective,
                ToDomain(perspective.CameraViewPoint),
                ToDomain(perspective.CameraDirection),
                ToDomain(perspective.CameraUpVector),
                FieldOfView: perspective.FieldOfView ?? 60);

        if (dto.OrthogonalCamera is { } orthogonal)
            return new BcfCamera(
                BcfCameraType.Orthogonal,
                ToDomain(orthogonal.CameraViewPoint),
                ToDomain(orthogonal.CameraDirection),
                ToDomain(orthogonal.CameraUpVector),
                ViewToWorldScale: orthogonal.ViewToWorldScale ?? 1);

        // Fallback to REDACTED-server.invalid's flat, non-spec camera fields - see the comments on
        // ViewpointDto/ViewpointWriteDto for why these exist alongside the nested shape above.
        if (dto.CameraViewPoint is { } viewPoint)
        {
            var direction = dto.CameraDirection ?? new PointDto();
            var up = dto.CameraUpVector ?? new PointDto();

            return dto.IsOrthogonal == true
                ? new BcfCamera(BcfCameraType.Orthogonal, ToDomain(viewPoint), ToDomain(direction), ToDomain(up), ViewToWorldScale: dto.FlatViewToWorldScale ?? 1)
                : new BcfCamera(BcfCameraType.Perspective, ToDomain(viewPoint), ToDomain(direction), ToDomain(up), FieldOfView: dto.FlatFieldOfView ?? 60);
        }

        return null;
    }

    private static BcfComponents? ToDomainFlatComponents(ViewpointDto dto)
    {
        var selection = dto.FlatSelection?.Select(guid => new BcfComponent(guid)).ToList();
        var coloring = dto.FlatColoring?
            .Select(c => new BcfColoring(c.Color ?? string.Empty) { Components = c.Components?.Select(guid => new BcfComponent(guid)).ToList() ?? new List<BcfComponent>() })
            .ToList();
        var hasVisibility = dto.DefaultVisibility is not null || dto.VisibilityExceptions is { Count: > 0 };

        if ((selection is null || selection.Count == 0) && (coloring is null || coloring.Count == 0) && !hasVisibility)
            return null;

        return new BcfComponents
        {
            Selection = selection ?? new List<BcfComponent>(),
            Coloring = coloring ?? new List<BcfColoring>(),
            Visibility = hasVisibility
                ? new BcfComponentVisibility(dto.DefaultVisibility ?? true)
                {
                    Exceptions = dto.VisibilityExceptions?.Select(guid => new BcfComponent(guid)).ToList() ?? new List<BcfComponent>(),
                }
                : null,
        };
    }

    private static CameraDto ToDto(BcfCamera camera) => new()
    {
        CameraViewPoint = ToDto(camera.ViewPoint),
        CameraDirection = ToDto(camera.Direction),
        CameraUpVector = ToDto(camera.UpVector),
        FieldOfView = camera.FieldOfView,
        ViewToWorldScale = camera.ViewToWorldScale,
    };

    private static BcfComponents ToDomain(ComponentsDto dto) => new()
    {
        ViewSetupHints = dto.ViewSetupHints is { } hints
            ? new BcfViewSetupHints(hints.SpacesVisible, hints.SpaceBoundariesVisible, hints.OpeningsVisible)
            : null,
        Selection = dto.Selection?.Select(ToDomain).ToList() ?? new List<BcfComponent>(),
        Coloring = dto.Coloring?.Select(ToDomain).ToList() ?? new List<BcfColoring>(),
        Visibility = dto.Visibility is { } visibility
            ? new BcfComponentVisibility(visibility.DefaultVisibility) { Exceptions = visibility.Exceptions?.Select(ToDomain).ToList() ?? new List<BcfComponent>() }
            : null,
    };

    private static ComponentsDto ToDto(BcfComponents components) => new()
    {
        ViewSetupHints = components.ViewSetupHints is { } hints
            ? new ViewSetupHintsDto
            {
                SpacesVisible = hints.SpacesVisible,
                SpaceBoundariesVisible = hints.SpaceBoundariesVisible,
                OpeningsVisible = hints.OpeningsVisible,
            }
            : null,
        Selection = components.Selection.Select(ToDto).ToList(),
        Coloring = components.Coloring.Select(ToDto).ToList(),
        Visibility = components.Visibility is { } visibility
            ? new VisibilityDto
            {
                DefaultVisibility = visibility.DefaultVisibility,
                Exceptions = visibility.Exceptions.Select(ToDto).ToList(),
            }
            : null,
    };

    private static BcfComponent ToDomain(ComponentDto dto) => new(dto.IfcGuid, dto.OriginatingSystem, dto.AuthoringToolId);

    private static ComponentDto ToDto(BcfComponent component) => new()
    {
        IfcGuid = component.IfcGuid,
        OriginatingSystem = component.OriginatingSystem,
        AuthoringToolId = component.AuthoringToolId,
    };

    private static BcfColoring ToDomain(ColoringDto dto) => new(dto.Color) { Components = dto.Components?.Select(ToDomain).ToList() ?? new List<BcfComponent>() };

    private static ColoringDto ToDto(BcfColoring coloring) => new()
    {
        Color = coloring.ColorHex,
        Components = coloring.Components.Select(ToDto).ToList(),
    };

    private static BcfLine ToDomain(LineDto dto) => new(ToDomain(dto.StartPoint), ToDomain(dto.EndPoint));

    private static LineDto ToDto(BcfLine line) => new() { StartPoint = ToDto(line.Start), EndPoint = ToDto(line.End) };

    private static BcfClippingPlane ToDomain(ClippingPlaneDto dto) => new(ToDomain(dto.Location), ToDomain(dto.Direction));

    private static ClippingPlaneDto ToDto(BcfClippingPlane plane) => new() { Location = ToDto(plane.Location), Direction = ToDto(plane.Direction) };

    private static BcfBitmap ToDomain(BitmapDto dto) => new(
        (BcfBitmapType)Enum.Parse(typeof(BcfBitmapType), dto.BitmapType, ignoreCase: true),
        dto.Reference,
        ToDomain(dto.Location),
        ToDomain(dto.Normal),
        ToDomain(dto.Up),
        dto.Height);

    private static BitmapDto ToDto(BcfBitmap bitmap) => new()
    {
        BitmapType = bitmap.Type.ToString(),
        Reference = bitmap.Reference,
        Location = ToDto(bitmap.Location),
        Normal = ToDto(bitmap.Normal),
        Up = ToDto(bitmap.Up),
        Height = bitmap.Height,
    };

    private static Point3D ToDomain(PointDto dto) => new(dto.X, dto.Y, dto.Z);

    private static PointDto ToDto(Point3D point) => new() { X = point.X, Y = point.Y, Z = point.Z };

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : null;

    private static Uri? ParseOptionalUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
