# bench

Local benchmark harness (A2.2 to A2.4). **Not a CI gate yet**: it runs by hand on one machine, and nothing fails on a slow result. Scratch and build outputs go under `tmp/bench/` (gitignored); only `bench/results/*.txt` is committed.

Needs the bootstrap `bayan` (published on demand with `dotnet publish`, or set `BAYAN_BIN`). `cc`, `rustc` and `zig` are optional: a missing one prints `n/a (no <tool>)` in its cells.

| Script | Measures | Writes |
| --- | --- | --- |
| `bash bench/run.sh` | Hashing (FNV-1a 64 over 8 MiB, 16 passes) and parsing (decimal integers in CSV lines, 4 MiB, 8 passes) in Bayan, C (`cc -O2`), Rust (`rustc -O`) and Zig (`-O ReleaseFast`), plus the self-hosted lexer (`selfhost/lexer`, `--time`) on `selfhost/semantics/*.byn` concatenated. Each program generates its input from the same LCG seed and times only its work loop with a monotonic clock. Median of `BENCH_RUNS` (default 5) runs, ratio to C. Checksums must agree across languages before the table is printed. | `results/<commit>.txt` |
| `bash bench/baseline.sh` | Clean build time of the self-hosted compiler (`selfhost/semantics` with its `parser` and `lexer` siblings, empty `BAYAN_CACHE`), rebuild time after a one-file change (on a copy), and sizes in bytes of the NativeAOT `bayan` and three goldens (`arithmetic`, `collection-copy`, `collection-literals`) built by it. One run each, wall seconds. | `results/<commit>-baseline.txt` |
| `bash bench/friction.sh` | Counts in `selfhost/**/*.byn` of `unsafe`, `@allow`, `...Unchecked(` calls and `@unformatted`, and the BYN50xx diagnostics `bayan check` reports on the self-hosted package. The textual counts skip comment-only lines but include string literals. | `results/<commit>-friction.txt` |

Tests: `bash bench/run.test.sh`, `bash bench/baseline.test.sh`, `bash bench/friction.test.sh`.

## Notes

- Losses are reported as measured; nothing is dropped.
- The lexer benchmark has no C, Rust or Zig port, so those cells print `n/a (no port)` and it has no ratio to C.
- Bayan's monotonic clock (`Io.monotonicNanos`) ticks in microseconds on this machine, so Bayan timings are microsecond-granular.
- The self-hosted package is built from a copy laid out as `src/app` (semantics), `src/parser`, `src/lexer` with a generated `package.byn` (`capabilities: [.fs]`), because the bootstrap resolves sibling modules from `src/`. The BYN5014 warnings come from that generated manifest having no `grammar`.
- Rebuild after a one-file change appends a function to `bigint.byn` in the copy. The bootstrap rebuilds the whole program, so the rebuild time is about the clean build time (no incremental gain is hidden or claimed).
