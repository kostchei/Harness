# Human-Directed Agent Loop

This harness is designed for a non-coding project owner working with a coding agent.

## Contract

- You provide intent: feature goals, constraints, acceptance criteria.
- The agent performs implementation work in Godot/C#/GDScript.
- Guardrails decide whether a change is acceptable.

## Guardrail Stages

1. Build and compile:
   - `dotnet build -warnaserror`
1. Static project checks:
   - `pwsh ./tools/godot.ps1 --headless --script res://tools/lint_project.gd`
   - `pwsh ./tools/godot.ps1 --headless --script res://tools/lint_shaders.gd`
1. Runtime checks through DevTools:
   - screenshots
   - scene validation
   - state inspection
   - input sequences
   - performance metrics
1. Tests:
   - `dotnet test`
   - `pwsh ./tools/test.ps1` (when gdUnit4 addon is installed)

## Acceptance Pattern

Treat a task as done only when:

- Build passes
- Lint passes
- Relevant tests pass
- Runtime evidence is attached (screenshot/log/result JSON) for visual or interaction tasks
