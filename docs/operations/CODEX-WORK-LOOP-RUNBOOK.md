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

## Autonomous push and PR-link cycle

```bash
tools/codex-work-loop/codex-work-loop.sh \
  --project /path/to/project --iterations 20 --auto-push --auto-pr
```

The two flags are intentionally coupled. Each validated cycle pushes only the
current feature branch, creates a PR with `gh pr create` when authenticated,
or records a GitHub branch PR URL when `gh` is unavailable. The loop never
merges. It writes `docs/status/LOOP-<timestamp>.md` and
`docs/status/PULL-REQUESTS.md`, then generates the next prompt before the next
cycle.

## Recovery and review

Inspect the generated status report, Codex last message and Git diff. Do not
use Rewind as execution recovery. If the task exposes an IAEngine contract gap,
stop and record it for human review. No AWS, Terraform, MCP, provider or
OnlineOS operation is supported by this loop.

PR review is asynchronous. An open PR does not block the next cycle; only a
real execution, security, architecture, contract or milestone-scope block does.
