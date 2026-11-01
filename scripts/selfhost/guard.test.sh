#!/bin/bash
# guard.sh's mutation step on a tiny fixture with a stub `dotnet` (no dotnet needed): mutants are distinct (line, operator)
# pairs, a fixed --seed repeats a run exactly, every operator on a line is its own mutant, and a hung mutant run is killed
# with its whole process group and reported as a timeout.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/guard-test"; aux="$root/tmp/guard-test-aux"  # stub and logs live outside the fixture repo
rm -rf "$r" "$aux" && mkdir -p "$r/selfhost/semantics" "$r/docs" "$aux" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t
printf 'fn f(a: int) -> int {\n    if a == 1 && a != 2 { return 1 }\n    return a + 1\n}\n' > selfhost/semantics/a.byn
printf '# Handoff\n' > docs/bayan-handoff.md
git add -A && git commit -qm "test: base" && git branch -q base
printf '# Handoff\n- **T99** landed.\n' > docs/bayan-handoff.md
# stub (logs each mutant): build and the clean run pass; `a != 1` hangs (== mutant); `||` fails (&& mutant); other mutants survive
cat > $aux/dotnet <<'STUB'
#!/bin/bash
[ "$1" = test ] || exit 0
echo "Total tests: 1  Passed: 1"
f=selfhost/semantics/a.byn
cmp -s $f $AUX/orig.byn || diff $AUX/orig.byn $f | grep '^>' >> $AUX/mutants.txt
if grep -q 'a != 1' $f; then sleep 300 & echo $! > $AUX/pid.txt; wait
elif grep -q '||' $f; then echo "  Failed Bayan.Tests.X"; exit 1
fi
exit 0
STUB
chmod +x $aux/dotnet; cp selfhost/semantics/a.byn $aux/orig.byn; export AUX=$aux
g() { PATH="$aux:$PATH" GUARD_MUTANT_TIMEOUT_MIN=2 bash "$root/scripts/selfhost/guard.sh" --base base --allow 'selfhost/semantics/a.byn,tmp/*' \
  --filter 'FullyQualifiedName~SelfhostSemanticsT99Tests' --mutate selfhost/semantics/a.byn "$@" || true; }
out=$(g --mutations 4 --seed 5)
[ ! -f $AUX/pid.txt ] || ! kill -0 "$(cat $AUX/pid.txt)" 2>/dev/null || { echo "FAIL: hung mutant's child survived"; exit 1; }
rm -f $AUX/mutants.txt; g --mutations 2 --seed 7 > /dev/null; one=$(cat $AUX/mutants.txt); rm $AUX/mutants.txt
g --mutations 2 --seed 7 > /dev/null; two=$(cat $AUX/mutants.txt)
rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
echo "$out" | grep -q '^mutation seed: 5$' || fail "seed not printed"
echo "$out" | grep -q '^mutation selfhost/semantics/a.byn: 2/4 killed$' || fail "4 pairs (3 operators on line 2, 1 on line 3) not all tried"
echo "$out" | grep -q '^mutation timeout: selfhost/semantics/a.byn:2' || fail "timeout not reported"
echo "$out" | grep -q 'mutation survived: selfhost/semantics/a.byn:3' || fail "survivor on line 3 not reported"
[ "$one" = "$two" ] || { echo "FAIL: same seed, different mutants"; exit 1; }
echo PASS
