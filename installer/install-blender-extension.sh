#!/usr/bin/env bash
# Builds and installs the openBCF Blender extension (src/OpenBcf.Blender.Extension) via Blender's
# own extension command-line tools - there is no separate native installer for Blender extensions
# the way openBCF.iss provides for Revit/Tekla on Windows: a Blender extension is just a .zip
# (built from blender_manifest.toml + the Python sources) that "blender --command extension
# install-file" unpacks into Blender's own per-user extensions folder, identically on Windows,
# macOS, and Linux, since the extension itself is pure Python with no OS-specific code.
#
# This script exists for macOS/Linux convenience, mirroring the two PowerShell lines in the main
# README for Windows. It has been checked against the exact same "blender --command extension
# build/install-file" invocations already verified (this session, on Windows) to work correctly
# against a real Blender 5.2 install - only this script's own Blender-executable auto-detection is
# untested on real macOS/Linux hardware.
#
# Usage:
#   ./install-blender-extension.sh
#   BLENDER_APP=/path/to/blender ./install-blender-extension.sh   # if auto-detection doesn't find it

set -euo pipefail

if [ -n "${BLENDER_APP:-}" ]; then
    BLENDER="$BLENDER_APP"
elif [ -x "/Applications/Blender.app/Contents/MacOS/Blender" ]; then
    # Default macOS install location.
    BLENDER="/Applications/Blender.app/Contents/MacOS/Blender"
elif command -v blender >/dev/null 2>&1; then
    # Covers Linux (package manager installs, or a Blender build already on PATH) and any macOS
    # install that symlinked its own binary onto PATH.
    BLENDER="$(command -v blender)"
else
    echo "Could not find Blender. Install it from https://www.blender.org/download/" >&2
    echo "or set BLENDER_APP to its executable path and re-run this script." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_DIR="$REPO_ROOT/src/OpenBcf.Blender.Extension"
OUTPUT_DIR="$REPO_ROOT/installer/Output"

mkdir -p "$OUTPUT_DIR"

echo "Using Blender: $BLENDER"
echo "Building openBCF extension..."
"$BLENDER" --command extension build --source-dir "$SOURCE_DIR" --output-dir "$OUTPUT_DIR"

ZIP_FILE=$(ls -t "$OUTPUT_DIR"/openbcf-*.zip | head -n 1)
echo "Installing $ZIP_FILE..."
"$BLENDER" --command extension install-file -r user_default --enable "$ZIP_FILE"

echo ""
echo "Done - openBCF is installed and enabled in Blender."
echo "Install Bonsai too (https://bonsaibim.org) for the selection/IFC half of viewpoint capture."
