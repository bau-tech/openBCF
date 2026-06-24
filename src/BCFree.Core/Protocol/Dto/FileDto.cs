using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class FileDto
{
    [JsonPropertyName("ifc_project")]
    public string? IfcProject { get; set; }

    [JsonPropertyName("ifc_spatial_structure_element")]
    public string? IfcSpatialStructureElement { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset? Date { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("is_external")]
    public bool? IsExternal { get; set; }
}

internal sealed class ProjectFileInformationDto
{
    [JsonPropertyName("file")]
    public FileDto File { get; set; } = new();

    [JsonPropertyName("display_information")]
    public List<DisplayInformationDto>? DisplayInformation { get; set; }
}

internal sealed class DisplayInformationDto
{
    [JsonPropertyName("field_display_name")]
    public string FieldDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("field_value")]
    public string FieldValue { get; set; } = string.Empty;
}
