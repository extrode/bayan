#!/usr/bin/env bash
# Tests for bench/run.sh: usage and failure paths, and the result file format (with a bayan stub that cannot build).
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected '$3', got '$2'"; fail=1; fi; }

bash bench/run.sh --bogus >/dev/null 2>&1; check "unknown argument exits 2" "$?" 2
bash bench/run.sh --help >/dev/null 2>&1; check "--help exits 0" "$?" 0
BENCH_RUNS=0 bash bench/run.sh >/dev/null 2>&1; check "BENCH_RUNS=0 exits 2" "$?" 2

d="tmp/bench-test-run-$$"
mkdir -p "$d"
printf '#!/bin/sh\nexit 1\n' > "$d/bayan"
chmod +x "$d/bayan"
out=$(BENCH_RUNS=1 BAYAN_BIN="$d/bayan" BENCH_TMP="$d/t" BENCH_RESULTS="$d/results" bash bench/run.sh 2>&1)
check "run with a failing bayan exits 0" "$?" 0
commit=$(git rev-parse --short HEAD)
f="$d/results/$commit.txt"
check "result file is <commit>.txt" "$([ -s "$f" ] && echo yes || echo no)" yes
check "result file equals the printed table" "$(cat "$f")" "$out"
check "header names the commit" "$(sed -n 1p "$f" | grep -c "^bayan bench $commit ")" 1
check "column line" "$(sed -n 2p "$f" | tr -s ' ')" "benchmark bayan c rust zig checksum"
for b in hash parse lexer; do check "row $b" "$(grep -c "^$b " "$f")" 1; done
check "bayan cell reports the failure" "$(grep -c "^hash .*n/a (build failed)" "$f")" 1
check "hash row has a ratio to C" "$(grep "^hash " "$f" | grep -c '1\.00x')" 1
check "hash checksum printed" "$(grep "^hash " "$f" | grep -c '12143377579992967301$')" 1
exit $fail
