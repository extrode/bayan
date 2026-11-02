#!/usr/bin/env bash
# Cleanup after recording the last 8 unmerged worktree branches as merged into dev (superseded, `-s ours`), 2026-11-02.
# 1. (--discard-worktree-changes) the leftover changes that kept two worktrees from removal, each only if it is what it
#    was on 2026-11-02:
#    - .worktrees/feat-010-self-hosted-semantics: six untracked files from the io-copy-file/io-list-dir run cases;
#    - tmp/wt/t28survey: un-skipped T28 parity tests (dev removed those skips since); reverted only when every removed
#      line is a `Skip = "T28 ...` attribute and every added one a bare `[Fact]` or `[Theory]`.
# 2. every worktree whose work dev holds, including ones that moved today, through merged-worktree-backlog.sh with
#    CLEANUP_RECENT_HOURS=1 (the day's merged worktrees go; one cut in the last hour, say by another session, stays);
#    --delete-worktree-scratch passes through.
# Dry run by default. --execute / -e acts.
set -euo pipefail

execute=0; discard=0; pass=()
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1; pass+=(-e) ;;
    --discard-worktree-changes) discard=1 ;;
    --delete-worktree-scratch) pass+=("$a") ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi
act() { [ $execute -eq 1 ] && [ $discard -eq 1 ]; }

printf '== 1. leftover changes\n'
w=.worktrees/feat-010-self-hosted-semantics
on="$( [ -d "$w" ] && git -C "$w" branch --show-current || true)"
for f in a.txt b.txt C.txt copy.bin empty.bin input.bin; do
  if [ ! -e "$w/$f" ]; then printf 'skip:  %s/%s already gone\n' "$w" "$f"; continue; fi
  if [ -z "$(git -C "$w" status --porcelain -- "$f" | grep '^??' || true)" ]; then
    printf 'refuse: %s/%s is not untracked. Left alone.\n' "$w" "$f"; failed=1; continue
  fi
  # As left on 2026-11-01: on that branch, and at most 6 bytes (the run cases write 0-6).
  if [ "$on" != feat/010-self-hosted-semantics ] || [ "$(wc -c < "$w/$f" | tr -d ' ')" -gt 6 ]; then
    printf 'refuse: %s/%s is not the run-case leftover (branch %s, or over 6 bytes). Left alone.\n' "$w" "$f" "${on:-none}"; failed=1; continue
  fi
  if act; then rm -f -- "$w/$f"; printf 'removed: %s/%s\n' "$w" "$f"
  else printf 'would: rm %s/%s (needs --execute --discard-worktree-changes)\n' "$w" "$f"; fi
done
w=tmp/wt/t28survey
if [ ! -d "$w" ] || [ -z "$(git -C "$w" status --porcelain)" ]; then printf 'skip:  %s has no changes\n' "$w"
elif [ -n "$(git -C "$w" status --porcelain | grep -v '^ M tests/Bayan.Tests/SelfhostSemantics\(Generator\)\?Tests.cs$' || true)" ] \
  || [ -n "$(git -C "$w" diff | grep '^-[^-]' | grep -v 'Skip = "T28' || true)" ] \
  || [ -n "$(git -C "$w" diff | grep '^+[^+]' | grep -Ev '^\+ *\[(Fact|Theory)\]$' || true)" ]; then
  printf 'refuse: %s holds changes other than the T28 un-skips. Left alone.\n' "$w"; failed=1
elif act; then git -C "$w" checkout -- tests/Bayan.Tests; printf 'reverted: %s (T28 un-skips)\n' "$w"
else printf 'would: git -C %s checkout -- tests/Bayan.Tests (needs --execute --discard-worktree-changes)\n' "$w"; fi

printf '== 2. worktrees whose work dev holds\n'
CLEANUP_RECENT_HOURS=1 bash "$repo/scripts/cleanup/merged-worktree-backlog.sh" ${pass[@]+"${pass[@]}"} || failed=1

if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
