"""
UI-bound state. Server URL/username are persisted via bpy.types.AddonPreferences (Blender's own
mechanism for "remember this across sessions", the same role BcfSettings plays for the other
clients); everything else (topics, the active session's access token, password) lives on
bpy.types.WindowManager, which - unlike Scene properties - Blender does not save into the .blend
file, matching how a live BCF session shouldn't be treated as part of the 3D model being authored.

Password is deliberately NOT persisted anywhere (unlike the other clients' DPAPI-protected
storage) - Blender runs on Windows/macOS/Linux with no common secure-storage primitive in the
standard library, and storing it in plain text would be a false sense of security rather than a
real equivalent. The user re-enters it each Blender session; only the server URL and username are
remembered.
"""

import bpy


def _enum_items_from(attr_name):
    """Returns an EnumProperty items callback reading a newline-joined string of server-provided
    values (topic types/statuses/priorities/users - see operators.py's _refresh_extensions) off
    the session. A plain newline-joined StringProperty rather than a nested CollectionProperty
    since that's the simplest thing an EnumProperty items callback can rebuild from on every
    redraw. Caches the built list on the function object itself so the (identifier, name,
    description) tuples stay referenced after returning - Blender's own documented requirement for
    dynamic EnumProperty items callbacks; letting a freshly-built local list get garbage collected
    is a real, known crash source, not just a style nit."""

    def items(self, context):
        raw = getattr(context.window_manager.openbcf, attr_name, "")
        values = [v for v in raw.split("\n") if v] if raw else []
        items.cache = [(v, v, "") for v in values] or [("", "(none)", "")]
        return items.cache

    return items


_topic_type_items = _enum_items_from("extension_topic_types")
_topic_status_items = _enum_items_from("extension_topic_statuses")
_priority_items = _enum_items_from("extension_priorities")
_user_items = _enum_items_from("extension_users")


class OpenBcfPreferences(bpy.types.AddonPreferences):
    bl_idname = __package__

    server_url: bpy.props.StringProperty(
        name="Server URL",
        description="Base URL of a buildingSMART BCF REST API server",
        default="https://REDACTED-server.invalid",
    )
    username: bpy.props.StringProperty(name="Username", default="")

    def draw(self, context):
        layout = self.layout
        layout.prop(self, "server_url")
        layout.prop(self, "username")


class OpenBcfTopicItem(bpy.types.PropertyGroup):
    guid: bpy.props.StringProperty()
    title: bpy.props.StringProperty()
    topic_type: bpy.props.StringProperty()
    topic_status: bpy.props.StringProperty()
    priority: bpy.props.StringProperty()
    description: bpy.props.StringProperty()
    assigned_to: bpy.props.StringProperty()
    due_date: bpy.props.StringProperty()
    creation_author: bpy.props.StringProperty()
    creation_date: bpy.props.StringProperty()


class OpenBcfCommentItem(bpy.types.PropertyGroup):
    guid: bpy.props.StringProperty()
    author: bpy.props.StringProperty()
    date: bpy.props.StringProperty()
    text: bpy.props.StringProperty()


class OpenBcfViewpointItem(bpy.types.PropertyGroup):
    guid: bpy.props.StringProperty()
    image_name: bpy.props.StringProperty()


class OpenBcfProjectChoice(bpy.types.PropertyGroup):
    project_id: bpy.props.StringProperty()
    name: bpy.props.StringProperty()


class OpenBcfSession(bpy.types.PropertyGroup):
    # Connection
    password: bpy.props.StringProperty(name="Password", default="", subtype="PASSWORD")
    connected: bpy.props.BoolProperty(default=False)
    status_message: bpy.props.StringProperty(default="")
    version_id: bpy.props.StringProperty(default="")
    access_token: bpy.props.StringProperty(default="")

    # Project
    project_id: bpy.props.StringProperty(default="")
    project_name: bpy.props.StringProperty(default="")
    project_choices: bpy.props.CollectionProperty(type=OpenBcfProjectChoice)
    project_choice_index: bpy.props.IntProperty(default=0)
    awaiting_project_choice: bpy.props.BoolProperty(default=False)

    # Topics
    topics: bpy.props.CollectionProperty(type=OpenBcfTopicItem)
    active_topic_index: bpy.props.IntProperty(default=-1)
    comments: bpy.props.CollectionProperty(type=OpenBcfCommentItem)
    viewpoints: bpy.props.CollectionProperty(type=OpenBcfViewpointItem)

    # Project extensions (GetExtensions equivalent) - newline-joined lists of the server's actual
    # allowed values, feeding the New Topic / Edit Topic dropdowns below instead of free text.
    extension_topic_types: bpy.props.StringProperty(default="")
    extension_topic_statuses: bpy.props.StringProperty(default="")
    extension_priorities: bpy.props.StringProperty(default="")
    extension_users: bpy.props.StringProperty(default="")

    # New topic form
    new_topic_title: bpy.props.StringProperty(name="Title", default="")
    new_topic_description: bpy.props.StringProperty(name="Description", default="")
    new_topic_type: bpy.props.EnumProperty(name="Type", items=_topic_type_items)
    new_topic_status: bpy.props.EnumProperty(name="Status", items=_topic_status_items)
    new_topic_priority: bpy.props.EnumProperty(name="Priority", items=_priority_items)
    new_topic_assigned_to: bpy.props.EnumProperty(name="Assigned To", items=_user_items)
    new_topic_due_date: bpy.props.StringProperty(name="Due Date (YYYY-MM-DD)", default="")

    # Edit topic form - pre-filled from the active topic by openbcf.view_topic.
    edit_topic_type: bpy.props.EnumProperty(name="Type", items=_topic_type_items)
    edit_topic_status: bpy.props.EnumProperty(name="Status", items=_topic_status_items)
    edit_topic_priority: bpy.props.EnumProperty(name="Priority", items=_priority_items)
    edit_topic_assigned_to: bpy.props.EnumProperty(name="Assigned To", items=_user_items)
    edit_topic_due_date: bpy.props.StringProperty(name="Due Date (YYYY-MM-DD)", default="")

    # New comment form
    new_comment_text: bpy.props.StringProperty(name="Comment", default="")


CLASSES = (
    OpenBcfPreferences,
    OpenBcfTopicItem,
    OpenBcfCommentItem,
    OpenBcfViewpointItem,
    OpenBcfProjectChoice,
    OpenBcfSession,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.WindowManager.openbcf = bpy.props.PointerProperty(type=OpenBcfSession)


def unregister():
    del bpy.types.WindowManager.openbcf
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
