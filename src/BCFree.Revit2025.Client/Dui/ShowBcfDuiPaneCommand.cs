using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCFree.Revit2025.Client.Dui;

[Transaction(TransactionMode.Manual)]
public sealed class ShowBcfDuiPaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        commandData.Application.GetDockablePane(BcfDuiPaneProvider.PaneId).Show();
        return Result.Succeeded;
    }
}
