# Bayān roadmap

Bayān compiles itself, but the shipped compiler is still the C# one (`CHANGELOG.md`, 0.1.0 Known limits). The next steps are to ship the self-hosted compiler, retire the C# code and drop every non-Bayān tool (`docs/bayan-handoff.md`, Target: zero dependencies). Goals below that no source plans are marked "(no plan yet)".

## Where we are

State of `dev` at 87637629, 2026-11-06.

### Shipped in v0.1.0 (2026-11-02)

Source: `CHANGELOG.md` (0.1.0), `docs/bayan-handoff.md` (Milestones).

| Milestone | What it delivered |
|---|---|
| M0 — Skeleton | Solution, AOT CLI, diagnostics renderer, golden harness, CI |
| M1 — Lexer | UTF-8 lexer, BYN0001–0016, `bayan lex`, `bayan explain` |
| M2 — Parser | AST, recovery, BYN1001–1016, parse and diag goldens |
| M3 — Semantics I: names, types, mutability | Names, types, mutability, flow, error sets; 95 diagnostics BYN2001–3001 |
| M4 — C backend: hello world | Lowering, C backend, runtime, `bayan build` and `run` |
| M5 — Errors | Error-handling goldens, `Result<T, E>`, non-exhaustive catch |
| M6 — Memory basics | Memory goldens, region analysis BYN3002–3005 |
| M7 — Tests | `bayan test`, one child process per test |
| M8 — tally-lite (exit criterion) | The v0.1 exit criterion, met |
| M9 — `bayan new` and `bayan init` | Project scaffolding, `package.byn` reader, project builds |
| M10 — Build outputs: `out/` and `dist/` | `--target`, `test --json`, `dist`, `clean` |
| M11 — Global cache and worktrees | Shared build cache, worktree reuse, cache stats |
| M12 — CLI completeness and machine-readable output | Colour, `bayan doctor`, `test --changed`, `--version`, `--diagnostics json`, exit codes 0–4 |

The 0.1.0 release also holds specs 001–010 and 013 (see the table below). Archives exist for linux-x64, linux-arm64, macos-arm64, macos-x64, windows-x64 and windows-arm64, with SHA256SUMS (`CHANGELOG.md`).

### What 0.2.0 adds (unreleased)

`CHANGELOG.md` `[0.2.0]` (from `chore/release-0.2.0`) covers specs 011, 012, 014 and the memory bounds.

| Item | Measured result | Source |
|---|---|---|
| Spec 011, self-hosted emitter (`selfhost/emitter/`, `bayan-emit`) | Parity 290 of 290 `run/` goldens; speed ratio against the C# from 120 to 1.26 (asserted at most 10); L024 made 2,632 comparisons quick and 9,544 full; 8,860 tests passed in the full run | `docs/bayan-handoff.md` (spec table, row 011) |
| Spec 012, self-hosted driver and bootstrap (`selfhost/driver/`, `bootstrap/bayan.c`) | Stage 2 C equals stage 3 C in `safe` and `debug` on one host; `cc` alone builds the compiler; L027 and L028 enacted | `docs/bayan-handoff.md` (Road to self-hosting, Bootstrap from `bootstrap/`) |
| Spec 014, `Path` module | `combine`, `directoryName`, `fullPath` agree with .NET (L026); `tests/windows-known-failures.txt` lists 1 test, down from 78 failures of 6,051 | `CHANGELOG.md`, `tests/windows-known-failures.txt` |
| `bayan-lsp` memory bounds | A repo with 2.3 million `.byn` copies drove it past 200 GB; it now peaks at 81 MB; about 10 bytes per character for closed documents | `CHANGELOG.md` (Unreleased, Fixed) |
| CLI memory bounds M1–M7 | Test output capped at 64 KiB head and tail; program cache trimmed to 2 GiB and 4,000 entries; C compiler killed after 10 minutes | `CHANGELOG.md` (Unreleased, Fixed) |

The driver does `lex`, `parse --ast`, `types`, `check`, `build` (with `--emit-c`) and `run` (`docs/bayan-handoff.md`, Road to self-hosting, finish line).

## How far we came

Status is the `Status:` line of each `docs/specs/NNN-*/NNN-spec.md`.

| Spec | Title | Status line |
|---|---|---|
| 001 | Self-hosted lexer | implemented (laws L001–L004 await enactment) |
| 002 | Ownership and law alignment | done (T12/T13: L005–L008 await enactment) |
| 003 | Ownership verification | done |
| 004 | Port friction | done |
| 005 | Terminal style | done |
| 006 | Interfaces and iterators | done |
| 007 | Lambdas | done |
| 008 | HashMap and symbols | done |
| 009 | Self-hosted parser | done |
| 010 | Self-hosted semantics | done |
| 011 | Self-hosted emitter | draft (rev 3) |
| 012 | Self-hosted bootstrap | approved |
| 013 | Test tooling | done (2026-11-02) |
| 014 | std Path module | done |
| 015 | Tooling | decided (2026-11-02); ports wait for 012 (S1) |
| 016 | std library | no `docs/specs/016-*` directory exists |

Two status lines are stale. The handoff records 011 and 012 as done (`docs/bayan-handoff.md`, spec table), and `docs/laws/` holds 29 `status: enacted` lines and none proposed, so the 001 and 002 law notes are also out of date. Fixing them is a separate edit.

## Known gaps

| Gap | Detail | Source |
|---|---|---|
| Commands still C#-only | `new`, `init`, `test`, the cache and `--json` stay in the C# CLI | `docs/bayan-handoff.md` (finish line) |
| Not in the self-hosted driver | `parse` without `--ast`; `-o` into an existing directory reports BYN4001 where the C# reports a cache error; `Io.run` has no working directory, so diagnostics name `<dir>/main.c` | `docs/bayan-handoff.md` (Friction found porting the driver) |
| No terminal style in the selfhost | Lex and parse dumps, check summary, progress line and `--ascii`/`--color` differ from the C#; "a small spec after 012" | `docs/bayan-handoff.md` (same section) |
| Slow full tier | L028 full tier takes 13 m 51 s against `timeout: 900` (open) | `docs/bayan-handoff.md` (same section) |
| Bootstrap is per host | Byte identity is claimed for one host; cross-host identity is a future strengthening | `docs/laws/027-bootstrap-fixpoint.md`, `docs/bayan-handoff.md` |
| Windows | 0.1.0 shipped it as a preview with 82 failing tests; the list is now down to `SelfhostEmitterSpeedTests.OneMegabyteLowerAndEmitWithinTenTimes`. Spec 013 T19 and T27 were open at release | `CHANGELOG.md`, `tests/windows-known-failures.txt`, `docs/specs/013-test-tooling/013-results.md` |
| WASM | `bayan dist --target all` fails on wasm32; WASM is v0.3 | `CHANGELOG.md` (0.1.0 Known limits) |
| Language | No `List.sort(by:)`; no format specifiers such as `{x,8}` | `CHANGELOG.md` (0.1.0 Known limits) |
| Open laws | None. `CHANGELOG.md` says L018 and L023 stay proposed, but their files read `status: enacted`. Proposed-law count in `docs/laws/`: 0 | `docs/laws/018-no-parser-panic.md`, `docs/laws/023-no-checker-panic.md` |
| Spec 015 ports | No script is ported yet. Ports start after 012; the spec estimates 2026-11-20 to 2026-11-23. Remaining: mutation-exempt, review-brief, wave-brief, plan-strings-check, guard-checks, quality-baseline, mutate-plan, report-paths, manifest, coverage-gate, stryker-to-quality, replay | `docs/specs/015-tooling/015-spec.md` (Timing, Parity plan) |
| Round-4 backlog | Many items marked open (size S to L), grouped into decision batches | `docs/bayan-design-round-4-inventory.md` |
| Library | Questions Q1–Q12 await a decision (database names, crypto scope, `kit.tls` backend, `kit.xml`, `std.config`, operator forms) | `docs/bayan-library-tiers.md` §13.1 |

## Where we want to go

Only what the source documents already say.

| Target | What the sources say | Source |
|---|---|---|
| Round 4 | One batch of syntax and checks after 012, with one migration of goldens and selfhost source. First commit: flip the clause order to `where → expects → uses → becomes → fails` and run `bayan format` | `docs/bayan-design-round-4-draft.md` §7, `docs/bayan-handoff.md` (Design inputs) |
| v0.2 | Library and tooling: TinyCC in-process for `bayan run`, verified decoding (`@derive(Decode)`), `@packed` with endianness, bounds-check report, `InlineList<T, N>`, `Pair<T>`, `sum(as: .saturating)`, `then` stages, finish `law` blocks. The handoff adds the interpreter (comptime, scripts), generics, units, `Map`, iterators, `Io.Blocking`/`Threaded`, JSON/CSV, property and fuzz tests; the formatter, LSP, prose tool and packages land across v0.2–v0.4 | `docs/bayan-design-round-4-draft.md` §7, `docs/bayan-handoff.md` (After v0.1) |
| v0.2 concurrency and views | Thread-per-core fibers, `scope`/`spawn`: spec in round 4, runtime in v0.2; `kit.web` and `kit.ui` views v0.2+ | `docs/bayan-handoff.md` (Design inputs), `docs/bayan-library-tiers.md` §13.2 |
| v0.3 | Fibers and `Io.Evented`, networking, HTTP, TLS through a C wrapper, WASM; `.par()` parallel iterators. Later and on evidence only: compile-time evaluation of functions with empty `uses` (an interpreter inside the compiler), `@soa` layout | `docs/bayan-handoff.md` (After v0.1), `docs/bayan-design-round-4-draft.md` §7 |
| v1.x and later | `bayan verify` for `requires`/`ensures` contracts (v1.x); `bayan migrate cs` post-v1; `bayan-concurrency.md` supersedes the handoff's v0.3 fiber line | `docs/bayan-handoff.md` (After v0.1), `docs/bayan-library-tiers.md` §13.2 |
| 1.0 | (no plan yet): the three sources set no 1.0 scope or date | none |
| Retire the C# compiler | Ship the selfhost: `cc bootstrap/bayan.c` builds the compiler. .NET leaves "when self-hosting replaces the C# compiler" | `docs/bayan-handoff.md` (Target: zero dependencies), `docs/bayan-design-round-4-draft.md` §6 |
| Zero non-Bayān tooling | Port the scripts; Python, bash, Deno, PowerShell and Node dependencies each have a stated exit | `docs/specs/015-tooling/015-spec.md`, `docs/bayan-library-tiers.md` §12, `docs/bayan-handoff.md` |
| Std library | Tier order P0–P4: `core`, `std` for scripts and CLIs, `std` services, `kit` | `docs/bayan-library-tiers.md` §9 |
| Toolchain independence | Phase 2 any C compiler; phase 3 own sysroots, no Zig; phase 4 optional debug backend | `docs/bayan-design-round-4-draft.md` §6 |
| Security audit | (no plan yet). Related only: `SECURITY.md` for reports, `bayan audit` for escape hatches | `docs/bayan-going-public.md`, `docs/plans/2026-11-05-001-v0.2a-loop.md` |
| Benchmark suite | A suite that publishes losses (P1), with gates in v0.2a | `docs/bayan-design-round-4-draft.md` §4, `docs/plans/2026-11-05-001-v0.2a-loop.md` |
| Documentation site | Home page `https://bayan.run` is registered; generated documentation site waits for public release planning | `docs/plans/2026-11-02-003-move-to-ikhbat-org.md`, `docs/plans/2026-11-05-005-parking-lot.md` (D06) |
| Published spec | (no plan yet) | none |

## What it takes

| Goal | Next steps | Decision needed from the user | Source |
|---|---|---|---|
| Land 011 and 012 | Update the two stale spec status lines; add a 0.2.0 changelog section; tag a release | Whether to cut 0.2.0 now. The release gate is the memory and disk gate | `CHANGELOG.md`, `docs/memory-leaks.md` |
| Round 4 | Flip BYN1065, run `bayan format` on the repo, then the batch in §7 | Each open item in the inventory; the inventory groups them into decision batches | `docs/bayan-design-round-4-inventory.md`, `docs/bayan-design-round-4-draft.md` §7 and "Open for round 4" |
| Retire the C# compiler | Move `new`, `init`, `test`, the cache and `--json` into the driver; add terminal style to the selfhost; cut the L028 full tier below its timeout; then ship `bootstrap/bayan.c` builds as the compiler. Retirement date: (no plan yet) | Whether to ship the selfhost as the default binary; the style spec; the accepted divergences | `docs/bayan-handoff.md` (Friction found porting the driver) |
| Zero non-Bayān tooling | Port the scripts in the parity plan after 012, per script with a byte-parity check. Zig stays until phase 3; the Node actions stay | Check on 2026-11-16 whether 011 landed, else option A returns to the user; `kit.xml` (Q9) and the `kit.http` timing | `docs/specs/015-tooling/015-spec.md`, `docs/bayan-library-tiers.md` §12.5, §13.1 |
| Std library | Spec 016 does not exist. Start at P0–P1; `std.path` is done (spec 014) | Q1–Q12 in the tiers doc, notably crypto scope (Q2), `kit.tls` backend (Q8), `std.config` (Q11) | `docs/bayan-library-tiers.md` §9, §13.1 |
| v0.2 | Build the §7 library and tooling items (TinyCC `bayan run`, `@derive(Decode)`, `@packed`, bounds-check report, `InlineList`/`Pair`/`sum(as:)`, `then` stages, `law` blocks) and the handoff's v0.2 list (interpreter, JSON/CSV, property and fuzz tests). Start only after round 4 lands | Which §7 rows to build; `then` stages wait for Sūq evidence; TinyCC on macOS/Windows ARM64 or system `cc` (Q7) | `docs/bayan-design-round-4-draft.md` §7, `docs/bayan-handoff.md` (After v0.1), `docs/bayan-design-round-4-inventory.md` |
| v0.2 concurrency and views | Write the `scope`/`spawn` spec in round 4, then the runtime in v0.2; `kit.web`/`kit.ui` views are provisional, v0.2+ | Whether `scope`/`spawn` replaces `async`/`await` and `Io.group` (marked decided 2026-11-04 in the inventory); view design stays provisional | `docs/bayan-handoff.md` (Design inputs), `docs/bayan-library-tiers.md` §13.2, `docs/bayan-design-round-4-draft.md` §7 |
| v0.3 | Fibers and `Io.Evented`, networking, HTTP, TLS through a C wrapper, WASM, `.par()`. Steps beyond that list: (no plan yet). The runtime needs `sys/wait.h`, signals and `pipe` on wasm32 | `kit.tls` wraps a C library via `std.c` (Q8, decided batch 7); `kit.http` timing; the older v0.3 fiber line predates `bayan-concurrency.md` | `docs/bayan-handoff.md` (After v0.1, Design inputs), `CHANGELOG.md` (0.1.0 Known limits), `docs/bayan-library-tiers.md` §13.1 |
| v1.x and later | `bayan verify` and `bayan migrate cs`: (no plan yet) beyond the version tags; compile-time evaluation and `@soa` wait for evidence | When evidence is enough to schedule compile-time evaluation and `@soa` | `docs/bayan-handoff.md` (After v0.1), `docs/bayan-library-tiers.md` §10, `docs/bayan-design-round-4-draft.md` §7 |
| 1.0 | (no plan yet) | Scope and date of 1.0 | none |
| Toolchain independence | Phase 1 is now (driver interface, C inventory, libc behind `rt`); phase 2 after 012: `cc bootstrap/bayan.c`, clang, gcc and TinyCC drivers; phase 3 own sysroots, no Zig, only on evidence of need; phase 4 optional QBE-style debug backend | MacOS SDK header licensing and libc source size per target before phase 3 | `docs/bayan-design-round-4-draft.md` §6 |
| Security audit | (no plan yet) | Whether and when to commission one | none |
| Benchmark suite | v0.2a #7: baselines, dedicated machine, report-only for two weeks, then enforced | Compile-time budget target numbers (P7 values unset) | `docs/plans/2026-11-05-001-v0.2a-loop.md`, `docs/bayan-design-round-4-inventory.md` |
| Documentation site and published spec | Site: generated from the docs once public release planning starts. Spec publication: (no plan yet) | When public release planning starts; hosting at `bayan.run` is the user's DNS (D6) | `docs/plans/2026-11-05-005-parking-lot.md`, `docs/plans/2026-11-02-003-move-to-ikhbat-org.md`, `docs/bayan-going-public.md` |
