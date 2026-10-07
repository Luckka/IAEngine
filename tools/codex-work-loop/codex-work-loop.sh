#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
source "$DIR/codex-work-loop-lib.sh"
MODE=""
ITERATIONS=1
PROJECT=""
CONFIG=""
CLI_AUTO_PUSH=false
CLI_AUTO_PR=false
while (($#)); do
  case "$1" in
    --project) [[ $# -gt 1 ]] || usage; PROJECT="$2"; shift 2;;
    --config) [[ $# -gt 1 ]] || usage; CONFIG="$2"; shift 2;;
    --dry-run) MODE=dry-run; shift;;
    --once) MODE=once; shift;;
    --iterations) [[ $# -gt 1 ]] || usage; MODE=iterations; ITERATIONS="$2"; shift 2;;
    --auto-push) CLI_AUTO_PUSH=true; shift;;
    --auto-pr) CLI_AUTO_PR=true; shift;;
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
[[ "$ITERATIONS" -le "$MAX_MILESTONES" ]] || die "milestone limit exceeded"
if [[ "$CLI_AUTO_PUSH" == true || "$CLI_AUTO_PR" == true ]]; then
  [[ "$CLI_AUTO_PUSH" == true && "$CLI_AUTO_PR" == true ]] || die "--auto-push and --auto-pr must be used together"
  AUTO_PUSH=true; AUTO_PR=true
fi
trap 'die "interrupted; stopping before another cycle"' INT TERM
generate() {
  "$DIR/codex-status.sh" --project "$PROJECT" --config "$CONFIG" >/dev/null
  "$DIR/codex-next-task.sh" --project "$PROJECT" --config "$CONFIG" >/dev/null
}
write_cycle_report() {
  local cycle="$1" base="$2" pr_url="$3" report summary current next changed commits
  report="$(loop_report_file)"; summary="$(milestone_summary)"
  current="$(printf '%s\n' "$summary" | sed -n 's/^current=//p')"; next="$(printf '%s\n' "$summary" | sed -n 's/^next=//p')"
  changed="$(git -C "$PROJECT" diff --name-only "$base" HEAD | sort -u | sed '/^$/d' | sanitize_report_text)"
  commits="$(git -C "$PROJECT" log --format='%h %s' "$base"..HEAD | sanitize_report_text)"
  mkdir -p "$(dirname "$report")"
  {
    printf '# Work Loop Cycle %s\n\n' "$cycle"
    printf '%s\n' "- Project: $PROJECT_ID" "- Milestone: $current" "- Next milestone: $next" "- Branch: $BRANCH" "- Commit: $COMMIT" "- PR link: ${pr_url:-not-created}" "- Build: passed" "- Tests: passed" "- SOLID/Clean Architecture review: required by prompt" "- AWS accessed: false" "- AWS profile: none" "- OnlineOS modified: false" "- HUMAN_REQUIRED: false" "- STOP_REASON: none"
    printf '\n## Commits\n%s\n\n## Files\n%s\n' "${commits:-none}" "${changed:-none}"
  } | sanitize_report_text > "$report"
}
record_pr() {
  local milestone="$1" pr_url="$2" registry="$(pr_registry_file)"
  mkdir -p "$(dirname "$registry")"
  [[ -f "$registry" ]] || printf '# Pull Requests\n\n' > "$registry"
  printf -- '- project: %s; milestone: %s; branch: %s; commit: %s; PR URL: %s; status: opened-or-generated; date: %s; build: passed; tests: passed\n' "$PROJECT_ID" "$milestone" "$BRANCH" "$COMMIT" "$pr_url" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" >> "$registry"
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
  if [[ "$CLI_AUTO_PUSH" == true && "$CLI_AUTO_PR" == true ]]; then AUTO_PUSH=true; AUTO_PR=true; fi
  validate_repository
  assert_execution_environment
  assert_clean_for_execution
  assert_no_blocking_markers
  generate
  BASE_COMMIT="$COMMIT"
  PROMPT_FILE="$(generated_prompt_file)"
  OUTPUT_FILE="$(resolve_path docs/status/codex-last-message.txt)"
  mkdir -p "$(dirname "$OUTPUT_FILE")"
  PROMPT="$(cat "$PROMPT_FILE")"
  info "starting Codex cycle ${cycle}/${ITERATIONS}"
  codex exec --cd "$PROJECT" --sandbox workspace-write --ask-for-approval on-request --output-last-message "$OUTPUT_FILE" "$PROMPT"
  validate_repository
  [[ "$BRANCH" != main ]] || die "protected branch after Codex"
  if rg -n 'AWS|Terraform|MCP|OnlineOS|ENGINE_CONTRACT_GAP|HUMAN_REQUIRED|HUMAN_DECISION_REQUIRED' "$OUTPUT_FILE" >/dev/null 2>&1; then die "Codex report contains a protected or human-required condition"; fi
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
  if [[ "$AUTO_PUSH" == true ]]; then git -C "$PROJECT" push origin "$BRANCH"; fi
  PR_URL=""
  if [[ "$AUTO_PR" == true ]]; then
    PR_URL="$("$DIR/codex-pr-link.sh" --project "$PROJECT" --config "$CONFIG" --milestone "$MILESTONE_HINT")"
    record_pr "$MILESTONE_HINT" "$PR_URL"
  else
    REMOTE_URL="$(git -C "$PROJECT" remote get-url origin 2>/dev/null || true)"
    if [[ -n "$REMOTE_URL" ]]; then PR_URL="$(remote_pr_url "$REMOTE_URL" "$BRANCH")"; else PR_URL="not-created"; fi
  fi
  write_cycle_report "$cycle" "$BASE_COMMIT" "$PR_URL"
  generate
  info "cycle ${cycle} validated"
done
