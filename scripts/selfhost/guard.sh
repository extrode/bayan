#!/bin/bash
# guard.sh — run in a writer worktree before finishing. Prints GUARD PASS or GUARD FAIL + the items to fix.
#   bash guard.sh --base <branch> --allow '<glob>[,<glob>…]' --filter '<dotnet test filter>' [--mutate <file.byn>[,…]] [--mutations N]
# Checks: 1 scope (changed files match --allow, plus handoff/todo/tasks), 2 no junk (binary or extensionless files),
# 3 build 0 warnings, 4 tests pass with 0 skipped, 5 banned C#-isms in changed .byn, 6 mutation: each of N semantic
# mutations per --mutate file must make the filtered tests fail (a surviving mutation = an untested line),
# 7 content checks from guard-checks.py (C# oracle in the tests, no stubs or empty bodies left, ctx.byn only in the
# task's own section, no open gating Question, docs: progress log entry and Question line citations that hold).
set -u
BASE=""; ALLOW=""; FILTER=""; MUTATE=""; N=6
while [ $# -gt 0 ]; do case "$1" in
  --base) BASE=$2; shift 2;; --allow) ALLOW=$2; shift 2;; --filter) FILTER=$2; shift 2;;
  --mutate) MUTATE=$2; shift 2;; --mutations) N=$2; shift 2;; *) echo "unknown arg $1"; exit 2;; esac; done
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

# 3 build
dotnet build -v q > $LOG/build.txt 2>&1
W=$(grep -cE ' warning [A-Z]+[0-9]+' $LOG/build.txt); E=$(grep -cE ' error [A-Z]+[0-9]+' $LOG/build.txt)
[ "$E" = 0 ] && [ "$W" = 0 ] || note "build: $E errors, $W warnings — $(grep -m3 -E ' (error|warning) [A-Z]+[0-9]+' $LOG/build.txt | sed 's/.*\///' | tr '\n' ' ')"

# 4 tests
if [ "$E" = 0 ]; then
  dotnet test --no-build --filter "$FILTER" --logger "console;verbosity=normal" > $LOG/test.txt 2>&1; TX=$?
  R=$(grep -E 'Total tests:|^ *(Passed|Failed|Skipped): *[0-9]+' $LOG/test.txt | tr -s ' \n' ' ')
  [ $TX = 0 ] || note "tests: FAIL ($R) — first failure: $(grep -m1 -A8 'Error Message' $LOG/test.txt | grep -v '^\s*$' | tr '\n' ' ' | cut -c1-400)"
  grep -qE 'No test matches|Total tests: 0' $LOG/test.txt && note "tests: filter '$FILTER' matched no tests"
  echo "$R" | grep -qE 'Skipped: [1-9]' && note "tests: skipped tests present ($R)"
fi

# 5 banned C#-isms (Bayan has none of these; see scripts/selfhost/bayan-cheatsheet.md)
for f in $CHANGED; do case "$f" in *.byn) [ -f "$f" ] || continue
  grep -nE '^\s*(let|var)\s*\(' "$f" | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple destructuring"; done
  grep -nE '[A-Za-z_)]\.[0-9]+\b' "$f" | grep -vE '"[^"]*\.[0-9]' | head -3 | while read -r l; do echo "banned: $f:${l%%:*} tuple field .N"; done
;; esac; done > $LOG/banned.txt
while read -r l; do [ -n "$l" ] && note "$l"; done < $LOG/banned.txt

# 7 content checks (before mutations: they are cheap)
TASK=$(echo "$FILTER" | grep -oE 'SelfhostSemanticsT[0-9]+Tests' | head -1 | sed -E 's/SelfhostSemantics(T[0-9]+)Tests/\1/')
if [ -n "$TASK" ]; then
  while read -r l; do [ -n "$l" ] && note "$l"; done < <(python3 -I "$(dirname "$0")/guard-checks.py" "$BASE" "$TASK" "$ALLOW")
else note "checks: --filter names no SelfhostSemanticsT<n>Tests class, so the content checks cannot run"; fi

# 6 mutations (only when everything above passed: a mutation run on a red tree proves nothing)
if [ ${#FAIL[@]} = 0 ] && [ -n "$MUTATE" ]; then
  . "$(dirname "$0")/mutant-outcome.sh"
  IFS=',' read -ra MF <<< "$MUTATE"
  for f in "${MF[@]}"; do
    cp "$f" $LOG/mutate-orig.txt
    # candidate lines: comparison, boolean, +/- 1, &&/||, outside comments and strings
    SITES=(); while read -r s; do SITES+=("$s"); done < <(grep -nE '(==|!=|<=|>=| < | > |\btrue\b|\bfalse\b|\+ 1\b|- 1\b|&&|\|\|)' "$f" | grep -vE '^\s*[0-9]+:\s*//' | cut -d: -f1)
    # lines whose only observable effect goes through an unported sibling may be exempted: `// mutation-exempt: Qnnn`
    # on the line above (or `… through N` for a span), the Question citing a line of the span (mutation-exempt.py)
    EX=(); while read -r k v; do case "$k" in skip) EX+=("$v");; note) note "$v";; esac; done < <(python3 -I "$(dirname "$0")/mutation-exempt.py" "$f" docs/bayan-handoff.md)
    KEEP=(); for s in "${SITES[@]}"; do skip=0; for e in ${EX[@]+"${EX[@]}"}; do [ "$s" = "$e" ] && skip=1; done; [ $skip = 0 ] && KEEP+=("$s"); done; SITES=("${KEEP[@]}")
    total=${#SITES[@]}; [ "$total" = 0 ] && { note "mutation: $f has no mutable lines"; continue; }
    step=$(( total / N )); [ $step -lt 1 ] && step=1; killed=0; tried=0
    for ((i=0; i<total && tried<N; i+=step)); do
      ln=${SITES[$i]}
      perl -i -pe "if (\$.==$ln) { s/==/!=/ or s/!=/==/ or s/<=/</ or s/>=/>/ or s/ < / >= / or s/ > / <= / or s/\btrue\b/false/ or s/\bfalse\b/true/ or s/\+ 1\b/+ 2/ or s/- 1\b/- 2/ or s/&&/||/ or s/\|\|/&&/ }" "$f"
      # the harness compiles the .byn when the tests run; a mutant it rejects proves nothing: skip it, try the next site
      dotnet test -v q --no-build --filter "$FILTER" --logger 'console;verbosity=normal' > $LOG/mt.txt 2>&1
      outcome=$(mutant_outcome $? $LOG/mt.txt)
      if [ "$outcome" = uncompiled ]; then cp $LOG/mutate-orig.txt "$f"; i=$((i - step + 1)); continue; fi
      tried=$((tried+1))
      if [ "$outcome" = survived ]; then note "mutation survived: $f:$ln ($(sed -n "${ln}p" "$f" | sed 's/^ *//' | cut -c1-90)) — no test fails when this line is changed; add a test that covers it"; else killed=$((killed+1)); fi
      cp $LOG/mutate-orig.txt "$f"
    done
    [ $tried -lt $N ] && [ $tried -lt $total ] && note "mutation: only $tried of $N mutants of $f compiled; add tests until enough distinct sites are covered"
    echo "mutation $f: $killed/$tried killed"
  done
  dotnet build -v q > /dev/null 2>&1
fi

if [ ${#FAIL[@]} = 0 ]; then echo "GUARD PASS"; exit 0; fi
echo "GUARD FAIL (${#FAIL[@]} items):"; for x in "${FAIL[@]}"; do echo "- $x"; done; exit 1
