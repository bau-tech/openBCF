using System.Text.Json.Serialization;

namespace OpenBcf.Core.Protocol.Dto;

internal sealed class TopicDto
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("topic_type")]
    public string? TopicType { get; set; }

    [JsonPropertyName("topic_status")]
    public string? TopicStatus { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("labels")]
    public List<string>? Labels { get; set; }

    [JsonPropertyName("creation_date")]
    public DateTimeOffset? CreationDate { get; set; }

    [JsonPropertyName("creation_author")]
    public string? CreationAuthor { get; set; }

    [JsonPropertyName("modified_date")]
    public DateTimeOffset? ModifiedDate { get; set; }

    [JsonPropertyName("modified_author")]
    public string? ModifiedAuthor { get; set; }

    [JsonPropertyName("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [JsonPropertyName("assigned_to")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Body for creating/updating a topic. The BCF API server assigns guid, creation_date,
/// modified_date and modified_author itself - sending them (even as an empty string default)
/// fails server-side schema validation with 422 Unprocessable Entity. creation_author is the
/// exception: the server's TopicCreate schema requires it on the client.
/// </summary>
internal sealed class TopicWriteDto
{
    [JsonPropertyName("topic_type")]
    public string? TopicType { get; set; }

    [JsonPropertyName("topic_status")]
    public string? TopicStatus { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("creation_author")]
    public string? CreationAuthor { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = new();

    [JsonPropertyName("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [JsonPropertyName("assigned_to")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
