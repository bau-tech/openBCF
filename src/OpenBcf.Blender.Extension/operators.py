"""
bpy.types.Operator classes driving the Connect / topic browsing / viewpoint capture-apply flow -
the native-Blender-UI equivalent of the other clients' Bindings/*.cs, since Blender has no
embeddable web view for the Vue "DUI3" frontend the other clients share (Blender's UI is drawn by
its own internal toolkit, not any standard OS UI framework - there is nothing to embed a browser
control into the way WebView2 attaches to a WPF panel or a raw HWND).
"""

import bpy

from . import bcf_client, camera, oauth, screenshot, selection, session


def _session(context) -> "bpy.types.OpenBcfSession":
    return context.window_manager.openbcf


def _resolve_author(context) -> str:
    prefs = context.preferences.addons[__package__].preferences
    return prefs.username or "Blender user"


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
                # Basic auth header - every server actually tested against (REDACTED-server.invalid)
                # requires OAuth2 anyway (http_basic_supported: false), so this was left out
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
        body = bcf_client.build_viewpoint_write_body(camera_dict, ifc_guids, snapshot_png)

        try:
            client.create_viewpoint(s.version_id, s.project_id, topic_guid, body)
        except bcf_client.BcfApiError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

        bpy.ops.openbcf.view_topic()
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


CLASSES = (
    OPENBCF_OT_connect,
    OPENBCF_OT_select_project,
    OPENBCF_OT_disconnect,
    OPENBCF_OT_refresh_topics,
    OPENBCF_OT_create_topic,
    OPENBCF_OT_view_topic,
    OPENBCF_OT_create_comment,
    OPENBCF_OT_capture_viewpoint,
    OPENBCF_OT_apply_viewpoint,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
