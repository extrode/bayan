#!/usr/bin/env bash
# Dry run of 015 on a fixture repo: a branch at dev's tip and an unmerged branch are kept, a merged branch whose
# worktree path holds a space is listed for removal, and nothing is removed.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/cleanup-015-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
git init -q -b dev && git config user.email t@t && git config user.name t
git commit -q --allow-empty -m base
git branch merged && git switch -q merged && git commit -q --allow-empty -m m && git switch -q dev && git merge -q --no-ff merged -m mm
git branch fresh
git branch open && git switch -q open && git commit -q --allow-empty -m o && git switch -q dev
mkdir -p .worktrees tmp/wt
git worktree add -q ".worktrees/with space" merged
git worktree add -q .worktrees/fresh fresh
git worktree add -q tmp/wt/open open
out="$(bash "$root/scripts/cleanup/015-spec-010-merge-and-013.sh")"
left="$(git worktree list | wc -l | tr -d ' ')"
cd "$root"; rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
echo "$out" | grep -qx 'would: git worktree remove .worktrees/with space' || fail "merged worktree with a space not listed"
echo "$out" | grep -qx 'would: git branch -d merged' || fail "merged branch not listed"
echo "$out" | grep -q '^keep:  .worktrees/fresh (fresh is at dev, no commits of its own)$' || fail "branch at dev's tip not kept"
echo "$out" | grep -q '^keep:  tmp/wt/open (open not in dev)$' || fail "unmerged branch not kept"
echo "$out" | grep -q 'branch -d fresh' && fail "branch at dev's tip listed for deletion"
[ "$left" = 4 ] || fail "dry run changed the worktrees ($left left)"
echo PASS
