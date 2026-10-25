#!/usr/bin/env bash
# Cleanup for revision 0011 and the round-3 review fixes (2026-10-25): the revision 0011 feature worktree and
# branch, the fix/review-2026-10-25-* worktrees and branches, the chore/worker-rv11*, chore/worker-rr4*
# chore/worker-rr5*, -rr6* and -rr7* worker worktrees and branches, and (second flag) their untracked scratch under tmp/.
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
workers="$(git branch --list 'chore/worker-rv11*' 'chore/worker-rr4*' 'chore/worker-rr5*' 'chore/worker-rr6*' 'chore/worker-rr7*' --format='%(refname:short)')"

# 1. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
fixes="$(git branch --list 'fix/review-2026-10-25-*' --format='%(refname:short)')"
wts=".worktrees/feat-revision-0011"
for b in $fixes; do wts="$wts .worktrees/${b//\//-}"; done
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

# 2. Feature, review-fix and worker branches (git branch -d refuses anything unmerged).
for b in feat/revision-0011-regex-validation-error $fixes $workers; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Revision 0011 and review scratch under tmp/ (needs --delete-scratch as well; refused if git tracks it).
shopt -s nullglob
for rel in tmp/rv11* tmp/rr4* tmp/rr5* tmp/review-r3-* tmp/review-rv11* tmp/ignored-rv11 tmp/ignored-rr4* \
           tmp/ignored-rr5* tmp/ignored-rr6* tmp/rr6* tmp/rr7* tmp/review-fix* tmp/fix-final-test.log tmp/fix2-final-test.log \
           tmp/apply-0011* tmp/resolve-rr4.py; do
  if [ ! -e "$rel" ] && [ ! -L "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining revision 0011 and review worktrees and branches:\n'
git worktree list | grep -E 'revision-0011|review-2026-10-25-|worker-rv11|worker-rr4|worker-rr5|worker-rr6|worker-rr7' || true
git branch --list 'feat/revision-0011-regex-validation-error' 'fix/review-2026-10-25-*' 'chore/worker-rv11*' 'chore/worker-rr4*' 'chore/worker-rr5*' 'chore/worker-rr6*' 'chore/worker-rr7*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
