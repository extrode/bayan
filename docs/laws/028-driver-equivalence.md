---
id: L028
title: Driver equivalence
status: enacted
kind: property
scope: selfhost/driver/**, src/Bayan.Cli/CliApp.cs
proof: tests/Bayan.Tests/SelfhostDriverGoldenTests.cs
check: dotnet test -v q --filter "Law=L028"
timeout: 1200
---

## Statement
For every case of the R10 corpus and the R2 usage corpus, stage 2's stdout, stderr and exit code equal `CliApp.Run`'s with elapsed time masked (before comparing, every match of the regex `[0-9]+\.[0-9]{2}s` — the `build.seconds` and `test.seconds` messages, `CliApp.cs` `Seconds` — becomes `<t>s`, and every JSON `"durationMs": <number>` value — `TestReport.cs` — becomes `"durationMs": 0`; nothing else is masked); the comparison runs with stderr not a terminal and `CI` set.

## Counterexample looks like
A corpus case (its argv and input files), and the first differing stream (stdout, stderr or exit code) between stage 2 and `CliApp.Run`, both outputs shown with elapsed seconds masked.

## Proof notes
`BAYAN_LAW_TIER`: unset runs a seeded subset; `full` runs the whole corpus, as L018/L023 do. Approved by the user 2026-11-05 with this wording (012-spec.md, Law candidates). Enactment is the user's edit of `status:` (012 T13).
