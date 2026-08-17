"""
bpy.types.Operator classes driving the Connect / topic browsing / viewpoint capture-apply flow -
the native-Blender-UI equivalent of the other clients' Bindings/*.cs, since Blender has no
embeddable web view for the Vue "DUI3" frontend the other clients share (Blender's UI is drawn by
its own internal toolkit, not any standard OS UI framework - there is nothing to embed a browser
control into the way WebView2 attaches to a WPF panel or a raw HWND).
"""

import bpy
from bpy_extras.io_utils import ExportHelper, ImportHelper

from . import annotate, bcf_archive, bcf_client, camera, oauth, screenshot, selection, session, snapshot_image


def _session(context) -> "bpy.types.OpenBcfSession":
    return context.window_manager.openbcf


def _resolve_author(context) -> str:
    prefs = context.preferences.addons[__package__].preferences
    return prefs.username or "Blender user"


def _parse_due_date(text: str):
    """"2026-08-20" (what the Due Date field asks for) isn't itself a valid BCF due_date - the
    server expects a full timestamp, matching every other date field this add-on already passes
    through unparsed from the REST API. Left as None (omitted from the write body) if blank."""
    text = (text or "").strip()
    if not text:
        return None
    return text if "T" in text else f"{text}T00:00:00+00:00"


def _refresh_extensions(context):
    """GetExtensions equivalent - fetches the project's actual allowed topic type/status/priority/
    user values once per Connect, feeding the New Topic / Edit Topic dropdowns instead of leaving
    them free text. Best-effort: a server that doesn't support this endpoint shouldn't block
    Connect from otherwise succeeding, it just leaves the dropdowns at "(none)"."""
    s = _session(context)
    client = session.get_client()
    if client is None:
        return
    try:
        ext = client.get_project_extensions(s.version_id, s.project_id)
    except bcf_client.BcfApiError:
        return

    s.extension_topic_types = "\n".join(ext.get("topic_type") or [])
    s.extension_topic_statuses = "\n".join(ext.get("topic_status") or [])
    s.extension_priorities = "\n".join(ext.get("priority") or [])
    s.extension_users = "\n".join(ext.get("users") or [])


class OPENBCF_OT_connect(bpy.types.Operator):
    bl_idname = "openbcf.connect"
    bl_label = "Connect"
    bl_description = "Connect to the configured BCF server"

    def execute(self, context):
        prefs = context.preferences.addons[__package__].preferences
        s = _session(context)

        try:
            client = bcf_client.BcfServerClient(prefs.server_url)
            versions = client.get_versions()
            bcf_versions = [v for v in versions if v["api_id"] == "bcf"]
            if not bcf_versions:
                raise bcf_client.BcfApiError(f"{prefs.server_url} does not advertise a BCF API version.")
            version_id = sorted(bcf_versions, key=lambda v: [int(p) for p in v["version_id"].split(".")])[-1]["version_id"]

            auth_options = client.get_auth_options(version_id)
            if auth_options.get("http_basic_supported"):
                # Not implemented: bcf_client.py's _request only ever sends a Bearer token, no
                # Basic auth header - every server actually tested against (the project's
                # reference test server) requires OAuth2 anyway (http_basic_supported: false), so this was left out
                # rather than shipped untested.
                raise bcf_client.BcfApiError(
                    "This server expects HTTP Basic auth, which this Blender add-on does not implement yet."
                )
            elif auth_options.get("supported_oauth2_flows") and "authorization_code_grant" in auth_options["supported_oauth2_flows"]:
                if not prefs.username or not s.password:
                    raise bcf_client.BcfApiError("Enter a username and password before connecting.")
                access_token = oauth.authenticate_with_password(auth_options, prefs.username, s.password)
                client = bcf_client.BcfServerClient(prefs.server_url, access_token=access_token)
                s.access_token = access_token
            else:
                raise bcf_client.BcfApiError(f"{prefs.server_url} does not support username/password sign-in.")

            session.set_client(client)
            s.version_id = version_id

            projects = client.get_projects(version_id)
            if not projects:
                raise bcf_client.BcfApiError("The server has no BCF projects.")

            s.project_choices.clear()
            for p in projects:
                item = s.project_choices.add()
                item.project_id = p["project_id"]
                item.name = p.get("name") or p["project_id"]

            if len(projects) == 1:
                s.project_id = projects[0]["project_id"]
                s.project_name = projects[0].get("name") or projects[0]["project_id"]
                s.awaiting_project_choice = False
                s.connected = True
                s.status_message = ""
                _refresh_extensions(context)
                bpy.ops.openbcf.refresh_topics()
            else:
                s.awaiting_project_choice = True
                s.status_message = "Select a project below, then click Select Project."

            return {"FINISHED"}

        except (bcf_client.BcfApiError, oauth.OAuthSignInError) as ex:
            s.status_message = str(ex)
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}


class OPENBCF_OT_select_project(bpy.types.Operator):
    bl_idname = "openbcf.select_project"
    bl_label = "Select Project"
    bl_description = "Use the chosen project for this session"

    def execute(self, context):
        s = _session(context)
        if not s.project_choices or s.project_choice_index >= len(s.project_choices):
            self.report({"ERROR"}, "No project selected.")
            return {"CANCELLED"}

        choice = s.project_choices[s.project_choice_index]
        s.project_id = choice.project_id
        s.project_name = choice.name
        s.awaiting_project_choice = False
        s.connected = True
        s.status_message = ""
        _refresh_extensions(context)
        bpy.ops.openbcf.refresh_topics()
        return {"FINISHED"}


class OPENBCF_OT_disconnect(bpy.types.Operator):
    bl_idname = "openbcf.disconnect"
    bl_label = "Disconnect"
    bl_description = "Disconnect from the BCF server"

    def execute(self, context):
        s = _session(context)
        session.clear()
        s.connected = False
        s.access_token = ""
        s.password = ""
        s.project_id = ""
        s.project_name = ""
        s.topics.clear()
        s.comments.clear()
        s.viewpoints.clear()
        s.active_topic_index = -1
        s.status_message = ""
        snapshot_image.clear_all()
        return {"FINISHED"}


class OPENBCF_OT_refresh_topics(bpy.types.Operator):
    bl_idname = "openbcf.refresh_topics"
    bl_label = "Refresh"
    bl_description = "Reload the topic list from the server"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None:
            self.report({"ERROR"}, "Connect to a BCF server first.")
            return {"CANCELLED"}

        try:
            topics = client.get_topics(s.version_id, s.project_id)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        s.topics.clear()
        for t in topics:
            item = s.topics.add()
            item.guid = t["guid"]
            item.title = t["title"]
            item.topic_type = t.get("topic_type") or ""
            item.topic_status = t.get("topic_status") or ""
            item.priority = t.get("priority") or ""
            item.description = t.get("description") or ""
            item.assigned_to = t.get("assigned_to") or ""
            item.due_date = t.get("due_date") or ""
            item.creation_author = t.get("creation_author") or ""
            item.creation_date = t.get("creation_date") or ""

        return {"FINISHED"}


class OPENBCF_OT_create_topic(bpy.types.Operator):
    bl_idname = "openbcf.create_topic"
    bl_label = "Create Topic"
    bl_description = "Create a new BCF topic"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None:
            self.report({"ERROR"}, "Connect to a BCF server first.")
            return {"CANCELLED"}
        if not s.new_topic_title.strip():
            self.report({"ERROR"}, "Enter a title first.")
            return {"CANCELLED"}

        body = {
            "title": s.new_topic_title,
            "description": s.new_topic_description or None,
            "topic_type": s.new_topic_type or None,
            "topic_status": s.new_topic_status or None,
            "priority": s.new_topic_priority or None,
            "assigned_to": s.new_topic_assigned_to or None,
            "due_date": _parse_due_date(s.new_topic_due_date),
            "creation_author": _resolve_author(context),
            "labels": [],
        }
        try:
            client.create_topic(s.version_id, s.project_id, body)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        s.new_topic_title = ""
        s.new_topic_description = ""
        s.new_topic_due_date = ""
        bpy.ops.openbcf.refresh_topics()
        return {"FINISHED"}


class OPENBCF_OT_update_topic(bpy.types.Operator):
    bl_idname = "openbcf.update_topic"
    bl_label = "Save Changes"
    bl_description = "Save this topic's type/status/priority/assignment/due date"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None or s.active_topic_index < 0 or s.active_topic_index >= len(s.topics):
            self.report({"ERROR"}, "Select a topic first.")
            return {"CANCELLED"}

        topic_guid = s.topics[s.active_topic_index].guid
        try:
            # PUT replaces the whole topic on this server (see bcf_client.py's update_topic doc),
            # so title/description/creation_author must be sent back unchanged, not omitted -
            # mirrors Tekla's BcfIssueBinding.UpdateTopicStatus, which fetches the full topic
            # first for the same reason.
            current = client.get_topic(s.version_id, s.project_id, topic_guid)
            body = {
                "title": current.get("title") or "",
                "description": current.get("description"),
                "creation_author": current.get("creation_author") or _resolve_author(context),
                "labels": current.get("labels") or [],
                "topic_type": s.edit_topic_type or None,
                "topic_status": s.edit_topic_status or None,
                "priority": s.edit_topic_priority or None,
                "assigned_to": s.edit_topic_assigned_to or None,
                "due_date": _parse_due_date(s.edit_topic_due_date),
            }
            client.update_topic(s.version_id, s.project_id, topic_guid, body)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        bpy.ops.openbcf.refresh_topics()
        return {"FINISHED"}


class OPENBCF_OT_view_topic(bpy.types.Operator):
    bl_idname = "openbcf.view_topic"
    bl_label = "View Topic"
    bl_description = "Load this topic's comments and viewpoints"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None or s.active_topic_index < 0 or s.active_topic_index >= len(s.topics):
            return {"CANCELLED"}

        topic_guid = s.topics[s.active_topic_index].guid
        try:
            comments = client.get_comments(s.version_id, s.project_id, topic_guid)
            viewpoints = client.get_viewpoints(s.version_id, s.project_id, topic_guid)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        topic = s.topics[s.active_topic_index]
        # Pre-fill the Edit Topic dropdowns with this topic's current values. Assigning an
        # EnumProperty an identifier that isn't in its *current* dynamic items list raises
        # TypeError (not a silent no-op) - guarded per-field so one unexpected value (e.g.
        # extensions fetch failed, or this topic predates the server's current allowed-values
        # list) can't stop the rest of View Topic from working.
        for attr, value in (
            ("edit_topic_type", topic.topic_type),
            ("edit_topic_status", topic.topic_status),
            ("edit_topic_priority", topic.priority),
            ("edit_topic_assigned_to", topic.assigned_to),
        ):
            try:
                setattr(s, attr, value)
            except TypeError:
                pass
        s.edit_topic_due_date = topic.due_date[:10] if topic.due_date else ""

        s.comments.clear()
        for c in sorted(comments, key=lambda c: c.get("date") or ""):
            item = s.comments.add()
            item.guid = c["guid"]
            item.author = c.get("author") or ""
            item.date = c.get("date") or ""
            item.text = c.get("comment") or ""

        s.viewpoints.clear()
        for v in viewpoints:
            item = s.viewpoints.add()
            item.guid = v["guid"]

            # Best-effort: not every viewpoint has a snapshot (a server may 404), and a missing
            # thumbnail shouldn't stop the rest of View Topic from working.
            try:
                png_bytes = client.get_snapshot(s.version_id, s.project_id, topic_guid, v["guid"])
                image = snapshot_image.get_or_load(f"openbcf_vp_{v['guid'][:8]}", png_bytes)
                item.image_name = image.name
            except bcf_client.BcfApiError:
                item.image_name = ""

        return {"FINISHED"}


class OPENBCF_OT_create_comment(bpy.types.Operator):
    bl_idname = "openbcf.create_comment"
    bl_label = "Add Comment"
    bl_description = "Add a comment to the selected topic"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None or s.active_topic_index < 0 or s.active_topic_index >= len(s.topics):
            self.report({"ERROR"}, "Select a topic first.")
            return {"CANCELLED"}
        if not s.new_comment_text.strip():
            self.report({"ERROR"}, "Enter a comment first.")
            return {"CANCELLED"}

        topic_guid = s.topics[s.active_topic_index].guid
        try:
            client.create_comment(s.version_id, s.project_id, topic_guid, s.new_comment_text, _resolve_author(context))
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        s.new_comment_text = ""
        bpy.ops.openbcf.view_topic()
        return {"FINISHED"}


class OPENBCF_OT_capture_viewpoint(bpy.types.Operator):
    bl_idname = "openbcf.capture_viewpoint"
    bl_label = "Capture Viewpoint"
    bl_description = "Capture the active 3D viewport's camera, selection, and a snapshot, and attach it to the selected topic"

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None or s.active_topic_index < 0 or s.active_topic_index >= len(s.topics):
            self.report({"ERROR"}, "Select a topic first.")
            return {"CANCELLED"}

        region_3d = context.space_data.region_3d if context.space_data and context.space_data.type == "VIEW_3D" else None
        if region_3d is None:
            self.report({"ERROR"}, "Capture from a 3D Viewport.")
            return {"CANCELLED"}

        camera_dict = camera.capture_from_region_3d(region_3d)

        try:
            ifc_guids = selection.get_selected_ifc_guids()
        except selection.BonsaiNotAvailableError as ex:
            self.report({"WARNING"}, f"{ex} Capturing camera only, no selection.")
            ifc_guids = []

        try:
            snapshot_png = screenshot.capture_viewport_png()
        except Exception as ex:  # noqa: BLE001 - surfaced to the user either way, see report() below
            self.report({"WARNING"}, f"Could not render a snapshot: {ex}. Saving without one.")
            snapshot_png = None

        topic_guid = s.topics[s.active_topic_index].guid

        if snapshot_png is None:
            # Nothing to annotate - upload immediately, same as before this feature existed.
            body = bcf_client.build_viewpoint_write_body(camera_dict, ifc_guids, None)
            try:
                client.create_viewpoint(s.version_id, s.project_id, topic_guid, body)
            except bcf_client.BcfApiError as ex:
                self.report({"ERROR"}, str(ex))
                return {"CANCELLED"}
            bpy.ops.openbcf.view_topic()
            return {"FINISHED"}

        annotate.pending_capture = {
            "topic_guid": topic_guid,
            "camera": camera_dict,
            "ifc_guids": ifc_guids,
            "snapshot_png": snapshot_png,
        }
        bpy.ops.openbcf.annotate_viewpoint("INVOKE_DEFAULT")
        return {"FINISHED"}


class OPENBCF_OT_apply_viewpoint(bpy.types.Operator):
    bl_idname = "openbcf.apply_viewpoint"
    bl_label = "Apply"
    bl_description = "Move the active 3D viewport's camera and selection to match this viewpoint"

    viewpoint_guid: bpy.props.StringProperty()

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None or s.active_topic_index < 0 or s.active_topic_index >= len(s.topics):
            self.report({"ERROR"}, "Select a topic first.")
            return {"CANCELLED"}

        region_3d = context.space_data.region_3d if context.space_data and context.space_data.type == "VIEW_3D" else None
        if region_3d is None:
            self.report({"ERROR"}, "Apply from a 3D Viewport.")
            return {"CANCELLED"}

        topic_guid = s.topics[s.active_topic_index].guid
        try:
            viewpoint_dto = client.get_viewpoint(s.version_id, s.project_id, topic_guid, self.viewpoint_guid)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        camera_dict = bcf_client.parse_viewpoint_camera(viewpoint_dto)
        if camera_dict is not None:
            camera.apply_to_region_3d(region_3d, camera_dict)

        ifc_guids = bcf_client.parse_viewpoint_selection(viewpoint_dto)
        if ifc_guids:
            try:
                matched = selection.select_by_ifc_guids(ifc_guids)
                if matched == 0:
                    self.report({"WARNING"}, "None of this viewpoint's components could be found in the current project.")
            except selection.BonsaiNotAvailableError as ex:
                self.report({"WARNING"}, str(ex))

        if camera_dict is None and not ifc_guids:
            self.report({"WARNING"}, "This viewpoint has no camera or selection to apply.")

        return {"FINISHED"}


class OPENBCF_OT_export_to_file(bpy.types.Operator, ExportHelper):
    bl_idname = "openbcf.export_to_file"
    bl_label = "Export to .bcfzip"
    bl_description = "Save every topic in this project to a local BCF archive file"

    filename_ext = ".bcfzip"
    filter_glob: bpy.props.StringProperty(default="*.bcfzip", options={"HIDDEN"})

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None:
            self.report({"ERROR"}, "Connect to a BCF server first.")
            return {"CANCELLED"}

        try:
            topics = client.get_topics(s.version_id, s.project_id)
            entries = []
            for t in topics:
                full_topic = client.get_topic(s.version_id, s.project_id, t["guid"])
                comments = client.get_comments(s.version_id, s.project_id, t["guid"])
                viewpoints = client.get_viewpoints(s.version_id, s.project_id, t["guid"])

                vp_entries = []
                for v in viewpoints:
                    vp_dto = client.get_viewpoint(s.version_id, s.project_id, t["guid"], v["guid"])
                    try:
                        snapshot_png_bytes = client.get_snapshot(s.version_id, s.project_id, t["guid"], v["guid"])
                    except bcf_client.BcfApiError:
                        snapshot_png_bytes = None
                    vp_entries.append({
                        "guid": v["guid"],
                        "camera": bcf_client.parse_viewpoint_camera(vp_dto),
                        "selection_ifc_guids": bcf_client.parse_viewpoint_selection(vp_dto),
                        "snapshot_png_bytes": snapshot_png_bytes,
                    })

                entries.append({"topic": full_topic, "comments": comments, "viewpoints": vp_entries})

            bcf_archive.write(self.filepath, s.version_id, s.project_id, s.project_name, entries)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        self.report({"INFO"}, f"Exported {len(entries)} topic(s) to {self.filepath}")
        return {"FINISHED"}


class OPENBCF_OT_import_from_file(bpy.types.Operator, ImportHelper):
    bl_idname = "openbcf.import_from_file"
    bl_label = "Import from .bcfzip"
    bl_description = "Load topics from a local BCF archive file into this project"

    filter_glob: bpy.props.StringProperty(default="*.bcfzip", options={"HIDDEN"})

    def execute(self, context):
        s = _session(context)
        client = session.get_client()
        if client is None:
            self.report({"ERROR"}, "Connect to a BCF server first.")
            return {"CANCELLED"}

        try:
            document = bcf_archive.read(self.filepath)
        except Exception as ex:  # noqa: BLE001 - a malformed/foreign .bcfzip should report cleanly
            self.report({"ERROR"}, f"Could not read {self.filepath}: {ex}")
            return {"CANCELLED"}

        try:
            for entry in document["topics"]:
                t = entry["topic"]
                body = {
                    "title": t.get("title") or "(untitled)",
                    "topic_type": t.get("topic_type"),
                    "topic_status": t.get("topic_status"),
                    "priority": t.get("priority"),
                    "description": t.get("description"),
                    "assigned_to": t.get("assigned_to"),
                    "due_date": t.get("due_date"),
                    "creation_author": _resolve_author(context),
                    "labels": [],
                }
                created = client.create_topic(s.version_id, s.project_id, body)

                for vp in entry["viewpoints"]:
                    vp_body = bcf_client.build_viewpoint_write_body(
                        vp["camera"], vp["selection_ifc_guids"], vp["snapshot_png_bytes"]
                    )
                    client.create_viewpoint(s.version_id, s.project_id, created["guid"], vp_body)

                for c in entry["comments"]:
                    client.create_comment(
                        s.version_id, s.project_id, created["guid"],
                        c.get("comment") or "", c.get("author") or _resolve_author(context),
                    )
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        bpy.ops.openbcf.refresh_topics()
        self.report({"INFO"}, f"Imported {len(document['topics'])} topic(s) from {self.filepath}")
        return {"FINISHED"}


CLASSES = (
    OPENBCF_OT_connect,
    OPENBCF_OT_select_project,
    OPENBCF_OT_disconnect,
    OPENBCF_OT_refresh_topics,
    OPENBCF_OT_create_topic,
    OPENBCF_OT_update_topic,
    OPENBCF_OT_view_topic,
    OPENBCF_OT_create_comment,
    OPENBCF_OT_capture_viewpoint,
    OPENBCF_OT_apply_viewpoint,
    OPENBCF_OT_export_to_file,
    OPENBCF_OT_import_from_file,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
