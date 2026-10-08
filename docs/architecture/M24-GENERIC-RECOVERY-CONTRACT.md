# M24 — Generic recovery transition contract

The Engine recovery API is consumer-neutral. An `ExecutionKey` identifies one
persisted execution by project, milestone, task, and stable execution id. A
recovery request records its reason and is represented by an append-only
`RecoveryAttempt`; it does not create a new task execution identity.

Recovery is allowed for terminal `Failed`, `Timeout`, `Cancellation`,
`Interrupted`, and crash/restart states. A `HumanRequired` execution remains
blocked until a matching explicit approval is persisted. Approval changes the
recovery record; it does not silently bypass the workflow state machine.

`MilestoneRunner` restores the persisted milestone state to `Running` only after
the host has authorized recovery. The current non-completed task is resumed
with its original run id, while tasks marked `Done` remain done. A non-terminal
persisted run uses `ContinueAsync`; a terminal run uses the generic workflow
reopen path. No second state machine is introduced.

Artifacts, checkpoints, and recovery attempts are idempotent by stable identity.
Previous task evidence remains persisted. Retry counters and recovery attempts are
separate. Memory `RewindAsync` remains a read-only historical projection and is
not a recovery operation.

The contract performs no provider, AWS, Terraform, OnlineOS, or commit activity.
Consumers own storage location and component composition.
