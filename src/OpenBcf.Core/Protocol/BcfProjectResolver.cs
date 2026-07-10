using OpenBcf.Core.Configuration;
using OpenBcf.Core.Model;

namespace OpenBcf.Core.Protocol;

/// <summary>
/// Picks which BCF project this host model syncs with. Always asks the caller-supplied picker -
/// this runs from the explicit "Connect / Sync" action, which is exactly the moment a user
/// expects to be able to choose or change the project, not have it silently reused. The project
/// last picked for this exact (server, model) pair is passed to the picker as a default
/// selection, and the new choice (which may just be confirming the same one) is what gets
/// remembered for next time.
/// </summary>
public static class BcfProjectResolver
{
    public static async Task<BcfProject> ResolveAsync(
        Uri serverUrl,
        string modelKey,
        IReadOnlyList<BcfProject> projects,
        Func<IReadOnlyList<BcfProject>, string?, Task<BcfProject?>> pickAsync)
    {
        if (projects.Count == 0)
            throw new InvalidOperationException($"{serverUrl} has no BCF projects to sync with.");

        var store = BcfProjectMappingStore.Load();
        var previousProjectId = store.TryGetProjectId(serverUrl, modelKey);

        var picked = await pickAsync(projects, previousProjectId).ConfigureAwait(false)
            ?? throw new OperationCanceledException("No BCF project was selected to sync with.");

        store.SetProjectId(serverUrl, modelKey, picked.ProjectId);
        return picked;
    }
}
