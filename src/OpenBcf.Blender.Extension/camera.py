"""
Captures/applies a BCF camera against Blender's active 3D viewport (bpy.types.RegionView3D) -
build- and run-verified for real in a live Blender 5.2 GUI session (capture_from_region_3d/
apply_to_region_3d round-tripped a test camera through a real viewport with ~1e-7 error - see
project memory for the full session). That live test caught and fixed two real bugs
apply_to_region_3d originally had: assigning view_matrix alone does not switch projection mode
(view_perspective must be set explicitly) or survive the next redraw in orthographic mode (a
viewport's actual internal state is view_location/view_rotation/view_distance, not a free
view_matrix - the relationship view_location = eye_position + forward * view_distance was
confirmed empirically against the live viewport, not guessed from docs).

Camera convention: Blender's camera (and, equivalently, RegionView3D's view orientation) looks
down its local -Z axis with +Y as up - this is Blender's own long-standing, stable convention
(unrelated to and unlike Tekla/Revit/ARCHICAD's own camera axis conventions in the other clients).
"""

import math
from typing import Optional

import mathutils


def capture_from_region_3d(region_3d) -> dict:
    """region_3d: bpy.types.RegionView3D (e.g. bpy.context.space_data.region_3d in a 3D Viewport)."""

    # view_matrix is world-to-view; its inverse's translation/rotation directly give the eye
    # position and orientation in world space - no need to reconstruct from view_location/
    # view_distance/view_rotation separately.
    eye_matrix = region_3d.view_matrix.inverted()
    position = eye_matrix.translation
    rotation = eye_matrix.to_3x3()
    forward = (rotation @ mathutils.Vector((0, 0, -1))).normalized()
    up = (rotation @ mathutils.Vector((0, 1, 0))).normalized()

    is_camera_locked = region_3d.view_perspective == "CAMERA"
    is_perspective = region_3d.is_perspective or is_camera_locked

    if is_perspective:
        field_of_view_degrees = _perspective_fov_degrees(region_3d)
        return {
            "type": "perspective",
            "view_point": tuple(position),
            "direction": tuple(forward),
            "up_vector": tuple(up),
            "field_of_view": field_of_view_degrees,
        }

    return {
        "type": "orthogonal",
        "view_point": tuple(position),
        "direction": tuple(forward),
        "up_vector": tuple(up),
        # view_distance has no direct "world units visible" meaning for BCF's ViewToWorldScale
        # (defined as world_height / 2, per the BCF schema) - Blender's own zoom for an orthographic
        # viewport is better approximated from view_distance itself, which scales linearly with
        # the visible world-space height in orthographic projection.
        "view_to_world_scale": max(region_3d.view_distance, 0.001),
    }


def _perspective_fov_degrees(region_3d) -> float:
    """Derives the effective field of view from the projection matrix's [1][1] element - standard,
    Blender-independent perspective-projection math (tan(fov/2) = 1/proj[1][1]), used because a
    plain viewport (not locked to a scene camera) has no single "lens angle" property of its own,
    only the combined perspective_matrix."""

    proj_11 = region_3d.window_matrix[1][1]
    if proj_11 <= 0:
        return 60.0
    return math.degrees(2 * math.atan(1.0 / proj_11))


def apply_to_region_3d(region_3d, camera: dict) -> None:
    """Inverse of capture_from_region_3d - moves the viewport's camera to match a BCF camera dict
    (as produced by bcf_client.parse_viewpoint_camera)."""

    position = mathutils.Vector(camera["view_point"])
    forward = mathutils.Vector(camera["direction"]).normalized()
    up = mathutils.Vector(camera["up_vector"]).normalized()

    right = forward.cross(up).normalized()
    true_up = right.cross(forward).normalized()

    rotation = mathutils.Matrix((right, true_up, -forward)).transposed().to_3x3()
    eye_matrix = rotation.to_4x4()
    eye_matrix.translation = position

    # Confirmed by live-viewport testing (a real GUI Blender session, not just docs): a viewport
    # left in orthographic mode ignores an assigned view_matrix's translation on the next
    # redraw/update() - Blender's actual internal representation for viewport navigation is
    # view_location/view_rotation/view_distance, not a free view_matrix (view_matrix looked
    # correct only until something else touched the derived state). is_perspective/
    # view_perspective must be set explicitly too - assigning view_matrix alone does not switch
    # projection mode.
    region_3d.view_perspective = "PERSP" if camera["type"] == "perspective" else "ORTHO"
    region_3d.view_distance = 10.0
    region_3d.view_rotation = rotation.to_quaternion()
    # Empirically confirmed (same live test): view_location is the pivot the camera orbits, sitting
    # view_distance in front of the eye along the view direction - i.e. where the eye would end up
    # looking at is eye_position + forward * view_distance.
    region_3d.view_location = position + forward * region_3d.view_distance
    region_3d.update()
