"""
Holds the live BcfServerClient instance for the current Blender session - a plain Python object,
so it can't live on a bpy.types.PropertyGroup (which only supports bpy's own property types)
the way OpenBcfSession in properties.py holds the rest of the session's state. Mirrors the other
clients' static BcfSession holder classes.
"""

from typing import Optional

from .bcf_client import BcfServerClient

_client: Optional[BcfServerClient] = None


def get_client() -> Optional[BcfServerClient]:
    return _client


def set_client(client: BcfServerClient) -> None:
    global _client
    _client = client


def clear() -> None:
    global _client
    _client = None
