"""
A calendar popup for the New/Edit Topic "Due Date" fields - Blender's UI toolkit has no built-in
date-picker widget, so this builds one: prev/next-month navigation plus a 7-wide grid of day
buttons, writing "YYYY-MM-DD" straight into whichever session field `target` names.

Uses invoke_popup (not invoke_props_dialog): a props_dialog stays open and redraws in place via
its check() callback, but can't cleanly host independently-clickable day buttons inside it - each
day needs to be its own operator call, and firing one from inside another operator's modal dialog
doesn't compose well. invoke_popup's actual behavior - closing on ANY button click inside it - is
instead put to use directly: every button here, including month navigation, re-invokes this same
operator with different year/month/day, so "closes and reopens" IS the navigation mechanism, not a
workaround for it.
"""

import calendar
import datetime

import bpy


def _current_value(context, target: str) -> str:
    return getattr(context.window_manager.openbcf, target, "") or ""


class OPENBCF_OT_pick_date(bpy.types.Operator):
    bl_idname = "openbcf.pick_date"
    bl_label = "Pick a Date"
    bl_options = {"REGISTER", "INTERNAL"}

    target: bpy.props.StringProperty()
    year: bpy.props.IntProperty()
    month: bpy.props.IntProperty()
    # 0 = just opening/navigating (no day chosen yet), -1 = "Clear" was clicked, >0 = that day.
    day: bpy.props.IntProperty(default=0)

    def invoke(self, context, event):
        if self.day == -1:
            setattr(context.window_manager.openbcf, self.target, "")
            return {"FINISHED"}
        if self.day > 0:
            setattr(context.window_manager.openbcf, self.target, f"{self.year:04d}-{self.month:02d}-{self.day:02d}")
            return {"FINISHED"}

        if not self.year or not self.month:
            current = _current_value(context, self.target)
            try:
                parsed = datetime.date.fromisoformat(current[:10]) if current else datetime.date.today()
            except ValueError:
                parsed = datetime.date.today()
            self.year, self.month = parsed.year, parsed.month

        return context.window_manager.invoke_popup(self, width=220)

    def execute(self, context):
        # invoke() already did the real work (committing a day/clear) before ever reaching here,
        # or opened a popup whose own button clicks are separate operator invocations - nothing
        # left to do in the "just opened/navigated" case.
        return {"FINISHED"}

    def draw(self, context):
        layout = self.layout

        nav = layout.row(align=True)
        prev_year, prev_month = (self.year, self.month - 1) if self.month > 1 else (self.year - 1, 12)
        next_year, next_month = (self.year, self.month + 1) if self.month < 12 else (self.year + 1, 1)

        op = nav.operator("openbcf.pick_date", text="<")
        op.target, op.year, op.month = self.target, prev_year, prev_month
        nav.label(text=f"{calendar.month_name[self.month]} {self.year}")
        op = nav.operator("openbcf.pick_date", text=">")
        op.target, op.year, op.month = self.target, next_year, next_month

        grid = layout.grid_flow(row_major=True, columns=7, even_columns=True, even_rows=True)
        for name in ("Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"):
            grid.label(text=name)
        for day in calendar.Calendar(firstweekday=0).itermonthdays(self.year, self.month):
            if day == 0:
                grid.label(text="")
            else:
                op = grid.operator("openbcf.pick_date", text=str(day))
                op.target, op.year, op.month, op.day = self.target, self.year, self.month, day

        layout.separator()
        clear = layout.operator("openbcf.pick_date", text="Clear")
        clear.target, clear.year, clear.month, clear.day = self.target, self.year, self.month, -1


CLASSES = (OPENBCF_OT_pick_date,)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
