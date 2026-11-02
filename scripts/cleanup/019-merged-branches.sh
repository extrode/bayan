#!/usr/bin/env bash
# Cleanup after 018 (2026-11-02): leaves only dev and main, locally and on origin. Every other branch whose tip is in dev
# goes; a branch not in dev, checked out in a worktree, or moved in the last hour (another session may have just cut it)
# stays and is listed.
# Order: remote first (--delete-remote-branches, `git push origin --delete`), then local with `git branch -d`. With the
# remote copy gone, -d checks the local branch against dev only, and still refuses one that is not merged.
# Dry run by default. --execute / -e deletes local branches; --delete-remote-branches also deletes them on origin.
set -euo pipefail

execute=0; remote=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-remote-branches) remote=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0
recent=$(( $(date +%s) - ${CLEANUP_RECENT_HOURS:-1} * 3600 ))

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi
git fetch -q --prune origin
[ "$(git rev-parse dev)" = "$(git rev-parse origin/dev)" ] \
  || { printf 'refuse: dev and origin/dev differ; push or pull first.\n'; exit 1; }

# newest of the ref's reflog time and its tip's commit time; a remote-tracking ref's reflog records the fetch, so it
# counts its commit time only
moved() {
  local t c
  t=""; [ "${1#refs/remotes/}" != "$1" ] || t="$(git log -g -1 --date=unix --format=%gd "$1" 2>/dev/null | sed -n 's/.*@{\([0-9]*\)}$/\1/p' || true)"
  c="$(git log -1 --format=%ct "$1")"
  if [ -n "$t" ] && [ "$t" -gt "$c" ]; then printf '%s' "$t"; else printf '%s' "$c"; fi
}

printf '== remote branches on origin\n'
while read -r b; do
  case "$b" in dev|main|HEAD|'') continue ;; esac
  if ! git merge-base --is-ancestor "refs/remotes/origin/$b" refs/heads/dev; then printf 'keep:  origin/%s (not in dev)\n' "$b"; continue; fi
  if [ "$(moved "refs/remotes/origin/$b")" -gt "$recent" ]; then printf 'keep:  origin/%s (moved in the last %sh)\n' "$b" "${CLEANUP_RECENT_HOURS:-1}"; continue; fi
  if [ $execute -eq 1 ] && [ $remote -eq 1 ]; then
    git push -q origin --delete "$b" && printf 'deleted: origin/%s\n' "$b" || { printf '  refused: origin/%s. Left alone.\n' "$b"; failed=1; }
  else printf 'would: git push origin --delete %s (needs --execute --delete-remote-branches)\n' "$b"; fi
done < <(git for-each-ref --format='%(refname:lstrip=3)' refs/remotes/origin)

printf '== local branches\n'
inwt="$(git worktree list --porcelain | sed -n 's|^branch refs/heads/||p')"
while read -r b; do
  case "$b" in dev|main|'') continue ;; esac
  if printf '%s\n' "$inwt" | grep -qxF -- "$b"; then printf 'keep:  %s (checked out in a worktree)\n' "$b"; continue; fi
  if ! git merge-base --is-ancestor "refs/heads/$b" refs/heads/dev; then printf 'keep:  %s (not in dev)\n' "$b"; continue; fi
  if [ "$(moved "refs/heads/$b")" -gt "$recent" ]; then printf 'keep:  %s (moved in the last %sh)\n' "$b" "${CLEANUP_RECENT_HOURS:-1}"; continue; fi
  if [ $execute -eq 1 ]; then
    git branch -q -d "$b" && printf 'deleted: %s\n' "$b" || { printf '  refused: %s (git said why above). Left alone.\n' "$b"; failed=1; }
  else printf 'would: git branch -d %s\n' "$b"; fi
done < <(git for-each-ref --format='%(refname:short)' refs/heads)

printf '\nbranches left: local %s, origin %s\n' "$(git for-each-ref refs/heads | wc -l | tr -d ' ')" \
  "$(git for-each-ref refs/remotes/origin | grep -vc 'refs/remotes/origin/HEAD$' || true)"
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
