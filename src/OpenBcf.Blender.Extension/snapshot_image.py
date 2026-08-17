"""
Loads a viewpoint snapshot's PNG bytes (fetched via bcf_client.get_snapshot) into a real
bpy.data.images datablock so panels.py can render a thumbnail with template_icon - Blender's Image
API has no "load from bytes" constructor, so this round-trips through a temp file, then packs the
result so the temp file isn't needed afterward (mirrors how the other clients keep the fetched
bytes as the only source of truth, not a file path).
"""

import os
import tempfile

import bpy


def get_or_load(name: str, png_bytes: bytes):
    """Returns the existing image datablock for `name` if one was already loaded (avoids a
    redundant network fetch + reload on repeat View Topic calls for the same viewpoint), otherwise
    loads `png_bytes` into a new one."""
    existing = bpy.data.images.get(name)
    if existing is not None:
        return existing

    fd, path = tempfile.mkstemp(suffix=".png")
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(png_bytes)
        image = bpy.data.images.load(path)
        image.name = name
        image.pack()
    finally:
        try:
            os.remove(path)
        except OSError:
            pass

    return image


def clear_all():
    """Removes every openBCF-loaded snapshot image datablock - called on disconnect/refresh so
    switching topics or projects doesn't leak stale images across sessions."""
    for image in list(bpy.data.images):
        if image.name.startswith("openbcf_vp_"):
            bpy.data.images.remove(image)
