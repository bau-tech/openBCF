using System.Text.Json.Serialization;

namespace OpenBcf.Core.Protocol.Dto;

internal sealed class ExtensionDto
{
    [JsonPropertyName("topic_type")]
    public List<string>? TopicTypes { get; set; }

    [JsonPropertyName("topic_status")]
    public List<string>? TopicStatuses { get; set; }

    [JsonPropertyName("topic_label")]
    public List<string>? TopicLabels { get; set; }

    [JsonPropertyName("snippet_type")]
    public List<string>? SnippetTypes { get; set; }

    [JsonPropertyName("priority")]
    public List<string>? Priorities { get; set; }

    [JsonPropertyName("users")]
    public List<string>? Users { get; set; }

    [JsonPropertyName("stage")]
    public List<string>? Stages { get; set; }

    [JsonPropertyName("project_actions")]
    public List<string>? ProjectActions { get; set; }

    [JsonPropertyName("topic_actions")]
    public List<string>? TopicActions { get; set; }

    [JsonPropertyName("comment_actions")]
    public List<string>? CommentActions { get; set; }
}
