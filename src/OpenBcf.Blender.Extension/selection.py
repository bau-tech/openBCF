"""
Selected-object <-> IFC GlobalId access via Bonsai (https://bonsaibim.org, the IFC/BIM authoring
add-on for Blender formerly named BlenderBIM) - verified for real against a live Blender 5.2 +
Bonsai 0.8.6 install: `import bonsai.tool as tool` works as a plain top-level import regardless of
which repository Bonsai itself was installed from, and tool.Ifc.get_entity(obj)/get_object(entity)
round-trip correctly against real IFC entities (see the project's dev notes).

Bonsai is a soft dependency: everything here degrades to a clear error (not a crash) if it isn't
installed, since openBCF's own manifest doesn't declare Bonsai as a hard requirement - a user
without Bonsai can still connect/browse/comment on topics, just not capture/apply the
selection-based part of a viewpoint.
"""

import bpy


class BonsaiNotAvailableError(Exception):
    pass


def _get_ifc_tool():
    try:
        import bonsai.tool as tool
    except ImportError as ex:
        raise BonsaiNotAvailableError(
            "The Bonsai add-on is required to capture/apply IFC element selection for BCF viewpoints. "
            "Install it from https://bonsaibim.org."
        ) from ex
    return tool


def get_selected_ifc_guids() -> list[str]:
    tool = _get_ifc_tool()
    guids = []
    for obj in bpy.context.selected_objects:
        entity = tool.Ifc.get_entity(obj)
        if entity is not None:
            guids.append(entity.GlobalId)
    return guids


def select_by_ifc_guids(ifc_guids: list[str]) -> int:
    """Replaces the current selection with whichever objects resolve from the given IFC GlobalIds.
    Returns how many of the requested GUIDs were actually found in the current project."""

    tool = _get_ifc_tool()
    ifc_file = tool.Ifc.get()
    if ifc_file is None:
        raise BonsaiNotAvailableError("Open or create an IFC project in Bonsai before applying a viewpoint.")

    bpy.ops.object.select_all(action="DESELECT")

    matched = 0
    active_set = False
    for guid in ifc_guids:
        entity = ifc_file.by_guid(guid) if guid else None
        if entity is None:
            continue
        obj = tool.Ifc.get_object(entity)
        if obj is None:
            continue
        obj.select_set(True)
        matched += 1
        if not active_set:
            bpy.context.view_layer.objects.active = obj
            active_set = True

    return matched
