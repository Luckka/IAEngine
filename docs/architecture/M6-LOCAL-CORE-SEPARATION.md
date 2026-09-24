# M6 — Local Core Library Separation

Status: **In progress**

## Baseline

- IAEngine branch: `feature/local-core-library-separation`.
- Baseline commit: `82f4ae6` (`docs: document local package surface review`).
- InfraSentinel branch: `feature/local-core-library-consumer`.
- InfraSentinel baseline includes the pre-existing commit `28f987a tets`; it is
  preserved and not rewritten.
- OnlineOS branch: `remediation/o01-visual-conformity`, clean and read-only.
- IAEngine currently has one executable project and one test project.
- InfraSentinel currently references `OnlineOs.AiOrchestrator.csproj` directly.
- Previous baseline: IAEngine 188 tests passed; InfraSentinel 7 tests passed.

## Initial classification

### Core candidates

- generic models for tasks, runs, validation, review and workflow state;
- provider, validator, policy, Git and run-store abstractions;
- runtime composition and fail-closed resolution;
- `EngineHost`, orchestrator, workflow state machine and recovery;
- roadmap/milestone contracts and dependency validation;
- generic filesystem implementations used by the local host.

### OnlineOS compatibility candidates

- `ValidationRunner` Flutter command handling;
- Patrol, ADB and emulator lifecycle/capture;
- `Reference/ReferenceInspection.cs` and monolith-specific artifacts;
- Ollama, Claude and Codex command adapters and prompts;
- prototype/visual-conformity gates;
- OnlineOS configuration defaults and CLI dispatch;
- historical failure categories and product-specific policy behavior.

### CLI candidates

- `Program.cs` top-level entrypoint;
- configuration loading and environment adaptation;
- command dispatch, status output and compatibility workflow wiring.

## Separation strategy

The first implementation unit is a local `IAEngine.Core` library. It will be
introduced without publishing a package and without changing the OnlineOS
repository. The existing executable remains the compatibility composition root
and will reference Core during the transition.

The adapter split is incremental. Any source that still requires OnlineOS
configuration or provider behavior remains outside Core. The InfraSentinel
consumer will reference Core only after its required host contracts compile and
its existing local tests remain green.

## Safety gates

- no NuGet package or `.nupkg` is created;
- no OnlineOS file, branch, commit, test or configuration is modified;
- no destructive Git operation is allowed;
- no external provider, AWS call, scanning or infrastructure access is used;
- behavior is preserved through the existing executable compatibility path;
- public-contract changes require an ADR and explicit review.

## Learning Checkpoint

1. The Core must be a library so consumers can compose the workflow without
   inheriting a CLI entrypoint or product-specific process behavior.
2. The CLI should translate configuration and dispatch commands; workflow rules
   belong to Core contracts and orchestration.
3. OnlineOS compatibility stays outside Core because Flutter, Patrol, ADB and
   historical prompts are technology/product decisions.
4. InfraSentinel consumes Core through a local project reference and supplies its
   own validators, policies, task sources and persistence.
5. Existing host, milestone, retry, recovery, approval and artifact behavior must
   remain preserved during the transition.
6. Assembly/API changes, persistence contract changes and removal of historical
   compatibility types still require human review.

