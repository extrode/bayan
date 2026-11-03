#!/bin/bash
# coverage-gate.py's coverage-exempt markers: a marker whose Question cites a line of its span exempts that span's missed
# lines and branches (`through N` a range), and the file gets one count line; a marker whose Question cites none of its span
# is a FAIL item and exempts nothing; an unknown line under a marker and a missed line outside every marker stay FAIL items.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/coverage-gate-test"
rm -rf "$r" && mkdir -p "$r/selfhost" "$r/docs" && cd "$r"
git init -q -b fixture && git config user.email t@t && git config user.name t && git config core.hooksPath /dev/null
printf 'void f() {\n    a()\n}\n' > selfhost/a.byn; git add -A; git commit -q -m base
printf '\nvoid g() {\n    // coverage-exempt: Q901\n    dead()\n    // coverage-exempt: Q902 through 10\n    x()\n    y()\n    // coverage-exempt: Q903\n    z()\n    live()\n}\n' >> selfhost/a.byn
printf '901. cites a.byn:7.\n902. cites a.byn:9-10.\n903. cites a.byn:3 only.\n' > docs/bayan-handoff.md
a=$r/selfhost/a.byn
printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": 1, "total": 7, "missed": [7, 10, 12, 13], "unknown": [9]}, "branches": {"covered": 0, "total": 1, "missed": [{"line": 7, "column": 5, "outcome": true}], "unknown": []}, "errorPaths": []}]}\n' "$a" > cov.json
printf '{"version": 1, "files": {"%s": [{"span": [1, 3], "lines": [1, 1], "branches": [0, 0]}, {"span": [5, 14], "lines": [0, 6], "branches": [0, 1]}]}}\n' "$a" > fns.json
out=$(python3 -I "$root/scripts/selfhost/coverage-gate.py" "$(git rev-parse HEAD)" cov.json fns.json)
cd "$root"; rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
echo "$out" | grep -qE '^- coverage: missed (line selfhost/a.byn:(7|10)|branch selfhost/a.byn:7:)' && fail "an exempted miss was a FAIL item"
echo "$out" | grep -q '^- coverage: unknown line selfhost/a.byn:9; ' || fail "an unknown line under a marker was exempted"
echo "$out" | grep -q '^- coverage: missed line selfhost/a.byn:12; ' || fail "a marker whose Question cites none of its span exempted its line"
echo "$out" | grep -q '^- coverage: selfhost/a.byn:11 exempts 12-12 for Q903, but Q903 in docs/bayan-handoff.md cites no a.byn line' || fail "an uncited marker was not a FAIL item"
echo "$out" | grep -q '^- coverage: missed line selfhost/a.byn:13; ' || fail "a missed line outside every marker was not a FAIL item"
echo "$out" | grep -qx 'coverage: selfhost/a.byn: 3 missed lines and branches under coverage-exempt markers' || fail "no count line for the exempted misses"
echo PASS
