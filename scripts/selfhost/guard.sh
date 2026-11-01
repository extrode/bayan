#!/bin/bash
# guard.sh — run in a writer worktree before finishing. Prints GUARD PASS or GUARD FAIL + the items to fix.
#   bash guard.sh --base <branch> --allow '<glob>[,<glob>…]' --filter '<dotnet test filter>' [--task T<n>] [--mutate <file.byn>[,…]]
# Checks: 1 scope (changed files match --allow, plus handoff/todo/tasks), 2 no junk (binary or extensionless files),
# 3 build 0 warnings, 4 tests pass with 0 skipped, and coverage (spec 013 T26): when the branch changes selfhost .byn files the
# tests run under BAYAN_COVERAGE with those files, every line and branch of them must be covered (each missed one is named),
# and no file's line coverage may fall below tests/quality-baseline.json; the report is copied to tmp/guard-run/quality-coverage.json, 5 banned C#-isms in changed .byn, 6 mutation (spec 013 R14): every
# mutation site of the --mutate files must make the filtered tests fail (`bayan mutate --binary` over a BAYAN_SNAPSHOT
# recording; a survivor = an untested change; `@unobservable because "…"` marks a statement whose mutants no test can see),
# and no file's score may fall below tests/quality-baseline.json,
# 7 content checks from guard-checks.py (C# oracle in the tests, no stubs or empty bodies left, ctx.byn only in the
# task's own section, no open gating Question, docs: progress log entry and Question line citations that hold).
# Per-task landing runs no mutation (land.sh drops --mutate, user decision 2026-11-01): it runs in batches and CI; coverage
# runs on every guard, landing included.
set -u
BASE=""; ALLOW=""; FILTER=""; TASK=""; MUTATE=""
while [ $# -gt 0 ]; do case "$1" in
  --base) BASE=$2; shift 2;; --allow) ALLOW=$2; shift 2;; --filter) FILTER=$2; shift 2;; --task) TASK=$2; shift 2;;
  --mutate) MUTATE=$2; shift 2;; *) echo "unknown arg $1"; exit 2;; esac; done
[ -n "$BASE" ] && [ -n "$FILTER" ] || { echo "need --base and --filter"; exit 2; }
FAIL=(); note() { FAIL+=("$1"); }
mkdir -p "$PWD/tmp/guard-run"; LOG=$PWD/tmp/guard-run

# 1 scope + 2 junk
CHANGED=$( { git diff --name-only "$BASE"...HEAD; git diff --name-only; git ls-files --others --exclude-standard; } | sort -u)
for f in $CHANGED; do
  ok=0; case "$f" in docs/bayan-handoff.md|work/todo.md|docs/specs/*/*-tasks.md|docs/specs/*/reviews/*.md|selfhost/semantics/ctx.byn) ok=1;; esac
  IFS=',' read -ra PATS <<< "$ALLOW"; for p in "${PATS[@]}"; do [[ "$f" == $p ]] && ok=1; done
  [ $ok = 1 ] || note "scope: $f is outside --allow ($ALLOW); revert it or record the need as a Question"
  b=$(basename "$f"); [[ "$b" != *.* ]] && note "junk: $f has no extension (stray run-case output?); delete it"
  [ -f "$f" ] && ! grep -Iq . "$f" 2>/dev/null && [ -s "$f" ] && note "junk: $f is binary; delete it"
done
# the changed selfhost .byn files still present: the files coverage is measured over
COVER=$(for f in $CHANGED; do case "$f" in selfhost/*.byn) [ -f "$f" ] && echo "$f";; esac; done | tr '\n' ',' | sed 's/,$//')
rm -f $LOG/quality-*.json  # a report left by an earlier run must not reach land.sh's baseline update
COV=$LOG/coverage; rm -f "$COV/bayan-quality.json"; TX=1

# 3 build
dotnet build -v q > $LOG/build.txt 2>&1
W=$(grep -cE ' warning [A-Z]+[0-9]+' $LOG/build.txt); E=$(grep -cE ' error [A-Z]+[0-9]+' $LOG/build.txt)
[ "$E" = 0 ] && [ "$W" = 0 ] || note "build: $E errors, $W warnings — $(grep -m3 -E ' (error|warning) [A-Z]+[0-9]+' $LOG/build.txt | sed 's/.*\///' | tr '\n' ' ')"

# 4 tests
if [ "$E" = 0 ]; then
  ( [ -n "$COVER" ] && export BAYAN_COVERAGE=$COV BAYAN_COVERAGE_FILES=$COVER
    dotnet test --no-build --filter "$FILTER" --logger "console;verbosity=normal" ) > $LOG/test.txt 2>&1; TX=$?
  R=$(grep -E 'Total tests:|^ *(Passed|Failed|Skipped): *[0-9]+' $LOG/test.txt | tr -s ' \n' ' ')
  [ $TX = 0 ] || note "tests: FAIL ($R) — first failure: $(grep -m1 -A8 'Error Message' $LOG/test.txt | grep -v '^\s*$' | tr '\n' ' ' | cut -c1-400)"
  grep -qE 'No test matches|Total tests: 0' $LOG/test.txt && note "tests: filter '$FILTER' matched no tests"
  echo "$R" | grep -qE 'Skipped: [1-9]' && note "tests: skipped tests present ($R)"
fi

# 4 coverage of the changed selfhost .byn files (a red run's coverage proves nothing): 100% of lines and branches, no baseline drop
if [ $TX = 0 ] && [ -n "$COVER" ]; then
  if [ ! -f "$COV/bayan-quality.json" ]; then
    note "coverage: the tests wrote no tmp/guard-run/coverage/bayan-quality.json: no test under --filter '$FILTER' builds a selfhost program with $COVER"
  else
    cp "$COV/bayan-quality.json" $LOG/quality-coverage.json
    while read -r l; do case "$l" in "- "*) note "${l#- }";; *) echo "$l";; esac; done < <(python3 -I -c 'import json, os, sys
why = {"missed": "no test under --filter runs it; add a C# oracle case that does", "unknown": "a counter file was unreadable; run the guard again"}
for f in json.load(open(sys.argv[1]))["files"]:
    p, l, b = os.path.relpath(f["path"]), f["lines"], f["branches"]
    print("coverage: %s: lines %d/%d, branches %d/%d (listing: tmp/guard-run/coverage/%s.cov.txt)" % (p, l["covered"], l["total"], b["covered"], b["total"], p))
    for s in ("missed", "unknown"):
        for n in l[s]: print("- coverage: %s line %s:%d; %s" % (s, p, n, why[s]))
        for x in b[s]: print("- coverage: %s branch %s:%d:%d never %s; %s" % (s, p, x["line"], x["column"], "true" if x["outcome"] else "false", why[s]))' "$COV/bayan-quality.json")
    while read -r l; do [ -n "$l" ] && note "$l"; done < <(python3 -I "$(dirname "$0")/quality-baseline.py" check tests/quality-baseline.json . $LOG/quality-coverage.json)
  fi
fi

# 5 banned C#-isms (Bayan has none of these; see scripts/selfhost/bayan-cheatsheet.md)
for f in $CHANGED; do case "$f" in *.byn) [ -f "$f" ] || continue
  grep -nE '^\s*(let|var)\s*\(' "$f" | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple destructuring"; done
  grep -nE '[A-Za-z_)]\.[0-9]+\b' "$f" | grep -vE '"[^"]*\.[0-9]' | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple field .N"; done
;; esac; done > $LOG/banned.txt
while read -r l; do [ -n "$l" ] && note "$l"; done < $LOG/banned.txt

# 7 content checks (before mutations: they are cheap)
# the task comes from --task, else from a SelfhostSemanticsT<n>Tests class in --filter (T26/T27 test classes carry no number)
[ -n "$TASK" ] || TASK=$(echo "$FILTER" | grep -oE 'SelfhostSemanticsT[0-9]+Tests' | head -1 | sed -E 's/SelfhostSemantics(T[0-9]+)Tests/\1/')
if [ -n "$TASK" ]; then
  while read -r l; do [ -n "$l" ] && note "$l"; done < <(python3 -I "$(dirname "$0")/guard-checks.py" "$BASE" "$TASK" "$ALLOW")
else note "checks: no --task and --filter names no SelfhostSemanticsT<n>Tests class, so the content checks cannot run"; fi

# 6 mutations (only when everything above passed: a mutation run on a red tree proves nothing)
# the filtered tests are recorded once (BAYAN_SNAPSHOT) against schemata builds whose sites lie only in the --mutate files,
# then `bayan mutate --binary` runs every site of each build, replaying the recording per mutant (replay.py); sites under
# `@unobservable because "…"` come back marked, not survived. The reports are copied to tmp/guard-run/ as quality-<n>-<binary>.json.
if [ ${#FAIL[@]} = 0 ] && [ -n "$MUTATE" ]; then
  BAYAN=src/Bayan.Cli/bin/Debug/net10.0/bayan; SNAP=$LOG/snap; REPLAY=$(dirname "$0")/replay.py
  if [ ! -x "$BAYAN" ]; then note "mutation: $BAYAN is missing; build src/Bayan.Cli (dotnet build) and run the guard again"
  else
    rm -f "$SNAP/manifest.json" "$SNAP/runs.jsonl"
    BAYAN_SNAPSHOT=$SNAP BAYAN_SNAPSHOT_FILES=$MUTATE dotnet test --no-build --filter "($FILTER)&Snapshot!=skip" --logger "console;verbosity=normal" > $LOG/record.txt 2>&1 \
      || note "mutation: recording the tests under BAYAN_SNAPSHOT failed (tmp/guard-run/record.txt) — $(grep -m1 -A8 'Error Message' $LOG/record.txt | grep -v '^\s*$' | tr '\n' ' ' | cut -c1-400)"
    if [ ${#FAIL[@]} = 0 ] && [ ! -f "$SNAP/manifest.json" ]; then
      note "mutation: the recording wrote no tmp/guard-run/snap/manifest.json: no test under --filter '$FILTER' builds a selfhost program"; fi
  fi
  if [ ${#FAIL[@]} = 0 ]; then
    n=0
    while IFS=$'\t' read -r bin sites; do
      n=$((n+1)); name=$n-$(basename "$bin"); rm -f "$(dirname "$bin")/bayan-quality.json"
      # site ids are per build: the replay runs only this build's recorded runs
      "$BAYAN" mutate --binary "$bin" --sites "$sites" -- python3 -I "$REPLAY" --binary "$bin" "$SNAP" > $LOG/mutate-$name.txt 2>&1; MX=$?
      # bayan mutate exits 1 only when mutant 0 fails (usage errors exit 2, a gate below its minimum 3)
      if [ $MX = 1 ]; then
        note "mutation: the recording does not replay on the unmutated build $bin (mutant 0) — $(BAYAN_MUTANT=0 python3 -I "$REPLAY" --binary "$bin" "$SNAP" 2>&1 | tail -3 | tr '\n' ' ' | cut -c1-400)"; continue; fi
      [ -f "$(dirname "$bin")/bayan-quality.json" ] || { note "mutation: bayan mutate exited $MX on $bin without a report — $(tail -3 $LOG/mutate-$name.txt | tr '\n' ' ')"; continue; }
      cp "$(dirname "$bin")/bayan-quality.json" $LOG/quality-$name.json
      echo "mutation build $name (per site: tmp/guard-run/mutate-$name.txt, report: tmp/guard-run/quality-$name.json):"
      sed -n '/^mutation: [0-9]/,$p' $LOG/mutate-$name.txt
      while read -r l; do l=${l%: survived}; [ -n "$l" ] && note "mutation survived: ${l#$PWD/} — no test fails under this change; add one that does, or mark the statement @unobservable because \"…\""
      done < <(grep -E ': survived$' $LOG/mutate-$name.txt)
    done < <(python3 -I -c 'import json, sys; [print(b["binary"] + "\t" + b["sites"]) for b in json.load(open(sys.argv[1]))["builds"]]' "$SNAP/manifest.json")
    # a score below tests/quality-baseline.json fails (land.sh raises the baseline)
    if ls $LOG/quality-*.json > /dev/null 2>&1; then
      while read -r l; do [ -n "$l" ] && note "$l"; done < <(python3 -I "$(dirname "$0")/quality-baseline.py" check tests/quality-baseline.json . $LOG/quality-*.json)
    fi
  fi
fi

if [ ${#FAIL[@]} = 0 ]; then echo "GUARD PASS"; exit 0; fi
echo "GUARD FAIL (${#FAIL[@]} items):"; for x in "${FAIL[@]}"; do echo "- $x"; done; exit 1
