using System.Text.Json.Serialization;

namespace OpenBcf.Core.Protocol.Dto;

internal sealed class ClientRegistrationRequestDto
{
    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = new();

    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = "openBCF";

    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = new() { "authorization_code", "refresh_token" };

    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = new() { "code" };
}

internal sealed class ClientRegistrationResponseDto
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }
}
