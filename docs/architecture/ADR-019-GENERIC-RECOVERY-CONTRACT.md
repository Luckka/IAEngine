# ADR-019 — Generic recovery contract

## Status

Accepted for incremental M22 implementation; terminal milestone continuation
remains an integration follow-up.

## Decision

Expose recovery through `IAEngine.Core.Recovery` using a stable execution key,
versioned persisted execution records, append-only attempts, stable artifact and
checkpoint identities, and explicit HumanRequired approvals. `EngineHost`
exposes only generic recovery APIs. Consumers provide the storage location and
continue to own project-specific evidence.

The existing `MilestoneRunner`, `Orchestrator` and workflow state machine remain
the only execution owners. No consumer-specific recovery executor is added.

## Consequences

- Recovery state survives process restart when the consumer uses the file store.
- Duplicate artifacts and checkpoints are rejected logically by identity.
- Rewind remains separate and read-only.
- A later change is still required to reopen terminal task runs through the
  central workflow state machine; this is intentionally not hidden in M22's
  persistence layer.
