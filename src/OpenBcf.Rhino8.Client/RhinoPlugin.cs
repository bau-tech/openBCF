namespace OpenBcf.Rhino8.Client;

/// <summary>
/// Every RhinoCommon plug-in must have exactly one <see cref="Rhino.PlugIns.PlugIn"/>-derived
/// class; Rhino creates the instance itself. Registers the openBCF panel here (see
/// <see cref="Dui.BcfDuiPanelHost"/>) - <see cref="OpenBcfCommand"/> is what actually shows it,
/// the same "one entry point opens the panel" shape as Tekla's ribbon button/ARCHICAD's menu item.
/// </summary>
[System.Runtime.InteropServices.Guid("FC15C4D1-F0BF-49E5-AA7D-B6692D79B056")]
public sealed class RhinoPlugin : Rhino.PlugIns.PlugIn
{
    public RhinoPlugin()
    {
        Instance = this;
    }

    public static RhinoPlugin? Instance { get; private set; }

    protected override Rhino.PlugIns.LoadReturnCode OnLoad(ref string errorMessage)
    {
        Rhino.UI.Panels.RegisterPanel(this, typeof(Dui.BcfDuiPanelHost), "openBCF", LoadPanelIcon());
        return Rhino.PlugIns.LoadReturnCode.Success;
    }

    private static System.Drawing.Icon LoadPanelIcon()
    {
        var assembly = typeof(RhinoPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("OpenBcf.Rhino8.Client.Resources.openBCF.ico")
            ?? throw new System.IO.FileNotFoundException("Embedded panel icon resource not found.");
        return new System.Drawing.Icon(stream);
    }
}
