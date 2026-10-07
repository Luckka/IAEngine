# ADR-019 — Generic recovery contract

## Status

Accepted for M22; terminal milestone continuation is exposed through the
generic EngineHost integration.

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
- `EngineHost.RecoverMilestoneAsync` reopens a persisted task through the
  existing `MilestoneRunner` and `Orchestrator`; the persistence layer remains
  independent of execution mechanics.
