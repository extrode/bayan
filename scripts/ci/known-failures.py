#!/usr/bin/env python3
"""Compare failed tests in .trx files against a list of known failures.

usage: known-failures.py LIST TRX [TRX...]
exit 0: failed set == listed set (minus listed tests that did not run)
exit 1: new failure, or a listed test now passes
exit 2: unreadable trx, no trx, or zero results
"""
import sys
import xml.etree.ElementTree as ET

# trx outcomes of a test that did not pass: a crashed or timed-out test is a failure too.
FAILED = {"Failed", "Error", "Timeout", "Aborted"}


def main(argv):
    if len(argv) < 3:
        print("known-failures: usage: known-failures.py LIST TRX [TRX...]; no trx given")
        return 2
    list_path = argv[1]
    try:
        with open(list_path, encoding="utf-8") as f:
            listed = {l.strip() for l in f if l.strip() and not l.lstrip().startswith("#")}
    except OSError as e:
        print(f"known-failures: cannot read {list_path}: {e}")
        return 2
    outcomes = {}
    for path in argv[2:]:
        try:
            root = ET.parse(path).getroot()
        except (OSError, ET.ParseError) as e:
            print(f"known-failures: cannot read {path}: {e}")
            return 2
        for el in root.iter():
            if el.tag.rsplit("}", 1)[-1] != "UnitTestResult":
                continue
            name, outcome = el.get("testName"), el.get("outcome")
            if name is None:
                continue
            if outcome in FAILED or name not in outcomes:
                outcomes[name] = outcome
    if not outcomes:
        print("known-failures: no test results in the trx files")
        return 2
    failed = {n for n, o in outcomes.items() if o in FAILED}
    new = sorted(failed - listed)
    passing = sorted(n for n in listed if outcomes.get(n) == "Passed")
    not_run = sorted(n for n in listed if n not in outcomes)
    for n in new:
        print(f"new failure: {n}")
    for n in passing:
        print(f"now passes: remove it from {list_path}: {n}")
    for n in not_run:
        print(f"not run: {n}")
    print(f"known-failures: {len(failed)} failed, {len(failed & listed)} known, {len(new)} new, {len(passing)} now passing")
    return 1 if new or passing else 0


sys.exit(main(sys.argv))
