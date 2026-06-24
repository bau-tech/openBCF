using System.Text.Json.Serialization;

namespace BCFree.Core.Protocol.Dto;

internal sealed class ServerVersionsResponseDto
{
    [JsonPropertyName("versions")]
    public List<ServerVersionDto> Versions { get; set; } = new();
}

internal sealed class ServerVersionDto
{
    /// <summary>
    /// buildingSMART servers list every API family they expose (e.g. "foundation", "bcf") in
    /// the same array, so callers must filter on this rather than assume entry 0 is BCF.
    /// </summary>
    [JsonPropertyName("api_id")]
    public string? ApiId { get; set; }

    [JsonPropertyName("version_id")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("detailed_version")]
    public string? DetailedVersion { get; set; }
}

internal sealed class AuthOptionsDto
{
    [JsonPropertyName("http_basic_supported")]
    public bool HttpBasicSupported { get; set; }

    [JsonPropertyName("oauth2_auth_url")]
    public string? OAuth2AuthorizationUrl { get; set; }

    [JsonPropertyName("oauth2_token_url")]
    public string? OAuth2TokenUrl { get; set; }

    [JsonPropertyName("oauth2_dynamic_client_reg_url")]
    public string? OAuth2DynamicClientRegistrationUrl { get; set; }

    [JsonPropertyName("supported_oauth2_flows")]
    public List<string>? SupportedOAuth2Flows { get; set; }
}

internal sealed class TokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}
