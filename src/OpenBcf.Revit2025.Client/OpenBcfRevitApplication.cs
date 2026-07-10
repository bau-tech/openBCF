using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using OpenBcf.Revit2025.Client.Dui;

namespace OpenBcf.Revit2025.Client;

public class OpenBcfRevitApplication : IExternalApplication
{
    private const string PanelName = "openBCF";

    public Result OnStartup(UIControlledApplication application)
    {
        // OnStartup runs before Revit's UIApplication exists, but bindings constructed later
        // (see BcfDuiPaneProvider) need one to read the active document. ApplicationInitialized
        // is the standard Revit idiom for picking one up outside of a command context.
        application.ControlledApplication.ApplicationInitialized += (sender, _) =>
            RevitContext.Capture(new UIApplication((Autodesk.Revit.ApplicationServices.Application)sender!));

        // ExternalEvent.Create requires a valid Revit API context - OnStartup is one, the
        // ApplicationInitialized handler above isn't guaranteed to be (and its own first call is
        // off in the future anyway) - so this is built here, eagerly, on the main thread.
        RevitContext.InitializeExternalEvents();

        var panel = application.CreateRibbonPanel(PanelName);
        var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var iconPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "Resources", "BCF-icon.png");
        var largeIcon = LoadIcon(iconPath, 32);
        var smallIcon = LoadIcon(iconPath, 16);

        var openPaneButtonData = new PushButtonData(
            "ShowOpenBcfDuiPane",
            "openBCF",
            assemblyPath,
            typeof(ShowBcfDuiPaneCommand).FullName);
        openPaneButtonData.ToolTip = "Open the openBCF panel (DUI3 WebView2 UI).";
        openPaneButtonData.LargeImage = largeIcon;
        openPaneButtonData.Image = smallIcon;
        panel.AddItem(openPaneButtonData);

        var devToolsButtonData = new PushButtonData(
            "ShowOpenBcfDuiDevTools",
            "openBCF\nDevTools",
            assemblyPath,
            typeof(ShowBcfDuiDevToolsCommand).FullName);
        devToolsButtonData.ToolTip = "Open WebView2 DevTools for the openBCF panel (debugging aid).";
        devToolsButtonData.LargeImage = largeIcon;
        devToolsButtonData.Image = smallIcon;
        panel.AddItem(devToolsButtonData);

        // SetupDockablePane only ever returns a lightweight placeholder (see
        // BcfDuiPaneProvider); the real WebView2 control is built lazily by
        // ShowBcfDuiPaneCommand, the first time the user actually opens the pane. Building it any
        // earlier - e.g. eagerly here or on the first Idling tick - risks doing so before any
        // document is open. Revit rebuilds its dockable-pane hosting layout once a document
        // loads, which orphans a WebView2 child window created against the pre-document host and
        // crashes Revit's draw thread the moment the new document's view renders.
        application.RegisterDockablePane(BcfDuiPaneProvider.PaneId, "openBCF", new BcfDuiPaneProvider());

        // If the pane was already open from a previous session, Revit auto-restores it as a
        // document loads without ever going through ShowBcfDuiPaneCommand - it just appears,
        // already black, with no Show() call of ours to piggyback the repaint nudge on. This is
        // the equivalent nudge for that path; RefreshVisibility no-ops if the pane was never
        // created (control still null).
        application.ControlledApplication.DocumentOpened += (_, _) => BcfDuiPaneProvider.RefreshVisibility();

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    // DecodePixelWidth forces WPF to rasterize at the target ribbon size instead of loading the
    // source PNG at full resolution and letting Revit's own scaling blur it - the same fix
    // applied on the Tekla ribbon side, where relying on the host to downscale a large source
    // produced a distorted icon (see project memory).
    private static BitmapImage LoadIcon(string path, int pixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = pixelWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
