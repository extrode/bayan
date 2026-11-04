#!/usr/bin/env bash
# Cleanup after spec 011, the self-hosted C emitter (2026-11-04). Removes, each only after git is asked at run time:
#   1. worker/probe/merge worktrees under .worktrees/ whose HEAD is in dev and which have no uncommitted changes
#   2. their branches and the other 011 side branches, with `git branch -d` only, when the tip is in dev
#   3. scratch under tmp/ (tracked paths refused) and four paths outside the repo
# Leaves dev, main and feat/011-self-hosted-emitter: the feature branch is the user's to delete.
# Dry run by default. --execute / -e acts. A worktree holding ignored build output (bin/obj/.cache) also needs
# --remove-ignored-build-output, which is the only place `git worktree remove --force` is used.
# CLEANUP_TMP_ROOT replaces /tmp (the test points it at its fixture); CLEANUP_RECENT_HOURS is the 019 recency window.
set -euo pipefail

execute=0; ignored=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --remove-ignored-build-output) ignored=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0
feat="feat/011-self-hosted-emitter"
recent=$(( $(date +%s) - ${CLEANUP_RECENT_HOURS:-1} * 3600 ))
ext="${CLEANUP_TMP_ROOT:-/tmp}"

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi
git rev-parse -q --verify "refs/heads/$feat" >/dev/null && git merge-base --is-ancestor "refs/heads/$feat" refs/heads/dev \
  || { printf 'refuse: %s is not in dev.\n' "$feat"; exit 1; }

# newest of the ref's reflog time and its tip's commit time
moved() {
  local t c
  t="$(git log -g -1 --date=unix --format=%gd "$1" 2>/dev/null | sed -n 's/.*@{\([0-9]*\)}$/\1/p' || true)"
  c="$(git log -1 --format=%ct "$1")"
  if [ -n "$t" ] && [ "$t" -gt "$c" ]; then printf '%s' "$t"; else printf '%s' "$c"; fi
}

printf '== 1. worktrees\n'
seen=0
wt_one() {
  local p="$1" b="$2" rel hit=0
  case "$p" in "$repo/.worktrees/"*) ;; *) return 0 ;; esac
  rel="${p#"$repo"/}"
  case "$b" in chore/worker-011-*|chore/worker-docs-small-rows|chore/t13-probe|chore/011-merge-dev) hit=1 ;; esac
  case "$rel" in .worktrees/dev-merge|.worktrees/docs-design-inputs) hit=1 ;; esac
  [ $hit -eq 1 ] || return 0
  seen=1
  if [ ! -d "$p" ]; then printf 'skip:  %s is registered but gone (pruned below)\n' "$rel"; return 0; fi
  if ! git merge-base --is-ancestor "$(git -C "$p" rev-parse HEAD)" refs/heads/dev; then printf 'keep:  %s (HEAD not in dev)\n' "$rel"; return 0; fi
  if [ -n "$(git -C "$p" status --porcelain)" ]; then printf 'keep:  %s (uncommitted changes)\n' "$rel"; return 0; fi
  local force=()
  if git -C "$p" status --porcelain --ignored | grep -q '^!!'; then
    if [ $ignored -eq 0 ]; then printf 'keep:  %s (holds ignored build output; needs --remove-ignored-build-output)\n' "$rel"; return 0; fi
    force=(--force)
  fi
  if [ $execute -eq 1 ]; then
    git worktree remove ${force[@]+"${force[@]}"} "$p" && printf 'removed: %s\n' "$rel" || { printf '  refused: %s. Left alone.\n' "$rel"; failed=1; }
  else printf 'would: git worktree remove %s%s\n' "${force[*]:+--force }" "$rel"; fi
}
wtpath=""; wtbranch=""
while IFS= read -r line; do
  case "$line" in
    "worktree "*) wtpath="${line#worktree }"; wtbranch="" ;;
    "branch refs/heads/"*) wtbranch="${line#branch refs/heads/}" ;;
    "") [ -z "$wtpath" ] || wt_one "$wtpath" "$wtbranch"; wtpath=""; wtbranch="" ;;
  esac
done < <(git worktree list --porcelain; echo)
[ $seen -eq 1 ] || printf 'skip:  no matching worktrees\n'
[ $execute -eq 0 ] || git worktree prune

printf '== 2. branches\n'
inwt="$(git worktree list --porcelain | sed -n 's|^branch refs/heads/||p')"
while read -r b; do
  case "$b" in
    dev|main|"$feat"|'') continue ;;
    chore/worker-011-*|chore/worker-docs-small-rows|docs/review-small-rows|docs/defer-grace-headers-manifest|fix/011-feat-red-rows|fix/011-guard-unreported-file|chore/011-merge-dev|chore/t13-probe|feat/011-p2|tmp/c20) ;;
    *) continue ;;
  esac
  if printf '%s\n' "$inwt" | grep -qxF -- "$b"; then printf 'keep:  %s (checked out in a worktree)\n' "$b"; continue; fi
  if ! git merge-base --is-ancestor "refs/heads/$b" refs/heads/dev; then printf 'keep:  %s (not in dev)\n' "$b"; continue; fi
  if [ "$(moved "refs/heads/$b")" -gt "$recent" ]; then printf 'keep:  %s (moved in the last %sh)\n' "$b" "${CLEANUP_RECENT_HOURS:-1}"; continue; fi
  if [ $execute -eq 1 ]; then
    git branch -q -d "$b" && printf 'deleted: %s\n' "$b" || { printf '  refused: %s (git said why above). Left alone.\n' "$b"; failed=1; }
  else printf 'would: git branch -d %s\n' "$b"; fi
done < <(git for-each-ref --format='%(refname:lstrip=2)' refs/heads)
for b in dev main "$feat"; do
  git rev-parse -q --verify "refs/heads/$b" >/dev/null || printf 'skip:  branch %s does not exist\n' "$b"
done

printf '== 3. scratch\n'
scratch() { # $1 path, $2 = repo-relative (tracked check) or empty
  local p="$1" rel="$2"
  if [ ! -e "$p" ] && [ ! -L "$p" ]; then printf 'skip:  %s already gone\n' "${rel:-$p}"; return 0; fi
  if [ -n "$rel" ] && git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
    failed=1; return 0
  fi
  if [ $execute -eq 1 ]; then rm -rf -- "$p" && printf 'removed: %s\n' "${rel:-$p}"
  else printf 'would: rm -rf %s\n' "${rel:-$p}"; fi
}
for rel in tmp/011 tmp/011-* tmp/docs-in/stale-cache tmp/docs-in/stale-cache2 tmp/guard-run; do
  case "$rel" in tmp/011-*) [ -d "$rel" ] || continue ;; esac  # only directories; an unmatched glob stays literal
  scratch "$repo/$rel" "$rel"
done
for p in d9break.3V2P gate-p2fix2-test.log q845gate q845-gate-test.log; do scratch "$ext/$p" ""; done

printf '\n== remaining\n'
git worktree list
git for-each-ref --format='branch: %(refname:short)' refs/heads
for p in tmp/011 tmp/011-* tmp/docs-in/stale-cache tmp/docs-in/stale-cache2 tmp/guard-run; do [ ! -e "$p" ] || printf 'scratch: %s\n' "$p"; done
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
