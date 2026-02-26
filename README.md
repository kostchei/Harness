# Harness

Harness is a Godot-oriented coding-agent substrate for human-directed game development.
It provides runtime control hooks, validation guardrails, and repeatable CLI workflows so an agent can make changes and verify them before handing work back to you.

## What Was Repurposed

This project ports the non-domain-specific loop and guardrails from `tea-leaves`:

- Runtime command server (`game/DevTools.cs`)
- Runtime scene validator (`game/SceneValidator.cs`)
- Optional startup window policy (`game/WindowSetup.cs`)
- CLI bridge for agent tooling (`tools/devtools.py`)
- Verification scripts (`tools/lint_project.gd`, `tools/lint_shaders.gd`, `tools/test.ps1`, `tools/lint_tests.ps1`)
- Input bootstrap and sequence scaffolding (`tools/setup_input_actions_cli.gd`, `test/sequences/example_template.json`)

## Agent Loop

1. Agent edits scenes/scripts/resources.
1. Agent runs static checks and tests.
1. Agent launches game and uses DevTools commands for runtime checks:
1. Screenshot capture
1. Scene validation
1. Scene-tree/state inspection
1. Input simulation sequences
1. Performance sampling
1. Changes are accepted only when guardrails pass.

## Runtime Protocol

DevTools autoload communicates through `user://`:

- `devtools_commands.json` (inbox)
- `devtools_results.json` (outbox)
- `devtools_log.jsonl` (structured logs)

## Quick Start

```powershell
dotnet restore
dotnet build -warnaserror

# Optional: seed default input actions
pwsh ./tools/godot.ps1 --headless --script res://tools/setup_input_actions_cli.gd

# Static lint
pwsh ./tools/godot.ps1 --headless --script res://tools/lint_project.gd
pwsh ./tools/godot.ps1 --headless --script res://tools/lint_shaders.gd

# Run game
pwsh ./tools/godot.ps1

# Runtime commands (in a second shell while game is running)
python tools/devtools.py ping
python tools/devtools.py screenshot --filename verification.png
python tools/devtools.py validate-all
python tools/devtools.py performance
python tools/devtools.py input clear

# Optional Web UI (serves UI + local API bridge)
python tools/devtools_web.py --project . --host 127.0.0.1 --port 8765
# Open http://127.0.0.1:8765
# Includes LLM backend panel: OpenAI, Anthropic, Gemini, LM Studio, Ollama

# Optional (recommended): secure OS keychain support for API keys
python -m pip install keyring
```

## Notes

- `tools/test.ps1` runs gdUnit4 tests and expects `addons/gdUnit4` to be installed.
- Window behavior is configurable; see `docs/window-placement.md`.
- Web UI details and static-host setup: `docs/web-ui.md`.
