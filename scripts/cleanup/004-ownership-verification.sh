#!/usr/bin/env bash
# Cleanup for spec 003, ownership verification (2026-10-21): audit and review worktrees, the merged feature,
# worker and review-fix branches, and (second flag) the spec's scratch under tmp/ plus two files a worker wrote to /tmp.
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

# 1. Worktrees (git worktree remove refuses a dirty tree; audit-003-3 holds an untracked AuditTmpTests.cs).
for wt in .worktrees/audit-003-1 .worktrees/audit-003-2 .worktrees/audit-003-3 .worktrees/audit-003-4 \
          .worktrees/audit-003-5 .worktrees/audit-003-dev .worktrees/fix-review-2026-10-21-1; do
  if ! git worktree list --porcelain | grep -qx "worktree $repo/$wt"; then
    printf 'skip:  worktree %s not registered\n' "$wt"
  elif [ $execute -eq 1 ]; then
    if git worktree remove "$wt"; then :; else printf '  refused: %s is dirty. Left alone.\n' "$wt"; failed=1; fi
  else
    printf 'would: git worktree remove %s\n' "$wt"
  fi
done
if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi

# 2. Feature, worker and review-fix branches (git branch -d refuses anything unmerged).
for b in feat/003-ownership-verification fix/review-2026-10-21-1 \
         chore/worker-003-t1 chore/worker-003-t2 chore/worker-003-t3 chore/worker-003-t4 chore/worker-003-t4b \
         chore/worker-003-t5 chore/worker-003-t7 chore/worker-003-t9 \
         chore/worker-003-rv-a chore/worker-003-rv-b chore/worker-003-rv-c chore/worker-003-rv-d; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Scratch (untracked, needs --delete-scratch as well).
shopt -s nullglob
for rel in tmp/003-* tmp/rv tmp/r8 tmp/r8.sh tmp/ndbg tmp/rv-review-correctness tmp/rv-review-consequence \
           tmp/sm tmp/l008 tmp/own-table tmp/cleanup-candidates.md; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done
# Written outside the project by worker 003-t7.
for f in /tmp/l008a.txt /tmp/l008b.txt; do
  if [ ! -e "$f" ]; then printf 'skip:  %s already gone\n' "$f"; continue; fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -f -- "$f"; printf 'removed: %s\n' "$f"
  else printf 'would: rm -f %s (needs --execute --delete-scratch)\n' "$f"; fi
done

printf '\nremaining spec 003 worktrees and branches:\n'
git worktree list | grep -E 'audit-003|fix-review-2026-10-21-1' || true
git branch --list 'feat/003-ownership-verification' 'fix/review-2026-10-21-1' 'chore/worker-003-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
