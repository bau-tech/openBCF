"""
bpy.types.Panel/UIList classes - shown in the 3D Viewport's sidebar (press N), under an "openBCF"
tab. This is the native-Blender-UI equivalent of the Vue frontend's connect form / issue list /
issue detail (see operators.py's module docstring for why there's no shared web UI here).
"""

import bpy


def _short_date(iso_date: str) -> str:
    """Trims a BCF server timestamp ("2026-08-16T23:35:10.267073+00:00") down to "2026-08-16
    23:35" - the full string (seconds, microseconds, UTC offset) doesn't fit a comment list row's
    width and just gets clipped mid-character by Blender's own label truncation."""
    return iso_date.replace("T", " ")[:16] if iso_date else ""


class OPENBCF_UL_topics(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        row = layout.row()
        row.label(text=item.title or "(untitled)")
        row.label(text=item.topic_status or "")


class OPENBCF_UL_comments(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        col = layout.column()
        col.label(text=f"{item.author} - {_short_date(item.date)}")
        col.label(text=item.text)


class OPENBCF_PT_main(bpy.types.Panel):
    bl_label = "openBCF"
    bl_idname = "OPENBCF_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "openBCF"

    def draw(self, context):
        layout = self.layout
        s = context.window_manager.openbcf
        prefs = context.preferences.addons[__package__].preferences

        if not s.connected and not s.awaiting_project_choice:
            layout.prop(prefs, "server_url")
            layout.prop(prefs, "username")
            layout.prop(s, "password")
            layout.operator("openbcf.connect")
            if s.status_message:
                layout.label(text=s.status_message, icon="ERROR")
            return

        if s.awaiting_project_choice:
            layout.label(text="Select a BCF project:")
            layout.template_list(
                "UI_UL_list", "openbcf_project_choices", s, "project_choices", s, "project_choice_index"
            )
            layout.operator("openbcf.select_project")
            return

        box = layout.box()
        box.label(text=f"Connected: {prefs.server_url}", icon="CHECKMARK")
        box.label(text=f"Project: {s.project_name}")
        box.operator("openbcf.disconnect")


class OPENBCF_PT_topics(bpy.types.Panel):
    bl_label = "Topics"
    bl_idname = "OPENBCF_PT_topics"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "openBCF"
    bl_parent_id = "OPENBCF_PT_main"

    @classmethod
    def poll(cls, context):
        return context.window_manager.openbcf.connected

    def draw(self, context):
        layout = self.layout
        s = context.window_manager.openbcf

        layout.operator("openbcf.refresh_topics")
        layout.template_list("OPENBCF_UL_topics", "", s, "topics", s, "active_topic_index")

        if 0 <= s.active_topic_index < len(s.topics):
            layout.operator("openbcf.view_topic")

        box = layout.box()
        box.label(text="New Topic")
        box.prop(s, "new_topic_title")
        box.prop(s, "new_topic_description")
        box.prop(s, "new_topic_type")
        box.prop(s, "new_topic_status")
        box.prop(s, "new_topic_priority")
        box.prop(s, "new_topic_assigned_to")
        due_row = box.row(align=True)
        due_row.prop(s, "new_topic_due_date")
        pick = due_row.operator("openbcf.pick_date", text="Pick...")
        pick.target = "new_topic_due_date"
        box.operator("openbcf.create_topic")

        layout.separator()
        row = layout.row(align=True)
        row.operator("openbcf.export_to_file", text="Export .bcfzip")
        row.operator("openbcf.import_from_file", text="Import .bcfzip")


class OPENBCF_PT_topic_detail(bpy.types.Panel):
    bl_label = "Topic Detail"
    bl_idname = "OPENBCF_PT_topic_detail"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "openBCF"
    bl_parent_id = "OPENBCF_PT_main"

    @classmethod
    def poll(cls, context):
        s = context.window_manager.openbcf
        return s.connected and 0 <= s.active_topic_index < len(s.topics)

    def draw(self, context):
        layout = self.layout
        s = context.window_manager.openbcf
        topic = s.topics[s.active_topic_index]

        layout.label(text=topic.title)

        box = layout.box()
        col = box.column(align=True)
        col.label(text=f"Type: {topic.topic_type or '-'}")
        col.label(text=f"Status: {topic.topic_status or '-'}")
        col.label(text=f"Priority: {topic.priority or '-'}")
        col.label(text=f"Assigned to: {topic.assigned_to or '-'}")
        col.label(text=f"Due date: {topic.due_date[:10] if topic.due_date else '-'}")
        col.label(text=f"Created by: {topic.creation_author or '-'}")
        if topic.description:
            box.separator()
            box.label(text="Description:")
            col = box.column(align=True)
            for line in topic.description.splitlines() or [topic.description]:
                col.label(text=line)

        box = layout.box()
        box.label(text="Edit Topic")
        box.prop(s, "edit_topic_type")
        box.prop(s, "edit_topic_status")
        box.prop(s, "edit_topic_priority")
        box.prop(s, "edit_topic_assigned_to")
        due_row = box.row(align=True)
        due_row.prop(s, "edit_topic_due_date")
        pick = due_row.operator("openbcf.pick_date", text="Pick...")
        pick.target = "edit_topic_due_date"
        box.operator("openbcf.update_topic")

        layout.label(text="Comments:")
        if len(s.comments) == 0:
            layout.label(text="No comments yet.")
        else:
            layout.template_list("OPENBCF_UL_comments", "", s, "comments", s, "active_topic_index", rows=3)
        box = layout.box()
        box.prop(s, "new_comment_text")
        box.operator("openbcf.create_comment")

        layout.separator()
        layout.operator("openbcf.capture_viewpoint")
        for vp in s.viewpoints:
            box = layout.box()
            image = bpy.data.images.get(vp.image_name) if vp.image_name else None
            if image is not None:
                image.preview_ensure()
                box.template_icon(icon_value=image.preview.icon_id, scale=6)
            row = box.row()
            row.label(text=vp.guid[:8])
            op = row.operator("openbcf.apply_viewpoint", text="Apply")
            op.viewpoint_guid = vp.guid


CLASSES = (
    OPENBCF_UL_topics,
    OPENBCF_UL_comments,
    OPENBCF_PT_main,
    OPENBCF_PT_topics,
    OPENBCF_PT_topic_detail,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
