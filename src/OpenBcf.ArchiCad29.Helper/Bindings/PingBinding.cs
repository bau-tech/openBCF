using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.ArchiCad29.Helper.Bindings;

/// <summary>Phase 0 proof of concept for the DUI3-style bridge - mirrors the other clients' PingBinding.</summary>
public sealed class PingBinding : IBinding
{
    public PingBinding(IBrowserBridge parent)
    {
        Parent = parent;
    }

    public string Name => "pingBinding";

    public IBrowserBridge Parent { get; }

    public Task<string> Ping(string message) => Task.FromResult($"ArchiCAD add-on received: \"{message}\"");

    public Task<string> GetHostName() => Task.FromResult("ArchiCAD");
}
