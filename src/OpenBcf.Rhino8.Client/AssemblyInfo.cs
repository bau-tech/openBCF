using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Rhino identifies a .rhp plugin's identity (PlugIn.Id) from this assembly-level Guid attribute,
// not from any attribute on the PlugIn-derived class itself - confirmed via a real "plugInId
// Can't be Guid.Empty" ArgumentException Rhino throws at load time when this is missing (SDK-style
// projects don't auto-generate one, unlike the classic .NET Framework project template). Must stay
// in sync with the folder name in RhinoPluginsPath (OpenBcf.Rhino8.Client.csproj) and
// installer/openBCF.iss.
[assembly: Guid("FC15C4D1-F0BF-49E5-AA7D-B6692D79B056")]

// Shown as "Organization" in Rhino's Options > Plug-ins page.
[assembly: PlugInDescription(DescriptionType.Organization, "bau-tech")]
