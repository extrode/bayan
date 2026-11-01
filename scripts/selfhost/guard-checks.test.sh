#!/bin/bash
# A Question citation fixed in the working tree after a commit that had it wrong is checked as it stands now.
# A cited directory path resolves exactly, a bare basename keeps the owned/semantics lookup, a missing path is reported.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/guard-checks-test"
rm -rf "$r" && mkdir -p "$r/selfhost/semantics" "$r/selfhost/parser" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t
printf 'void f() {\n    let x = 1\n}\n' > selfhost/semantics/a.byn
printf 'x\nlet y = 2\n' > selfhost/parser/e.byn
printf '# Handoff\n' > docs/bayan-handoff.md
git add -A && git commit -qm "test: base" && git branch -q base
printf '# Handoff\n900. T99 cites a.byn:9 `let x = 1`. Open.\n' > docs/bayan-handoff.md
git commit -qam "test: stale"
printf '# Handoff\n900. T99 cites a.byn:2 `let x = 1`. Open.\n- **T99** landed.\n' > docs/bayan-handoff.md
out=$(python3 -I "$root/scripts/selfhost/guard-checks.py" base T99 'selfhost/semantics/a.byn,docs/bayan-handoff.md' || true)
printf '# Handoff\n901. T99 cites `selfhost/parser/e.byn:2` `let y = 2`, a.byn:2 `let x = 1`, selfhost/parser/nope.byn:3.\n- **T99** landed.\n' > docs/bayan-handoff.md
out2=$(python3 -I "$root/scripts/selfhost/guard-checks.py" base T99 'selfhost/semantics/a.byn,docs/bayan-handoff.md' || true)
rm -rf "$r"
if echo "$out" | grep -q 'a.byn:9'; then echo "FAIL: stale committed citation reported"; exit 1; fi
if echo "$out" | grep -q 'Progress log'; then echo "FAIL: working-tree Progress log entry not seen"; exit 1; fi
if echo "$out2" | grep -q 'e.byn:2'; then echo "FAIL: directory-path citation not resolved exactly"; echo "$out2"; exit 1; fi
if echo "$out2" | grep -q ' a.byn:2'; then echo "FAIL: bare basename citation not resolved"; echo "$out2"; exit 1; fi
echo "$out2" | grep -q 'nope.byn:3, but selfhost/parser/nope.byn does not exist' || { echo "FAIL: missing cited path not reported"; echo "$out2"; exit 1; }
echo PASS
