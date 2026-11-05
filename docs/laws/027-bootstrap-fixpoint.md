---
id: L027
title: Bootstrap fixpoint
status: proposed
kind: property
scope: selfhost/driver/**, selfhost/emitter/**, selfhost/semantics/**, bootstrap/bayan.c
proof: (written by 012 T12 via /prove, after T9)
check: dotnet test -v q --filter "Law=L027"
timeout: 900
---

## Statement
For the driver's sources and each of `safe` and `debug`, the C the C# compiler emits, the C stage 1 emits and the C stage 2 emits are byte-identical, and contain no absolute path and no timestamp, all stages built on the same host.

## Counterexample looks like
A mode and the first differing byte offset between two stages' emitted C (for example stage 1 vs stage 2 in `debug`), with the C# source line each side came from; or an absolute path or timestamp found in one stage's C.

## Proof notes
Cross-host byte identity for the same `--target` is a tracked future strengthening, not part of L027. Approved by the user 2026-11-05 with this wording (012-spec.md, Law candidates). Enactment is the user's edit of `status:` (012 T11).
