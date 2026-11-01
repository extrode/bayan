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
bash -n scripts/selfhost/guard.sh
echo PASS
