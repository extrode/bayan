#!/usr/bin/env bash
# C# coverage report (spec 013 R15): dotnet test with coverlet, converted to bayan-quality.json (kind "coverage").
# usage: coverage.sh [--out FILE] [-- dotnet test args, e.g. --filter ...]
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0  # no MSBuild worker or build server outlives the run
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
out="$repo/tmp/quality/bayan-quality.json"
while [ $# -gt 0 ]; do
  case "$1" in
    --out) out="$2"; shift 2 ;;
    --) shift; break ;;
    *) echo "coverage.sh: unknown argument: $1" >&2; exit 2 ;;
  esac
done
results="$repo/tmp/quality/coverlet"
rm -rf "$results"
mkdir -p "$results" "$(dirname "$out")"
project="$repo/tests/Bayan.Tests/Bayan.Tests.csproj"

start=$SECONDS
dotnet build "$project" -v q --nologo >&2
built=$SECONDS
dotnet test "$project" --no-build --collect:"XPlat Code Coverage" --results-directory "$results" -v q --nologo "$@" >&2
cobertura="$(find "$results" -name 'coverage.cobertura.xml' | head -n 1)"
if [ -z "$cobertura" ]; then echo "coverage.sh: coverlet wrote no coverage.cobertura.xml" >&2; exit 1; fi
python3 -I "$here/cobertura_to_quality.py" "$cobertura" "$repo" "$out" "$((built - start))" "$((SECONDS - start))"
echo "wrote $out"
