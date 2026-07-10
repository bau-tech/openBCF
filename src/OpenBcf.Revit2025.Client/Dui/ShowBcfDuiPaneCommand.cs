using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OpenBcf.Revit2025.Client.Dui;

[Transaction(TransactionMode.Manual)]
public sealed class ShowBcfDuiPaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        BcfDuiPaneProvider.EnsureControlCreated();
        commandData.Application.GetDockablePane(BcfDuiPaneProvider.PaneId).Show();
        BcfDuiPaneProvider.RefreshVisibility();
        return Result.Succeeded;
    }
}
