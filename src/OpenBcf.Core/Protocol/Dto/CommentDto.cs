using System.Text.Json.Serialization;

namespace OpenBcf.Core.Protocol.Dto;

internal sealed class CommentDto
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("viewpoint_guid")]
    public string? ViewpointGuid { get; set; }

    [JsonPropertyName("modified_date")]
    public DateTimeOffset? ModifiedDate { get; set; }

    [JsonPropertyName("modified_author")]
    public string? ModifiedAuthor { get; set; }
}

/// <summary>
/// Body for creating a comment. The server assigns guid and date itself, but - as with
/// <see cref="TopicWriteDto"/>'s creation_author - its CommentCreate schema requires "author"
/// from the client; omitting it fails with 422 "Field required".
/// </summary>
internal sealed class CommentWriteDto
{
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("viewpoint_guid")]
    public string? ViewpointGuid { get; set; }
}
