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
printf '#!/bin/sh\nexit 0\n' > "$d/bayan"
chmod +x "$d/bayan"
out=$(BAYAN_BIN="$d/bayan" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" FRICTION_SRC="$d/src" bash bench/friction.sh 2>&1)
check "run with a stub bayan exits 0" "$?" 0
f="$d/results/$(git rev-parse --short HEAD)-friction.txt"
check "result file equals the printed counts" "$(cat "$f")" "$out"
check "unsafe count" "$(grep '^unsafe ' "$f")" "unsafe 2"
check "@allow count" "$(grep '^@allow ' "$f")" "@allow 1"
check "Unchecked count" "$(grep '^Unchecked_calls ' "$f")" "Unchecked_calls 1"
check "@unformatted count" "$(grep '^@unformatted ' "$f")" "@unformatted 1"
check "BYN50xx line" "$(grep '^BYN50xx_reported ' "$f")" "BYN50xx_reported 0"
exit $fail
