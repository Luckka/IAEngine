# Generic recovery state machine

```text
persisted workflow state
        |
        +-- active/non-terminal ------------------> ContinueAsync
        |
        +-- Failed/timeout/cancel/crash ----------> approved recovery request
        |                                             |
        |                                             v
        |                                      reopen existing run
        |
        +-- HumanRequired -----------------------> explicit approval required
                                                      |
                                                      v
                                               reopen existing run
```

The reopened run enters the existing workflow state machine at `Implementing`
when implementation is incomplete, or `Validating` when implementation is
already complete. The milestone state is restored to `Running` only for the
current task; completed tasks remain `Done`. The existing orchestrator then
drives the run to `Approved`, `HumanRequired`, or `Failed`.

`CompleteAwaitingApproval` is a milestone checkpoint state, not a recoverable
task failure. `Approved` runs are not reopened by generic recovery. Repeated
artifact/checkpoint writes with the same identity are no-ops, and repeated
recovery calls preserve the execution key and historical attempts.
