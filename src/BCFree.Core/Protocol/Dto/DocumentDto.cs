using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class DocumentReferenceDto
{
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("document_guid")]
    public string? DocumentGuid { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class DocumentDto
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("creation_date")]
    public DateTimeOffset? CreationDate { get; set; }

    [JsonPropertyName("creation_author")]
    public string? CreationAuthor { get; set; }

    [JsonPropertyName("modified_date")]
    public DateTimeOffset? ModifiedDate { get; set; }

    [JsonPropertyName("modified_author")]
    public string? ModifiedAuthor { get; set; }
}
