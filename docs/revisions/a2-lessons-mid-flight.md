# A2: Lessons, mid-flight (during 011–012)

Date: 2026-11-05. Follows addendum A1.

## Ground rules (same as A1)

- No change to language behaviour, emitted C, the golden suite, `lex-diff` or parity results.
- Each item lands as a small commit between task merges and touches no file an active step owns. The gate summary lists any shared file with a one-line reason (revision 0007).
- New tooling is Bayān or shell, never Node, Python or Deno.

## Items

| # | Lesson | Change | Done when |
|---|---|---|---|
| A2.1 | — | Add `docs/design/lessons-from-other-languages.md` and `docs/plans/2026-11-05-000-lessons-index.md`. Link both from README | Both files in `docs/`, linked |
| A2.2 | P09 | `bench/` harness. The same programs in Bayān, C, Rust and Zig: hashing, parsing, and the self-hosted lexer on a fixed input. `bench/run.sh` prints a table and writes `bench/results/<commit>.txt`. Losses are reported, not hidden. **Not a CI gate yet** | `bench/run.sh` runs locally on one machine |
| A2.3 | P08, draft P7 | `bench/baseline.sh` records: clean build time of the self-hosted compiler, rebuild time after a one-file change, and binary size of `bayan` and three goldens | Baseline committed for the current commit |
| A2.4 | M07, draft §3 | `bench/friction.sh` counts in the self-hosted source: `unsafe`, `@allow`, calls to `…Unchecked` members, `@unformatted`, and applied BYN50xx fixes. Feeds the round-4 reevaluation of particles and naming rules | Counts recorded beside the baseline |
| A2.5 | B13 | Docs only. The 012 brief already carries K6 (no timestamps or absolute paths in emitted C or `#line`). Add one sentence: user builds share the same reproducibility goal, checked later by `bayan verify` (v0.2a) | Sentence in the 012 brief |

## Not mid-flight

Everything in R4 and later. In particular: `BY_RESTRICT`, `bayan check`, any CLI change and any emitted-C change.

## Errata for A1

A1's K2 example named the runtime header `bayan_rt.h` (fixed in place). The file in the tree is `bayan-rt.h`. K2's macros and R4.6's `BY_RESTRICT` both go in `bayan-rt.h`.
