#!/usr/bin/env bash
# Cleanup for spec 001 (self-hosted lexer), written 2026-10-20 by the /implement run.
# Removes the merged worker branches and feature branch, and (second flag) the review scratch under tmp/.
# Dry run by default. --execute / -e acts on branches; --delete-scratch also removes untracked scratch dirs.
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

# 1. Worker branches and the feature branch (git branch -d refuses anything unmerged).
for b in feat/001-self-hosted-lexer chore/worker-m13 chore/worker-m14 chore/worker-m15 chore/worker-m16 \
         chore/worker-m17 chore/worker-m18 chore/worker-m19 chore/worker-m20 chore/worker-m21 \
         chore/worker-m22u chore/worker-m22l chore/worker-m23 chore/worker-rv1 chore/worker-rv2; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 2. Leftover worktree registrations.
if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi

# 3. Review scratch (untracked, needs --delete-scratch as well).
for rel in tmp/review-scratch tmp/cold-cache tmp/corpus; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining worker branches:\n'; git branch --list 'chore/worker-*' 'feat/001-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
