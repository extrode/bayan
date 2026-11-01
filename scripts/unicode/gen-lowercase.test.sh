#!/bin/bash
# An interrupted UnicodeData.txt download is not reused as the cache: the next run downloads again (stub urlretrieve, no network).
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/gen-lowercase-test"; rm -rf "$r" && mkdir -p "$r"
python3 -I -B - "$root/scripts/unicode/gen-lowercase.py" "$r" <<'PY'
import importlib.util, sys
spec = importlib.util.spec_from_file_location("gen", sys.argv[1]); gen = importlib.util.module_from_spec(spec); spec.loader.exec_module(gen)
gen.ROOT = sys.argv[2]
def interrupted(url, dest):
    open(dest, "w").write("partial"); raise OSError("connection reset")
def whole(url, dest):
    open(dest, "w").write("whole")
gen.urllib.request.urlretrieve = interrupted
try:
    gen.cached_ucd()
except OSError:
    pass
gen.urllib.request.urlretrieve = whole
got = open(gen.cached_ucd()).read()
sys.exit(0 if got == "whole" else f"FAIL: the cache holds {got!r} from the interrupted download")
PY
rm -rf "$r"
echo PASS
