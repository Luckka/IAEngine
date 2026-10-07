# Codex Work Loop runbook

## Dry-run

```bash
tools/codex-work-loop/codex-work-loop.sh --project "$PWD" --dry-run
```

Review `docs/operations/NEXT-CODEX-TASK.md` and the generated report under
`docs/status/`. Dry-run does not invoke Codex or create commits.

## One cycle

```bash
tools/codex-work-loop/codex-work-loop.sh --project "$PWD" --once
```

The script requires an allowed feature/fix/docs/chore branch, clean state, no
AWS profile environment, and an explicit non-empty configuration. Codex runs
with workspace-write and approval-on-request, then the configured build and
tests run sequentially. The script accepts at most one Conventional Commit.

## Bounded cycles

```bash
tools/codex-work-loop/codex-work-loop.sh --project "$PWD" --iterations 3
```

The configured maximum is an upper bound. A failed build/test, changed branch,
dirty unexpected tree, contract gap, human decision or security-policy
violation stops the loop before another cycle.

## Recovery and review

Inspect the generated status report, Codex last message and Git diff. Do not
use Rewind as execution recovery. If the task exposes an IAEngine contract gap,
stop and record it for human review. No AWS, Terraform, MCP, provider or
OnlineOS operation is supported by this loop.
