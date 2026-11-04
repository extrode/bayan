#!/usr/bin/env bash
# 022 on a fixture repo under tmp/: gates refuse off dev and when the feature branch is not in dev; a dry run changes
# nothing; -e deletes merged branches and keeps an unmerged one; a worktree with ignored build output needs the second
# flag; a tracked path under tmp/011 is refused; a second -e run succeeds with skips.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
script="$root/scripts/cleanup/022-self-hosted-emitter.sh"
r="$root/tmp/cleanup-022-test"
rm -rf "$r" && mkdir -p "$r/ext" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
export CLEANUP_TMP_ROOT="$r/ext"
old="2020-01-01T00:00:00"
g() { GIT_COMMITTER_DATE="$old" GIT_AUTHOR_DATE="$old" git -c user.email=t@t -c user.name=t "$@"; }
git init -q -b dev w && cd w && git config core.hooksPath /dev/null
feat="feat/011-self-hosted-emitter"
printf 'bin/\nobj/\n' > .gitignore
mkdir -p tmp/011 && echo x > tmp/011/tracked.txt
g add -A && g commit -q -m base && g branch main
for b in "$feat" chore/worker-011-a docs/review-small-rows fix/011-guard-unreported-file chore/worker-011-ign chore/011-merge-dev dev-keep; do g branch "$b"; done
g switch -q fix/011-guard-unreported-file && g commit -q --allow-empty -m u && g switch -q dev
g worktree add -q .worktrees/ign chore/worker-011-ign
g worktree add -q .worktrees/dev-merge chore/011-merge-dev
mkdir .worktrees/ign/bin && echo o > .worktrees/ign/bin/out.o
mkdir -p tmp/011-x tmp/guard-run "$r/ext/q845gate" && echo s > tmp/011-x/f
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }
has() { git rev-parse -q --verify "refs/heads/$1" >/dev/null; }
state() { git for-each-ref refs/heads; git worktree list; find tmp .worktrees "$r/ext" -type f -o -type d | sort; }

git switch -q main
out="$(bash "$script")" && fail "ran off dev"
echo "$out" | grep -q "^refuse: check out dev first" || fail "off-dev gate message"
git switch -q dev
g switch -q "$feat" && g commit -q --allow-empty -m f && g switch -q dev
out="$(bash "$script")" && fail "ran with feat not in dev"
echo "$out" | grep -q "^refuse: $feat is not in dev" || fail "feat gate message"
git branch -q -f "$feat" dev

before="$(state)"
out="$(bash "$script")" || fail "dry run exited non-zero"
echo "$out" | grep -q "^would: git branch -d chore/worker-011-a$" || fail "merged branch not listed"
echo "$out" | grep -q "^keep:  .worktrees/ign (holds ignored build output" || fail "ignored-output worktree not left"
[ "$before" = "$(state)" ] || fail "dry run changed something"

out="$(bash "$script" -e)" && fail "-e did not fail on tracked tmp/011"
echo "$out" | grep -q "^refuse: tmp/011 is tracked by git" || fail "tracked tmp/011 not refused"
[ -f tmp/011/tracked.txt ] || fail "tracked file deleted"
has chore/worker-011-a && fail "merged branch not deleted"
has docs/review-small-rows && fail "second merged branch not deleted"
has fix/011-guard-unreported-file || fail "unmerged branch deleted"
echo "$out" | grep -q "^keep:  fix/011-guard-unreported-file (not in dev)$" || fail "unmerged branch not listed"
for b in dev main "$feat" dev-keep; do has "$b" || fail "$b deleted"; done
[ -d .worktrees/ign ] || fail "ignored-output worktree removed without the second flag"
[ ! -d .worktrees/dev-merge ] || fail "clean worktree not removed"
has chore/011-merge-dev && fail "branch of removed worktree not deleted"
[ ! -e tmp/011-x ] && [ ! -e tmp/guard-run ] && [ ! -e "$r/ext/q845gate" ] || fail "scratch not removed"

git rm -rq tmp/011 && g commit -q -m untrack
out="$(bash "$script" -e --remove-ignored-build-output)" || fail "run with second flag failed"
[ ! -d .worktrees/ign ] || fail "ignored-output worktree not removed with the second flag"
has chore/worker-011-ign && fail "its branch not deleted"
has fix/011-guard-unreported-file || fail "unmerged branch deleted"

out="$(bash "$script" -e --remove-ignored-build-output)" || fail "second -e run failed"
echo "$out" | grep -q "^skip:" || fail "no skips on the second run"
cd "$root"; echo PASS
