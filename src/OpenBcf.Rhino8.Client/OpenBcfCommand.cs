using Rhino.Commands;

namespace OpenBcf.Rhino8.Client;

/// <summary>
/// Typed at Rhino's command line ("openBCF") to show/hide the panel - the closest Rhino
/// equivalent to Tekla's ribbon button or ARCHICAD's menu item, since a custom toolbar button
/// would need its own .rui workspace file to define (a reasonable follow-up, not required for the
/// panel to be usable - Rhino's command line is a first-class, always-available entry point).
/// </summary>
[System.Runtime.InteropServices.Guid("D1E6C4F1-5A3E-4B8C-9C2A-8F3B6C1D9E04")]
public sealed class OpenBcfCommand : Command
{
    public override string EnglishName => "openBCF";

    protected override Result RunCommand(Rhino.RhinoDoc doc, RunMode mode)
    {
        var panelType = typeof(Dui.BcfDuiPanelHost);
        if (Rhino.UI.Panels.IsPanelVisible(panelType))
            Rhino.UI.Panels.ClosePanel(panelType);
        else
            Rhino.UI.Panels.OpenPanel(panelType);

        return Result.Success;
    }
}
