using Autodesk.Revit.UI;

namespace OpenBcf.Revit2025.Client;

/// <summary>
/// Holds the process-wide <see cref="UIApplication"/> so bindings (constructed lazily, outside
/// any command context - see <see cref="Dui.BcfDuiPaneProvider"/>) can still reach the active
/// document. Captured once via <see cref="Autodesk.Revit.ApplicationServices.ControlledApplication.ApplicationInitialized"/>
/// in <see cref="OpenBcfRevitApplication.OnStartup"/>.
/// </summary>
public static class RevitContext
{
    public static UIApplication? Current { get; private set; }

    /// <summary>
    /// Marshals Revit-API calls back onto the main thread for binding methods that need to touch
    /// the API after an awaited call (see <see cref="RevitExternalEventRunner"/>).
    /// <see cref="ExternalEvent.Create"/> requires a valid Revit API context (e.g. OnStartup,
    /// unlike the ApplicationInitialized handler below, which doesn't), so this is built eagerly
    /// in <see cref="OpenBcfRevitApplication.OnStartup"/> via <see cref="InitializeExternalEvents"/>
    /// rather than lazily on first use - first use could otherwise happen off the main thread.
    /// </summary>
    public static RevitExternalEventRunner? ExternalEvents { get; private set; }

    internal static void Capture(UIApplication application) => Current = application;

    internal static void InitializeExternalEvents() => ExternalEvents ??= new RevitExternalEventRunner();
}
