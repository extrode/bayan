#!/usr/bin/env bash
# 017 on a fixture repo: the dry run lists a merged p015 worktree and branch, keeps an unmerged one, ignores a worktree
# outside the patterns, puts scratch behind --delete-scratch and changes nothing; --execute removes what dev holds and
# keeps the rest; a tracked tmp/ path is refused.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
script="$root/scripts/cleanup/017-spec-015-prep.sh"
r="$root/tmp/cleanup-017-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
git init -q -b dev && git config core.hooksPath /dev/null
git -c user.email=t@t -c user.name=t commit -q --allow-empty -m base
ci() { git -c user.email=t@t -c user.name=t commit -q --allow-empty -m "$1"; }
git switch -q -c chore/worker-p015-guard && ci g && git switch -q dev && git -c user.email=t@t -c user.name=t merge -q --no-ff chore/worker-p015-guard -m mg
git switch -q -c chore/worker-p015-quality && ci q && git switch -q dev
git branch docs/015-decisions && git branch other
mkdir -p .worktrees tmp/parity && echo x > tmp/s8 && git add -f tmp/s8 && ci tracked
git worktree add -q .worktrees/worker-p015-guard chore/worker-p015-guard
git worktree add -q .worktrees/worker-p015-quality chore/worker-p015-quality
git worktree add -q .worktrees/other other
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }

out="$(bash "$script")"
echo "$out" | grep -q "^would: git worktree remove .*/.worktrees/worker-p015-guard$" || fail "merged worker worktree not listed"
echo "$out" | grep -q "^would: git branch -d chore/worker-p015-guard$" || fail "merged worker branch not listed"
echo "$out" | grep -q "^keep:  .*/.worktrees/worker-p015-quality (chore/worker-p015-quality not in dev)$" || fail "unmerged worktree not kept"
echo "$out" | sed '/^remaining worktrees:/,$d' | grep -q "worktrees/other" && fail "a worktree outside the patterns was considered"
echo "$out" | grep -q "^would: git branch -d docs/015-decisions$" || fail "worktree-less branch not listed"
echo "$out" | grep -q "^skip:  branch fix/scaffold-checkout-v5 no longer exists$" || fail "absent branch not skipped"
echo "$out" | grep -q "^would: rm -rf tmp/parity (needs --execute --delete-scratch)$" || fail "scratch not behind its flag"
echo "$out" | grep -q "^refuse: tmp/s8 is tracked by git" || fail "tracked tmp path not refused"
[ "$(git worktree list | wc -l | tr -d ' ')" = 4 ] && [ -d tmp/parity ] && git rev-parse -q --verify docs/015-decisions >/dev/null \
  || fail "dry run changed something"

out="$(bash "$script" --execute --delete-scratch || true)"
[ ! -d .worktrees/worker-p015-guard ] && [ -d .worktrees/worker-p015-quality ] && [ -d .worktrees/other ] || fail "execute removed the wrong worktrees"
! git rev-parse -q --verify chore/worker-p015-guard >/dev/null && ! git rev-parse -q --verify docs/015-decisions >/dev/null || fail "merged branches left"
git rev-parse -q --verify chore/worker-p015-quality >/dev/null || fail "unmerged branch deleted"
[ ! -e tmp/parity ] && [ -f tmp/s8 ] || fail "scratch handling wrong"

git switch -q -c side; out="$(bash "$script" || true)"
echo "$out" | grep -q "^refuse: check out dev first (on side)" || fail "ran off dev"
cd "$root"; rm -rf "$r"
echo PASS
