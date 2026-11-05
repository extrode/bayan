#!/usr/bin/env bash
# bench/friction.sh: counts in selfhost/**/*.byn of `unsafe`, `@allow`, calls to `...Unchecked` members and
# `@unformatted` (occurrences on lines that are not comment-only), and of the BYN50xx diagnostics the bayan
# compiler reports on the self-hosted sources (each carries a fix). Writes bench/results/<commit>-friction.txt.
# Feeds the round-4 reevaluation of particles and naming rules. Not a CI gate.
#
#     bash bench/friction.sh
#
# Environment: BAYAN_BIN (default tmp/bench/publish/bayan, published on demand), BENCH_TMP (default tmp/bench),
# BENCH_RESULTS (default bench/results), FRICTION_SRC (default selfhost; a directory of *.byn to count in).
set -u

usage() { echo "usage: bash bench/friction.sh   (environment: BAYAN_BIN, BENCH_TMP, BENCH_RESULTS, FRICTION_SRC)" >&2; }
case "${1:-}" in
    "") ;;
    -h|--help) usage; exit 0 ;;
    *) echo "bench/friction.sh: unexpected argument '$1'" >&2; usage; exit 2 ;;
esac

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1
T=${BENCH_TMP:-tmp/bench}
results=${BENCH_RESULTS:-bench/results}
src=${FRICTION_SRC:-selfhost}
commit=$(git rev-parse --short HEAD 2>/dev/null || echo nogit)
W="$T/friction-$commit-$$"
mkdir -p "$W" "$results"
[ -d "$src" ] || { echo "bench/friction.sh: no such directory: $src" >&2; exit 1; }

bayan=${BAYAN_BIN:-$T/publish/bayan}
if [ ! -x "$bayan" ] && [ -z "${BAYAN_BIN:-}" ] && command -v dotnet >/dev/null 2>&1; then
    dotnet publish src/Bayan.Cli -c Release -r osx-arm64 -o "$T/publish" >"$W/publish.log" 2>&1
fi
[ -x "$bayan" ] || { echo "bench/friction.sh: no executable bayan at $bayan (set BAYAN_BIN, or run bench/run.sh first)" >&2; exit 1; }
bayan=$(cd "$(dirname "$bayan")" && pwd)/$(basename "$bayan")

# Occurrences of an extended regex on lines that are not comment-only.
count() { grep -rh --include='*.byn' -E "$1" "$src" | grep -v '^[[:space:]]*//' | grep -o -E "$1" | wc -l | tr -d ' '; }

# The compiler's package layout (see bench/baseline.sh): semantics as entry module, parser and lexer beside it.
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
"$bayan" check "$W/sh" --all >"$W/check.log" 2>&1
codes=$(grep -o -E '^(warning|error|note|info) BYN50[0-9][0-9]' "$W/check.log" | sed 's/^[a-z]* //' | sort | uniq -c | awk '{ printf "%s%s=%s", sep, $2, $1; sep = " " }')
total=$(grep -c -E '^(warning|error|note|info) BYN50[0-9][0-9]' "$W/check.log")

{
    echo "bayan friction $commit (counts in $src/**/*.byn)"
    echo "unsafe $(count '\bunsafe\b')"
    echo "@allow $(count '@allow\b')"
    echo "Unchecked_calls $(count '\.[A-Za-z0-9_]*Unchecked[[:space:]]*\(')"
    echo "@unformatted $(count '@unformatted\b')"
    echo "BYN50xx_reported $total${codes:+ ($codes)}"
} | tee "$results/$commit-friction.txt"
