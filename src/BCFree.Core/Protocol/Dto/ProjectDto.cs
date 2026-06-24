using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class ProjectDto
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class ProjectWriteDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
