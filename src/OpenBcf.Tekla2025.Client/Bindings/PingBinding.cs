using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.Tekla2025.Client.Bindings;

/// <summary>
/// Phase 0 proof of concept for the DUI3-style bridge - proves the WebView2 + virtual-host-mapping
/// + JSON-RPC bridge plumbing round-trips a real call before any actual BCF feature is built on it.
/// </summary>
public sealed class PingBinding : IBinding
{
    public PingBinding(IBrowserBridge parent)
    {
        Parent = parent;
    }

    public string Name => "pingBinding";

    public IBrowserBridge Parent { get; }

    public Task<string> Ping(string message) => Task.FromResult($"Tekla add-in received: \"{message}\"");

    public Task<string> GetHostName() => Task.FromResult("Tekla");
}
