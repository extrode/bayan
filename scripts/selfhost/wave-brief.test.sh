#!/bin/bash
# Smoke test (with the T28 review brief's paths and filter): the brief for T10 carries its task text, its 010-api.md entry points and a guard line on the given base.
set -e
cd "$(dirname "$0")/../.."
# an open task with no range gets one assigned (below); these runs must not record one in the main checkout's tmp/q-ranges.tsv
export BAYAN_QRANGES=$PWD/tmp/wave-brief-q-ranges.tsv; rm -f "$BAYAN_QRANGES"
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
# a task naming its test file only as tests/Bayan.Tests/X.cs (012 T4) is gated by that class, not by <prefix><n>Tests (Q945)
python3 -I scripts/selfhost/wave-brief.py . T4 --spec 012 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/012-T4/prompt.md | grep -q -- "--filter 'FullyQualifiedName~SelfhostDriverRuntimeTests' " || { echo "FAIL: a test file named with its directory does not gate the task"; exit 1; }
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
# a C runtime source named in a task line (012 T16, src/runtime/bayan-rt.c) is in its guard --allow
python3 -I scripts/selfhost/wave-brief.py . T16 --spec 012 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/012-t16/prompt.md | grep -q 'src/runtime/bayan-rt.h,src/runtime/bayan-rt.c,' || { echo "FAIL: a .c file is not in the guard --allow"; exit 1; }
# a top-level bootstrap/ file (012 T6) is in its guard --allow
python3 -I scripts/selfhost/wave-brief.py . T6 --spec 012 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/012-t6/prompt.md | grep -q 'bootstrap/bayan.c,bootstrap/bayan-rt.h,bootstrap/bayan-rt.c,bootstrap/README.md,' || { echo "FAIL: a bootstrap/ file is not in the guard --allow"; exit 1; }
# Question ranges (012 plan D6), on a fixture repo: an open task with no range in its line gets the five numbers after the highest
# Question in the handoff, any local branch's handoff, any tasks-file range and any range already in tmp/q-ranges.tsv; a second
# brief for it reuses the row; briefs written at once do not overlap; a range in the task line is kept and not recorded;
# a ticked task gets none; the 012 brief tells the writer to run preflight.sh, the 010 brief does not.
here=$PWD; q=$here/tmp/wave-brief-q; rm -rf "$q"; mkdir -p "$q/docs/specs/012-self-hosted-bootstrap" "$q/docs/specs/011-self-hosted-emitter"
( cd "$q"; git init -q -b base; git config user.email t@t; git config user.name t
  printf '1005. a\n1007. b\n' > docs/bayan-handoff.md
  printf -- '- [ ] T90 a — files: `x.byn` — covers: R1\n' > docs/specs/011-self-hosted-emitter/011-tasks.md
  : > docs/specs/012-self-hosted-bootstrap/012-plan.md
  for n in 91 92 93 94 95; do printf -- '- [ ] T%s t — files: `selfhost/driver/t%s.byn` — covers: R1\n' $n $n; done > docs/specs/012-self-hosted-bootstrap/012-tasks.md
  printf -- '- [ ] T96 t — files: `t.byn` — covers: R1 — Questions Q1100–Q1104\n- [x] T97 t — files: `t.byn` — covers: R1\n- [x] T98 t — files: `selfhost/semantics/{compile,types}.byn`, `x.byn` — covers: R1\n' >> docs/specs/012-self-hosted-bootstrap/012-tasks.md
  git add -A; git commit -qm "test: base"; git switch -q -c other; printf '1012. c\n' >> docs/bayan-handoff.md; git commit -qam "test: other"; git switch -q base )
export BAYAN_QRANGES=$q/tmp/q-ranges.tsv
python3 -I scripts/selfhost/wave-brief.py "$q" T96 --spec 012 > /dev/null
grep -q 'Questions Q1100–Q1104 only' "$q/tmp/012-t96/prompt.md" || { echo "FAIL: a range in the task line is not the brief's"; exit 1; }
[ ! -f "$BAYAN_QRANGES" ] || { echo "FAIL: a range in the task line was recorded"; exit 1; }
python3 -I scripts/selfhost/review-brief.py "$q" T96 --spec 012 > /dev/null
grep -q 'and Questions Q1100–Q1104 in' "$q/tmp/012-t96/review.md" || { echo "FAIL: the review brief does not read a 4-digit range"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py "$q" T91 --spec 012 > /dev/null
grep -q 'Questions Q1105–Q1109 only' "$q/tmp/012-t91/prompt.md" || { echo "FAIL: the range is not the block after the highest Question, branch or tasks range"; grep -o 'Questions Q[^ ]* only' "$q/tmp/012-t91/prompt.md"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py "$q" T91 --spec 012 > /dev/null
[ "$(grep -c '^012	T91	1105	1109$' "$BAYAN_QRANGES")" = 1 ] && grep -q 'Questions Q1105–Q1109 only' "$q/tmp/012-t91/prompt.md" || { echo "FAIL: a second brief does not reuse the recorded range"; cat "$BAYAN_QRANGES"; exit 1; }
python3 -I scripts/selfhost/review-brief.py "$q" T91 --spec 012 > /dev/null
grep -q 'and Questions Q1105–Q1109 in' "$q/tmp/012-t91/review.md" || { echo "FAIL: the review brief does not read the range from q-ranges.tsv"; exit 1; }
perl -i -pe 's/ — Questions Q1100–Q1104//' "$q/docs/specs/012-self-hosted-bootstrap/012-tasks.md"
python3 -I scripts/selfhost/wave-brief.py "$q" T92 --spec 012 > /dev/null
grep -q 'Questions Q1110–Q1114 only' "$q/tmp/012-t92/prompt.md" || { echo "FAIL: a range already recorded is not skipped"; exit 1; }
printf '1200. d\n' >> "$q/docs/bayan-handoff.md"
python3 -I scripts/selfhost/wave-brief.py "$q" T93 --spec 012 > /dev/null & python3 -I scripts/selfhost/wave-brief.py "$q" T94 --spec 012 > /dev/null &
python3 -I scripts/selfhost/wave-brief.py "$q" T95 --spec 012 > /dev/null & wait
r=$(awk -F'\t' '$2 ~ /^T9[345]$/ { print $3 "-" $4 }' "$BAYAN_QRANGES" | sort | tr '\n' ' ')
[ "$r" = "1201-1205 1206-1210 1211-1215 " ] || { echo "FAIL: briefs written at once overlap or skip ($r)"; cat "$BAYAN_QRANGES"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py "$q" T97 --spec 012 > /dev/null
grep -q 'Questions in your range only' "$q/tmp/012-t97/prompt.md" && ! grep -q 'T97' "$BAYAN_QRANGES" || { echo "FAIL: a ticked task got a range"; exit 1; }
grep -qF 'bash scripts/selfhost/preflight.sh 012 T91 "$(git rev-parse --abbrev-ref HEAD)"' "$q/tmp/012-t91/prompt.md" || { echo "FAIL: the 012 brief does not tell the writer to run preflight.sh"; exit 1; }
grep -q 'preflight.sh' tmp/010-t10/prompt.md && { echo "FAIL: the 010 brief names preflight.sh"; exit 1; }
# a brace list in a files list (012 T18) is one name per alternative in the guard's --allow (which splits on commas)
python3 -I scripts/selfhost/wave-brief.py "$q" T98 --spec 012 > /dev/null
grep '^bash scripts/selfhost/guard.sh' "$q/tmp/012-t98/prompt.md" | grep -q -- "--allow 'selfhost/semantics/compile.byn,selfhost/semantics/types.byn,selfhost/driver/x.byn," || { echo "FAIL: a brace list is not expanded in --allow"; grep '^bash scripts/selfhost/guard.sh' "$q/tmp/012-t98/prompt.md"; exit 1; }
rm -rf "$q"
# 012 T17 names its test files with their directory: its review runs those classes, not SelfhostDriverT17Tests (no such class);
# a file declaring another class (SelfhostDriverInventoryTests.cs holds SelfhostDriverT8Tests) is gated by that class
python3 -I scripts/selfhost/review-brief.py . T17 --spec 012 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostDriverRuntimeTests|FullyQualifiedName~StdEnvTests|FullyQualifiedName~StdFsTests"`' tmp/012-t17/review.md || { echo "FAIL: the 012 T17 review filter is not its files' classes"; grep -o 'filter "[^"]*"' tmp/012-t17/review.md; exit 1; }
python3 -I scripts/selfhost/review-brief.py . T8 --spec 012 > /dev/null
grep -q 'filter "FullyQualifiedName~SelfhostDriverT8Tests' tmp/012-t8/review.md || { echo "FAIL: the review filter is the file name, not the class it declares"; grep -o 'filter "[^"]*"' tmp/012-t8/review.md; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T8 --spec 012 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/012-t8/prompt.md | grep -q -- "--filter 'FullyQualifiedName~SelfhostDriverT8Tests'" || { echo "FAIL: the guard filter is the file name, not the class it declares"; grep '^bash scripts/selfhost/guard.sh' tmp/012-t8/prompt.md; exit 1; }
rm -f "$here/tmp/wave-brief-q-ranges.tsv"
# golden expected files named in a task line (014 T6) are in its guard --allow
python3 -I scripts/selfhost/wave-brief.py . T6 --spec 014 > /dev/null
grep '^bash scripts/selfhost/guard.sh' tmp/014-T6/prompt.md | grep -q 'tests/cases/run/path-full-path-empty.exitcode,.*tests/cases/emit/run-c.sha256' || { echo "FAIL: golden expected files are not in the guard --allow"; exit 1; }
bash -n scripts/selfhost/guard.sh
echo PASS
