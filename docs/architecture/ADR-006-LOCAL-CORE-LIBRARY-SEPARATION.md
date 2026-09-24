# ADR-006 — Local Core library separation

Status: **Proposed; implementation in progress and not merged to main**

## Context

M5 identified that the executable IAEngine project was not a safe package
boundary because its assembly also contained OnlineOS compatibility behavior.
M6 requires a local library boundary without publishing NuGet packages or
altering the read-only OnlineOS repository.

## Decision proposed

Introduce `IAEngine.Core` as the reusable local library and
`IAEngine.OnlineOSAdapter` as the technology-specific adapter. Keep
`OnlineOs.AiOrchestrator.csproj` as the compatibility executable that composes
Core and the adapter during migration.

Consumers that need only generic workflow behavior, including InfraSentinel,
reference `IAEngine.Core` directly. The adapter is not a transitive dependency
of the consumer.

## Boundary contracts

- `IEngineReferenceContextProvider` supplies opaque consumer/adapter context to
  Core without naming a repository or technology.
- `IMilestoneTaskGate` lets a consumer add an optional post-approval gate.
- Core owns workflow, composition, persistence mechanics, retries, recovery and
  state transitions.
- OnlineOS adapter owns Flutter, Patrol, ADB, provider prompts, reference
  inspection and compatibility policy.

## Compatibility and limitations

The namespace and several serialized model fields retain historical names for
compatibility. A later API review is required before removing them. No package
is produced, no public distribution contract is claimed, and the executable
compatibility path remains covered by the existing suite.

