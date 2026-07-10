namespace OpenBcf.Core.Protocol;

public sealed record BcfServerAuthOptions(
    bool HttpBasicSupported = false,
    Uri? OAuth2AuthorizationUrl = null,
    Uri? OAuth2TokenUrl = null,
    Uri? OAuth2DynamicClientRegistrationUrl = null,
    IReadOnlyList<string>? SupportedOAuth2Flows = null)
{
    public bool SupportsPasswordGrant =>
        SupportedOAuth2Flows?.Contains("password", StringComparer.OrdinalIgnoreCase) == true;

    public bool SupportsAuthorizationCodeGrant =>
        OAuth2AuthorizationUrl is not null && OAuth2TokenUrl is not null &&
        (SupportedOAuth2Flows is null || SupportedOAuth2Flows.Contains("authorization_code_grant", StringComparer.OrdinalIgnoreCase));
}
