using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class BimSnippetDto
{
    [JsonPropertyName("snippet_type")]
    public string SnippetType { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("reference_schema")]
    public string? ReferenceSchema { get; set; }

    [JsonPropertyName("is_external")]
    public bool? IsExternal { get; set; }
}

internal sealed class RelatedTopicDto
{
    [JsonPropertyName("related_topic_guid")]
    public string RelatedTopicGuid { get; set; } = string.Empty;
}
