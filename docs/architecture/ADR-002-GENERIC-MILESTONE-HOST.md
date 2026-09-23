# ADR-002 — Generic milestone execution through the host

Status: Proposed; not automatically approved.

## Context

The generic host could execute one `DevelopmentTask`, while the existing
`MilestoneRunner` already owned dependency ordering, per-task state, checkpoint
approval, and failure transitions. External consumers needed access to that
behavior without rebuilding the runner.

## Decision proposed

`EngineHost` adapts one consumer-provided `IEngineMilestoneSource` result into an
in-memory `RoadmapCatalog`, creates the existing `MilestoneRunner`, and routes
each compiled task through the existing `Orchestrator`. `RoadmapStateStore`
accepts an explicit state directory so each consumer controls its own milestone
state boundary.

The host returns `EngineMilestoneExecutionResult` and exposes separate operations
for run, continue, and explicit human checkpoint approval.

## Consequences

- Existing task workflow, retry, remediation, recovery, and `HUMAN_REQUIRED`
  semantics are reused.
- Consumer milestone definitions are validated by the existing roadmap validator.
- The host does not add product, security, AWS, Flutter, or scanning rules.
- Full generic milestone source/execution distribution remains future work.
