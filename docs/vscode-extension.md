# VS Code Extension

Harness includes a local VS Code extension scaffold in `vscode-extension/` that provides an in-editor control surface for the agent loop.

## What It Adds

- Bridge lifecycle commands (`start`, `stop`, `health`)
- Embedded Harness web panel in a VS Code webview
- Guardrail commands (`build`, `lint`, `test`, `run guardrails`)
- Runtime quick actions (`ping`, `screenshot`, `validate`, `performance`, `scene tree`, `clear input`, `quit`)

## Run It

1. Open the repository in VS Code.
1. Open `vscode-extension/`.
1. Press `F5` to start an Extension Development Host.
1. In the dev host, run:
   - `Harness: Start Agent Bridge`
   - `Harness: Open Agent Panel`

## Extension Settings

- `harnessAgent.pythonPath`
- `harnessAgent.host`
- `harnessAgent.port`
- `harnessAgent.projectPath`
- `harnessAgent.bridgeScript`
- `harnessAgent.guardrails.buildCommand`
- `harnessAgent.guardrails.lintCommand`
- `harnessAgent.guardrails.testCommand`

## Notes

- The bridge defaults to `http://127.0.0.1:8765`.
- The panel renders the same `webui/` interface served by `tools/devtools_web.py`.
- Runtime actions require the Godot game running with `DevTools` autoload active.
