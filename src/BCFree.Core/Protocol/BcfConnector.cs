using BCFree.Core.Model;

namespace BCFree.Core.Protocol;

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

        var versions = await discoveryClient.GetServerVersionsAsync(cancellationToken);
        var bcfVersion = versions.FirstOrDefault(v => v.ApiId == "bcf")
            ?? throw new InvalidOperationException($"{serverUrl} does not advertise a BCF API version.");
        var versionId = bcfVersion.VersionId;

        // Picks HTTP Basic, OAuth2 (with a browser sign-in if required), or anonymous access
        // based on what the server actually advertises - never assumes a "password" grant.
        connection = await BcfAuthenticationResolver.AuthenticateAsync(discoveryClient, connection, versionId, username, password, cancellationToken);

        using var client = new BcfServerClient(connection);
        var projects = await client.GetProjectsAsync(versionId, cancellationToken);
        var project = await BcfProjectResolver.ResolveAsync(client.BaseUrl, modelKey, projects, pickProjectAsync);

        return new BcfActiveSession(connection, versionId, project, modelKey);
    }
}
