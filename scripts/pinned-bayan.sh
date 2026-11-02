#!/usr/bin/env bash
# Prints the path of the pinned bayan: the CLI built from the tip of `dev` (every commit there is a merge), cached by commit
# in <main checkout>/.cache/bayan/<sha>/. Gates and tooling run this binary, never the working tree's (spec 015 handoff,
# decision T2), so a broken compiler change cannot disable the gate meant to catch it. The compiler's own tests still use
# the working tree. A failed build exits 1 naming the commit and the build log; there is no fallback.
# Usage: pinned-bayan.sh
#   PINNED_BAYAN_REF  the ref to pin (default dev; CI's mutation run, checked out at the dev commit it measures, passes HEAD)
#   PINNED_BAYAN      tests only: the binary to print instead; it must be executable
set -euo pipefail

if [ -n "${PINNED_BAYAN:-}" ]; then
  [ -x "$PINNED_BAYAN" ] || { echo "pinned-bayan: PINNED_BAYAN=$PINNED_BAYAN is not an executable" >&2; exit 1; }
  echo "$PINNED_BAYAN"; exit 0
fi
ref="${PINNED_BAYAN_REF:-dev}"
sha="$(git rev-parse -q --verify "$ref^{commit}")" || { echo "pinned-bayan: no commit for '$ref'" >&2; exit 1; }
main="$(cd "$(git rev-parse --git-common-dir)/.." && pwd)"  # shared by every worktree of the repo
cache="$main/.cache/bayan/$sha"
exe=bayan; case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) exe=bayan.exe ;; esac
if [ ! -x "$cache/bin/$exe" ]; then
  # Built from a fresh `git clone --shared` checked out at the commit: the caller's tree, index and worktrees are never
  # touched, and the CLI's version line (git rev-parse HEAD in the project) names the pinned commit.
  build="$main/.cache/bayan/.build-$sha-$$"
  mkdir -p "$build/src"
  log="$build/build.log"
  if ! { git clone -q --shared --no-checkout "$main" "$build/src" && git -C "$build/src" checkout -q --detach "$sha" && dotnet build "$build/src/src/Bayan.Cli" -c Debug -o "$build/bin" -v q --nologo; } > "$log" 2>&1; then
    echo "pinned-bayan: building bayan at $sha ($ref) failed; log: $log" >&2; exit 1
  fi
  mkdir -p "$(dirname "$cache")"
  mv "$build" "$cache" 2>/dev/null || [ -x "$cache/bin/$exe" ] || { echo "pinned-bayan: could not move $build to $cache" >&2; exit 1; }
fi
echo "$cache/bin/$exe"
