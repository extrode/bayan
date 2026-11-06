#!/bin/bash
# mutation-step.sh — the mutation step shared by guard.sh (check 6) and mutate-batch.sh (spec 013 T28); source it, do not run it.
#   mutation_step <log dir (absolute, under $PWD)> <dotnet test filter> <file.byn>[,…] <1 = survivors are FAIL items | 0 = they are not>
# The filtered tests are recorded once (BAYAN_SNAPSHOT) against schemata builds whose sites lie only in the files, then
# `bayan mutate --binary` runs every site of each build, replaying the recording per mutant (replay.py); sites under
# `@unobservable because "…"` come back marked, not survived. The reports are copied to <log dir>/quality-<n>-<binary>.json.
# Problems go to the caller's note() (which fills its FAIL array: the step runs only while FAIL is empty). Returns 0 when it
# reached the per-build runs, 1 when the CLI, the recording or the manifest stopped it before.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0  # no MSBuild worker or build server outlives the run
mutation_step() {
  local LOG=$1 FILTER=$2 MUTATE=$3 SURV=$4 REL=${1#$PWD/} n name bin sites l MX
  local BAYAN SNAP=$1/snap REPLAY
  REPLAY=$(dirname "${BASH_SOURCE[0]}")/replay.py
  # `bayan mutate` is tooling: it runs from the pinned build of dev (scripts/pinned-bayan.sh), never the branch under test
  if ! BAYAN=$(bash "$(dirname "${BASH_SOURCE[0]}")/../pinned-bayan.sh" 2> "$LOG/pinned-bayan.txt"); then
    note "mutation: no pinned bayan — $(tail -1 "$LOG/pinned-bayan.txt")"
  else
    rm -f "$SNAP/manifest.json" "$SNAP/runs.jsonl"
    BAYAN_SNAPSHOT=$SNAP BAYAN_SNAPSHOT_FILES=$MUTATE dotnet test --no-build -p:UseSharedCompilation=false --filter "($FILTER)&Snapshot!=skip" --logger "console;verbosity=normal" > "$LOG/record.txt" 2>&1 \
      || note "mutation: recording the tests under BAYAN_SNAPSHOT failed ($REL/record.txt) — $(grep -m1 -A8 'Error Message' "$LOG/record.txt" | grep -v '^\s*$' | tr '\n' ' ' | cut -c1-400)"
    if [ ${#FAIL[@]} = 0 ] && [ ! -f "$SNAP/manifest.json" ]; then
      note "mutation: the recording wrote no $REL/snap/manifest.json: no test under --filter '$FILTER' builds a selfhost program"; fi
  fi
  if [ ${#FAIL[@]} = 0 ]; then
    BUILDS=$(python3 -I -c 'import json, sys; [print(b["binary"] + "\t" + b["sites"]) for b in json.load(open(sys.argv[1]))["builds"]]' "$SNAP/manifest.json" 2> "$LOG/manifest.txt") \
      || note "mutation: $REL/snap/manifest.json is unreadable — $(tail -1 "$LOG/manifest.txt")"
  fi
  [ ${#FAIL[@]} = 0 ] || return 1
  n=0
  while IFS=$'\t' read -r bin sites; do
    [ -n "$bin" ] || continue; n=$((n+1)); name=$n-$(basename "$bin"); rm -f "$(dirname "$bin")/bayan-quality.json"
    # site ids are per build: the replay runs only this build's recorded runs
    "$BAYAN" mutate --binary "$bin" --sites "$sites" -- python3 -I "$REPLAY" --binary "$bin" "$SNAP" > "$LOG/mutate-$name.txt" 2>&1; MX=$?
    # bayan mutate exits 1 only when mutant 0 fails (usage errors exit 2, a gate below its minimum 3)
    if [ $MX = 1 ]; then
      note "mutation: the recording does not replay on the unmutated build $bin (mutant 0) — $(BAYAN_MUTANT=0 python3 -I "$REPLAY" --binary "$bin" "$SNAP" 2>&1 | tail -3 | tr '\n' ' ' | cut -c1-400)"; continue; fi
    [ -f "$(dirname "$bin")/bayan-quality.json" ] || { note "mutation: bayan mutate exited $MX on $bin without a report — $(tail -3 "$LOG/mutate-$name.txt" | tr '\n' ' ')"; continue; }
    cp "$(dirname "$bin")/bayan-quality.json" "$LOG/quality-$name.json"
    echo "mutation build $name (per site: $REL/mutate-$name.txt, report: $REL/quality-$name.json):"
    sed -n '/^mutation: [0-9]/,$p' "$LOG/mutate-$name.txt"
    [ $SURV = 1 ] && while read -r l; do l=${l%: survived}; [ -n "$l" ] && note "mutation survived: ${l#$PWD/} — no test fails under this change; add one that does, or mark the statement @unobservable because \"…\""
    done < <(grep -E ': survived$' "$LOG/mutate-$name.txt")
  done <<< "$BUILDS"
  return 0
}
