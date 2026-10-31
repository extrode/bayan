#!/usr/bin/env bash
# C# mutation report (spec 013 R15): Stryker scoped to the named src/ files, converted to bayan-quality.json (kind "mutation").
# usage: mutate.sh [--base BRANCH] [--out FILE] [src/.../File.cs ...]
# With no file, the changed src/ files: git diff --name-only BASE...HEAD -- 'src/*.cs' (BASE defaults to dev).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
out="$repo/tmp/quality/bayan-quality.json"
base=dev
files=()
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    *) files+=("$1"); shift ;;
  esac
done
cd "$repo"
if [ ${#files[@]} -eq 0 ]; then
  while IFS= read -r f; do files+=("$f"); done < <(git diff --name-only "$base...HEAD" -- 'src/*.cs')
fi
if [ ${#files[@]} -eq 0 ]; then echo "mutate.sh: no src/ files to mutate" >&2; exit 1; fi

work="$repo/tmp/quality/stryker"
rm -rf "$work"
mkdir -p "$work" "$(dirname "$out")"
dotnet tool restore >&2

# Stryker mutates one project per run: group the files by their nearest csproj.
projects=()
for f in "${files[@]}"; do
  [ -f "$repo/$f" ] || { echo "mutate.sh: no such file: $f" >&2; exit 1; }
  d="$(dirname "$repo/$f")"
  while [ ! -f "$(ls "$d"/*.csproj 2>/dev/null | head -n 1)" ]; do
    [ "$d" != "$repo" ] && [ "$d" != "/" ] || { echo "mutate.sh: no csproj above $f" >&2; exit 1; }
    d="$(dirname "$d")"
  done
  projects+=("$(ls "$d"/*.csproj | head -n 1)")
done

start=$SECONDS
reports=()
n=0
for proj in $(printf '%s\n' "${projects[@]}" | sort -u); do
  mutate=()
  i=0
  for f in "${files[@]}"; do
    # Stryker globs are relative to the project directory
    [ "${projects[$i]}" = "$proj" ] && mutate+=(--mutate "**/${f#"$(dirname "${proj#"$repo/"}")/"}")
    i=$((i + 1))
  done
  n=$((n + 1))
  (cd "$repo/tests/Bayan.Tests" && dotnet tool run dotnet-stryker --project "$(basename "$proj")" "${mutate[@]}" \
    --reporter json --output "$work/$n" >&2)
  reports+=("$(find "$work/$n" -name 'mutation-report.json' | head -n 1)")
done
python3 -I "$here/stryker_to_quality.py" "$out" "$repo" 0 "$((SECONDS - start))" "${reports[@]}"
echo "wrote $out"
