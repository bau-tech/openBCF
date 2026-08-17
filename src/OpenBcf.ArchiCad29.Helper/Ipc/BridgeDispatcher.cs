using System.Reflection;
using System.Text.Json;
using OpenBcf.Dui.Bindings;

namespace OpenBcf.ArchiCad29.Helper.Ipc;

/// <summary>
/// Invokes a single method on an <see cref="IBinding"/> by name, given a JSON args array - the same
/// reflection dispatch <see cref="OpenBcf.Dui.Bridge.BrowserBridge"/> does for the WebView2-hosted
/// clients, reimplemented here rather than reused because the delivery shape is different: ACAPI's
/// RegisterAsynchJSObject already gives the calling JS a real native Promise (see BcfPalette.cpp),
/// so BridgeServer can just await the result and hand it straight back as this call's return value,
/// instead of BrowserBridge.RunMethod's fire-and-forget + later script-injected delivery (which
/// exists only because WebView2's synchronous COM RunMethod call has no way to return a not-yet-
/// ready async result).
/// </summary>
internal static class BridgeDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<string> InvokeAsync(IBinding binding, string methodName, string argsJson)
    {
        try
        {
            var method = binding.GetType()
                .GetMethods()
                .FirstOrDefault(m => !m.IsSpecialName && m.DeclaringType != typeof(object) && m.Name == methodName);
            if (method is null)
            {
                throw new ArgumentException($"Binding '{binding.Name}' has no method '{methodName}'.");
            }

            var parameters = method.GetParameters();
            var argElements = JsonSerializer.Deserialize<JsonElement[]>(argsJson, JsonOptions) ?? [];
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = i < argElements.Length ? argElements[i].Deserialize(parameters[i].ParameterType, JsonOptions) : null;
            }

            var returnValue = method.Invoke(binding, args);
            var result = returnValue switch
            {
                Task<object?> objectTask => await objectTask.ConfigureAwait(false),
                Task task => await AwaitAndUnwrapAsync(task).ConfigureAwait(false),
                _ => returnValue,
            };

            return JsonSerializer.Serialize(new { isError = false, result }, JsonOptions);
        }
        catch (Exception ex)
        {
            var message = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
            return JsonSerializer.Serialize(new { isError = true, message }, JsonOptions);
        }
    }

    private static async Task<object?> AwaitAndUnwrapAsync(Task task)
    {
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        // Plain (non-generic) Task has no Result; Task<T>'s Result is what we want to send back.
        return resultProperty?.Name == "Result" && task.GetType().IsGenericType ? resultProperty.GetValue(task) : null;
    }
}
