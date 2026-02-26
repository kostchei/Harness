#!/usr/bin/env python3
"""Minimal multi-provider LLM bridge for Harness.

Supported providers:
- openai
- anthropic
- gemini
- lmstudio (OpenAI-compatible)
- ollama (OpenAI-compatible)
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any
from urllib import error, parse, request


DEFAULT_BASE_URLS = {
    "openai": "https://api.openai.com",
    "anthropic": "https://api.anthropic.com",
    "gemini": "https://generativelanguage.googleapis.com",
    "lmstudio": "http://127.0.0.1:1234",
    "ollama": "http://127.0.0.1:11434",
}

SUPPORTED_PROVIDERS = tuple(DEFAULT_BASE_URLS.keys())


@dataclass
class LlmRequest:
    provider: str
    model: str
    prompt: str
    api_key: str | None = None
    base_url: str | None = None
    temperature: float | None = None
    max_tokens: int | None = None
    timeout: float = 60.0


class LlmBridgeError(RuntimeError):
    """User-facing bridge error."""


class LlmBridge:
    """Provider-agnostic chat interface."""

    @staticmethod
    def provider_info() -> dict[str, Any]:
        return {
            "supported_providers": list(SUPPORTED_PROVIDERS),
            "defaults": {
                "openai": {"base_url": DEFAULT_BASE_URLS["openai"], "model": "gpt-4.1-mini"},
                "anthropic": {"base_url": DEFAULT_BASE_URLS["anthropic"], "model": "claude-3-5-sonnet-latest"},
                "gemini": {"base_url": DEFAULT_BASE_URLS["gemini"], "model": "gemini-2.0-flash"},
                "lmstudio": {"base_url": DEFAULT_BASE_URLS["lmstudio"], "model": "local-model"},
                "ollama": {"base_url": DEFAULT_BASE_URLS["ollama"], "model": "llama3.1"},
            },
            "notes": {
                "openai": "Uses /v1/chat/completions",
                "anthropic": "Uses /v1/messages",
                "gemini": "Uses /v1beta/models/{model}:generateContent",
                "lmstudio": "Uses OpenAI-compatible /v1/chat/completions",
                "ollama": "Uses OpenAI-compatible /v1/chat/completions",
            },
        }

    @staticmethod
    def chat(req: LlmRequest) -> dict[str, Any]:
        provider = (req.provider or "").strip().lower()
        if provider not in SUPPORTED_PROVIDERS:
            raise LlmBridgeError(f"Unsupported provider '{provider}'")
        if not req.model:
            raise LlmBridgeError("model is required")
        if not req.prompt:
            raise LlmBridgeError("prompt is required")

        base_url = (req.base_url or DEFAULT_BASE_URLS[provider]).rstrip("/")

        if provider == "openai":
            return _chat_openai(req, base_url)
        if provider == "anthropic":
            return _chat_anthropic(req, base_url)
        if provider == "gemini":
            return _chat_gemini(req, base_url)
        if provider in ("lmstudio", "ollama"):
            return _chat_openai_compatible(req, base_url)

        raise LlmBridgeError(f"Provider '{provider}' not implemented")


def _chat_openai(req: LlmRequest, base_url: str) -> dict[str, Any]:
    if not req.api_key:
        raise LlmBridgeError("api_key is required for openai")

    body: dict[str, Any] = {
        "model": req.model,
        "messages": [{"role": "user", "content": req.prompt}],
    }
    if req.temperature is not None:
        body["temperature"] = req.temperature
    if req.max_tokens is not None:
        body["max_tokens"] = req.max_tokens

    data = _http_json(
        "POST",
        f"{base_url}/v1/chat/completions",
        headers={
            "Authorization": f"Bearer {req.api_key}",
            "Content-Type": "application/json",
        },
        body=body,
        timeout=req.timeout,
    )

    text = _extract_openai_text(data)
    return {"provider": "openai", "model": req.model, "text": text, "raw": data}


def _chat_openai_compatible(req: LlmRequest, base_url: str) -> dict[str, Any]:
    body: dict[str, Any] = {
        "model": req.model,
        "messages": [{"role": "user", "content": req.prompt}],
    }
    if req.temperature is not None:
        body["temperature"] = req.temperature
    if req.max_tokens is not None:
        body["max_tokens"] = req.max_tokens

    headers = {"Content-Type": "application/json"}
    if req.api_key:
        headers["Authorization"] = f"Bearer {req.api_key}"

    data = _http_json(
        "POST",
        f"{base_url}/v1/chat/completions",
        headers=headers,
        body=body,
        timeout=req.timeout,
    )

    text = _extract_openai_text(data)
    return {"provider": req.provider, "model": req.model, "text": text, "raw": data}


def _chat_anthropic(req: LlmRequest, base_url: str) -> dict[str, Any]:
    if not req.api_key:
        raise LlmBridgeError("api_key is required for anthropic")

    body: dict[str, Any] = {
        "model": req.model,
        "messages": [{"role": "user", "content": req.prompt}],
        "max_tokens": req.max_tokens or 1024,
    }
    if req.temperature is not None:
        body["temperature"] = req.temperature

    data = _http_json(
        "POST",
        f"{base_url}/v1/messages",
        headers={
            "x-api-key": req.api_key,
            "anthropic-version": "2023-06-01",
            "Content-Type": "application/json",
        },
        body=body,
        timeout=req.timeout,
    )

    text_parts: list[str] = []
    for part in data.get("content", []):
        if part.get("type") == "text" and part.get("text"):
            text_parts.append(part["text"])
    text = "\n".join(text_parts).strip()

    return {"provider": "anthropic", "model": req.model, "text": text, "raw": data}


def _chat_gemini(req: LlmRequest, base_url: str) -> dict[str, Any]:
    if not req.api_key:
        raise LlmBridgeError("api_key is required for gemini")

    endpoint = f"{base_url}/v1beta/models/{parse.quote(req.model, safe='')}:generateContent"
    endpoint = f"{endpoint}?key={parse.quote(req.api_key, safe='')}"

    body: dict[str, Any] = {
        "contents": [{"role": "user", "parts": [{"text": req.prompt}]}],
    }
    if req.temperature is not None or req.max_tokens is not None:
        body["generationConfig"] = {}
        if req.temperature is not None:
            body["generationConfig"]["temperature"] = req.temperature
        if req.max_tokens is not None:
            body["generationConfig"]["maxOutputTokens"] = req.max_tokens

    data = _http_json(
        "POST",
        endpoint,
        headers={"Content-Type": "application/json"},
        body=body,
        timeout=req.timeout,
    )

    text_parts: list[str] = []
    for candidate in data.get("candidates", []):
        content = candidate.get("content", {})
        for part in content.get("parts", []):
            if part.get("text"):
                text_parts.append(part["text"])

    text = "\n".join(text_parts).strip()
    return {"provider": "gemini", "model": req.model, "text": text, "raw": data}


def _extract_openai_text(data: dict[str, Any]) -> str:
    choices = data.get("choices") or []
    if not choices:
        return ""
    message = choices[0].get("message", {})
    content = message.get("content", "")
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        text_parts: list[str] = []
        for item in content:
            if isinstance(item, dict) and item.get("type") in ("text", "output_text"):
                text = item.get("text")
                if text:
                    text_parts.append(text)
        return "\n".join(text_parts)
    return ""


def _http_json(method: str, url: str, headers: dict[str, str], body: dict[str, Any], timeout: float) -> dict[str, Any]:
    payload = json.dumps(body).encode("utf-8")
    req = request.Request(url=url, data=payload, method=method)
    for name, value in headers.items():
        req.add_header(name, value)

    try:
        with request.urlopen(req, timeout=timeout) as resp:  # noqa: S310
            raw = resp.read().decode("utf-8")
            if not raw:
                return {}
            return json.loads(raw)
    except error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace") if exc.fp else str(exc)
        raise LlmBridgeError(f"HTTP {exc.code}: {detail}") from exc
    except error.URLError as exc:
        raise LlmBridgeError(f"Connection error: {exc.reason}") from exc
    except json.JSONDecodeError as exc:
        raise LlmBridgeError(f"Invalid JSON response: {exc}") from exc
