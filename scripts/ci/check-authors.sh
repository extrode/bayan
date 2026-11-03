#!/usr/bin/env bash
# Refuses a history with a Claude/Anthropic author or committer, or an AI attribution trailer.
# Usage: check-authors.sh [<rev>]   (default HEAD; scans every commit reachable from it)
# Exit 0 clean, 1 offending commits listed on stderr, 2 usage or git error.
set -uo pipefail
rev=${1:-HEAD}
git rev-parse -q --verify "$rev^{commit}" >/dev/null || { echo "check-authors: not a commit: $rev" >&2; exit 2; }
id_re='[Cc]laude|[Aa]nthropic'
msg_re='Co-[Aa]uthored-[Bb]y:.*([Cc]laude|[Aa]nthropic)|Generated (with|by) .*Claude|🤖'
bad=$(git log --format='%H%x09%an <%ae>%x09%cn <%ce>' "$rev" | awk -F'\t' -v re="$id_re" '$2 ~ re || $3 ~ re { print substr($1, 1, 12) " author " $2 " committer " $3 }') || exit 2
msgs=$(git log -E --format='%H' --grep="$msg_re" "$rev" | cut -c1-12 | sed 's/$/ message: AI attribution line/') || exit 2
out=$(printf '%s\n%s\n' "$bad" "$msgs" | sed '/^$/d')
if [ -n "$out" ]; then
  printf '%s\n' "$out" >&2
  echo "check-authors: $(printf '%s\n' "$out" | wc -l | tr -d ' ') offending commit line(s) reachable from $rev" >&2
  exit 1
fi
echo "check-authors: clean ($(git rev-list --count "$rev") commits)"
