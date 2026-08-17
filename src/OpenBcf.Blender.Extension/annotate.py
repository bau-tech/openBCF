"""
Lets the user draw on a captured viewpoint snapshot before it's uploaded - the Blender-native
equivalent of MarkupEditor.vue (the DUI3 frontend's freehand/arrow/line/text/cloud canvas the other
clients share). Blender's UI toolkit has no custom-canvas widget to reuse, but it does ship a real
2D annotation tool (Grease Pencil "Annotate", available in the Image Editor) - this swaps the
invoking area into an Image Editor showing the just-captured snapshot, activates that tool, and
lets the user draw directly with it rather than reimplementing a drawing surface from scratch.

Confidence note: the modal takeover and area/tool switching (context.area.type, SpaceImageEditor.
image, wm.tool_set_by_id) are standard, well-documented Blender scripting. The one genuinely
uncertain step is turning "an Image Editor with Grease Pencil strokes drawn over it" back into a
flat PNG for upload - there is no direct "flatten and export" API, so this uses
bpy.ops.screen.screenshot(full=False) scoped to the swapped area via a context override, which
captures whatever is actually rendered on screen (image + annotation overlay together, no need to
touch Grease Pencil's own stroke data at all). This has NOT been verified in a live Blender
session - if it doesn't produce the expected flattened image, the fallback (see _finish) is to
upload the original, un-annotated snapshot instead of losing the capture entirely.
"""

import os
import tempfile

import bpy

from . import bcf_client, session, snapshot_image

# {"topic_guid":, "camera": <dict or None>, "ifc_guids": [...], "snapshot_png": bytes} - set by
# operators.py's OPENBCF_OT_capture_viewpoint right before invoking OPENBCF_OT_annotate_viewpoint,
# consumed (and cleared) by this operator's _finish. Module-level rather than a bpy.props since it
# holds raw bytes/tuples, not something Blender's property system can store.
pending_capture = None


def _capture_area_png(context, area) -> bytes:
    region = next((r for r in area.regions if r.type == "WINDOW"), None)
    if region is None:
        raise RuntimeError("Annotated area has no WINDOW region to capture.")

    fd, path = tempfile.mkstemp(prefix="openbcf-annotated-", suffix=".png")
    os.close(fd)
    try:
        with context.temp_override(area=area, region=region):
            result = bpy.ops.screen.screenshot(filepath=path, full=False)
        if "FINISHED" not in result:
            raise RuntimeError(f"screen.screenshot returned {result}")
        with open(path, "rb") as f:
            return f.read()
    finally:
        if os.path.exists(path):
            os.remove(path)


class OPENBCF_OT_annotate_viewpoint(bpy.types.Operator):
    bl_idname = "openbcf.annotate_viewpoint"
    bl_label = "Annotate Viewpoint Snapshot"
    bl_description = "Draw on the captured snapshot before saving it"
    bl_options = {"REGISTER", "INTERNAL"}

    def invoke(self, context, event):
        if pending_capture is None:
            self.report({"ERROR"}, "Nothing to annotate.")
            return {"CANCELLED"}

        image = snapshot_image.get_or_load("openbcf_pending_annotation", pending_capture["snapshot_png"])

        self._area = context.area
        self._original_type = context.area.type
        self._area.type = "IMAGE_EDITOR"
        self._area.spaces.active.image = image

        # Best-effort: an unknown/renamed tool id on some Blender build shouldn't block
        # annotating outright - the user can still pick Annotate from the toolbar by hand.
        try:
            with context.temp_override(area=self._area):
                bpy.ops.wm.tool_set_by_id(name="builtin.annotate")
        except Exception:
            pass

        context.workspace.status_text_set("openBCF: draw your annotation, then Enter to save, or Esc to save without one")
        context.window_manager.modal_handler_add(self)
        return {"RUNNING_MODAL"}

    def modal(self, context, event):
        if event.type in {"RET", "NUMPAD_ENTER"} and event.value == "PRESS":
            return self._finish(context, use_annotation=True)
        if event.type == "ESC" and event.value == "PRESS":
            return self._finish(context, use_annotation=False)
        # Everything else (mouse drags, the Annotate tool's own clicks) must pass through, or
        # drawing on the image would never actually work.
        return {"PASS_THROUGH"}

    def _finish(self, context, use_annotation: bool):
        global pending_capture
        pending = pending_capture
        pending_capture = None
        context.workspace.status_text_set(None)

        final_png = pending["snapshot_png"]
        if use_annotation:
            try:
                final_png = _capture_area_png(context, self._area)
            except Exception as ex:  # noqa: BLE001 - keep the original snapshot rather than losing the capture
                self.report({"WARNING"}, f"Could not flatten the annotation, saving without it: {ex}")

        self._area.type = self._original_type

        client = session.get_client()
        s = context.window_manager.openbcf
        body = bcf_client.build_viewpoint_write_body(pending["camera"], pending["ifc_guids"], final_png)
        try:
            client.create_viewpoint(s.version_id, s.project_id, pending["topic_guid"], body)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        bpy.ops.openbcf.view_topic()
        return {"FINISHED"}


CLASSES = (OPENBCF_OT_annotate_viewpoint,)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
