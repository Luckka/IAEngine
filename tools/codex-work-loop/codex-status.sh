#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
source "$DIR/codex-work-loop-lib.sh"
parse_common_args "$@"
load_config
validate_repository
working_tree_state
SUMMARY="$(milestone_summary)"
CURRENT="$(printf '%s\n' "$SUMMARY" | sed -n 's/^current=//p')"
NEXT="$(printf '%s\n' "$SUMMARY" | sed -n 's/^next=//p')"
OUTPUT="$(report_file)"
mkdir -p "$(dirname "$OUTPUT")"
CHANGES="$(git -C "$PROJECT" status --short --untracked-files=no | sed -E 's/[[:space:]]+[^[:space:]]+$/ [path-redacted]/' || true)"
SUBJECT="$(git -C "$PROJECT" log -1 --format='%s')"
cat > "$OUTPUT" <<EOF
# Work Loop Status — ${PROJECT_ID}

- Generated: $(date -u '+%Y-%m-%dT%H:%M:%SZ')
- Project: ${PROJECT_ID}
- Repository: ${REPOSITORY:-$(basename "$PROJECT")}
- Branch: ${BRANCH}
- Commit: ${COMMIT}
- Working tree: ${WORKTREE}
- Current milestone: ${CURRENT}
- Next milestone: ${NEXT}
- Build known: not executed by status generator
- Tests known: not executed by status generator
- Last semantic commit subject: ${SUBJECT}
- Local tracked changes: ${CHANGES:-none}
- AWS accessed: false
- AWS profile used: none
- AWS mutations executed: false
- Corporate profile accessed: false
- OnlineOS accessed: false
- Recommendation: generate NEXT-CODEX-TASK.md and review it before --once.

Configured build: ${BUILD_COMMAND}

Configured tests: ${TEST_COMMAND}
EOF
printf '%s\n' "$OUTPUT"
