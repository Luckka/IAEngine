# M7 — IAEngine.Core public API inventory

Status: inventory completed on 2026-09-24. This document inventories the API
surface as it exists after M6; it is not a promise that every public type is a
supported extension point.

## Classification

| Area | Public types | Classification | Compatibility assessment |
| --- | --- | --- | --- |
| Hosting | `EngineHost`, `EngineHostContext`, `EngineHostComponentNames`, `EngineExecutionResult`, `EngineMilestoneExecutionResult` | Stable API; required by InfraSentinel | Keep unchanged in M7. The host is the main consumer entry point, but its context currently exposes historical contracts. |
| Composition | `IProjectComposition`, `EngineCompositionPlan`, `EngineCompositionRuntimeBuilder`, `EngineCompositionRuntime`, component IDs/statuses and resolution exception | Stable API with experimental registration details | Keep registration behavior. A neutral façade must preserve provider, validator, policy and capability resolution. |
| Execution contracts | `ITaskRouter`, `IImplementationAgent`, `IReviewAgent`, `IValidationRunner`, `IFailureDiagnoser`, `IProcessRunner`, `IProgressReporter` | Required to substitute providers and validators; some are experimental | Keep semantics. Public signatures depend on historical workflow models, so namespace replacement is breaking. |
| Git contracts | `IGitService`, `IGitWorkflowManager`, `GitWorkflowResult`, lifecycle models | Required infrastructure abstraction | Keep. These are generic in behavior but are coupled to the current workflow state model. |
| Persistence | `IRunStore`, `RunStore`, `RoadmapStateStore` and run/artifact models | Required API for recovery, retries and artifacts | Keep. Persistence models are serialized; renaming fields or types risks state incompatibility. |
| Milestones/tasks | `IEngineTaskSource`, `IEngineMilestoneSource`, `MilestoneDefinition`, `RoadmapTaskDefinition`, runtime state/status types | Required by InfraSentinel; historical prototype fields | Keep execution fields. `PrototypeStates` and `PrototypeConformityRequired` are historical/OnlineOS-specific and should be isolated in a future adapter or major version. |
| Workflow models | `DevelopmentTask`, `EngineeringProfile`, validation/review/failure/recovery/run models | Stable serialized domain surface with historical fields | Keep for compatibility. Engineering flags, failure categories and review evidence need a separate neutral model design before replacement. |
| Configuration | `AppOptions`, `ProjectProfileOptions`, `QaOptions`, `OrchestratorOptions`, provider names and composition flags | Historical compatibility API; OnlineOS-specific portions | Generic project profile is reusable. `IsOnlineOsCompatibilityProfile`, Flutter/Patrol/ADB/reference options and legacy provider names remain adapter-owned for now. |
| QA/prototype | E2E, Patrol, ADB, device artifact and prototype evidence types | OnlineOS-specific API | Must not move into or be consumed by a neutral Core surface. They remain in `IAEngine.OnlineOSAdapter`. |
| Pipeline implementations | Orchestrator, workflow state machine, validators, policies, stores and services | Internal implementation accidentally public | Do not expose as new API. Making them internal is a breaking change because tests and existing consumers reference several of them. |
| Roadmap helpers | `RoadmapCatalog`, `MilestoneRunner`, gates and state store | Internal implementation with public seams | Keep binary compatibility in M7; future API should expose only host/source abstractions. |

## Consumer inventory

- The legacy CLI consumes the historical `OnlineOs.AiOrchestrator.*` surface and
  the OnlineOS adapter.
- `IAEngine.Core.Tests` exercises composition, host, configuration and boundary
  behavior.
- `OnlineOs.AiOrchestrator.Tests` is the compatibility suite and references the
  historical surface broadly.
- InfraSentinel currently consumes `EngineHost`, composition, contracts,
  persistence, Git and milestone models from the historical namespace. It does
  not reference `IAEngine.OnlineOSAdapter`.
- The OnlineOS checkout is external, read-only, and is not a consumer to modify
  in this milestone.

## M7 compatibility strategy

The smallest safe strategy is to retain the historical namespace and contracts
temporarily, document them as compatibility API, and defer removal to a major
version. Do not use internal `using` aliases as public compatibility. A future
neutral façade must be a deliberate, tested mapping for host, models, contracts,
configuration, persistence and milestone state together; a partial wrapper would
produce two competing domain models.

## Public types that are candidates for obsolescence

`AppOptions`' OnlineOS-specific sections, `ProjectProfileOptions.IsOnlineOsCompatibilityProfile`,
historical provider names, prototype fields on roadmap models, and the public
pipeline implementation classes are candidates. None is marked `[Obsolete]` in
M7 because current CLI and compatibility consumers still compile against them.

## Learning Checkpoint

1. A stable public API has intentional semantics, small substitutable contracts,
   compatible serialization, and consumers/tests that define its behavior.
2. Renaming a namespace changes source and binary identity, so existing consumers
   fail even when the implementation is unchanged.
3. Compatibility can be preserved by keeping a narrow legacy façade while new
   consumers migrate to a complete neutral model; preserving every old internal
   type would not be necessary.
4. InfraSentinel must not consume the OnlineOS adapter because its infrastructure
   validation is independent of Flutter, Patrol, ADB and OnlineOS prompts.
5. A compatibility façade maps an old contract to a deliberate new contract;
   a generic abstraction is a reusable domain boundary, not merely a rename.
6. Mark an API obsolete only after a supported replacement exists, migration is
   documented, and the old API can remain during the promised compatibility window.
7. A major version is needed to remove old contracts because namespace, type,
   serialized-state, and source compatibility can all break at once.

## Decision gate

`HUMAN_DECISION_REQUIRED = true` for introducing a complete neutral façade or
removing historical contracts. M7 makes no breaking public-contract change.
