using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OpenBcf.Revit2025.Client.Dui;

/// <summary>
/// Debugging aid while the DUI3 bridge is still being built out - opens WebView2's DevTools
/// window directly, since right-click "Inspect" is unreliable inside Revit's DockablePane hosting.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ShowBcfDuiDevToolsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var control = BcfDuiPaneProvider.Control;
        if (control is null)
        {
            TaskDialog.Show("openBCF", "Open the \"openBCF\" pane first.");
            return Result.Succeeded;
        }

        control.ShowDevTools();
        return Result.Succeeded;
    }
}
