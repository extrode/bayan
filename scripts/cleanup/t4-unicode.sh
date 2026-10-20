#!/usr/bin/env bash
# Cleanup for T4 (shared Unicode tables), written 2026-10-20 by the worker run.
# Removes the stray build output a misdirected publish left under src/<project>/tmp/ (gitignored,
# but its .cs files are picked up by the default compile glob). Dry run by default; --execute / -e deletes.
set -euo pipefail

execute=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
for p in Bayan.Backend.C Bayan.Cli Bayan.Lowering Bayan.Semantics Bayan.Syntax; do
  d="$repo/src/$p/tmp"
  [ -d "$d" ] || continue
  if [ "$execute" = 1 ]; then rm -rf "$d"; printf 'removed %s\n' "$d"; else printf 'would remove %s\n' "$d"; fi
done
