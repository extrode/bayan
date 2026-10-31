#!/bin/bash
# Smoke test: the brief for T10 carries its task text, its 010-api.md entry points and a guard line on the given base.
set -e
cd "$(dirname "$0")/../.."
BAYAN_BASE=test-base python3 -I scripts/selfhost/wave-brief.py . T10 > /dev/null
b=tmp/010-t10/prompt.md
grep -q '^- \[.\] T10 ' $b || { echo "FAIL: task text missing"; exit 1; }
grep -q '^- T10 manifest.byn: ' $b || { echo "FAIL: api entry points missing"; exit 1; }
grep -q 'bash scripts/selfhost/guard.sh --base test-base ' $b || { echo "FAIL: guard line missing"; exit 1; }
grep -q '## Bayan cheat sheet' $b || { echo "FAIL: cheat sheet missing"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . T17 > /dev/null
grep -q "allow '[^']*,selfhost/parser/literals.byn," tmp/010-t17/prompt.md || { echo "FAIL: a selfhost/ path is not kept as is"; exit 1; }
grep -q "selfhost/semantics/selfhost/" tmp/010-t17/prompt.md && { echo "FAIL: selfhost/ path prefixed twice"; exit 1; }
bash -n scripts/selfhost/guard.sh
echo PASS
