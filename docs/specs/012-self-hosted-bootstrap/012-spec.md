# 012 — Self-hosting step 6: the driver and the bootstrap
Status: approved
Created: 2026-10-22
Revised: 2026-11-04: realigned with 011 as built (driver on top of `bayan-emit` and `selfhost/semantics/cli.byn`, no layout move, paths from spec 014's `Path`, laws L027/L028, no new builtins before 012 passes, C# ranges re-read on `feat/011-self-hosted-emitter`). 2026-11-04: Q1–Q9 decided by the user (see *Decisions*).

Depends on: 011 merged to `dev` with L024 and L025 enacted (011 T17–T20); spec 014's `combine`, `directoryName` and `fullPath` (as 014 R1/R2 specify; `fullPath` carries `alloc` only, user decision 2026-11-04, following the dispatcher decision Q753 of 2026-11-03 for `Path.relative`; `isRooted`, `relative` and `fileName` already landed in 011 P2); `scripts/selfhost/land.sh` with a `012` row (P1). Plan: `./012-plan.md`. Tasks: `./012-tasks.md`.

## Problem

Steps 1 to 5 ported the lexer (`selfhost/lexer/`, L001), the parser (`selfhost/parser/`, L017, L018), the checker (`selfhost/semantics/`, L022, L023) and the emitter (`selfhost/emitter/`, `bayan-emit`, L024, L025). Each is a separate test program built by a C# harness through `Compilation.Create(sources, packageRoot: selfhost/)` (Q430), and none of them is a compiler a user can run: nothing in Bayan reads a command line like `CliApp.Run` (`src/Bayan.Cli/CliApp.cs:37-106`), finds a C compiler (`src/Bayan.Backend.C/Toolchain.cs:83-91`), writes the runtime beside the generated C and links it (`Toolchain.cs:276-313`), or runs the result (`CliApp.cs:544-580`). The handoff's finish line (`docs/bayan-handoff.md`, "Road to self-hosting") is a Bayan-built compiler that runs `lex`, `parse`, `check`, `build` (with `--emit-c`) and `run` like the C# one, whose stage 2 and stage 3 C are byte-identical, which passes the golden suite, and whose C is committed as `bootstrap/bayan.c`. The A1 items K3–K6 (`docs/revisions/0016-addendum-a1-midflight.md`) are assigned to this step.

## Goals

- A Bayan driver, `selfhost/driver/` (new), that composes the existing lexer, parser, checker and emitter modules into one `bayan` executable with the C# CLI's command surface for `lex`, `parse`, `types`, `check`, `build`, `run`, `version` and `help`.
- The three-stage bootstrap: the C# compiler builds stage 1, stage 1 builds stage 2, stage 2 builds stage 3; the emitted C of stages 1, 2 and 3 are byte-identical.
- The golden suite passes against stage 2 for the commands it ports.
- `bootstrap/bayan.c` (new) committed, with the command that builds it without Zig (K5).
- K3 (the C compiler behind an interface), K4 (the runtime's libc list) and K6 (reproducible C) written down and tested.

## Non-goals

- The commands that stay in C# (handoff finish line): `test`, `new`, `init`, the build cache, `--json`; also `format`, `audit`, `features`, `explain`, `doctor`, `mutate`, `dist`, `cache`, `clean` and scripts. The driver refuses them (R3).
- Any change to emitted C, the runtime's behaviour, or the language before 012 passes (A1 ground rule, `docs/revisions/0016-addendum-a1-midflight.md`; round 4). This includes new runtime builtins (Q1) and K2's macros.
- Path normalisation of its own: every path operation is spec 014's `Path` module (R4).
- Porting `std` library source or `import std.x` (spec 015 S1 waits for 012).
- Retiring the C# compiler or the per-step test programs (`bayan-emit` and its siblings keep their harnesses).

## User stories

- As a contributor, I run `bayan build --package selfhost selfhost/driver -o tmp/bootstrap/stage2/bayan` with a Bayan-built `bayan` and get a compiler whose C equals what the C# compiler emits for the same sources.
- As a packager without .NET or Zig, I build `bootstrap/bayan.c` with a C11 compiler and get a working `bayan`.
- As a maintainer, I see a failing test when stage 2 and stage 3 C differ or when a golden case differs under stage 2.

## Requirements

**R1 Driver program.** `selfhost/driver/main.byn` (new) and its siblings in `selfhost/driver/` (new) form one program that imports `lexer`, `parser`, `semantics` and `emitter` under the package root `selfhost/`, as `selfhost/emitter/main.byn` does. It reuses, not copies, the existing entry points: `selfhost/semantics/cli.byn` (`t26Symbols`, `t26ParseBuildOptions`, `t26Compile`, `t26Report`, `t26Failed`) for option parsing, compiling and reporting, and `selfhost/emitter/main.byn`'s `t12Emit`/`t12Package` path for C. It dispatches like `CliApp.Run` (`CliApp.cs:50-105`): `version`/`--version` (`:52-55`), `help`/`--help` (`:58-62`), `lex`/`parse`/`types` (`:63-68`, bodies `:108-147`), `check` (`:77`, body `:448-456`), `build` (`:79`, body `:458-503`) and `run` (`:81`, body `:544-580`), with the global options of `TakeGlobalOptions` (`:315-353`) and the build options of `ParseBuildOptions` (`:151-249`) that are not refused by R3. Exit codes are `CliApp.cs:18-22` (0, 1, 2, 4; 3 is `test`'s and is never produced).

**R2 Output equivalence.** For every argument list the R2 corpus holds (the golden cases of R10 plus a usage corpus built from `CliTests.cs` argument lists the driver accepts), the driver's stdout, stderr and exit code equal `CliApp.Run`'s with the no-colour terminal (`CliApp.cs:27` `Terminal`) and the run's elapsed seconds (`CliApp.cs:513-514`) masked. The version line is Q3.

**R3 Refusals.** A command or option the driver does not port (the non-goals list, `lex --json`, the `ParseBuildOptions` flags `--json`, `--mutate`, `--coverage`, `--changed`, `--base`, `--only`) exits 2 with one stderr line naming it and pointing at the C# `bayan`; the exact text is Q4. An unknown command behaves as `CliApp.cs:100-104` (`UnknownCommand`, the `FormatterHint` for `fmt`, then usage, exit 2).

**R4 Paths.** Every path operation in `selfhost/driver/` goes through spec 014's `Path` module: `Path.isRooted`, `Path.relative`, `Path.fileName` (landed, 011 P2) and `Path.combine`, `Path.directoryName` (`string?`) and `Path.fullPath` (`uses alloc` only, like `Path.relative`: user decision 2026-11-04, following the dispatcher decision Q753 of 2026-11-03 for `Path.relative`; 014 R2 and 014 Q3 at `feat/014-std-path-module` 65591aba) from 014. Relative command-line paths resolve against the working directory through `Path.fullPath`. The driver adds no separator scanning, no `..` folding and no private path helper (the 010 copies at `selfhost/semantics/compile.byn:1271-1340` are 014 R7's to replace).

**R5 C compiler.** The driver reaches the C compiler only through an interface with the shape of K3's `ICCompiler` (compile and link for a target, `uses process`), written over `string` paths (the sketch's `Path` type does not exist; 014's `Path` is a module). The default implementation runs `zig cc`; `cc`, `clang` and `gcc` are siblings tried in `Toolchain.FindCompiler` order (`Toolchain.cs:83-91`), found through `Io.run`'s `PATH` search. No Zig-only flag and no `zig build`. Flags are `Toolchain.cs:198-204` (`-std=c11 -fwrapv -ffp-contract=off -w`, then the mode's), the compile and link line `Toolchain.cs:286-290` (`-lm`, or `-lshell32` on Windows). No compiler found exits 4 with `Toolchain.cs:189`'s text; a compiler failure is reported as `CliApp.cs:390-406` (`ToolchainError`, or BYN4001 with the C text). `BAYAN_CC` is Q1.

**R6 Build outputs.** `build` writes the generated C and the runtime files where the C compiler reads them, then links to the `-o` path or `CliApp.cs:473-477`'s default name; `--emit-c` writes the C as `CliApp.cs:468-472`. The driver has no build cache (`Toolchain.cs:44-57`, `:182-224` stay C#). Where the scratch files go is Q1.

**R7 Run.** `run` builds as R6, then runs the program with the arguments after `--` through `Io.run` and exits with its exit code. `Io.run` captures stdout and stderr and writes them after the program ends, where `CliApp.Start` (`CliApp.cs:573-580`) inherits them; the difference is Q5.

**R8 Runtime text.** The driver carries `src/runtime/bayan-rt.h` and `src/runtime/bayan-rt.c` (the C# embeds them, `Toolchain.cs:35-37`) as generated string constants in `selfhost/driver/runtime.byn` (new). A test fails when the constants differ from the files; `BAYAN_UPDATE=1` regenerates them (`tests/Bayan.Tests/Golden.cs:14` convention).

**R9 Stages and fixpoint.** Stage 1 is the driver built by the C# compiler; stage 2 is the driver built by stage 1; stage 3 by stage 2. C1, C2 and C3 (each stage's emitted C of the driver, `--emit-c`) are byte-identical, in `safe` and `debug` modes. Emitted C carries no timestamp and no absolute path, in code or `#line` (K6; the C# names files relative to the package root, `CEmitter.cs:387-394`). User builds share the same reproducibility goal, checked later by `bayan verify` (v0.2a, `docs/plans/2026-11-05-001-v0.2a-loop.md`; addendum A2.5). All three stages are built with one invocation, `build --package selfhost selfhost/driver`, run by the harness with the C# `bayan` for stage 1 and the previous stage for stages 2 and 3 (Q2).

**R10 Golden suite against stage 2.** The golden cases of `tests/cases/lex` (`LexerTests`), `parse` (`ParserTests`), `typed` (`TypedDumpTests`), `diag` (`DiagTests`) and `run` without `.test.byn` (`RunTests`) pass when run through stage 2 instead of `CliApp.Run`, each with the command its C# test uses. A case that cannot pass for a known cause is a listed skip with its Question, as 011's `Skip` constants.

**R11 Bootstrap C.** `bootstrap/bayan.c` (new) is C1 in `safe` mode, committed, and checked equal to a fresh C1 by a test. `bootstrap/README.md` (new) gives the command that builds it with a C11 compiler and no Zig (K5): `cc -fwrapv -ffp-contract=off bootstrap/bayan.c bootstrap/bayan-rt.c -lm` (Q6). `bootstrap/bayan-rt.h` and `bootstrap/bayan-rt.c` (new) are copies of `src/runtime/bayan-rt.{h,c}`, and a CI check fails when either copy is not byte-identical to its source.

**R12 Libc and C inventory.** The runtime's libc uses are listed (K4) in a file the test keeps exact, and the C emitted for the `run/` corpus is scanned for the K2 constructs of `docs/specs/011-self-hosted-emitter/011-c-inventory.md` (the secondary scan left to 012, `work/todo.md:458`). Neither changes emitted C; a direct libc call in emitted C is a row, not a fix (Q7).

**R13 Tests and gates.** The driver lands through `scripts/selfhost/land.sh` with the `012` row of `scripts/selfhost/specs.tsv`, the guard (`guard.sh`, `guard-checks.py`, `coverage-gate.py`), whose tests build and run the working tree (`guard.sh:55`, `:64`) and whose mutation step runs `bayan mutate` from the pinned bayan (`scripts/pinned-bayan.sh`, the CLI built from the tip of `dev`; `mutation-step.sh:13-14`, shared with `mutate-batch.sh`) and a review under `docs/specs/012-self-hosted-bootstrap/reviews/` (new, as `docs/specs/011-self-hosted-emitter/reviews/`). A harness (`SelfhostDriverHarness.cs`, new) builds stage 1 as `SelfhostEmitterHarness.cs` builds `bayan-emit`; expected values come from `CliApp.Run`, never typed by hand. New `.byn` lines meet spec 013's coverage gate (exemptions as `@unobservable because "…"`), and their mutants go into `tests/quality-baseline.json` through `mutate-batch.sh` (`mutation.yml`). Windows failures with a known cause go into `tests/windows-known-failures.txt` under a `# 012 Tn (Qn)` line (`scripts/ci/known-failures.py`). Which tests run per merge is Q8.

**R14 Source style.** Driver sources use round-3 syntax (Appendix A of `docs/bayan-spec.md`: names first only for local bindings, types first in fields, parameters and returns, `->` only in function types; `x.trustUnchecked() because "…"`) and pass `bayan format --check` run with the pinned bayan (`bash scripts/pinned-bayan.sh` prints its path; tooling runs that binary, never the working tree's, per the script's header).

## Acceptance criteria

- `SelfhostDriverTests` (new): R2 over the corpus, R3, R5's exit-4 path, R7.
- `SelfhostDriverRuntimeTests` (new): R8 constants equal the runtime files.
- `BootstrapTests` (new, `Law=L027`): stages 1–3 in `tmp/bootstrap/stage{1,2,3}`; C1 == C2 == C3 in `safe` and `debug`; no absolute path or timestamp in C1; `bootstrap/bayan.c` == C1 `safe`; the `bootstrap/README.md` command builds it with `cc` on the host and the result passes `version`.
- `SelfhostDriverGoldenTests` (new, `Law=L028`): R10 against stage 2.
- `SelfhostDriverInventoryTests` (new): R12.
- `docs/laws/027-bootstrap-fixpoint.md` and `docs/laws/028-driver-equivalence.md` (new) enacted by the user and proved by `laws-check.js --run` (`docs/laws/README.md:12`).
- `docs/bayan-handoff.md` row 012 done; Questions recorded.

## Law candidates

Numbering assumes spec 014 takes L026 (its `L0xx` `Path` agrees with .NET); 012 starts at L027. If 014 takes a different number, 012's shift to follow it.

*Approved (user 2026-11-05) with the amendments below.*

**L027 Bootstrap fixpoint.** For the driver's sources and each of `safe` and `debug`, the C the C# compiler emits, the C stage 1 emits and the C stage 2 emits are byte-identical, and contain no absolute path and no timestamp, all stages built on the same host. Cross-host byte identity for the same `--target` is a tracked future strengthening, not part of L027. Kind: property over the build; check `dotnet test -v q --filter "Law=L027"`.

**L028 Driver equivalence.** For every case of the R10 corpus and the R2 usage corpus, stage 2's stdout, stderr and exit code equal `CliApp.Run`'s with elapsed time masked (before comparing, every match of the regex `[0-9]+\.[0-9]{2}s` — the `build.seconds` and `test.seconds` messages, `CliApp.cs` `Seconds` — becomes `<t>s`, and every JSON `"durationMs": <number>` value — `TestReport.cs` — becomes `"durationMs": 0`; nothing else is masked); the comparison runs with stderr not a terminal and `CI` set. Kind: property; check `dotnet test -v q --filter "Law=L028"`, honouring `BAYAN_LAW_TIER` (unset = seeded subset, `full` = whole corpus) as L018/L023 do.

## Decisions

Per-spec numbering as 011 and 014; handoff numbers are assigned by the dispatcher. All nine were decided by the user on 2026-11-04: Q1, Q3, Q4, Q5, Q7, Q8 and Q9 take the default below; Q2 and Q6 are decided as their *Decided* line says, which replaces the default.

Open questions: none.

**Q1 Environment, working directory and scratch files.** The 2026-10-22 draft added `Io.env`, `Io.currentDir` and `Io.createDir` builtins. A1 (`docs/revisions/0016-addendum-a1-midflight.md`: no change to emitted C or language behaviour before 012 passes) and spec 015 S1 (new runtime builtins after 012) forbid that, and 015 S2 (`Env.get … uses env`) and S4 (`std.fs` `createDirectory`, `exists`) approved other names. *Default:* no new builtin. The working directory comes from `Path.fullPath`; the C compiler is found by trying R5's list through `Io.run`; `BAYAN_CC`, `BAYAN_CACHE` and `TMPDIR` are not read; scratch files (`main.c`, `bayan-rt.h`, `bayan-rt.c`) are written into the existing directory of the output and compiled with absolute paths (`-I <dir>`, `-ffile-prefix-map=<dir>=.`), since `Io.run` has no `directory:` (015 S6 is new). *Forecloses:* `BAYAN_CC` and a temp scratch directory in the self-hosted driver; a project build without `-o`, whose `out/<mode>/<target>/c/` the C# `PrepareOut` creates (`CliApp.cs:422-432`, called only when `-o` is absent, `:474-476`), fails instead, so that case leaves the R2 corpus. `-o` into a missing directory fails in both (`Toolchain.ReplaceFile`, `Toolchain.cs:221`, `:230-235`, creates no directory), but the driver fails earlier, writing its scratch files, with its own text, so that case leaves the R2 corpus too. Alternative for the user: a narrow A1 exception adding S2's `Env.get` and S4's `createDirectory` before 012, as 015 D3 was. *Decided (user 2026-11-04):* the default. *Revised (user 2026-11-05, handoff Q970):* three exceptions land before T3 completes: compile-time `Target.os`/`Target.arch`, std `Env.get(name) -> string?` under the `process` effect, and an is-executable query in std `Fs`; the rest of the default stands.

**Q2 Package root for stages 2 and 3.** `bayan build <dir>` uses the directory itself as the package root (`Compilation.FromPath`, `Compilation.cs:90-100`; imports resolve at `:187`), so neither the C# CLI nor an equivalent driver can build `selfhost/driver` importing `semantics` (Q430). A manifest build needs `src/<entry>/` (`CliApp.cs:265-282`), which `selfhost/` is not. *Default:* stage 1 is built by the harness through `Compilation.Create(sources, packageRoot: selfhost/)` (Q430 precedent); the driver's `build`, `check` and `--emit-c` accept a Bayan-only `--package <root>` (as `bayan-emit`'s `--package`, Q648 precedent for Bayan-only modes), used for stages 2 and 3. *Forecloses:* building the compiler with the plain C# `bayan build`; the `--package` flag is outside L028's corpus. Alternatives: a package-root option in the C# CLI (a C# change that emits no different C), or moving `selfhost/` to a `src/` manifest layout (the draft's R8 move, which breaks every 009–011 harness path). *Decided (user 2026-11-04):* harness plus `--package`, with `--package <root>` added to the C# CLI in the same change, so stages 1–3 use one identical invocation and the harness does not branch on which compiler it drives. `--package` is then in L028's corpus.

**Q3 Version line.** `CliApp.cs:33` prints `bayan <Version> (commit <sha>)` from `BuildInfo.Commit`, which the driver cannot know. *Default:* the driver prints `bayan <Version> (selfhost)` with `<Version>` = `Toolchain.CompilerVersion` (`Toolchain.cs:33`); R2 masks the parenthesised part for `version`. *Forecloses:* byte-identical `version` output and telling self-hosted builds apart by commit. *Decided (user 2026-11-04):* the default.

**Q4 Refusal text.** *Default:* `` error: `<what>` is not available in the self-hosted compiler; use the C# `bayan` `` (the draft's text), exit 2, registered nowhere in `Messages.cs` (the C# never prints it). *Forecloses:* nothing; the text is new. *Decided (user 2026-11-04):* the default.

**Q5 `run` stdio.** `Io.run` captures; `CliApp.Start` inherits, so an interactive program, stdin, or output order between stdout and stderr differ. *Default:* capture, then write stdout then stderr; R10's `run` cases compare stdout and stderr separately, cases reading stdin leave the corpus. *Forecloses:* interactive `run` and interleaved output until 015 S6 or a streaming `Io.run` exists after 012. *Decided (user 2026-11-04):* the default.

**Q6 Bootstrap command.** K5 says `cc bootstrap/bayan.c` with no other file; the C needs the runtime (`#include "bayan-rt.h"`, `bayan-rt.c`) and the flags `-fwrapv -ffp-contract=off -lm`. *Default:* `bootstrap/` holds `bayan.c` plus copies of `bayan-rt.h` and `bayan-rt.c` checked equal to `src/runtime/`, built with `cc -std=c11 -fwrapv -ffp-contract=off -O2 -DBYN_NDEBUG -I bootstrap bootstrap/bayan.c bootstrap/bayan-rt.c -o bayan -lm`. *Forecloses:* the literal one-file, no-flag command of K5; an amalgamated single file is a later choice. *Decided (user 2026-11-04):* `bayan.c` plus the runtime copies, with a CI check that `bootstrap/bayan-rt.{h,c}` are byte-identical to `src/runtime/`, so the copies cannot drift. The README command is `cc -fwrapv -ffp-contract=off bootstrap/bayan.c bootstrap/bayan-rt.c -lm`, and K5's wording in `docs/revisions/0016-addendum-a1-midflight.md` changes to that command. Amalgamating the runtime into `bayan.c` keeps one file, but the flags still make it more than one bare command, so the copies are the simpler honest option.

**Q7 K4 scope.** K4 says emitted code calls `rt`, never libc. Moving a direct libc call out of emitted C changes emitted C, which A1 (`docs/revisions/0016-addendum-a1-midflight.md`) holds until 012 passes. *Default:* 012 lists the runtime's libc functions and every libc call in the `run/` corpus's emitted C as rows; no emitted C changes. *Forecloses:* "emitted code never calls libc" holding at 012's end. *Decided (user 2026-11-04):* the default.

**Q8 Per-merge cost.** Three full builds of the compiler plus the golden suite under stage 2 are slow (the Q744 tier question). *Default:* `BootstrapTests` and `SelfhostDriverGoldenTests` honour `BAYAN_LAW_TIER`: unset builds stages 1 and 2 and compares C1 with C2 and a seeded golden subset; `full` (`laws-nightly.yml`, `pre-push-laws.sh`) builds stage 3 and runs every case. *Forecloses:* a stage-3 difference being caught before the nightly run. *Decided (user 2026-11-04):* the default.

**Q9 Stage 1 mode.** The harness builds `bayan-emit` in `fast` mode (`SelfhostEmitterHarness.cs`); R9 compares `safe` and `debug` C. *Default:* stages are built in `safe`; C is compared in `safe` and `debug` by `--emit-c --mode`. *Forecloses:* nothing beyond build time. *Decided (user 2026-11-04):* the default.
