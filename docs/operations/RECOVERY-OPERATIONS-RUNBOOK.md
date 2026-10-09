# Recovery operations

Configure `FileRecoveryStore` beneath the consumer's namespaced state
directory. Use an execution key containing project, milestone, task and
execution identifiers. Record an attempt before recovery work and close it with
the final status and a sanitized error.

Persist artifact and checkpoint identities before requesting the corresponding
operation. Repeating the same identity is safe and must not create a second
artifact, checkpoint or logical commit request.

At the milestone host boundary, repeat recovery and approval calls are safe
after `CompleteAwaitingApproval` or `Approved`: persisted state is returned and
no task or checkpoint is executed again.

For `HumanRequired`, stop. Inspect the persisted reason and approve the exact
execution key explicitly. Do not use `RewindAsync` to resume work: Rewind only
constructs historical context. A terminal execution still needs the existing
Engine workflow integration to reopen it; do not create a consumer-side state
machine.
