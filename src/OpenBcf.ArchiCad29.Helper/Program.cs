using OpenBcf.ArchiCad29.Helper.Bindings;
using OpenBcf.ArchiCad29.Helper.Http;
using OpenBcf.ArchiCad29.Helper.Ipc;
using OpenBcf.Dui.Bindings;
using OpenBcf.Dui.Bridge;

namespace OpenBcf.ArchiCad29.Helper;

/// <summary>
/// Entry point for the out-of-process openBCF ArchiCAD 29 helper - launched by
/// ../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.cpp's LaunchHelperProcess, passed the
/// ArchiCAD process's PID (used to derive both pipe names and the local HTTP port - see
/// Ipc/MessageTypes.cs and Http/StaticFileServer.cs).
///
/// Headless by design: this process owns no window of its own. ArchiCAD's own native DG::Browser
/// control (BcfPalette.cpp) hosts the actual UI, loading this process's StaticFileServer over HTTP
/// and calling back into BridgeServer for every binding method - see
/// ../OpenBcf.ArchiCad29.NativeAddOn/Src/HelperProcess.h's header comment for why an earlier
/// WPF/WebView2-owning version of this process was replaced (it deadlocked ArchiCAD by requiring a
/// foreign-process window to be reparented into ArchiCAD's palette).
///
/// [STAThread] is still required: Microsoft.Win32.SaveFileDialog/OpenFileDialog
/// (BcfArchiveBinding.ExportToFile/ImportFromFile) need an STA COM apartment on the calling thread,
/// even though no WPF Application/Dispatcher/Window is ever created here.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var archiCadPid = ParseArchiCadPid(args);
        NativeCallbacksClient.ArchiCadPid = archiCadPid;

        var bindings = CreateBindings();

        new StaticFileServer(HttpPort(archiCadPid)).Start();
        new BridgeServer(archiCadPid, bindings).Start();

        // This process's whole lifetime is meant to outlive any number of the native Add-On's
        // Initialize()/FreeData() cycles (see HelperProcess.h) - nothing here ever signals this,
        // it just runs until ArchiCAD (and therefore this process's reason to exist) goes away.
        Thread.Sleep(Timeout.Infinite);
    }

    private static IReadOnlyList<IBinding> CreateBindings()
    {
        IBrowserBridge NewBridge(string name)
        {
            var bridge = new BrowserBridge(new NativeScriptExecutor());
            return bridge;
        }

        var pingBinding = new PingBinding(NewBridge("pingBinding"));
        var sessionBinding = new BcfSessionBinding(NewBridge("bcfSessionBinding"));
        var issueBinding = new BcfIssueBinding(NewBridge("bcfIssueBinding"));
        var archiveBinding = new BcfArchiveBinding(NewBridge("bcfArchiveBinding"));

        IBinding[] bindings = [pingBinding, sessionBinding, issueBinding, archiveBinding];
        foreach (var binding in bindings)
        {
            // Needed only so BrowserBridge.Send can fill in FrontendBoundName - RunMethod is never
            // called through these bridges (see NativeScriptExecutor's header comment), only Send.
            (binding.Parent as BrowserBridge)?.AssociateWithBinding(binding);
        }

        return bindings;
    }

    private static int HttpPort(int archiCadPid) => 20000 + (archiCadPid % 20000);

    private static int ParseArchiCadPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--archicad-pid" && int.TryParse(args[i + 1], out var pid))
                return pid;
        }

        throw new ArgumentException("Missing required --archicad-pid <pid> argument.");
    }
}
