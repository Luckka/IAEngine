# Local Codex Work Loop

This tool is an operational wrapper around the installed `codex exec` CLI. It
is intentionally outside `IAEngine.Core` and is configured per repository.

```bash
./codex-work-loop.sh --project /path/to/project --dry-run
./codex-work-loop.sh --project /path/to/project --once
./codex-work-loop.sh --project /path/to/project --iterations 20 --auto-push --auto-pr
```

Each cycle is bounded by configuration, uses one generated milestone prompt,
requires a semantic commit, runs build and tests sequentially, and then
generates the next prompt. Push and PR creation are opt-in flags. The loop
never merges, force-pushes, accesses AWS, invokes Terraform/MCP/providers, or
touches OnlineOS.

`codex-pr-link.sh` uses `gh pr create` only when `gh auth status` succeeds;
otherwise it returns the safe GitHub branch PR URL. The loop stops on dirty
state, a protected branch, failed validation, contract gaps, human decisions,
secrets, or scope expansion.
