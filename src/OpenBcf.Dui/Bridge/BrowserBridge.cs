using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenBcf.Dui.Bindings;

namespace OpenBcf.Dui.Bridge;

/// <summary>
/// Wraps exactly one <see cref="IBinding"/> and is the object actually registered with
/// <c>CoreWebView2.AddHostObjectToScript(binding.Name, bridge)</c>. Exposes a single COM-visible
/// entry point (<see cref="RunMethod"/>) that reflects into the binding's public methods, so adding
/// a new binding method never requires touching COM marshalling - only the JS-side caller and the
/// binding class itself.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class BrowserBridge : IBrowserBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IBrowserScriptExecutor _scriptExecutor;
    private IReadOnlyDictionary<string, MethodInfo> _methodCache = new Dictionary<string, MethodInfo>();
    private IBinding? _binding;

    public BrowserBridge(IBrowserScriptExecutor scriptExecutor)
    {
        _scriptExecutor = scriptExecutor;
    }

    public string FrontendBoundName { get; private set; } = "Unknown";

    public void AssociateWithBinding(IBinding binding)
    {
        _binding = binding;
        FrontendBoundName = binding.Name;
        _methodCache = binding
            .GetType()
            .GetMethods()
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public void RunMethod(string methodName, string requestId, string argsJson) =>
        _ = RunMethodAsync(methodName, requestId, argsJson);

    private async Task RunMethodAsync(string methodName, string requestId, string argsJson)
    {
        try
        {
            var result = await InvokeAsync(methodName, argsJson).ConfigureAwait(false);
            DeliverResult(requestId, isError: false, JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception ex)
        {
            var message = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
            DeliverResult(requestId, isError: true, JsonSerializer.Serialize(new { message }, JsonOptions));
        }
    }

    private async Task<object?> InvokeAsync(string methodName, string argsJson)
    {
        if (_binding is null)
        {
            throw new InvalidOperationException("Bridge was not associated with a binding.");
        }

        if (!_methodCache.TryGetValue(methodName, out var method))
        {
            throw new ArgumentException($"Binding '{FrontendBoundName}' has no method '{methodName}'.");
        }

        var parameters = method.GetParameters();
        var argElements = JsonSerializer.Deserialize<JsonElement[]>(argsJson, JsonOptions) ?? [];
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = i < argElements.Length ? argElements[i].Deserialize(parameters[i].ParameterType, JsonOptions) : null;
        }

        var returnValue = method.Invoke(_binding, args);
        return returnValue switch
        {
            Task<object?> objectTask => await objectTask.ConfigureAwait(false),
            Task task => await AwaitAndUnwrapAsync(task).ConfigureAwait(false),
            _ => returnValue,
        };
    }

    private static async Task<object?> AwaitAndUnwrapAsync(Task task)
    {
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        // Plain (non-generic) Task has no Result; Task<T>'s Result is what we want to send back.
        return resultProperty?.Name == "Result" && task.GetType().IsGenericType ? resultProperty.GetValue(task) : null;
    }

    private void DeliverResult(string requestId, bool isError, string payloadJson)
    {
        var script =
            $"window.__openbcfDuiReceiveResult && window.__openbcfDuiReceiveResult({JsonSerializer.Serialize(requestId)}, {(isError ? "true" : "false")}, {JsonSerializer.Serialize(payloadJson)})";
        _scriptExecutor.ExecuteScript(script);
    }

    public void Send(string eventName, object? payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var script =
            $"window.__openbcfDuiReceiveEvent && window.__openbcfDuiReceiveEvent({JsonSerializer.Serialize(FrontendBoundName)}, {JsonSerializer.Serialize(eventName)}, {JsonSerializer.Serialize(payloadJson)})";
        _scriptExecutor.ExecuteScript(script);
    }
}
