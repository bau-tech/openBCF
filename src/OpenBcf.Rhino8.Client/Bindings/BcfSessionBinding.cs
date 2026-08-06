using OpenBcf.Core.Configuration;
using OpenBcf.Core.Model;
using OpenBcf.Core.Protocol;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;
using Rhino;

namespace OpenBcf.Rhino8.Client.Bindings;

/// <summary>
/// Drives <see cref="BcfConnector"/> from the frontend's "Connect" form - mirrors
/// OpenBcf.Tekla2026.Client.Bindings.BcfSessionBinding. The model key is the open Rhino document's
/// file path (<see cref="RhinoDoc.Path"/>), the same role Tekla's model path or Revit's document
/// path play; an unsaved document falls back to its in-memory name.
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
    /// returning users aren't asked to log in again.
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
        // WebView2-dispatched call on the UI thread - see OpenBcf.Tekla2026.Client's
        // BcfSessionBinding for why the continuation must not run inline on that same call.
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
        var doc = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("Open a Rhino document before connecting to a BCF server.");

        return string.IsNullOrEmpty(doc.Path) ? doc.Name ?? "Untitled" : doc.Path;
    }
}
