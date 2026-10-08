# ADR: Generic recovery restores the persisted milestone state

## Status

Accepted — 2026-10-07

## Context

M22 already persisted generic recovery records and exposed
`EngineHost.RecoverMilestoneAsync`, but the runner passed a terminal run to the
orchestrator without restoring the corresponding milestone runtime state. The
milestone therefore remained `Failed` or `HumanRequired`, so its drive loop did
not continue.

## Decision

Before invoking the existing orchestrator, `MilestoneRunner.RecoverAsync`
restores the persisted milestone state to `Running` for the current task. A
terminal run is reopened through `Orchestrator.RecoverAsync`; an active run is
continued through `Orchestrator.ContinueAsync`. The original run id and task
failure evidence are retained. Completed tasks cannot be recovered and remain
excluded from task selection.

## Consequences

Recovery is observable through the existing workflow transitions and persisted
state. Retry and memory rewind remain separate. The change is generic and adds
no consumer, provider, AWS, or OnlineOS knowledge.
