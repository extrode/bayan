#!/usr/bin/env bash
# Prints the path of the pinned bayan: the CLI built from the tip of `dev` (every commit there is a merge), cached by commit
# in <main checkout>/.cache/bayan/<sha>/. Gates and tooling run this binary, never the working tree's (spec 015 handoff,
# decision T2), so a broken compiler change cannot disable the gate meant to catch it. The compiler's own tests still use
# the working tree. A failed build exits 1 naming the commit and the build log; there is no fallback.
# Usage: pinned-bayan.sh
#   PINNED_BAYAN_REF  the ref to pin (default dev; CI's mutation run, checked out at the dev commit it measures, passes HEAD)
#   PINNED_BAYAN      tests only: the binary to print instead; it must be executable
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0  # no MSBuild worker or build server outlives the build

if [ -n "${PINNED_BAYAN:-}" ]; then
  [ -x "$PINNED_BAYAN" ] || { echo "pinned-bayan: PINNED_BAYAN=$PINNED_BAYAN is not an executable" >&2; exit 1; }
  echo "$PINNED_BAYAN"; exit 0
fi
ref="${PINNED_BAYAN_REF:-dev}"
sha="$(git rev-parse -q --verify "$ref^{commit}")" || { echo "pinned-bayan: no commit for '$ref'" >&2; exit 1; }
main="$(cd "$(git rev-parse --git-common-dir)/.." && pwd)"  # shared by every worktree of the repo
cache="$main/.cache/bayan/$sha"
exe=bayan; case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) exe=bayan.exe ;; esac
# CACHEDIR.TAG marks .cache/bayan as a disposable cache (worktree removal, backups skip it): written under a temporary
# name and moved into place, on every run, and never fatal.
tag="$main/.cache/bayan/CACHEDIR.TAG"
[ -f "$tag" ] || { mkdir -p "$(dirname "$tag")" && printf %s\\n "Signature: 8a477f597d28d172789f06886806bc55" "# This file is a cache directory tag created by pinned-bayan.sh." > "$tag.$$" && mv -f "$tag.$$" "$tag"; } 2>/dev/null || rm -f "$tag.$$" 2>/dev/null || true
touch -c "$cache" 2>/dev/null || true  # mark the pin used before anything else can rank it for eviction
if [ -e "$cache" ] && [ ! -x "$cache/bin/$exe" ]; then
  echo "pinned-bayan: $cache exists but holds no bin/$exe; remove it and rerun" >&2; exit 1
fi
if [ ! -x "$cache/bin/$exe" ]; then
  # Built from a fresh `git clone --shared` checked out at the commit: the caller's tree, index and worktrees are never
  # touched, and the CLI's version line (git rev-parse HEAD in the project) names the pinned commit.
  build="$main/.cache/bayan/.build-$sha-$$"
  mkdir -p "$build/src"
  trap 'rm -rf "$build"; exit 1' INT TERM
  log="$build/build.log"
  if ! { git clone -q --shared --no-checkout "$main" "$build/src" && git -C "$build/src" checkout -q --detach "$sha" && dotnet build "$build/src/src/Bayan.Cli" -c Debug -o "$build/bin" -v q --nologo -p:UseSharedCompilation=false; } > "$log" 2>&1; then
    echo "pinned-bayan: building bayan at $sha ($ref) failed; log: $log" >&2; exit 1
  fi
  rm -rf "$build/src"  # only bin/ is used (the version is stamped at build time); nothing reads the checkout afterwards
  mkdir -p "$(dirname "$cache")"
  # A concurrent build of the same commit may have won: then mv nests ours inside the cache (exit 0), so drop it.
  mv "$build" "$cache" 2>/dev/null || true
  [ -d "$cache/$(basename "$build")" ] && rm -rf "${cache:?}/$(basename "$build")"
  [ -d "$build" ] && rm -rf "$build"
  [ -x "$cache/bin/$exe" ] || { echo "pinned-bayan: could not move $build to $cache" >&2; exit 1; }
  trap - INT TERM
  # Keep the 3 most recently used commits (never the one just built, nor any used within 2 hours: another run may be
  # executing it); other builds' leftovers go only once an hour old.
  touch "$cache"
  n=0
  for d in $(ls -t "$main/.cache/bayan"); do
    case "$d" in .*|CACHEDIR.TAG) continue ;; CACHEDIR.TAG.*) [ -n "$(find "$main/.cache/bayan/$d" -mmin -120)" ] || rm -f "$main/.cache/bayan/$d"; continue ;; esac
    n=$((n+1)); [ "$n" -le 3 ] || [ "$d" = "$sha" ] || [ -n "$(find "$main/.cache/bayan/$d" -maxdepth 0 -mmin -120)" ] || rm -rf "${main:?}/.cache/bayan/$d"
  done
  find "$main/.cache/bayan" -maxdepth 1 -name '.build-*' -mmin +60 -exec rm -rf {} + 2>/dev/null || true
fi
touch -c "$cache"  # last-use order, which the eviction above follows
echo "$cache/bin/$exe"
