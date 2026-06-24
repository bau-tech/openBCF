using Autodesk.Revit.UI;
using BCFree.Revit2025.Client.Dui;

namespace BCFree.Revit2025.Client;

public class BCFreeRevitApplication : IExternalApplication
{
    private const string PanelName = "BCFree";

    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel(PanelName);
        var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        var openPaneButtonData = new PushButtonData(
            "ShowBcfreeDuiPane",
            "BCFree",
            assemblyPath,
            typeof(ShowBcfDuiPaneCommand).FullName);
        openPaneButtonData.ToolTip = "Open the BCFree panel (DUI3 WebView2 UI).";
        panel.AddItem(openPaneButtonData);

        var devToolsButtonData = new PushButtonData(
            "ShowBcfreeDuiDevTools",
            "BCFree\nDevTools",
            assemblyPath,
            typeof(ShowBcfDuiDevToolsCommand).FullName);
        devToolsButtonData.ToolTip = "Open WebView2 DevTools for the BCFree panel (debugging aid).";
        panel.AddItem(devToolsButtonData);

        application.RegisterDockablePane(BcfDuiPaneProvider.PaneId, "BCFree", new BcfDuiPaneProvider());

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
