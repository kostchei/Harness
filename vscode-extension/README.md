# Harness Agent VS Code Extension

This extension adds a Roo-style in-editor control surface for Harness.

## Features

- Start/stop local bridge (`tools/devtools_web.py`)
- Open embedded Harness panel in a VS Code webview
- Run guardrails (build, lint, test) from the command palette
- Run runtime DevTools actions (ping/screenshot/validate/perf/scene tree/input clear/quit)

## Local Development

1. Open the `Harness` workspace in VS Code.
1. Open `vscode-extension/` in the integrated terminal.
1. Press `F5` to launch the Extension Development Host.
1. In the dev host, run commands from Command Palette:
   - `Harness: Start Agent Bridge`
   - `Harness: Open Agent Panel`

## Settings

- `harnessAgent.pythonPath` (default: `python`)
- `harnessAgent.host` (default: `127.0.0.1`)
- `harnessAgent.port` (default: `8765`)
- `harnessAgent.projectPath` (default: `.`)
- `harnessAgent.bridgeScript` (default: `tools/devtools_web.py`)
- `harnessAgent.guardrails.buildCommand`
- `harnessAgent.guardrails.lintCommand`
- `harnessAgent.guardrails.testCommand`
