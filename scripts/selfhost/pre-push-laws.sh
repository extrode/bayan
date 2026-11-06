#!/bin/bash
# pre-push-laws.sh — the full tier of every enacted law (docs/laws/*.md with `status: enacted`): BAYAN_LAW_TIER=full runs a proof's
# whole corpus where the quick tier (unset, what `laws-check.js --run` and the merge gate run) runs a fixed seeded subset (spec 011 D11).
# Run it from the repo root before pushing; the laws-nightly workflow runs it too. It never pushes.
#   bash scripts/selfhost/pre-push-laws.sh
set -u
cd "$(dirname "$0")/../.."
failed=()
for f in docs/laws/0*.md; do
  [ "$(sed -n 's/^status: *//p' "$f" | head -1)" = enacted ] || continue
  id=$(sed -n 's/^id: *//p' "$f" | head -1)
  echo "== $id (full tier)"
  BAYAN_LAW_TIER=full dotnet test -v q --filter "Law=$id" || failed+=("$id")
done
if [ ${#failed[@]} = 0 ]; then echo "All enacted laws hold (full tier)."; exit 0; fi
echo "LAWS FAILED (full tier): ${failed[*]}"; exit 1
