#!/bin/bash
# guard.sh's mutation step on a tiny fixture with a stub `dotnet` and a stub `bayan` CLI at the repo's build path (no dotnet
# needed): --mutate records once under BAYAN_SNAPSHOT (the --mutate files, the filter with Snapshot!=skip), then runs
# `bayan mutate --binary` with replay.py per build of the manifest and prints its summary. A survivor fails with its site,
# all killed passes (marked sites included), each build of a two-build manifest replays with its own --binary, a mutant-0 mismatch fails, a bayan error
# with only an earlier run's report beside the binary fails, a score below tests/quality-baseline.json fails, a
# null baseline passes, a failed recording, a missing CLI or manifest fails with a message, and no --mutate records nothing and leaves no report.
# Coverage (T26): with a.byn changed the tests run under BAYAN_COVERAGE with it; 100% passes and leaves quality-coverage.json,
# each missed or unknown line and branch fails by file:line, a drop below the baseline fails, no report fails, and a red test run
# is not checked for coverage.
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
fi
if [ -n "${BAYAN_COVERAGE:-}" ]; then
  echo "$BAYAN_COVERAGE_FILES" > $AUX/cover.txt
  [ -f $AUX/nocov ] || { mkdir -p "$BAYAN_COVERAGE"; cp $AUX/cov.json "$BAYAN_COVERAGE/bayan-quality.json"; }
fi
[ -f $AUX/testfail ] && { printf '  Failed T99\n  Error Message:\n   oracle differs\n'; exit 1; }
exit 0
STUB
# stub bayan: logs its arguments; with $AUX/mismatch it fails as on mutant 0, with $AUX/usage as on bad arguments, else prints $AUX/out.txt and writes $AUX/report.json
mkdir -p src/Bayan.Cli/bin/Debug/net10.0
cat > src/Bayan.Cli/bin/Debug/net10.0/bayan <<'STUB'
#!/bin/bash
echo "$@" > $AUX/mutate-args.txt; echo "$@" >> $AUX/mutate-args-all.txt
[ -f $AUX/mismatch ] && { echo "bayan: (any text: the guard keys on the exit code)" >&2; exit 1; }
[ -f $AUX/usage ] && { echo "bayan: usage" >&2; exit 2; }
cat $AUX/out.txt; cp $AUX/report.json "$(dirname "$3")/bayan-quality.json"
grep -q ': survived$' $AUX/out.txt && exit 3 || exit 0
STUB
chmod +x $aux/dotnet src/Bayan.Cli/bin/Debug/net10.0/bayan; export AUX=$aux
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
touch $aux/recordfail; recordfail=$(g --mutate selfhost/semantics/a.byn); rm $aux/recordfail
rm $aux/record.txt; plain=$(g); [ -f $aux/record.txt ] && plainrec=1 || plainrec=0; ls tmp/guard-run/quality-*.json > /dev/null 2>&1 && plainrep=1 || plainrep=0
mv src/Bayan.Cli/bin/Debug/net10.0/bayan $aux/bayan; nocli=$(g --mutate selfhost/semantics/a.byn)
# cov <covered> <total> <missed lines> <unknown lines> <branches [line, column, outcome, status]>: the coverage report of a.byn
cov() { python3 -I -c 'import json, sys
f, covered, total, missed, unknown, branches = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), *map(json.loads, sys.argv[4:])
br = {"covered": sum(b[3] == "hit" for b in branches), "total": len(branches)}
for s in ("missed", "unknown"): br[s] = [{"line": b[0], "column": b[1], "outcome": b[2]} for b in branches if b[3] == s]
lines = {"covered": covered, "total": total, "missed": missed, "unknown": unknown}
print(json.dumps({"version": 1, "kind": "coverage", "files": [{"path": f, "lines": lines, "branches": br, "errorPaths": []}]}))' "$a" "$@" > $aux/cov.json; }
printf '    return a + 2\n' >> selfhost/semantics/a.byn
cov 3 3 '[]' '[]' '[[2, 10, true, "hit"], [2, 10, false, "hit"]]'; covpass=$(g); covfiles=$(cat $aux/cover.txt)
[ -f tmp/guard-run/quality-coverage.json ] && covrep=1 || covrep=0
cov 1 3 '[3]' '[5]' '[[2, 10, true, "missed"], [2, 10, false, "hit"], [3, 5, false, "unknown"]]'; covmiss=$(g)
touch $aux/testfail; covred=$(g); rm $aux/testfail
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": null, "coverage": 1.0 } } }\n' > tests/quality-baseline.json
cov 2 3 '[3]' '[]' '[[2, 10, true, "hit"], [2, 10, false, "hit"]]'; covdrop=$(ALLOWX=,tests/quality-baseline.json g); git show HEAD:tests/quality-baseline.json > tests/quality-baseline.json
touch $aux/nocov; covnone=$(g); rm $aux/nocov
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
echo "$recordfail" | grep -q '^- mutation: recording the tests under BAYAN_SNAPSHOT failed (tmp/guard-run/record.txt) — .*recording broke' || fail "a failed recording did not fail with its message" "$recordfail"
echo "$nocli" | grep -q '^- mutation: src/Bayan.Cli/bin/Debug/net10.0/bayan is missing' || fail "a missing CLI did not fail with a message" "$nocli"
echo "$plain" | grep -q '^GUARD PASS$' && [ $plainrec = 0 ] && [ $plainrep = 0 ] || fail "a guard without --mutate recorded or left a report" "$plain"
echo "$covpass" | grep -q '^GUARD PASS$' && echo "$covpass" | grep -q '^coverage: selfhost/semantics/a.byn: lines 3/3, branches 2/2 (listing: tmp/guard-run/coverage/selfhost/semantics/a.byn.cov.txt)$' \
  || fail "full coverage did not pass with its summary" "$covpass"
[ "$covfiles" = selfhost/semantics/a.byn ] || fail "the tests ran under BAYAN_COVERAGE_FILES='$covfiles', not the changed a.byn" "$covpass"
[ $covrep = 1 ] || fail "the coverage report was not copied to tmp/guard-run/quality-coverage.json" "$covpass"
echo "$covmiss" | grep -q '^- coverage: missed line selfhost/semantics/a.byn:3; no test under --filter runs it' || fail "a missed line was not a FAIL item with file:line" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: unknown line selfhost/semantics/a.byn:5; a counter file was unreadable' || fail "an unknown line was not a FAIL item" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: missed branch selfhost/semantics/a.byn:2:10 never true; ' || fail "a missed branch was not a FAIL item with file:line" "$covmiss"
echo "$covmiss" | grep -q '^- coverage: unknown branch selfhost/semantics/a.byn:3:5 never false; ' || fail "an unknown branch was not a FAIL item" "$covmiss"
echo "$covred" | grep -q '^- tests: FAIL' && ! echo "$covred" | grep -q '^- coverage:' || fail "a red test run was checked for coverage" "$covred"
echo "$covdrop" | grep -q '^- baseline: coverage score of selfhost/semantics/a.byn fell from 1.0 to 0.6667 (tests/quality-baseline.json)$' || fail "a coverage drop below the baseline was not a FAIL item" "$covdrop"
echo "$covnone" | grep -q "^- coverage: the tests wrote no tmp/guard-run/coverage/bayan-quality.json: no test under --filter 'FullyQualifiedName~SelfhostSemanticsT99Tests' builds a selfhost program with selfhost/semantics/a.byn$" \
  || fail "a coverage run without a report (an earlier run's left in place) did not fail" "$covnone"
echo PASS
