using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBcf.Core.Protocol;

namespace OpenBcf.Core.Configuration;

/// <summary>
/// User-configurable openBCF settings, shared across host plugins (Revit, Tekla, ...) via a
/// single JSON file under the current user's AppData folder. Never hardcode a server URL in
/// plugin code — always go through <see cref="Load"/>.
/// </summary>
public sealed record BcfSettings(Uri ServerUrl, string? Username = null, string? Password = null)
{
    public static readonly Uri DefaultServerUrl = BcfServerConnection.DefaultServerUrl;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openBCF", "settings.json");

    public static BcfSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new BcfSettings(DefaultServerUrl);

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json);
            return dto?.ServerUrl is { Length: > 0 } url
                ? new BcfSettings(new Uri(url), dto.Username, ProtectedSecret.Unprotect(dto.ProtectedPassword))
                : new BcfSettings(DefaultServerUrl);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UriFormatException)
        {
            return new BcfSettings(DefaultServerUrl);
        }
    }

    // The password is stored only as a DPAPI-protected value scoped to the current Windows user.
    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new SettingsDto
        {
            ServerUrl = ServerUrl.ToString(),
            Username = Username,
            ProtectedPassword = ProtectedSecret.Protect(Password),
        });
        File.WriteAllText(SettingsPath, json);
    }

    private sealed class SettingsDto
    {
        [JsonPropertyName("server_url")]
        public string? ServerUrl { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("protected_password")]
        public string? ProtectedPassword { get; set; }
    }
}
