"""Cobertura XML (coverlet) to bayan-quality.json, kind "coverage" (spec 013 R12, R15).

usage: cobertura_to_quality.py COBERTURA.xml REPO_ROOT OUT.json BUILD_S TOTAL_S
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET


def convert(path, root, build_s, total_s):
    lines_by_file = {}
    tree = ET.parse(path).getroot()
    sources = [e.text.strip() for e in tree.iter("source") if e.text]
    for cls in tree.iter("class"):
        name = cls.get("filename")
        if name is None:
            raise ValueError("class without filename")
        if not os.path.isabs(name) and sources:
            # coverlet writes names relative to a <source> root; take the root where the file exists
            name = next((os.path.join(d, name) for d in sources if os.path.exists(os.path.join(d, name))),
                        os.path.join(sources[0], name))
        if os.path.isabs(name):
            name = os.path.relpath(name, root)
        entries = lines_by_file.setdefault(name, {})
        for line in cls.findall("lines/line"):
            n, hits = int(line.get("number")), int(line.get("hits"))
            cov = line.get("condition-coverage")
            cond = (0, 0)
            if line.get("branch", "").lower() == "true":
                m = re.match(r"\s*[\d.]+%\s*\((\d+)/(\d+)\)", cov or "")
                if not m:
                    raise ValueError("bad condition-coverage on line %d: %r" % (n, cov))
                cond = (int(m.group(1)), int(m.group(2)))
            old = entries.get(n)
            # a line shared by several classes is hit when any class hits it; keep the most branches and the most
            # covered separately, so (1/4) and (2/2) give 2/4 and no uncovered branch is dropped
            if old is not None:
                hits, cond = max(hits, old[0]), (max(cond[0], old[1][0]), max(cond[1], old[1][1]))
            entries[n] = (hits, cond)
    files = []
    tot = {"lines": [0, 0], "branches": [0, 0]}
    for name in sorted(lines_by_file):
        entries = lines_by_file[name]
        missed = [n for n in sorted(entries) if entries[n][0] == 0]
        covered_branches = sum(c[0] for _, c in entries.values())
        total_branches = sum(c[1] for _, c in entries.values())
        # Cobertura gives no column or outcome for a condition: column 0, outcome false.
        missed_branches = [{"line": n, "column": 0, "outcome": False}
                           for n in sorted(entries) for _ in range(entries[n][1][1] - entries[n][1][0])]
        files.append({
            "path": name,
            "lines": {"covered": len(entries) - len(missed), "total": len(entries), "missed": missed, "unknown": []},
            "branches": {"covered": covered_branches, "total": total_branches, "missed": missed_branches, "unknown": []},
            "errorPaths": [],
        })
        tot["lines"][0] += len(entries) - len(missed)
        tot["lines"][1] += len(entries)
        tot["branches"][0] += covered_branches
        tot["branches"][1] += total_branches
    return {
        "version": 1,
        "kind": "coverage",
        "tool": "coverlet",
        "timing": {"build_s": build_s, "total_s": total_s},
        "files": files,
        "totals": {
            "lines": {"covered": tot["lines"][0], "total": tot["lines"][1]},
            "branches": {"covered": tot["branches"][0], "total": tot["branches"][1]},
            "errorPaths": {"covered": 0, "total": 0},
        },
    }


def summary(r):
    t = r["totals"]
    pct = lambda x: "100%" if x["total"] == 0 else "%.3g%%" % (100.0 * x["covered"] / x["total"])
    out = "coverage: lines %d/%d (%s), branches %d/%d (%s)\n" % (
        t["lines"]["covered"], t["lines"]["total"], pct(t["lines"]),
        t["branches"]["covered"], t["branches"]["total"], pct(t["branches"]))
    for f in r["files"]:
        for n in f["lines"]["missed"]:
            out += "uncovered line: %s:%d\n" % (f["path"], n)
    return out


def main(argv):
    if len(argv) != 6:
        sys.stderr.write(__doc__)
        return 2
    try:
        report = convert(argv[1], argv[2], float(argv[4]), float(argv[5]))
    except (ET.ParseError, ValueError, OSError, TypeError) as e:
        sys.stderr.write("cobertura_to_quality: %s\n" % e)
        return 1
    with open(argv[3], "w") as f:
        json.dump(report, f, indent=2)
        f.write("\n")
    sys.stdout.write(summary(report))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
