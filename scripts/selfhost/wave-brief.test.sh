#!/bin/bash
# Smoke test (with the T28 review brief's paths and filter): the brief for T10 carries its task text, its 010-api.md entry points and a guard line on the given base.
set -e
cd "$(dirname "$0")/../.."
BAYAN_BASE=test-base python3 -I scripts/selfhost/wave-brief.py . T10 > /dev/null
b=tmp/010-t10/prompt.md
grep -q '^- \[.\] T10 ' $b || { echo "FAIL: task text missing"; exit 1; }
grep -q '^- T10 manifest.byn: ' $b || { echo "FAIL: api entry points missing"; exit 1; }
grep -q 'bash scripts/selfhost/guard.sh --base test-base ' $b || { echo "FAIL: guard line missing"; exit 1; }
grep '^bash scripts/selfhost/guard.sh' $b | grep -q -- '--mutate' && { echo "FAIL: guard line still mutates (decision 2026-11-01)"; exit 1; }
grep -q '## Bayan cheat sheet' $b || { echo "FAIL: cheat sheet missing"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T17 > /dev/null
grep -q "allow '[^']*,selfhost/parser/literals.byn," tmp/010-t17/prompt.md || { echo "FAIL: a selfhost/ path is not kept as is"; exit 1; }
grep -q "selfhost/semantics/selfhost/" tmp/010-t17/prompt.md && { echo "FAIL: selfhost/ path prefixed twice"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T28 > /dev/null
grep -q "allow '[^']*,docs/specs/010-self-hosted-semantics/010-api.md," tmp/010-t28/prompt.md || { echo "FAIL: a rooted docs/ path is not kept as is"; exit 1; }
grep -q "allow '[^']*,tests/cases/diag/generic-guard-mutate-rebind.diag," tmp/010-t28/prompt.md || { echo "FAIL: a rooted tests/ path is not kept as is"; exit 1; }
grep -q "tests/Bayan.Tests/tests/\|selfhost/semantics/tests/" tmp/010-t28/prompt.md && { echo "FAIL: rooted path prefixed"; exit 1; }
grep -q "^bash scripts/selfhost/guard.sh .*--filter 'FullyQualifiedName~SelfhostSemantics' " tmp/010-t28/prompt.md || { echo "FAIL: T28 guard does not cover every SelfhostSemantics class"; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T28 > /dev/null
grep -q "selfhost/semantics/selfhost/\|selfhost/semantics/tests/\|tests/Bayan.Tests/tests/" tmp/010-t28/review.md && { echo "FAIL: review brief prefixes a rooted path"; exit 1; }
grep -q '`tests/cases/diag/generic-guard-mutate-rebind.diag`' tmp/010-t28/review.md || { echo "FAIL: review brief drops a rooted tests/ path"; exit 1; }
grep -q 'filter "FullyQualifiedName~SelfhostSemantics"`' tmp/010-t28/review.md || { echo "FAIL: T28 review runs a test class that does not exist"; exit 1; }
grep -q "allow '[^']*,work/todo.md," tmp/010-t28/prompt.md || { echo "FAIL: T28 guard does not allow work/todo.md"; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T17 > /dev/null
grep -q '`selfhost/parser/literals.byn`' tmp/010-t17/review.md || { echo "FAIL: review brief does not keep a selfhost/ path as is"; exit 1; }
grep -q "selfhost/semantics/selfhost/" tmp/010-t17/review.md && { echo "FAIL: review brief prefixes a selfhost/ path twice"; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T31 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostSemanticsNoPanicLawTests"`' tmp/010-t31/review.md || { echo "FAIL: T31 review does not run its own test class"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T30 > /dev/null; python3 -I scripts/selfhost/review-brief.py . T30 > /dev/null
grep -q "guard.sh .* --filter 'Law=L022' --task T30" tmp/010-t30/prompt.md || { echo "FAIL: a law-enact task's guard does not run its law's proof"; exit 1; }
grep -q 'filter "Law=L022"`' tmp/010-t30/review.md || { echo "FAIL: a law-enact task's review does not run its law's proof"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T33 > /dev/null
grep -q "guard.sh --base [^ ]* --allow 'docs/bayan-handoff.md,CHANGELOG.md,work/todo.md,docs/specs/010-self-hosted-semantics/010-spec.md,docs/specs/010-self-hosted-semantics/010-tasks.md' --filter 'FullyQualifiedName~SelfhostSemanticsApiTests' --task T33" tmp/010-t33/prompt.md || { echo "FAIL: a docs task's guard lacks its root and spec files once each, or runs no test"; grep guard.sh tmp/010-t33/prompt.md; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T33 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostSemanticsApiTests"`' tmp/010-t33/review.md || { echo "FAIL: a docs task's review runs no test"; exit 1; }
# Row 011 (specs.tsv): bare .byn names resolve under selfhost/emitter/, the guard line takes the row's base, test class and --spec, the brief goes to tmp/011-<t>/.
python3 -I scripts/selfhost/wave-brief.py . T3 --spec 011 > /dev/null
grep -q "^bash scripts/selfhost/guard.sh --base feat/011-self-hosted-emitter --allow 'selfhost/emitter/lowerstmt.byn,selfhost/emitter/lower.byn,tests/Bayan.Tests/SelfhostEmitterT3Tests.cs,docs/bayan-handoff.md,docs/specs/011-self-hosted-emitter/011-tasks.md' --filter 'FullyQualifiedName~SelfhostEmitterT3Tests' --task T3 --spec 011\$" tmp/011-t3/prompt.md || { echo "FAIL: the 011 guard line"; grep '^bash scripts/selfhost/guard.sh' tmp/011-t3/prompt.md; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T3 --spec 011 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostEmitterT3Tests"`' tmp/011-t3/review.md || { echo "FAIL: the 011 review filter"; exit 1; }
# every test class a 011 task owns is in its guard and review filter; T21 also runs the SelfhostSemantics* classes (Q870)
python3 -I scripts/selfhost/wave-brief.py . T6 --spec 011 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/011-t6/prompt.md | grep -q -- "--filter 'FullyQualifiedName~SelfhostEmitterT6Tests|FullyQualifiedName~SelfhostEmitterT8Tests' " || { echo "FAIL: a second owned test class is not in the guard filter"; grep '^bash scripts/selfhost/guard.sh' tmp/011-t6/prompt.md; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T6 --spec 011 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostEmitterT6Tests|FullyQualifiedName~SelfhostEmitterT8Tests"`' tmp/011-t6/review.md || { echo "FAIL: a second owned test class is not in the review filter"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T21 --spec 011 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/011-t21/prompt.md | grep -q -- "--filter 'FullyQualifiedName~SelfhostEmitterT21Tests|FullyQualifiedName~SelfhostSemantics' " || { echo "FAIL: the T21 guard does not count the SelfhostSemantics tests"; exit 1; }
# T13 (integration) also runs every SelfhostEmitter* class (Q886); T14 owns law pages and tests, and every one of its test classes gates it
python3 -I scripts/selfhost/wave-brief.py . T13 T14 --spec 011 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/011-t13/prompt.md | grep -q -- "|FullyQualifiedName~SelfhostEmitter' " || { echo "FAIL: the T13 guard does not count every SelfhostEmitter class"; exit 1; }
grep '^bash scripts/selfhost/guard.sh' tmp/011-t14/prompt.md | grep -q -- "--filter 'FullyQualifiedName~SelfhostEmitterEquivalenceLawTests|FullyQualifiedName~SelfhostEmitterNoPanicLawTests|FullyQualifiedName~SelfhostEmitterSpeedTests' " || { echo "FAIL: a task with law pages and tests is not gated by every test class"; grep '^bash scripts/selfhost/guard.sh' tmp/011-t14/prompt.md; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T14 --spec 011 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostEmitterEquivalenceLawTests|FullyQualifiedName~SelfhostEmitterNoPanicLawTests|FullyQualifiedName~SelfhostEmitterSpeedTests"`' tmp/011-t14/review.md || { echo "FAIL: the T14 review does not run every test class"; exit 1; }
# a 011 law-enact task line without covers: (T17) still yields its law's proof as the guard filter.
python3 -I scripts/selfhost/wave-brief.py . T17 --spec 011 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/011-t17/prompt.md | grep -q -- "--allow 'docs/laws/024-emitter-equivalence.md,.* --filter 'Law=L024' " || { echo "FAIL: a task line without covers: has no guard line"; grep '^bash scripts/selfhost/guard.sh' tmp/011-t17/prompt.md; exit 1; }
# a .txt file in a task's files (T12's tests/windows-known-failures.txt) is in its allow list.
python3 -I scripts/selfhost/wave-brief.py . T12 --spec 011 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/011-t12/prompt.md | grep -q ",tests/windows-known-failures.txt," || { echo "FAIL: a .txt task file is not in the guard's allow list"; grep '^bash scripts/selfhost/guard.sh' tmp/011-t12/prompt.md; exit 1; }
# Row 010 is byte-for-byte what it was before specs.tsv: the briefs of the commit before the row table (8dae45cc) against this tree's, for T43
# (skipped when that commit is not in this clone, e.g. a depth-1 checkout).
old=8dae45cc
if git cat-file -e $old:scripts/selfhost/wave-brief.py 2> /dev/null; then
  mkdir -p tmp/wave-brief-old
  here="$PWD/scripts/selfhost"
  git show $old:scripts/selfhost/wave-brief.py | sed "s|^HERE = .*|HERE = '$here'|" > tmp/wave-brief-old/wave-brief.py
  git show $old:scripts/selfhost/review-brief.py > tmp/wave-brief-old/review-brief.py
  python3 -I tmp/wave-brief-old/wave-brief.py . T43 > /dev/null; python3 -I tmp/wave-brief-old/review-brief.py . T43 > /dev/null
  cp tmp/010-t43/prompt.md tmp/wave-brief-old/prompt.md; cp tmp/010-t43/review.md tmp/wave-brief-old/review.md
  python3 -I scripts/selfhost/wave-brief.py . T43 > /dev/null; python3 -I scripts/selfhost/review-brief.py . T43 > /dev/null
  cmp -s tmp/wave-brief-old/prompt.md tmp/010-t43/prompt.md || { echo "FAIL: the 010 T43 writer brief changed"; exit 1; }
  cmp -s tmp/wave-brief-old/review.md tmp/010-t43/review.md || { echo "FAIL: the 010 T43 review brief changed"; exit 1; }
  rm -rf tmp/wave-brief-old
fi
bash -n scripts/selfhost/guard.sh
echo PASS
