using System.IO;
using OpenBcf.Core.Configuration;
using OpenBcf.Core.Model;
using OpenBcf.Core.Protocol;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;
// Aliased rather than `using Tekla.Structures.Model;` - that namespace also has a Task class,
// which collides with System.Threading.Tasks.Task used throughout this file.
using Model = Tekla.Structures.Model.Model;

namespace OpenBcf.Tekla2025.Client.Bindings;

/// <summary>
/// Drives <see cref="BcfConnector"/> from the frontend's "Connect" form. Project selection has no
/// dedicated C#-to-JS request channel - only push events (<see cref="IBrowserBridge.Send"/>) and
/// frontend-initiated calls exist - so <see cref="PickProjectAsync"/> pushes a
/// "projectPickRequested" event and awaits a <see cref="TaskCompletionSource{T}"/> that
/// <see cref="ResolveProjectPick"/> (called back from the frontend once the user has chosen)
/// completes.
/// </summary>
public sealed class BcfSessionBinding : IBinding
{
    private IReadOnlyList<BcfProject> _lastOfferedProjects = Array.Empty<BcfProject>();
    private TaskCompletionSource<BcfProject?>? _pendingProjectPick;

    public BcfSessionBinding(IBrowserBridge parent)
    {
        Parent = parent;
    }

    public string Name => "bcfSessionBinding";

    public IBrowserBridge Parent { get; }

    public Task<object> GetSettings()
    {
        var settings = BcfSettings.Load();
        return Task.FromResult<object>(new { serverUrl = settings.ServerUrl.ToString(), username = settings.Username });
    }

    /// <summary>
    /// Silently reconnects using the saved server/username/password and this model's previously
    /// picked project (see <see cref="BcfProjectMappingStore"/>) - called once on panel open so
    /// returning users aren't asked to log in again. Unlike <see cref="Connect"/>, there is no
    /// user interaction backing this call, so it never shows the project picker: if nothing is
    /// saved yet, the model was never mapped to a project, or anything about the reconnect fails
    /// (server unreachable, credentials rejected, ...), it just returns null and the frontend
    /// falls back to the normal Connect form instead of surfacing an error for something the user
    /// didn't explicitly ask to happen right now.
    /// </summary>
    public async Task<object?> TryAutoConnect()
    {
        var settings = BcfSettings.Load();
        if (settings.Username is not { Length: > 0 } || settings.Password is not { Length: > 0 })
            return null;

        try
        {
            var modelKey = ResolveModelKey();
            var session = await BcfConnector.ConnectAsync(settings.ServerUrl, settings.Username, settings.Password, modelKey, PickPreviousProjectSilently).ConfigureAwait(false);

            BcfSession.Set(session, new BcfServerClient(session.Connection));

            return new
            {
                serverUrl = session.Connection.BaseUrl.ToString(),
                projectId = session.Project.ProjectId,
                projectName = session.Project.Name,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Task<BcfProject?> PickPreviousProjectSilently(IReadOnlyList<BcfProject> projects, string? previousProjectId) =>
        Task.FromResult(previousProjectId is null ? null : projects.FirstOrDefault(p => p.ProjectId == previousProjectId));

    /// <summary>
    /// Drops the live session and forgets the saved password, so the panel goes back to the
    /// Connect form and TryAutoConnect won't silently sign back in on next open. The server URL
    /// and username are kept (not wiped) purely as a form-prefill convenience - Connect always
    /// overwrites all three anyway once the user (or TryAutoConnect) signs in again.
    /// </summary>
    public void Disconnect()
    {
        BcfSession.Clear();

        var settings = BcfSettings.Load();
        new BcfSettings(settings.ServerUrl, settings.Username, Password: null).Save();
    }

    public async Task<object> Connect(string serverUrl, string? username, string? password)
    {
        var modelKey = ResolveModelKey();
        var session = await BcfConnector.ConnectAsync(new Uri(serverUrl), username, password, modelKey, PickProjectAsync).ConfigureAwait(false);

        BcfSession.Set(session, new BcfServerClient(session.Connection));
        new BcfSettings(session.Connection.BaseUrl, username, password).Save();

        return new
        {
            serverUrl = session.Connection.BaseUrl.ToString(),
            projectId = session.Project.ProjectId,
            projectName = session.Project.Name,
        };
    }

    public void ResolveProjectPick(string? projectId)
    {
        var project = projectId is null
            ? null
            : _lastOfferedProjects.FirstOrDefault(p => p.ProjectId == projectId);
        _pendingProjectPick?.TrySetResult(project);
    }

    private Task<BcfProject?> PickProjectAsync(IReadOnlyList<BcfProject> projects, string? previousProjectId)
    {
        _lastOfferedProjects = projects;
        // RunContinuationsAsynchronously: ResolveProjectPick calls TrySetResult from a nested,
        // WebView2-dispatched call on the UI thread. Without this flag, the continuation of the
        // await below would run synchronously, inline, on that same call - reentering Tekla's
        // message loop in a way it doesn't reliably pump, the same hang seen on the Revit side.
        _pendingProjectPick = new TaskCompletionSource<BcfProject?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Parent.Send("projectPickRequested", new
        {
            projects = projects.Select(p => new { id = p.ProjectId, name = p.Name }),
            previousProjectId,
        });

        return _pendingProjectPick.Task;
    }

    private static string ResolveModelKey()
    {
        var model = new Model();
        if (!model.GetConnectionStatus())
            throw new InvalidOperationException("Open a Tekla Structures model before connecting to a BCF server.");

        var info = model.GetInfo();
        return string.IsNullOrEmpty(info.ModelPath) ? info.ModelName : Path.Combine(info.ModelPath, info.ModelName);
    }
}
