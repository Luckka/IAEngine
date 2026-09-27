# Source Manifest

## Source ownership

This repository is the maintained IAEngine source. The original OnlineOS
repository is an external, read-only compatibility reference and is not a build
input, runtime input, or source of truth for IAEngine.Core.

The historical `OnlineOs.AiOrchestrator.*` namespace remains in a small public
compatibility surface because existing consumers, including InfraSentinel, still
compile against those contracts. That naming is a compatibility exception, not a
dependency on the OnlineOS checkout.

## Project and tests

- `OnlineOs.AiOrchestrator.csproj` — legacy compatibility executable .NET 10 project.
- `src/IAEngine.Core/IAEngine.Core.csproj` — generic Engine assembly; it does not
  compile `Pipeline/GitWorkflowManager.cs` and has no project reference to the
  OnlineOS adapter.
- `src/IAEngine.OnlineOSAdapter/IAEngine.OnlineOSAdapter.csproj` — optional
  compatibility adapter referencing the Core in one direction.
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
- `Pipeline/` — generic orchestrator, state machine, routing/validation/review/
  remediation, health, and policy components. QA, Flutter, Patrol, ADB, reference,
  and legacy Git workflow files are consumer/adapter concerns and are not compiled
  into IAEngine.Core.
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

`consumer composition → task/milestone loading → implementation/provider stages →
deterministic validation → review → bounded remediation → Approved | HumanRequired |
Failed → RunStore persistence/recovery`.

QA/Patrol is a separate CLI pipeline. Milestone execution wraps task runs and may
invoke the Git lifecycle manager. The state machine permits routing, implementation,
validation, review, remediation, retry, human-required, approval, and failure paths;
the source was not changed.

## OnlineOS-specific references and paths

- The legacy executable and adapter retain OnlineOS-specific prompts, QA,
  Flutter/Patrol/ADB, reference inspection, and compatibility configuration.
- `GitWorkflowManager.cs` remains a legacy executable concern and is intentionally
  excluded from IAEngine.Core; it is not reused by the generic checkpoint contract.
- Historical option/model members and the `OnlineOs.AiOrchestrator.*` namespace
  remain because removing them would be a public breaking change for existing
  consumers. They are listed explicitly here rather than treated as generic Core
  capabilities.

These are remaining compatibility couplings outside the generic Core composition,
not hidden build dependencies on the OnlineOS repository. Full removal requires a
separate major API decision and coordinated consumer migration.
