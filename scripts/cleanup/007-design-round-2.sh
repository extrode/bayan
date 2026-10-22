#!/usr/bin/env bash
# Cleanup for design round 2 (2026-10-22): the round-2 feature, docs and laws-timeout worktrees and branches,
# every chore/worker-r2-* worker worktree and branch, and (second flag) scratch gate/test runs left in /tmp.
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

# Worker keys are read from git at run time: the round added keys as it went.
workers="$(git branch --list 'chore/worker-r2-*' --format='%(refname:short)')"

# 1. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
wts=".worktrees/feat-design-round-2 .worktrees/docs-design-round-2 .worktrees/chore-laws-timeout"
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

# 2. Feature, docs and worker branches (git branch -d refuses anything unmerged).
for b in feat/design-round-2 docs/design-round-2 chore/laws-timeout $workers; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Scratch outside the repo (needs --delete-scratch as well): suite and laws logs, a blame dump, a backup copy.
for abs in /tmp/r2u-blame /tmp/r2u-full.txt /tmp/r2u-full2.txt /tmp/r2u-full3.txt /tmp/r2u-l001.txt /tmp/r2u-l002.txt \
           /tmp/r2u-laws12.txt /tmp/r2u-laws12b.txt /tmp/r2u-lawscheck.txt /tmp/r2u-lawscheck2.txt /tmp/r2u-pre.txt \
           /tmp/r2u-tests.txt /tmp/w2bk; do
  if [ ! -e "$abs" ]; then printf 'skip:  %s already gone\n' "$abs"; continue; fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$abs"; printf 'removed: %s\n' "$abs"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$abs"; fi
done

# 4. Test-host hang dumps inside the repo (needs --delete-scratch as well; refused if git tracks them).
for rel in tests/Bayan.Tests/TestResults; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining design round 2 worktrees and branches:\n'
git worktree list | grep -E 'design-round-2|worker-r2-|laws-timeout' || true
git branch --list 'feat/design-round-2' 'docs/design-round-2' 'chore/laws-timeout' 'chore/worker-r2-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
