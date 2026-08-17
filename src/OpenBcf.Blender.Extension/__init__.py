from . import annotate, date_picker, operators, panels, properties


def register():
    properties.register()
    date_picker.register()
    annotate.register()
    operators.register()
    panels.register()


def unregister():
    panels.unregister()
    operators.unregister()
    annotate.unregister()
    date_picker.unregister()
    properties.unregister()
