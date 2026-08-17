using System.Net;
using OpenBcf.Core.Model;

namespace OpenBcf.Core.Protocol;

/// <summary>
/// Centralizes what "Connect / Sync" means: discover the server's BCF API version, authenticate,
/// fetch its projects, and let the user pick (via <see cref="BcfProjectResolver"/>) which project
/// this host model syncs with. Query Issues and Add New Issue then just reuse the resulting
/// <see cref="BcfActiveSession"/> instead of repeating discovery/auth/project-selection on every
/// action.
/// </summary>
public static class BcfConnector
{
    public static async Task<BcfActiveSession> ConnectAsync(
        Uri serverUrl,
        string? username,
        string? password,
        string modelKey,
        Func<IReadOnlyList<BcfProject>, string?, Task<BcfProject?>> pickProjectAsync,
        CancellationToken cancellationToken = default)
    {
        var connection = new BcfServerConnection(serverUrl);
        using var discoveryClient = new BcfServerClient(connection);

        var versions = await discoveryClient.GetServerVersionsAsync(cancellationToken).ConfigureAwait(false);
        var bcfVersions = versions.Where(v => v.ApiId == "bcf").ToList();
        if (bcfVersions.Count == 0)
            throw new InvalidOperationException($"{serverUrl} does not advertise a BCF API version.");

        // Servers list every version they support, not just the latest (e.g. both 2.1 and 3.0) -
        // take the highest one rather than whichever happens to come first in the response.
        var versionId = bcfVersions.OrderByDescending(v => ParseVersion(v.VersionId)).First().VersionId;

        // Picks HTTP Basic, OAuth2 (with a browser sign-in if required), or anonymous access
        // based on what the server actually advertises - never assumes a "password" grant.
        connection = await BcfAuthenticationResolver.AuthenticateAsync(discoveryClient, connection, versionId, username, password, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<BcfProject> projects;
        using (var client = new BcfServerClient(connection))
        {
            try
            {
                projects = await client.GetProjectsAsync(versionId, cancellationToken).ConfigureAwait(false);
            }
            catch (BcfHttpRequestException ex) when (ex.ResponseStatusCode == HttpStatusCode.Unauthorized)
            {
                // The server just rejected a token BcfAuthenticationResolver believed was still
                // good - e.g. a cached OAuth2 access token whose local expiry hasn't passed yet,
                // but the server itself revoked/rotated it (restart, session revocation, clock
                // drift). Retry once with forceRefresh so the OAuth flow skips that stale
                // local-expiry shortcut and goes straight to a refresh-token exchange (or a fresh
                // sign-in if that also fails) instead of surfacing a 401 the user can't act on.
                connection = await BcfAuthenticationResolver.AuthenticateAsync(discoveryClient, connection, versionId, username, password, cancellationToken, forceRefresh: true).ConfigureAwait(false);
                using var retryClient = new BcfServerClient(connection);
                projects = await retryClient.GetProjectsAsync(versionId, cancellationToken).ConfigureAwait(false);
            }

            var project = await BcfProjectResolver.ResolveAsync(connection.BaseUrl, modelKey, projects, pickProjectAsync).ConfigureAwait(false);
            return new BcfActiveSession(connection, versionId, project, modelKey);
        }
    }

    private static Version ParseVersion(string versionId) =>
        Version.TryParse(versionId, out var version) ? version : new Version(0, 0);
}
