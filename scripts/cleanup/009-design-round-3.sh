#!/usr/bin/env bash
# Cleanup for design round 3 (2026-10-25): the round-3 feature worktree and branch, every chore/worker-r3-*
# and chore/worker-review-fix-* worker worktree and branch, the review fix branch, and (second flag) the
# untracked round-3 scratch under tmp/.
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
workers="$(git branch --list 'chore/worker-r3-*' 'chore/worker-review-fix-*' --format='%(refname:short)')"

# 1. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
wts=".worktrees/feat-design-round-3"
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
for b in feat/design-round-3 fix/review-2026-10-24-1 $workers; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 3. Round-3 scratch under tmp/ (needs --delete-scratch as well; refused if git tracks it).
# tmp/rr3-consequence holds a symlink to /: rm -rf removes the link, not its target.
shopt -s nullglob
for rel in tmp/rv2 tmp/rv3 tmp/rr2-* tmp/rr3-* tmp/refute* tmp/rev9 tmp/psc.* tmp/bt tmp/ignored-* \
           tmp/r3-* tmp/review-dev* tmp/0010-becomes-needs-ref-this.md; do
  if [ ! -e "$rel" ] && [ ! -L "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining design round 3 worktrees and branches:\n'
git worktree list | grep -E 'design-round-3|worker-r3-|worker-review-fix-' || true
git branch --list 'feat/design-round-3' 'fix/review-2026-10-24-1' 'chore/worker-r3-*' 'chore/worker-review-fix-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
