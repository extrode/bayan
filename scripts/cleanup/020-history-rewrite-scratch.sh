#!/usr/bin/env bash
# Cleanup after the history-rewrite prep (chore/history-cleanup, 2026-11-03): removes the scratch the rehearsals left in
# tmp/. Keeps tmp/history-rewrite/replacements.txt and dry.out: the real rewrite reads the first.
# Dry run by default. --execute / -e removes.
set -euo pipefail

execute=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: run from the main checkout on dev (on %s); the scratch is in its tmp/.\n' "$(git branch --show-current)"; exit 1
fi

# 1. rehearsal clones, probe repos, and the rewrite's generated lists (lhs.txt names the removed phrases)
printf '== 1. rehearsal scratch\n'
for t in tmp/hr-test tmp/history-rewrite/trial.git tmp/probe-rev tmp/probe-rev2 \
         tmp/history-rewrite/lhs.txt tmp/history-rewrite/mailmap tmp/history-rewrite/tip; do
  if [ ! -e "$t" ]; then printf 'skip:  %s (absent)\n' "$t"; continue; fi
  if git ls-files --error-unmatch "$t" >/dev/null 2>&1; then printf 'refuse: %s is tracked\n' "$t"; continue; fi
  if [ $execute -eq 1 ]; then rm -rf "$t" && printf 'removed: %s\n' "$t"
  else printf 'would: rm -rf %s (%s)\n' "$t" "$(du -sh "$t" | cut -f1)"; fi
done

printf '\nleft in tmp/history-rewrite: %s\n' "$(ls tmp/history-rewrite 2>/dev/null | tr '\n' ' ')"
