#!/bin/bash
# mutate-batch.sh on a tiny fixture with a stub `dotnet` and a stub `bayan` CLI at the repo's build path (no dotnet needed),
# the way guard.test.sh stubs them, and a fixture tasks file: --list derives each file's filter from the *Tests.cs classes of
# the tasks naming it (sorted union, a glob class skipped, a class of another family dropped, the family default when none is left, a bare basename when unique, a {a,b} path) and falls back to the
# directory default, an unknown directory fails naming it; files sharing a filter share one recording; --shard k/n and --files
# limit the groups; the reports land in tmp/mutate-batch/<group>/; an empty baseline is filled, a higher score replaces a lower
# one, a drop fails naming the file with old and new score and leaves the baseline unchanged; a failed mutant 0, a missing CLI
# and a manifest-less recording fail naming the group, the other groups still raise the baseline; a failed group's reports are
# moved to rejected/ (a score drop moves every OK group's reports there too) and not matched by the merge job's glob; the copied reports' absolute
# paths (as bayan mutate writes them) are rewritten repo-root-relative, a path outside the root fails the group naming the
# report; .github/workflows/mutation.yml: read-only permissions, no persisted credentials, a concurrency group, shards upload
# reports and not the baseline, a final job needing the shards merges all reports in one update, uploads the accepted reports, and documents applying the run.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/mutate-batch-test"; aux="$root/tmp/mutate-batch-test-aux"
rm -rf "$r" "$aux" && mkdir -p "$r/selfhost/semantics" "$r/selfhost/parser" "$r/tests" "$aux/rep" "$aux/bin" && cd "$r"
for f in semantics/a semantics/b semantics/c parser/p; do printf 'fn f() -> int {\n    return 1\n}\n' > selfhost/$f.byn; done
cat > tasks.md <<'T'
# Tasks
- [x] T1 One — files: `a.byn`, `selfhost/semantics/c.byn`, `SelfhostSemanticsATests.cs`, `SelfhostParserXTests.cs` (not `Other.cs`) — covers: R1 — after: none
  - files: `selfhost/parser/p.byn` and `ZTests.cs` here is a detail line, not a task line
- [x] T2 Two — files: `selfhost/semantics/{b,c}.byn`, `SelfhostSemanticsCTests.cs`, `SelfhostSemanticsBTests.cs`, `SelfhostSemanticsATests.cs`, `SelfhostSemanticsT*Tests.cs` — covers: R1
- [x] T3 Three — files: `selfhost/parser/p.byn`, `SelfhostSemanticsATests.cs` — covers: R1
T
printf '{ "version": 1, "files": {} }\n' > tests/quality-baseline.json
cp tests/quality-baseline.json "$aux/empty.json"
cat > $aux/dotnet <<'STUB'
#!/bin/bash
[ "$1" = test ] || exit 0
echo "Total tests: 1  Passed: 1"
echo "$BAYAN_SNAPSHOT_FILES ${@: -3:1}" >> $AUX/record.txt
bn=$(basename "${BAYAN_SNAPSHOT_FILES%%,*}")
[ -f $AUX/nomanifest ] || { mkdir -p "$BAYAN_SNAPSHOT" $AUX/bin/$bn; b=$(printf '{"binary": "%s", "sites": "%s"}' $AUX/bin/$bn/prog $AUX/bin/$bn/prog.sites.json)
  [ -f $AUX/twobuilds ] && b="$b, $(printf '{"binary": "%s", "sites": "%s"}' $AUX/bin/$bn/prog2 $AUX/bin/$bn/prog2.sites.json)"
  echo "{\"version\": 1, \"builds\": [$b]}" > "$BAYAN_SNAPSHOT/manifest.json"; }
exit 0
STUB
mkdir -p src/Bayan.Cli/bin/Debug/net10.0
cat > src/Bayan.Cli/bin/Debug/net10.0/bayan <<'STUB'
#!/bin/bash
bn=$(basename "$(dirname "$3")")
prog=$(basename "$3")
[ -f $AUX/rep/$bn-$prog.mismatch ] || [ -f $AUX/rep/$bn.mismatch ] && { echo "bayan: mutant 0 differs" >&2; exit 1; }
cat $AUX/rep/$bn.txt; cp $AUX/rep/$bn.json "$(dirname "$3")/bayan-quality.json"
exit 0
STUB
chmod +x $aux/dotnet src/Bayan.Cli/bin/Debug/net10.0/bayan; export AUX=$aux PINNED_BAYAN=$PWD/src/Bayan.Cli/bin/Debug/net10.0/bayan
# rep <file> <killed> <survived> <marked>: the stub report and summary of the build recorded for <file>
rep() { local bn; bn=$(basename "$1")
  echo "mutation: $2 killed, $3 survived, 0 timeout, $4 marked, 0 not reached, 0 not emitted" > $aux/rep/$bn.txt
  python3 -I -c 'import json, os, sys
f, k, s, m = sys.argv[1], *map(int, sys.argv[2:])
ms = [{"site": {"file": f}, "status": st} for st, n in (("killed", k), ("survived", s), ("marked", m)) for _ in range(n)]
print(json.dumps({"version": 1, "kind": "mutation", "files": [{"path": os.environ.get("REPPATH", os.getcwd() + "/" + f), "mutants": ms}]}))' "$1" "$2" "$3" "$4" > $aux/rep/$bn.json; }
rep_all() { rep selfhost/semantics/a.byn "$@"; rep selfhost/semantics/b.byn "$@"; rep selfhost/parser/p.byn "$@"; }
b() { PATH="$aux:$PATH" bash "$root/scripts/selfhost/mutate-batch.sh" --tasks tasks.md "$@" 2>&1 || true; }
score() { python3 -I -c 'import json, sys; print(json.load(open("tests/quality-baseline.json"))["files"].get(sys.argv[1], {}).get("mutation"))' "$1"; }
list=$(b --list)
A='FullyQualifiedName~SelfhostSemanticsATests'; B="$A|FullyQualifiedName~SelfhostSemanticsBTests|FullyQualifiedName~SelfhostSemanticsCTests"
touch selfhost/semantics/z.byn; mkdir selfhost/other; echo > selfhost/other/z.byn; badlist=$(b --list); rm -r selfhost/other selfhost/semantics/z.byn
[ -f $aux/record.txt ] && listran=1 || listran=0
# fill: an empty baseline gets an entry for every file
rep_all 3 1 1; fill=$(b); rec=$(sort $aux/record.txt); fa=$(score selfhost/semantics/a.byn); fb=$(score selfhost/semantics/b.byn); fp=$(score selfhost/parser/p.byn)
[ -f tmp/mutate-batch/semantics-a/quality-1-prog.json ] && report=1 || report=0
relpath=$(python3 -I -c 'import json; print(json.load(open("tmp/mutate-batch/semantics-a/quality-1-prog.json"))["files"][0]["path"])')
# rise: a higher score replaces a lower one, and a lower one fails
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": 0.5, "coverage": null, "branches": null } } }\n' > tests/quality-baseline.json
rep_all 4 0 0; rm $aux/record.txt; rise=$(b --files selfhost/semantics/a.byn); ra=$(score selfhost/semantics/a.byn); rrec=$(cat $aux/record.txt)
# drop: fails naming the file, baseline unchanged (b's rise in the same run does not get written either)
printf '{ "version": 1, "files": { "selfhost/semantics/a.byn": { "mutation": 1.0, "coverage": null, "branches": null } } }\n' > tests/quality-baseline.json
cp tests/quality-baseline.json $aux/before.json
rep selfhost/semantics/a.byn 1 1 0; rep selfhost/semantics/b.byn 4 0 0; set +e; PATH="$aux:$PATH" bash "$root/scripts/selfhost/mutate-batch.sh" --tasks tasks.md > $aux/drop.txt 2>&1; dropx=$?; set -e; drop=$(cat $aux/drop.txt)
cmp -s tests/quality-baseline.json $aux/before.json && dropsame=1 || dropsame=0
dropleft=$(find tmp/mutate-batch -name 'quality-[0-9]*.json' -not -path '*/rejected/*'); droprej=$(find tmp/mutate-batch -path '*/rejected/rejected-quality-[0-9]*.json' | wc -l | tr -d ' ')
# shards over the 3 groups (p, a, b+c): 1/2 takes groups 1 and 3, 2/2 group 2
rep_all 2 0 0; rm $aux/record.txt; s1=$(b --shard 1/2 | grep '^== group' | cut -d' ' -f3 | tr '\n' ' '); s2=$(b --shard 2/2 | grep '^== group' | cut -d' ' -f3 | tr '\n' ' ')
badshard=$(b --shard 3/2)
# a failed mutant 0 names its group; the other groups still raise the baseline
cp $aux/empty.json tests/quality-baseline.json; touch $aux/rep/b.byn.mismatch; set +e; PATH="$aux:$PATH" bash "$root/scripts/selfhost/mutate-batch.sh" --tasks tasks.md > $aux/m0.txt 2>&1; m0x=$?; set -e; m0=$(cat $aux/m0.txt); rm $aux/rep/b.byn.mismatch
m0a=$(score selfhost/semantics/a.byn); m0b=$(score selfhost/semantics/b.byn)
touch $aux/twobuilds $aux/rep/b.byn-prog2.mismatch; set +e
PATH="$aux:$PATH" bash "$root/scripts/selfhost/mutate-batch.sh" --tasks tasks.md --files selfhost/semantics/b.byn > $aux/m0_2.txt 2>&1
m0_2x=$?; set -e; m0_2=$(cat $aux/m0_2.txt); rm -f $aux/twobuilds $aux/rep/b.byn-prog2.mismatch
fail_matches=$(find tmp/mutate-batch/semantics-b -name 'quality-[0-9]*.json')
fail_rej=$(find tmp/mutate-batch/semantics-b/rejected -name 'rejected-quality-[0-9]*.json' 2>/dev/null || true)
REPPATH=/elsewhere/p.byn rep selfhost/parser/p.byn 1 0 0; outside=$(b --files selfhost/parser/p.byn); rep selfhost/parser/p.byn 1 0 0
touch $aux/nomanifest; nomanifest=$(b --files selfhost/parser/p.byn); rm $aux/nomanifest
mv src/Bayan.Cli/bin/Debug/net10.0/bayan $aux/bayan; nocli=$(b --files selfhost/parser/p.byn); cd "$root"
wf=$(cat .github/workflows/mutation.yml)
rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
want="selfhost/parser/p.byn -> FullyQualifiedName~SelfhostParser
selfhost/semantics/a.byn -> $A
selfhost/semantics/b.byn -> $B
selfhost/semantics/c.byn -> $B"
[ "$list" = "$want" ] || fail "--list mapping differs (want a: SelfhostSemanticsATests; b, c: A|B|C sorted, glob class skipped; p: directory default)" "$list"
echo "$badlist" | grep -q 'selfhost/other/z.byn' && echo "$badlist" | grep -q 'no test family' || fail "an unknown directory was not a FAIL naming it" "$badlist"
[ $listran = 0 ] || fail "--list ran dotnet" ""
[ "$(echo "$rec" | wc -l | tr -d ' ')" = 3 ] || fail "files sharing a filter did not share one recording (want 3 recordings)" "$rec"
echo "$rec" | grep -qxF "selfhost/semantics/b.byn,selfhost/semantics/c.byn ($B)&Snapshot!=skip" || fail "b and c were not recorded together with their filter" "$rec"
echo "$fill" | grep -q '^mutation: 3 killed, 1 survived, 0 timeout, 1 marked' || fail "bayan mutate's summary was not printed" "$fill"
[ "$fa" = 0.75 ] && [ "$fb" = 0.75 ] && [ "$fp" = 0.75 ] || fail "an empty baseline was not filled (a=$fa b=$fb p=$fp)" "$fill"
[ $report = 1 ] || fail "the report was not written to tmp/mutate-batch/semantics-a/quality-1-prog.json" "$fill"
[ "$ra" = 1.0 ] || fail "a higher score did not replace the baseline (a=$ra)" "$rise"
[ "$rrec" = "selfhost/semantics/a.byn ($A)&Snapshot!=skip" ] || fail "--files a.byn did not record a's group" "$rrec"
echo "$drop" | grep -q '^baseline: mutation score of selfhost/semantics/a.byn fell from 1.0 to 0.5 ' || fail "a drop did not name the file with old and new score" "$drop"
[ $dropx != 0 ] && [ $dropsame = 1 ] || fail "a drop exited $dropx or changed the baseline" "$drop"
[ -z "$dropleft" ] && [ "$droprej" = 3 ] || fail "a refused check left accepted reports ($dropleft) or rejected $droprej, not 3" "$drop"
[ "$s1" = "parser-p semantics-b " ] && [ "$s2" = "semantics-a " ] || fail "--shard split the groups as '$s1' / '$s2'" ""
echo "$badshard" | grep -q 'want k/n' || fail "a bad --shard was accepted" "$badshard"
[ "$m0x" != 0 ] && [ "$m0a" = 1.0 ] && [ "$m0b" = None ] || fail "a failed mutant 0 exited $m0x (a=$m0a b=$m0b)" "$m0"
echo "$m0" | grep -q '^group semantics-b: mutation: the recording does not replay on the unmutated build' || fail "a failed mutant 0 did not name its group" "$m0"
echo "$nomanifest" | grep -q '^group parser-p: mutation: the recording wrote no tmp/mutate-batch/parser-p/snap/manifest.json' || fail "a manifest-less recording did not name its group" "$nomanifest"
echo "$nocli" | grep -q '^group parser-p: mutation: no pinned bayan — pinned-bayan: PINNED_BAYAN=.*/src/Bayan.Cli/bin/Debug/net10.0/bayan is not an executable' || fail "a missing CLI did not name its group" "$nocli"
[ "$relpath" = selfhost/semantics/a.byn ] || fail "the copied report's path was not rewritten repo-root-relative (got $relpath)" "$fill"
echo "$outside" | grep -q '^group parser-p: report: .*quality-1-prog.json: path /elsewhere/p.byn is not under the repo root' || fail "a report path outside the repo root did not fail the group naming the report" "$outside"
[ "$m0_2x" != 0 ] && [ -z "$fail_matches" ] && [ -n "$fail_rej" ] || fail "a failed group's report was matched by the merge job's glob ($fail_matches)" "$m0_2"
has() { echo "$wf" | grep -qE "$1" || fail "mutation.yml: $2" ""; }
has '^permissions:' "no workflow-level permissions"; has '^  contents: read$' "permissions are not contents: read"
[ "$(echo "$wf" | grep -c 'git fetch -q --depth=1 --no-tags "$GITHUB_SERVER_URL/$GITHUB_REPOSITORY.git" "$GITHUB_SHA"')" = 2 ] || fail "mutation.yml: both jobs need the tokenless shell checkout" ""
echo "$wf" | grep -qE 'uses: actions/checkout@' && fail "mutation.yml: actions/checkout is back (it persists a token)" ""
echo "$wf" | grep -qE '^  schedule:' && fail "mutation.yml: the nightly schedule is on before one recording per shard (Q910)" ""
has '^concurrency:' "no concurrency group"; has 'group: mutation-\$\{\{ github.ref \}\}' "the concurrency group is not mutation-\${{ github.ref }}"
has 'cancel-in-progress: false' "concurrency cancels runs in progress"
[ "$(echo "$wf" | grep -c 'name: quality-baseline-')" = 0 ] || fail "mutation.yml: a shard uploads its own tests/quality-baseline.json" ""
has '^    needs: mutate$' "no merge job needing the shard job"
has 'mapfile -t reports < <\(find reports' "the merge job collects no reports"
has 'quality-baseline.py check tests/quality-baseline.json \. "\$\{reports\[@\]\}"' "the merge job does not check the reports before updating"
has 'quality-baseline.py update tests/quality-baseline.json \. "\$\{reports\[@\]\}"' "the merge job runs no single update over all reports"
has 'Apply the run by `python3 -I scripts/selfhost/quality-baseline.py update tests/quality-baseline\.json \. <reports\.\.\.>`' "the header does not document applying the run by updating from the reports"
has 'on a chore/ branch off current dev, never by copying the baseline file' "the header does not warn against copying the baseline file"
has 'name: accepted-reports' "the merge job does not upload the accepted reports artifact"
has 'path: reports/\*\*/quality-\[0-9\]\*\.json' "the merge job reports upload path is not the accepted reports"
echo PASS
