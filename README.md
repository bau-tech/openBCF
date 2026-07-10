# openBCF

A free, open BCF (BIM Collaboration Format) client for Revit and Tekla Structures, built against
the [buildingSMART BCF REST API](https://github.com/buildingSMART/BCF-API) so it works against any
compliant BCF server rather than a single vendor's platform.

BCF is the open standard BIM tools use to exchange coordination issues ("topics") — a title,
status, comments, and a 3D viewpoint/snapshot pinned to a specific camera position and set of
visible/selected model elements — without needing everyone on the same platform or the same
authoring tool. openBCF lets Revit and Tekla users connect to a shared BCF server, browse existing
issues, and create new ones with a real camera viewpoint captured straight from the model.

## Download

[**Download the installer**](https://github.com/bau-tech/openBCF/releases/latest) — a single
`openBCF-Setup.exe` that auto-detects Revit 2025 and/or Tekla Structures 2025.0 and installs the
matching add-in(s). No admin rights required.

## Status

Personal, in-development project. Functional end-to-end (connect → browse issues → create an
issue with a captured viewpoint) but not hardened for production use: no BCF server auth flow
beyond username/password, limited error handling, and both host integrations are tested by hand
rather than through CI.

## Architecture

The UI is built once, in web technology, and hosted inside both CAD/BIM tools via WebView2 — the
same "DUI3" pattern [Speckle](https://speckle.systems/) uses for its own multi-host connectors.

| Project | Target | Purpose |
|---|---|---|
| `src/OpenBcf.Core` | net8.0 / net48 | BCF 2.1 data model, XML (de)serialization for `.bcfzip` archives, and a full client for the BCF REST API (projects, topics, comments, viewpoints, snapshots, documents, events). |
| `src/OpenBcf.Dui` | net8.0 / net48 | Host-agnostic WebView2 bridge: reflection-based JSON-RPC between the .NET host and the JS frontend, independent of which CAD tool is hosting it. |
| `src/OpenBcf.Dui.WebView` | net8.0 / net48 | WPF `UserControl` that actually hosts the WebView2 control and wires it to the bridge. |
| `frontend/openbcf-dui` | Vue 3 + TypeScript + Vite | The DUI3 frontend: connect form, issue list/detail, new-issue form. Built once, deployed identically into both host add-ins. |
| `src/OpenBcf.Revit2025.Client` | net8.0-windows | Revit 2025 add-in: ribbon panel, dockable WebView2 pane, viewpoint capture/apply against the active Revit view. |
| `src/OpenBcf.Tekla2025.Client` | net48 | Tekla Structures 2025.0 plugin (`[Plugin("openBCF")]`, catalog-based, real ribbon entry): floating WebView2 tool window, viewpoint capture/apply against Tekla's `ViewHandler`/`ViewCamera`. |

Both host clients reference the same `OpenBcf.Core`/`OpenBcf.Dui`/`OpenBcf.Dui.WebView` projects and
the same built frontend — the only per-host code is the thin binding layer (`Bindings/`) and
viewpoint capture/apply, since Revit and Tekla each expose the active view/camera/selection through
completely different APIs.

## Building & deploying

Both host client projects build and deploy themselves in one step — no separate copy scripts:

```powershell
dotnet build src/OpenBcf.Revit2025.Client/OpenBcf.Revit2025.Client.csproj -c Debug
dotnet build src/OpenBcf.Tekla2025.Client/OpenBcf.Tekla2025.Client.csproj -c Debug
```

Each build: builds the Vue frontend (`npm install`/`npm run build`, skipped if `dist/` is already
up to date), then copies the DLLs, `wwwroot/`, and the WebView2 native loader into the real host
location:

- **Revit**: `%APPDATA%\Autodesk\Revit\Addins\2025\` — restart Revit to pick up changes.
- **Tekla**: `C:\ProgramData\Trimble\Tekla Structures\2025.0\Environments\common\extensions\openBCF\`
  (plugin DLLs) and `...\system\Ribbons\CustomTabs\Modeling\` (ribbon tab/icon) — restart Tekla
  Structures to pick up ribbon changes.

Override the deploy locations with `-p:RevitAddinsPath=...` / `-p:TeklaCommonEnvironmentPath=...`
if Revit or Tekla is installed somewhere non-default.

**Requirements**: .NET 8 SDK, Node.js/npm, Revit 2025 (for the Revit client) and/or Tekla
Structures 2025.0 (for the Tekla client, which compiles against Tekla's Open API assemblies
directly from its install folder — no NuGet feed exists for those).

## Connecting to a BCF server

Server URL/credentials are configurable at runtime (not hardcoded) via the connect form in either
host's panel; `OpenBcf.Core`'s `BcfSettings` persists them locally per user. Any BCF REST
API-compliant server should work.
