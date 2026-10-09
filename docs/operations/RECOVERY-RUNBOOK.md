# Recovery runbook

Use the consumer-owned `FileRecoveryStore` and public `EngineHost` recovery
API. Record a stable `ExecutionKey` and a sanitized recovery attempt before
reopening a terminal run. Retry counters belong to the workflow run and are
distinct from recovery-attempt numbers.

Failed, timed out, cancelled, interrupted, and simulated crash/restart runs are
recoverable through the existing workflow. `HumanRequired` remains blocked
until explicit approval for the exact execution key is persisted. Completed
tasks keep their original run identity and are never replayed.

Artifacts, checkpoints, and logical commits use stable identities. Repeated
writes are no-ops. Repeating recovery after `CompleteAwaitingApproval` or
`Approved`, and repeating approval after `Approved`, returns persisted state
without reopening work or creating a second checkpoint. `RewindAsync` is
read-only context and is never a recovery action.

This contract is local-only: no AWS, provider, OnlineOS, push, or merge activity
is performed.
