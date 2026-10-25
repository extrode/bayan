#!/usr/bin/env bash
# Cleanup for spec 007, lambdas and function types (2026-10-25): the feat/007-lambdas worktree and branch, the
# chore/worker-007-* worker worktrees and branches, and (second flag) their untracked scratch under tmp/.
# Dry run by default. --execute / -e acts on worktrees and branches; --delete-scratch also removes untracked scratch.
# Not handled: /tmp/t6probe.RXhH/, a probe a 007 T6 writer left outside the repo; remove it by hand if it is still there.
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

# Worker keys are read from git at run time: the build added keys as it went.
workers="$(git branch --list 'chore/worker-007-*' --format='%(refname:short)')"

# 1. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
wts=".worktrees/feat-007-lambdas"
for b in $workers; do wts="$wts .worktrees/worker-${b#chore/worker-}"; done
for wt in $wts; do
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
for b in feat/007-lambdas $workers; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Spec 007 scratch under tmp/ (needs --delete-scratch as well; refused if git tracks it).
shopt -s nullglob
for rel in tmp/007-* tmp/ignored-007-* tmp/review-007; do
  if [ ! -e "$rel" ] && [ ! -L "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining spec 007 worktrees and branches:\n'
git worktree list | grep -E 'feat-007-lambdas|worker-007-' || true
git branch --list 'feat/007-lambdas' 'chore/worker-007-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
