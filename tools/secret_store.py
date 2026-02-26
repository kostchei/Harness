#!/usr/bin/env python3
"""Secret storage abstraction for Harness web bridge.

Primary backend: OS keychain via `keyring`.
Fallback backend: local JSON file in user home when keyring is unavailable.
"""

from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any


SERVICE_NAME = "harness.devtools.webui"
INDEX_KEY = "__index__"
SECRET_NAME_RE = re.compile(r"^[A-Za-z0-9._-]{1,64}$")


class SecretStoreError(RuntimeError):
    """User-facing secret store error."""


@dataclass
class SecretStoreStatus:
    backend: str
    secure: bool


class SecretStore:
    """Store and retrieve API keys."""

    def __init__(self) -> None:
        self._backend = _create_backend()

    def status(self) -> SecretStoreStatus:
        return SecretStoreStatus(backend=self._backend.name, secure=self._backend.secure)

    def list_names(self) -> list[str]:
        names = self._read_index()
        return sorted(names)

    def get(self, name: str) -> str | None:
        clean = _validate_name(name)
        return self._backend.get(clean)

    def set(self, name: str, value: str) -> None:
        clean = _validate_name(name)
        if not value:
            raise SecretStoreError("Secret value cannot be empty")
        self._backend.set(clean, value)
        names = self._read_index()
        if clean not in names:
            names.append(clean)
            self._write_index(names)

    def delete(self, name: str) -> None:
        clean = _validate_name(name)
        self._backend.delete(clean)
        names = self._read_index()
        if clean in names:
            names = [n for n in names if n != clean]
            self._write_index(names)

    def _read_index(self) -> list[str]:
        raw = self._backend.get(INDEX_KEY)
        if not raw:
            return []
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError:
            return []
        if not isinstance(parsed, list):
            return []
        return [n for n in parsed if isinstance(n, str) and SECRET_NAME_RE.match(n)]

    def _write_index(self, names: list[str]) -> None:
        self._backend.set(INDEX_KEY, json.dumps(sorted(set(names))))


class _Backend:
    name = "unknown"
    secure = False

    def get(self, name: str) -> str | None:  # pragma: no cover - interface
        raise NotImplementedError

    def set(self, name: str, value: str) -> None:  # pragma: no cover - interface
        raise NotImplementedError

    def delete(self, name: str) -> None:  # pragma: no cover - interface
        raise NotImplementedError


class _KeyringBackend(_Backend):
    name = "keyring"
    secure = True

    def __init__(self, keyring_module: Any) -> None:
        self._keyring = keyring_module

    def get(self, name: str) -> str | None:
        return self._keyring.get_password(SERVICE_NAME, name)

    def set(self, name: str, value: str) -> None:
        self._keyring.set_password(SERVICE_NAME, name, value)

    def delete(self, name: str) -> None:
        try:
            self._keyring.delete_password(SERVICE_NAME, name)
        except Exception:
            # Missing keys are treated as already deleted.
            pass


class _FileBackend(_Backend):
    name = "file"
    secure = False

    def __init__(self) -> None:
        root = Path(os.environ.get("USERPROFILE") or Path.home()) / ".harness"
        root.mkdir(parents=True, exist_ok=True)
        self._path = root / "secrets.json"

    def get(self, name: str) -> str | None:
        data = self._read_all()
        value = data.get(name)
        return value if isinstance(value, str) else None

    def set(self, name: str, value: str) -> None:
        data = self._read_all()
        data[name] = value
        self._write_all(data)

    def delete(self, name: str) -> None:
        data = self._read_all()
        if name in data:
            del data[name]
            self._write_all(data)

    def _read_all(self) -> dict[str, str]:
        if not self._path.exists():
            return {}
        try:
            raw = self._path.read_text(encoding="utf-8")
            parsed = json.loads(raw)
            if isinstance(parsed, dict):
                return {k: v for k, v in parsed.items() if isinstance(k, str) and isinstance(v, str)}
        except Exception:
            pass
        return {}

    def _write_all(self, data: dict[str, str]) -> None:
        self._path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def _create_backend() -> _Backend:
    try:
        import keyring  # type: ignore

        return _KeyringBackend(keyring)
    except Exception:
        return _FileBackend()


def _validate_name(name: str) -> str:
    value = (name or "").strip()
    if not SECRET_NAME_RE.match(value):
        raise SecretStoreError("Invalid secret name. Use letters, numbers, dot, underscore, or dash.")
    if value == INDEX_KEY:
        raise SecretStoreError("Reserved secret name")
    return value
