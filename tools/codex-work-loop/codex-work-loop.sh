#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
source "$DIR/codex-work-loop-lib.sh"
MODE=""
ITERATIONS=1
PROJECT=""
CONFIG=""
while (($#)); do
  case "$1" in
    --project) [[ $# -gt 1 ]] || usage; PROJECT="$2"; shift 2;;
    --config) [[ $# -gt 1 ]] || usage; CONFIG="$2"; shift 2;;
    --dry-run) MODE=dry-run; shift;;
    --once) MODE=once; shift;;
    --iterations) [[ $# -gt 1 ]] || usage; MODE=iterations; ITERATIONS="$2"; shift 2;;
    -h|--help) printf 'Usage: codex-work-loop.sh --project /path --dry-run|--once|--iterations N\n'; exit 0;;
    *) die "unknown argument: $1";;
  esac
done
[[ -n "$PROJECT" && -n "$MODE" ]] || die "project and execution mode are required"
[[ "$ITERATIONS" =~ ^[1-9][0-9]*$ ]] || die "iterations must be positive"
[[ "$MODE" != once ]] || ITERATIONS=1
if [[ -n "$CONFIG" ]]; then parse_common_args --project "$PROJECT" --config "$CONFIG"; else parse_common_args --project "$PROJECT"; fi
load_config
[[ "$ITERATIONS" -le "$MAX_ITERATIONS" ]] || die "iteration limit exceeded"
trap 'die "interrupted; stopping before another cycle"' INT TERM
generate() {
  "$DIR/codex-status.sh" --project "$PROJECT" --config "$CONFIG" >/dev/null
  "$DIR/codex-next-task.sh" --project "$PROJECT" --config "$CONFIG" >/dev/null
}
if [[ "$MODE" == dry-run ]]; then
  validate_repository
  generate
  info "dry-run complete; Codex not invoked"
  exit 0
fi
for ((cycle=1; cycle<=ITERATIONS; cycle++)); do
  parse_common_args --project "$PROJECT" --config "$CONFIG"
  load_config
  validate_repository
  assert_execution_environment
  assert_clean_for_execution
  assert_no_blocking_markers
  generate
  BASE_COMMIT="$COMMIT"
  OUTPUT_FILE="$(resolve_path docs/status/codex-last-message.txt)"
  mkdir -p "$(dirname "$OUTPUT_FILE")"
  PROMPT="Read AGENTS.md and the generated NEXT-CODEX-TASK.md. Execute exactly one coherent slice. Do not access AWS, Terraform, MCP, providers, external infrastructure, OnlineOS, or IAEngine.OnlineOSAdapter. Do not change main, force-push, merge, create branches, or use destructive Git commands. Validate build and tests sequentially. Stop on failure, human decision, ENGINE_CONTRACT_GAP, secret or scope expansion. Keep within configured limits and create no more than one Conventional Commit after validation. Return a concise report."
  info "starting Codex cycle ${cycle}/${ITERATIONS}"
  codex exec --cd "$PROJECT" --sandbox workspace-write --ask-for-approval on-request --output-last-message "$OUTPUT_FILE" "$PROMPT"
  validate_repository
  [[ "$BRANCH" != main ]] || die "protected branch after Codex"
  changed="$(changed_file_count "$BASE_COMMIT")"
  [[ "$changed" -le "$MAX_FILES" ]] || die "file limit exceeded"
  commits="$(git -C "$PROJECT" rev-list --count "$BASE_COMMIT"..HEAD)"
  [[ "$commits" -le "$MAX_COMMITS" ]] || die "commit limit exceeded"
  [[ "$commits" -gt 0 ]] || die "Codex left changes uncommitted"
  subject="$(git -C "$PROJECT" log -1 --format='%s')"
  conventional_commit "$subject" || die "latest commit is not Conventional Commit"
  sh -c "$BUILD_COMMAND"
  sh -c "$TEST_COMMAND"
  git -C "$PROJECT" diff --check
  generate
  info "cycle ${cycle} validated"
done
