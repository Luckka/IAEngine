#!/usr/bin/env bash
set -euo pipefail

die() { printf 'codex-work-loop: %s\n' "$*" >&2; exit 1; }
info() { printf 'codex-work-loop: %s\n' "$*"; }
usage() { printf 'Usage: script --project /path [--config /path/config.json]\n' >&2; exit 2; }

config_get() {
  python3 -c 'import functools,json,sys; v=functools.reduce(lambda x,p: x.get(p) if isinstance(x,dict) else None,sys.argv[2].split("."),json.load(open(sys.argv[1]))); print("true" if v is True else "false" if v is False else "" if v is None else v if isinstance(v,str) else json.dumps(v,separators=(",",":")))' "$1" "$2"
}
config_array() {
  python3 -c 'import functools,json,sys; v=functools.reduce(lambda x,p: x.get(p) if isinstance(x,dict) else None,sys.argv[2].split("."),json.load(open(sys.argv[1]))); print(chr(10).join(str(x) for x in v or []))' "$1" "$2"
}

parse_common_args() {
  PROJECT=""; CONFIG=""
  while (($#)); do
    case "$1" in
      --project) [[ $# -gt 1 ]] || usage; PROJECT="$2"; shift 2;;
      --config) [[ $# -gt 1 ]] || usage; CONFIG="$2"; shift 2;;
      -h|--help) usage;;
      *) die "unknown argument: $1";;
    esac
  done
  [[ -n "$PROJECT" ]] || usage
  PROJECT="$(cd "$PROJECT" 2>/dev/null && pwd -P)" || die "project does not exist"
  if [[ -z "$CONFIG" ]]; then
    [[ -f "$PROJECT/automation/codex-work-loop.json" ]] && CONFIG="$PROJECT/automation/codex-work-loop.json"
    [[ -n "$CONFIG" || ! -f "$PROJECT/tools/codex-work-loop/config/project.json" ]] || CONFIG="$PROJECT/tools/codex-work-loop/config/project.json"
  fi
  [[ -f "$CONFIG" ]] || die "configuration not found"
  CONFIG="$(cd "$(dirname "$CONFIG")" && pwd -P)/$(basename "$CONFIG")"
}

load_config() {
  PROJECT_ID="$(config_get "$CONFIG" projectId)"; CONFIG_ROOT="$(config_get "$CONFIG" projectRoot)"
  [[ "$CONFIG_ROOT" == "$PROJECT" ]] || die "projectRoot does not match project"
  REPOSITORY="$(config_get "$CONFIG" repository)"; ROADMAP_PATH="$(config_get "$CONFIG" roadmapPath)"
  STATUS_PATH="$(config_get "$CONFIG" statusPath)"; REPORT_PATH="$(config_get "$CONFIG" reportPath)"
  BUILD_COMMAND="$(config_get "$CONFIG" buildCommand)"; TEST_COMMAND="$(config_get "$CONFIG" testCommand)"
  DIFF_COMMAND="$(config_get "$CONFIG" diffCommand)"; MAX_ITERATIONS="$(config_get "$CONFIG" maxIterations)"
  MAX_FILES="$(config_get "$CONFIG" maxFilesPerIteration)"; MAX_COMMITS="$(config_get "$CONFIG" maxCommitsPerIteration)"
  ALLOW_AWS="$(config_get "$CONFIG" allowAws)"; ALLOW_EXTERNAL="$(config_get "$CONFIG" allowExternalInfrastructure)"
  REQUIRE_SEMANTIC="$(config_get "$CONFIG" requireSemanticCommits)"; MILESTONE_HINT="$(config_get "$CONFIG" milestoneHint)"
  MAX_MILESTONES="$(config_get "$CONFIG" maxMilestones)"; STOP_ON_TEST_FAILURE="$(config_get "$CONFIG" stopOnTestFailure)"
  STOP_ON_ARCH="$(config_get "$CONFIG" stopOnArchitectureDecision)"; STOP_ON_AWS="$(config_get "$CONFIG" stopOnAwsRequest)"
  AUTO_PUSH="$(config_get "$CONFIG" autoPush)"; AUTO_PR="$(config_get "$CONFIG" autoCreatePullRequest)"
  PR_BASE="$(config_get "$CONFIG" prBaseBranch)"
  [[ -n "$MAX_MILESTONES" ]] || MAX_MILESTONES="$MAX_ITERATIONS"
  [[ -n "$STOP_ON_TEST_FAILURE" ]] || STOP_ON_TEST_FAILURE=true
  [[ -n "$STOP_ON_ARCH" ]] || STOP_ON_ARCH=true
  [[ -n "$STOP_ON_AWS" ]] || STOP_ON_AWS=true
  [[ -n "$AUTO_PUSH" ]] || AUTO_PUSH=false
  [[ -n "$AUTO_PR" ]] || AUTO_PR=false
  [[ -n "$PR_BASE" ]] || PR_BASE=main
  [[ "$ALLOW_AWS" == false && "$ALLOW_EXTERNAL" == false && "$REQUIRE_SEMANTIC" == true ]] || die "unsafe policy in configuration"
  [[ "$MAX_ITERATIONS" =~ ^[1-9][0-9]*$ && "$MAX_MILESTONES" =~ ^[1-9][0-9]*$ && "$MAX_FILES" =~ ^[1-9][0-9]*$ && "$MAX_COMMITS" =~ ^[1-9][0-9]*$ ]] || die "limits must be positive"
  [[ "$AUTO_PUSH" == false && "$AUTO_PR" == false ]] || [[ "$AUTO_PUSH" == true && "$AUTO_PR" == true ]] || die "auto push and PR must be enabled together"
  printf '%s\n' "$BUILD_COMMAND $TEST_COMMAND $DIFF_COMMAND" | rg -i '(^|[[:space:];|&])(aws|terraform|mcp|ssh|scp|curl|git[[:space:]]+push[[:space:]]+--force|force-push|git[[:space:]]+reset[[:space:]]+--hard|git[[:space:]]+clean)([[:space:]]|$)' >/dev/null && die "external or destructive command in configuration" || true
}

validate_repository() {
  [[ "$(git -C "$PROJECT" rev-parse --show-toplevel 2>/dev/null)" == "$PROJECT" ]] || die "unexpected repository root"
  [[ "$(basename "$PROJECT")" != *OnlineOS* ]] || die "OnlineOS is blocked"
  BRANCH="$(git -C "$PROJECT" symbolic-ref --quiet --short HEAD 2>/dev/null || true)"; [[ -n "$BRANCH" ]] || die "detached HEAD"
  local p prefix allowed=false
  while IFS= read -r p; do [[ -n "$p" && "$BRANCH" != "$p" ]] || die "protected branch: $BRANCH"; done < <(config_array "$CONFIG" protectedBranches)
  while IFS= read -r prefix; do [[ -n "$prefix" && "$BRANCH" == "$prefix"* ]] && allowed=true; done < <(config_array "$CONFIG" allowedBranchPrefixes)
  [[ "$allowed" == true ]] || die "branch is not allowed: $BRANCH"; COMMIT="$(git -C "$PROJECT" rev-parse HEAD)"
}
working_tree_state() { [[ -z "$(git -C "$PROJECT" status --porcelain --untracked-files=all)" ]] && WORKTREE=clean || WORKTREE=dirty; }
resolve_path() { [[ "$1" = /* ]] && printf '%s\n' "$1" || printf '%s/%s\n' "$PROJECT" "$1"; }
roadmap_file() { resolve_path "$ROADMAP_PATH"; }
status_file() { resolve_path "$STATUS_PATH"; }
report_file() {
  local path="$(resolve_path "$REPORT_PATH")"
  if [[ "$path" == *.md ]]; then
    printf '%s/%s-%s.md\n' "$(dirname "$path")" "$(basename "$path" .md)" "$(date -u '+%Y%m%dT%H%M%SZ')"
  else
    printf '%s-%s.md\n' "$path" "$(date -u '+%Y%m%dT%H%M%SZ')"
  fi
}
generated_prompt_file() { printf '%s/docs/operations/generated/NEXT-MILESTONE-PROMPT.md\n' "$PROJECT"; }
loop_report_file() { printf '%s/docs/status/LOOP-%s.md\n' "$PROJECT" "$(date -u '+%Y%m%dT%H%M%SZ')"; }
pr_registry_file() { printf '%s/docs/status/PULL-REQUESTS.md\n' "$PROJECT"; }
remote_pr_url() {
  local remote="$1" branch="$2" normalized
  normalized="${remote%.git}"
  case "$normalized" in
    git@github.com:*) printf 'https://github.com/%s/pull/new/%s\n' "${normalized#git@github.com:}" "$branch";;
    https://github.com/*) printf '%s/pull/new/%s\n' "$normalized" "$branch";;
    *) printf 'manual-pr-required:%s\n' "$branch";;
  esac
}
sanitize_report_text() { sed -E 's/(token|secret|password|api.?key|authorization)[^[:space:]]*/[redacted]/Ig'; }

milestone_summary() {
  local f="$(roadmap_file)"
  if [[ -f "$f" && "$f" == *.json ]]; then
    python3 -c 'import json,re,sys; ms=json.load(open(sys.argv[1])).get("milestones",[]); h=sys.argv[2] or (re.search(r"feature/m([0-9]+)",sys.argv[3],re.I).group(1) if re.search(r"feature/m([0-9]+)",sys.argv[3],re.I) else ""); h=("M"+h) if h and not h.upper().startswith("M") else h; a=next((m for m in ms if m.get("id")==h),None) if h else None; a=a or ({"id":h,"title":"Current feature milestone","status":"in_progress"} if h else None) or next((m for m in ms if m.get("status") not in ("completed","approved")),None); i=ms.index(a) if a in ms else -1; n=ms[i+1] if i>=0 and i+1<len(ms) else None; print("current=%s|%s|%s"%(a.get("id","unknown") if a else "unknown",a.get("title","") if a else "",a.get("status","unknown") if a else "unknown")); print("next=%s|%s|%s"%(n.get("id","unknown") if n else "human-review",n.get("title","Determine next milestone") if n else "Determine next milestone",n.get("status","unknown") if n else "unknown"))' "$f" "$MILESTONE_HINT" "$BRANCH"
  elif [[ -f "$f" ]]; then
    local current="${MILESTONE_HINT:-$(rg '^##+[[:space:]]+M[0-9]+' "$f" | tail -1 | sed -E 's/^#+[[:space:]]+([^—-]+).*/\1/' | xargs || true)}"
    current="${MILESTONE_HINT:-$(printf '%s' "$BRANCH" | sed -nE 's#.*feature/m([0-9]+).*#M\1#p')}"; printf 'current=%s|Markdown roadmap|unknown\nnext=human-review|Determine from roadmap|unknown\n' "${current:-unknown}"
  else printf 'current=%s|Configured hint|unknown\nnext=human-review|Roadmap missing|blocked\n' "${MILESTONE_HINT:-unknown}"; fi
}
assert_clean_for_execution() { working_tree_state; [[ "$WORKTREE" == clean ]] || die "working tree is dirty"; }
assert_execution_environment() { [[ -z "${AWS_PROFILE:-}" && -z "${AWS_DEFAULT_PROFILE:-}" ]] || die "AWS profile environment is present; execution blocked"; }
assert_no_blocking_markers() { local f="$(status_file)"; [[ ! -f "$f" ]] || ! rg -n 'ENGINE_CONTRACT_GAP[=:][[:space:]]*true|HUMAN_DECISION_REQUIRED[=:][[:space:]]*true|HUMAN_REQUIRED[=:][[:space:]]*true' "$f" >/dev/null || die "blocking status marker"; }
changed_file_count() { { git -C "$PROJECT" diff --name-only "$1" HEAD; git -C "$PROJECT" ls-files --others --exclude-standard; } | sort -u | wc -l | tr -d ' '; }
conventional_commit() { [[ "$1" =~ ^(feat|fix|docs|test|chore|refactor|perf|build|ci)(\([[:alnum:]_.-]+\))?:[[:space:]].+$ ]]; }
