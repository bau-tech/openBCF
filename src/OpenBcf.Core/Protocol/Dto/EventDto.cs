using System.Text.Json.Serialization;

namespace OpenBcf.Core.Protocol.Dto;

internal sealed class EventDto
{
    [JsonPropertyName("topic_guid")]
    public string TopicGuid { get; set; } = string.Empty;

    [JsonPropertyName("comment_guid")]
    public string? CommentGuid { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}
