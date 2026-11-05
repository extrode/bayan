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

# A stub bayan whose `build ... -o <out>` writes a 7-byte file: the result file format.
cat > "$d/stub" <<'STUB'
#!/bin/sh
while [ $# -gt 0 ]; do if [ "$1" = "-o" ]; then printf 'stub!!\n' > "$2"; fi; shift; done
exit 0
STUB
chmod +x "$d/stub"
out=$(BAYAN_BIN="$d/stub" BENCH_TMP="$d/t2" BENCH_RESULTS="$d/results2" bash bench/baseline.sh 2>&1)
check "run with a stub bayan exits 0" "$?" 0
f="$d/results2/$(git rev-parse --short HEAD)-baseline.txt"
check "result file equals the printed lines" "$(cat "$f")" "$out"
check "header names the commit" "$(sed -n 1p "$f" | grep -c "^bayan baseline $(git rev-parse --short HEAD) ")" 1
check "clean build line" "$(grep -c -E '^clean_build_selfhost_seconds [0-9]+\.[0-9]+$' "$f")" 1
check "rebuild line" "$(grep -c -E '^rebuild_one_file_seconds [0-9]+\.[0-9]+$' "$f")" 1
check "bayan size line" "$(grep -c -E '^size bayan [0-9]+$' "$f")" 1
check "three golden size lines" "$(grep -c -E '^size golden [a-z-]+ [0-9]+$' "$f")" 3

# An absolute BENCH_TMP is used as is: its cache lands under it and nothing is created at $PWD$PWD/$d.
BAYAN_BIN="$d/stub" BENCH_TMP="$PWD/$d/abs" BENCH_RESULTS="$d/results3" bash bench/baseline.sh >/dev/null 2>&1
check "absolute BENCH_TMP exits 0" "$?" 0
check "cache under the absolute BENCH_TMP" "$(ls -d "$d"/abs/baseline-*/cache 2>/dev/null | wc -l | tr -d ' ')" 1
check "nothing created at the doubled path" "$([ -e "$PWD$PWD/$d" ] && echo yes || echo no)" no
exit $fail
