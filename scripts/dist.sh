#!/usr/bin/env bash
# Local compiler builds kept by target and version (dist/ is gitignored).
#   scripts/dist.sh [--rid <rid>]... [--label <version>] [--from <publish-dir> [--commit <sha>]] [--dist <dir>]
# Layout:
#   dist/<target>/<version>/bayan[.exe]   one directory per version, with BUILD-INFO
#   dist/<target>/latest/                 a copy of the most recent build for that target, replaced on every build
# Targets use the release names (scripts/release/package.sh): osx-arm64 -> macos-arm64, win-x64 -> windows-x64.
# <version> is Toolchain.CompilerVersion; a prerelease (0.2.0-dev) gets +<12-hex commit>, and a tree with
# any change not yet committed, untracked files included (the binary embeds docs/diagnostics too), gets .dirty:
# each commit has its own directory, a release keeps its plain number, and .dirty builds of one commit replace each other.
# NativeAOT does not cross-compile between operating systems: run this on each OS (mb1, wlt1, wp1), or publish
# elsewhere and import the binary with --from <dir> --rid <rid> (its label is this checkout's unless --label is given;
# its BUILD-INFO commit is "unknown" unless --commit names the commit it was built from).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lock=""
trap '[ -z "$lock" ] || rmdir "$lock" 2>/dev/null' EXIT
die() { echo "dist: $*" >&2; exit 1; }
usage() { sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

host_rid() {
  local arch
  case "$2" in arm64|aarch64) arch=arm64 ;; x86_64|amd64) arch=x64 ;; *) return 1 ;; esac
  case "$1" in
    Darwin) echo "osx-$arch" ;;
    Linux) echo "linux-$arch" ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-$arch" ;;
    *) return 1 ;;
  esac
}

# target <rid> -> "<target> <exe>"
target() {
  case "$1" in
    linux-x64|linux-arm64) echo "$1 bayan" ;;
    osx-x64|osx-arm64) echo "macos-${1#osx-} bayan" ;;
    win-x64|win-arm64) echo "windows-${1#win-} bayan.exe" ;;
    *) return 1 ;;
  esac
}

label() {
  local v sha dirty=""
  v="$(sed -nE 's/.*const string CompilerVersion = "([^"]+)";.*/\1/p' "$root/src/Bayan.Backend.C/Toolchain.cs")"
  [ -n "$v" ] || die "no CompilerVersion in src/Bayan.Backend.C/Toolchain.cs"
  [ -z "$(git -C "$root" status --porcelain 2>/dev/null)" ] || dirty=.dirty
  case "$v" in
    *-*) sha="$(git -C "$root" rev-parse --short=12 HEAD 2>/dev/null)" || die "a prerelease label needs a git checkout"
         echo "$v+$sha$dirty" ;;
    *) echo "$v$dirty" ;;
  esac
}

rids=() lbl="" from="" dist="$root/dist" commit=""
while [ $# -gt 0 ]; do
  case "$1" in
    --rid) [ $# -ge 2 ] || die "--rid needs a value"; rids+=("$2"); shift 2 ;;
    --label) [ $# -ge 2 ] || die "--label needs a value"; lbl="$2"; shift 2 ;;
    --from) [ $# -ge 2 ] || die "--from needs a value"; from="$2"; shift 2 ;;
    --dist) [ $# -ge 2 ] || die "--dist needs a value"; dist="$2"; shift 2 ;;
    --commit) [ $# -ge 2 ] || die "--commit needs a value"; commit="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; die "unexpected argument '$1'" ;;
  esac
done

if [ ${#rids[@]} -eq 0 ]; then
  [ -z "$from" ] || die "--from needs --rid (the binary's own target)"
  r="$(host_rid "$(uname -s)" "$(uname -m)")" || die "unknown host $(uname -s) $(uname -m); pass --rid"
  rids=("$r")
fi
[ -z "$from" ] || [ ${#rids[@]} -eq 1 ] || die "--from takes exactly one --rid"
[ -n "$dist" ] || die "--dist needs a directory"
[ -n "$lbl" ] || lbl="$(label)"
case "$lbl" in ''|.*|*/*|*\\*|*[[:space:]]*|latest) die "bad version label '$lbl'" ;; esac
# An imported binary was built elsewhere: this checkout's HEAD says nothing about it unless --commit names it.
if [ -z "$commit" ]; then
  if [ -n "$from" ]; then commit=unknown; else commit="$(git -C "$root" rev-parse HEAD 2>/dev/null || echo unknown)"; fi
fi

for rid in "${rids[@]}"; do
  t="$(target "$rid")" || die "unknown rid $rid"
  tgt="${t% *}" exe="${t#* }"
  # One run per target at a time, taken before publishing: two runs would share tmp/dist-publish/<rid> and obj/.
  mkdir -p "$dist/$tgt"
  lock="$dist/$tgt/.lock"
  mkdir "$lock" 2>/dev/null || { lock=""; die "another dist run holds $dist/$tgt/.lock (remove it if no run is going)"; }
  if [ -n "$from" ]; then
    pub="$from"
  else
    command -v dotnet >/dev/null || die "dotnet not on PATH"
    mkdir -p "$root/tmp"
    pub="$root/tmp/dist-publish/$rid"
    echo "dist: publishing $rid" >&2
    dotnet publish "$root/src/Bayan.Cli" -c Release -r "$rid" -o "$pub" >"$root/tmp/dist-publish-$rid.log" 2>&1 \
      || die "dotnet publish -r $rid failed, see tmp/dist-publish-$rid.log"
  fi
  [ -f "$pub/$exe" ] || die "$pub/$exe not found"

  dir="$dist/$tgt/$lbl"
  mkdir -p "$dir"
  cp "$pub/$exe" "$dir/$exe"
  chmod +x "$dir/$exe"
  printf 'version %s\ntarget %s\nrid %s\ncommit %s\nbuilt %s\nhost %s\nsource %s\n' \
    "$lbl" "$tgt" "$rid" "$commit" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(uname -n)" "${from:-dotnet publish}" > "$dir/BUILD-INFO"

  # latest: copy beside it, move the old one aside, move the copy in. A failed step leaves the old latest in place
  # (Windows refuses to move a folder holding a running bayan.exe).
  latest="$dist/$tgt/latest" next="$dist/$tgt/.latest-next" old="$dist/$tgt/.latest-old"
  rm -rf "$next" "$old" 2>/dev/null || die "cannot clear $old or $next (is a bayan from it running?); $dir is written, latest is not updated"
  cp -R "$dir" "$next"
  if [ -e "$latest" ] && ! mv "$latest" "$old"; then
    rm -rf "$next"; die "cannot replace $latest (is its bayan running?); it is unchanged"
  fi
  if ! mv "$next" "$latest"; then
    if [ -e "$old" ] && ! mv "$old" "$latest"; then die "cannot move the new build into $latest, nor the old one back from $old"; fi
    die "cannot move the new build into $latest; the old one is back"
  fi
  rm -rf "$old" || echo "dist: could not remove $old" >&2
  rmdir "$lock"; lock=""
  echo "$dir/$exe"
done
