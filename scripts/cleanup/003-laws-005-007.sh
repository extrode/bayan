#!/usr/bin/env bash
# Cleanup for laws L005–L007 (enacted 2026-10-21): the merged feature, docs and worker branches,
# and (second flag) the proof, gate and review scratch under tmp/.
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

# 1. Feature, docs and worker branches (git branch -d refuses anything unmerged).
for b in feat/laws-005-007 docs/laws-005-008 \
         chore/worker-l005-proof chore/worker-l006-proof chore/worker-l007-sink chore/cleanup-laws-005-007; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

# 2. Proof, gate and review scratch (untracked, needs --delete-scratch as well).
for rel in tmp/l005-proof-criteria.md tmp/l005-proof-criteria-g.md tmp/l006-proof-criteria.md tmp/l006-proof-criteria-g.md \
           tmp/l007-sink-criteria.md tmp/l007-sink-criteria-g.md tmp/l005-gate.log tmp/l006-gate.log tmp/l007-gate.log \
           tmp/l007-sink.log tmp/review-laws-005-007.diff tmp/review-laws-005-007.log tmp/review-l007-probe \
           tmp/laws-branch-check.log tmp/laws-final-check.log tmp/l005-run-batch-unsupported; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining laws-005-007 branches:\n'
git branch --list 'feat/laws-005-007' 'docs/laws-005-008' 'chore/worker-l00[5-7]-*' 'chore/cleanup-laws-005-007'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
