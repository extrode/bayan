#!/usr/bin/env bash
# bench/run.sh: the same hashing and parsing programs in Bayan, C, Rust and Zig, plus the self-hosted lexer
# (Bayan only), each timing its own work loop. Prints a table (median of BENCH_RUNS runs, ratio to C) and
# writes it to bench/results/<commit>.txt. Not a CI gate.
#
#     bash bench/run.sh
#
# Environment: BENCH_RUNS (default 5), BAYAN_BIN (default tmp/bench/publish/bayan, published on demand),
# BENCH_TMP (default tmp/bench), BENCH_RESULTS (default bench/results).
set -u

usage() { echo "usage: bash bench/run.sh   (environment: BENCH_RUNS, BAYAN_BIN, BENCH_TMP, BENCH_RESULTS)" >&2; }
case "${1:-}" in
    "") ;;
    -h|--help) usage; exit 0 ;;
    *) echo "bench/run.sh: unexpected argument '$1'" >&2; usage; exit 2 ;;
esac

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
runs=${BENCH_RUNS:-5}
case "$runs" in ''|*[!0-9]*|0) echo "bench/run.sh: BENCH_RUNS must be a positive integer" >&2; exit 2 ;; esac
T=${BENCH_TMP:-tmp/bench}
results=${BENCH_RESULTS:-bench/results}
bayan=${BAYAN_BIN:-$T/publish/bayan}
commit=$(git rev-parse --short HEAD 2>/dev/null || echo nogit)
mkdir -p "$T/bin" "$T/res" "$results"
export BAYAN_CACHE="$T/cache-run-$$"

# --- Bayan toolchain ---
bayan_note=""
if [ ! -x "$bayan" ]; then
    if command -v dotnet >/dev/null 2>&1; then
        dotnet publish src/Bayan.Cli -c Release -r osx-arm64 -o "$T/publish" >"$T/publish.log" 2>&1
    fi
    [ -x "$bayan" ] || bayan_note="n/a (no bayan: dotnet publish failed or dotnet missing)"
fi

# note <bench> <lang> <text>: the cell's explanation; res/<bench>.<lang> holds "<median ns> <checksum>" when it ran.
note() { echo "$3" > "$T/res/$1.$2.note"; }

# measure <bench> <lang> <cmd...>: runs the command $runs times, stores the median ns and the checksum.
measure() {
    local bench=$1 lang=$2; shift 2
    local i out ns sum first="" nss=""
    for ((i = 0; i < runs; i++)); do
        out=$("$@" 2>/dev/null) || { note "$bench" "$lang" "n/a (run failed)"; return; }
        ns=${out%% *}
        sum=${out#* }
        [ "$out" = "$ns" ] && sum=-
        if [ -z "$first" ]; then first=$sum; elif [ "$first" != "$sum" ]; then note "$bench" "$lang" "n/a (checksum varies between runs)"; return; fi
        nss="$nss$ns"$'\n'
    done
    echo "$(printf '%s' "$nss" | sort -n | sed -n "$(( (runs + 1) / 2 ))p") $first" > "$T/res/$bench.$lang"
    rm -f "$T/res/$bench.$lang.note"
}

for b in hash parse lexer; do for l in bayan c rust zig; do : > "$T/res/$b.$l"; rm -f "$T/res/$b.$l.note"; done; done

# --- build and run ---
for b in hash parse; do
    # Bayan
    if [ -n "$bayan_note" ]; then note $b bayan "$bayan_note"
    elif "$bayan" build "bench/programs/$b/main.byn" --mode fast -o "$T/bin/bayan-$b" >"$T/bin/bayan-$b.log" 2>&1; then measure $b bayan "$T/bin/bayan-$b"
    else note $b bayan "n/a (build failed)"; fi
    # C
    if ! command -v cc >/dev/null 2>&1; then note $b c "n/a (no cc)"
    elif cc -O2 "bench/programs/c/$b.c" -o "$T/bin/c-$b" 2>"$T/bin/c-$b.log"; then measure $b c "$T/bin/c-$b"
    else note $b c "n/a (build failed)"; fi
    # Rust
    if ! command -v rustc >/dev/null 2>&1; then note $b rust "n/a (no rustc)"
    elif rustc -O "bench/programs/rust/$b.rs" -o "$T/bin/rust-$b" 2>"$T/bin/rust-$b.log"; then measure $b rust "$T/bin/rust-$b"
    else note $b rust "n/a (build failed)"; fi
    # Zig
    if ! command -v zig >/dev/null 2>&1; then note $b zig "n/a (no zig)"
    elif zig build-exe "bench/programs/zig/$b.zig" -O ReleaseFast --cache-dir "$T/zig-cache" --global-cache-dir "$T/zig-global" -femit-bin="$T/bin/zig-$b" 2>"$T/bin/zig-$b.log"; then measure $b zig "$T/bin/zig-$b"
    else note $b zig "n/a (build failed)"; fi
done

# Lexer: selfhost/semantics/*.byn concatenated in name order (every file of the largest self-hosted module).
cat selfhost/semantics/*.byn > "$T/lexer-input.byn"
if [ -n "$bayan_note" ]; then note lexer bayan "$bayan_note"
elif "$bayan" build selfhost/lexer/main.byn --mode fast -o "$T/bin/bayan-lexer" >"$T/bin/bayan-lexer.log" 2>&1; then
    measure lexer bayan "$T/bin/bayan-lexer" --time "$T/lexer-input.byn"
else note lexer bayan "n/a (build failed)"; fi
for l in c rust zig; do note lexer $l "n/a (no port)"; done

# --- checksums must agree per benchmark ---
status=0
for b in hash parse; do
    sums=$(for l in bayan c rust zig; do [ -s "$T/res/$b.$l" ] && { read -r _ s < "$T/res/$b.$l"; echo "$s"; }; done | sort -u | wc -l | tr -d ' ')
    if [ "$sums" -gt 1 ]; then
        echo "bench/run.sh: checksums disagree for '$b':" >&2
        for l in bayan c rust zig; do [ -s "$T/res/$b.$l" ] && echo "  $l: $(cat "$T/res/$b.$l")" >&2; done
        status=1
    fi
done
[ $status -ne 0 ] && exit $status

# --- table ---
cell() { # <bench> <lang>
    local f="$T/res/$1.$2" ns sum cns
    if [ -s "$f" ]; then
        read -r ns sum < "$f"
        if [ -s "$T/res/$1.c" ]; then read -r cns _ < "$T/res/$1.c"; else cns=0; fi
        awk -v ns="$ns" -v c="$cns" 'BEGIN { if (c > 0) printf "%.1f ms (%.2fx)", ns / 1e6, ns / c; else printf "%.1f ms (n/a)", ns / 1e6 }'
    else
        cat "$T/res/$1.$2.note" 2>/dev/null || echo "n/a"
    fi
}
checksum() { local f s l; for l in c bayan rust zig; do f="$T/res/$1.$l"; if [ -s "$f" ]; then read -r _ s < "$f"; echo "$s"; return; fi; done; echo "-"; }

table() {
    echo "bayan bench $commit ($(uname -sm), median of $runs runs, ratio to C, lower is better)"
    printf '%-8s %-24s %-24s %-24s %-24s %s\n' benchmark bayan c rust zig checksum
    for b in hash parse lexer; do
        printf '%-8s %-24s %-24s %-24s %-24s %s\n' "$b" "$(cell $b bayan)" "$(cell $b c)" "$(cell $b rust)" "$(cell $b zig)" "$(checksum $b)"
    done
}
table | tee "$results/$commit.txt"
