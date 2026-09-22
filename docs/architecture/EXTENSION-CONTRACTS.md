# Extension Contracts — Evaluation

This is a contract assessment, not an implementation plan. Contracts should be introduced only where a real boundary has two or more plausible implementations or where a dependency rule must be enforced.

## Recommended first contracts

| Contrato | Problema resolvido | Consumidor | Implementação inicial | Ciclo de vida | Relação com classes atuais | Primeira extração | Risco de abstração prematura |
|---|---|---|---|---|---|---|---|
| `IProjectContextProvider` | Supplies bounded project identity, context paths, prompt inputs and reference context without Core knowing OnlineOS | Router, implementation agent, reviewer, context builder | Compatibility provider backed by current OnlineOS constants/config | Per run; immutable resolved context | Replaces constants in `OllamaTaskRouter`, `ClaudeAgent`, `RemediationContextBuilder`, `ReferenceInspection` inputs | Yes | Medium; keep it data-oriented, not a service-per-file façade |
| `IProjectPolicy` | Makes project-specific requirements explicit: review rubric, standards applicability, human gates | Orchestrator, review policy, validator planning | Compatibility policy wrapping `EngineeringStandardsPolicy`, `ReviewPolicy`, prototype gate | Per project/run | Extracts decisions from `EngineeringStandardsPolicy`, `ReviewPolicy`, `PrototypeEvidenceGate`, `VisualConformityReviewGate` | Yes | Medium; avoid one method per existing policy class |
| `IValidator` | Registers deterministic validation/gate units without hard-coded Flutter categories | Orchestration validation stage | Adapter around existing configured command execution; later Flutter/Patrol validators | Per host or per run, stateless preferred | Generalizes `ValidationRunner`; technology validators remain outside Core | Yes | Low/medium if result contract remains small |
| `IAgentProvider` | Resolves implementation/review/remediation agents from explicit provider configuration | Host/composition and orchestrator | Compatibility composition of `ClaudeAgent`, `CodexAgent`, `OllamaTaskRouter` | Per host; provider clients reused where safe | Consolidates current constructor wiring, not agent semantics | Yes | Medium; do not hide stage-specific contracts behind `object` |
| `IRunStateStore` | Decouples durable run/checkpoint/artifact state from filesystem implementation | Orchestrator, recovery, CLI | Existing `RunStore` as compatibility implementation | Per workspace/host; isolated root required | Renames/narrows current `IRunStore` responsibility | Yes | Low; current interface is already explicit |
| `IPreflightCheck` | Allows project/provider/technology checks to be registered and prevents host hard-coding | `PreflightService`/host | Wrappers for Git, provider health, command checks, optional Flutter/Patrol checks | Per invocation | Splits `PreflightService.RunAsync` checks | Yes | Low if checks return simple structured result |
| `IGitWorkflowPolicy` | Makes branch names, staging paths, merge/push permissions and protected branches project-owned | `GitWorkflowManager` | Compatibility policy using current `GitOptions` and hard-coded stage paths | Per milestone/workspace | Extracts `GitWorkflowManager` policy branches | Yes | Medium; safety policy must fail closed when absent |
| `IHumanInterventionPolicy` | Centralizes which failures/gates stop automatically and how required action is described | `RecoveryPolicy`, gates, Git lifecycle | Compatibility behavior from `RecoveryPolicy` and Git HumanRequired paths | Per project/run | Extracts category lists and human messages | Yes | Medium; preserve state machine, only move decisions |
| `ITaskSource` | Removes `Program.cs`/roadmap coupling from task acquisition | CLI/host, milestone runner | JSON task-file and roadmap adapter | Per invocation | `Program` task/task-file branches and `RoadmapCatalog` | Yes | Low |
| `IMilestoneSource` | Makes roadmap schema project-provided | `MilestoneRunner`, host | Compatibility `RoadmapCatalog` over current JSON | Per host/invocation | `RoadmapCatalog`, `RoadmapModels` | Yes | Low/medium; schema versioning remains needed |

## Contracts recommended later, not in first extraction

| Contrato | Assessment |
|---|---|
| `IProjectAdapter` | **RECOMMENDED later / HUMAN DECISION REQUIRED.** It can become a composition bundle, but creating a “god interface” containing context, validators, policy, Git and roadmap would reproduce current mixing. Prefer small contracts above and a host-side profile object. |
| `IContextRouter` | **RECOMMENDED later.** Current `ITaskRouter` already routes task metadata and context selection. First characterize `ITaskRouter` and add a separate context selector only if another implementation is needed. |
| `IRemediationPolicy` | **RECOMMENDED later.** Current `RecoveryPolicy` and `RemediationContextBuilder` cover different concerns. Create this only when validation/review remediation policies have genuinely different implementations. |
| `IReviewPolicy` | **RECOMMENDED later.** `ReviewPolicy` is already a concrete, small policy. Use it behind a contract when a second consumer needs a different review decision, not merely for symmetry. |
| `IMilestoneSource` | **YES for first extraction** if milestone host is retained; otherwise defer with `RoadmapCatalog` until a second source exists. |

## Existing contracts to preserve or narrow

### VERIFIED IN CODE

`Abstractions/Contracts.cs` already defines `IProcessRunner`, `ITaskRouter`, `IFailureDiagnoser`, `IImplementationAgent`, `IReviewAgent`, `IValidationRunner`, `IE2ETestRunner`, `IE2EDeviceManager`, `IGitService`, `IRunStore`, `IProgressReporter`. `Abstractions/GitWorkflowContracts.cs` defines `IGitWorkflowManager`.

### RECOMMENDED

- Keep `IProcessRunner`, `IGitService`, and `IRunStore`; they represent actual seams used by tests and runtime.
- Keep stage-specific `IImplementationAgent` and `IReviewAgent`; do not replace them with an untyped provider interface.
- Treat `IE2ETestRunner`, `IE2EDeviceManager` and ADB/Patrol contracts as adapter/technology contracts, not Core contracts.
- Add explicit result metadata for project/validator identity only if required by persistence; do not retrofit every current model preemptively.

## Contracts explicitly not recommended now

### RECOMMENDED

Do not create these solely for aesthetic symmetry:

- `IOnlineOsService`, `IFlutterService`, `IPatrolService`, `IFinGuardService`, `ICodeUpService`, or `IAwsService`.
- One interface for every concrete class (`IWorkflowStateMachine`, `IRunRecordFactory`, `IReviewFindingNormalizer`, etc.).
- A generic `IProvider` returning `object` or dynamic JSON for all router/implementation/review stages.
- A generic `IPolicy` with `bool Evaluate(...)`; gate inputs and reason codes would be lost.
- A generic `IAdapter` that combines project context, commands, Git, validators and prompts.
- A Core `IPrototypeValidator`; prototype conformity is a project policy/validator implementation.

## Lifecycle and isolation requirements

### RECOMMENDED

- Resolve project profile once at host startup and pass immutable values into a run.
- Scope `IRunStateStore` to one workspace and one project identity; include project identity in artifact metadata.
- Do not share mutable provider clients or context builders across projects unless their state is demonstrably request-local.
- Register validators and policies explicitly in the host; no ambient global registry.
- Make missing required policy/validator configuration a preflight `ConfigurationError`/`HUMAN_REQUIRED`, not a silent default.

### UNKNOWN

Whether a consumer will need dynamic assembly/plugin discovery, remote task sources, or multi-tenant concurrency is not specified. Those requirements must not drive first-extraction contracts.

## Compatibility relation

### RECOMMENDED

Initially, a compatibility composition root should instantiate current classes and new seams without changing prompts, command lines, state transitions or limits. The old concrete behavior remains the reference until characterization tests prove equivalent behavior.

### HUMAN DECISION REQUIRED

Choose whether compatibility is provided by the current executable, a façade namespace, or a separate host. This choice determines whether existing `.ai-runs` JSON and external scripts must remain byte/schema compatible.

