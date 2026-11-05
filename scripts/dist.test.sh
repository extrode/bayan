#!/usr/bin/env bash
# scripts/dist.sh with a fake dotnet: layout per target and version, latest replaced on each build, --from import, refusals.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
t="$root/tmp/dist-test-$$"
mkdir -p "$t/bin"
trap 'rm -rf "$t"' EXIT
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok: $1"; else echo "FAIL: $1: got '$2', want '$3'"; fail=1; fi; }
d="$t/dist"
# fake dotnet: `dotnet publish <proj> -c Release -r <rid> -o <dir>` writes a binary naming its rid and FAKE_BUILD
cat > "$t/bin/dotnet" <<'FAKE'
#!/usr/bin/env bash
while [ $# -gt 0 ]; do case "$1" in -r) rid=$2; shift 2 ;; -o) out=$2; shift 2 ;; *) shift ;; esac; done
[ "${FAKE_FAIL:-}" = 1 ] && exit 1
mkdir -p "$out"; exe=bayan; case "$rid" in win-*) exe=bayan.exe ;; esac
echo "$rid ${FAKE_BUILD:-1}" > "$out/$exe"
FAKE
chmod +x "$t/bin/dotnet"
run() { PATH="$t/bin:$PATH" bash "$root/scripts/dist.sh" --dist "$d" "$@"; }

out=$(FAKE_BUILD=1 run --rid osx-arm64 --label 1.0.0 2>/dev/null); check "first build exits 0" "$?" 0
check "prints the binary path" "$out" "$d/macos-arm64/1.0.0/bayan"
check "version dir binary" "$(cat "$d/macos-arm64/1.0.0/bayan")" "osx-arm64 1"
check "latest binary" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 1"
check "binary executable" "$([ -x "$d/macos-arm64/1.0.0/bayan" ] && echo y)" y
check "BUILD-INFO version" "$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")" "version 1.0.0"
check "BUILD-INFO rid" "$(grep '^rid ' "$d/macos-arm64/1.0.0/BUILD-INFO")" "rid osx-arm64"

FAKE_BUILD=2 run --rid osx-arm64 --label 1.1.0 >/dev/null 2>&1
check "older version kept" "$(cat "$d/macos-arm64/1.0.0/bayan")" "osx-arm64 1"
check "new version dir" "$(cat "$d/macos-arm64/1.1.0/bayan")" "osx-arm64 2"
check "latest replaced by the newest build" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"
check "latest BUILD-INFO replaced" "$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")" "version 1.1.0"
check "no staging dir left" "$(ls -A "$d/macos-arm64" | tr '\n' ' ')" "1.0.0 1.1.0 latest "

FAKE_BUILD=3 run --rid win-x64 --rid linux-arm64 --label 1.1.0 >/dev/null 2>&1
check "windows target name and exe" "$(cat "$d/windows-x64/latest/bayan.exe")" "win-x64 3"
check "linux target" "$(cat "$d/linux-arm64/1.1.0/bayan")" "linux-arm64 3"
check "other targets untouched" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"

mkdir -p "$t/pub"; echo imported > "$t/pub/bayan.exe"
run --from "$t/pub" --rid win-x64 --label 1.2.0 >/dev/null 2>&1; check "--from exits 0" "$?" 0
check "--from imported" "$(cat "$d/windows-x64/latest/bayan.exe")" imported
check "--from source recorded" "$(grep '^source ' "$d/windows-x64/1.2.0/BUILD-INFO")" "source $t/pub"
check "--from commit unknown without --commit" "$(grep '^commit ' "$d/windows-x64/1.2.0/BUILD-INFO")" "commit unknown"
run --from "$t/pub" --rid win-x64 --label 1.2.1 --commit abc123 >/dev/null 2>&1
check "--commit recorded" "$(grep '^commit ' "$d/windows-x64/1.2.1/BUILD-INFO")" "commit abc123"
check "host build records this checkout" "$(grep '^commit ' "$d/macos-arm64/1.0.0/BUILD-INFO")" "commit $(git -C "$root" rev-parse HEAD)"

FAKE_FAIL=1 run --rid osx-arm64 --label 2.0.0 >/dev/null 2>&1; check "failed publish exits 1" "$?" 1
check "failed publish leaves latest" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"
check "failed publish makes no version dir" "$([ -e "$d/macos-arm64/2.0.0" ] && echo y || echo n)" n

err=$(run --rid sparc-x64 --label 1.0.0 2>&1); check "unknown rid exits 1" "$?" 1
check "unknown rid message" "$err" "dist: unknown rid sparc-x64"
run --rid osx-arm64 --label latest >/dev/null 2>&1; check "label 'latest' refused" "$?" 1
run --rid osx-arm64 --label ../x >/dev/null 2>&1; check "label with / refused" "$?" 1
run --from "$t/pub" >/dev/null 2>&1; check "--from without --rid refused" "$?" 1
run --bogus >/dev/null 2>&1; check "unknown argument refused" "$?" 1

lbl=$(FAKE_BUILD=4 run 2>/dev/null); check "host build exits 0" "$?" 0
check "default label is CompilerVersion(+commit)(.dirty)" "$(echo "$lbl" | grep -cE '/[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+\+[0-9a-f]{12})?(\.dirty)?/bayan(\.exe)?$')" 1
prev="$(cat "$d/macos-arm64/latest/bayan")" prevv="$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")"
mkdir "$d/macos-arm64/.lock"
FAKE_BUILD=5 run --rid osx-arm64 --label 3.0.0 >/dev/null 2>&1; check "held lock exits 1" "$?" 1
check "held lock leaves latest" "$(cat "$d/macos-arm64/latest/bayan")" "$prev"
check "held lock makes no version dir" "$([ -e "$d/macos-arm64/3.0.0" ] && echo y || echo n)" n
rmdir "$d/macos-arm64/.lock"

# a mv that cannot move latest aside, as Windows refuses for a folder holding a running bayan.exe
mkdir -p "$t/nomv"
printf '#!/usr/bin/env bash\ncase "$1" in */latest) exit 1 ;; esac\nexec /bin/mv "$@"\n' > "$t/nomv/mv"; chmod +x "$t/nomv/mv"
PATH="$t/nomv:$PATH" FAKE_BUILD=6 run --rid osx-arm64 --label 3.1.0 >/dev/null 2>&1; check "latest in use exits 1" "$?" 1
check "latest in use is unchanged" "$(cat "$d/macos-arm64/latest/bayan")" "$prev"
check "latest in use keeps its BUILD-INFO" "$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")" "$prevv"
check "latest in use leaves no staging or lock" "$(ls -A "$d/macos-arm64" | grep -c '^\.')" 0

run --rid osx-arm64 --label .latest-next >/dev/null 2>&1; check "label starting with . refused" "$?" 1
run --rid osx-arm64 --label "1.0 x" >/dev/null 2>&1; check "label with a space refused" "$?" 1
PATH="$t/bin:$PATH" bash "$root/scripts/dist.sh" --dist "" --rid osx-arm64 --label 1.0.0 >/dev/null 2>&1; check "empty --dist refused" "$?" 1

# .dirty counts untracked files under src/: a scratch repo holding just the script and Toolchain.cs
g="$t/repo"; mkdir -p "$g/scripts" "$g/src/Bayan.Backend.C"
cp "$root/scripts/dist.sh" "$g/scripts/"; cp "$root/src/Bayan.Backend.C/Toolchain.cs" "$g/src/Bayan.Backend.C/"
git -C "$g" init -q -b scratch && git -C "$g" add -A && git -C "$g" -c core.hooksPath=/dev/null commit -qm "test: fixture"
grun() { PATH="$t/bin:$PATH" bash "$g/scripts/dist.sh" --dist "$t/gdist" --rid osx-arm64 2>/dev/null; }
check "clean scratch repo has no .dirty" "$(grun | grep -c '\.dirty/')" 0
touch "$g/notes.txt"
check "untracked file outside src/ is not dirty" "$(grun | grep -c '\.dirty/')" 0
touch "$g/src/New.cs"
check "untracked file under src/ is dirty" "$(grun | grep -c '\.dirty/')" 1

exit $fail
