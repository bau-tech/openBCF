; openBCF installer - packages the Release build output of the Revit, Tekla, and Rhino clients and
; deploys them straight into each host application's real plugin location (Revit's per-user
; Addins\2025 folder, Tekla's machine-wide common environment folder, Rhino's per-user Plug-ins
; folder) - the same locations OpenBcf.Revit2025.Client.csproj / OpenBcf.Tekla2025.Client.csproj /
; OpenBcf.Tekla2026.Client.csproj / OpenBcf.Rhino8.Client.csproj deploy to automatically on every
; dev build (see their DeployToRevitAddins / DeployToTeklaExtensions / DeployToRhinoPlugins
; targets). None of these products install under {app}; {app} only exists to host the uninstaller.
;
; Build Release output for the .NET clients before compiling this script:
;   dotnet build ..\src\OpenBcf.Revit2025.Client\OpenBcf.Revit2025.Client.csproj -c Release
;   dotnet build ..\src\OpenBcf.Tekla2025.Client\OpenBcf.Tekla2025.Client.csproj -c Release
;   dotnet build ..\src\OpenBcf.Tekla2026.Client\OpenBcf.Tekla2026.Client.csproj -c Release
;   dotnet build ..\src\OpenBcf.Rhino8.Client\OpenBcf.Rhino8.Client.csproj -c Release
; (the Tekla builds need a real Tekla Structures install, or -p:TeklaStructuresBinPath pointed at
; the matching version's SDK assemblies, to compile - see OpenBcf.Tekla2026.Client.csproj; the
; Rhino build needs a real Rhino 8 install, or -p:RhinoSystemPath pointed at its System folder)
; then compile with Inno Setup 6 (ISCC.exe openBCF.iss).

#define MyAppName "openBCF"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "openBCF"

[Setup]
AppId={{67EFA528-C91C-4A69-AFEE-673EAACA549E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=Open BCF client for Revit, Tekla Structures, and Rhino
VersionInfoDescription=openBCF Setup
DefaultDirName={autopf}\openBCF
DefaultGroupName=openBCF
DisableProgramGroupPage=yes
SetupIconFile=assets\openBCF.ico
WizardImageFile=assets\wizard-image.bmp
WizardSmallImageFile=assets\wizard-small-image.bmp
WizardImageStretch=no
; No admin rights required or requested, ever: the Revit path ({userappdata}) is per-user, and
; Tekla's ProgramData path has been user-writable on this machine (see project memory). Omitting
; PrivilegesRequiredOverridesAllowed means there is no dialog/commandline path to elevate either -
; Inno always emits an asInvoker manifest, so Windows will never show a UAC prompt for this
; installer. If a locked-down machine's ProgramData ACLs block the Tekla copy, that component's
; files will simply fail to copy (logged) rather than silently elevating - see the /LOG option.
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=openBCF-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={uninstallexe}

[Types]
Name: "full"; Description: "Full installation (auto-detected)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "revit"; Description: "Revit 2025 add-in"; Types: full custom
Name: "tekla2025"; Description: "Tekla Structures 2025.0 plugin"; Types: full custom
Name: "tekla2026"; Description: "Tekla Structures 2026.0 plugin"; Types: full custom
Name: "rhino8"; Description: "Rhino 8 plug-in"; Types: full custom

[Files]
; --- Revit 2025 add-in ---
; Everything except the .addin manifest goes into the addin's own subfolder; the manifest itself
; goes directly into the Addins\2025 root, which is the only place Revit scans for *.addin files.
Source: "..\src\OpenBcf.Revit2025.Client\bin\Release\net8.0-windows\*"; \
  DestDir: "{code:GetRevitAddinsDir}\OpenBcf.Revit2025.Client"; \
  Excludes: "OpenBcf.Revit2025.Client.addin"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Components: revit
Source: "..\src\OpenBcf.Revit2025.Client\bin\Release\net8.0-windows\OpenBcf.Revit2025.Client.addin"; \
  DestDir: "{code:GetRevitAddinsDir}"; Flags: ignoreversion; Components: revit

; --- Tekla Structures 2025.0 / 2026.0 plugins ---
; DLLs land in each version's own common environment's extensions\openBCF\ folder (makes the
; [Plugin("openBCF")] loadable); the ribbon tab XML + icon land in CustomTabs\Modeling\ separately
; (makes it show up in the UI - see TeklaPlugin.cs / TeklaEnvironment\OpenBcf-Ribbon.xml for why
; these are split). GetTeklaExtensionsDir/GetTeklaRibbonDir take the Tekla version string
; ("2025.0"/"2026.0") via Inno's {code:Func|Param} syntax since the two versions deploy to
; separate, version-numbered ProgramData folders.
Source: "..\src\OpenBcf.Tekla2025.Client\bin\Release\net48\*"; \
  DestDir: "{code:GetTeklaExtensionsDir|2025.0}\openBCF"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Components: tekla2025
Source: "..\src\OpenBcf.Tekla2025.Client\TeklaEnvironment\OpenBcf-Ribbon.xml"; \
  DestDir: "{code:GetTeklaRibbonDir|2025.0}"; Flags: ignoreversion; Components: tekla2025
Source: "..\src\OpenBcf.Tekla2025.Client\TeklaEnvironment\BCF-icon.png"; \
  DestDir: "{code:GetTeklaRibbonDir|2025.0}"; Flags: ignoreversion; Components: tekla2025

Source: "..\src\OpenBcf.Tekla2026.Client\bin\Release\net48\*"; \
  DestDir: "{code:GetTeklaExtensionsDir|2026.0}\openBCF"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Components: tekla2026
Source: "..\src\OpenBcf.Tekla2026.Client\TeklaEnvironment\OpenBcf-Ribbon.xml"; \
  DestDir: "{code:GetTeklaRibbonDir|2026.0}"; Flags: ignoreversion; Components: tekla2026
Source: "..\src\OpenBcf.Tekla2026.Client\TeklaEnvironment\BCF-icon.png"; \
  DestDir: "{code:GetTeklaRibbonDir|2026.0}"; Flags: ignoreversion; Components: tekla2026

; --- Rhino 8 plug-in ---
; Rhino only auto-loads per-user plugin folders named "<name> (<plugin-guid>)" - confirmed live
; via process module inspection, not assumption (see RhinoPlugin.cs's [Guid] attribute, which
; this folder's guid must match) - same shape as OpenBcf.Rhino8.Client.csproj's own
; DeployToRhinoPlugins dev-build target.
Source: "..\src\OpenBcf.Rhino8.Client\bin\Release\net48\*"; \
  DestDir: "{code:GetRhinoPluginsDir}\OpenBcf.Rhino8.Client (FC15C4D1-F0BF-49E5-AA7D-B6692D79B056)"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Components: rhino8

[Icons]
Name: "{group}\Uninstall openBCF"; Filename: "{uninstallexe}"

[Code]
function GetRevitAddinsDir(Param: String): String;
begin
  Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2025');
end;

function GetTeklaCommonEnvDir(Version: String): String;
begin
  Result := ExpandConstant('{commonappdata}\Trimble\Tekla Structures\' + Version + '\Environments\common');
end;

function GetTeklaExtensionsDir(Version: String): String;
begin
  Result := GetTeklaCommonEnvDir(Version) + '\extensions';
end;

function GetTeklaRibbonDir(Version: String): String;
begin
  Result := GetTeklaCommonEnvDir(Version) + '\system\Ribbons\CustomTabs\Modeling';
end;

function GetRhinoPluginsDir(Param: String): String;
begin
  Result := ExpandConstant('{userappdata}\McNeel\Rhinoceros\8.0\Plug-ins');
end;

function RevitDetected(): Boolean;
begin
  Result := DirExists(ExpandConstant('{pf64}\Autodesk\Revit 2025'));
end;

function TeklaVersionDetected(Version: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{pf64}\Tekla Structures\' + Version)) or DirExists(GetTeklaCommonEnvDir(Version));
end;

function RhinoDetected(): Boolean;
begin
  Result := DirExists(ExpandConstant('{pf64}\Rhino 8'));
end;

procedure InitializeWizard();
var
  Selected: String;
begin
  Selected := '';
  if RevitDetected() then
    Selected := Selected + 'revit';
  if TeklaVersionDetected('2025.0') then
  begin
    if Selected <> '' then
      Selected := Selected + ',';
    Selected := Selected + 'tekla2025';
  end;
  if TeklaVersionDetected('2026.0') then
  begin
    if Selected <> '' then
      Selected := Selected + ',';
    Selected := Selected + 'tekla2026';
  end;
  if RhinoDetected() then
  begin
    if Selected <> '' then
      Selected := Selected + ',';
    Selected := Selected + 'rhino8';
  end;
  // Leaves everything unchecked if nothing is detected, rather than blindly installing into
  // folders for products that aren't there - NextButtonClick below still lets the user force it
  // manually.
  WizardSelectComponents(Selected);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and (not WizardIsComponentSelected('revit'))
    and (not WizardIsComponentSelected('tekla2025')) and (not WizardIsComponentSelected('tekla2026'))
    and (not WizardIsComponentSelected('rhino8')) then
  begin
    MsgBox('Select at least one component to install.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectComponents then
  begin
    if (not RevitDetected()) and (not TeklaVersionDetected('2025.0')) and (not TeklaVersionDetected('2026.0'))
      and (not RhinoDetected()) then
      MsgBox('Neither Revit 2025, Tekla Structures 2025.0/2026.0, nor Rhino 8 was detected on this machine.' + #13#10 +
        'You can still select a component to install it anyway (e.g. to prepare ahead of installing the host application).',
        mbInformation, MB_OK);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  MsgBox('If Revit, Tekla Structures, or Rhino is currently running, close it first so the add-in files can be removed.',
    mbInformation, MB_OK);
  Result := True;
end;
