#!/usr/bin/env bash
# The memory and disk release gate of docs/memory-leaks.md, run by .github/workflows/release.yml before any release
# is drafted, and by hand before packaging anything else (the VS Code extension and bayan-lsp have no workflow).
# Every command in the doc's *Release gate* block runs here; memory-gate.test.sh fails when the two drift apart.
# Exit 0 only when all of them pass; every failing command is named.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."
failed=()
run() { echo "memory-gate: $*" >&2; "$@" || failed+=("$*"); }

run dotnet test -v q --filter "FullyQualifiedName~LanguageServer"
run dotnet test -v q --filter "FullyQualifiedName~CliMemoryBounds"
run bash scripts/pinned-bayan.test.sh

if [ ${#failed[@]} -gt 0 ]; then
  printf 'memory-gate: FAILED: %s\n' "${failed[@]}" >&2
  exit 1
fi
echo "memory-gate: PASS"
