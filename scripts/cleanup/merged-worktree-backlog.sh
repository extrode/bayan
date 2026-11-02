#!/usr/bin/env bash
# Recurring cleanup (written 2026-11-02, after spec 015 prep left 184 worktrees behind): removes every worktree whose
# work dev already holds, then its branch. The targets are computed at run time, so the script is re-run whenever the
# backlog grows again.
# A worktree is removed when:
#   - its branch tip is in dev (a detached one: its HEAD is in dev);
#   - its branch or HEAD has not moved in the last $CLEANUP_RECENT_HOURS hours (default 24), so a worktree just cut from
#     dev for new work, whose tip is still a dev commit, is kept;
#   - git worktree remove accepts it (no modified or untracked files, not locked).
# `git worktree remove` also deletes ignored files. Build output (bin/, obj/, TestResults/, __pycache__/, .DS_Store) goes
# with --execute; any other ignored content (a tmp/ of test runs, say) also needs --delete-worktree-scratch.
# Dry run by default. --execute / -e acts.
set -euo pipefail

execute=0; scratch=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-worktree-scratch) scratch=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0; removed=0; kept=0; held=0
recent=$(( $(date +%s) - ${CLEANUP_RECENT_HOURS:-24} * 3600 ))

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi
main="$(git rev-parse --path-format=absolute --git-common-dir)"; main="${main%/.git}"

# reflog_time WT REF: the time of REF's newest reflog entry (not its commit's date), empty when it has none.
reflog_time() { git -C "$1" log -g -1 --date=unix --format=%gd "$2" 2>/dev/null | sed -n 's/.*@{\([0-9]*\)}$/\1/p' || true; }
# last_moved WT BRANCH: when the branch (or a detached worktree's HEAD) last moved, from its reflog, else its tip commit.
last_moved() {
  local t
  if [ -n "$2" ]; then t="$(reflog_time "$1" "refs/heads/$2")"
    [ -n "$t" ] || t="$(git log -1 --format=%ct "refs/heads/$2")"
  else t="$(reflog_time "$1" HEAD)"
    [ -n "$t" ] || t="$(git -C "$1" log -1 --format=%ct HEAD)"
  fi
  printf '%s' "$t"
}

printf '== worktrees whose work dev holds\n'
while IFS=$'\t' read -r wt br; do
  [ "$wt" = "$main" ] && continue
  rel="${wt#"$repo"/}"; label="${br:-detached}"
  if [ ! -d "$wt" ]; then printf 'skip:  %s is gone (git worktree prune drops it)\n' "$rel"; continue; fi
  if [ -n "$br" ]; then tip="refs/heads/$br"; else tip="$(git -C "$wt" rev-parse HEAD)"; fi
  if ! git merge-base --is-ancestor "$tip" refs/heads/dev 2>/dev/null; then
    printf 'keep:  %s (%s not in dev)\n' "$rel" "$label"; kept=$((kept+1)); continue
  fi
  if [ "$(last_moved "$wt" "$br")" -gt "$recent" ]; then
    printf 'keep:  %s (%s moved in the last %sh)\n' "$rel" "$label" "${CLEANUP_RECENT_HOURS:-24}"; kept=$((kept+1)); continue
  fi
  if [ -n "$(git -C "$wt" status --porcelain)" ]; then
    printf 'keep:  %s (modified or untracked files)\n' "$rel"; kept=$((kept+1)); continue
  fi
  other="$(git -C "$wt" status --porcelain --ignored | sed -n 's/^!! //p' \
    | grep -Ev '(^|/)(bin|obj|TestResults|__pycache__)/$|(^|/)\.DS_Store$' || true)"
  if [ -n "$other" ] && [ $scratch -eq 0 ]; then
    printf 'hold:  %s (ignored files beyond build output: %s; needs --delete-worktree-scratch)\n' \
      "$rel" "$(printf '%s\n' "$other" | head -3 | tr '\n' ' ')"; held=$((held+1))
    continue
  fi
  if [ $execute -eq 0 ]; then
    printf 'would: git worktree remove %s\n' "$rel"; [ -n "$br" ] && printf 'would: git branch -d %s\n' "$br"
    continue
  fi
  if ! git worktree remove "$wt"; then printf '  refused: %s (git said why above). Left alone.\n' "$rel"; failed=1; continue; fi
  printf 'removed: %s\n' "$rel"; removed=$((removed+1))
  if [ -n "$br" ]; then git branch -d "$br" || { printf '  refused: branch %s. Left alone.\n' "$br"; failed=1; }; fi
done < <(git worktree list --porcelain | awk '/^worktree /{w=substr($0,10)} /^branch /{print w "\t" substr($0,19)} /^detached/{print w "\t"}')

if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi
printf '\nremoved %s, kept %s, held for --delete-worktree-scratch %s; %s worktrees remain\n' \
  "$removed" "$kept" "$held" "$(git worktree list | wc -l | tr -d ' ')"
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
