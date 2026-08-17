"""
Local .bcfzip archive read/write - the Python-side equivalent of OpenBcf.Core.Serialization's
BcfArchive/BcfMarkupSerializer/BcfVisualizationSerializer, kept element-for-element consistent so
files this add-on writes open in the other openBCF clients (and any spec-compliant BCF viewer),
and files they write open here.

Deliberately narrower than the full BCF spec those C# serializers implement: Blender's own topic/
viewpoint model (bcf_client.py, operators.py) never carries labels, BIM snippets, document
references, related topics, coloring, clipping planes, lines, or bitmaps, so there is nothing to
round-trip for those - only what this add-on actually reads/writes elsewhere (topic core fields,
comments, one camera + IFC-GUID selection + snapshot per viewpoint) is implemented here. Uses only
the standard library (zipfile/xml.etree), matching bcf_client.py's own stdlib-only convention.
"""

import xml.etree.ElementTree as ET
import zipfile
from typing import Any, Optional

_VERSION_ENTRY = "bcf.version"
_PROJECT_ENTRY = "project.bcf"
_MARKUP_ENTRY = "markup.bcf"


def _point_element(tag: str, point: tuple) -> ET.Element:
    el = ET.Element(tag)
    for axis, value in zip("XYZ", point):
        ET.SubElement(el, axis).text = repr(float(value))
    return el


def _point_from_element(el: Optional[ET.Element]) -> tuple:
    if el is None:
        return (0.0, 0.0, 0.0)
    return tuple(float(el.findtext(axis, "0")) for axis in "XYZ")


def _sub(parent: ET.Element, tag: str, value: Optional[str]) -> None:
    """Adds `<tag>value</tag>` only when value is present - mirrors the C# serializers' `if (x is
    not null)` guards, since an empty element would round-trip as "" instead of missing/None."""
    if value:
        ET.SubElement(parent, tag).text = value


def write(path: str, version_id: str, project_id: str, project_name: str, topics: list[dict]) -> None:
    """`topics` is a list of {"topic": <get_topic()-shaped dict>, "comments": [...],
    "viewpoints": [{"camera": <camera dict or None>, "selection_ifc_guids": [...],
    "snapshot_png_bytes": bytes or None}, ...]}."""
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
        version_root = ET.Element("Version", {"VersionId": version_id})
        zf.writestr(_VERSION_ENTRY, ET.tostring(version_root, encoding="unicode", xml_declaration=True))

        project_el = ET.Element("Project", {"ProjectId": project_id})
        _sub(project_el, "Name", project_name)
        project_root = ET.Element("ProjectExtension")
        project_root.append(project_el)
        zf.writestr(_PROJECT_ENTRY, ET.tostring(project_root, encoding="unicode", xml_declaration=True))

        for entry in topics:
            t = entry["topic"]
            folder = t["guid"]

            markup_root = ET.Element("Markup")
            topic_attrs = {"Guid": t["guid"]}
            if t.get("topic_type"):
                topic_attrs["TopicType"] = t["topic_type"]
            if t.get("topic_status"):
                topic_attrs["TopicStatus"] = t["topic_status"]
            topic_el = ET.SubElement(markup_root, "Topic", topic_attrs)
            ET.SubElement(topic_el, "Title").text = t.get("title") or ""
            _sub(topic_el, "Priority", t.get("priority"))
            _sub(topic_el, "CreationDate", t.get("creation_date"))
            _sub(topic_el, "CreationAuthor", t.get("creation_author"))
            _sub(topic_el, "DueDate", t.get("due_date"))
            _sub(topic_el, "AssignedTo", t.get("assigned_to"))
            _sub(topic_el, "Description", t.get("description"))

            for c in entry.get("comments", []):
                comment_el = ET.SubElement(markup_root, "Comment", {"Guid": c["guid"]})
                ET.SubElement(comment_el, "Date").text = c.get("date") or ""
                ET.SubElement(comment_el, "Author").text = c.get("author") or ""
                ET.SubElement(comment_el, "Comment").text = c.get("comment") or ""

            for index, vp in enumerate(entry.get("viewpoints", [])):
                vp_filename = "viewpoint.bcfv" if index == 0 else f"viewpoint_{index}.bcfv"
                snapshot_filename = None

                vis_root = ET.Element("VisualizationInfo", {"Guid": vp.get("guid") or f"vp-{index}"})
                guids = vp.get("selection_ifc_guids") or []
                if guids:
                    components_el = ET.SubElement(vis_root, "Components")
                    selection_el = ET.SubElement(components_el, "Selection")
                    for guid in guids:
                        ET.SubElement(selection_el, "Component", {"IfcGuid": guid})

                camera = vp.get("camera")
                if camera is not None:
                    is_ortho = camera.get("type") == "orthogonal"
                    cam_el = ET.SubElement(vis_root, "OrthogonalCamera" if is_ortho else "PerspectiveCamera")
                    cam_el.append(_point_element("CameraViewPoint", camera["view_point"]))
                    cam_el.append(_point_element("CameraDirection", camera["direction"]))
                    cam_el.append(_point_element("CameraUpVector", camera["up_vector"]))
                    if is_ortho:
                        ET.SubElement(cam_el, "ViewToWorldScale").text = repr(float(camera.get("view_to_world_scale") or 1))
                    else:
                        ET.SubElement(cam_el, "FieldOfView").text = repr(float(camera.get("field_of_view") or 60))

                zf.writestr(f"{folder}/{vp_filename}", ET.tostring(vis_root, encoding="unicode", xml_declaration=True))

                snapshot_bytes = vp.get("snapshot_png_bytes")
                if snapshot_bytes:
                    snapshot_filename = "snapshot.png" if index == 0 else f"snapshot_{index}.png"
                    zf.writestr(f"{folder}/{snapshot_filename}", snapshot_bytes)

                vp_ref_attrs = {"Guid": vp.get("guid") or f"vp-{index}"}
                vp_ref_el = ET.SubElement(markup_root, "Viewpoints", vp_ref_attrs)
                ET.SubElement(vp_ref_el, "Viewpoint").text = vp_filename
                if snapshot_filename:
                    ET.SubElement(vp_ref_el, "Snapshot").text = snapshot_filename

            zf.writestr(f"{folder}/{_MARKUP_ENTRY}", ET.tostring(markup_root, encoding="unicode", xml_declaration=True))


def read(path: str) -> dict:
    """Returns {"version_id":..., "project_id":..., "project_name":..., "topics": [{"topic": {...
    same shape write() takes}, "comments": [...], "viewpoints": [{"camera":..., "
    selection_ifc_guids": [...], "snapshot_png_bytes": bytes or None}]}]}."""
    with zipfile.ZipFile(path, "r") as zf:
        names = zf.namelist()

        version_id = ""
        version_name = next((n for n in names if "/" not in n and n.lower() == _VERSION_ENTRY), None)
        if version_name:
            version_id = ET.fromstring(zf.read(version_name)).get("VersionId", "")

        project_id, project_name = "", None
        project_entry_name = next((n for n in names if "/" not in n and n.lower() == _PROJECT_ENTRY), None)
        if project_entry_name:
            project_el = ET.fromstring(zf.read(project_entry_name)).find("Project")
            if project_el is not None:
                project_id = project_el.get("ProjectId", "")
                project_name = project_el.findtext("Name")

        folders: dict[str, list[str]] = {}
        for name in names:
            if "/" not in name:
                continue
            folder = name.split("/", 1)[0]
            folders.setdefault(folder, []).append(name)

        topics = []
        for folder, entries in folders.items():
            markup_name = next((n for n in entries if n.rsplit("/", 1)[-1].lower() == _MARKUP_ENTRY), None)
            if markup_name is None:
                continue

            markup_root = ET.fromstring(zf.read(markup_name))
            topic_el = markup_root.find("Topic")
            if topic_el is None:
                continue

            topic = {
                "guid": topic_el.get("Guid") or folder,
                "topic_type": topic_el.get("TopicType"),
                "topic_status": topic_el.get("TopicStatus"),
                "title": topic_el.findtext("Title") or "",
                "priority": topic_el.findtext("Priority"),
                "creation_date": topic_el.findtext("CreationDate"),
                "creation_author": topic_el.findtext("CreationAuthor"),
                "due_date": topic_el.findtext("DueDate"),
                "assigned_to": topic_el.findtext("AssignedTo"),
                "description": topic_el.findtext("Description"),
            }

            comments = []
            for comment_el in markup_root.findall("Comment"):
                comments.append({
                    "guid": comment_el.get("Guid"),
                    "date": comment_el.findtext("Date"),
                    "author": comment_el.findtext("Author"),
                    "comment": comment_el.findtext("Comment") or "",
                })

            viewpoints = []
            for vp_ref_el in markup_root.findall("Viewpoints"):
                vp_filename = vp_ref_el.findtext("Viewpoint")
                snapshot_filename = vp_ref_el.findtext("Snapshot")
                if not vp_filename:
                    continue

                vp_entry_name = f"{folder}/{vp_filename}"
                if vp_entry_name not in entries:
                    continue

                vis_root = ET.fromstring(zf.read(vp_entry_name))
                camera = None
                for tag, cam_type in (("PerspectiveCamera", "perspective"), ("OrthogonalCamera", "orthogonal")):
                    cam_el = vis_root.find(tag)
                    if cam_el is None:
                        continue
                    camera = {
                        "type": cam_type,
                        "view_point": _point_from_element(cam_el.find("CameraViewPoint")),
                        "direction": _point_from_element(cam_el.find("CameraDirection")),
                        "up_vector": _point_from_element(cam_el.find("CameraUpVector")),
                    }
                    if cam_type == "orthogonal":
                        camera["view_to_world_scale"] = float(cam_el.findtext("ViewToWorldScale", "1"))
                    else:
                        camera["field_of_view"] = float(cam_el.findtext("FieldOfView", "60"))
                    break

                selection_ifc_guids = [
                    c.get("IfcGuid") for c in vis_root.findall("./Components/Selection/Component") if c.get("IfcGuid")
                ]

                snapshot_png_bytes = None
                if snapshot_filename:
                    snapshot_entry_name = f"{folder}/{snapshot_filename}"
                    if snapshot_entry_name in entries:
                        snapshot_png_bytes = zf.read(snapshot_entry_name)

                viewpoints.append({
                    "camera": camera,
                    "selection_ifc_guids": selection_ifc_guids,
                    "snapshot_png_bytes": snapshot_png_bytes,
                })

            topics.append({"topic": topic, "comments": comments, "viewpoints": viewpoints})

        return {"version_id": version_id, "project_id": project_id, "project_name": project_name, "topics": topics}
