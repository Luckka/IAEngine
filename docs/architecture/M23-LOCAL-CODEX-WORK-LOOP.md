# M23 — Local Codex Work Loop

M23 adds a reusable, project-configured shell automation outside
`IAEngine.Core`. It reads persistent rules, roadmap, Git state and configured
validation commands, then generates a deterministic task context for Codex.

The loop has three explicit modes:

- `--dry-run` generates status and next-task files without invoking Codex;
- `--once` runs one Codex cycle and validates one slice;
- `--iterations N` runs at most N bounded, validated cycles.

The loop is fail-closed for protected branches, dirty unexplained trees, AWS
profiles, external commands, OnlineOS paths, architectural gaps, human
decisions and exceeded file/commit limits. It does not access AWS and does not
belong in the runtime Core or any project-specific adapter.

Consumers provide JSON configuration with their root, roadmap, status,
commands, branch policy, limits and disabled external-access policies. A
consumer configuration never contains credentials.

The installed Codex CLI currently supports `codex exec`; the loop uses only
documented local flags and approval-on-request. If the CLI changes, the
invocation must be reviewed before updating the script.
