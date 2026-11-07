#!/usr/bin/env bash
# Cleanup after the 2026-11-03 CI fixes, Q747 and review fix/review-2026-11-03-2: removes the scratch those runs
# left in tmp/ here and the two scratch clones and bundles on wp1. Keeps tmp/publish (the bayan binary
# installed in ~/.local/bin is copied from it). Dry run by default. --execute / -e removes.
set -euo pipefail

execute=0
failed=0
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

# 1. local scratch: review claim probes, worker scratch, bundles, run logs, the hello sample
printf '== 1. tmp/ scratch\n'
for t in tmp/wt-scratch tmp/claim1 tmp/claim4 tmp/claim4-base-check tmp/claim4-cmp-16767 tmp/claim4-cmp-6104 \
         tmp/dev-3500eca5.bundle tmp/dev-73104cb2.bundle tmp/q747-final.out tmp/q747-laws.out tmp/q747-lawscheck.out \
         tmp/q747-nopanic.out tmp/q747-test.out tmp/rv2-final.out tmp/rv2-gate.log tmp/rv2-rerun.out \
         tmp/wp1-3500eca5.out tmp/wp1-73104cb2.out tmp/try \
         tmp/ci-41612215707.out tmp/ci-41612215707-failed.log tmp/ci-41612215707-win.log tmp/lex242 tmp/lex242.byn \
         tmp/fix-d75290e1.bundle tmp/fix-24503535.bundle tmp/fix-d75290e1-suite.out tmp/wp1-d75290e1.out tmp/wp1-24503535.out; do
  if [ ! -e "$t" ]; then printf 'skip:  %s (absent)\n' "$t"; continue; fi
  if git ls-files --error-unmatch "$t" >/dev/null 2>&1; then printf 'refuse: %s is tracked\n' "$t"; continue; fi
  if [ $execute -eq 1 ]; then { rm -rf "$t" && printf 'removed: %s\n' "$t"; } || { printf 'failed: %s\n' "$t"; failed=1; }
  else printf 'would: rm -rf %s (%s)\n' "$t" "$(du -sh "$t" | cut -f1)"; fi
done

# 2. wp1 (PowerShell over ssh): Windows verification clones under the old checkout's tmp/, and their bundles
printf '\n== 2. wp1 scratch\n'
for t in C:/home/code/extrode.com/bayan/tmp/win-3500eca5 C:/home/code/extrode.com/bayan/tmp/win-73104cb2 \
         C:/Users/Sy/AppData/Local/Temp/bayan-3500eca5.bundle C:/Users/Sy/AppData/Local/Temp/bayan-73104cb2.bundle \
         C:/home/code/extrode.com/bayan/tmp/win-d75290e1 C:/home/code/extrode.com/bayan/tmp/win-24503535 \
         C:/Users/Sy/AppData/Local/Temp/bayan-d75290e1.bundle C:/Users/Sy/AppData/Local/Temp/bayan-24503535.bundle; do
  if ! ssh -o BatchMode=yes wp1 "if (Test-Path '$t') { exit 0 } else { exit 1 }" 2>/dev/null; then
    printf 'skip:  wp1:%s (absent or wp1 unreachable)\n' "$t"; continue
  fi
  if [ $execute -eq 1 ]; then
    ssh -o BatchMode=yes wp1 "Remove-Item -Recurse -Force '$t'" 2>/dev/null \
      && printf 'removed: wp1:%s\n' "$t" || { printf 'failed: wp1:%s\n' "$t"; failed=1; }
  else printf 'would: remove wp1:%s\n' "$t"; fi
done

printf '\nleft in tmp/: %s entries\n' "$(ls tmp | wc -l | tr -d ' ')"
exit $failed
