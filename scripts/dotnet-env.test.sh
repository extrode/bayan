#!/usr/bin/env bash
# Every script under scripts/ that runs `dotnet test|build|run|publish` must set MSBUILDDISABLENODEREUSE=1 and
# DOTNET_CLI_USE_MSBUILD_SERVER=0, so MSBuild worker nodes and the build server do not outlive the run.
# Excluded: scripts/cleanup/ (one-off), *.md and *.py (not run as shell), this file, scripts/dist.sh (not yet converted),
# scripts/ci/known-failures.test.sh (only greps CI's workflow for the text), and comment lines.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bad=0
while IFS= read -r f; do
  case "$f" in
    "$root"/scripts/cleanup/*|*.md|*.py|"$root"/scripts/dotnet-env.test.sh|"$root"/scripts/dist.sh|"$root"/scripts/ci/known-failures.test.sh) continue ;;
  esac
  grep -vE '^[[:space:]]*#' "$f" | grep -qE 'dotnet (test|build|run|publish)' || continue
  for v in MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0; do
    grep -q "$v" "$f" || { echo "FAIL: ${f#"$root"/} runs dotnet but does not set $v" >&2; bad=1; }
  done
done < <(find "$root/scripts" -type f \( -name '*.sh' -o -name '*.js' \) | sort)
[ "$bad" = 0 ] || exit 1
echo PASS
