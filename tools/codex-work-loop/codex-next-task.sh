#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
source "$DIR/codex-work-loop-lib.sh"
parse_common_args "$@"
load_config
validate_repository
working_tree_state
TASK_FILE="$(generated_prompt_file)"
mkdir -p "$(dirname "$TASK_FILE")"
SUMMARY="$(milestone_summary)"
CURRENT="$(printf '%s\n' "$SUMMARY" | sed -n 's/^current=//p')"
NEXT="$(printf '%s\n' "$SUMMARY" | sed -n 's/^next=//p')"
cat > "$TASK_FILE" <<EOF
# Next Codex Task — ${PROJECT_ID}

Generated deterministically by codex-next-task.sh.

## Context

- Project: ${PROJECT_ID}
- Repository: ${REPOSITORY:-$(basename "$PROJECT")}
- Branch: ${BRANCH}
- Commit: ${COMMIT}
- Working tree: ${WORKTREE}
- Roadmap: $(roadmap_file)
- Status: $(status_file)
- Current milestone: ${CURRENT}
- Next milestone candidate: ${NEXT}

## Objective

Implement exactly one small, reviewable slice of the next safe milestone. Confirm roadmap, status, branch and architecture before editing.

## Scope

- Read AGENTS.md, the configured roadmap/status and relevant ADRs.
- Inspect current Git state before changing files.
- Implement one coherent slice only.
- Run build and tests sequentially, then git diff --check.

## Out of scope

- OnlineOS or IAEngine.OnlineOSAdapter.
- AWS, Terraform, MCP, providers, external infrastructure, network calls or credentials.
- main, force push, merge, branch creation or destructive Git operations.
- A second state machine, workaround for a missing contract or unrelated cleanup.
- More than ${MAX_FILES} changed files or ${MAX_COMMITS} commit in this cycle.

## Expected validation

- $(config_get "$CONFIG" diffCommand)
- $(config_get "$CONFIG" buildCommand)
- $(config_get "$CONFIG" testCommand)

## Stop conditions

Stop for dirty/unexplained state, failed validation, secrets, external access, ENGINE_CONTRACT_GAP, HUMAN_DECISION_REQUIRED, protected branches or scope expansion.

## Report and commit

Report branch, commit, files, build/tests, blockers, AWS/profile state, OnlineOS state and recommendation. Suggested semantic commit: $(config_get "$CONFIG" suggestedCommit).
EOF
cp "$TASK_FILE" "$(resolve_path docs/operations/NEXT-CODEX-TASK.md)"
printf '%s\n' "$TASK_FILE"
