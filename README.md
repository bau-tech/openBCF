# openBCF

A free, open BCF (BIM Collaboration Format) client for Revit, Tekla Structures, Rhino,
and Blender (via [Bonsai](https://bonsaibim.org)), built against the
[buildingSMART BCF REST API](https://github.com/buildingSMART/BCF-API) so it works against any
compliant BCF server rather than a single vendor's platform.

BCF is the open standard BIM tools use to exchange coordination issues ("topics") — a title,
status, comments, and a 3D viewpoint/snapshot pinned to a specific camera position and set of
visible/selected model elements — without needing everyone on the same platform or the same
authoring tool. openBCF lets Revit, Tekla, Rhino, and Blender/Bonsai users connect to a
shared BCF server, browse existing issues, and create new ones with a viewpoint captured straight
from the model.

## Download

[**Download the installer**](https://github.com/bau-tech/openBCF/releases/latest) — a single
`openBCF-Setup.exe` that auto-detects Revit 2025, Tekla Structures 2025.0/2026.0, and/or Rhino 8
and installs the matching add-in(s). Windows only (Revit, Tekla, and this Rhino build don't run on
macOS). No admin rights required.

The Blender/Bonsai extension (`src/OpenBcf.Blender.Extension`) is cross-platform (Windows, macOS,
Linux) and isn't part of `openBCF-Setup.exe` either — see "Building & deploying" below for the
one-command build+install on each platform.

## Status

Personal, in-development project. Functional end-to-end (connect → browse issues → create an
issue with a captured viewpoint) but not hardened for production use: no BCF server auth flow
beyond username/password, limited error handling, and every host integration is tested by hand
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
| `src/OpenBcf.Tekla2026.Client` | net48 | Same as above, built against Tekla Structures 2026.0's Open API assemblies. |
| `src/OpenBcf.Rhino8.Client` | net48 (`.rhp`) | Rhino 8 plug-in: a dockable WPF panel (`RhinoWindows.Controls.WpfElementHost` bridging `BcfDuiWebView` into Rhino's panel framework), opened via the `openBCF` command; viewpoint capture/apply against `RhinoViewport`'s camera and `RhinoObject` selection. |
| `src/OpenBcf.Blender.Extension` | Python (Blender 4.2+ extension) | A Blender Add-On (Extensions Platform `blender_manifest.toml`) with a **from-scratch, pure-Python BCF REST client and native `bpy` UI** — no shared code with the .NET clients at all (see "Why the Blender client is different" below). Uses [Bonsai](https://bonsaibim.org) for IFC-aware selection. |

The Revit/Tekla/Rhino clients all reference the same `OpenBcf.Core`/`OpenBcf.Dui`/`OpenBcf.Dui.WebView`
projects and the same built frontend — the only per-host code is the thin binding layer
(`Bindings/`) and viewpoint capture/apply, since each host exposes the active view/camera/selection
through a completely different API. Rhino's panel-hosting differs slightly (`WpfElementHost`
instead of a dockable pane or floating `ElementHost`-wrapped window), but the pattern is the same.

### Why the Blender client is different

Blender add-ons are plain Python modules running inside Blender's own process, and Blender has no
embeddable web view of any kind — its UI is drawn by its own internal toolkit, not any standard OS
UI framework, so there is nothing to attach a WebView2/Chromium control to the way every other
client does. `OpenBcf.Blender.Extension` is therefore not a thin binding layer over shared code the
way the other clients are — it's a **complete, independent reimplementation** in Python:

- `bcf_client.py`/`oauth.py` — a BCF REST client and OAuth2 Authorization Code (RFC 6749, with
  RFC 7591 dynamic client registration) sign-in flow, using only the standard library
  (`urllib.request`/`json`), kept endpoint-for-endpoint and field-for-field consistent with
  `OpenBcf.Core`'s C# client — including the same dual-shape (spec-compliant nested +
  REDACTED-server.invalid's actual flat) accommodation for viewpoint camera/selection/snapshot data (see
  `ViewpointDto.cs`'s comments for why that's necessary at all).
- `camera.py` — captures/applies a BCF camera against Blender's active 3D viewport
  (`bpy.types.RegionView3D.view_matrix`), `selection.py` — selected-object ⇄ IFC GlobalId via
  Bonsai's `bonsai.tool.Ifc`, `screenshot.py` — a real rendered snapshot via
  `bpy.ops.render.opengl`.
- `properties.py`/`operators.py`/`panels.py` — native `bpy.types.Panel`/`Operator` UI (a "openBCF"
  tab in the 3D Viewport's sidebar) standing in for the Vue frontend's connect form/issue
  list/detail, since there's no web view to put that frontend in here.

Bonsai is a soft dependency (only needed for the selection/IFC half of a viewpoint - connecting,
browsing, and commenting on topics works without it) and is not bundled by this add-on; install it
separately from [bonsaibim.org](https://bonsaibim.org). Passwords are never persisted (unlike the
.NET clients' DPAPI-protected storage - Blender runs on Windows/macOS/Linux with no common
secure-storage primitive in the standard library) - only the server URL and username are
remembered, via Blender's own Add-on Preferences.

**Verification status**: this one could be build- and run-verified for real, end
to end, against a local Blender 5.2.0 LTS + Bonsai 0.8.6 install: the extension was built with
`blender --command extension build`, installed with `--command extension install-file`, and loaded
without error (`register()` succeeding is itself a real test - a bad property/panel/operator
declaration throws immediately). The full `openbcf.connect` operator was exercised with real HTTP
calls against the project's own test server (REDACTED-server.invalid) - version discovery, auth discovery,
dynamic client registration, and a wrong-credentials sign-in attempt all worked exactly as
designed.

`camera.py`/`screenshot.py` were also run-verified in a real Blender GUI session (not just
`--background`, which has no 3D Viewport for `RegionView3D`/`bpy.ops.render.opengl` to exist in):
a launched Blender window ran a script driving the real viewport via `bpy.context.temp_override`.
This caught and fixed two real bugs in `apply_to_region_3d` - assigning `view_matrix` directly
doesn't switch `view_perspective`, and doesn't survive the next redraw in orthographic mode, since
a viewport's actual internal state is `view_location`/`view_rotation`/`view_distance`, not a free
`view_matrix` - the fix (setting all three explicitly, with `view_location = eye_position +
forward * view_distance` confirmed empirically, not guessed) brought a live capture → apply →
recapture round trip down to ~1e-7 error. The viewport screenshot came back as a real ~1.2 MB file
starting with the exact PNG magic bytes.

The full server round trip has since been run for real too, with a real account on
REDACTED-server.invalid: sign-in, listing the account's actual projects, creating a topic, adding a
comment, creating a viewpoint (synthetic camera/selection/snapshot), reading it all back
(camera/selection/snapshot bytes all matched exactly), confirming the topic appears in the topic
list, and deleting the test topic again afterward to leave the account clean. Every step passed on
the first attempt.

## Building & deploying

The Revit, Tekla, and Rhino client projects build and deploy themselves in one step — no separate
copy scripts:

```powershell
dotnet build src/OpenBcf.Revit2025.Client/OpenBcf.Revit2025.Client.csproj -c Debug
dotnet build src/OpenBcf.Tekla2025.Client/OpenBcf.Tekla2025.Client.csproj -c Debug
dotnet build src/OpenBcf.Tekla2026.Client/OpenBcf.Tekla2026.Client.csproj -c Debug
dotnet build src/OpenBcf.Rhino8.Client/OpenBcf.Rhino8.Client.csproj -c Debug
```

Each build: builds the Vue frontend (`npm install`/`npm run build`, skipped if `dist/` is already
up to date), then copies the DLLs, `wwwroot/`, and the WebView2 native loader into the real host
location:

- **Revit**: `%APPDATA%\Autodesk\Revit\Addins\2025\` — restart Revit to pick up changes.
- **Tekla**: `C:\ProgramData\Trimble\Tekla Structures\<version>\Environments\common\extensions\openBCF\`
  (plugin DLLs) and `...\system\Ribbons\CustomTabs\Modeling\` (ribbon tab/icon) — restart Tekla
  Structures to pick up ribbon changes.
- **Rhino**: `%APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins\OpenBcf.Rhino8.Client\` (the flat, per-user
  plugin folder Rhino scans for `.rhp` files at startup) — restart Rhino to pick up changes, then
  type `openBCF` at the command line to open the panel.

Override the deploy locations with `-p:RevitAddinsPath=...` / `-p:TeklaCommonEnvironmentPath=...` /
`-p:RhinoPluginsPath=...` if Revit, Tekla, or Rhino is installed somewhere non-default.

**Verification status for Rhino**: every RhinoCommon/RhinoWindows member this client calls
(`RhinoViewport.CameraLocation`/`CameraDirection`/`CameraUp`/`GetCameraAngle`/`Camera35mmLensLength`/
`Magnify`, `ObjectTable.GetSelectedObjects`/`Select`/`Find`, `RhinoView.CaptureToBitmap`,
`RhinoWindows.Controls.WpfElementHost`, `Rhino.UI.Panels.RegisterPanel`/`OpenPanel`) was confirmed
by loading the actual installed `RhinoCommon.dll`/`RhinoWindows.dll` (Rhino 8.30) via .NET
reflection and inspecting their real signatures directly — not just documentation. This caught one
real design error before it could ever run: an initial `BcfViewpointApply` draft called a
`RhinoViewport.SetFrustum` method that doesn't actually exist; reflection against the real
assembly showed there's no absolute frustum/FOV setter at all, only `Camera35mmLensLength`
(perspective) and the relative `Magnify` (parallel), and the code was rewritten around those
instead. The project builds cleanly (Debug and Release) and deploys to Rhino's real plugin folder,
but hasn't yet been loaded inside a running Rhino session.

**Requirements**: .NET 8 SDK, Node.js/npm, and whichever of Revit 2025 / Tekla Structures 2025.0 or
2026.0 you're building against (the Tekla clients compile against Tekla's Open API assemblies
directly from its install folder — no NuGet feed exists for those).

`OpenBcf.Blender.Extension` needs no compilation at all (pure Python, no OS-specific code) — build
and install it with Blender's own extension command-line tools, which work identically on Windows,
macOS, and Linux:

**Windows**:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --command extension build `
  --source-dir src\OpenBcf.Blender.Extension --output-dir installer\Output

& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --command extension install-file `
  -r user_default --enable installer\Output\openbcf-0.1.0.zip
```

**macOS / Linux**: run [`installer/install-blender-extension.sh`](installer/install-blender-extension.sh),
which wraps the same two commands (auto-detecting Blender at its default macOS location,
`/Applications/Blender.app`, or on `PATH` for Linux — set `BLENDER_APP` to override):

```sh
./installer/install-blender-extension.sh
```

There's no separate native installer for a Blender extension the way `openBCF.iss` provides for
Revit/Tekla — a `.zip` built from `blender_manifest.toml` + the Python sources *is* the installer,
identically across platforms, since none of the extension's code is Windows-specific
(no registry access, no `ctypes`, no hardcoded path separators — verified by grep, though only
actually run end-to-end on Windows so far; the shell script's own Blender-detection logic hasn't
been exercised on real macOS/Linux hardware).

Requires Blender 4.2+ (developed/tested against 5.2.0 LTS) and, for the selection/IFC half of a
viewpoint, the free [Bonsai](https://bonsaibim.org) add-on installed alongside it (also cross-platform).

## Connecting to a BCF server

Server URL/credentials are configurable at runtime (not hardcoded) via the connect form in any
host's panel; `OpenBcf.Core`'s `BcfSettings` persists them locally per user (Blender instead uses
its own Add-on Preferences — see "Why the Blender client is different" above for why passwords
aren't remembered there). Any BCF REST API-compliant server should work.
