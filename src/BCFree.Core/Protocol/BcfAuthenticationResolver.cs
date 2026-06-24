namespace BCFree.Core.Protocol;

/// <summary>
/// Picks the right authentication mechanism for a server based on what it actually advertises
/// at <c>bcf/{version}/auth</c>, rather than assuming a username/password textbox always maps
/// to an OAuth2 "password" grant - the BCF API spec doesn't define one. HTTP Basic is used
/// directly when the server supports it; otherwise an OAuth2 authorization_code sign-in (with a
/// browser popup) is triggered automatically when required, regardless of what's typed in.
/// </summary>
public static class BcfAuthenticationResolver
{
    public static async Task<BcfServerConnection> AuthenticateAsync(
        BcfServerClient discoveryClient,
        BcfServerConnection connection,
        string versionId,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var options = await discoveryClient.GetAuthOptionsAsync(versionId, cancellationToken);

        if (options.HttpBasicSupported && !string.IsNullOrWhiteSpace(username))
            return connection.WithBasicCredentials(username!, password ?? string.Empty);

        if (options.SupportsAuthorizationCodeGrant)
        {
            var token = await BcfOAuthAuthorizationCodeFlow.AuthenticateAsync(options, connection.BaseUrl, cancellationToken);
            return connection.WithAccessToken(token);
        }

        if (options.SupportsPasswordGrant && !string.IsNullOrWhiteSpace(username))
        {
            var token = await discoveryClient.AuthenticateWithPasswordAsync(versionId, username!, password ?? string.Empty, cancellationToken);
            return connection.WithAccessToken(token);
        }

        if (!string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException($"{connection.BaseUrl} does not support username/password sign-in directly.");

        return connection;
    }
}
