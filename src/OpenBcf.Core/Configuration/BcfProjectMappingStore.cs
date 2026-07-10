using System.Text.Json;

namespace OpenBcf.Core.Configuration;

/// <summary>
/// Remembers which BCF server project a given host model (a Revit document, a Tekla model, ...)
/// was last published to, so publishing twice from the same model always lands in the same BCF
/// project instead of silently picking whatever happens to be first in the server's project list.
/// Shared across host plugins via a single JSON file, mirroring <see cref="BcfSettings"/>.
/// </summary>
public sealed class BcfProjectMappingStore
{
    private static readonly string MappingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openBCF", "project-mappings.json");

    private readonly Dictionary<string, string> _projectIdByKey;

    private BcfProjectMappingStore(Dictionary<string, string> projectIdByKey) => _projectIdByKey = projectIdByKey;

    public static BcfProjectMappingStore Load()
    {
        if (!File.Exists(MappingsPath))
            return new BcfProjectMappingStore(new Dictionary<string, string>());

        try
        {
            var json = File.ReadAllText(MappingsPath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            return new BcfProjectMappingStore(map);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new BcfProjectMappingStore(new Dictionary<string, string>());
        }
    }

    /// <summary>
    /// <paramref name="modelKey"/> identifies the open host model (e.g. a Revit document's file
    /// path, or a Tekla model's project GUID) - it has no meaning to the BCF server itself, it
    /// just needs to be stable across publishes from the same model. The mapping is scoped per
    /// server URL too, since the same model could be published to different servers over time
    /// and a remembered project id from one server is meaningless on another.
    /// </summary>
    public string? TryGetProjectId(Uri serverUrl, string modelKey) =>
        _projectIdByKey.TryGetValue(Key(serverUrl, modelKey), out var projectId) ? projectId : null;

    public void SetProjectId(Uri serverUrl, string modelKey, string projectId)
    {
        _projectIdByKey[Key(serverUrl, modelKey)] = projectId;

        var directory = Path.GetDirectoryName(MappingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(MappingsPath, JsonSerializer.Serialize(_projectIdByKey));
    }

    private static string Key(Uri serverUrl, string modelKey) => $"{serverUrl}|{modelKey}";
}
