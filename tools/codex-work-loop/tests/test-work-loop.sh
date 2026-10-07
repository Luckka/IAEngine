#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
PROJECT="$TMP/project"
mkdir -p "$PROJECT" "$TMP/bin"
PROJECT="$(cd "$PROJECT" && pwd -P)"
git -C "$PROJECT" init -q -b feature/test
git -C "$PROJECT" config user.email test@example.invalid
git -C "$PROJECT" config user.name Test
printf '## M23 — Test\n' > "$PROJECT/ROADMAP.md"
printf 'docs/\n' > "$PROJECT/.gitignore"
git -C "$PROJECT" add .
git -C "$PROJECT" commit -qm 'chore: initialize fake project'
cat > "$PROJECT/config.json" <<EOF
{"projectId":"fake","projectRoot":"$PROJECT","repository":"fake","roadmapPath":"ROADMAP.md","statusPath":"STATUS.md","reportPath":"docs/status/report","protectedBranches":["main"],"allowedBranchPrefixes":["feature/"],"buildCommand":"true","testCommand":"true","diffCommand":"git diff --check","suggestedCommit":"feat: fake work","maxIterations":2,"maxMilestones":2,"maxFilesPerIteration":4,"maxCommitsPerIteration":1,"stopOnTestFailure":true,"stopOnArchitectureDecision":true,"stopOnAwsRequest":true,"autoPush":false,"autoCreatePullRequest":false,"prBaseBranch":"main","allowAws":false,"allowExternalInfrastructure":false,"requireSemanticCommits":true,"milestoneHint":"M23"}
EOF
git -C "$PROJECT" add config.json
git -C "$PROJECT" commit -qm 'chore: add test configuration'
git init --bare -q "$TMP/remote.git"
git -C "$PROJECT" remote add origin "$TMP/remote.git"
git -C "$PROJECT" push -q -u origin feature/test
assert_contains() { grep -Fq "$2" "$1" || { printf 'missing %s in %s\n' "$2" "$1" >&2; exit 1; }; }
"$ROOT/codex-status.sh" --project "$PROJECT" --config "$PROJECT/config.json" >/dev/null
"$ROOT/codex-next-task.sh" --project "$PROJECT" --config "$PROJECT/config.json" >/dev/null
assert_contains "$(find "$PROJECT/docs/status" -name 'report-*.md' | head -1)" 'AWS accessed: false'
assert_contains "$PROJECT/docs/operations/NEXT-CODEX-TASK.md" 'Current milestone: M23'
before="$(git -C "$PROJECT" rev-parse HEAD)"
"$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --dry-run >/dev/null
[[ "$before" == "$(git -C "$PROJECT" rev-parse HEAD)" ]]
printf '#!/usr/bin/env bash\ncd "%s"\nprintf generated > work.txt\ngit add work.txt\ngit commit -qm "feat: fake work"\n' "$PROJECT" > "$TMP/bin/codex"
chmod +x "$TMP/bin/codex"
env -u AWS_PROFILE -u AWS_DEFAULT_PROFILE PATH="$TMP/bin:$PATH" "$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --once >/dev/null
[[ "$(git -C "$PROJECT" log -2 --format='%s' | head -1)" == 'feat: fake work' ]]
env -u AWS_PROFILE -u AWS_DEFAULT_PROFILE PATH="$TMP/bin:$PATH" "$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --iterations 2 >/dev/null
[[ "$(git -C "$PROJECT" rev-list --count HEAD~2..HEAD)" -eq 2 ]]
env -u AWS_PROFILE -u AWS_DEFAULT_PROFILE PATH="$TMP/bin:$PATH" "$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --once --auto-push --auto-pr >/dev/null
git ls-remote --exit-code "$TMP/remote.git" refs/heads/feature/test >/dev/null
[[ -f "$PROJECT/docs/status/PULL-REQUESTS.md" ]]
set +e
env -u AWS_PROFILE -u AWS_DEFAULT_PROFILE PATH="$TMP/bin:$PATH" "$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --iterations 3 >/dev/null 2>&1
[[ "$?" -ne 0 ]]
AWS_PROFILE=personal-infrasentinel "$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --once >/dev/null 2>&1
[[ $? -ne 0 ]]
git -C "$PROJECT" switch -q -c main
"$ROOT/codex-work-loop.sh" --project "$PROJECT" --config "$PROJECT/config.json" --dry-run >/dev/null 2>&1
[[ $? -ne 0 ]]
set -e
printf 'work-loop tests passed\n'
