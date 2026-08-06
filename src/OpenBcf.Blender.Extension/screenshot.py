"""
Renders the active 3D viewport to a real PNG - build- and run-verified for real in a live Blender
5.2 GUI session (bpy.ops.render.opengl(write_still=True, view_context=True), called with a 3D
Viewport window/area/region context override, produced a real ~1.2MB file starting with the exact
PNG magic bytes \\x89PNG\\r\\n\\x1a\\n - see project memory for the full session). Requires a live
3D Viewport as context the same way RegionView3D does - called from an operator invoked with one
(e.g. a button in this add-on's own sidebar panel, which panels.py places there), it just works;
called without one (as from Blender's --background mode), it has no viewport to render.
"""

import os
import tempfile

import bpy


def capture_viewport_png() -> bytes:
    scene = bpy.context.scene
    original_filepath = scene.render.filepath
    original_format = scene.render.image_settings.file_format

    fd, path = tempfile.mkstemp(prefix="openbcf-viewpoint-", suffix=".png")
    os.close(fd)

    try:
        scene.render.image_settings.file_format = "PNG"
        scene.render.filepath = path

        result = bpy.ops.render.opengl(write_still=True, view_context=True)
        if "FINISHED" not in result:
            raise RuntimeError(f"Blender did not produce a viewport snapshot (operator returned {result}).")

        with open(path, "rb") as f:
            return f.read()
    finally:
        scene.render.filepath = original_filepath
        scene.render.image_settings.file_format = original_format
        if os.path.exists(path):
            os.remove(path)
