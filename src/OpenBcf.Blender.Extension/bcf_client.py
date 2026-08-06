"""
HTTP client for the buildingSMART BCF REST API - the Python-side equivalent of
OpenBcf.Core.Protocol.BcfServerClient, kept endpoint-for-endpoint and field-for-field consistent
with it (same URL paths, same JSON keys) so both clients interoperate with the same servers,
notably the project's own test server bcf.chladny.de.

Deliberately uses only the standard library (urllib.request/json/base64) rather than "requests" -
Blender extensions can bundle third-party wheels, but there is no reason to here, and it keeps
this add-on's manifest free of any [build] wheels entry.
"""

import base64
import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Optional


class BcfApiError(Exception):
    """Raised for any non-2xx response, with the server's own error body when it sent one -
    mirrors BcfServerClient.EnsureSuccessAsync in the C# client, which reads the body before
    raising rather than discarding the server's actual validation error message."""


class BcfServerClient:
    def __init__(self, base_url: str, access_token: Optional[str] = None):
        self.base_url = base_url.rstrip("/")
        self.access_token = access_token

    # ------------------------------------------------------------------ #
    # Low-level request helpers
    # ------------------------------------------------------------------ #

    def _request(self, method: str, path: str, body: Optional[dict] = None, timeout: float = 15.0) -> Any:
        url = f"{self.base_url}/{path.lstrip('/')}"
        data = json.dumps(body).encode("utf-8") if body is not None else None

        headers = {"Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        if self.access_token:
            headers["Authorization"] = f"Bearer {self.access_token}"

        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                if response.status == 204 or not response.length:
                    return None
                return json.loads(response.read())
        except urllib.error.HTTPError as ex:
            detail = ex.read().decode("utf-8", errors="replace")
            raise BcfApiError(f"{ex.code} {ex.reason} from {method} {url}: {detail}") from ex

    def _get_bytes(self, path: str, timeout: float = 15.0) -> bytes:
        url = f"{self.base_url}/{path.lstrip('/')}"
        headers = {}
        if self.access_token:
            headers["Authorization"] = f"Bearer {self.access_token}"
        request = urllib.request.Request(url, headers=headers, method="GET")
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read()
        except urllib.error.HTTPError as ex:
            detail = ex.read().decode("utf-8", errors="replace")
            raise BcfApiError(f"{ex.code} {ex.reason} from GET {url}: {detail}") from ex

    @staticmethod
    def _segment(value: str) -> str:
        return urllib.parse.quote(value, safe="")

    # ------------------------------------------------------------------ #
    # Discovery / auth
    # ------------------------------------------------------------------ #

    def get_versions(self) -> list[dict]:
        return self._request("GET", "bcf/versions")["versions"]

    def get_auth_options(self, version_id: str) -> dict:
        return self._request("GET", f"bcf/{version_id}/auth")

    # ------------------------------------------------------------------ #
    # Projects
    # ------------------------------------------------------------------ #

    def get_projects(self, version_id: str) -> list[dict]:
        return self._request("GET", f"bcf/{version_id}/projects")

    def get_project_extensions(self, version_id: str, project_id: str) -> dict:
        return self._request("GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/extensions")

    # ------------------------------------------------------------------ #
    # Topics
    # ------------------------------------------------------------------ #

    def get_topics(self, version_id: str, project_id: str) -> list[dict]:
        return self._request("GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics")

    def get_topic(self, version_id: str, project_id: str, topic_guid: str) -> dict:
        return self._request("GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}")

    def create_topic(self, version_id: str, project_id: str, topic: dict) -> dict:
        # Mirrors TopicWriteDto: the server assigns guid/creation_date/modified_* itself - sending
        # them (even blank) fails schema validation with a 422.
        return self._request("POST", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics", topic)

    def update_topic(self, version_id: str, project_id: str, topic_guid: str, topic: dict) -> dict:
        return self._request("PUT", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}", topic)

    def delete_topic(self, version_id: str, project_id: str, topic_guid: str) -> None:
        self._request("DELETE", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}")

    # ------------------------------------------------------------------ #
    # Comments
    # ------------------------------------------------------------------ #

    def get_comments(self, version_id: str, project_id: str, topic_guid: str) -> list[dict]:
        return self._request("GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/comments")

    def create_comment(self, version_id: str, project_id: str, topic_guid: str, comment: str, author: str) -> dict:
        body = {"comment": comment, "author": author}
        return self._request("POST", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/comments", body)

    # ------------------------------------------------------------------ #
    # Viewpoints
    # ------------------------------------------------------------------ #

    def get_viewpoints(self, version_id: str, project_id: str, topic_guid: str) -> list[dict]:
        return self._request("GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/viewpoints")

    def get_viewpoint(self, version_id: str, project_id: str, topic_guid: str, viewpoint_guid: str) -> dict:
        return self._request(
            "GET", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/viewpoints/{viewpoint_guid}"
        )

    def create_viewpoint(self, version_id: str, project_id: str, topic_guid: str, viewpoint: dict) -> dict:
        return self._request(
            "POST", f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/viewpoints", viewpoint
        )

    def get_snapshot(self, version_id: str, project_id: str, topic_guid: str, viewpoint_guid: str) -> bytes:
        return self._get_bytes(
            f"bcf/{version_id}/projects/{self._segment(project_id)}/topics/{topic_guid}/viewpoints/{viewpoint_guid}/snapshot"
        )


def build_viewpoint_write_body(
    camera: Optional[dict], selection_ifc_guids: list[str], snapshot_png_bytes: Optional[bytes]
) -> dict:
    """
    Builds a viewpoint creation body in BOTH the spec-compliant nested shape (perspective_camera/
    orthogonal_camera/components) AND bcf.chladny.de's actual flat shape (camera_view_point/
    camera_direction/... and a top-level "selection" IFC GUID array) - mirrors
    BcfRestMapper.ToWriteDto in OpenBcf.Core exactly, including the "snapshot_base64" field that
    server expects instead of the spec's nested "snapshot" object. A spec-compliant server ignores
    the extra flat/snapshot_base64 fields; bcf.chladny.de ignores the nested ones - sending both
    is what makes a viewpoint's camera/selection/snapshot actually round-trip on this server (see
    ViewpointDto.cs's comments for how this was discovered).
    """
    body: dict[str, Any] = {
        "lines": [],
        "clipping_planes": [],
        "bitmaps": [],
        "default_visibility": True,
        "visibility_exceptions": [],
        "coloring": [],
        "selection": list(selection_ifc_guids),
        "components": {
            "selection": [{"ifc_guid": guid} for guid in selection_ifc_guids],
        },
    }

    if camera is not None:
        camera_dto = {
            "camera_view_point": _point_dto(camera["view_point"]),
            "camera_direction": _point_dto(camera["direction"]),
            "camera_up_vector": _point_dto(camera["up_vector"]),
        }
        is_orthogonal = camera["type"] == "orthogonal"
        if is_orthogonal:
            camera_dto["view_to_world_scale"] = camera.get("view_to_world_scale")
            body["orthogonal_camera"] = camera_dto
        else:
            camera_dto["field_of_view"] = camera.get("field_of_view")
            body["perspective_camera"] = camera_dto

        body["is_orthogonal"] = is_orthogonal
        body["camera_view_point"] = camera_dto["camera_view_point"]
        body["camera_direction"] = camera_dto["camera_direction"]
        body["camera_up_vector"] = camera_dto["camera_up_vector"]
        body["field_of_view"] = camera.get("field_of_view")
        body["view_to_world_scale"] = camera.get("view_to_world_scale")

    if snapshot_png_bytes:
        snapshot_base64 = base64.b64encode(snapshot_png_bytes).decode("ascii")
        body["snapshot"] = {"snapshot_type": "png", "snapshot_data": snapshot_base64}
        body["snapshot_base64"] = snapshot_base64

    return body


def parse_viewpoint_camera(viewpoint_dto: dict) -> Optional[dict]:
    """Inverse of build_viewpoint_write_body's camera fields - reads whichever shape a GET
    response actually used (nested perspective_camera/orthogonal_camera, or bcf.chladny.de's flat
    is_orthogonal/camera_* fields), mirroring BcfRestMapper.ToDomainCamera's fallback order."""
    perspective = viewpoint_dto.get("perspective_camera")
    if perspective:
        return {
            "type": "perspective",
            "view_point": _point_tuple(perspective["camera_view_point"]),
            "direction": _point_tuple(perspective["camera_direction"]),
            "up_vector": _point_tuple(perspective["camera_up_vector"]),
            "field_of_view": perspective.get("field_of_view", 60),
        }

    orthogonal = viewpoint_dto.get("orthogonal_camera")
    if orthogonal:
        return {
            "type": "orthogonal",
            "view_point": _point_tuple(orthogonal["camera_view_point"]),
            "direction": _point_tuple(orthogonal["camera_direction"]),
            "up_vector": _point_tuple(orthogonal["camera_up_vector"]),
            "view_to_world_scale": orthogonal.get("view_to_world_scale", 1.0),
        }

    if viewpoint_dto.get("camera_view_point") is not None:
        is_orthogonal = bool(viewpoint_dto.get("is_orthogonal"))
        result = {
            "type": "orthogonal" if is_orthogonal else "perspective",
            "view_point": _point_tuple(viewpoint_dto["camera_view_point"]),
            "direction": _point_tuple(viewpoint_dto["camera_direction"]),
            "up_vector": _point_tuple(viewpoint_dto["camera_up_vector"]),
        }
        if is_orthogonal:
            result["view_to_world_scale"] = viewpoint_dto.get("view_to_world_scale", 1.0)
        else:
            result["field_of_view"] = viewpoint_dto.get("field_of_view", 60)
        return result

    return None


def parse_viewpoint_selection(viewpoint_dto: dict) -> list[str]:
    """Reads selected IFC GUIDs from either shape - nested components.selection[].ifc_guid, or
    bcf.chladny.de's flat "selection" array (which the server can echo back either as plain
    strings or, with a Speckle bridge, as {"ifc_guid": ..., "speckle_id": ...} objects - see
    FlexibleGuidListConverter in ViewpointDto.cs for why both are handled here too)."""
    components = viewpoint_dto.get("components")
    if components and components.get("selection"):
        return [c["ifc_guid"] for c in components["selection"] if c.get("ifc_guid")]

    flat_selection = viewpoint_dto.get("selection")
    if flat_selection:
        result = []
        for entry in flat_selection:
            if isinstance(entry, str):
                result.append(entry)
            elif isinstance(entry, dict) and entry.get("ifc_guid"):
                result.append(entry["ifc_guid"])
        return result

    return []


def _point_dto(point: tuple) -> dict:
    return {"x": point[0], "y": point[1], "z": point[2]}


def _point_tuple(point_dto: dict) -> tuple:
    return (point_dto["x"], point_dto["y"], point_dto["z"])
