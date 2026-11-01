#!/bin/bash
# land.sh on a fixture repo with a stub wave-brief.py and guard.sh, run by /bin/bash (3.2 on macOS): it refuses a last
# commit that is not a lone `docs(010): T99 review (PASS` commit, a branch that changes scripts/selfhost/, and a branch that
# moves while it is checked, one with no merge base, and a prompt with no guard line; the fixture path holds & and ' (the guard must still run); it runs the base checkout's guard by absolute path, without --mutate/--seed,
# and lands a two-round reviewed branch; tests/quality-baseline.json changes only when the guard run left a mutation report, then in the tick commit.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/land&test'q"; aux="$root/tmp/land-test-aux"
rm -rf "$r" "$aux" && mkdir -p "$r/scripts/selfhost" "$r/docs/specs/010-self-hosted-semantics/reviews" "$aux" && cd "$r"
git init -q -b base && git config user.email t@t && git config user.name t
printf 'import os, sys\nd = "tmp/010-" + sys.argv[2].lower()\nos.makedirs(d, exist_ok=True)\nopen(d + "/prompt.md", "w").write("" if os.environ.get("NOGUARD") else "bash scripts/selfhost/guard.sh --base x --mutate a.byn --seed 3 --filter F\\n")\n' > scripts/selfhost/wave-brief.py
# the stub guard logs the path it runs as; with $AUX/move it adds a commit to feat-move, as a writer still pushing would
cat > scripts/selfhost/guard.sh <<'STUB'
echo "$0" >> "$AUX/guard-ran.txt"; echo "$*" > "$AUX/guard-args.txt"
[ -f "$AUX/report" ] && mkdir -p tmp/guard-run && cp "$AUX/report" tmp/guard-run/quality-1-prog.json
[ -f "$AUX/move" ] && git branch -f feat-move "$(git commit-tree -p HEAD -m "test: unchecked" 'HEAD^{tree}')"
echo GUARD PASS
STUB
printf '/tmp/\n' > .gitignore
printf -- '- [ ] T99 fixture\n' > docs/specs/010-self-hosted-semantics/010-tasks.md
mkdir tests; printf '{ "version": 1, "files": { "x.byn": { "mutation": 0.5, "coverage": null } } }\n' > tests/quality-baseline.json; cp tests/quality-baseline.json "$aux/baseline.json"
git add -A && git commit -qm "test: base"
export AUX=$aux
REV=docs/specs/010-self-hosted-semantics/reviews/T99.md
# review [verdict [subject]]: commit a review of HEAD
review() { mkdir -p ${REV%/*}; printf '# T99 review\nCommit: %s\nVerdict: %s\n' "$(git rev-parse HEAD)" "${1:-PASS}" > $REV; git add $REV
  git commit -qm "${2:-docs(010): T99 review (${1:-PASS}, round 1)}"; }
code() { git switch -q -c "$1" base; echo "$1" > "$1.txt"; git add -A; git commit -qm "test: code"; }
land() { /bin/bash "$root/scripts/selfhost/land.sh" T99 "$1" 2>&1 || true; }
code feat-chore; review PASS "chore(worker): T99 review (PASS, round 1)"; git switch -q base
code feat-extra; mkdir -p ${REV%/*}; printf '# T99 review\nCommit: %s\nVerdict: PASS\n' "$(git rev-parse HEAD)" > $REV; echo x >> feat-extra.txt
git add -A; git commit -qm "docs(010): T99 review (PASS, round 1)"; git switch -q base
code feat-guard; echo '# rewritten' >> scripts/selfhost/guard.sh; git commit -qam "test: guard"; review; git switch -q base
code feat-move; review; git switch -q base
git switch -q --orphan feat-orphan; echo o > o.txt; git add -A; git commit -qm "test: orphan"; review; git switch -q -f base
code feat-ok; review FAIL; echo fix >> feat-ok.txt; git commit -qam "test: fix"; review PASS "docs(010): T99 review (PASS, round 2)"; git switch -q base
chore=$(land feat-chore); extra=$(land feat-extra); guard=$(land feat-guard); orphan=$(land feat-orphan)
noguard=$(NOGUARD=1 land feat-ok)
touch $aux/move; moved=$(land feat-move); rm $aux/move; moved_in=$(git log --format=%s base | grep -c unchecked || true)
rm -f $aux/guard-ran.txt; ok=$(land feat-ok); ran=$(cat $aux/guard-ran.txt 2>/dev/null || true); ticked=$(grep -c '^- \[x\] T99 ' docs/specs/010-self-hosted-semantics/010-tasks.md || true)
gargs=$(cat $aux/guard-args.txt); cmp -s tests/quality-baseline.json $aux/baseline.json && noreport=1 || noreport=0
# a second landing on top of the first (its review file then merges cleanly), its guard run leaving a report
code feat-rep; review; git switch -q base
printf '{"version": 1, "kind": "mutation", "files": [{"path": "x.byn", "mutants": [{"site": {}, "status": "killed"}]}]}\n' > $aux/report
rep=$(land feat-rep); raised=$(python3 -I -c 'import json; print(json.load(open("tests/quality-baseline.json"))["files"]["x.byn"]["mutation"])')
intick=$(git log -1 --format=%s -- tests/quality-baseline.json)
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
echo "$chore" | grep -q "LAND REFUSED: the branch's last commit must" || fail "a chore(worker) review commit was accepted" "$chore"
echo "$extra" | grep -q "LAND REFUSED: the branch's last commit must" || fail "a review commit touching another file was accepted" "$extra"
echo "$guard" | grep -q 'LAND REFUSED: feat-guard changes scripts/selfhost/' || fail "a branch that changes scripts/selfhost/ was accepted" "$guard"
echo "$orphan" | grep -q 'LAND REFUSED: cannot diff feat-orphan' || fail "a branch with no merge base was not refused" "$orphan"
echo "$noguard" | grep -q "LAND REFUSED: tmp/010-t99/prompt.md has no line starting 'bash scripts/selfhost/guard.sh'" || fail "a prompt with no guard line did not refuse with a message" "$noguard"
echo "$moved" | grep -q 'LAND REFUSED: feat-move moved' || fail "a branch that moved during the guard was not refused" "$moved"
[ "$moved_in" = 0 ] || fail "the commit added during the guard was merged unchecked" "$moved"
echo "$ok" | grep -q '^LANDED T99$' || fail "a well-reviewed branch did not land under /bin/bash" "$ok"
[ "$ticked" = 1 ] || fail "the task was not ticked" "$ok"
[ "$ran" = "$r/scripts/selfhost/guard.sh" ] || fail "the guard ran as '$ran', not the base checkout's by absolute path" "$ok"
[ "$gargs" = "--base base --filter F" ] || fail "the guard ran with '$gargs', not without --mutate and --seed" "$ok"
[ $noreport = 1 ] || fail "a landing without a mutation report changed tests/quality-baseline.json" "$ok"
echo "$rep" | grep -q '^LANDED T99$' && [ "$raised" = 1.0 ] || fail "a landing with a mutation report did not raise the baseline to 1.0 (got $raised)" "$rep"
[ "$intick" = "chore(010): tick T99" ] || fail "the baseline change is not in the tick commit (last commit touching it: $intick)" "$rep"
echo PASS
