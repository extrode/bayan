# 012 — Plan
Spec: ./012-spec.md
Written 2026-10-22 against `d462122` (spec 010's docs on top of dev), with spec 011's docs read from the `worker-011-docs` worktree. Line numbers below are at that commit; round-3 steps 5.2b and 5.5 move `CliApp.cs` and `Messages.cs`, and T1 re-reads and re-sizes them.

## Approach

Port the part of the C# CLI the finish line names, the way 001 and 009–011 ported the compiler: one Bayan file per C# region, one Bayan method per C# method, same names in camel case, and a harness that diffs the Bayan output against the C# reference in process. The reference here is `CliApp.Run` with redirected streams (Q180), so the driver ports message text, exit codes and the footer as well as the work.

Three pieces of C# work come first (T1). The driver needs `Io.env`, `Io.currentDir` and `Io.createDir` (R7), which go into the C# compiler, the runtime and the spec with `run/` goldens, and then into 010's builtin table and 011's emitter, because the compiler that uses them must compile them. The four ported directories move into `selfhost/bayan/` (R8, Q178), so that `bayan build selfhost/bayan` works with the input form both compilers already have; the move edits no Bayan line, and the four law harnesses are re-pointed and re-run in the same change. And the runtime text becomes a generated Bayan file (R9).

The driver then grows command by command (T3–T6), each checked against `CliApp.Run` on stage 1, which the harness builds with `Toolchain.Build` as the earlier tools are built. Stage 1 is the first program built from `selfhost/bayan/` whose `main` is the driver, so it is also the first point where the whole compiler runs as one binary.

The bootstrap (T8–T10) only adds processes: stage 1 builds stage 2, stage 2 builds stage 3, every stage prints its `--emit-c` of the compiler, and stage 2 runs the goldens. By L018 and L019 every stage's C already equals `C0` if the stage binary behaves as its source says; L020 is the check that it does, which is what a self-hosted compiler can get wrong and the earlier laws cannot see.

## Tech

- Language: Bayan in round-3 syntax for `selfhost/bayan/main.byn` and `driver-*.byn`; C# for T1, the generator and the harness; C for T1's runtime functions.
- Uses from 006: `for x in list` through `IIndexable`; no interface of its own.
- Uses from 007: `string -> bool` predicates with `it` for the option scan and the refused-command check; no capture of `this` or a `var`; no function value in a field.
- Uses from 008: `Map<string, Command>` for dispatch, insertion order (L003) for `--help`'s command list.
- Uses from 009–011: the lexer's `lex` and `TokenDump` port, the parser's `parse` and `AstDump` port, 010's `Checker`, typed dump and `renderAll`, 011's `lower` and `emit`, each called from the driver exactly as the tool entries call them.
- New builtins: `Io.env`, `Io.currentDir`, `Io.createDir` (R7). Existing: `Io.run`, `Io.writeFile`, `Io.readText`, `Io.listDir`, `Io.stdin.readAll`, `Io.monotonicNanos`, `Io.stdout`/`Io.stderr` `write`, the `main(args)` entry and an `ExitCode` return.
- Build: `CliApp.Run(["build", "selfhost/bayan", "--mode", "safe", "-o", <stage1>])` in process for stage 1; stages 2 and 3 as processes; `BAYAN_CACHE=tmp/cache`.
- No new NuGet packages, no change to emitted C for programs that do not call the new builtins.

## Changes by area

### C# compiler, runtime and spec (T1)

- `src/Bayan.Semantics/Builtins.cs`: `IoEnv`, `IoCurrentDir`, `IoCreateDir` beside `IoReadFile` (`:29-37`), registered in the name switch (`:137-141`), with round-3 5.3d's effects (`env`, `fs`, `fs`); `IoEnv` returns `string?` and does not fail.
- `src/Bayan.Lowering/Lowerer.cs` and `src/Bayan.Backend.C/CEmitter.cs`: the three calls emit as the other `Io` builtins do; T1 names the methods after re-reading round 3's builtin emission.
- `src/runtime/bayan-rt.{h,c}`: `byn_io_env`, `byn_io_current_dir`, `byn_io_create_dir` next to `byn_io_write_file` (`bayan-rt.h:269`), POSIX and Windows bodies, `IoError` codes as `byn_io_error`.
- `docs/bayan-spec.md` §13: the three members, one line each, under the `Io` list; the handoff's "Questions" gets Q181 in T13.
- Goldens: `tests/cases/run/io-env.{byn,stdout}`, `io-current-dir.{byn,stdout}` (prints whether the result is absolute and ends with no separator), `io-create-dir.{byn,stdout}` (nested create, then `listDir`, then create again).
- Ports: `selfhost/semantics/builtins.byn` (010 T5's table) and `selfhost/backend/emit-expressions.byn` (011 T6) gain the three entries; L018 and L019 are re-run.
- Size: `wc -l` of `CliApp.cs`, `Messages.cs` and `Toolchain.cs` after 5.5; the method ranges in the table below are re-read.

### Layout move (T2)

| From | To |
|---|---|
| `selfhost/lexer/<f>.byn` (not `main.byn`) | `selfhost/bayan/lexer-<f>.byn` |
| `selfhost/parser/<f>.byn` (not `main.byn`) | `selfhost/bayan/parser-<f>.byn` |
| `selfhost/semantics/<f>.byn` (not `main.byn`) | `selfhost/bayan/semantics-<f>.byn` |
| `selfhost/backend/<f>.byn` (not `main.byn`) | `selfhost/bayan/backend-<f>.byn` |
| `selfhost/{lexer,parser,semantics,backend}/main.byn` | `selfhost/tools/{lex,parse,check,emit}.byn` |

- `git mv` only; a test asserts that every moved file's bytes equal its pre-move blob (by `git show` in T2's check, recorded in the commit message).
- Harnesses re-pointed: `tests/Bayan.Tests/SelfhostLexerTests.cs` (`LexerDir`, `:18`), `SelfhostParserTests.cs` (009), `SelfhostCheckTests.cs` (010), `SelfhostEmitTests.cs` (011): entry files are `selfhost/bayan/*.byn` minus `main.byn` and `driver-*.byn`, plus the tool file, package root `selfhost/bayan`.
- Law files: `docs/laws/001-*`, `017-*`, `018-*`, `019-*` frontmatter `scope` becomes `selfhost/bayan` (plus `selfhost/tools/<tool>.byn`); statements unchanged.
- `docs/bayan-self-hosting.md` and `selfhost/` READMEs, if any, name the new paths.
- Ordinal file order in one directory interleaves the four origins by prefix (`backend-` < `driver-` < `lexer-` < `parser-` < `semantics-`); declaration order changes, so `C0` and diagnostic order on the self-reach inputs change, on both sides alike.

### Runtime text (T2)

- `selfhost/bayan/driver-runtime.byn`: `const runtimeHeader: string = "…"` and `const runtimeSource: string = "…"`, escaped with the lexer's escape rules, one source line per string line joined by `\n` escapes.
- `tests/Bayan.Tests/RuntimeTextTests.cs` (new): generates the file from `Toolchain.RuntimeHeader` and `RuntimeSource`, compares, and rewrites under `BAYAN_UPDATE=1`.

### Bayan driver (T3–T7)

New files in `selfhost/bayan/`, one per C# region:

| Bayan file | C# source | Task |
|---|---|---|
| `main.byn` | `Program.cs` (16), `CliApp.Run` dispatch (`CliApp.cs:37-97`), `TakeGlobalOptions` (`:296-334`), exit codes (`:18-22`) | T3 |
| `driver-messages.byn` | the `Messages.cs` entries R1's commands print (usage replaced, Q179) | T3 |
| `driver-style.byn` | `Style.Plain`'s `Symbols` and the ASCII set from `TerminalStyle.cs` (Q187) | T3 |
| `driver-paths.byn` | `Path.GetFullPath`, `GetFileName`, `GetFileNameWithoutExtension`, `GetDirectoryName`, `Combine` for absolute POSIX paths (R4) | T3 |
| `driver-commands.byn` | `Lex`, `Parse`, `Types` (`:99-138`), `TryReadSingleFile` (`:804-819`) | T3 |
| `driver-options.byn` | `BuildOptions`, `ParseBuildOptions` (`:140-231`) without `--target`, `--diagnostics`, `--changed`, `--base`, `--json`, `--only`, `-v` | T4 |
| `driver-compile.byn` | `Compile` (`:241-272`) without the manifest branch, `IsScript` (`:343-349`), `Report` (`:277-290`), `Failed` (`:475-487`), `Check` (`:403-411`), `ProgramName` (`:490-497`), `Seconds` (`:468-469`) | T4 |
| `driver-toolchain.byn` | `FindCompiler` (`Toolchain.cs:82-92`), mode flags (`:197-203`), `Compile` (`:263-281`) without the cache, `Targets.ExecutableName` for the host, `CacheDirectory`'s `BAYAN_CACHE` read (`:43-57`) for Q183 | T5 |
| `driver-build.byn` | `Build` (`CliApp.cs:413-458`) without `PrepareOut`, the verbose block and `--target`; `RunProgram` and `Start` (`:499-535`) without the script cache | T5, T6 |
| `driver-runtime.byn` | generated (R9) | T2 |

`Compile`'s loading (`Compilation.FromPath`, `Compilation.cs:63-73`: a directory is its module, a file pulls in its whole directory unless it is a script, files in ordinal order, tests only with `includeTests`) is ported into `selfhost/bayan/semantics-compilation.byn` beside 010's loader if 010 T4 loaded only files and `main.byn` packages; T4 records which.

The C# driver measures `Stopwatch` time; the port uses `Io.monotonicNanos()` and formats seconds with two decimals from integer centiseconds, rounded as `ToString("0.00")` rounds (half away from zero), which the masking in R2 hides but T4 tests directly.

### Harness (T2–T11)

- `tests/Bayan.Tests/SelfhostDriverTests.cs` (new):
  - a `Lazy` stage 1 built through `CliApp.Run` (the same build T8 uses);
  - `Reference(args, cwd)`: `CliApp.Run(args, out, err, new Terminal(false, false, 0))` with `NO_COLOR=1` (set through the environment hook `TerminalStyle.Decide` reads), seconds masked;
  - `Actual(args, cwd)`: stage 1 as a process with the same environment, seconds masked;
  - `[Theory]` rows of R14, keyed `<command> <case>`; the pending list `tests/Bayan.Tests/selfhost-driver-pending.txt`, where a listed row that matches fails with "remove `<row>` from the pending list".
- `tests/Bayan.Tests/BootstrapTests.cs` (new):
  - `Stages`: a `Lazy` that builds stage 1, 2 and 3 into `tmp/bootstrap/stage{1,2,3}` and returns their paths and the time each took;
  - `[Fact, Trait("Law","L020")]` for R10 over safe and debug, naming the stage pair and the first differing line;
  - `[Theory, Trait("Law","L020")]` for R11 over `Golden.Cases` of `lex`, `parse`, `typed`, `diag` and `run` (minus `.test.byn`), run against stage 2; `run/` rows reuse `RunTests`' mode line, `.args` and `.stdin` handling;
  - `[Fact]` for R12: `bootstrap/bayan.c` equals `C0` (no C compile), rewritten under `BAYAN_UPDATE=1`;
  - `[Fact]` for R12's from-source build: the README command, run with `Toolchain.FindCompiler()`'s compiler, then `--emit-c` compared;
  - all skipped on Windows with Q186's reason.

### Docs (T1, T12, T13)

- `docs/bayan-spec.md` §13: three `Io` members (T1).
- `bootstrap/bayan.c`, `bootstrap/README.md` (T10).
- `docs/laws/020-bootstrap-fixpoint.md` (T12).
- `docs/bayan-handoff.md`: Q178–Q190 under Questions, row 012 marked done, the finish line marked reached for the hosts of Q186.
- `docs/bayan-self-hosting.md`: phase 4 done, "Bootstrapping from source afterwards" names `bootstrap/README.md`.
- `CHANGELOG.md`: one entry.

## Data and interfaces

### New diagnostic codes

None. The driver reports through `Messages` text and exit codes as the C# CLI does, and the three builtins fail with `IoError` at run time. A search of every worktree's `docs/` and `src/` at this commit finds no `BYN286x` or `BYN287x`; 012 reserves nothing. Ranges in use that a later need must avoid: dev BYN2700–2791; specs 006, 007, 008 BYN2800–2819, 2820–2829, 2830–2839; 009 BYN1070 (1071–1079 kept); 010 BYN2840–2849; 011 BYN2850–2859; the round-3 plan BYN1040–1062, 3201–3209, 3210, 5001–5008, 6001–6007, 7001–7004 and 8001.

### Builtins (spec §13 text)

| Member | Signature | Effects | Runtime |
|---|---|---|---|
| `Io.env` | `env(name: string) -> string?` | `env` | `byn_io_env(byn_str name, byn_str* out, byn_alloc* alloc) -> bool` |
| `Io.currentDir` | `currentDir() -> string fails IoError` | `fs` | `byn_io_current_dir(byn_str* out, byn_alloc* alloc) -> uint32_t` |
| `Io.createDir` | `createDir(path: string) fails IoError` | `fs` | `byn_io_create_dir(byn_str path) -> uint32_t` |

### Driver state (round-3 syntax)

```bayan
enum Command {
    lex
    parse
    types
    check
    build
    run
}

struct BuildOptions {
    path: string
    mode: BuildMode
    output: string?
    emitC: bool
    all: bool
    programArgs: List<string>
}

struct Toolchain {
    compiler: List<string>
    scratch: string
}

modeFlags(mode: BuildMode) -> List<string> {
    if mode == .debug {
        return ["-std=c11", "-fwrapv", "-ffp-contract=off", "-w", "-O0", "-g", "-DBYN_DEBUG"]
    }
    return ["-std=c11", "-fwrapv", "-ffp-contract=off", "-w", "-O2", "-DBYN_NDEBUG"]
}

findCompiler() -> List<string>? uses env, fs {
    let configured = Io.env("BAYAN_CC") ?? ""
    if configured.length > 0 {
        return splitOnSpaces(configured)
    }
    for candidate in [["zig", "cc"], ["cc"], ["clang"], ["gcc"]] {
        if starts(candidate) {
            return candidate
        }
    }
    return null
}
```

`BuildMode` is 011's. `starts` runs `<program> --version` through `Io.run` and treats `IoError.notFound` as absent, which stands in for `FindOnPath` (`Toolchain.cs:150`). The driver keeps no other state: every command reads its arguments, does its work and returns an exit code from `main`.

### CLI

As R1. Unsupported commands and options are refused by name before any work (R3). `help` prints the commands of R1 and the line "Other commands are in the C# `bayan`."

## Risks

- Round-3 drift in the CLI. 5.2b and 5.5 add commands and messages after this plan. Mitigation: T1 depends on 5.5 and re-sizes; a later C# CLI change that touches an R1 command fails R14 until ported.
- The move (R8). Four harnesses, four law files and every open branch that edits `selfhost/<dir>/` conflict with it. Mitigation: T2 lands as one change after 011 is on dev, with no other `selfhost/` branch open, and edits no Bayan line.
- Declaration order. One directory changes the order the compiler's files are declared in, so self-reach outputs and `C0` change. Mitigation: both sides change together; L018 and L019 compare them, not stored text.
- `Untrusted` arguments. If 5.3e wraps `main`'s arguments, passing a path to `Io.run` is BYN2786. Mitigation: R15's `trustUnchecked() because "…"` at the one place the driver forms the C compiler's arguments.
- Paths. `GetFullPath` on macOS resolves nothing but `.`/`..`; `/tmp` vs `/private/tmp` and case-insensitive volumes appear in rendered paths. Mitigation: the harness runs both sides from the same working directory with the same argument text, and R4 normalizes only what .NET normalizes.
- Captured output. `Io.run` buffers the program's output in memory; a `run/` case that prints a lot doubles its memory. Mitigation: R11 runs `run/` cases through `build -o` and executes the binary directly, so only R14's `run` rows use the capture.
- Cost. Three stages compile the whole compiler's C at `-O2` and R11 compiles every `run/` case again. Mitigation: Q190's 300 s line, measured in T11.
- Toolchain mismatch. Stage builds use the driver's compiler discovery, the C# side `Toolchain`'s; with `BAYAN_CC` unset both find the same compiler. Mitigation: the harness sets `BAYAN_CC` to `Toolchain.FindCompiler()` joined by spaces for stage processes.
- Fixpoint false pass. If both stages share a miscompilation that does not change emitted C, L020 holds while behaviour is wrong. Mitigation: R11 runs the goldens against stage 2, and L018/L019 tie stage 1's source to C#'s.

## Coverage check

| Req | Tasks |
|---|---|
| R1 | T3, T4, T5, T6 |
| R2 | T3–T6 |
| R3 | T3, T4 |
| R4 | T3, T4 |
| R5 | T5 |
| R6 | T6 |
| R7 | T1 |
| R8 | T2 |
| R9 | T2 |
| R10 | T8 |
| R11 | T9 |
| R12 | T10 |
| R13 | T8, T9, T10, T11 |
| R14 | T2–T7 |
| R15 | T3–T7 |
| L020 | T11, T12 |
| Q178–Q190 | T13 |
