# 016 — Tasks
Plan: ./016-plan.md

- [ ] T1 C# std imports — files: `lib/std/README.md`, `src/Bayan.Semantics/{Bayan.Semantics.csproj,StdLibrary.cs,Compilation.cs}`, `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`, `docs/diagnostics/`, `tests/Bayan.Tests/StdImportTests.cs`, `tests/cases/diag/` — covers: R1–R6 — done when: the injected-table tests and the diag case pass, `dotnet test -v q` passes.
- [ ] T2 Selfhost std imports — files: `selfhost/semantics/{compile,codes}.byn`, generated `selfhost/semantics/stdsource.byn` + generator test — covers: R7 — done when: the selfhost tests mirror T1's with expected lines from the C#.
- [ ] T3 Gates over `lib/std/` — files: `scripts/selfhost/` format/coverage gates — covers: R8 — done when: a misformatted file under `lib/std/` fails the gate.
- [ ] T4 `std.json` reader — needs its member list approved first (015 R4, S7) — covers: Q4.
