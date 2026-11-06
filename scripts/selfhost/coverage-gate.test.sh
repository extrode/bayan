#!/bin/bash
# coverage-gate.py's coverage-exempt markers: a marker whose Question cites a line of its file exempts its span's missed
# lines and branches (`through `text`` a range to the one line holding text, Q1040), and the file gets one count line; a marker
# whose Question cites no line of its file, an anchor matching no line or two, and a `through N` line number are FAIL items; an unknown line under a marker and a missed line outside every marker stay FAIL items;
# a file absent from the work tree has no markers, so its missed line in a touched function stays a FAIL item.
# coverage-blocked markers (second fixture): one naming an open sibling task exempts its span and gets its own count line; one
# naming the guarded task or a ticked task is a FAIL item (also in a file outside the report) and exempts nothing; a malformed one
# is a FAIL item; without a tasks file every coverage-blocked marker is a FAIL item.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/coverage-gate-test"
rm -rf "$r" && mkdir -p "$r/selfhost" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t && git config core.hooksPath /dev/null
printf 'void f() {\n    a()\n}\n' > selfhost/a.byn; git add -A; git commit -q -m base
printf '\nvoid g() {\n    // coverage-exempt: Q1001\n    dead()\n    // coverage-exempt: Q902 through `y()`\n    x()\n    y()\n    // coverage-exempt: Q903\n    z()\n    live()\n    // coverage-exempt: Q902 through `nope()`\n    w()\n    // coverage-exempt: Q902 through `v()`\n    v()\n    v()\n    // coverage-exempt: Q902 through 20\n    u()\n}\nvoid h() {\n    y()\n}\n' >> selfhost/a.byn
printf '1001. cites a.byn:7.\n902. cites a.byn:9-10.\n903. cites b.byn:3 only.\n' > docs/bayan-handoff.md
a=$r/selfhost/a.byn
printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": 1, "total": 7, "missed": [7, 10, 12, 13], "unknown": [9]}, "branches": {"covered": 0, "total": 1, "missed": [{"line": 7, "column": 5, "outcome": true}], "unknown": []}, "errorPaths": []}, {"path": "%s", "lines": {"covered": 0, "total": 1, "missed": [2], "unknown": []}, "branches": {"covered": 0, "total": 0, "missed": [], "unknown": []}, "errorPaths": []}]}\n' "$a" "$r/selfhost/gone.byn" > cov.json
printf '{"version": 1, "files": {"%s": [{"span": [1, 3], "lines": [1, 1], "branches": [0, 0]}, {"span": [5, 21], "lines": [0, 6], "branches": [0, 1]}], "%s": [{"span": [1, 3], "lines": [0, 1], "branches": [0, 0]}]}}\n' "$a" "$r/selfhost/gone.byn" > fns.json
out=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json)
cd "$root"; rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
echo "$out" | grep -qE '^- coverage: missed (line selfhost/a.byn:(7|10)|branch selfhost/a.byn:7:)' && fail "an exempted miss was a FAIL item"
echo "$out" | grep -q '^- coverage: unknown line selfhost/a.byn:9; ' || fail "an unknown line under a marker was exempted"
echo "$out" | grep -q '^- coverage: missed line selfhost/a.byn:12; ' || fail "a marker whose Question cites none of its span exempted its line"
echo "$out" | grep -q '^- coverage: selfhost/a.byn:11 exempts 12-12 for Q903, but Q903 in docs/bayan-handoff.md cites no a.byn line' || fail "an uncited marker was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/a.byn:13; ' || fail "a missed line outside every marker was not a FAIL item"
echo "$out" | grep -q '^- coverage: selfhost/a.byn:14 exempts through anchor `nope()`, which matches no line of the rest of its function; ' || fail "an anchor matching no line was not a FAIL item naming it"
echo "$out" | grep -q '^- coverage: selfhost/a.byn:16 exempts through anchor `v()`, which matches lines 17, 18 of the rest of its function; ' || fail "an anchor matching two lines was not a FAIL item naming it"
echo "$out" | grep -q '^- coverage: selfhost/a.byn:19 pins `through` to a line number; ' || fail "a through-N marker was not a FAIL item"
echo "$out" | grep -qx 'coverage: selfhost/a.byn: 3 missed lines and branches under coverage-exempt markers' || fail "no count line for the exempted misses"
echo "$out" | grep -q '^- coverage: missed line .*selfhost/gone.byn:2; ' || fail "a missed line in a source file absent from the work tree was not a FAIL item"

rm -rf "$r" && mkdir -p "$r/selfhost/x" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t && git config core.hooksPath /dev/null
printf 'void f() {\n    a()\n}\n' > selfhost/b.byn; git add -A; git commit -q -m base
printf '\nvoid g() {\n    // coverage-blocked: Q904 T4 lowerExpr\n    p()\n    // coverage-blocked: Q905 T2 own\n    q()\n    // coverage-blocked: Q904 T1 done through `r()`\n    r()\n    s()\n    // coverage-blocked: Q904 lowerExpr\n    t()\n}\n' >> selfhost/b.byn
printf 'void h() {\n    // coverage-blocked: Q906 T2 own\n    u()\n}\n' > selfhost/x/c.byn
printf '904. cites b.byn:7 and b.byn:10-11 and b.byn:14.\n905. cites b.byn:9.\n906. cites c.byn:3.\n' > docs/bayan-handoff.md
printf -- '- [x] T1 one — done\n- [ ] T2 two — done\n- [ ] T4 four — done\n' > docs/tasks.md
b=$r/selfhost/b.byn
printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": 1, "total": 7, "missed": [7, 9, 11, 14], "unknown": []}, "branches": {"covered": 0, "total": 1, "missed": [{"line": 7, "column": 5, "outcome": false}], "unknown": []}, "errorPaths": []}]}\n' "$b" > cov.json
printf '{"version": 1, "files": {"%s": [{"span": [1, 3], "lines": [1, 1], "branches": [0, 0]}, {"span": [5, 16], "lines": [0, 6], "branches": [0, 1]}]}}\n' "$b" > fns.json
out=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json docs/tasks.md T2 selfhost)
bare=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json)
cd "$root"; rm -rf "$r"
echo "$out" | grep -qE '^- coverage: missed (line selfhost/b.byn:7;|branch selfhost/b.byn:7:)' && fail "a miss blocked on an open sibling task was a FAIL item"
echo "$out" | grep -qx 'coverage: selfhost/b.byn: 2 missed lines and branches under coverage-blocked markers' || fail "no count line for the blocked misses"
echo "$out" | grep -q '^- coverage: selfhost/b.byn:8 is coverage-blocked on T2 own, the task under guard; ' || fail "a marker naming the guarded task was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/b.byn:9; ' || fail "a marker naming the guarded task exempted its line"
echo "$out" | grep -q '^- coverage: selfhost/b.byn:10 is coverage-blocked on T1 done, but T1 is ticked' || fail "a marker naming a ticked task was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/b.byn:11; ' || fail "a marker naming a ticked task exempted its line"
echo "$out" | grep -q '^- coverage: selfhost/b.byn:13 has a coverage-blocked marker not of the form' || fail "a malformed marker was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/b.byn:14; ' || fail "a malformed marker exempted its line"
echo "$out" | grep -q '^- coverage: selfhost/x/c.byn:2 is coverage-blocked on T2 own, the task under guard; ' || fail "a stale marker in a file outside the report was not a FAIL item"
out=$bare
echo "$out" | grep -q '^- coverage: selfhost/b.byn:6 is coverage-blocked on T4 lowerExpr, but the gate was given no tasks file' || fail "a marker without a tasks file was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/b.byn:7; ' || fail "a marker without a tasks file exempted its line"

# (b) an 011 task editing selfhost/semantics (another row's bydir in specs.tsv): only its changed lines are gated (Q871);
# (c) its own selfhost/emitter file keeps the touched-function rule
rm -rf "$r" && mkdir -p "$r/selfhost/emitter" "$r/selfhost/semantics" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t && git config core.hooksPath /dev/null
printf 'void f() {\n    a()\n    b()\n}\n' > selfhost/semantics/d.byn; printf 'void k() {\n    a()\n}\n' > selfhost/emitter/e.byn; git add -A; git commit -q -m base
printf 'void f() {\n    a()\n    c()\n}\n' > selfhost/semantics/d.byn; printf 'void k() {\n    z()\n    a()\n}\n' > selfhost/emitter/e.byn
printf -- '- [ ] T2 two — done\n' > docs/tasks.md; : > docs/bayan-handoff.md
d=$r/selfhost/semantics/d.byn; e=$r/selfhost/emitter/e.byn
printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": 0, "total": 2, "missed": [2, 3], "unknown": []}, "branches": {"covered": 0, "total": 1, "missed": [{"line": 2, "column": 5, "outcome": true}], "unknown": []}, "errorPaths": []}, {"path": "%s", "lines": {"covered": 0, "total": 2, "missed": [2, 3], "unknown": []}, "branches": {"covered": 0, "total": 0, "missed": [], "unknown": []}, "errorPaths": []}]}\n' "$d" "$e" > cov.json
printf '{"version": 1, "files": {"%s": [{"span": [1, 4], "lines": [0, 2], "branches": [0, 1]}], "%s": [{"span": [1, 4], "lines": [0, 2], "branches": [0, 0]}]}}\n' "$d" "$e" > fns.json
out=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json docs/tasks.md T2 selfhost/emitter)
cd "$root"; rm -rf "$r"
echo "$out" | grep -q '^- coverage: missed line selfhost/semantics/d.byn:3; ' || fail "a changed line under another row's bydir was not gated"
echo "$out" | grep -qE '^- coverage: missed (line selfhost/semantics/d.byn:2;|branch selfhost/semantics/d.byn:2:)' && fail "an unchanged line under another row's bydir was gated"
echo "$out" | grep -qx 'coverage: selfhost/semantics/d.byn: outside selfhost/emitter, so only its changed lines are gated (Q871)' || fail "no info line for a file under another row's bydir"
echo "$out" | grep -q '^- coverage: missed line selfhost/emitter/e.byn:3; ' || fail "an unchanged line of a touched function inside <dir> was not gated"

# (a) a 010 task (dir selfhost/semantics) editing selfhost/parser, in no row's bydir: the touched-function rule, an unchanged line fails
rm -rf "$r" && mkdir -p "$r/selfhost/parser" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t && git config core.hooksPath /dev/null
printf 'void f() {\n    a()\n    b()\n}\n' > selfhost/parser/p.byn; git add -A; git commit -q -m base
printf 'void f() {\n    a()\n    c()\n}\n' > selfhost/parser/p.byn
printf -- '- [ ] T2 two — done\n' > docs/tasks.md; : > docs/bayan-handoff.md
p=$r/selfhost/parser/p.byn
printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": 0, "total": 2, "missed": [2, 3], "unknown": []}, "branches": {"covered": 0, "total": 0, "missed": [], "unknown": []}, "errorPaths": []}]}\n' "$p" > cov.json
printf '{"version": 1, "files": {"%s": [{"span": [1, 4], "lines": [0, 2], "branches": [0, 0]}]}}\n' "$p" > fns.json
out=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json docs/tasks.md T2 selfhost/semantics)
cd "$root"; rm -rf "$r"
echo "$out" | grep -q '^- coverage: missed line selfhost/parser/p.byn:2; ' || fail "an unchanged line of a touched function in selfhost/parser was not gated for 010"
echo "$out" | grep -q '^- coverage: missed line selfhost/parser/p.byn:3; ' || fail "a changed line in selfhost/parser was not gated for 010"
echo "$out" | grep -q 'Q871' && fail "selfhost/parser was called foreign"
echo PASS
