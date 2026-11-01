#!/bin/bash
# quality-baseline.py: check fails on a mutation or coverage score below the baseline and passes on an equal or higher one,
# a file with no entry, and a null on either side; update creates the file, adds new files, raises a score and keeps a
# higher one; report paths (absolute, or relative to the root) become root-relative keys.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
d="$root/tmp/quality-baseline-test"; rm -rf "$d" && mkdir -p "$d/w"
q() { python3 -I "$root/scripts/selfhost/quality-baseline.py" "$@"; }
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
# mutation report: <file> <killed> <survived> [timeout] [marked]
mut() { python3 -I -c 'import json, sys
f, k, s, t, m = sys.argv[1], *map(int, sys.argv[2:])
ms = [{"site": {"file": f}, "status": st} for st, n in (("killed", k), ("survived", s), ("timeout", t), ("marked", m)) for _ in range(n)]
print(json.dumps({"version": 1, "kind": "mutation", "files": [{"path": f, "mutants": ms}]}))' "$1" "$2" "$3" "${4:-0}" "${5:-0}"; }
cov() { printf '{"version": 1, "kind": "coverage", "files": [{"path": "%s", "lines": {"covered": %s, "total": %s, "missed": []}}]}\n' "$1" "$2" "$3"; }
B=$d/baseline.json
printf '{ "version": 1, "files": { "a.byn": { "mutation": 0.75, "coverage": 0.5 }, "n.byn": { "mutation": null, "coverage": null } } }\n' > $B
mut a.byn 2 1 > $d/drop.json; mut a.byn 2 1 1 > $d/equal.json; mut "$d/w/a.byn" 4 0 0 3 > $d/abs.json
mut b.byn 1 1 > $d/new.json; mut n.byn 0 5 > $d/null.json; cov a.byn 1 4 > $d/covdrop.json; mut m.byn 0 0 0 2 > $d/marked.json
out=$(q check $B "$d/w" $d/drop.json) && fail "a mutation drop passed" "$out"
[ "$out" = "baseline: mutation score of a.byn fell from 0.75 to 0.6667 ($B)" ] || fail "drop message" "$out"
out=$(q check $B "$d/w" $d/equal.json) || fail "an equal score (timeout counts as killed) failed" "$out"
out=$(q check $B "$d/w" $d/abs.json) || fail "a higher score from an absolute path failed" "$out"
out=$(q check $B "$d/w" $d/new.json) || fail "a file with no baseline entry failed" "$out"
out=$(q check $B "$d/w" $d/null.json) || fail "a null baseline score failed" "$out"
out=$(q check $B "$d/w" $d/marked.json) || fail "a null new score (only marked mutants) failed" "$out"
out=$(q check $B "$d/w" $d/drop.json $d/abs.json) || fail "two reports on one file were not summed ((2+4)/(6+1) > 0.75)" "$out"
out=$(q check $B "$d/w" $d/covdrop.json) && fail "a coverage drop passed" "$out"
echo "$out" | grep -q 'coverage score of a.byn fell from 0.5 to 0.25' || fail "coverage drop message" "$out"
q update $B "$d/w" $d/abs.json $d/new.json
got=$(python3 -I -c 'import json, sys; f = json.load(open(sys.argv[1]))["files"]; print(f["a.byn"], f["b.byn"], f["n.byn"])' $B)
[ "$got" = "{'mutation': 1.0, 'coverage': 0.5} {'mutation': 0.5, 'coverage': None} {'mutation': None, 'coverage': None}" ] || fail "update did not raise a.byn and add b.byn" "$got"
q update $B "$d/w" $d/drop.json
got=$(python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1]))["files"]["a.byn"]["mutation"])' $B)
[ "$got" = 1.0 ] || fail "a lower score replaced a higher baseline" "$got"
q update $d/fresh.json "$d/w" $d/new.json
got=$(python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1])))' $d/fresh.json)
[ "$got" = "{'version': 1, 'files': {'b.byn': {'mutation': 0.5, 'coverage': None}}}" ] || fail "update did not create a missing baseline" "$got"
rm -rf "$d"
echo PASS
