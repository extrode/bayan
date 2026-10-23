#!/usr/bin/env bash
# Cleanup for spec 006 interfaces and iterators (2026-10-23): the feature worktree and branch, every
# chore/worker-006-* worker worktree and branch, and (second flag) the writers' ignored files moved
# into the feature worktree's tmp/ before each land, /tmp/fixh-bak (a writer's backups), plus any test-host hang dump.
# tmp/009-xcheck-ignored (the 009 spec-doc writer, landed on feat/009) also sits there; its
# worktree and branch chore/worker-009-xcheck belong to the 009 cleanup, after 009 merges.
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
feat=.worktrees/feat-006-interfaces-iterators

# Gate: run from dev, so no branch below is the checked-out one.
if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi

# Worker keys are read from git at run time: the spec added keys as it went.
workers="$(git branch --list 'chore/worker-006-*' --format='%(refname:short)')"

# 1. Writers' ignored files, moved into the feature worktree's tmp/ before each land (needs --delete-scratch).
#    Runs before step 2 so the feature worktree is still there to hold them.
for abs in "$repo/$feat"/tmp/*-ignored*; do
  [ -e "$abs" ] || { printf 'skip:  no %s/tmp/*-ignored* left\n' "$feat"; break; }
  rel="${abs#"$repo/$feat"/}"
  if git -C "$feat" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$abs"; printf 'removed: %s\n' "$abs"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$abs"; fi
done

# 2. Test-host hang dumps inside the repo (needs --delete-scratch as well; refused if git tracks them).
for rel in tests/Bayan.Tests/TestResults; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

# 2b. Scratch a writer left outside the repo (006-fixh break-test backups; needs --delete-scratch).
for abs in /tmp/fixh-bak; do
  if [ ! -e "$abs" ]; then printf 'skip:  %s already gone\n' "$abs"; continue; fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$abs"; printf 'removed: %s\n' "$abs"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$abs"; fi
done

# 3. Worktrees (git worktree remove refuses a dirty tree; ignored tmp/ scratch goes with it).
wts="$feat"
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

# 4. Feature and worker branches (git branch -d refuses anything not merged into dev).
for b in feat/006-interfaces-iterators $workers; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s not merged. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

printf '\nremaining spec 006 worktrees and branches:\n'
git worktree list | grep -E 'feat-006-interfaces-iterators|worker-006-' || true
git branch --list 'feat/006-interfaces-iterators' 'chore/worker-006-*'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
