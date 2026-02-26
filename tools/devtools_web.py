#!/usr/bin/env python3
"""
devtools_web.py - lightweight local web bridge for Harness DevTools.

Serves a static Web UI and exposes a small JSON API that forwards to
DevTools commands via user:// JSON files (same transport as devtools.py).
"""

from __future__ import annotations

import argparse
import json
from functools import partial
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

from devtools import send_command
from llm_bridge import LlmBridge, LlmBridgeError, LlmRequest
from secret_store import SecretStore, SecretStoreError


ALLOWED_ACTIONS = {
    "ping",
    "screenshot",
    "validate_scene",
    "validate_all_scenes",
    "scene_tree",
    "performance",
    "get_state",
    "set_state",
    "run_method",
    "input_press",
    "input_release",
    "input_tap",
    "input_clear",
    "input_actions",
    "input_sequence",
    "quit",
}


class DevToolsWebHandler(SimpleHTTPRequestHandler):
    """Serve static UI files and /api endpoints."""

    def __init__(self, *args, project_path: Path, web_root: Path, **kwargs):
        self._project_path = project_path
        self._web_root = web_root
        self._secrets = SecretStore()
        super().__init__(*args, directory=str(web_root), **kwargs)

    def end_headers(self) -> None:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        super().end_headers()

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(HTTPStatus.NO_CONTENT)
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/api/health":
            self._write_json(HTTPStatus.OK, {
                "ok": True,
                "project_path": str(self._project_path),
                "web_root": str(self._web_root),
            })
            return
        if self.path == "/api/llm/providers":
            self._write_json(HTTPStatus.OK, {"ok": True, "providers": LlmBridge.provider_info()})
            return
        if self.path == "/api/secrets/status":
            status = self._secrets.status()
            self._write_json(HTTPStatus.OK, {"ok": True, "backend": status.backend, "secure": status.secure})
            return
        if self.path == "/api/secrets/list":
            self._write_json(HTTPStatus.OK, {"ok": True, "names": self._secrets.list_names()})
            return
        if self.path in ("/", "/index.html"):
            self.path = "/index.html"
        return super().do_GET()

    def do_POST(self) -> None:  # noqa: N802
        if self.path == "/api/command":
            self._handle_devtools_command()
            return
        if self.path == "/api/llm/chat":
            self._handle_llm_chat()
            return
        if self.path == "/api/secrets/set":
            self._handle_secret_set()
            return
        if self.path == "/api/secrets/get":
            self._handle_secret_get()
            return
        if self.path == "/api/secrets/delete":
            self._handle_secret_delete()
            return
        self._write_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": "Not found"})

    def _handle_devtools_command(self) -> None:
        payload = self._read_json_body()
        if payload is None:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "Invalid JSON body"})
            return

        action = payload.get("action")
        cmd_args = payload.get("args") or {}
        timeout = payload.get("timeout")

        if action not in ALLOWED_ACTIONS:
            self._write_json(HTTPStatus.BAD_REQUEST, {
                "ok": False,
                "error": f"Action '{action}' is not allowed",
                "allowed_actions": sorted(ALLOWED_ACTIONS),
            })
            return

        if timeout is None:
            timeout = 30.0

        try:
            result = send_command(self._project_path, action, cmd_args, float(timeout))
            self._write_json(HTTPStatus.OK, {"ok": True, "result": result})
        except Exception as exc:  # pylint: disable=broad-except
            self._write_json(HTTPStatus.INTERNAL_SERVER_ERROR, {
                "ok": False,
                "error": str(exc),
                "action": action,
            })

    def _handle_llm_chat(self) -> None:
        payload = self._read_json_body()
        if payload is None:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "Invalid JSON body"})
            return

        try:
            api_key = payload.get("api_key")
            api_key_name = payload.get("api_key_name")
            if not api_key and api_key_name:
                api_key = self._secrets.get(api_key_name)
                if not api_key:
                    raise LlmBridgeError(f"No API key found for '{api_key_name}'")

            llm_request = LlmRequest(
                provider=payload.get("provider", ""),
                model=payload.get("model", ""),
                prompt=payload.get("prompt", ""),
                api_key=api_key,
                base_url=payload.get("base_url"),
                temperature=payload.get("temperature"),
                max_tokens=payload.get("max_tokens"),
                timeout=float(payload.get("timeout", 60)),
            )
            result = LlmBridge.chat(llm_request)
            self._write_json(HTTPStatus.OK, {"ok": True, "result": result})
        except LlmBridgeError as exc:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": str(exc)})
        except Exception as exc:  # pylint: disable=broad-except
            self._write_json(HTTPStatus.INTERNAL_SERVER_ERROR, {"ok": False, "error": str(exc)})

    def _handle_secret_set(self) -> None:
        payload = self._read_json_body()
        if payload is None:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "Invalid JSON body"})
            return
        try:
            name = payload.get("name", "")
            value = payload.get("value", "")
            self._secrets.set(name, value)
            self._write_json(HTTPStatus.OK, {"ok": True, "name": name})
        except SecretStoreError as exc:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": str(exc)})
        except Exception as exc:  # pylint: disable=broad-except
            self._write_json(HTTPStatus.INTERNAL_SERVER_ERROR, {"ok": False, "error": str(exc)})

    def _handle_secret_get(self) -> None:
        payload = self._read_json_body()
        if payload is None:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "Invalid JSON body"})
            return
        try:
            name = payload.get("name", "")
            value = self._secrets.get(name)
            if value is None:
                self._write_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": f"Secret '{name}' not found"})
                return
            self._write_json(HTTPStatus.OK, {"ok": True, "name": name, "value": value})
        except SecretStoreError as exc:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": str(exc)})
        except Exception as exc:  # pylint: disable=broad-except
            self._write_json(HTTPStatus.INTERNAL_SERVER_ERROR, {"ok": False, "error": str(exc)})

    def _handle_secret_delete(self) -> None:
        payload = self._read_json_body()
        if payload is None:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "Invalid JSON body"})
            return
        try:
            name = payload.get("name", "")
            self._secrets.delete(name)
            self._write_json(HTTPStatus.OK, {"ok": True, "name": name})
        except SecretStoreError as exc:
            self._write_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": str(exc)})
        except Exception as exc:  # pylint: disable=broad-except
            self._write_json(HTTPStatus.INTERNAL_SERVER_ERROR, {"ok": False, "error": str(exc)})

    def _read_json_body(self) -> dict[str, Any] | None:
        length = self.headers.get("Content-Length")
        if not length:
            return {}
        try:
            size = int(length)
            raw = self.rfile.read(size)
            return json.loads(raw.decode("utf-8"))
        except Exception:  # pylint: disable=broad-except
            return None

    def _write_json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
        encoded = json.dumps(payload, indent=2).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run Harness DevTools Web UI/API")
    parser.add_argument("--project", "-p", default=".", help="Path to Godot project (default: current dir)")
    parser.add_argument("--host", default="127.0.0.1", help="Bind host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8765, help="Bind port (default: 8765)")
    parser.add_argument("--web-root", default="webui", help="Static web root path (default: webui)")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    project_path = Path(args.project).resolve()
    web_root = Path(args.web_root).resolve()

    if not web_root.exists():
        raise FileNotFoundError(f"Web root not found: {web_root}")

    handler = partial(DevToolsWebHandler, project_path=project_path, web_root=web_root)
    server = ThreadingHTTPServer((args.host, args.port), handler)

    print(f"DevTools Web UI: http://{args.host}:{args.port}")
    print(f"Project path: {project_path}")
    print("Press Ctrl+C to stop")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping server...")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
