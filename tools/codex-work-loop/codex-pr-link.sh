#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
source "$DIR/codex-work-loop-lib.sh"

PROJECT=""; CONFIG=""; MILESTONE="milestone"; TITLE=""
while (($#)); do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --config) CONFIG="$2"; shift 2;;
    --milestone) MILESTONE="$2"; shift 2;;
    --title) TITLE="$2"; shift 2;;
    -h|--help) printf '%s\n' 'Usage: codex-pr-link.sh --project /path [--config file] [--milestone M<N>]'; exit 0;;
    *) die "unknown argument: $1";;
  esac
done
[[ -n "$PROJECT" ]] || usage
if [[ -n "$CONFIG" ]]; then parse_common_args --project "$PROJECT" --config "$CONFIG"; else parse_common_args --project "$PROJECT"; fi
load_config
validate_repository
assert_execution_environment
[[ "$BRANCH" != main ]] || die "protected branch"
REMOTE="$(git -C "$PROJECT" remote get-url origin 2>/dev/null || true)"
[[ -n "$REMOTE" ]] || die "origin remote is required"
if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  TITLE="${TITLE:-$MILESTONE: $PROJECT_ID}"
  BODY_FILE="$(mktemp "${TMPDIR:-/tmp}/codex-pr.XXXXXX.md")"
  trap 'rm -f "$BODY_FILE"' EXIT
  printf '# %s\n\nAutomated bounded milestone cycle for `%s`.\n\nBuild and tests were validated locally.\n' "$TITLE" "$MILESTONE" > "$BODY_FILE"
  gh pr create --base "$PR_BASE" --head "$BRANCH" --title "$TITLE" --body-file "$BODY_FILE"
else
  remote_pr_url "$REMOTE" "$BRANCH"
fi
