#!/bin/bash
# guard.sh's mutation step on a tiny fixture with a stub `dotnet` and a stub `bayan` CLI at the repo's build path (no dotnet
# needed): --mutate records once under BAYAN_SNAPSHOT (the --mutate files, the filter with Snapshot!=skip), then runs
# `bayan mutate --binary` with replay.py per build of the manifest and prints its summary. A survivor fails with its site,
# all killed passes (marked sites included), each build of a two-build manifest replays with its own --binary, a mutant-0 mismatch fails, a bayan error
# with only an earlier run's report beside the binary fails, a score below tests/quality-baseline.json fails, a
# null baseline passes, a failed recording, a missing CLI or manifest fails with a message, a malformed manifest or baseline
# fails naming it, and no --mutate records nothing and leaves no report.
# Coverage (T26): with a.byn changed the tests run under BAYAN_COVERAGE with it. a.byn's base function f is untouched and the
# branch adds g: g fully covered passes with f uncovered (f an info line); each missed or unknown line and branch of g fails by
# file:line, none of f's; a deleted line in f makes f gated, and a file new since --base is gated throughout; no coverage
# baseline check runs and no quality-coverage.json is left (F2); no report fails, a malformed report fails naming it, and a
# red test run is not checked for coverage. The tests run with VSTEST_TESTHOST_SHUTDOWN_TIMEOUT of at least 60 s: the stub
# writes no report without it, as vstest kills the test host before its exit handler merges ~1,100 counter files (T41).
# A changed file the coverage report has no entry for (b.byn's run reports only b.byn) fails naming it (Q820).
# lib/std (spec 016 T3): a changed lib/std/<m>/x.byn is in BAYAN_COVERAGE_FILES; misformatted it fails the format check naming it, formatted it passes.
# A changed file whose name has a space is reported by its full name.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/guard-test"; aux="$root/tmp/guard-test-aux"  # stubs and logs live outside the fixture repo
rm -rf "$r" "$aux" && mkdir -p "$r/selfhost/semantics" "$r/docs" "$r/tests" "$aux/bin" "$aux/bin2" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t
printf 'fn f(a: int) -> int {\n    if a == 1 { return 1 }\n    return a + 1\n}\n' > selfhost/semantics/a.byn
printf '# Handoff\n' > docs/bayan-handoff.md
printf 'bin/\n' > .gitignore
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": 1.0, "coverage": null } } }\n' > tests/quality-baseline.json
git add -A && git commit -qm "test: base" && git branch -q base
printf '# Handoff\n- **T99** landed.\n' > docs/bayan-handoff.md
# stub dotnet: build and tests pass; under BAYAN_SNAPSHOT it logs the filter and files and writes a one-build manifest
# (two builds with $AUX/two, none with $AUX/nomanifest)
cat > $aux/dotnet <<'STUB'
#!/bin/bash
[ "$1" = test ] || exit 0
echo "Total tests: 1  Passed: 1"
if [ -n "${BAYAN_SNAPSHOT:-}" ]; then
  echo "$BAYAN_SNAPSHOT_FILES ${@: -3:1}" > $AUX/record.txt
  [ -f $AUX/recordfail ] && { printf '  Failed T99\n  Error Message:\n   recording broke\n'; exit 1; }
  b=$(printf '{"binary": "%s", "sites": "%s"}' $AUX/bin/prog $AUX/bin/prog.sites.json)
  [ -f $AUX/two ] && b="$b, $(printf '{"binary": "%s", "sites": "%s"}' $AUX/bin2/prog2 $AUX/bin2/prog2.sites.json)"
  [ -f $AUX/nomanifest ] || { mkdir -p "$BAYAN_SNAPSHOT"; echo "{\"version\": 1, \"builds\": [$b]}" > "$BAYAN_SNAPSHOT/manifest.json"; }
  [ -f $AUX/badmanifest ] && echo '{"version": 1, "builds": [' > "$BAYAN_SNAPSHOT/manifest.json"
fi
if [ -n "${BAYAN_COVERAGE:-}" ]; then
  echo "$BAYAN_COVERAGE_FILES" > $AUX/cover.txt
  # as the real test host: the report is written at process exit, which vstest cuts short unless the guard lengthens the wait
  [ -f $AUX/nocov ] || [ "${VSTEST_TESTHOST_SHUTDOWN_TIMEOUT:-0}" -lt 60000 ] || { mkdir -p "$BAYAN_COVERAGE"; cp $AUX/cov.json "$BAYAN_COVERAGE/bayan-quality.json"; cp $AUX/fns.json "$BAYAN_COVERAGE/functions.json"; }
fi
[ -f $AUX/testfail ] && { printf '  Failed T99\n  Error Message:\n   oracle differs\n'; exit 1; }
exit 0
STUB
# stub bayan: logs its arguments; with $AUX/mismatch it fails as on mutant 0, with $AUX/usage as on bad arguments, else prints $AUX/out.txt and writes $AUX/report.json
mkdir -p src/Bayan.Cli/bin/Debug/net10.0
cat > src/Bayan.Cli/bin/Debug/net10.0/bayan <<'STUB'
#!/bin/bash
[ "$1" = format ] && { if grep -q BAD "${@: -1}"/*/*.byn; then echo "${@: -1}/m/x.byn"; exit 1; fi; exit 0; }
[ "$1" = parse ] && { grep -q '^fn ' "${@: -1}" && echo "Function f"; echo "Program"; exit 0; }
echo "$@" > $AUX/mutate-args.txt; echo "$@" >> $AUX/mutate-args-all.txt
[ -f $AUX/mismatch ] && { echo "bayan: (any text: the guard keys on the exit code)" >&2; exit 1; }
[ -f $AUX/usage ] && { echo "bayan: usage" >&2; exit 2; }
cat $AUX/out.txt; cp $AUX/report.json "$(dirname "$3")/bayan-quality.json"
grep -q ': survived$' $AUX/out.txt && exit 3 || exit 0
STUB
chmod +x $aux/dotnet src/Bayan.Cli/bin/Debug/net10.0/bayan; export AUX=$aux PINNED_BAYAN=$PWD/src/Bayan.Cli/bin/Debug/net10.0/bayan
a=$r/selfhost/semantics/a.byn
# result <killed> <survived> <marked>: stub per-site lines, summary and report
result() {
  { for ((i = 0; i < $1; i++)); do echo "$a:2:10 relational \`==\` -> \`!=\`: killed"; done
    for ((i = 0; i < $2; i++)); do echo "$a:3:14 arithmetic \`+\` -> \`-\`: survived"; done
    for ((i = 0; i < $3; i++)); do echo "$a:3:5 statement \`return a + 1\` -> \`\`: marked"; done
    echo "mutation: $1 killed, $2 survived, 0 timeout, $3 marked, 0 not reached, 0 not emitted"; echo "timing: total 1 s"; } > $aux/out.txt
  python3 -I -c 'import json, sys
f, k, s, m = sys.argv[1], *map(int, sys.argv[2:])
ms = [{"site": {"file": f}, "status": st} for st, n in (("killed", k), ("survived", s), ("marked", m)) for _ in range(n)]
print(json.dumps({"version": 1, "kind": "mutation", "files": [{"path": f, "mutants": ms}]}))' "$a" "$@" > $aux/report.json; }
g() { PATH="$aux:$PATH" bash "$root/scripts/selfhost/guard.sh" --base base --allow "selfhost/semantics/a.byn,tmp/*${ALLOWX:-}" \
  --filter 'FullyQualifiedName~SelfhostSemanticsT99Tests' "$@" 2>&1 || true; }
result 2 0 1; pass=$(g --mutate selfhost/semantics/a.byn); rec=$(cat $aux/record.txt); args=$(cat $aux/mutate-args.txt)
[ -f tmp/guard-run/quality-1-prog.json ] && report=1 || report=0
result 1 1 0; survived=$(g --mutate selfhost/semantics/a.byn)
result 2 0 1; rm -f $aux/mutate-args-all.txt; touch $aux/two; two=$(g --mutate selfhost/semantics/a.byn); rm $aux/two; twoargs=$(cat $aux/mutate-args-all.txt)
result 1 1 0; touch $aux/mismatch; mismatch=$(g --mutate selfhost/semantics/a.byn); rm $aux/mismatch
touch $aux/usage; usage=$(g --mutate selfhost/semantics/a.byn); rm $aux/usage
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": null, "coverage": null } } }\n' > tests/quality-baseline.json
nullbase=$(ALLOWX=,tests/quality-baseline.json g --mutate selfhost/semantics/a.byn); git show HEAD:tests/quality-baseline.json > tests/quality-baseline.json
touch $aux/nomanifest; nomanifest=$(g --mutate selfhost/semantics/a.byn); rm $aux/nomanifest
touch $aux/badmanifest; badmanifest=$(g --mutate selfhost/semantics/a.byn); rm $aux/badmanifest
result 2 0 1; printf '{ "version": 1, "files": ' > tests/quality-baseline.json
badbase=$(ALLOWX=,tests/quality-baseline.json g --mutate selfhost/semantics/a.byn); git show HEAD:tests/quality-baseline.json > tests/quality-baseline.json
touch $aux/recordfail; recordfail=$(g --mutate selfhost/semantics/a.byn); rm $aux/recordfail
rm $aux/record.txt; plain=$(g); [ -f $aux/record.txt ] && plainrec=1 || plainrec=0; ls tmp/guard-run/quality-*.json > /dev/null 2>&1 && plainrep=1 || plainrep=0
printf x > "stray file.txt"; space=$(g); rm "stray file.txt"
mv src/Bayan.Cli/bin/Debug/net10.0/bayan $aux/bayan; nocli=$(g --mutate selfhost/semantics/a.byn); mv $aux/bayan src/Bayan.Cli/bin/Debug/net10.0/bayan
# cov <covered> <total> <missed lines> <unknown lines> <branches [line, column, outcome, status]>: the coverage report of a.byn
cov() { python3 -I -c 'import json, sys
f, covered, total, missed, unknown, branches = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), *map(json.loads, sys.argv[4:])
br = {"covered": sum(b[3] == "hit" for b in branches), "total": len(branches)}
for s in ("missed", "unknown"): br[s] = [{"line": b[0], "column": b[1], "outcome": b[2]} for b in branches if b[3] == s]
lines = {"covered": covered, "total": total, "missed": missed, "unknown": unknown}
print(json.dumps({"version": 1, "kind": "coverage", "files": [{"path": f, "lines": lines, "branches": br, "errorPaths": []}]}))' "$a" "$@" > $aux/cov.json; }
# fns <f's [covered, total] lines> <f's branches> <g's lines> <g's branches>: functions.json for a.byn (f 1-4, a blank line, g 6-9)
fns() { printf '{"version": 1, "files": {"%s": [{"span": [1, 4], "lines": %s, "branches": %s}, {"span": [6, 9], "lines": %s, "branches": %s}]}}\n' "$a" "$@" > $aux/fns.json; }
printf '\nfn g(a: int) -> int {\n    if a > 2 { return 2 }\n    return a\n}\n' >> selfhost/semantics/a.byn
# f uncovered (line 3, branch 2:10 never true), g covered; the blank line opens the hunk adding g
cov 3 4 '[3]' '[]' '[[2, 10, true, "missed"], [2, 10, false, "hit"], [7, 10, true, "hit"], [7, 10, false, "hit"]]'; fns '[1, 2]' '[1, 2]' '[2, 2]' '[2, 2]'
covpass=$(g); covfiles=$(cat $aux/cover.txt); [ -f tmp/guard-run/quality-coverage.json ] && covrep=1 || covrep=0
# f and g both short: only g's are items
cov 1 4 '[3, 8]' '[7]' '[[2, 10, true, "missed"], [7, 10, true, "missed"], [7, 10, false, "hit"], [8, 5, false, "unknown"]]'; fns '[1, 2]' '[0, 1]' '[0, 2]' '[1, 3]'
covmiss=$(g)
touch $aux/testfail; covred=$(g); rm $aux/testfail
# a coverage baseline above the run's is not checked
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": null, "coverage": 1.0, "branches": 1.0 } } }\n' > tests/quality-baseline.json
cov 3 4 '[3]' '[]' '[[2, 10, true, "missed"], [2, 10, false, "hit"], [7, 10, true, "hit"], [7, 10, false, "hit"]]'; fns '[1, 2]' '[1, 2]' '[2, 2]' '[2, 2]'
covbase=$(ALLOWX=,tests/quality-baseline.json g); git show HEAD:tests/quality-baseline.json > tests/quality-baseline.json
touch $aux/nocov; covnone=$(g); rm $aux/nocov
printf '{"version": 1, "files": [' > $aux/cov.json; covbad=$(g)
# deleting f's line 2 makes f (then 1-3) gated: a deletion touches the lines on either side of it
sed -i.bak 2d selfhost/semantics/a.byn; rm selfhost/semantics/a.byn.bak
cov 2 3 '[2]' '[]' '[[6, 10, true, "hit"], [6, 10, false, "hit"]]'
printf '{"version": 1, "files": {"%s": [{"span": [1, 3], "lines": [0, 1], "branches": [0, 0]}, {"span": [5, 8], "lines": [2, 2], "branches": [2, 2]}]}}\n' "$a" > $aux/fns.json
covtouch=$(g)
# a file new since base is touched throughout
printf 'fn h() -> int {\n    return 1\n}\n' > selfhost/semantics/b.byn
a=$r/selfhost/semantics/b.byn cov 0 1 '[2]' '[]' '[]'
printf '{"version": 1, "files": {"%s": [{"span": [1, 3], "lines": [0, 1], "branches": [0, 0]}]}}\n' "$r/selfhost/semantics/b.byn" > $aux/fns.json
covnew=$(ALLOWX=,selfhost/semantics/b.byn g)
# a changed file with only constants, absent from the report, has nothing to cover (info line, no FAIL item)
printf 'const k: string = "x"\n' > selfhost/semantics/c.byn
covconst=$(ALLOWX=,selfhost/semantics/b.byn,selfhost/semantics/c.byn g)
# lib/std: a misformatted file fails the format check naming it, the formatted one passes, and a changed one is covered
mkdir -p lib/std/m; printf 'fn k() -> int {\n  return 1 /*BAD*/\n}\n' > lib/std/m/x.byn
stdbad=$(ALLOWX=,lib/std/m/x.byn g); stdcover=$(cat $aux/cover.txt)
printf 'fn k() -> int {\n    return 1\n}\n' > lib/std/m/x.byn
stdgood=$(ALLOWX=,lib/std/m/x.byn g)
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
echo "$pass" | grep -q '^GUARD PASS$' || fail "all killed (one marked) did not pass" "$pass"
echo "$pass" | grep -q '^mutation: 2 killed, 0 survived, 0 timeout, 1 marked' || fail "the bayan mutate summary was not printed" "$pass"
[ "$rec" = "selfhost/semantics/a.byn (FullyQualifiedName~SelfhostSemanticsT99Tests)&Snapshot!=skip" ] || fail "recorded with the wrong files or filter" "$rec"
[ "$args" = "mutate --binary $aux/bin/prog --sites $aux/bin/prog.sites.json -- python3 -I $root/scripts/selfhost/replay.py --binary $aux/bin/prog $r/tmp/guard-run/snap" ] || fail "bayan mutate ran with the wrong arguments" "$args"
S="$r/tmp/guard-run/snap"; R="python3 -I $root/scripts/selfhost/replay.py"
[ "$twoargs" = "mutate --binary $aux/bin/prog --sites $aux/bin/prog.sites.json -- $R --binary $aux/bin/prog $S
mutate --binary $aux/bin2/prog2 --sites $aux/bin2/prog2.sites.json -- $R --binary $aux/bin2/prog2 $S" ] && echo "$two" | grep -q '^GUARD PASS$' || fail "the two builds did not each replay with their own --binary" "$twoargs
$two"
[ $report = 1 ] || fail "the report was not copied to tmp/guard-run/quality-1-prog.json" "$pass"
echo "$survived" | grep -q '^- mutation survived: selfhost/semantics/a.byn:3:14 arithmetic `+` -> `-` — ' || fail "a survivor was not a FAIL item with its site" "$survived"
echo "$survived" | grep -q '^- baseline: mutation score of selfhost/semantics/a.byn fell from 1.0 to 0.5 (tests/quality-baseline.json)$' || fail "a baseline drop was not a FAIL item" "$survived"
echo "$mismatch" | grep -q '^- mutation: the recording does not replay on the unmutated build' || fail "a mutant-0 mismatch did not fail" "$mismatch"
echo "$usage" | grep -q "^- mutation: bayan mutate exited 2 on $aux/bin/prog without a report" || fail "an earlier run's report was used after a bayan error" "$usage"
echo "$nullbase" | grep -q 'fell from' && fail "a null baseline failed" "$nullbase"
echo "$nullbase" | grep -q '^- mutation survived' || fail "the null-baseline run did not run mutation" "$nullbase"
echo "$nomanifest" | grep -q '^- mutation: the recording wrote no tmp/guard-run/snap/manifest.json' || fail "a missing manifest did not fail with a message" "$nomanifest"
echo "$badmanifest" | grep -q '^GUARD FAIL' && echo "$badmanifest" | grep -q '^- mutation: tmp/guard-run/snap/manifest.json is unreadable — ' || fail "a malformed manifest did not fail naming it" "$badmanifest"
echo "$badbase" | grep -q '^GUARD FAIL' && echo "$badbase" | grep -q '^- baseline: quality-baseline.py check failed on tests/quality-baseline.json and tmp/guard-run/quality-\*.json — ' || fail "a malformed baseline did not fail naming it" "$badbase"
echo "$recordfail" | grep -q '^- mutation: recording the tests under BAYAN_SNAPSHOT failed (tmp/guard-run/record.txt) — .*recording broke' || fail "a failed recording did not fail with its message" "$recordfail"
echo "$nocli" | grep -q '^- mutation: no pinned bayan — pinned-bayan: PINNED_BAYAN=.*/src/Bayan.Cli/bin/Debug/net10.0/bayan is not an executable' || fail "a missing CLI did not fail with a message" "$nocli"
echo "$plain" | grep -q '^GUARD PASS$' && [ $plainrec = 0 ] && [ $plainrep = 0 ] || fail "a guard without --mutate recorded or left a report" "$plain"
L='(listing: tmp/guard-run/coverage/selfhost/semantics/a.byn.cov.txt)'
echo "$covpass" | grep -q '^GUARD PASS$' || fail "an uncovered untouched function failed the guard" "$covpass"
echo "$covpass" | grep -qF "coverage: selfhost/semantics/a.byn: touched functions: lines 2/2, branches 2/2 $L" \
  && echo "$covpass" | grep -qF "coverage: selfhost/semantics/a.byn: other functions, not gated (no line of theirs changed): lines 1/2, branches 1/2 $L" \
  || fail "the touched and other functions' summary lines are missing" "$covpass"
[ "$covfiles" = selfhost/semantics/a.byn ] || fail "the tests ran under BAYAN_COVERAGE_FILES='$covfiles', not the changed a.byn" "$covpass"
[ $covrep = 0 ] || fail "the coverage report was copied to tmp/guard-run/quality-coverage.json (land.sh would record it)" "$covpass"
echo "$covmiss" | grep -q '^- coverage: missed line selfhost/semantics/a.byn:8; no test under --filter runs it' || fail "a missed line of a touched function was not a FAIL item with file:line" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: unknown line selfhost/semantics/a.byn:7; a counter file was unreadable' || fail "an unknown line was not a FAIL item" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: missed branch selfhost/semantics/a.byn:7:10 never true; ' || fail "a missed branch was not a FAIL item with file:line" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: unknown branch selfhost/semantics/a.byn:8:5 never false; ' || fail "an unknown branch was not a FAIL item" "$covmiss"
echo "$covmiss" | grep -qE '^- coverage: .* selfhost/semantics/a.byn:(3|2:10)' && fail "an untouched function's miss was a FAIL item" "$covmiss"
echo "$covred" | grep -q '^- tests: FAIL' && ! echo "$covred" | grep -q '^- coverage:' || fail "a red test run was checked for coverage" "$covred"
echo "$covbase" | grep -q '^GUARD PASS$' || fail "the guard checked coverage against the baseline" "$covbase"
echo "$space" | grep -qxF -- "- scope: stray file.txt is outside --allow (selfhost/semantics/a.byn,tmp/*); revert it or record the need as a Question" || fail "a changed file with a space in its name was not reported by its full name" "$space"
echo "$covbad" | grep -q '^GUARD FAIL' && echo "$covbad" | grep -q "^- coverage: coverage-gate.py: $r/tmp/guard-run/coverage/bayan-quality.json is unreadable: " || fail "a malformed coverage report did not fail naming it" "$covbad"
echo "$covtouch" | grep -q '^- coverage: missed line selfhost/semantics/a.byn:2; ' || fail "a deleted line in f did not make f gated" "$covtouch"
echo "$covnew" | grep -q '^- coverage: missed line selfhost/semantics/b.byn:2; ' || fail "a function of a file new since --base was not gated" "$covnew"
echo "$covnew" | grep -qxF -- "- coverage: selfhost/semantics/a.byn is in no build under --filter 'FullyQualifiedName~SelfhostSemanticsT99Tests' (no entry in tmp/guard-run/coverage/bayan-quality.json), so its touched functions are unchecked" \
  && ! echo "$covnew" | grep -q '^- coverage: selfhost/semantics/b.byn is in no build' || fail "a changed file absent from the coverage report was not a FAIL item naming it" "$covnew"
echo "$covconst" | grep -qxF -- "coverage: selfhost/semantics/c.byn declares no function; nothing to cover" && ! echo "$covconst" | grep -q '^- coverage: selfhost/semantics/c.byn' || fail "a const-only file absent from the report was not an info line" "$covconst"
echo "$covpass" | grep -q 'is in no build' && fail "a changed file in the coverage report was reported absent" "$covpass"
echo "$covnone" | grep -q "^- coverage: the tests wrote no tmp/guard-run/coverage/bayan-quality.json: no test under --filter 'FullyQualifiedName~SelfhostSemanticsT99Tests' builds a selfhost program with selfhost/semantics/a.byn$" \
  || fail "a coverage run without a report (an earlier run's left in place) did not fail" "$covnone"
echo "$stdbad" | grep -q '^- format: bayan format --check lib/std failed: .*lib/std/m/x.byn' || fail "a misformatted lib/std file did not fail the format check naming it" "$stdbad"
echo "$stdgood" | grep -q '^- format:' && fail "a formatted lib/std file failed the format check" "$stdgood"
case ",$stdcover," in *,lib/std/m/x.byn,*) ;; *) fail "a changed lib/std file was not in BAYAN_COVERAGE_FILES ($stdcover)" "$stdbad";; esac
echo PASS
