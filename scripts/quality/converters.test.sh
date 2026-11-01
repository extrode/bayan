#!/usr/bin/env bash
# Runs both converters over the fixtures and diffs against the expected JSON; set UPDATE=1 to rewrite the expected files.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
scratch="$repo/tmp/quality/converters-test"
rm -rf "$scratch"
mkdir -p "$scratch"
fail=0

check() { # name actual expected
  if [ "${UPDATE:-}" = 1 ]; then cp "$2" "$3"; fi
  if diff -u "$3" "$2"; then echo "ok: $1"; else echo "FAIL: $1"; fail=1; fi
}

python3 -I "$here/cobertura_to_quality.py" "$here/fixtures/cobertura.xml" /repo "$scratch/cov.json" 1.5 4 >/dev/null
check coverage "$scratch/cov.json" "$here/fixtures/coverage.expected.json"

python3 -I "$here/stryker_to_quality.py" "$scratch/mut.json" /repo 2 10 "$here/fixtures/stryker.json" >/dev/null
check mutation "$scratch/mut.json" "$here/fixtures/mutation.expected.json"

# a line in two classes with (1/4) and (2/2) branches keeps all 4 branches, 2 covered
cat > "$scratch/shared.xml" <<'XML'
<coverage><packages><package name="P"><classes>
  <class name="A" filename="/repo/src/A.cs"><lines><line number="1" hits="1" branch="True" condition-coverage="25% (1/4)" /></lines></class>
  <class name="A/Inner" filename="/repo/src/A.cs"><lines><line number="1" hits="1" branch="True" condition-coverage="100% (2/2)" /></lines></class>
</classes></package></packages></coverage>
XML
python3 -I "$here/cobertura_to_quality.py" "$scratch/shared.xml" /repo "$scratch/shared.json" 0 0 >/dev/null
if python3 -I -c 'import json, sys; b = json.load(open(sys.argv[1]))["totals"]["branches"]; sys.exit(b != {"covered": 2, "total": 4})' "$scratch/shared.json"
then echo "ok: shared-line-branches"; else echo "FAIL: shared-line-branches: (1/4)+(2/2) is not 2/4"; fail=1; fi

expect_failure() { # name command...
  local name="$1"; shift
  if "$@" >"$scratch/out" 2>"$scratch/err"; then echo "FAIL: $name exited 0"; fail=1
  elif [ -s "$scratch/err" ] && [ ! -s "$scratch/out" ]; then echo "ok: $name"
  else echo "FAIL: $name wrote nothing to stderr or something to stdout"; fail=1; fi
}
expect_failure malformed-cobertura python3 -I "$here/cobertura_to_quality.py" "$here/fixtures/malformed.xml" /repo "$scratch/x.json" 0 0
expect_failure missing-cobertura python3 -I "$here/cobertura_to_quality.py" "$scratch/none.xml" /repo "$scratch/x.json" 0 0
expect_failure bad-status python3 -I "$here/stryker_to_quality.py" "$scratch/x.json" /repo 0 0 "$here/fixtures/bad-status.json"
expect_failure malformed-stryker python3 -I "$here/stryker_to_quality.py" "$scratch/x.json" /repo 0 0 "$here/fixtures/malformed.xml"

exit "$fail"
