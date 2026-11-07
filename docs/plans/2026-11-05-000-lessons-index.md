# Lessons intake: index and crosswalk

Date: 2026-11-05.
Source: `docs/design/lessons-from-other-languages.md`.

This index maps every Planned and Watch lesson to one handoff. Adopted lessons need no handoff unless a row below names follow-up work.

## Handoffs

| Handoff | File | Gate to start |
|---|---|---|
| A2 Mid-flight | `docs/revisions/a2-lessons-mid-flight.md` | Now. Docs and measurement only (A1 ground rules) |
| R4 Lessons in the round-4 batch | `docs/plans/2026-11-05-r4-section-10-lessons.md`, appended to the round-4 draft as §10 | 012 passes; lands with the round-4 batch |
| v0.2a Fast loop, inspection, testing | `docs/plans/2026-11-05-001-v0.2a-loop.md` | Round-4 batch merged |
| v0.2b IDE and syntax API | `docs/plans/2026-11-05-002-v0.2b-ide.md` | Round-4 batch merged; runs in parallel with v0.2a after v0.2a #2 |
| v0.3 Packages and supply chain | `docs/plans/2026-11-05-003-v0.3-packages.md` | v0.2a merged; before any public registry opens |
| v0.4 Interop and targets | `docs/plans/2026-11-05-004-v0.4-interop.md` | Toolchain phase 2 done |
| Parking lot | `docs/plans/2026-11-05-005-parking-lot.md` | Per-item evidence trigger |

## Crosswalk

| Lesson | Handoff | Lesson | Handoff | Lesson | Handoff |
|---|---|---|---|---|---|
| P02 | R4 (draft P3) | B03 | v0.2a | S02 | v0.3 |
| P03 | R4.6 | B04 | Parking (phase 4) | S03 | v0.3 |
| P04 | v0.2a (draft P2) | B05 | v0.2a | S04 | v0.3 |
| P05 | v0.4 | B06 | Parking | S05 | v0.3 |
| P06 | Parking (draft `@soa`) | B07 | v0.2a | S07 | v0.3 |
| P08 | A2, v0.2a | B08 | Parking | S08 | v0.3 |
| P09 | A2, v0.2a | B09 | Parking | I01 | v0.4 |
| M07 | R4.8, v0.2a | B10 | v0.2b | I02 | v0.4 |
| M08 | v0.2a | B11 | v0.2b | I03 | v0.4 |
| E04 | R4.1 | B12 | v0.4 | I04 | v0.4 |
| E05 | Parking | B13 | A2, v0.2a | I05 | v0.4 |
| T06 | R4.2 | D01 | v0.2a | G02 | v0.3 |
| T08 | Round-4 draft (queued) | D04 | R4.5 | G04 | R4.4 |
| X03 | v0.2b | D06 | Parking | G06 | R4.3 |
| X04 | Parking | C05 | Parking | Q04 | v0.2a |
| X05 | R4.7, parking (syntax) | | | | |

## Decisions recorded on 2026-11-05

- **R4.1:** private signatures infer `fails` (as they already infer `uses`). Public signatures always declare. `signatures: .declared` in `package.byn` requires every signature to declare.
- **R4.2:** coherence rule B. A conformance lives with the type's owner or the interface's owner. `extend T : I { … }` is the placeholder spelling for a conformance written away from the type.
- **v0.4:** .NET only through C ABI exports of NativeAOT libraries. Pilot: JauntyQ generates both the C# export shim and the Bayān bindings. Long-term: a native port of JauntyQ's model.
- Remaining questions accepted as recommended:
  - CI gates: report-only for two weeks, then gate.
  - Fuzzing: built-in generators first, libFuzzer later.
  - Visual Studio: thin extension hosting the language server.
  - SBOM format: CycloneDX.
  - WebAssembly: v0.4, fiber-free code only.
- Still open: registry hosting (Ikhbat foundation recommended). It must be decided before the first publish.

## Pre-1.0 gate

1.0 ships only when all of these hold:
- T06 coherence is enforced.
- Diagnostics are renumbered, with one edition of aliases.
- The I03 ABI policy is in SPEC.
- S04 and S05 are live on the registry.
- G06 deprecation works.
- The stability promise per edition is published.
