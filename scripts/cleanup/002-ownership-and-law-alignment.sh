#!/usr/bin/env bash
# Cleanup for spec 002 (ownership and law alignment), written 2026-10-20 by the /implement run.
# Removes the worktrees and merged branches from spec 002, addendum 002 §1 and the Q57 docs work,
# and (second flag) the review scratch under tmp/.
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

# 1. Worktrees (git worktree remove refuses a dirty tree; left alone if so).
for wt in .worktrees/feat-reserve-law-keywords .worktrees/docs-q57-terminal-style; do
  if ! git worktree list --porcelain | grep -qx "worktree $repo/$wt"; then
    printf 'skip:  worktree %s not registered\n' "$wt"
  elif [ $execute -eq 1 ]; then
    if git worktree remove "$wt"; then printf 'removed: worktree %s\n' "$wt"
    else printf '  refused: %s is dirty. Left alone.\n' "$wt"; failed=1; fi
  else
    printf 'would: git worktree remove %s\n' "$wt"
  fi
done
if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi

# 2. Feature, docs and worker branches (git branch -d refuses anything unmerged).
for b in feat/002-ownership-and-law-alignment feat/reserve-law-keywords \
         docs/q57-terminal-style docs/q57-milestone-name \
         chore/worker-t1-map-api chore/worker-t2-l003 chore/worker-t3-l004 chore/worker-t4-unicode \
         chore/worker-t5-json-stdin chore/worker-t6-l001-l002 chore/worker-t7-copy chore/worker-t8-ownership \
         chore/worker-t9-untrusted chore/worker-t10-run-batch chore/worker-l004-lint \
         chore/worker-rv002-fix chore/worker-rv002-fix2; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Review and task scratch (untracked, needs --delete-scratch as well).
for rel in tmp/refute tmp/rv tmp/rv1 tmp/rv2 tmp/rv3 tmp/rv5 tmp/rv6 \
           tmp/review-002.diff tmp/review-002-fix.diff tmp/review-002-fix2.diff \
           tmp/review-002-criteria.md tmp/review-002-loop2-criteria.md tmp/l004-lint-criteria.md; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining worktrees:\n'; git worktree list
printf '\nremaining spec-002 branches:\n'
git branch --list 'chore/worker-t*' 'chore/worker-l004-lint' 'chore/worker-rv002-*' \
  'feat/002-*' 'feat/reserve-law-keywords' 'docs/q57-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
