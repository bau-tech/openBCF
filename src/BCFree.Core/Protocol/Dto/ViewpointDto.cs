using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class ViewpointDto
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("perspective_camera")]
    public CameraDto? PerspectiveCamera { get; set; }

    [JsonPropertyName("orthogonal_camera")]
    public CameraDto? OrthogonalCamera { get; set; }

    [JsonPropertyName("lines")]
    public List<LineDto>? Lines { get; set; }

    [JsonPropertyName("clipping_planes")]
    public List<ClippingPlaneDto>? ClippingPlanes { get; set; }

    [JsonPropertyName("bitmaps")]
    public List<BitmapDto>? Bitmaps { get; set; }

    [JsonPropertyName("components")]
    public ComponentsDto? Components { get; set; }
}

/// <summary>
/// Body for creating a viewpoint. As with <see cref="TopicWriteDto"/>, the server assigns guid
/// on creation - sending an empty one fails schema validation. Snapshot is optional and only
/// used when importing a .bcfzip that already has one; the server has no way to receive a
/// snapshot for a viewpoint created without it (GetSnapshotAsync is read-only).
/// </summary>
internal sealed class ViewpointWriteDto
{
    [JsonPropertyName("perspective_camera")]
    public CameraDto? PerspectiveCamera { get; set; }

    [JsonPropertyName("orthogonal_camera")]
    public CameraDto? OrthogonalCamera { get; set; }

    [JsonPropertyName("lines")]
    public List<LineDto>? Lines { get; set; }

    [JsonPropertyName("clipping_planes")]
    public List<ClippingPlaneDto>? ClippingPlanes { get; set; }

    [JsonPropertyName("bitmaps")]
    public List<BitmapDto>? Bitmaps { get; set; }

    [JsonPropertyName("components")]
    public ComponentsDto? Components { get; set; }

    [JsonPropertyName("snapshot")]
    public SnapshotDto? Snapshot { get; set; }

    // Not part of the official BCF-API 2.1 ViewpointCreate shape (which nests snapshot_type and
    // snapshot_data under "snapshot" above) - bcf.chladny.de's actual schema (verified against its
    // /openapi.json) instead expects a flat base64 string under this key and ignores "snapshot"
    // entirely. Sending both covers a spec-compliant server (which ignores the extra field) and
    // this one (which ignores the nested one).
    [JsonPropertyName("snapshot_base64")]
    public string? SnapshotBase64 { get; set; }
}

internal sealed class SnapshotDto
{
    [JsonPropertyName("snapshot_type")]
    public string SnapshotType { get; set; } = "png";

    [JsonPropertyName("snapshot_data")]
    public string SnapshotData { get; set; } = string.Empty;
}

internal sealed class PointDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

internal sealed class CameraDto
{
    [JsonPropertyName("camera_view_point")]
    public PointDto CameraViewPoint { get; set; } = new();

    [JsonPropertyName("camera_direction")]
    public PointDto CameraDirection { get; set; } = new();

    [JsonPropertyName("camera_up_vector")]
    public PointDto CameraUpVector { get; set; } = new();

    [JsonPropertyName("field_of_view")]
    public double? FieldOfView { get; set; }

    [JsonPropertyName("view_to_world_scale")]
    public double? ViewToWorldScale { get; set; }
}

internal sealed class LineDto
{
    [JsonPropertyName("start_point")]
    public PointDto StartPoint { get; set; } = new();

    [JsonPropertyName("end_point")]
    public PointDto EndPoint { get; set; } = new();
}

internal sealed class ClippingPlaneDto
{
    [JsonPropertyName("location")]
    public PointDto Location { get; set; } = new();

    [JsonPropertyName("direction")]
    public PointDto Direction { get; set; } = new();
}

internal sealed class BitmapDto
{
    [JsonPropertyName("bitmap_type")]
    public string BitmapType { get; set; } = "Bitmap";

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public PointDto Location { get; set; } = new();

    [JsonPropertyName("normal")]
    public PointDto Normal { get; set; } = new();

    [JsonPropertyName("up")]
    public PointDto Up { get; set; } = new();

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

internal sealed class ComponentDto
{
    [JsonPropertyName("ifc_guid")]
    public string IfcGuid { get; set; } = string.Empty;

    [JsonPropertyName("originating_system")]
    public string? OriginatingSystem { get; set; }

    [JsonPropertyName("authoring_tool_id")]
    public string? AuthoringToolId { get; set; }
}

internal sealed class ViewSetupHintsDto
{
    [JsonPropertyName("spaces_visible")]
    public bool SpacesVisible { get; set; }

    [JsonPropertyName("space_boundaries_visible")]
    public bool SpaceBoundariesVisible { get; set; }

    [JsonPropertyName("openings_visible")]
    public bool OpeningsVisible { get; set; }
}

internal sealed class ColoringDto
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("components")]
    public List<ComponentDto>? Components { get; set; }
}

internal sealed class VisibilityDto
{
    [JsonPropertyName("default_visibility")]
    public bool DefaultVisibility { get; set; } = true;

    [JsonPropertyName("exceptions")]
    public List<ComponentDto>? Exceptions { get; set; }
}

internal sealed class ComponentsDto
{
    [JsonPropertyName("view_setup_hints")]
    public ViewSetupHintsDto? ViewSetupHints { get; set; }

    [JsonPropertyName("selection")]
    public List<ComponentDto>? Selection { get; set; }

    [JsonPropertyName("coloring")]
    public List<ColoringDto>? Coloring { get; set; }

    [JsonPropertyName("visibility")]
    public VisibilityDto? Visibility { get; set; }
}
