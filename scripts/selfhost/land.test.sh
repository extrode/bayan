#!/bin/bash
# land.sh on a fixture repo with a stub wave-brief.py and guard.sh, run by /bin/bash (3.2 on macOS): it refuses a review
# file an earlier commit touched, a branch that changes scripts/selfhost/, and a branch that moves while it is checked; it
# runs the base checkout's guard by absolute path and lands a well-reviewed branch.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/land-test"; aux="$root/tmp/land-test-aux"
rm -rf "$r" "$aux" && mkdir -p "$r/scripts/selfhost" "$r/docs/specs/010-self-hosted-semantics/reviews" "$aux" && cd "$r"
git init -q -b base && git config user.email t@t && git config user.name t
printf 'import os, sys\nd = "tmp/010-" + sys.argv[2].lower()\nos.makedirs(d, exist_ok=True)\nopen(d + "/prompt.md", "w").write("bash scripts/selfhost/guard.sh --base x --filter F\\n")\n' > scripts/selfhost/wave-brief.py
# the stub guard logs the path it runs as; with $AUX/move it adds a commit to feat-move, as a writer still pushing would
cat > scripts/selfhost/guard.sh <<'STUB'
echo "$0" >> "$AUX/guard-ran.txt"
[ -f "$AUX/move" ] && git branch -f feat-move "$(git commit-tree -p HEAD -m "test: unchecked" 'HEAD^{tree}')"
echo GUARD PASS
STUB
printf '/tmp/\n' > .gitignore
printf -- '- [ ] T99 fixture\n' > docs/specs/010-self-hosted-semantics/010-tasks.md
git add -A && git commit -qm "test: base"
export AUX=$aux
REV=docs/specs/010-self-hosted-semantics/reviews/T99.md
review() { mkdir -p ${REV%/*}; printf '# T99 review\nCommit: %s\nVerdict: PASS\n' "$(git rev-parse HEAD)" > $REV; git add $REV; git commit -qm "test: review"; }
code() { git switch -q -c "$1" base; echo "$1" > "$1.txt"; git add -A; git commit -qm "test: code"; }
land() { /bin/bash "$root/scripts/selfhost/land.sh" T99 "$1" 2>&1 || true; }
code feat-forged; review; echo x >> feat-forged.txt; git commit -qam "test: code again"; review; git switch -q base
code feat-guard; echo '# rewritten' >> scripts/selfhost/guard.sh; git commit -qam "test: guard"; review; git switch -q base
code feat-move; review; git switch -q base
code feat-ok; review; git switch -q base
forged=$(land feat-forged); guard=$(land feat-guard)
touch $aux/move; moved=$(land feat-move); rm $aux/move; moved_in=$(git log --format=%s base | grep -c unchecked || true)
rm -f $aux/guard-ran.txt; ok=$(land feat-ok); ran=$(cat $aux/guard-ran.txt 2>/dev/null || true); ticked=$(grep -c '^- \[x\] T99 ' docs/specs/010-self-hosted-semantics/010-tasks.md || true)
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
echo "$forged" | grep -q 'LAND REFUSED: .*by no earlier commit' || fail "a review file an earlier commit touched was accepted" "$forged"
echo "$guard" | grep -q 'LAND REFUSED: feat-guard changes scripts/selfhost/' || fail "a branch that changes scripts/selfhost/ was accepted" "$guard"
echo "$moved" | grep -q 'LAND REFUSED: feat-move moved' || fail "a branch that moved during the guard was not refused" "$moved"
[ "$moved_in" = 0 ] || fail "the commit added during the guard was merged unchecked" "$moved"
echo "$ok" | grep -q '^LANDED T99$' || fail "a well-reviewed branch did not land under /bin/bash" "$ok"
[ "$ticked" = 1 ] || fail "the task was not ticked" "$ok"
[ "$ran" = "$r/scripts/selfhost/guard.sh" ] || fail "the guard ran as '$ran', not the base checkout's by absolute path" "$ok"
echo PASS
