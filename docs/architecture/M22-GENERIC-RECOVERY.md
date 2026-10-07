# M22 — Generic Recovery Contract

M22 adds a consumer-neutral recovery record to `IAEngine.Core`. It is separate
from the existing workflow state machine and does not make Rewind an execution
recovery mechanism.

## Contract

An `ExecutionKey` is composed of project, milestone, task and execution
identifiers. A `FileRecoveryStore` persists a versioned `RecoveryExecution`
under the consumer-owned state directory. Attempts, sanitized errors,
artifact identities, checkpoint identities and human approvals are append-only
logical records.

Artifacts and checkpoints are idempotent by their stable identity. Repeated
writes return the existing execution without adding a duplicate. Recovery and
retry remain distinct concepts: this contract records recovery history; the
existing `MilestoneRunner` and `Orchestrator` continue to own task execution
and workflow transitions.

`HumanRequired` executions cannot be recovered until an explicit approval is
persisted. Approval is bound to the execution key and duplicate approval ids
are harmless.

## Boundaries and limitations

The contract does not execute tasks, call providers, create commits, or access
AWS. It does not use memory `RewindAsync`; Rewind remains a read-only contextual
projection. Full terminal-run continuation still requires wiring the persisted
record into the existing runner through a separately reviewed orchestration
change.

The schema is currently version 1. Breaking changes require an explicit
migration and compatibility tests. Error text is caller-supplied and must be
sanitized before persistence; the generic store does not receive credentials or
provider payloads.
