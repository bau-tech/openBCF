using OpenBcf.ArchiCad29.Helper.Ipc;
using OpenBcf.Core.Configuration;
using OpenBcf.Core.Model;
using OpenBcf.Core.Protocol;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.ArchiCad29.Helper.Bindings;

/// <summary>
/// Drives <see cref="BcfConnector"/> from the frontend's "Connect" form - mirrors
/// OpenBcf.Rhino8.Client.Bindings.BcfSessionBinding, except Connect/project-pick is split into two
/// independent, non-nested bridge calls (Connect, then CompleteConnect) instead of one call that
/// blocks mid-flight waiting for a second call to answer it - see CompleteConnect's doc comment for
/// why. The model key is the active plan file's display name, fetched over the callbacks pipe via
/// <see cref="NativeCallbacksClient"/> since this process has no direct ACAPI access - the same role
/// RhinoDoc.Path/Tekla's model path play on the other clients.
/// </summary>
public sealed class BcfSessionBinding : IBinding
{
    private sealed record PendingConnect(Uri ServerUrl, string? Username, string? Password, string ModelKey);

    // Thrown synchronously from the pickProjectAsync callback BcfConnector.ConnectAsync invokes -
    // propagates out of ConnectAsync exactly like an awaited faulted Task would, letting Connect()
    // capture the offered projects without ever blocking on a second, later call to unblock it.
    private sealed class NeedsProjectPickSignal(IReadOnlyList<BcfProject> projects, string? previousProjectId) : Exception
    {
        public IReadOnlyList<BcfProject> Projects { get; } = projects;
        public string? PreviousProjectId { get; } = previousProjectId;
    }

    private PendingConnect? _pendingConnect;

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
    /// returning users aren't asked to log in again. The picker callback here
    /// (<see cref="PickPreviousProjectSilently"/>) always resolves immediately without waiting on
    /// anything external, so - unlike Connect()/CompleteConnect() - this never risks the "one native
    /// call blocked waiting for a second to unblock it" problem those are split to avoid.
    /// </summary>
    public async Task<object?> TryAutoConnect()
    {
        var settings = BcfSettings.Load();
        if (settings.Username is not { Length: > 0 } || settings.Password is not { Length: > 0 })
            return null;

        try
        {
            var modelKey = NativeCallbacksClient.GetActiveProjectName();
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

    /// <summary>
    /// Discovers the server's BCF projects and either finishes immediately (server has exactly the
    /// project already mapped to this model - not currently exercised, since the picker callback
    /// below always signals a pick is needed) or returns { needsProjectPick, projects,
    /// previousProjectId } for the frontend to show a picker - see CompleteConnect for the second
    /// half of this flow.
    /// </summary>
    public async Task<object> Connect(string serverUrl, string? username, string? password)
    {
        var modelKey = NativeCallbacksClient.GetActiveProjectName();
        var uri = new Uri(serverUrl);

        try
        {
            var session = await BcfConnector.ConnectAsync(uri, username, password, modelKey, SignalProjectPickNeeded).ConfigureAwait(false);
            return FinalizeSession(session, username, password);
        }
        catch (NeedsProjectPickSignal signal)
        {
            _pendingConnect = new PendingConnect(uri, username, password, modelKey);
            return new
            {
                needsProjectPick = true,
                projects = signal.Projects.Select(p => new { id = p.ProjectId, name = p.Name }),
                previousProjectId = signal.PreviousProjectId,
            };
        }
    }

    /// <summary>
    /// Finishes a Connect() call that returned needsProjectPick - deliberately a brand new,
    /// top-level bridge call rather than Connect() itself blocking mid-flight until a later call
    /// answers it (the design every other client's BcfSessionBinding uses, via a
    /// TaskCompletionSource a WebView2-hosted ResolveProjectPick call resolves). That pattern
    /// relies on being able to make a second native-JS call while the first is still pending, which
    /// ArchiCAD's DG::Browser/ACAPI bridge does not reliably support - confirmed live
    /// (REDACTED-internal-ip, 2026-08-12): even with each binding method registered as its own
    /// independent JS::Function (see BcfPalette.cpp's kBindingMethods), a call to
    /// window.bcfSessionBinding.ResolveProjectPick made while window.bcfSessionBinding.Connect's
    /// own native call was still in flight never reached this process's native callback at all - no
    /// error, just silently never delivered. Re-running discovery+auth+project-fetch here (rather
    /// than trying to resume Connect()'s suspended call) costs an extra couple of HTTP round trips
    /// but sidesteps that whole class of problem entirely.
    /// </summary>
    public async Task<object> CompleteConnect(string? projectId)
    {
        var pending = _pendingConnect ?? throw new InvalidOperationException("No connection attempt is awaiting a project pick.");
        _pendingConnect = null;

        var session = await BcfConnector.ConnectAsync(pending.ServerUrl, pending.Username, pending.Password, pending.ModelKey,
            (projects, _) => Task.FromResult(projectId is null ? null : projects.FirstOrDefault(p => p.ProjectId == projectId))).ConfigureAwait(false);

        return FinalizeSession(session, pending.Username, pending.Password);
    }

    private static object FinalizeSession(BcfActiveSession session, string? username, string? password)
    {
        BcfSession.Set(session, new BcfServerClient(session.Connection));
        new BcfSettings(session.Connection.BaseUrl, username, password).Save();

        return new
        {
            serverUrl = session.Connection.BaseUrl.ToString(),
            projectId = session.Project.ProjectId,
            projectName = session.Project.Name,
        };
    }

    private static Task<BcfProject?> SignalProjectPickNeeded(IReadOnlyList<BcfProject> projects, string? previousProjectId) =>
        throw new NeedsProjectPickSignal(projects, previousProjectId);
}
