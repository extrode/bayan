#!/usr/bin/env bash
# Cleanup for spec 016 T4 (std.json), written 2026-11-07 by the worker run.
# Removes the coverage output `bayan test --coverage lib/std/json` left under lib/std/out/.
# Dry run by default; --execute / -e deletes.
set -euo pipefail

execute=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"

# 1. The stray coverage output.
rel="lib/std/out"
d="$repo/$rel"
if [ -d "$d" ]; then
  if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
  elif [ "$execute" = 1 ]; then
    rm -rf "$d"; printf 'removed %s\n' "$d"
  else
    printf 'would remove %s\n' "$d"
  fi
fi

# 2. Remaining state.
git -C "$repo" status --short -- lib/std
