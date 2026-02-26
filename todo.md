# Harness TODO Plan

Planning doc for high-impact reliability features inspired by Roo Code patterns, adapted to Harness and Godot workflows.

## Scope

Included:
- Checkpoint/rollback safety
- Automatic diagnostics-delta gate
- Scoped agent access rules
- Task todo state enforcement (for complex tasks)
- Context condensing/summarization for long sessions
- Semantic codebase indexing (for larger projects)

Not included right now:
- Parallel experiments/worktree-first workflow

## Priority Order

1. Checkpoint/rollback safety
2. Automatic diagnostics-delta gate
3. Scoped agent access rules
4. Task todo state enforcement
5. Context condensing/summarization
6. Semantic codebase indexing

## 1) Checkpoint/Rollback Safety (High)

Goal:
- Ensure every meaningful task has a recoverable known-good state.

Plan:
- Add `tools/checkpoint.ps1` with commands:
  - `start <task-id>`: records current git commit + dirty file snapshot metadata.
  - `diff <task-id>`: shows changes since checkpoint.
  - `restore <task-id>`: restores tracked files to checkpoint commit/state.
  - `finalize <task-id>`: marks checkpoint complete and archives metadata.
- Store metadata in `.harness/checkpoints/<task-id>.json`.
- Add a non-destructive default (`restore --preview`) before actual restore.
- Document usage in `README.md` and `docs/agent-loop.md`.

Acceptance criteria:
- Agent can start a task checkpoint in one command.
- Agent can show exact delta against checkpoint.
- Agent can safely restore from failed work without manual git surgery.

## 2) Automatic Diagnostics-Delta Gate (High)

Goal:
- Fail tasks only on newly introduced diagnostics, not pre-existing noise.

Plan:
- Add baseline capture script `tools/diagnostics_baseline.ps1`:
  - captures output for `dotnet build -warnaserror` and Godot lint scripts.
  - normalizes paths and volatile text.
  - writes `.harness/diagnostics/baseline.json`.
- Add compare script `tools/diagnostics_delta.ps1`:
  - runs current diagnostics.
  - reports only new/changed issues vs baseline.
  - exits non-zero on regressions.
- Integrate into `tools/test.ps1` or a new `tools/verify.ps1` pipeline.

Acceptance criteria:
- Baseline can be refreshed intentionally.
- CI/local gate fails on net-new diagnostics.
- Output clearly lists “new” vs “existing” findings.

## 3) Scoped Agent Access Rules (High)

Goal:
- Give agents explicit boundaries for where and how they can operate.

Plan:
- Add `.harnessignore` (Harness equivalent of `.rooignore`) for excluded paths.
- Add rule folders:
  - `.harness/rules/global.md`
  - `.harness/rules/modes/implementer.md`
  - `.harness/rules/modes/reviewer.md`
- Keep `agents.md` as top-level policy, then load layered rules in this order:
  1. `agents.md`
  2. `.harness/rules/global.md`
  3. `.harness/rules/modes/<mode>.md`
  4. task-local overrides (if present)
- Add docs describing precedence and conflict handling.

Acceptance criteria:
- Ignored paths are never modified by agent workflows.
- Rule precedence is deterministic and documented.
- Mode-specific behavior can be switched without editing core docs.

## 4) Task Todo State Enforcement (Complex Tasks)

Goal:
- Prevent long tasks from drifting or skipping validation steps.

Plan:
- Add optional task file format: `.harness/tasks/<task-id>.json`.
- States: `pending`, `in_progress`, `blocked`, `done`.
- Require “done” state only when guardrails pass and evidence is attached.
- Add `tools/task_status.ps1` for read/update/status checks.

Acceptance criteria:
- Complex tasks have explicit checklist/state tracking.
- “Done” cannot be set if guardrail status is failing.

## 5) Context Condensing/Summarization (Long Sessions)

Goal:
- Keep long-running agent sessions coherent without context bloat.

Plan:
- Add `docs/session-summary-template.md` for periodic summaries.
- Add `tools/summarize_session.ps1` to collect:
  - goals
  - decisions
  - changed files
  - open risks
  - next actions
- Persist summaries under `.harness/summaries/<date>-<task-id>.md`.

Acceptance criteria:
- Sessions over agreed threshold produce summary artifacts.
- Handoff quality improves (clear “where we are” snapshot).

## 6) Semantic Codebase Indexing (Large Projects)

Goal:
- Improve agent retrieval quality as project size grows.

Plan:
- Start with lightweight symbol indexing:
  - C#: project/solution symbols from existing .NET tooling.
  - GDScript: script/class/function map generated from source parse.
- Store index at `.harness/index/symbols.json`.
- Add refresh command `tools/build_index.ps1`.
- Later: optional vector/semantic layer if needed.

Acceptance criteria:
- Agent can query “where is X defined/used” quickly from index.
- Index refresh is fast enough for daily use.

## Suggested Rollout

Phase 1 (safety foundations):
- 1) Checkpoint/rollback
- 2) Diagnostics-delta gate
- 3) Scoped rules

Phase 2 (execution quality):
- 4) Task todo enforcement
- 5) Context summarization

Phase 3 (scale):
- 6) Semantic indexing

## Definition of Done for This Plan

- Each phase has scripts, docs, and at least one demo workflow in `README.md`.
- Guardrails remain runnable through existing `tools/godot.ps1` + PowerShell entry points.
- No requirement for parallel experiments/worktrees at this stage.
