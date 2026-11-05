#!/usr/bin/env bash
# Tests for bench/friction.sh: usage and failure paths, the counts on a small source tree, the result file format.
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected '$3', got '$2'"; fail=1; fi; }

bash bench/friction.sh --bogus >/dev/null 2>&1; check "unknown argument exits 2" "$?" 2
bash bench/friction.sh --help >/dev/null 2>&1; check "--help exits 0" "$?" 0

d="tmp/bench-test-friction-$$"
mkdir -p "$d/src"
FRICTION_SRC="$d/nope" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" bash bench/friction.sh >/dev/null 2>&1
check "missing source directory exits 1" "$?" 1
BAYAN_BIN="$d/missing" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" FRICTION_SRC="$d/src" bash bench/friction.sh >/dev/null 2>&1
check "missing bayan exits 1" "$?" 1

cat > "$d/src/a.byn" <<'SRC'
// unsafe in a comment does not count
unsafe void f() {}
@allow(x) unsafe void g() { x.readUnchecked() }
@unformatted
SRC
printf '#!/bin/sh\nexit 0\n' > "$d/silent"
printf '#!/bin/sh\necho boom >&2\nexit 134\n' > "$d/crash"
printf '#!/bin/sh\necho "warning BYN5014: x"\necho "◆ checked bench-selfhost in 0.1s"\nexit 2\n' > "$d/exit2"
chmod +x "$d/silent" "$d/crash" "$d/exit2"
for s in silent crash exit2; do
    BAYAN_BIN="$d/$s" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" FRICTION_SRC="$d/src" bash bench/friction.sh >/dev/null 2>"$d/err"
    check "stub '$s' is rejected" "$?" 1
    check "stub '$s' message names check.log" "$(grep -c 'check\.log' "$d/err")" 1
done
printf '#!/bin/sh\necho "warning BYN5014: x"\necho "◆ checked bench-selfhost in 0.1s"\nexit 0\n' > "$d/bayan"
chmod +x "$d/bayan"
out=$(BAYAN_BIN="$d/bayan" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" FRICTION_SRC="$d/src" bash bench/friction.sh 2>&1)
check "run with a stub bayan exits 0" "$?" 0
f="$d/results/$(git rev-parse --short HEAD)-friction.txt"
check "result file equals the printed counts" "$(cat "$f")" "$out"
check "unsafe count" "$(grep '^unsafe ' "$f")" "unsafe 2"
check "@allow count" "$(grep '^@allow ' "$f")" "@allow 1"
check "Unchecked count" "$(grep '^Unchecked_calls ' "$f")" "Unchecked_calls 1"
check "@unformatted count" "$(grep '^@unformatted ' "$f")" "@unformatted 1"
check "BYN50xx line" "$(grep '^BYN50xx_reported ' "$f")" "BYN50xx_reported (selfhost compiler package) 1 (BYN5014=1)"

# An absolute BENCH_TMP is used as is: its cache lands under it and nothing is created at $PWD$PWD/$d.
touch "$d/marker"; sleep 1
BAYAN_BIN="$d/bayan" BENCH_TMP="$PWD/$d/abs" BENCH_RESULTS="$d/results" FRICTION_SRC="$d/src" bash bench/friction.sh >/dev/null 2>&1
check "absolute BENCH_TMP exits 0" "$?" 0
check "cache under the absolute BENCH_TMP" "$(ls -d "$d"/abs/friction-*/cache 2>/dev/null | wc -l | tr -d ' ')" 1
check "nothing changed in the repo outside tmp/" "$(find . -path ./tmp -prune -o -path ./.git -prune -o -newer "$d/marker" -print | wc -l | tr -d ' ')" 0
check "nothing created at the doubled path" "$([ -e "$PWD$PWD/$d" ] && echo yes || echo no)" no
exit $fail
