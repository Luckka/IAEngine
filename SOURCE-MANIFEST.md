# Source Manifest

## Discovered implementation

`DISCOVERED_SOURCE_ORCHESTRATOR`:

external OnlineOS orchestrator source (path intentionally omitted from the public repository)

There was exactly one implementation matching the requested classes and symbols.
It is the implementation wired to the current CLI, project file, tests, and
orchestrator README. No competing implementation was found.

## Project and tests

- `OnlineOs.AiOrchestrator.csproj` — executable .NET 10 project.
- `tests/OnlineOs.AiOrchestrator.Tests.csproj` — xUnit test project referencing the executable.
- No `.sln` file exists for this orchestrator.
- Source project target framework: `net10.0`.
- Test project packages: Microsoft.NET.Test.Sdk 17.12.0, xunit 2.9.2, xunit.runner.visualstudio 2.8.2.

## Copied categories

### Code

All regular source files under the discovered orchestrator were copied, preserving
their directories:

- `Abstractions/` — contracts and Git workflow contracts.
- `Agents/` — `ClaudeAgent`, `CodexAgent`, and `OllamaTaskRouter`.
- `Configuration/` — orchestrator and QA options/configuration loading.
- `Infrastructure/` — process execution, Git service/lifecycle state, JSON, progress,
  run persistence, and workspace boundary.
- `Models/` — workflow, E2E, and device artifact models.
- `Pipeline/` — orchestrator, state machine, routing/validation/review/remediation,
  preflight, health, policy, QA, evidence, and Git workflow components.
- `Reference/` — read-only reference inspection and audit aggregation.
- `Roadmap/` — roadmap catalog, models, state, and milestone runner.
- `Program.cs` — CLI entry point.

### Tests

All 24 source test files under `tests/` were copied, including tests for agents,
routing, orchestration, validation, review policy, remediation, roadmap, health,
run state, persistence, QA, reference inspection, process execution, and workspace
boundaries.

### Documentation and safe support configuration

- Orchestrator `README.md`.
- `appsettings.json` and `appsettings.example.json`; inspection found no secrets.
- `AGENTS.md` and `CLAUDE.md` copied from the source repository root because the
  current prompts/tests explicitly reference them.
- `ai/roadmap/MILESTONES.json` copied because the current milestone CLI and roadmap
  tests load that exact path.

## Excluded material

| Item | Category | Copied? | Reason |
| --- | --- | ---: | --- |
| `.git` | repository metadata | No | Destination must remain without Git. |
| `.ai-runs` | historical execution state | No | Contains prior run artifacts and audit history. |
| `.ai-state` | historical/mutable execution state | No | Contains prior roadmap, Git lifecycle, and audit state. |
| `bin` | generated artifact | No | Build output; must be regenerated in destination if needed. |
| `obj` | generated/cache artifact | No | Intermediate build output. |
| logs, temp files, cache files | generated/cache artifact | No | Not part of the source baseline. |
| tokens, API keys, passwords, credentials, key files | secret | No | Sensitive material is never copied. |
| OnlineOS `app/` | OnlineOS-specific application | No | This phase isolates the orchestrator only. |
| OnlineOS full `docs/`, `architecture/`, and `ai/` trees | OnlineOS-specific reference | No | Only the exact safe support files required by current tests/CLI were copied. |

## Implemented CLI commands

The parser in `Program.cs` implements:

- `help`, `--help`, `-h`
- `preflight`
- `task "..." [--dry-run]`
- `task-file <path> [--dry-run]`
- `milestone <ID>`
- `milestone approve <ID>`
- `milestone finalize <ID>`
- `continue`
- `status`
- `run abandon [--reason "..."]`
- `run retry <RUN_ID>`
- `audit`
- `qa fast`
- `qa milestone <ID>`
- `qa full`
- `qa report <RUN_ID>`

The README documents the primary commands and was compared with the parser. The
parser additionally exposes `help`, `--help`, `-h`, `milestone finalize`, `status`,
`run abandon`, `run retry`, and `audit`; these are recorded here because they are
not all presented together in the README's primary examples.

## Confirmed pipeline

`CLI → task/milestone loading → Ollama/Qwen routing (fallback) → Claude implementation → deterministic validation → Codex review → Claude remediation → Approved | HumanRequired | Failed → RunStore persistence/recovery`.

QA/Patrol is a separate CLI pipeline. Milestone execution wraps task runs and may
invoke the Git lifecycle manager. The state machine permits routing, implementation,
validation, review, remediation, retry, human-required, approval, and failure paths;
the source was not changed.

## OnlineOS-specific references and paths

- `Program.cs` searches for a Git repository and reports OnlineOS-oriented errors.
- `Program.cs` expects `ai/roadmap/MILESTONES.json` and `.ai-runs`/`.ai-state` under
  the resolved workspace.
- `OllamaTaskRouter.cs` contains the OnlineOS task prompt and OnlineOS documentation
  index (`ai/skills`, `docs`, `architecture`).
- `ClaudeAgent.cs` contains OnlineOS repository instructions and references to
  `AGENTS.md`, `CLAUDE.md`, and `architecture/ENGINEERING_STANDARDS.md`.
- `EngineeringStandardsPolicy.cs` and tests encode OnlineOS engineering context.
- `ReferenceInspection.cs` supports an optional read-only OnlineOS reference root
  via `ONLINEOS_REFERENCE_ROOT` and requires the configured reference branch.
- `GitWorkflowManager.cs` stages OnlineOS paths (`app`, `tools/ai-orchestrator`,
  `ai`, `architecture`, `docs`, `README.md`, `.gitignore`) and retains the source
  Git lifecycle assumptions.
- QA defaults retain OnlineOS device/emulator names and Flutter `app` paths.
- `QaReportWriter.cs` and CLI output retain OnlineOS-visible labels.

These are remaining couplings, not silently generalized in the baseline.
