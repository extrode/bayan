#!/bin/bash
# guard.sh — run in a writer worktree before finishing. Prints GUARD PASS or GUARD FAIL + the items to fix.
#   bash guard.sh --base <branch> --allow '<glob>[,<glob>…]' --filter '<dotnet test filter>' [--task T<n>] [--mutate <file.byn>[,…]] [--spec <nnn>] [--scope-only]
# --spec picks the row of specs.tsv (default 010): its shared-state files are allowed in scope, its test-class prefix names the task;
# --scope-only stops after check 1 and 2 (land.sh --check-scope).
# Changed lib/std/**/*.byn files are treated like selfhost .byn files wherever the guard selects them (coverage, banned C#-isms);
# with one changed, the coverage run adds StdTestFilesTests to --filter, whose harness build of each std module's tests counts them,
# and check 5 runs the pinned `bayan format --check` over lib/std/ (skipped on Windows, as SourcesAreFormatted).
# Checks: 1 scope (changed files match --allow, plus handoff/todo/tasks), 2 no junk (binary or extensionless files),
# 3 build 0 warnings, 4 tests pass with 0 skipped, and coverage (spec 013 T26): when the branch changes selfhost .byn files the
# tests run under BAYAN_COVERAGE with those files, and every line and branch of each function the branch touches (its span
# holds a line added or changed against the merge base with --base; a new function counts) must be covered, each missed one
# named (coverage-gate.py; `// coverage-exempt:` for code dead in the C#, `// coverage-blocked: Qnnn T<k> <fn>` for code
# behind sibling T<k>'s stub while T<k> is open and not --task); each file's other functions get one info line, never a FAIL; a file under another specs.tsv row's bydir (an 011 task editing selfhost/semantics) has only its changed lines gated (Q871), while files outside every bydir (selfhost/parser) keep the touched-function rule; a changed file the report has no entry for is one (Q820) unless its AST (pinned bayan parse --ast) holds no Function, Constructor, Getter, Property, Test or Block line: then an info line. The guard makes no baseline check of
# coverage: it leaves the report at tmp/guard-run/coverage/bayan-quality.json, and land.sh records it (raise-only) in
# tests/quality-baseline.json. 5 banned C#-isms in changed .byn, 6 mutation (spec 013 R14): every
# mutation site of the --mutate files must make the filtered tests fail (`bayan mutate --binary` over a BAYAN_SNAPSHOT
# recording; a survivor = an untested change; `@unobservable because "…"` marks a statement whose mutants no test can see),
# and no file's score may fall below tests/quality-baseline.json (only with --mutate),
# 7 content checks from guard-checks.py (C# oracle in the tests, no stubs or empty bodies left, ctx.byn only in the
# task's own section, no open gating Question, docs: progress log entry and Question line citations that hold).
# Per-task landing runs no mutation (land.sh drops --mutate, user decision 2026-11-01): it runs in batches and CI; coverage
# runs on every guard (when a selfhost .byn changed), landing included. A python helper that exits non-zero (a malformed report, manifest or baseline) is a
# FAIL item naming its input, never a silent pass.
set -u
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0  # no MSBuild worker or build server outlives the run
BASE=""; ALLOW=""; FILTER=""; TASK=""; MUTATE=""; SPEC=010; SCOPEONLY=0
while [ $# -gt 0 ]; do case "$1" in
  --base) BASE=$2; shift 2;; --allow) ALLOW=$2; shift 2;; --filter) FILTER=$2; shift 2;; --task) TASK=$2; shift 2;;
  --mutate) MUTATE=$2; shift 2;; --spec) SPEC=$2; shift 2;; --scope-only) SCOPEONLY=1; shift;; *) echo "unknown arg $1"; exit 2;; esac; done
[ -n "$BASE" ] && [ -n "$FILTER" ] || { echo "need --base and --filter"; exit 2; }
ROW=$(awk -F'\t' -v s="$SPEC" 'NR>1 && $1==s' "$(dirname "$0")/specs.tsv")
[ -n "$ROW" ] || { echo "no row for spec $SPEC in specs.tsv"; exit 2; }
IFS=$'\t' read -r _ SDIR STASKS _ _ SHARED BYDIR PREFIX _ <<< "$ROW"
FAIL=(); note() { FAIL+=("$1"); }
mkdir -p "$PWD/tmp/guard-run"; LOG=$PWD/tmp/guard-run

# 1 scope + 2 junk
CHANGED=$( { git diff --name-only "$BASE"...HEAD; git diff --name-only; git ls-files --others --exclude-standard; } | sort -u)
while IFS= read -r f; do
  [ -n "$f" ] || continue
  ok=0; case "$f" in docs/bayan-handoff.md|work/todo.md|docs/specs/*/*-tasks.md|docs/specs/*/reviews/*.md) ok=1;; esac
  IFS=',' read -ra SH <<< "$SHARED"; for p in "${SH[@]}"; do [ "$f" = "$p" ] && ok=1; done
  IFS=',' read -ra PATS <<< "$ALLOW"; for p in "${PATS[@]}"; do [[ "$f" == $p ]] && ok=1; done
  [ $ok = 1 ] || note "scope: $f is outside --allow ($ALLOW); revert it or record the need as a Question"
  b=$(basename "$f"); [[ "$b" != *.* ]] && note "junk: $f has no extension (stray run-case output?); delete it"
  [ -f "$f" ] && ! grep -Iq . "$f" 2>/dev/null && [ -s "$f" ] && note "junk: $f is binary; delete it"
done <<< "$CHANGED"
if [ $SCOPEONLY = 1 ]; then
  if [ ${#FAIL[@]} = 0 ]; then echo "GUARD PASS"; exit 0; fi
  echo "GUARD FAIL (${#FAIL[@]} items):"; for x in "${FAIL[@]}"; do echo "- $x"; done; exit 1
fi
# the changed selfhost .byn files still present: the files coverage is measured over
COVER=$(while IFS= read -r f; do case "$f" in selfhost/*.byn|lib/std/*.byn) [ -f "$f" ] && echo "$f";; esac; done <<< "$CHANGED" | tr '\n' ',' | sed 's/,$//')
# a changed lib/std file is covered by its module's own tests (StdTestFilesTests), whatever --filter selects
TFILTER=$FILTER; case ",$COVER" in *,lib/std/*) TFILTER="($FILTER)|FullyQualifiedName~StdTestFilesTests";; esac
rm -f "$LOG/quality-"*.json  # a mutation report left by an earlier run must not reach this run's baseline check
COV="$LOG/coverage"; rm -f "$COV/bayan-quality.json" "$COV/functions.json"; TX=1

# 3 build
dotnet build -v q -p:UseSharedCompilation=false > "$LOG/build.txt" 2>&1
W=$(grep -cE ' warning [A-Z]+[0-9]+' "$LOG/build.txt"); E=$(grep -cE ' error [A-Z]+[0-9]+' "$LOG/build.txt")
[ "$E" = 0 ] && [ "$W" = 0 ] || note "build: $E errors, $W warnings — $(grep -m3 -E ' (error|warning) [A-Z]+[0-9]+' "$LOG/build.txt" | sed 's/.*\///' | tr '\n' ' ')"

# 4 tests
if [ "$E" = 0 ]; then
  # the harness writes the coverage report in a ProcessExit handler; vstest kills the test host shortly after the run unless
  # given longer, and merging ~1,100 counter files (L022 under coverage, T41) takes more than its default
  ( [ -n "$COVER" ] && export BAYAN_COVERAGE=$COV BAYAN_COVERAGE_FILES=$COVER VSTEST_TESTHOST_SHUTDOWN_TIMEOUT=600000
    dotnet test --no-build -p:UseSharedCompilation=false --filter "$TFILTER" --logger "console;verbosity=normal" ) > "$LOG/test.txt" 2>&1; TX=$?
  R=$(grep -E 'Total tests:|^ *(Passed|Failed|Skipped): *[0-9]+' "$LOG/test.txt" | tr -s ' \n' ' ')
  [ $TX = 0 ] || note "tests: FAIL ($R) — first failure: $(grep -m1 -A8 'Error Message' "$LOG/test.txt" | grep -v '^\s*$' | tr '\n' ' ' | cut -c1-400)"
  grep -qE 'No test matches|Total tests: 0' "$LOG/test.txt" && note "tests: filter '$TFILTER' matched no tests"
  echo "$R" | grep -qE 'Skipped: [1-9]' && note "tests: skipped tests present ($R)"
fi

# 4 coverage of the functions the branch touches in the changed selfhost .byn files (a red run's coverage proves nothing):
# 100% of their lines and branches; no baseline check (see the header)
if [ $TX = 0 ] && [ -n "$COVER" ]; then
  if [ ! -f "$COV/bayan-quality.json" ]; then
    note "coverage: the tests wrote no tmp/guard-run/coverage/bayan-quality.json: no test under --filter '$TFILTER' builds a selfhost program with $COVER"
  else
    # a changed file no build under --filter compiles has no entry in the report, so none of its functions is checked (Q820);
    # a malformed report fails in coverage-gate.py below, naming it
    miss=$(python3 -I -c 'import json, os, sys; p = {os.path.realpath(f["path"]) for f in json.load(open(sys.argv[1]))["files"]}; print("\n".join(f for f in sys.argv[2].split(",") if os.path.realpath(f) not in p))' \
      "$COV/bayan-quality.json" "$COVER" 2> /dev/null) || miss=""
    while read -r f; do
      [ -n "$f" ] || continue
      # a file declaring no function has no counters, so the report never lists it: nothing to cover (any doubt stays a FAIL item)
      if CLI=$(bash "$(dirname "$0")/../pinned-bayan.sh" 2> /dev/null) && ast=$("$CLI" parse --ast "$f" 2> /dev/null) \
        && ! echo "$ast" | awk '$1 ~ /^(Function|Constructor|Getter|Property|Test|Block)$/ { found = 1 } END { exit !found }'; then
        echo "coverage: $f declares no function; nothing to cover"; continue
      fi
      note "coverage: $f is in no build under --filter '$TFILTER' (no entry in tmp/guard-run/coverage/bayan-quality.json), so its touched functions are unchecked"
    done <<< "$miss"
    out=$(python3 -I "$(dirname "$0")/coverage-gate.py" "$(git merge-base "$BASE" HEAD)" "$COV/bayan-quality.json" "$COV/functions.json" \
      "$SDIR/$STASKS" "$TASK" "$BYDIR" 2> "$LOG/coverage-gate.txt") \
      || note "coverage: $(tail -1 "$LOG/coverage-gate.txt")"
    while read -r l; do case "$l" in "- "*) note "${l#- }";; ?*) echo "$l";; esac; done <<< "$out"
  fi
fi

# 5 banned C#-isms (Bayan has none of these; see scripts/selfhost/bayan-cheatsheet.md)
while IFS= read -r f; do case "$f" in *.byn) [ -f "$f" ] || continue
  grep -nE '^\s*(let|var)\s*\(' "$f" | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple destructuring"; done
  grep -nE '[A-Za-z_)]\.[0-9]+\b' "$f" | grep -vE '"[^"]*\.[0-9]' | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple field .N"; done
;; esac; done <<< "$CHANGED" > "$LOG/banned.txt"
while read -r l; do [ -n "$l" ] && note "$l"; done < "$LOG/banned.txt"

# 5 bayan format --check over lib/std/ (only when the branch changes a lib/std .byn file; skipped on Windows, where pinned-bayan.sh exits 127)
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *)
  case ",$COVER" in *,lib/std/*)
    if CLI=$(bash "$(dirname "$0")/../pinned-bayan.sh" 2> "$LOG/pinned.txt" | tail -1) && [ -n "$CLI" ]; then
      fmt=$("$CLI" format --check lib/std 2>&1) || note "format: bayan format --check lib/std failed: $(echo "$fmt" | grep -v '^\s*$' | head -5 | tr '\n' ' ')"
    else note "format: no pinned bayan for the lib/std format check — $(tail -1 "$LOG/pinned.txt")"; fi;;
  esac;;
esac

# 7 content checks (before mutations: they are cheap)
# the task comes from --task, else from a <prefix><n>Tests class in --filter (the row's test-class prefix; T26/T27 test classes carry no number)
[ -n "$TASK" ] || TASK=$(echo "$FILTER" | grep -oE "${PREFIX}[0-9]+Tests" | head -1 | sed -E "s/${PREFIX%T}(T[0-9]+)Tests/\1/")
if [ -n "$TASK" ]; then
  out=$(python3 -I "$(dirname "$0")/guard-checks.py" "$BASE" "$TASK" "$ALLOW" "$SPEC" 2> "$LOG/checks.txt") || note "checks: guard-checks.py failed: $(tail -1 "$LOG/checks.txt")"
  while read -r l; do [ -n "$l" ] && note "$l"; done <<< "$out"
else note "checks: no --task and --filter names no ${PREFIX}<n>Tests class, so the content checks cannot run"; fi

# 6 mutations (only when everything above passed: a mutation run on a red tree proves nothing)
# the recording and the per-build runs are mutation-step.sh (shared with mutate-batch.sh); the reports are copied to
# tmp/guard-run/ as quality-<n>-<binary>.json
if [ ${#FAIL[@]} = 0 ] && [ -n "$MUTATE" ]; then
  . "$(dirname "$0")/mutation-step.sh"
  if mutation_step "$LOG" "$FILTER" "$MUTATE" 1; then
    # a score below tests/quality-baseline.json fails (mutate-batch.sh, the batch run, raises the baseline)
    if ls "$LOG/quality-"*.json > /dev/null 2>&1; then
      # exit 1 with lines: the drops; a non-zero exit without them: quality-baseline.py failed on its inputs
      out=$(python3 -I "$(dirname "$0")/quality-baseline.py" check tests/quality-baseline.json . "$LOG/quality-"*.json 2> "$LOG/baseline.txt"); BX=$?
      while read -r l; do [ -n "$l" ] && note "$l"; done <<< "$out"
      [ $BX = 0 ] || [ -n "$out" ] || note "baseline: quality-baseline.py check failed on tests/quality-baseline.json and tmp/guard-run/quality-*.json — $(tail -1 "$LOG/baseline.txt")"
    fi
  fi
fi

if [ ${#FAIL[@]} = 0 ]; then echo "GUARD PASS"; exit 0; fi
echo "GUARD FAIL (${#FAIL[@]} items):"; for x in "${FAIL[@]}"; do echo "- $x"; done; exit 1
