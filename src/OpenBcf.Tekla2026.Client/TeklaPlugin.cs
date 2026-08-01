using Tekla.Structures.Plugins;

namespace OpenBcf.Tekla2026.Client;

/// <summary>
/// Registers openBCF as a real Tekla plugin, loaded from extensions\openBCF\ (see
/// DeployToTeklaExtensions in OpenBcf.Tekla2026.Client.csproj) - replaces the old macro-based
/// entry point with the same pattern Speckle's Tekla connector uses: shows up in Tekla's
/// "Applications &amp; Components" catalog without a macro file to maintain, and is invoked
/// directly via "Plugin.CatalogPluginComponentItem?openBCF" from the ribbon button (see
/// TeklaEnvironment/OpenBcf-Ribbon.xml). Tekla instantiates <see cref="Dui.BcfDuiHostForm"/> - the
/// type named below - by reflection once this plugin runs.
/// </summary>
[Plugin("openBCF")]
[PluginUserInterface("OpenBcf.Tekla2026.Client.Dui.BcfDuiHostForm")]
[InputObjectDependency(InputObjectDependency.NOT_DEPENDENT)]
public class TeklaPlugin : PluginBase
{
    public override bool Run(List<InputDefinition> Input) => true;

    public override List<InputDefinition> DefineInput() => new();
}
