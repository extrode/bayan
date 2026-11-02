# 015 — Bayān tooling: replace Python, Deno and other non-Bayān scripts
Status: decided (2026-11-02); ports wait for 012 (S1)
Created: 2026-11-02

Sources: `docs/handoffs/2026-11-02-spec-015-tooling.md` (the handoff, filed as 015 because 014 is the `Path` module) and
`docs/bayan-library-tiers.md` (library placement; it wins over the handoff). Decisions T1–T4 there are settled and not
re-proposed here. Porting starts after 012 passes (S1, which updates T3's "after 010"); the decisions are below.

## Problem
Building, testing and gating Bayān needs Python 3 (11 scripts plus 3 inline blocks), Deno (1 script and its test),
PowerShell (1 script) and Node (GitHub's artifact actions, the VS Code extension). Each is a runtime to install, pin and
audit, and none of them exercises Bayān. The user set a zero-dependency target on 2026-11-02 and asked that the repo's
tooling run on Bayān itself.

## Goals
- Every script in the repo is Bayān or shell, except the Keep list (R2).
- No CI step runs Python or Deno; only `upload-artifact`/`download-artifact` run on Node.
- Every library API a port adds lives in the module `docs/bayan-library-tiers.md` names, with tests.
- Each port matches the script it replaces on recorded inputs (R6) before the old script is deleted.

## Non-goals
- The C# compiler, its tests, Stryker (`scripts/quality/mutate.sh`) and `tools/Bayan.UnicodeGen` (C#): they leave with
  self-hosting (010–012), not here.
- Library scope beyond what a port needs (T1); `std.cli` derivation, `kit.tls`, native HTTP.
- Changing what any tool decides or prints. A port that finds a bug in its original records it in `work/todo.md` and
  matches the original first.

## Inventory
Found by extension, shebang, manifest (`package.json` only; no `deno.json`, `requirements*.txt` or `pyproject.toml`
exist) and every workflow step. `scripts/cleanup/` (19 one-off scripts, written for the user to run) is out of scope.

### Python (runtime: Python 3)

| File | Lines | What it does | Called from |
| --- | --- | --- | --- |
| `scripts/selfhost/mutation-exempt.py` | 26 | lines a `// mutation-exempt: Qnnn` marker skips, checked against the handoff | `guard.sh` |
| `scripts/selfhost/review-brief.py` | 68 | the reviewer brief for a 010 task (task text, C# ranges, files) | `land.sh` |
| `scripts/selfhost/wave-brief.py` | 111 | the writer brief for a 010 task; reads `BAYAN_BASE` | `land.sh`, by hand |
| `scripts/selfhost/guard-checks.py` | 115 | guard checks A–D (oracle, stubs, ctx sections, gating Questions) over `git diff` | `guard.sh` |
| `scripts/selfhost/coverage-gate.py` | 66 | changed-function coverage gate over `git diff -U0` and two JSON files | `guard.sh` |
| `scripts/selfhost/quality-baseline.py` | 74 | `check`/`update` of `tests/quality-baseline.json` | `mutate-batch.sh`, `land.sh`, `mutation.yml` |
| `scripts/selfhost/replay.py` | 68 | replays recorded runs (`runs.jsonl`: program, args, cwd, base64 stdin) under a mutant | `mutation-step.sh` (as `bayan mutate`'s test command) |
| `scripts/quality/stryker_to_quality.py` | 105 | Stryker JSON → `bayan-quality` JSON | `scripts/quality/mutate.sh` |
| `scripts/quality/cobertura_to_quality.py` | 105 | Cobertura XML → `bayan-quality` JSON | `scripts/quality/coverage.sh` |
| `scripts/ci/known-failures.py` | 59 | failed tests in `.trx` files vs `tests/windows-known-failures.txt` | `ci.yml` (Windows) |
| `scripts/unicode/gen-lowercase.py` | 76 | downloads `UnicodeData.txt`, regenerates the case tables in `src/runtime/bayan-rt.c` (rev 0015) | by hand |
| inline in `scripts/selfhost/mutate-batch.sh:24` | ~60 | the group/file/filter plan: glob `selfhost/**/*.byn`, brace-expand and match task patterns, shard | `mutate-batch.sh` |
| inline in `scripts/selfhost/mutate-batch.sh:91` | 12 | rewrites report paths repo-relative | `mutate-batch.sh` |
| inline in `scripts/selfhost/mutation-step.sh:24` | 1 | lists `binary<TAB>sites` from `manifest.json` | `mutation-step.sh` |

### Deno, PowerShell, JavaScript, other

| File | What it does | Runtime |
| --- | --- | --- |
| `scripts/plan-strings-check.js` (128) + `.test.js` (156) | plan diagnostic strings vs `src/` and `docs/diagnostics/` | Deno |
| `editors/visualstudio/install-textmate.ps1` (8) | copies the TextMate grammar into `%USERPROFILE%\.vs\Extensions\bayan` | PowerShell |
| `editors/vscode/extension.js`, `package.json`, `.vscodeignore` | the VS Code extension | VS Code's Node |
| `docs/assets/artifact.js` | browser behaviour for generated HTML docs; synced from `~/.claude/tools/html/` | browser |
| `.config/dotnet-tools.json` (`dotnet-stryker`) | C# mutation testing | .NET |

### Shell (bash) — already in the target form
`scripts/ci/install-zig.sh`, `scripts/ci/install-dotnet.sh` (on `chore/ci-shell-checkout-dotnet`), `scripts/pinned-bayan.sh`,
`scripts/quality/{coverage,mutate}.sh`, `scripts/selfhost/{guard,land,mutate-batch,mutation-step}.sh`, and the 12 `*.test.sh`
files. `mutate-batch.sh` (127 lines) is the only one past the ~50-line rule; its logic is the inline Python above, and
the rest is sequencing.

### Workflow steps

| Workflow | Step | Today |
| --- | --- | --- |
| `ci.yml` | checkout, setup .NET | JS actions; shell on `chore/ci-shell-checkout-dotnet` (T4, held for 013 T19) |
| `ci.yml` | Install zig | shell |
| `ci.yml` | Script tests | bash; the tests call Python |
| `ci.yml` | Test, Test (Windows), Publish (NativeAOT), Smoke test | `dotnet` |
| `ci.yml` | Check known failures (Windows) | `python -I scripts/ci/known-failures.py` |
| `mutation.yml` | checkout, setup .NET | JS actions; shell on the T4 branch |
| `mutation.yml` | Install zig, Build, Mutate | shell / `dotnet`; Mutate runs `mutate-batch.sh` (Python inside) |
| `mutation.yml` | Merge | `python3 -I scripts/selfhost/quality-baseline.py` |
| `mutation.yml` | upload/download artifact | JS actions (Keep) |

## Classification

| Item | Class | Reason |
| --- | --- | --- |
| VS Code extension | Keep | VS Code loads only JS (handoff §4) |
| `upload-artifact`, `download-artifact` | Keep | no supported shell equivalent (§4) |
| `scripts/pinned-bayan.sh`, `install-zig.sh`, `install-dotnet.sh` | Keep (shell) | run before any `bayan` exists (§4) |
| Stryker, `scripts/quality/mutate.sh`, `tools/Bayan.UnicodeGen` | Keep | C#-side tooling, leaves with self-hosting (§4 "C# compiler and its tests") |
| `docs/assets/artifact.js` | Keep | runs in the reader's browser; owned by `~/.claude` (S11) |
| `guard.sh`, `land.sh`, `mutation-step.sh`, `coverage.sh`, the `*.test.sh` files | Shell | sequencing; their callers switch to the ports |
| `mutate-batch.sh` | Shell, after its inline Python moves into a Bayān port (`tools/mutate-plan.byn`) | |
| `install-textmate.ps1` | Bayān (S10) | 3 file copies; Windows then needs only `bayan` |
| All 11 `.py` files and the 3 inline blocks | Bayān | parsing (JSON, XML, Markdown task lists, diffs) and decisions |
| `plan-strings-check.js` (+ its test) | Bayān | reads C# sources and a plan, regex matching |

## Requirements
- R1 Every item classed Bayān runs as `bayan run tools/<name>.byn` (a package under `tools/` only when shared) under the
  pinned `bayan` (`scripts/pinned-bayan.sh`), never the working tree's.
- R2 After the last port, the repo holds no `.py`, Deno or PowerShell file and no `.js` outside the Keep list.
- R3 Each library API a port adds is in the module the tiers doc names, declares its effects, takes outside data as
  `Untrusted<T>`, calls libc only through `rt`, and has `std.test` tests and doc comments; nothing a port does not use.
- R4 No new public API is written before the user approves its shape (*Decisions* below records the approved ones).
- R5 A port replaces its script in one commit: callers (CI, shell, docs) switch, the old script and its test go.
- R6 A port lands only after its parity check (below) passes on every recorded input, the old script's test cases among them.
- R7 When the last user of Python, Deno or PowerShell goes, that runtime leaves CI and the setup docs in the same commit.
- R8 A port that would fetch over HTTPS calls `curl` through the process builtin and records a `work/todo.md` line (§12.5).
- R9 Ports land only through the normal branch flow, between 010–013 task merges, and change no language behavior,
  emitted C, goldens, `lex-diff` or 010 parity (addendum A1).

## APIs per port
Status: **Exists** = v0.1 builtin in the compiler today; **Spec-only** = in the language spec, not built; **New** = in
neither, settled under *Decisions*; **014-path** = specified by spec 014 (`Path`), built there.

| API | Module (tiers doc) | Status | First port |
| --- | --- | --- | --- |
| `args`, `i32 main(ReadOnlySpan<string> args)` exit code | builtins | Exists | all |
| `readText`, `readFile`, `writeFile`, `listDir` | builtins (`fs`) | Exists | all |
| `print`, `print(…, to: .stderr)`, `stdin` | builtins (`console`) | Exists | all |
| `run(program, args)` → `exitCode`, `stdout`, `stderr` | `std.process` | Exists | `guard-checks` |
| string members: `lines`, `parts`, `trimmed`, `position`, `replaced`, `joined`, slices | `core` | Exists (v0.1 set) | all |
| `List`, `Map`, stable `sort` | `std.collections` | Exists (in part) | all |
| `Path.combine`, `fileName`, `directoryName`, `isRooted`, `fullPath`, `relative` | `std.path` | 014-path (spec 014) | `review-brief` |
| environment variable read | `std.env` | New (S2) | `wave-brief` |
| regex match, search, find-all, replace, escape | `std.regex` | New (S3) | none yet (S13) |
| `exists`, `createDirectory`, `rename`, `canonical` (symlinks resolved) | `std.fs` | Spec-only, API New (S4) | `wave-brief` |
| recursive glob / walk; shell-style pattern match (`fnmatch`) | `std.fs` | Spec-only, API New (S5) | `mutate-plan` |
| `run` with `directory:`, `input:` bytes, `env:` over the inherited env | `std.process` | New (S6) | `replay` |
| JSON read (decode to a struct or a tree) and write (2-space indent, Python's `json.dumps(indent=2)` layout) | `std.json` | Spec-only (decoders, §11), API New (S7) | `quality-baseline` |
| `Base64.decode` | byte codecs, module Open (Q3) | Spec-only (J83) | `replay` |
| XML pull reader (.trx, Cobertura) | `kit.xml` | Not built (S9) | — |
| `f64` formatting matching Python `repr`, for ratios in quality JSON | `core` | New (S8) | `stryker-to-quality` |
| Unicode data lines → tables | `core` strings + `std.fs` | Exists | `gen-lowercase` |
| HTTPS fetch | `kit.http` | Spec-only; `curl` via `run` meanwhile (R8) | `gen-lowercase` |

## Port order
Easiest first; each New API arrives with the first port that needs it. Group by group, after 010 lands.

| # | Port (`tools/…`) | Replaces | Introduces |
| --- | --- | --- | --- |
| 1 | `mutation-exempt.byn` | `mutation-exempt.py` | nothing new (S13) |
| 2 | `review-brief.byn` | `review-brief.py` | uses `std.path` (014) |
| 3 | `wave-brief.byn` | `wave-brief.py` | `std.env`, `std.fs` exists |
| 4 | `plan-strings-check.byn` | `plan-strings-check.js` + test (Deno leaves, R7) | — |
| 5 | `guard-checks.byn` | `guard-checks.py` | `std.fs` pattern match |
| 6 | `quality-baseline.byn` | `quality-baseline.py` | `std.json` read/write, `fs` real path |
| 7 | `mutate-plan.byn`, `report-paths.byn`, manifest listing | the 3 inline blocks | `std.fs` glob/walk |
| 8 | `coverage-gate.byn` | `coverage-gate.py` | — |
| 9 | `stryker-to-quality.byn` | `stryker_to_quality.py` | `f64` formatting (S8) |
| 10 | `replay.byn` | `replay.py` | `run` with `directory:`/`input:`/`env:`, `Base64` |
| 11 | not ported (S9) | `known-failures.py`, deleted with the C# suite | — |
| 12 | not ported (S9) | `cobertura_to_quality.py`, deleted with the C# suite | — |
| 13 | `gen-lowercase.byn` | `gen-lowercase.py` (Python leaves once the C# suite and its two S9 scripts go, R7) | `curl` shell-out (R8) |
| 14 | Bayān | `install-textmate.ps1` (PowerShell leaves) | per S10 |

## Decisions (user, 2026-11-02)
The questions S1–S12 and D1–D4 were answered on 2026-11-02. Signatures are the approved shapes; names follow the house
rule of no abbreviations. Nothing below is built before its first port (R4 now reads: built as approved here).

| # | Decision |
| --- | --- |
| S1 | **Wait for self-hosting (option B).** Library source, `import std.x` and new runtime builtins land after 012 passes (A1). Prep now: this spec, the CI chore (handoff §5.1), shell-only ports, parity baselines (`tests/parity/`). Revisit option A (a narrow A1 exception) if 011 + 012 run past 4 weeks; see *Timing* below. |
| S2 | `Untrusted<string>? Env.get(string name) uses env` (absent → `none`); no `set`. `run` takes `env:` (S6). |
| S3 | `std.regex`, RE2-style: classes, `\d\s\w`, anchors, groups, `?*+{m,n}`, alternation, lazy quantifiers; no backreferences or lookaround; linear time. Members as proposed: `Regex.parse(pattern) fails RegexError`, `matches`, `find → Match?`, `findAll`, `replaced(text, by:)`, `Match.group(i)`, `Match.span`, `Regex.escaped(text)`. |
| S4 | Path strings now, under `uses fs`: `bool exists(string path)`, `void createDirectory(string path, bool parents = true) fails IoError`, `void rename(string from, string to) fails IoError`, `string canonical(string path) fails IoError` (symlinks resolved). The same verbs later on capability `Dir` handles. |
| S5 | `List<string> glob(string pattern) uses fs` in `std.fs` (`**`, `*`, `?`, `[…]`, `{a,b}`; sorted; `/` separators); pure `bool Glob.matches(string pattern, string path)` in `std.path`. |
| S6 | Named arguments on `run`: `run(program, args, directory: string? = none, input: ReadOnlySpan<u8>? = none, env: Map<string, string>? = none)`. The environment is inherited; `env:` entries are added over it (user, 2026-11-02). A clean-environment switch (`inheritEnv: false`) is added only when a port needs one. No `Command` builder. |
| S7 | `std.json` in layers: a pull reader first, the tree (`JsonValue`: null, bool, number as `f64` plus its text, string, list, object in key order) built on the reader, `Json.encode(JsonValue v, i32 indent = 0)`; struct derivation later, on the reader. Decoded values are `Untrusted`. |
| S8 | Parity on float fields compares numbers, not bytes; `tests/quality-baseline.json` changes once when its writer moves (`1.0` → `1`). Measured on the pinned build: `5.0 / 6.0` → `0.8333333333333334`, `0.1 + 0.2` → `0.30000000000000004` (both as Python), `1.0` → `1`, `1e21` → `1e21` (Python `1e+21`). `1.0 → 1` is logged for round 4 (`docs/bayan-design-round-4-draft.md` §3). |
| S9 | `kit.xml` is not built (user, 2026-11-02): `known-failures.py` and `cobertura_to_quality.py` are not ported and are deleted with the C# test suite; R7 (no `python` in CI) holds from that commit. Both XML readers (`known-failures.py` over `.trx`, `cobertura_to_quality.py` over coverlet output) serve the C# suite, which self-hosting retires. |
| S10 | `install-textmate.ps1` is ported to Bayān (Windows then needs only `bayan`), using S4. |
| S11 | `docs/assets/artifact.js` is on the Keep list. |
| S12 | `Base64` lives in `std.encoding` with `Hex` (settles tiers Q3). |
| S13 | `mutation-exempt` is ported to Bayān with string members only (`lines`, `position`, slices, `i64.tryParse`), not with `std.regex` and not to awk (user, 2026-11-02). `std.regex` (S3) arrives with the first later port that needs it. |
| D1 | The macOS 10x timing test stays as it is (green three runs in a row). |
| D2 | The .NET SDK stays pinned exactly (10.0.403, sha512) with a monthly reminder to bump the patch. |
| D3 | The `bayan new --ci github` template moves to `actions/checkout@v5` now: the scaffolder is CLI code that 010 does not port, so this is an A1 exception the user granted. |
| D4 | Script mode imports `kit` explicitly; no auto-import (tiers Q10 dropped). |

### Timing
010 finished on 2026-11-01 (`Status: done`). A1 holds until 012 passes, so porting waits for 011 (lowering and the C
emitter: about 4,580 C# lines, matched on 269 `run/` goldens; spec drafted, no plan yet) and 012 (driver and bootstrap;
not yet specified). 010 took 6 days for 40 tasks. Estimate: 011 in 7–10 days, 012 in 5–7, so porting starts around
2026-11-20 to 2026-11-23, about 3 weeks out, under the 4-week bound. Check on 2026-11-16: if 011 has not landed, put
option A back to the user.

## Parity plan
One check per port, run before it lands: old script and port on the same inputs, comparing exit code, stdout, stderr and
every file written.

| Port | Inputs | Compare |
| --- | --- | --- |
| mutation-exempt | every `selfhost/**/*.byn` with the current handoff; the `mutation-exempt.test.sh` cases | bytes |
| review-brief, wave-brief | each of the 40 tasks in `010-tasks.md` on the current spec files; `wave-brief.test.sh` cases | bytes |
| plan-strings-check | the default plan, `--all-help`, each code; the Deno test's fixtures | bytes; exit code |
| guard-checks | the last 10 landed 010 tasks (`<base> <task> <globs>` from `land.sh` history); `guard-checks.test.sh` | bytes |
| quality-baseline | `check` and `update` over the last mutation run's reports; `quality-baseline.test.sh` | output bytes; written baseline bytes (S8 may make floats normalized) |
| mutate-plan, report-paths, manifest | `--shard k/4` for k=1..4, `--files` cases from `mutate-batch.test.sh` | bytes |
| coverage-gate | merge bases of the last 10 010 landings with their reports | bytes |
| stryker/cobertura-to-quality | `scripts/quality/fixtures/` plus one real Stryker and Cobertura report | JSON: bytes, or parsed-equal if S8 says so |
| replay | a recorded snapshot from `guard.test.sh`, with and without a mutant | exit code, stdout, stderr per record |
| gen-lowercase | `UnicodeData.txt` 16.0.0 (the script's `UNICODE_VERSION`) from `tmp/ucd-16.0.0/`; then a fresh download | regenerated `bayan-rt.c` bytes |
| install-textmate | a scratch `USERPROFILE` | file tree and bytes |

Baselines are recorded now, while the originals run (S1 prep): `tests/parity/<tool>/<case>/` holds a case's inputs and
the original's exit code, stdout, stderr and resulting files; `scripts/parity/run.sh <tool> [--impl '<port>']` replays
them (`--record` writes them, from the original only), and `scripts/parity/run.test.sh` in CI keeps them true against the
originals. A port passes `--impl "$(scripts/pinned-bayan.sh) run tools/<name>.byn"`; its commit deletes the original and
its `impl` line points at the port (R5). The last run's output goes into this spec's port record. `replay` (needs built
mutants), `gen-lowercase` (needs the Unicode download), the report-paths and manifest blocks (not runnable alone) and
`install-textmate` are recorded at their port.

## Acceptance criteria
- Given the repo after port 14, when searching by extension and shebang, then only the Keep list remains non-Bayān,
  non-shell (R2).
- Given CI after the last port and the C# suite's retirement (S9), then no step runs `python`, `python3` or `deno` (R7).
- Given a port, when its parity check runs, then every recorded input matches (R6), and its commit removes the old script (R5).
- Given a working tree whose compiler is broken, when a gate runs a port, then it runs the pinned build (R1).

## Law candidates
- For every ported tool and every recorded input, the port's exit code and outputs equal the original's (R6); holds
  only until the original is deleted, so a parity check, not a law.

## Open questions
- Conflicts found: the handoff's paths `GRAMMAR.md`, `HANDOFF.md`, `bootstrap/`, `npm run lsp`, `npm run grammar` and
  `tests/lsp-regress.js` do not exist here (the first two are `docs/bayan-grammar.md`, `docs/bayan-handoff.md`; the
  rest have no counterpart, so nothing to port).

## Port record
(Filled per port: commit, parity result.)
