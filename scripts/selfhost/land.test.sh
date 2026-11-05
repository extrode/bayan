#!/bin/bash
# land.sh on a fixture repo with a stub wave-brief.py and guard.sh, run by /bin/bash (3.2 on macOS): it refuses a last
# commit that is not a lone `docs(010): T99 review (PASS` commit, a branch that changes scripts/selfhost/ or edits tests/quality-baseline.json, and a branch that
# moves while it is checked, one with no merge base, and a prompt with no guard line; the fixture path holds & and ' (the guard must still run); it runs the base checkout's guard by absolute path, without --mutate/--seed,
# refuses before any merge when the base checkout's preflight.sh fails (and passes it the spec, task, branch and BAYAN_BASE), and lands a two-round reviewed branch; tests/quality-baseline.json changes only when the guard run left its coverage report
# (tmp/guard-run/coverage/bayan-quality.json, the only place the real guard writes one; no quality-*.json): a landing with
# none leaves the baseline alone, one fills coverage and branches, one with a higher coverage raises it (both in the tick commit),
# a lower filtered coverage lands and leaves the recorded value unchanged, and a malformed report is refused before the merge.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/land&test'q"; aux="$root/tmp/land-test-aux"
rm -rf "$r" "$aux" && mkdir -p "$r/scripts/selfhost" "$r/docs/specs/010-self-hosted-semantics/reviews" "$aux" && cd "$r"
git init -q -b base && git config user.email t@t && git config user.name t
printf 'import os, sys\nd = "tmp/010-" + sys.argv[2].lower()\nos.makedirs(d, exist_ok=True)\nopen(d + "/prompt.md", "w").write("" if os.environ.get("NOGUARD") else "bash scripts/selfhost/guard.sh --base x --mutate a.byn --seed 3 --filter F\\n")\n' > scripts/selfhost/wave-brief.py
# the stub guard logs the path it runs as; with $AUX/move it adds a commit to feat-move, as a writer still pushing would
cat > scripts/selfhost/guard.sh <<'STUB'
echo "$0" >> "$AUX/guard-ran.txt"; echo "$*" > "$AUX/guard-args.txt"
[ -f "$AUX/covreport" ] && mkdir -p tmp/guard-run/coverage && cp "$AUX/covreport" tmp/guard-run/coverage/bayan-quality.json
[ -f "$AUX/move" ] && git branch -f feat-move "$(git commit-tree -p HEAD -m "test: unchecked" 'HEAD^{tree}')"
echo GUARD PASS
STUB
# the stub preflight logs its arguments and the base it was given; with $AUX/preflight-fail it fails
printf 'echo "$* BAYAN_BASE=$BAYAN_BASE" > "$AUX/preflight-args.txt"\n[ -f "$AUX/preflight-fail" ] && { echo "FAIL cites: stub"; exit 1; }\necho "ok cites"\n' > scripts/selfhost/preflight.sh
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
code feat-baseline; echo '{}' > tests/quality-baseline.json; git commit -qam "test: baseline"; review; git switch -q base
code feat-move; review; git switch -q base
git switch -q --orphan feat-orphan; echo o > o.txt; git add -A; git commit -qm "test: orphan"; review; git switch -q -f base
code feat-ok; review FAIL; echo fix >> feat-ok.txt; git commit -qam "test: fix"; review PASS "docs(010): T99 review (PASS, round 2)"; git switch -q base
chore=$(land feat-chore); extra=$(land feat-extra); guard=$(land feat-guard); orphan=$(land feat-orphan)
noguard=$(NOGUARD=1 land feat-ok)
touch $aux/preflight-fail; prehead=$(git rev-parse HEAD); pre=$(land feat-ok); prehead2=$(git rev-parse HEAD); rm $aux/preflight-fail
touch $aux/move; moved=$(land feat-move); rm $aux/move; moved_in=$(git log --format=%s base | grep -c unchecked || true)
rm -f $aux/guard-ran.txt; ok=$(land feat-ok); ran=$(cat $aux/guard-ran.txt 2>/dev/null || true); ticked=$(grep -c '^- \[x\] T99 ' docs/specs/010-self-hosted-semantics/010-tasks.md || true)
gargs=$(cat $aux/guard-args.txt); pargs=$(cat $aux/preflight-args.txt); cmp -s tests/quality-baseline.json $aux/baseline.json && noreport=1 || noreport=0
# further landings on top of the first (T99 unticked first, so each tick commit is not empty), the guard run leaving a coverage report
next() { code "$1"; review; git switch -q base
  perl -i -pe 's/^- \[x\] T99 /- [ ] T99 /' docs/specs/010-self-hosted-semantics/010-tasks.md; git commit -qam "test: untick T99"; }
covrep() { printf '{"version": 1, "kind": "coverage", "files": [{"path": "y.byn", "lines": {"covered": %s, "total": 4, "missed": []}, "branches": {"covered": %s, "total": 2, "missed": []}}]}\n' "$1" "$2" > $aux/covreport; }
ycov() { python3 -I -c 'import json; print(json.load(open("tests/quality-baseline.json"))["files"].get("y.byn"))'; }
next feat-fill; covrep 3 1
fill=$(land feat-fill); filled=$(ycov); intick=$(git log -1 --format=%s -- tests/quality-baseline.json)
next feat-rise; covrep 4 2; rise=$(land feat-rise); rose=$(ycov)
next feat-drop; covrep 1 2; cp tests/quality-baseline.json $aux/before-drop.json
drop=$(land feat-drop); dropped=$(ycov)
cmp -s tests/quality-baseline.json $aux/before-drop.json && dropsame=1 || dropsame=0
next feat-bad; echo '{' > $aux/covreport; badbase=$(git rev-parse HEAD)
bad=$(land feat-bad); badhead=$(git rev-parse HEAD); badtree=$(git status --porcelain --untracked-files=no)
cmp -s tests/quality-baseline.json $aux/before-drop.json && badsame=1 || badsame=0
rm -f $aux/covreport; baseline=$(land feat-baseline)  # last: if it were accepted, its merge must not break the cases above
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
echo "$chore" | grep -q "LAND REFUSED: the branch's last commit must" || fail "a chore(worker) review commit was accepted" "$chore"
echo "$extra" | grep -q "LAND REFUSED: the branch's last commit must" || fail "a review commit touching another file was accepted" "$extra"
echo "$guard" | grep -q 'LAND REFUSED: feat-guard changes scripts/selfhost/' || fail "a branch that changes scripts/selfhost/ was accepted" "$guard"
echo "$baseline" | grep -q 'LAND REFUSED: feat-baseline edits tests/quality-baseline.json; only land.sh and the batch mutation run write it' || fail "a branch that edits tests/quality-baseline.json was accepted" "$baseline"
echo "$orphan" | grep -q 'LAND REFUSED: cannot diff feat-orphan' || fail "a branch with no merge base was not refused" "$orphan"
echo "$noguard" | grep -q "LAND REFUSED: tmp/010-t99/prompt.md has no line starting 'bash scripts/selfhost/guard.sh'" || fail "a prompt with no guard line did not refuse with a message" "$noguard"
echo "$pre" | grep -q '^FAIL cites: stub$' && echo "$pre" | grep -q '^LAND REFUSED: preflight failed' && [ "$prehead" = "$prehead2" ] || fail "a failing preflight did not refuse before the merge" "$pre"
[ "$pargs" = "010 T99 feat-ok BAYAN_BASE=base" ] || fail "preflight ran with '$pargs', not the spec, task, branch and base" "$ok"
echo "$moved" | grep -q 'LAND REFUSED: feat-move moved' || fail "a branch that moved during the guard was not refused" "$moved"
[ "$moved_in" = 0 ] || fail "the commit added during the guard was merged unchecked" "$moved"
echo "$ok" | grep -q '^LANDED T99$' || fail "a well-reviewed branch did not land under /bin/bash" "$ok"
[ "$ticked" = 1 ] || fail "the task was not ticked" "$ok"
[ "$ran" = "$r/scripts/selfhost/guard.sh" ] || fail "the guard ran as '$ran', not the base checkout's by absolute path" "$ok"
[ "$gargs" = "--base base --filter F" ] || fail "the guard ran with '$gargs', not without --mutate and --seed" "$ok"
[ $noreport = 1 ] || fail "a landing without a coverage report changed tests/quality-baseline.json" "$ok"
echo "$fill" | grep -q '^LANDED T99$' && [ "$filled" = "{'mutation': None, 'coverage': 0.75, 'branches': 0.5}" ] || fail "a landing with a coverage report did not fill coverage and branches (y.byn: $filled)" "$fill"
[ "$intick" = "chore(010): tick T99" ] || fail "the baseline change is not in the tick commit (last commit touching it: $intick)" "$fill"
echo "$rise" | grep -q '^LANDED T99$' && [ "$rose" = "{'mutation': None, 'coverage': 1.0, 'branches': 1.0}" ] || fail "a landing with a higher coverage did not raise the baseline (y.byn: $rose)" "$rise"
echo "$drop" | grep -q '^LANDED T99$' && [ "$dropped" = "{'mutation': None, 'coverage': 1.0, 'branches': 1.0}" ] && [ $dropsame = 1 ] || fail "a lower filtered coverage did not land leaving the recorded value unchanged (y.byn: $dropped)" "$drop"
echo "$bad" | grep -q '^LAND REFUSED: quality-baseline.py failed on ' && [ "$badhead" = "$badbase" ] && [ -z "$badtree" ] && [ $badsame = 1 ] \
  || fail "a malformed coverage report was not refused before the merge (head moved: $([ "$badhead" = "$badbase" ] && echo no || echo yes); dirty: $badtree)" "$bad"
echo PASS
