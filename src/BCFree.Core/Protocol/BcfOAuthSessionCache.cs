using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using BCFree.Core.Configuration;

namespace BCFree.Core.Protocol;

internal sealed record CachedOAuthSession(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string? RefreshToken,
    string ClientId,
    string? ClientSecret,
    string TokenEndpointAuthMethod,
    string RedirectUri);

/// <summary>
/// In-memory, per-process cache of OAuth2 sessions keyed by server base URL. Avoids re-running
/// dynamic client registration and the interactive browser sign-in on every publish/browse
/// action within the same Revit or Tekla session. Nothing here is written to disk - tokens and
/// client secrets never outlive the host process.
/// </summary>
internal static class BcfOAuthSessionCache
{
    private static readonly ConcurrentDictionary<string, CachedOAuthSession> Sessions = new();
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openBCF", "oauth-sessions.json");

    public static CachedOAuthSession? TryGet(Uri serverBaseUrl)
    {
        var key = serverBaseUrl.ToString();
        if (Sessions.TryGetValue(key, out var session))
            return session;

        var persisted = LoadPersistedSessions();
        if (!persisted.TryGetValue(key, out var dto))
            return null;

        session = dto.ToSession();
        Sessions[key] = session;
        return session;
    }

    public static void Set(Uri serverBaseUrl, CachedOAuthSession session)
    {
        var key = serverBaseUrl.ToString();
        Sessions[key] = session;

        var persisted = LoadPersistedSessions();
        persisted[key] = PersistedOAuthSession.FromSession(session);
        SavePersistedSessions(persisted);
    }

    private static Dictionary<string, PersistedOAuthSession> LoadPersistedSessions()
    {
        if (!File.Exists(CachePath))
            return new Dictionary<string, PersistedOAuthSession>();

        try
        {
            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<Dictionary<string, PersistedOAuthSession>>(json)
                ?? new Dictionary<string, PersistedOAuthSession>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Dictionary<string, PersistedOAuthSession>();
        }
    }

    private static void SavePersistedSessions(Dictionary<string, PersistedOAuthSession> sessions)
    {
        var directory = Path.GetDirectoryName(CachePath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(sessions);
        File.WriteAllText(CachePath, json);
    }

    private sealed class PersistedOAuthSession
    {
        [JsonPropertyName("access_token")]
        public string? ProtectedAccessToken { get; set; }

        [JsonPropertyName("expires_at_utc")]
        public DateTimeOffset ExpiresAtUtc { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? ProtectedRefreshToken { get; set; }

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("client_secret")]
        public string? ProtectedClientSecret { get; set; }

        [JsonPropertyName("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; } = "client_secret_basic";

        [JsonPropertyName("redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;

        public CachedOAuthSession ToSession() => new(
            ProtectedSecret.Unprotect(ProtectedAccessToken) ?? string.Empty,
            ExpiresAtUtc,
            ProtectedSecret.Unprotect(ProtectedRefreshToken),
            ClientId,
            ProtectedSecret.Unprotect(ProtectedClientSecret),
            TokenEndpointAuthMethod,
            RedirectUri);

        public static PersistedOAuthSession FromSession(CachedOAuthSession session) => new()
        {
            ProtectedAccessToken = ProtectedSecret.Protect(session.AccessToken),
            ExpiresAtUtc = session.ExpiresAtUtc,
            ProtectedRefreshToken = ProtectedSecret.Protect(session.RefreshToken),
            ClientId = session.ClientId,
            ProtectedClientSecret = ProtectedSecret.Protect(session.ClientSecret),
            TokenEndpointAuthMethod = session.TokenEndpointAuthMethod,
            RedirectUri = session.RedirectUri,
        };
    }
}
