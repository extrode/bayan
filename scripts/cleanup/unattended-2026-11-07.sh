#!/usr/bin/env bash
# Written 2026-11-07 by the unattended session (017 T3-T5, 016 review fix, round 4): removes the scratch directories it
# left under tmp/ in the main checkout and the feat/017 worktree. Worker worktrees and their branches are not handled
# here: run scripts/cleanup/merged-worktree-backlog.sh for those once their work is in dev.
# Every target is checked at run time: a path git tracks is refused, a missing path is a skip.
# Dry run by default. --execute / -e acts.
set -euo pipefail

execute=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
main="$(git rev-parse --path-format=absolute --git-common-dir)"; main="${main%/.git}"
if [ "$repo" != "$main" ] || [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: run from the main checkout on dev (here: %s on %s).\n' "$repo" "$(git branch --show-current)"; exit 1
fi
failed=0; removed=0

# 1. scratch under tmp/, relative to each checkout that holds it
targets=(
  "$main|tmp/rtleak"
  "$main|tmp/native/io-read-release"
  "$main|tmp/ovl"
  "$main|tmp/gatecheck"
  "$main|tmp/review-t3"
  "$main|tmp/review-t4"
  "$main|tmp/driver-t3"
  "$main/.worktrees/feat-017-selfhost-default|tmp/land-t1"
  "$main/.worktrees/feat-017-selfhost-default|tmp/land-t3"
  "$main/.worktrees/feat-017-selfhost-default|tmp/land-t4"
)
printf '== 1. scratch directories\n'
for t in "${targets[@]}"; do
  root="${t%%|*}"; rel="${t#*|}"
  if [ ! -e "$root/$rel" ]; then printf 'skip:  %s/%s is gone\n' "${root#"$main"}" "$rel"; continue; fi
  if git -C "$root" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1 \
     || [ -n "$(git -C "$root" ls-files -- "$rel")" ]; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 0 ]; then printf 'would: rm -rf %s/%s\n' "${root#"$main"}" "$rel"; continue; fi
  rm -rf -- "${root:?}/$rel" && removed=$((removed+1)) && printf 'removed: %s/%s\n' "${root#"$main"}" "$rel"
done

# 2. remaining state
printf '== 2. remaining\n'
for t in "${targets[@]}"; do
  root="${t%%|*}"; rel="${t#*|}"
  [ -e "$root/$rel" ] && printf 'left:  %s/%s\n' "${root#"$main"}" "$rel"
done
printf 'removed %d; failed %d\n' "$removed" "$failed"
exit $failed
