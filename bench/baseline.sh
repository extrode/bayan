#!/usr/bin/env bash
# bench/baseline.sh: clean build time of the self-hosted compiler (selfhost/semantics, built by the bootstrap
# bayan with an empty BAYAN_CACHE), rebuild time after a one-file change (on a copy), and binary sizes of
# `bayan` and three goldens built by it. Writes bench/results/<commit>-baseline.txt. Not a CI gate.
#
#     bash bench/baseline.sh
#
# Environment: BAYAN_BIN (skip the NativeAOT publish and use this bayan; its size is still recorded),
# BENCH_TMP (default tmp/bench), BENCH_RESULTS (default bench/results).
set -u

usage() { echo "usage: bash bench/baseline.sh   (environment: BAYAN_BIN, BENCH_TMP, BENCH_RESULTS)" >&2; }
case "${1:-}" in
    "") ;;
    -h|--help) usage; exit 0 ;;
    *) echo "bench/baseline.sh: unexpected argument '$1'" >&2; usage; exit 2 ;;
esac

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
T=${BENCH_TMP:-tmp/bench}
results=${BENCH_RESULTS:-bench/results}
commit=$(git rev-parse --short HEAD 2>/dev/null || echo nogit)
W="$T/baseline-$commit-$$"
mkdir -p "$W" "$results"

bayan=${BAYAN_BIN:-}
if [ -z "$bayan" ]; then
    case "$(uname -s)-$(uname -m)" in
        Darwin-arm64) rid=osx-arm64 ;;
        Darwin-x86_64) rid=osx-x64 ;;
        Linux-x86_64) rid=linux-x64 ;;
        Linux-aarch64) rid=linux-arm64 ;;
        *) echo "bench/baseline.sh: no runtime identifier for $(uname -s)-$(uname -m)" >&2; exit 1 ;;
    esac
    command -v dotnet >/dev/null 2>&1 || { echo "bench/baseline.sh: dotnet not found (needed to publish bayan)" >&2; exit 1; }
    dotnet publish src/Bayan.Cli -c Release -r "$rid" -o "$W/publish" >"$W/publish.log" 2>&1 || { echo "bench/baseline.sh: publish failed, see $W/publish.log" >&2; exit 1; }
    bayan="$W/publish/bayan"
fi
[ -x "$bayan" ] || { echo "bench/baseline.sh: no executable bayan at $bayan" >&2; exit 1; }
bayan=$(cd "$(dirname "$bayan")" && pwd)/$(basename "$bayan")

size() { wc -c < "$1" | tr -d ' '; }
secs() { # <log> <cmd...>: wall seconds of the command, its output into <log>
    local log=$1 TIMEFORMAT=%R t; shift
    t=$( { time "$@" >"$log" 2>&1; } 2>&1 ) || return 1
    echo "$t"
}

# The compiler's package: semantics is the entry module, parser and lexer its siblings (the layout the
# bootstrap needs); `.fs` because the self-hosted sources read files.
mkdir -p "$W/sh/src/app"
cp selfhost/semantics/*.byn "$W/sh/src/app/"
cp -R selfhost/parser selfhost/lexer "$W/sh/src/"
cat > "$W/sh/package.byn" <<'PKG'
Package {
    name: "local/bench-selfhost",
    version: "0.1.0",
    edition: 2027,
    kind: .app,
    capabilities: [.fs],
    deps: [],
    dist: Dist {
        targets: [.host],
        mode: .safe,
    },
}
PKG

export BAYAN_CACHE="$PWD/$W/cache"
mkdir -p "$BAYAN_CACHE"
clean=$(secs "$W/clean.log" "$bayan" build "$W/sh" -o "$W/sem") || { echo "bench/baseline.sh: clean build failed, see $W/clean.log" >&2; exit 1; }
printf '\ni32 benchTouch() {\n    return 0\n}\n' >> "$W/sh/src/app/bigint.byn"
rebuild=$(secs "$W/rebuild.log" "$bayan" build "$W/sh" -o "$W/sem2") || { echo "bench/baseline.sh: rebuild failed, see $W/rebuild.log" >&2; exit 1; }

goldens="arithmetic collection-copy collection-literals"
sizes=""
for g in $goldens; do
    mkdir -p "$W/g-$g"
    cp "tests/cases/run/$g.byn" "$W/g-$g/main.byn"
    "$bayan" build "$W/g-$g/main.byn" -o "$W/g-$g/out" >"$W/g-$g/log" 2>&1 || { echo "bench/baseline.sh: golden '$g' failed to build, see $W/g-$g/log" >&2; exit 1; }
    sizes="$sizes$(printf 'size golden %s %s' "$g" "$(size "$W/g-$g/out")")"$'\n'
done

{
    echo "bayan baseline $commit ($(uname -sm))"
    echo "clean_build_selfhost_seconds $clean"
    echo "rebuild_one_file_seconds $rebuild"
    echo "size bayan $(size "$bayan")"
    printf '%s' "$sizes"
} | tee "$results/$commit-baseline.txt"
