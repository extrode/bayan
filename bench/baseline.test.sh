#!/usr/bin/env bash
# Tests for bench/baseline.sh: usage and failure paths.
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected '$3', got '$2'"; fail=1; fi; }

bash bench/baseline.sh --bogus >/dev/null 2>&1; check "unknown argument exits 2" "$?" 2
bash bench/baseline.sh --help >/dev/null 2>&1; check "--help exits 0" "$?" 0

d="tmp/bench-test-baseline-$$"
mkdir -p "$d"
BAYAN_BIN="$d/missing" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" bash bench/baseline.sh >/dev/null 2>&1
check "missing bayan exits 1" "$?" 1
printf '#!/bin/sh\nexit 1\n' > "$d/bayan"
chmod +x "$d/bayan"
BAYAN_BIN="$d/bayan" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" bash bench/baseline.sh >/dev/null 2>&1
check "failing clean build exits 1" "$?" 1
check "no result file after a failure" "$(ls "$d/results" | wc -l | tr -d ' ')" 0
exit $fail
