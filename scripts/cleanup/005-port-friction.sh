#!/usr/bin/env bash
# Cleanup for spec 004, port friction (2026-10-21): the feature worktree, the merged feature and worker branches,
# and (second flag) a leftover worker-004-t2 directory that git no longer tracks as a worktree.
# Dry run by default. --execute / -e acts on worktrees and branches; --delete-scratch also removes untracked scratch.
set -euo pipefail

execute=0; scratch=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-scratch) scratch=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0

# Gate: run from dev, so no branch below is the checked-out one.
if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi

# 1. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
for wt in .worktrees/feat-004-port-friction .worktrees/worker-004-t16; do
  if ! git worktree list --porcelain | grep -qx "worktree $repo/$wt"; then
    printf 'skip:  worktree %s not registered\n' "$wt"
  elif [ $execute -eq 1 ]; then
    if git worktree remove "$wt"; then :; else printf '  refused: %s is dirty. Left alone.\n' "$wt"; failed=1; fi
  else
    printf 'would: git worktree remove %s\n' "$wt"
  fi
done
if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi

# 2. Feature and worker branches (git branch -d refuses anything unmerged).
for b in feat/004-port-friction \
         chore/worker-004-t1 chore/worker-004-t2 chore/worker-004-t4 chore/worker-004-t5 chore/worker-004-t6 \
         chore/worker-004-t8 chore/worker-004-t9 chore/worker-004-t11 chore/worker-004-t13 chore/worker-004-t14 \
         chore/worker-004-t15 chore/worker-004-t16 chore/worker-004-rv chore/worker-004-rv2; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Scratch (untracked, needs --delete-scratch as well): a directory left after worker-004-t2's worktree was retired.
for rel in .worktrees/worker-004-t2; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git worktree list --porcelain | grep -qx "worktree $repo/$rel"; then
    printf 'refuse: %s is a registered worktree. Left alone.\n' "$rel"; failed=1; continue
  fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining spec 004 worktrees and branches:\n'
git worktree list | grep -E '004' || true
git branch --list 'feat/004-port-friction' 'chore/worker-004-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
