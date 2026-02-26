# Web UI

The harness now includes a minimal web interface for DevTools commands.

## Local (single process)

Run the local bridge server (serves UI + API):

```powershell
python tools/devtools_web.py --project . --host 127.0.0.1 --port 8765
```

Open: `http://127.0.0.1:8765`

## VS Code-hosted panel

If you want the same UI embedded in VS Code, use the local extension scaffold in `vscode-extension/`:

1. Launch Extension Development Host (`F5` from `vscode-extension`)
1. Run `Harness: Start Agent Bridge`
1. Run `Harness: Open Agent Panel`

The panel renders this same web UI in a VS Code webview and adds command-palette actions for guardrails and runtime quick actions.

## Static-hosted UI + local API

You can host `webui/` on any static host (GitHub Pages, Netlify, Azure Static Web Apps) and keep the API bridge local:

```powershell
python tools/devtools_web.py --project . --host 0.0.0.0 --port 8765
```

In the UI, set **API Base URL** to your local bridge, for example:

- `http://127.0.0.1:8765` (same machine)
- `http://<LAN-IP>:8765` (another device on local network)

The bridge enables CORS so a separately-hosted static frontend can call it.

## Available quick actions

- Ping game
- Screenshot
- Validate all scenes
- Performance sample
- Scene tree
- Clear simulated inputs
- Quit game
- Custom action + args JSON

## LLM backend panel

The same Web UI now includes an **LLM Backends** panel with support for:

- OpenAI
- Anthropic
- Gemini (Google AI Studio API)
- LM Studio (local OpenAI-compatible endpoint)
- Ollama (local OpenAI-compatible endpoint)

API endpoints:

- `GET /api/llm/providers`
- `POST /api/llm/chat`
- `GET /api/secrets/status`
- `GET /api/secrets/list`
- `POST /api/secrets/set`
- `POST /api/secrets/get`
- `POST /api/secrets/delete`

`/api/llm/chat` JSON body:

```json
{
  "provider": "openai|anthropic|gemini|lmstudio|ollama",
  "model": "model-id",
  "prompt": "Your prompt text",
  "api_key": "optional-for-local-required-for-cloud",
  "api_key_name": "optional-saved-key-name",
  "base_url": "optional override",
  "temperature": 0.2,
  "max_tokens": 800,
  "timeout": 60
}
```

`api_key_name` resolves a previously saved key in the secret store.

## Secret storage

Roo-style key handling is implemented with a secure store abstraction:

- Preferred backend: OS keychain via Python `keyring`
- Fallback backend: `~/.harness/secrets.json` (less secure)

Install secure backend support:

```powershell
python -m pip install keyring
```

In the UI:

1. Set **Saved Key Name** (for example `openai-main`)
1. Paste key into **API Key**
1. Click **Save Key**
1. Use **Run Using Saved Key Name** for future calls

You can verify backend mode with **Check Secret Store**.

## Notes

- The Godot game must be running with the `DevTools` autoload active.
- The bridge only forwards an allowlist of actions for safety.
- Transport remains file-based through `user://devtools_commands.json` and `user://devtools_results.json`.
