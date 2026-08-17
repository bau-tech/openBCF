namespace OpenBcf.Core.Protocol;

/// <summary>
/// Picks the right authentication mechanism for a server based on what it actually advertises
/// at <c>bcf/{version}/auth</c>, rather than assuming a username/password textbox always maps
/// to an OAuth2 "password" grant - the BCF API spec doesn't define one. HTTP Basic is used
/// directly when the server supports it; otherwise an OAuth2 authorization_code sign-in is
/// triggered when required - silently, submitting whatever username/password was typed into the
/// plugin directly to the server (see <see cref="BcfOAuthAuthorizationCodeFlow.AuthenticateWithCredentialsAsync"/>),
/// or via a browser popup if none was given (or the silent attempt doesn't recognize the
/// server's login page).
/// </summary>
public static class BcfAuthenticationResolver
{
    public static async Task<BcfServerConnection> AuthenticateAsync(
        BcfServerClient discoveryClient,
        BcfServerConnection connection,
        string versionId,
        string? username,
        string? password,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var options = await discoveryClient.GetAuthOptionsAsync(versionId, cancellationToken).ConfigureAwait(false);

        if (options.HttpBasicSupported && !string.IsNullOrWhiteSpace(username))
            return connection.WithBasicCredentials(username!, password ?? string.Empty);

        if (options.SupportsAuthorizationCodeGrant)
        {
            var token = string.IsNullOrWhiteSpace(username)
                ? await BcfOAuthAuthorizationCodeFlow.AuthenticateAsync(options, connection.BaseUrl, cancellationToken, forceRefresh).ConfigureAwait(false)
                : await BcfOAuthAuthorizationCodeFlow.AuthenticateWithCredentialsAsync(options, connection.BaseUrl, username!, password ?? string.Empty, cancellationToken, forceRefresh).ConfigureAwait(false);
            return connection.WithAccessToken(token);
        }

        if (options.SupportsPasswordGrant && !string.IsNullOrWhiteSpace(username))
        {
            var token = await discoveryClient.AuthenticateWithPasswordAsync(versionId, username!, password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return connection.WithAccessToken(token);
        }

        if (!string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException($"{connection.BaseUrl} does not support username/password sign-in directly.");

        return connection;
    }
}
