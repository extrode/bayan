# replay.py — replays a BAYAN_SNAPSHOT recording of the selfhost tests (spec 013 R14) without `dotnet test`.
#   python3 -I scripts/selfhost/replay.py <snapshot dir>
# Runs every record of <dir>/runs.jsonl (program, args, cwd, stdin) with BAYAN_MUTANT inherited, as
# `bayan mutate --binary <exe> --sites <map.json> -- python3 -I scripts/selfhost/replay.py <dir>` does per mutant.
# Exit 0 when each run's exit code, stdout and stderr match the recording, 1 at the first mismatch (named on stderr),
# 2 when the recording cannot be read or a program cannot start.
# The C# oracle checks run once, at record time, and a recording only completes if every recorded test passed them
# (013-plan.md:21, "compute the C# oracle once"); the replay never runs C#.
# A recording runs each program twice and fails when the runs differ; tests whose output is not deterministic carry
# [Trait("Snapshot", "skip")], and a recording's `dotnet test --filter` ends in `&Snapshot!=skip` to leave them out.
# Input files are read at replay time, so a recorded test must use unique input paths (one rewritten between runs cannot replay).
import base64, json, os, subprocess, sys

if len(sys.argv) != 2:
    print('usage: replay.py <snapshot dir>', file=sys.stderr)
    sys.exit(2)
try:
    with open(os.path.join(sys.argv[1], 'runs.jsonl'), encoding='utf-8') as f:
        records = [json.loads(line) for line in f if line.strip()]
except (OSError, ValueError) as e:
    print(f'replay: {e}', file=sys.stderr)
    sys.exit(2)


def first_difference(expected, actual):
    a, b = expected.split(b'\n'), actual.split(b'\n')
    for i, (x, y) in enumerate(zip(a, b), 1):
        if x != y:
            return f'line {i}\n expected: {x.decode(errors="replace")}\n   actual: {y.decode(errors="replace")}'
    return f'{len(a)} lines expected, {len(b)} printed'


for n, r in enumerate(records, 1):
    command = [r['program'], *r['args']]
    stdin = r.get('stdin')
    io = {'input': base64.b64decode(stdin)} if stdin is not None else {'stdin': subprocess.DEVNULL}
    try:
        p = subprocess.run(command, cwd=r['cwd'], capture_output=True, **io)
    except OSError as e:
        print(f'replay: run {n}: {e}', file=sys.stderr)
        sys.exit(2)
    exit = 128 - p.returncode if p.returncode < 0 else p.returncode  # killed by a signal: .NET's ExitCode is 128 + signal
    if exit != r['exit']:
        print(f'replay: run {n} ({" ".join(command)}): exit {exit}, recorded {r["exit"]}', file=sys.stderr)
        sys.exit(1)
    for key, actual in (('stdout', p.stdout), ('stderr', p.stderr)):
        expected = base64.b64decode(r[key])
        if actual != expected:
            print(f'replay: run {n} ({" ".join(command)}): {key} differs at {first_difference(expected, actual)}', file=sys.stderr)
            sys.exit(1)
print(f'replay: {len(records)} runs match')
