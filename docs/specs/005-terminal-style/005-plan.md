# 005 — Plan
Spec: ./005-spec.md

## Approach

Add one small style layer and route every human-facing line through it, rather than patching strings in place. `Style` (palette + symbols) lives beside the renderer in `Bayan.Syntax.Diagnostics`, because `DiagnosticRenderer.cs` cannot see `Bayan.Cli` and `tests/Bayan.Tests/DiagTests.cs:21` renders without the CLI. The colour and ASCII decision (R1–R3) is made once in `CliApp.Run` for stdout and stderr separately, replacing the two `[ThreadStatic] bool` flags at `CliApp.cs:73-74` with two `Style` values, so `Doctor`, the test runner and `Report` all consume the same object. CLI prose comes from a `Messages` registry that copies the `DiagnosticCodes` idiom (`Register` list, `{0}` templates, `DiagnosticCodes.cs:9-20`), which is NativeAOT-safe (no resx, no reflection) and already the pattern the repo calls a "table". The alternative of a resource-file catalogue was rejected: it needs `ResourceManager` reflection paths and buys nothing until a second language exists.

Output shapes are pinned by a golden triple harness that drives `CliApp.Run` with a fake `Terminal` and controlled env (the `With` pattern at `CliEnvironmentTests.cs:19-30`), and the same harness is the L011 proof. Three places where the spec conflicts with itself are decided conservatively rather than papered over: help rewording (R15) and the `pending` status (R10) necessarily move a handful of JSON goldens, so R18 is read as "JSON format and renderer do not change"; secondary spans (R4) get renderer support and a renderer-level golden, but no binder site is converted because doing so changes label text and thus JSON (see Q81); `-v` reports phases not modules (spec already says so), and the C compiler compiles and links in one invocation (`Toolchain.cs:206`), so "C compile" and "link" are one phase.

## Tech / dependencies

- C#/.NET 10 NativeAOT, xunit; no new packages. `Console.WindowWidth`, `Console.OutputEncoding.CodePage`, `System.Threading.Timer` are all AOT-safe.
- Env vars read in Cli only: `NO_COLOR`, `FORCE_COLOR`, `TERM`, `COLORTERM`, `BAYAN_THEME`, `COLORFGBG`, `LC_ALL`, `LC_CTYPE`, `LANG`, `CI`.
- SGR: truecolour `\e[38;2;R;G;Bm`; 16-colour `31/33/34/32`, dim `\e[2m`; reset `\e[0m` (`DiagnosticRenderer.cs:24-30` constants move into `Palette`).
- Runtime: one new C entry point `byn_todo_str` in `src/runtime/bayan-rt.c` (beside `byn_panic_str`, line 52) and its prototype in `bayan-rt.h`; exits the test child with code 4 (0 pass, 1 fail via `byn_report_error`, 2 bad index at `CEmitter.cs:122`, abort = panic).
- No new BYN codes, no new `docs/diagnostics` pages. `todo` is a builtin, not a diagnostic.

## Changes by area

- `src/Bayan.Syntax/Diagnostics/Style.cs` (new) — `Palette`, `Symbols`, `Style` records; truecolour light/dark and ANSI palettes with the guide's hex values; `Symbols.Unicode`/`Symbols.Ascii`. R1, R3.
- `src/Bayan.Syntax/Diagnostics/DiagnosticRenderer.cs` — `Render(d, includePath, Style style)` replaces `bool color` (keep a `bool color` overload mapping to `Style.Plain`/`Style.Ansi` for `DiagTests`). Header: `severity CODE` painted, message unpainted (drop the `Bold` at :46). `help:` in the help colour (:81), gutter/line numbers/`-->` in the note colour (:44, :51, :66). Fix lines gain a marker line `+++` under the replacement or `---` under a removed span (:82-89). New optional `Related` spans render as `---` with their label in the note colour. `RenderAll` gains `int errorLimit` and `out int omittedErrors`: errors past the limit are skipped, warnings never are. Prose lines (message, help) wrap at `style.Width`. R4, R5, R16.
- `src/Bayan.Syntax/Diagnostics/Diagnostic.cs:22-30` — add `IReadOnlyList<RelatedSpan>? Related = null` (record `RelatedSpan(TextSpan Span, string Label)`), not written by `DiagnosticJson.cs` (R18). R4.
- `src/Bayan.Cli/TerminalStyle.cs` (new) — `Decide(colorFlag, ascii, isTty, env)` implementing the R2 order, theme pick (R1), locale check (R3), width (R16). Returns `Style`. R1, R2, R3, R16.
- `src/Bayan.Cli/Messages.cs` (new) — the message registry; every string from the sites listed in R14 (`CliApp.cs` usage errors at 134, 193, 203, 218, 235, 245, 250, 278, 353, 362, 451, 471, 575-588, 648, 654, 678, 683; `Doctor.cs:32-33` and check texts; new summaries/footers/hints). R14.
- `src/Bayan.Cli/CliApp.cs` — `TakeColor` (:301) becomes `TakeGlobalOptions` returning `(color, ascii)`; `Run` (:80-85) builds `s_styleOut`/`s_styleErr`; `Terminal` (:71) gains `int Width`. `BuildOptions` (:173) gains `Verbose`, `All`, `Only string[]`; `ParseBuildOptions` parses `-v`/`--verbose`, `--all`, `--only <s>` (repeatable, test only). `Report` (:291) passes the limit (`20` unless `All`) and prints the cap line. `Check` (:399) prints `◆ checked …`; `Build` (:405) prints the success line with ` · from cache` from `NativeBuild.CacheHit` (`Toolchain.cs:14`), `-v` phase lines, and the failure footer plus `explain:` hint; `RunProgram` (:432) prints the footer only. `Test` (:487-559): header with module count (distinct `ModuleOf`), silent passes, `✗ module: name` blocks with parsed assertion/panic detail and frame trimming, pending from child exit 3, new summary, rerun hint, `--only` filtering (union; intersected with `--changed`), exit 0 when nothing failed. R5, R6, R7, R8, R9, R10, R13 hooks.
- `src/Bayan.Cli/Program.cs` — pass `Console.WindowWidth` (guarded) into `Terminal`. R16.
- `src/Bayan.Cli/Progress.cs` (new) — timer-driven in-place line on stderr after 300 ms; suppressed when not a TTY, `--json`/`--diagnostics json`, or `CI` set; ASCII prints `...` once. Used around compile, native build and the test loop. R13.
- `src/Bayan.Cli/Doctor.cs:29-35` — render `◆ item detail` / `✗ item: problem` + `  help: fix`; `Run` takes the stdout `Style`. R12.
- `src/Bayan.Cli/TestReport.cs:7, 38, 44-52` — `TestStatus.Pending`; `"status": "pending"`; `summary.pending`. R10.
- `src/Bayan.Semantics/Symbols/Symbols.cs:236` and `src/Bayan.Semantics/Builtins.cs:14-16, 142-144` — `BuiltinFunction.Todo`, `todo(message: string): never`. R10.
- `src/Bayan.Lowering/Lowerer.cs:941-948` — assert: when the condition is `BoundBinary { Op: Eq|Ne|Lt|Le|Gt|Ge }` over int/float/bool/string/enum, spill each non-literal side (`Spill`), test the temps, and panic through `LWrite(WriteSink.Panic, …)` with parts `"assertion failed: <text>\n  <left-text> is "`, `{tmpL}`, `"\n  <right-text> is "`, `{tmpR}`. Other conditions keep today's `LPanic`. `todo` lowers like non-interpolated `panic`. R10, R11.
- `src/Bayan.Backend.C/CEmitter.cs` — emit `byn_todo_str(msg, file, line)` for `BuiltinFunction.Todo` next to the `Panic` case. R10.
- `src/runtime/bayan-rt.c:32-56`, `bayan-rt.h` — `byn_todo_str`: flush stdout, `pending: <msg>\n  at file:line`, `exit(4)`. R10.
- `tests/Bayan.Tests/StyleTests.cs` (new) + `tests/cases/style/` — golden triples for every shape; also the L011 proof's input. R17.
- `tests/Bayan.Tests/ContrastTests.cs` (new) — parses §2 of `docs/bayan-terminal-style.md`, asserts 4.5:1 and agreement with `Palette`. R17.
- `tests/Bayan.Tests/HelpLanguageTests.cs` (new) — every `help:` line in `tests/cases/diag/**/*.diag` starts with a verb (allowlist of verbs plus named exceptions). R15.
- Binder help strings (`src/Bayan.Semantics/Binding/*.cs`, ~164 `help:` sites, of which the first-word census over the goldens shows roughly 70 not starting with a verb: "a …", "`s.data.copy(to:` …", "only …", "the …", "its …", "this …", "0 …") — reword to start with a verb; regenerate affected `diag/` and `diag-json/` goldens. R15.
- Pinned tests updated: `DiagnosticRendererTests.cs:34, 60-75, 79, 122`; `CliEnvironmentTests.cs:55-100, 114-138`; `CliTests.cs:357`; `tests/cases/out/results-fail.json` (new `pending` summary key); `OutputTests.cs:102` unchanged.
- `docs/bayan-terminal-style.md`, `docs/specs/005-terminal-style/005-spec.md`, `docs/bayan-handoff.md`, `CHANGELOG.md`, `tests/cases/README.md`, `docs/laws/011-*.md`, `docs/laws-and-decisions.md`. R19, L011.

## Data / interfaces

- `Palette(string Error, string Warning, string Help, string Success, string Note)`: SGR prefixes. Statics `TruecolorLight`, `TruecolorDark`, `Ansi16`, plus `Hex` table (`(role, light, dark)`) for the contrast test.
- `Symbols(string Dot, string Cross, string Bar, string Caret, string Ellipsis, string[] Progress)`: `Unicode` = `◆ ✗ │ ^ … [◆◇◇,◇◆◇,◇◇◆]`, `Ascii` = `* x | ^ ... [...]`.
- `Style(Palette? Colors, Symbols Symbols, int Width)`; `Style.Plain` = no colours, Unicode, width 100; `Paint(role, text)` returns `text` when `Colors` is null, so L011 holds by construction.
- `CliApp.Terminal(bool Stdout, bool Stderr, int Width = 0)`; 0 means unknown → 100.
- `Messages`: `public sealed record Message(string Id, string Format)`; `Messages.All`; ids are dotted, e.g. `build.built`, `build.failed`, `build.explain`, `check.checked`, `test.header`, `test.failure`, `test.summary`, `test.summary.ok`, `test.rerun.one`, `test.rerun.many`, `diag.capped`, `doctor.ok`, `doctor.fail`, `doctor.help`, `usage.unknown-command`, … Symbols and counts are arguments, never part of the template.
- Flags: `--ascii` global (stripped like `--color`, same `--`/script exemption at `CliApp.cs:305`); `--all`, `-v`/`--verbose` on check/build/run/test; `--only <substring>` repeatable on test. `--only` with no match and no `--changed` is a usage error (exit 2).
- Test child exit codes: 0 pass, 1 fail, 2 unknown index, 3 pending, abort panic.
- `results.json`/`--json`: `"status": "pending"`; `summary.pending` appended after `timedOut`; everything else unchanged.
- Golden triples: `tests/cases/style/<shape>.color`, `<shape>.plain`, `<shape>.ascii`. `.color` = terminal on, `COLORTERM=truecolor`, `BAYAN_THEME=dark`, `LANG=en_US.UTF-8`; `.plain` = same with `NO_COLOR=1`; `.ascii` = colour on with `--ascii`. Masks: `\d+\.\d+s` → `0.00s`, host target → `<host>`, version/commit → `<version>`/`<commit>`, cache path → `<cache>`. Shapes: `diag-error-fix`, `diag-removal-fix`, `diag-secondary` (hand-built `Diagnostic`, renderer only), `diag-cap`, `diag-cap-all`, `build-ok`, `build-ok-cache`, `build-ok-verbose`, `build-fail`, `check-ok`, `run-fail`, `test-ok`, `test-fail`, `test-pending`, `test-verbose`, `test-assert-values`, `doctor-ok`, `doctor-fail` (via `Doctor.Render(checks, style, writer)` over a fixed check list).
- Test runner lines: header `running N tests in M modules`; failure `  ✗ <module>: <name>` where module is the module directory's last segment (file stem for a loose file); detail indented 6 with `assert(<expr>)`, value lines indented 8, `file:line` last; panic detail `panic: <msg>` then up to three `at` lines (all with `-v`); `-v` pass line `  ◆ <module>: <name> · <s>s`; `-v` pending line `  pending <module>: <name>: <message>`.

## Risks & mitigations

- R15 and R10 move `diag-json` and `results-fail.json` goldens; R18 forbids that literally. Mitigation: only help text and one appended summary key change; the JSON writer is untouched; stated in handoff (Q79).
- Enum operand formatting in R11 depends on `CTypes.Format` supporting enums (`CEmitter.cs:1057`). If `$"{e}"` does not format enums today, R11 ships for int/float/bool/string and enum is noted, not invented.
- Contrast test may fail a guide colour (`#5F5E5A` on white is close to 6.5:1; needs computing, not trusting). Mitigation: the test is written first; a failing colour is fixed in the guide (R19), never silently in `Palette`.
- Progress uses a timer thread writing to stderr while the compiler runs on the main thread. Mitigation: it only writes `\r…` and `\r\e[2K`, never interleaves with diagnostics (started after `Compile` returns for the native phase; `Done()` before any stderr write), and is unit-tested with an injected threshold and a `StringWriter`.
- Wrapping must never touch source lines or fix lines. Mitigation: wrap only in a `Prose()` helper used for header/help/label text; goldens at width 100 and one narrow-width unit test.
- The 70-odd help rewordings are tedious and touch many binder files. Mitigation: the R15 test runs first with the allowlist equal to today's offenders, then the allowlist shrinks per batch; wording stays minimal ("a `move` …" → "move it …").
- Locale detection on Windows via `Console.OutputEncoding` throws when no console. Mitigation: try/catch, default ASCII off.

## Coverage check

- R1 → Changes (Style.cs, TerminalStyle.cs); R2 → TerminalStyle.cs, CliApp.Run; R3 → Symbols, TakeGlobalOptions, TerminalStyle locale; R4 → DiagnosticRenderer, Diagnostic.Related; R5 → RenderAll limit, Report, `--all`; R6 → Build, Compilation timings, Messages; R7 → Check, RunProgram; R8 → Test; R9 → `--only`; R10 → Builtins/Lowerer/CEmitter/bayan-rt.c/TestReport; R11 → Lowerer assert; R12 → Doctor; R13 → Progress.cs; R14 → Messages.cs; R15 → HelpLanguageTests + binder help strings; R16 → Style.Width, Terminal.Width, renderer Prose; R17 → StyleTests, ContrastTests; R18 → DiagnosticJson untouched, Related not serialised, JSON goldens checked unchanged except the two noted; R19 → docs task; L011 → prove task (test over `tests/cases/style`) and enact task.

## Task order

1. **Style types + contrast test** — `src/Bayan.Syntax/Diagnostics/Style.cs`, `tests/Bayan.Tests/ContrastTests.cs`. Done when the contrast test passes against the guide table and `Palette.Hex` matches it. R1, R3, R17.
2. **Renderer on Style** — `DiagnosticRenderer.cs`, `DiagnosticRendererTests.cs`. Done when plain output is byte-identical to today except the message is no longer bold in colour mode, and `ColorOnlyAddsEscapes` passes with the new palette. Depends on 1. R4 (header rule).
3. **Colour/ASCII decision in the CLI** — `src/Bayan.Cli/TerminalStyle.cs`, `CliApp.cs` (`Run`, `TakeGlobalOptions`, `Terminal.Width`), `Program.cs`. Done when `CliEnvironmentTests` covers `FORCE_COLOR`, `TERM=dumb`, `COLORTERM`, `BAYAN_THEME`, `COLORFGBG`, `LANG`, `--ascii`. Depends on 2. R1, R2, R3.
4. **Messages table** — `src/Bayan.Cli/Messages.cs`, `CliApp.cs` usage strings. Done when no literal human string remains in `CliApp`/`Doctor`/`Dist` (including `Dist.cs:104` `built …`) and existing CLI tests pass unchanged. R14.
5. **Fix markers and related spans** — `DiagnosticRenderer.cs`, `Diagnostic.cs`. Done when `RendersHandoffExample` matches the guide's §4 failure block shape (`+++` under `var`) and a `Related` unit golden renders `-----` in the note colour. Depends on 2. R4.
6. **Error cap and `--all`** — `DiagnosticRenderer.RenderAll`, `CliApp.Report`/`ParseBuildOptions`. Done when 25 errors give 20 plus the cap line, `--all` gives 25, warnings uncapped, JSON uncapped. Depends on 4. R5.
7. **Wrapping** — `DiagnosticRenderer.cs` prose helper, `Style.Width`. Done when a 180-column help wraps under its indent at width 100 and source lines never wrap. Depends on 2. R16.
8. **build/check/run footers and summaries, `-v` phases** — `CliApp.cs`, `src/Bayan.Semantics/Compilation.cs` (parse/check stopwatches). Done when `build-ok`, `build-ok-cache`, `build-ok-verbose`, `build-fail`, `check-ok`, `run-fail` shapes render as specified. Depends on 4. R6, R7.
9. **Doctor layout** — `Doctor.cs`, `CliEnvironmentTests.cs:114-138`. Done when the three doctor tests assert `◆`/`✗`/`help:` and `Doctor.Render` is callable from tests. Depends on 3, 4. R12.
10. **`todo` builtin and pending status** — `Builtins.cs`+`Symbols.cs`, `Lowerer.cs`+`CEmitter.cs`, `bayan-rt.c`+`bayan-rt.h`, `TestReport.cs`. Done when a `todo("x")` test exits 4, reports `pending` in JSON, and `results-fail.json` gains `pending: 0`. R10.
11. **Assertion values** — `Lowerer.cs:941`. Done when `assert(net == -34)` with `net` = -33 panics with `net is -33` on its own line and a `run/` golden pins it. Depends on 10 only for the test runner display. R11.
12. **Test runner output and `--only`** — `CliApp.Test`, `ParseBuildOptions`. Done when `test-ok`, `test-fail`, `test-pending`, `test-verbose`, `test-assert-values` shapes render, `--only cents` selects by substring, `CliTests.cs:357` asserts the new summary. Depends on 10, 11. R8, R9.
13. **Progress line** — `src/Bayan.Cli/Progress.cs`, hooks in `CliApp`. Done when a unit test with a 0 ms threshold sees the rewrite and clear sequence, the ASCII form prints `...` once, and `CI=1` prints nothing. Depends on 3. R13.
14. **Golden triple harness** — `tests/Bayan.Tests/StyleTests.cs`, `tests/cases/style/*`. Done when every shape listed under Data has three goldens and `BAYAN_UPDATE=1` regenerates them deterministically twice. Depends on 5–13. R17.
15. **Help language test and rewording** — `tests/Bayan.Tests/HelpLanguageTests.cs`, binder files + `diag`/`diag-json` goldens. Done when the test passes with an allowlist of at most the justified snippet-first helps. R15.
16. **Prove L011** — `tests/Bayan.Tests/L011ColourAddsOnlyEscapesTests.cs` (`[Trait("Law","L011")]`, strips `\e[…m` from every `.color` golden and from an in-memory `--ascii`+`NO_COLOR` render, asserts equality with `.plain`/`.ascii`), `docs/laws/011-colour-adds-only-escapes.md` as `status: proposed` with `proof`/`check`. Done when a mutation (a colour-only symbol) makes the test fail and `laws-check.js --run` passes. Depends on 14.
17. **Enact L011** — `docs/laws/011-*.md` status → `enacted` (with `enacted:` date), `docs/laws-and-decisions.md` entry. Done in the same change as T16's passing proof (standing authorization, 2026-10-21). Depends on 16.
18. **Docs** — `docs/bayan-terminal-style.md` (R2 order, R3 locale rule, R6 phases replacing the per-module `-v` example, R8 rerun forms and `-v` lines, R13 `CI`, R10 child exit 4, note that `link` is folded into `compile`), `005-spec.md` status, `docs/bayan-handoff.md` (progress entry + Q79–Q86), `CHANGELOG.md`, `tests/cases/README.md` (`style/` row). Depends on all. R19.

## Decisions for handoff Questions

- Q79. R18 scope: the JSON writers and renderer are untouched; `diag-json` goldens for reworded help lines and `results-fail.json` (new `summary.pending`) are regenerated. Any other JSON golden moving is a bug.
- Q80. `-v` phases are `parse`, `check`, `emit`, `compile` (the C compiler compiles and links in one call, `Toolchain.cs:206`; a cache hit prints `compile  from cache`). The guide's per-module example is replaced.
- Q81. `Diagnostic.Related` exists and renders, but no binder site is converted in 005: converting `BYN2104` from label "declared with `let` on line 9" to a secondary span changes its label text and so its JSON. Conversion of the 8 "on line {n}" sites is a follow-up with its own JSON decision.
- Q82. `severity CODE`, `help:`, and `^^^` keep bold plus colour; the message loses bold. Bold is weight, not a second colour role, and L011 holds either way.
- Q83. Test child exit code 4 means pending (not 3: `abort()` exits 3 on Windows, so a panic would read as pending); `todo(message)` is typed `never`, accepted anywhere, and in a normal program prints `pending: <message>` and exits 4.
- Q84. Rerun hint uses the full test name; with more than three failures no hint is printed. `--only` with no match and no `--changed` is a usage error; `--only` ∩ `--changed` empty prints `running 0 tests in 0 modules` and exits 0.
- Q85. Panic "frames" are the runtime's `at file:line` lines (`bayan-rt.c:40`); there is one today, so the top-three rule is implemented over `at` lines and takes effect when the runtime grows a backtrace.
- Q86. `.ascii` goldens are rendered with colour on; the fourth combination (ASCII, no colour) is checked only by the L011 proof, in memory.

Noticed, not fixed: `bayan parse` (`CliApp.cs:168`) renders without colour even on a terminal.
