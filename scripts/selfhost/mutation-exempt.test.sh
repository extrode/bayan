#!/bin/bash
# mutation-exempt.py: a plain marker skips the next line, `through N` skips the span, and a marker whose Question
# cites no line of its span, or whose span runs backwards, skips nothing and is reported.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/mutation-exempt-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
printf 'void f() {\n    // mutation-exempt: Q901\n    if a == 1 { }\n    // mutation-exempt: Q902 through 6\n    if b == 1 { }\n    if c == 1 { }\n    if d == 1 { } // mutation-exempt: Q903 through 9\n    if e == 1 { }\n    if g == 1 { }\n    // mutation-exempt: Q901 through 2\n}\n' > a.byn
printf '901. cites a.byn:3.\n902. cites a.byn:5-6.\n903. cites a.byn:3 only.\n' > h.md
out=$(python3 -I "$root/scripts/selfhost/mutation-exempt.py" a.byn h.md)
rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
[ "$(echo "$out" | grep '^skip' | tr '\n' ' ')" = "skip 3 skip 5 skip 6 " ] || fail "skipped lines"
echo "$out" | grep -q 'a.byn:7 exempts 8-9 for Q903, but Q903 cites no a.byn line' || fail "uncited span not reported"
echo "$out" | grep -q 'a.byn:10 exempts through 2' || fail "backward span not reported"
echo PASS
