# Current Architecture — Isolated Baseline

Analysis date: 2026-09-21. Scope: this repository only.

## Evidence convention

- **VERIFIED IN CODE** — directly observed in the isolated source or its tests.
- **INFERRED** — interpretation derived from verified behavior, not a declared contract.
- **RECOMMENDED** — future proposal; not implemented.
- **UNKNOWN** — not present or not determinable from this baseline.
- **HUMAN DECISION REQUIRED** — requires an architectural/product decision.

## Baseline confirmation

### VERIFIED IN CODE

| Check | Result |
|---|---|
| Current path | this repository |
| Git metadata in destination | Public repository Git metadata is present; the copied source baseline originally had none. |
| `BASELINE.md` | Present; records source branch `remediation/o01-visual-conformity`, commit `724030b2e8fc71c6fc7356f98b676a233eccfaa7`, copy date `2026-09-20`. |
| `SOURCE-MANIFEST.md` | Present; records copied implementation, tests, configuration, prompts and exclusions. |
| `COPY-BOUNDARIES.md` | Present; declares the OnlineOS source read-only and destination independently evolvable. |
| Executable project | `OnlineOs.AiOrchestrator.csproj`, target `net10.0`, `OutputType=Exe`. |
| Solution | No `.sln` exists; this is also recorded by `SOURCE-MANIFEST.md`. |
| Test project | `tests/OnlineOs.AiOrchestrator.Tests.csproj`, xUnit; 24 test source files are present. |
| Origin source | Read-only external source; branch `remediation/o01-visual-conformity` and commit `724030b2...` are recorded without publishing a local filesystem path. |
| Runtime state copied | Historical `.ai-runs` and `.ai-state` are absent from the destination; code can create them at runtime. |

### VERIFIED IN CODE — validation result

`dotnet test tests/OnlineOs.AiOrchestrator.Tests.csproj --no-restore` completed with 170 tests: 168 passed and 2 failed. The failures are existing baseline behavior, not changed by this analysis:

1. `tests/ValidationRunnerTests.cs:11-21` — `FlutterProfileRunsEachConfiguredCategoryFromAppRoot` expects the integration result to pass, but the fake device probe output does not contain the configured `macos` target, so `Pipeline/ValidationRunner.cs:77-85` returns `NotExecutable`.
2. `tests/ValidationRunnerTests.cs:66-75` — `IntegrationWithoutTargetIsExplicitlyNotExecutable` expects `NotExecutable`, but `Pipeline/ValidationRunner.cs:74-75` returns `NotApplicable` when `integration_test` is absent.

These are recorded as behavior/documentation divergence. No source or test was modified.

## Component inventory

| Componente | Arquivo/classe | Responsabilidade | Dependências | Testes |
|---|---|---|---|---|
| CLI/host | `Program.cs` top-level program; `FindRepository`, `FindConfigDirectory`, `PrintUsage` | Resolve workspace/configuration, composes concrete services, dispatches `preflight`, `task`, `task-file`, `milestone`, `continue`, `status`, `run abandon`, `run retry`, `audit`, `qa` | All major namespaces; local process/Git/filesystem | `OrchestratorTests`, `RoadmapTests`, QA tests, `ProgressReporterTests` |
| Orchestration | `Pipeline/Orchestrator.cs:Orchestrator.ExecuteAsync`, `ExecutePersistedRunAsync`, `ContinueAsync`, `RetryHumanRequiredAsync` | Owns stage execution, persistence, recovery, implementation/validation/review/remediation loop | `ITaskRouter`, `IImplementationAgent`, `IValidationRunner`, `IReviewAgent`, `IGitService`, `IRunStore`, `WorkflowStateMachine`, `ReviewPolicy`, `RecoveryPolicy` | `tests/OrchestratorTests.cs` |
| State machine | `Pipeline/WorkflowStateMachine.cs:WorkflowStateMachine.Move` and `ReopenForAuthorized*` | Enforces transitions and constrained reopen paths | `WorkflowState`, `RunRecord`, `FailureCategory` | `WorkflowStateMachineTests`, `RunStateConsistencyTests`, `OrchestratorTests` |
| Task/run model | `Models/WorkflowModels.cs:DevelopmentTask`, `RunRecord`, `FailureContext`, `ValidationResult`, `ReviewResult` | Defines task, run audit, stage, failure, recovery, review and Git lifecycle data | `System.Text.Json` attributes; domain enums | Broad model assertions in orchestration/recovery tests |
| Milestones | `Roadmap/RoadmapModels.cs`, `Roadmap/RoadmapCatalog.cs`, `Roadmap/MilestoneRunner.cs` | Loads JSON roadmap, orders dependencies, stores milestone state, runs one task at a time | `MILESTONES.json`, `IRunStore`, `IGitWorkflowManager`, `Orchestrator` | `RoadmapTests`, `OrchestratorTests` |
| Local router | `Agents/OllamaTaskRouter.cs:OllamaTaskRouter.RouteAsync`, `DiagnoseAsync` | Calls Ollama JSON endpoint, validates/normalizes route, applies deterministic fallback and failure diagnosis | `HttpClient`, `OllamaOptions`, OnlineOS index/prompt constants | `OllamaTaskRouterTests`, `FailureDiagnosisTests` |
| Implementation agent | `Agents/ClaudeAgent.cs:ClaudeAgent.ImplementAsync`, `RemediateAsync` | Invokes configured Claude CLI and parses structured implementation/remediation result | `IProcessRunner`, `ClaudeAgentOptions`, `RemediationContextBuilder`, OnlineOS prompt text | `ClaudeAgentTests`, `OrchestratorTests`, `RemediationContextTests` |
| Review agent | `Agents/CodexAgent.cs:CodexAgent.ReviewAsync` | Invokes Codex read-only review, validates quality rubric/protocol/evidence | `IProcessRunner`, `CliAgentOptions`, `EngineeringProfile` | `CodexAgentTests`, `EngineeringStandardsPolicyTests` |
| Deterministic validator | `Pipeline/ValidationRunner.cs:RunAsync` and `RunIntegrationAsync` | Executes configured commands, resolves workspace paths, creates explicit status for missing targets | `IProcessRunner`, `ValidationOptions`, `WorkspaceBoundary`, `EngineeringProfile` | `ValidationRunnerTests` |
| Review policy | `Pipeline/ReviewPolicy.cs:ReviewPolicy.Passes` | Decides whether Codex result blocks approval; handles quality/prototype evidence | `ReviewPolicyOptions`, `ReviewResult` | `ReviewPolicyTests` |
| Failure/recovery | `Pipeline/FailureDiagnosis.cs:FailureClassifier`, `RecoveryPolicy`, `ReviewConvergence` | Classifies failure, selects retry/remediation/wait/human-required, tracks convergence | `OrchestratorOptions`, `FailureContext`, `ReviewResult` | `FailureDiagnosisTests`, `OrchestratorTests` |
| Preflight | `Pipeline/PreflightService.cs:PreflightService.RunAsync` | Checks Git, branch safety, Ollama/model, Claude, Codex and optionally Flutter | `IProcessRunner`, `ITaskRouter`, `IGitService`, `AppOptions` | `OrchestratorTests`; direct preflight coverage is limited |
| Health/recovery | `Pipeline/RunHealthEvaluator.cs:RunHealthEvaluator.Assess` | Interprets persisted run and pointer health; decides resumability | `RunRecord`, `WorkflowState` | `RunHealthTests`, `RunStoreTests` |
| Run persistence | `Infrastructure/RunStore.cs:RunStore` | Atomically writes `run.json`, artifacts and `.ai-runs/active-run.json`; repairs pointers | `WorkspaceBoundary`, JSON, filesystem | `RunStoreTests`, `RunStateConsistencyTests` |
| Process execution | `Infrastructure/ProcessRunner.cs:ProcessRunner` | Runs commands with timeout/cancellation, captures output and metadata | `System.Diagnostics.Process` | `ProcessRunnerTests` |
| Workspace boundary | `Infrastructure/WorkspaceBoundary.cs:WorkspaceBoundary.Resolve` | Prevents path escape, including symlink escape | Filesystem | `WorkspaceBoundaryTests`, QA tests |
| Git service | `Infrastructure/GitService.cs:GitService` | Reads root/branch/status/diff/commit and branch safety | `IProcessRunner`, `GitOptions` | `GitWorkflowManagerTests`, orchestration fakes |
| Git lifecycle | `Pipeline/GitWorkflowManager.cs:GitWorkflowManager.PrepareMilestoneAsync`, `FinalizeMilestoneAsync` | Creates feature branch, validates, stages/commits/pushes/merges under safety gates | Git CLI, `GitLifecycleStore`, `IValidationRunner`, OnlineOS path list | `GitWorkflowManagerTests` |
| Reference boundary | `Reference/ReferenceInspection.cs:ReadOnlyReferenceBoundary`, `MonolithReferenceInspector` | Reads configured reference repository and writes local reference artifacts; checks branch/dirty state | `IProcessRunner`, `ReferenceOptions`, `ONLINEOS_REFERENCE_ROOT` | `ReferenceInspectionTests` |
| QA/Patrol | `Pipeline/PatrolE2ETestRunner.cs`, `PatrolDeviceManager`, `PatrolTestSelector`, `PatrolExecutableResolver`, `Adb*`, `QaReportWriter` | Separate emulator/Patrol execution, retry and artifact/report aggregation | Flutter `app`, Patrol, ADB, emulator, QA options | `PatrolQaTests`, `PatrolDeviceLifecycleTests`, `PatrolArtifactTests` |
| Policy profile | `Pipeline/EngineeringStandardsPolicy.cs:EngineeringStandardsPolicy.Create` | Derives OnlineOS engineering and validation expectations from task/route | `DevelopmentTask`, `RoutingResult` | `EngineeringStandardsPolicyTests` |

## Confirmed execution flow

### Main task flow — VERIFIED IN CODE

`Program.cs` composes services, `PreflightService.RunAsync` gates non-dry runs, and `Orchestrator.ExecuteAsync` persists `Created` before routing.

```text
CLI (Program.cs)
  -> repository/config resolution
  -> preflight (non-dry-run)
  -> task/task-file loading
  -> RunStore.InitializeAsync (Created persisted)
  -> Routing (OllamaTaskRouter, fallback if unavailable/malformed)
  -> Claude implementation
  -> deterministic ValidationRunner
      -> remediation on required validation failure
  -> Codex review + ReviewPolicy
      -> remediation on blocking review failure
  -> Approved | HumanRequired | Failed
  -> RunStore artifacts/pointer/recovery history
```

`ClaudeAgent` itself does not invoke Codex or Ollama; `Orchestrator` is the coordinator. **VERIFIED IN CODE.**

QA is a separate path in `Program.cs:63-111`: Patrol device setup/test execution/artifact collection/reporting, with infrastructure retries only. It is not a stage in the main task state machine. **VERIFIED IN CODE.**

Milestone execution wraps task runs in `Roadmap/MilestoneRunner.cs` and may call `GitWorkflowManager`; it is therefore a second orchestration layer around the main run. **VERIFIED IN CODE.**

## State machine, failures and limits

### States and transitions — VERIFIED IN CODE

States: `Created`, `Routing`, `Routed`, `Implementing`, `Validating`, `Reviewing`, `Remediating`, `WaitingRetry`, `Approved`, `HumanRequired`, `Failed`, `Completed`, `Abandoned` (`Models/WorkflowModels.cs:6`). Terminal states have no normal outgoing transition in `WorkflowStateMachine.cs:7-21`.

Authorized reopen is intentionally narrower than normal transitions:

- `HumanRequired` review/provider/context-overflow/routine-engineering recovery can reopen to `Validating` only when the corresponding persisted failure predicate passes.
- Ordinary `continue` does not run exceptional `HumanRequired` runs; `run retry <RUN_ID>` calls `RetryHumanRequiredAsync`.
- Permission denial during implementation can become terminal `Failed`; other human-required conditions become `HumanRequired`.

### Limits — VERIFIED IN CODE

Defaults in `Configuration/OrchestratorOptions.cs`:

| Mechanism | Default |
|---|---:|
| General provider transient retries | 3 |
| Malformed output retries | 2 |
| Unknown diagnosis retries | 1 |
| Deterministic validation remediation cycles | 5 |
| Engineering/review remediation cycles | 5 |
| Progress extensions for review convergence | 2 |
| Context reduction attempts | 2 |
| Process timeout | 1800 seconds |
| Ollama timeout / response retries | 60 seconds / 1 |
| Backoff | 15 seconds initial, 60 seconds max |
| Automatic wait ceiling | 15 minutes |
| Preflight dependency retries | 1 |
| QA infrastructure retries | 1 (`QaOptions.MaxInfrastructureRetries`) |

The exact recovery decision is in `FailureDiagnosis.cs:RecoveryPolicy.Decide`; provider/protocol counters are independent from engineering remediation counters (`RunRecord` comments at lines 194-200). **VERIFIED IN CODE.**

## Persistence, recovery and artifacts

### VERIFIED IN CODE

- `.ai-runs/<RUN_ID>/task.json`, `run.json`, `routing.json`, `engineering-profile.json`, `validation-expectations.json`, `validation-N.json`, `review-N.json`, `implementation.json`, `remediation-*.json`, `diagnosis-N.json`, `recovery-N.json`, `dry-run.json`, and QA artifacts are generated as stages execute.
- `.ai-runs/active-run.json` is an atomic pointer for nonterminal runs. Terminal saves clear the pointer if owned.
- `RunStore.AssessActiveAsync` repairs malformed/missing pointers; pointer repair currently moves then deletes the temporary retired pointer (`RunStore.cs:120-137`), preserving run artifacts but not the broken pointer file.
- `.ai-state/roadmap-state.json` is managed by `RoadmapStateStore`; `.ai-state/git-lifecycle-<milestone>.json`, reference index, backend audit and roadmap state are infrastructure artifacts.
- JSON artifact names are sanitized with `Path.GetFileName` and selected secret-shaped values are redacted by `RunStore.SecretPattern`.
- `WorkspaceBoundary` constrains configured paths to the workspace.

### INFERRED

The run record is both operational checkpoint and audit document; this is useful for extraction but currently couples lifecycle state, provider evidence, project policy outcomes and Git metadata into one large model.

## Configuration loading

### VERIFIED IN CODE

`Configuration/ConfigLoader.Load` resolves `ONLINEOS_ORCHESTRATOR_CONFIG`, a direct `appsettings.json`, or the legacy `tools/ai-orchestrator/appsettings.json`; it never falls back to the executable binary directory. It then applies other `ONLINEOS_*` environment overrides only when `Project.Id` is `onlineos-mobile`. Generic option defaults are neutral; the checked-in OnlineOS profile explicitly supplies `app`, `macos`, `OnlineOS-QA`, `b1208`, `developer`, `producao`, and Flutter/Patrol commands. `Project.Composition` records explicit provider/capability names; M2 adds `EngineCompositionRuntimeBuilder`, which requires explicit typed registrations and resolves only declared components.

### UNKNOWN

There is no external schema/versioning/migration mechanism for configuration, no YAML loader, and no CLI input for an external host's runtime registrations. The current executable remains OnlineOS-compatible only for operational commands; external consumers must host the builder in code until a later host/adapter milestone.

## Divergences and risks

- **VERIFIED IN CODE:** README and prompts call the product “OnlineOS”; namespace/assembly is `OnlineOs.AiOrchestrator`; QA and reference code use product-specific names.
- **VERIFIED IN CODE:** source documentation claims provider interfaces, but those interfaces still expose OnlineOS-derived models and prompts; this is capability isolation, not project isolation.
- **VERIFIED IN CODE:** the copied Git lifecycle is still OnlineOS-oriented; the public repository now satisfies repository discovery, but consumer execution remains dependent on an explicit workspace/profile decision.
- **INFERRED:** the first extraction should characterize and preserve behavior before any namespace, state or prompt changes.
- **HUMAN DECISION REQUIRED:** whether this baseline should remain a compatibility executable during extraction or become a new Engine host immediately.
