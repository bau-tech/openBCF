using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace OpenBcf.Revit2025.Client;

/// <summary>
/// Marshals an action back onto Revit's main thread from any other thread. Needed because
/// binding methods that await something (an HTTP call, etc.) before touching the Revit API
/// resume on a thread-pool thread - every await in this project uses ConfigureAwait(false) - and
/// the Revit API requires being called from the same thread Revit's own message loop runs on;
/// off-thread reads like <see cref="Autodesk.Revit.UI.UIDocument.ActiveView"/> don't reliably
/// throw, they can just return wrong/stale data instead. <see cref="ExternalEvent"/> is Revit's
/// documented mechanism for getting back onto that thread from arbitrary calling contexts.
/// </summary>
public sealed class RevitExternalEventRunner : IExternalEventHandler
{
    private readonly ExternalEvent _externalEvent;
    private readonly ConcurrentQueue<(Action<UIApplication> Action, TaskCompletionSource<object?> Completion)> _pending = new();

    public RevitExternalEventRunner()
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public Task RunAsync(Action<UIApplication> action)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((action, completion));
        _externalEvent.Raise();
        return completion.Task;
    }

    public void Execute(UIApplication app)
    {
        while (_pending.TryDequeue(out var item))
        {
            try
            {
                item.Action(app);
                item.Completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }
    }

    public string GetName() => "openBCF";
}
