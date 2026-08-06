"""
OAuth2 Authorization Code grant (RFC 6749) with dynamic client registration (RFC 7591) - the
Python-side equivalent of OpenBcf.Core.Protocol.BcfOAuthAuthorizationCodeFlow's "silent"
credentials path. The BCF API spec has no "password" grant, so signing in still goes through
this flow even though the user only ever sees a username/password form: the credentials are
submitted directly to the server's own authorization endpoint (confirmed, for REDACTED-server.invalid's
bcf-bridge, to be a plain email/password form with no other session/CSRF requirement - see the
C# implementation's comments) rather than opening a browser.

Unlike the C# client, there is no interactive-browser-popup fallback here (that needs a local
HTTP listener + opening a system browser + waiting for the redirect - a real feature, deliberately
left out of this first version rather than half-implemented). If a server's login page doesn't
match the known bcf-bridge contract, sign-in fails with a clear error instead of silently trying
something unverified.
"""

import json
import urllib.error
import urllib.parse
import urllib.request
import uuid
from typing import Optional


class OAuthSignInError(Exception):
    pass


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Returning None here (rather than building the normal redirected request) is what makes
    urlopen raise an HTTPError for the 302 instead of transparently following it - the only way to
    read the Location header (carrying ?code=...) ourselves instead of a listener catching it."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def register_client(registration_url: str, redirect_uri: str) -> dict:
    body = json.dumps(
        {
            "redirect_uris": [redirect_uri],
            "client_name": "openBCF",
            "grant_types": ["authorization_code", "refresh_token"],
            "response_types": ["code"],
        }
    ).encode("utf-8")

    request = urllib.request.Request(
        registration_url, data=body, headers={"Content-Type": "application/json", "Accept": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.loads(response.read())


def sign_in_with_credentials(auth_options: dict, redirect_uri: str, client: dict, username: str, password: str) -> dict:
    """Returns the token response dict (access_token/expires_in/refresh_token) or raises
    OAuthSignInError. redirect_uri is never actually listened on - see the module docstring."""

    state = uuid.uuid4().hex
    form = urllib.parse.urlencode(
        {
            "client_id": client["client_id"],
            "redirect_uri": redirect_uri,
            "response_type": "code",
            "code_challenge": "",
            "code_challenge_method": "",
            "state": state,
            "scope": "",
            "email": username,
            "password": password,
        }
    ).encode("utf-8")

    opener = urllib.request.build_opener(_NoRedirectHandler)
    request = urllib.request.Request(
        auth_options["oauth2_auth_url"],
        data=form,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )

    try:
        response = opener.open(request, timeout=15)
        # A 200 here means the server did not redirect at all - not the expected contract.
        response.read()
        raise OAuthSignInError("Sign-in did not redirect as expected - this server's login page may require a browser.")
    except urllib.error.HTTPError as ex:
        if ex.code == 401:
            raise OAuthSignInError("Sign-in failed - check your username and password.") from ex
        if ex.code not in (301, 302, 303, 307, 308):
            raise OAuthSignInError(f"Unexpected sign-in response: {ex.code} {ex.reason}") from ex

        location = ex.headers.get("Location")
        if not location:
            raise OAuthSignInError("Sign-in redirect had no Location header.") from ex

        query = urllib.parse.urlparse(urllib.parse.urljoin(redirect_uri, location)).query
        params = dict(urllib.parse.parse_qsl(query))

        if "error" in params:
            raise OAuthSignInError(f"The server denied sign-in: {params['error']}.") from ex
        if params.get("code") is None or params.get("state") != state:
            raise OAuthSignInError("Sign-in did not return the expected authorization code.") from ex

        return _exchange_code_for_token(auth_options["oauth2_token_url"], client, redirect_uri, params["code"])


def _exchange_code_for_token(token_url: str, client: dict, redirect_uri: str, code: str) -> dict:
    form_fields = {
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": redirect_uri,
        "client_id": client["client_id"],
    }

    headers = {"Content-Type": "application/x-www-form-urlencoded"}
    token_auth_method = client.get("token_endpoint_auth_method")
    client_secret = client.get("client_secret")

    if token_auth_method == "client_secret_basic" and client_secret:
        import base64

        raw = f"{client['client_id']}:{client_secret}".encode("utf-8")
        headers["Authorization"] = f"Basic {base64.b64encode(raw).decode('ascii')}"
    elif client_secret:
        form_fields["client_secret"] = client_secret

    request = urllib.request.Request(
        token_url, data=urllib.parse.urlencode(form_fields).encode("utf-8"), headers=headers, method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as ex:
        detail = ex.read().decode("utf-8", errors="replace")
        raise OAuthSignInError(f"Token exchange failed: {ex.code} {ex.reason}: {detail}") from ex


def get_free_loopback_port() -> int:
    import socket

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def authenticate_with_password(auth_options: dict, username: str, password: str) -> str:
    """Full sign-in: register a client, submit credentials, exchange the code - returns an
    access_token. Raises OAuthSignInError with a clear reason on any failure."""

    if not auth_options.get("oauth2_dynamic_client_reg_url"):
        raise OAuthSignInError("The server did not advertise a dynamic client registration endpoint.")
    if not auth_options.get("oauth2_auth_url") or not auth_options.get("oauth2_token_url"):
        raise OAuthSignInError("The server did not advertise OAuth2 authorization/token endpoints.")

    redirect_uri = f"http://127.0.0.1:{get_free_loopback_port()}/callback/"
    client = register_client(auth_options["oauth2_dynamic_client_reg_url"], redirect_uri)
    token = sign_in_with_credentials(auth_options, redirect_uri, client, username, password)
    return token["access_token"]
