#!/usr/bin/env bash
# Parity baselines for spec 015: runs a tool over its recorded cases and compares exit code, stdout, stderr and the
# work directory afterwards with what the original (Python/Deno/PowerShell) script produced when the case was recorded.
# Usage: scripts/parity/run.sh <tool> [--impl '<command>'] [--record] [case...]
#   <tool>     a directory under tests/parity/
#   --impl     the command under test (default: the tool's `impl` file, the original script); a port passes its own,
#              e.g. --impl "$(scripts/pinned-bayan.sh) run tools/mutation-exempt.byn"
#   --record   write expected/ from this run instead of comparing (only ever with the original script)
# A case is tests/parity/<tool>/<case>/ holding:
#   args       arguments, one per line (optional); `$ROOT` and `$WORK` in a line become the repo root and the work directory
#   in/        inputs; copied to a scratch directory that becomes the working directory (files in in/bin/ made executable)
#   stdin      bytes fed on stdin (optional)
#   env        KEY=VALUE lines added to the environment (optional); `$ROOT`, `$WORK` and `$PATH` expand, so
#              PATH=$WORK/bin:$PATH puts a stub (say a `git` printing a recorded diff) first
#   setup      a bash script run in the work directory before the tool (optional; e.g. builds a git repo)
#   expected/  exit, stdout, stderr, and files/ (the work directory's files after the run, setup's .git excluded)
# Prints `PASS <tool> <n> cases` or one `FAIL <tool>/<case>: …` line per mismatch and exits 1.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tool="${1:?usage: run.sh <tool> [--impl cmd] [--record] [case...]}"; shift
dir="$root/tests/parity/$tool"
case "$tool" in ""|.|..|*/*) echo "parity: bad tool name: $tool" >&2; exit 2 ;; esac
[ -d "$dir" ] || { echo "parity: no cases at tests/parity/$tool" >&2; exit 2; }
impl=""; record=0; cases=()
while [ $# -gt 0 ]; do
  case "$1" in
    --impl) impl="$2"; shift 2 ;;
    --record) record=1; shift ;;
    *) cases+=("$1"); shift ;;
  esac
done
[ -n "$impl" ] || impl="$(sed "s|\$ROOT|$root|g" "$dir/impl")"
[ ${#cases[@]} -gt 0 ] || for c in "$dir"/*/; do cases+=("$(basename "$c")"); done

# Files only: git keeps no empty directories, so they are not part of a case (a case that leaves nothing has no
# expected/files in a checkout).
list_files() { [ -d "$1" ] && (cd "$1" && find . \( -type f -o -type l \) | LC_ALL=C sort); return 0; }
same_files() {
  diff <(list_files "$1") <(list_files "$2") || return 1
  local f; while IFS= read -r f; do cmp "$1/$f" "$2/$f" || return 1; done < <(list_files "$2")
}

scratch="$root/tmp/parity/$tool"
bad=0; n=0
for c in "${cases[@]}"; do
  case "$c" in ""|.|..|*/*) echo "FAIL $tool/$c: bad case name"; bad=1; continue ;; esac
  case_dir="$dir/$c"; [ -d "$case_dir" ] || { echo "FAIL $tool/$c: no such case"; bad=1; continue; }
  n=$((n+1))
  work="$scratch/$c/work"; got="$scratch/$c/got"
  rm -rf "${scratch:?}/$c"; mkdir -p "$work" "$got"
  [ -d "$case_dir/in" ] && cp -R "$case_dir/in/." "$work/"
  [ -d "$work/bin" ] && chmod +x "$work/bin"/*  # stubs, whatever mode the checkout gave them
  argv=()
  if [ -f "$case_dir/args" ]; then while IFS= read -r a || [ -n "$a" ]; do a="${a//\$ROOT/$root}"; argv+=("${a//\$WORK/$work}"); done < "$case_dir/args"; fi
  envv=()
  if [ -f "$case_dir/env" ]; then while IFS= read -r e || [ -n "$e" ]; do
    [ -n "$e" ] || continue; e="${e//\$ROOT/$root}"; e="${e//\$WORK/$work}"; envv+=("${e//\$PATH/$PATH}")
  done < "$case_dir/env"; fi
  stdin=/dev/null; [ -f "$case_dir/stdin" ] && stdin="$case_dir/stdin"
  if [ -f "$case_dir/setup" ] && ! (cd "$work" && env ${envv[@]+"${envv[@]}"} bash "$case_dir/setup") > "$got/setup.log" 2>&1; then
    echo "FAIL $tool/$c: setup failed: $(tail -1 "$got/setup.log")"; bad=1; continue
  fi
  set +e
  # shellcheck disable=SC2086  # impl is a command line
  (cd "$work" && env ${envv[@]+"${envv[@]}"} $impl ${argv[@]+"${argv[@]}"} < "$stdin" > "$got/stdout" 2> "$got/stderr")
  echo $? > "$got/exit"
  set -e
  mkdir -p "$got/files"; (cd "$work" && tar --exclude ./.git -cf - .) | (cd "$got/files" && tar -xf -)
  # Scratch paths differ per checkout: the work and repo paths read as $WORK and $ROOT.
  # Literal match, and only a whole path: a root of /w must not rewrite /wave-brief.
  for f in stdout stderr; do W="$work" R="$root" perl -pi -e 's/\Q$ENV{W}\E(?![\w.-])/\$WORK/g; s/\Q$ENV{R}\E(?![\w.-])/\$ROOT/g' "$got/$f"; done
  # Python 3.11+ adds traceback caret lines (`  ^^^^`, `~~~^^`) that older Pythons do not print: drop them.
  perl -ni -e 'print unless /^\s*[~^]+\s*$/' "$got/stderr"
  if [ $record = 1 ]; then
    rm -rf "$case_dir/expected"; mkdir -p "$case_dir/expected"
    cp "$got/exit" "$got/stdout" "$got/stderr" "$case_dir/expected/"; cp -R "$got/files" "$case_dir/expected/files"
    continue
  fi
  [ -d "$case_dir/expected" ] || { echo "FAIL $tool/$c: no expected/ (record it with --record)"; bad=1; continue; }
  for f in exit stdout stderr; do
    cmp -s "$case_dir/expected/$f" "$got/$f" || { echo "FAIL $tool/$c: $f differs: $(diff "$case_dir/expected/$f" "$got/$f" | sed -n 2,3p | tr '\n' ' ' | cut -c1-300)"; bad=1; }
  done
  same_files "$case_dir/expected/files" "$got/files" > "$got/files.diff" 2>&1 \
    || { echo "FAIL $tool/$c: files differ: $(head -3 "$got/files.diff" | tr '\n' ' ' | cut -c1-300)"; bad=1; }
done
[ $record = 1 ] && { echo "RECORDED $tool $n cases"; exit $bad; }
[ $bad = 0 ] && echo "PASS $tool $n cases"
exit $bad
