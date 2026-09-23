# Contract Quality Review — stabilization milestone

Status: reviewed; no public contract refactor required in this milestone.

## Scope

Reviewed `EngineHost`, `EngineHostContext`, execution results, task/milestone
sources, composition builder, `MilestoneRunner`, `RoadmapCatalog`, `RunStore`,
`RoadmapStateStore`, workflow state transitions, provider resolution and the
consumer boundary.

## Findings

| Severity | Finding | Impact | Decision |
|---|---|---|---|
| Medium | `EngineHost` constructs the current concrete `MilestoneRunner` and filesystem state store | Consumers cannot yet replace milestone persistence with a non-filesystem implementation | Defer until a second persistence implementation exists |
| Medium | `EngineHostContext` carries identity, configuration, registration, services and component names | The context is broad but cohesive for a composition root; splitting it now would add indirection | Keep and cover with host tests |
| Medium | `Succeeded` can be true for `CompleteAwaitingApproval` | A caller must inspect `Status` and `RequiresHumanApproval` to distinguish checkpoint pending from final approval | Preserve compatibility and document the distinction |
| Low | `IEngineMilestoneSource` returns the Engine roadmap DTO | Consumer code depends on a stable Engine data contract | Acceptable while the roadmap contract is the shared boundary |
| Low | `RoadmapStateStore` is filesystem-specific | Limits future distribution options | Defer to a persistence milestone |

## Quality assessment

- SOLID: host composition and workflow execution remain separated; domain policy is not added to Core.
- Clean Architecture: consumers provide adapters and the Engine depends on interfaces for providers, Git and runs.
- Cohesion: `EngineHost` owns composition and execution entry points; `MilestoneRunner` owns milestone progression.
- Coupling: no direct InfraSentinel dependency; OnlineOS compatibility remains outside generic host registration.
- Testability: factories, task sources, milestone sources, Git and run stores can be replaced with local fakes.
- Async/cancellation: public host operations propagate `CancellationToken`; source and workflow calls remain cancellable.
- Fail-closed: configuration, identity and declared component registrations are validated before factories or workflow execution.
- Observability: structured execution results and persisted run/milestone artifacts expose state, failure and approval status.

## Deferred decisions

Replacing `RoadmapStateStore` with a generic persistence interface and introducing
an explicit outcome enum would change the public contract. Those decisions require
a second consumer or a reviewed compatibility plan and are intentionally deferred.

The existing two Flutter characterization failures remain unrelated to this
review: the isolated Engine checkout has no `app/integration_test` Flutter tree.
