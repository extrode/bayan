"""Stryker mutation-testing-elements JSON to bayan-quality.json, kind "mutation" (spec 013 R12, R15).

usage: stryker_to_quality.py OUT.json REPO_ROOT BUILD_S TOTAL_S REPORT.json...
"""
import json
import os
import sys

STATUS = {"Killed": "killed", "Survived": "survived", "Timeout": "timeout", "NoCoverage": "not_reached",
          "Ignored": "marked", "CompileError": "not_emitted", "RuntimeError": "not_emitted"}
FILTERED = "Removed by mutate filter"
COUNTS = ["killed", "survived", "timeout", "marked", "not_reached", "not_emitted"]


def original(source, start, end):
    lines = source.split("\n")
    if start["line"] == end["line"]:
        return lines[start["line"] - 1][start["column"] - 1:end["column"] - 1]
    parts = [lines[start["line"] - 1][start["column"] - 1:]]
    parts += lines[start["line"]:end["line"] - 1]
    parts.append(lines[end["line"] - 1][:end["column"] - 1])
    return "\n".join(parts)


def convert(paths, root, build_s, total_s):
    files, operators, next_id = [], {}, 1
    for p in paths:
        with open(p) as f:
            data = json.load(f)
        if not isinstance(data.get("files"), dict):
            raise ValueError("%s: no files object" % p)
        for name, entry in data["files"].items():
            if os.path.isabs(name):
                name = os.path.relpath(name, root)
            # Stryker lists the files --mutate left out too, their mutants Ignored "Removed by mutate filter" or CompileError.
            if any(m.get("statusReason") == FILTERED for m in entry["mutants"]):
                continue
            mutants = []
            for m in entry["mutants"]:
                if m["status"] not in STATUS:
                    raise ValueError("%s: unknown status %r" % (p, m["status"]))
                status, op = STATUS[m["status"]], m["mutatorName"]
                counts = operators.setdefault(op, dict.fromkeys(COUNTS, 0))
                counts[status] += 1
                if status == "not_emitted":
                    continue
                start, end = m["location"]["start"], m["location"]["end"]
                mutant = {"site": {"id": next_id, "file": name, "line": start["line"], "column": start["column"],
                                   "operator": op, "original": original(entry.get("source", ""), start, end),
                                   "mutated": m["replacement"]},
                          "status": status}
                if status == "survived":
                    mutant["suggestion"] = "add a case that fails under this change"
                next_id += 1
                mutants.append(mutant)
            if entry["mutants"]:
                files.append({"path": name, "mutants": mutants})
    totals = {c: sum(o[c] for o in operators.values()) for c in COUNTS}
    run = totals["killed"] + totals["survived"] + totals["timeout"]
    mutant_s = max(total_s - build_s, 0.0)
    mean = mutant_s / run if run else 0
    return {
        "version": 1,
        "kind": "mutation",
        "tool": "stryker",
        # Stryker reports no per-mutant time: the mean is the run time over the mutants run, the max is not known.
        "timing": {"build_s": build_s, "mean_mutant_s": mean, "max_mutant_s": 0, "total_s": total_s,
                   "mutant_share_of_build": mean / build_s if build_s > 0 else 0},
        "files": files,
        "totals": totals,
        "operators": operators,
    }


def summary(r):
    t = r["totals"]
    out = "mutation: %d killed, %d survived, %d timeout, %d marked, %d not reached, %d not emitted\n" % (
        t["killed"], t["survived"], t["timeout"], t["marked"], t["not_reached"], t["not_emitted"])
    for f in r["files"]:
        for m in f["mutants"]:
            s = m["site"]
            if m["status"] == "survived":
                out += "survived: %s:%d:%d: %s -> %s [%s]\n  suggestion: %s\n" % (
                    s["file"], s["line"], s["column"], s["original"], s["mutated"], s["operator"], m["suggestion"])
    return out


def main(argv):
    if len(argv) < 6:
        sys.stderr.write(__doc__)
        return 2
    try:
        report = convert(argv[5:], argv[2], float(argv[3]), float(argv[4]))
    except (ValueError, KeyError, TypeError, IndexError, OSError) as e:
        sys.stderr.write("stryker_to_quality: %s\n" % repr(e))
        return 1
    with open(argv[1], "w") as f:
        json.dump(report, f, indent=2)
        f.write("\n")
    sys.stdout.write(summary(report))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
