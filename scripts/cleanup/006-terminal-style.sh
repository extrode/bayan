#!/usr/bin/env bash
# Cleanup for spec 005, terminal style (2026-10-21): the feature worktree, the merged feature and worker branches,
# and (second flag) /tmp/gate-t257, scratch a gate checker left outside the repo.
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
for wt in .worktrees/feat-005-terminal-style; do
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
for b in feat/005-terminal-style \
         chore/worker-005-t1 chore/worker-005-t257 chore/worker-005-t1011 chore/worker-005-t3 \
         chore/worker-005-t4 chore/worker-005-t13 chore/worker-005-t15; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Scratch (untracked, needs --delete-scratch as well): a gate checker's logs outside the repo.
for abs in /tmp/gate-t257; do
  if [ ! -e "$abs" ]; then printf 'skip:  %s already gone\n' "$abs"; continue; fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$abs"; printf 'removed: %s\n' "$abs"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$abs"; fi
done

printf '\nremaining spec 005 worktrees and branches:\n'
git worktree list | grep -E '005' || true
git branch --list 'feat/005-terminal-style' 'chore/worker-005-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
