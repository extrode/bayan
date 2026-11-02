# 005 — Terminal style: `bayan` output per `docs/bayan-terminal-style.md`
Status: done
Created: 2026-10-21

## Problem

`docs/bayan-terminal-style.md` is normative for CLI output (addendum 002 §6, handoff Q57), but the shipped output predates it. Some of it already matches: words carry meaning, the `--color` / `NO_COLOR` / non-TTY rules, the stream split and the exit codes. The rest does not:
- 16-colour SGR only, with help in cyan and the message bolded.
- No `◆`/`✗` symbols, no `--ascii`, no `FORCE_COLOR` or `TERM=dumb`.
- Silent `build` success, no failure footer, no error cap, no fix markers, no wrapping.
- `bayan test` prints every passing test, has no pending count and no rerun hint, and `assert` failures show only the expression text.
- `doctor` uses `ok`/`FAIL`/`fix:`.
- No progress line, no message tables.
- No colour / `NO_COLOR` / `--ascii` golden triples and no contrast test.

The road to self-hosting puts this before any renderer is ported again (specs 009–012 port the diagnostic renderer to Bayan), so the port copies the final format rather than one that is about to change.

## Goals

- Every rule and output shape in `docs/bayan-terminal-style.md` is implemented, or is stated as an explicit, recorded deviation in the guide itself.
- Every output shape has goldens rendered three ways: colour, `NO_COLOR`, and `--ascii`.
- The JSON outputs (`--diagnostics json`, `--json`) are unchanged.

## Non-goals

- Translations. Messages move into tables (R14) so they can be translated later; no second language ships.
- Per-module compilation or caching. v0.1 compiles one translation unit per program (handoff Q38), so `-v` reports phases, not modules (R6).
- New diagnostics or diagnostic wording changes beyond layout. Message text stays as it is; R15 audits help lines but changes only the ones that do not start with a verb.
- `bayan fmt`, LSP, and any renderer port to Bayan.

## User stories

- As a programmer at a terminal, I want a one-line success summary and failures that stand out, so I can see the result at a glance.
- As a programmer with a colour-blind or monochrome setup, I want every meaningful line to start with a word or a symbol with an ASCII fallback, so nothing depends on colour.
- As a CI system, I want colour forced on or off by environment, and no progress lines, so logs stay readable.
- As the author of the self-hosted renderer (spec 011), I want the output format pinned by goldens, so the port has a fixed target.

## Requirements

- R1 Colour roles. Error, warning, help/fix, success and note/secondary colours from guide §2. Truecolour when the terminal reports it (`COLORTERM` is `truecolor` or `24bit`), otherwise the 16 ANSI colours of the guide's fallback column. Light or dark palette from `BAYAN_THEME=light|dark`, else `COLORFGBG` (background 0–6 or 8 is dark, 7 or 9–15 is light), else dark.
- R2 Colour decision as in guide §2, in this order: `--color always|never` wins; then `NO_COLOR` non-empty → off; then `FORCE_COLOR` non-empty and not `0` → on; then `TERM=dumb` → off; then not a terminal → off; else on. Decided separately for stdout and stderr, as today.
- R3 Symbols `◆ ✗ │ ^` and their ASCII fallbacks `* x | ^`. The fallback is used with `--ascii`, with `TERM=dumb`, or when the locale is not UTF-8. The locale check: the first non-empty of `LC_ALL`, `LC_CTYPE`, `LANG` does not mention `UTF-8`/`utf8` (case-insensitive). On Windows the console output code page is not 65001. `--ascii` is a global option like `--color`.
- R4 Diagnostic layout per guide §4:
  - Header `severity CODE: message`, with only `severity CODE` coloured.
  - `--> path:line:column`, relative to the package root.
  - Gutter `│`.
  - Primary span `^^^` in the severity colour. Secondary spans `---` with their labels in the note colour, for diagnostics that carry related locations.
  - Exactly one blank line between diagnostics.
  - Fixes show the changed line, with `+++` under inserted text and `---` under removed text.
- R5 Error cap. After 20 errors, rendering stops and prints `… and N more errors (use --all to show them)`. The ASCII form is `...`. `--all` shows every error. Warnings are not capped, and the cap does not apply to JSON.
- R6 `bayan build` output:
  - **Success:** one line on stdout, `◆ built <name> (<mode>, <target>) in <s>s`, plus ` · from cache` when the build was a cache hit.
  - **`-v`:** before the summary, one indented line per phase (parse, check, emit, C compile, link) with its time, then the output path.
  - **Failure:** after the diagnostics on stderr, `✗ build failed: N error(s), M warning(s)`, then `  explain: bayan explain <code>` for the first error's code.
- R7 `bayan check` and `bayan run`. `check` follows R6's failure footer and prints a success summary `◆ checked <name> in <s>s`. `run` prints nothing of its own on success; build diagnostics and the failure footer go to stderr; the program's exit code passes through (unchanged).
- R8 `bayan test` output per guide §4:
  - A header `running N tests in M modules`.
  - Passing tests are silent unless `-v`.
  - Each failure is `  ✗ <module>: <test name>`, then its detail indented. Panics show the message and the top three stack frames (all with `-v`) when frames are available, otherwise the message alone.
  - The summary is `◆ P passed · ✗ F failed · T pending · <s>s`. With everything passing it is `◆ N passed · <s>s`, plus ` · T pending` when T > 0.
  - On failure, a rerun hint: `  rerun the failure: bayan test --only "<name>"`, or `rerun the failures: bayan test --only "<a>" --only "<b>"` for up to three failures.
- R9 `bayan test --only <substring>` runs the tests whose name contains the substring. It is repeatable, and selects the union. `--only` combined with `--changed` selects the intersection.
- R10 Pending tests. A test that calls `todo(…)` (spec §21) reports **pending**, never passed or failed. Pending tests are counted in the summary and listed with `-v`. `--json` and `out/test/results.json` gain a `pending` status.
- R11 Assertion values. When `assert(a op b)` fails and `op` is a comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`), the failure shows the expression, then `<a-text> is <value>` for each side that is not a literal, for integer, float, bool, `string` and enum operands. Other assertions show the expression alone, as today.
- R12 `bayan doctor` layout per guide §4: `◆ <item> <detail>` per check, `✗ <item>: <problem>` per failure with `  help: <fix>` under it.
- R13 Progress line per guide §6:
  - Shown only when stderr is a terminal and the operation passes 300 ms.
  - One line, rewritten in place (`◆◇◇ <phase>`), and removed when the operation finishes.
  - Never shown in `--json` / `--diagnostics json` mode, when piped, or when `CI` is set.
  - The ASCII form prints `...` once, without rewriting.
- R14 Message tables. Every human-facing CLI string (summaries, footers, headers, doctor lines, usage errors, hints) comes from one message table keyed by id. Codes, paths and code snippets are inserted as arguments, never translated. Diagnostic messages stay in `DiagnosticCodes`, which is already a table.
- R15 Language per guide §7. Every `help` text in the diagnostic table starts with a verb. A test enforces it, with an allowlist for justified exceptions.
- R16 Wrapping. Prose lines (messages, help, notes) wrap at the terminal width when stdout/stderr is a terminal, else at 100 columns. A wrapped line continues under its own indent. Source lines and code are never wrapped.
- R17 Testing per guide §8:
  - Every output shape (R4–R8, R12, R5's cap line) has a golden rendered three ways: colour, `NO_COLOR`, and `--ascii`.
  - A contrast test checks every hex colour in R1 against white and against `#14202E`, and fails below 4.5:1.
  - The guide's tables are the test's source of truth, read from `docs/bayan-terminal-style.md` or mirrored with a test that they agree.
- R18 Machine output is unchanged: `--diagnostics json`, `--json`, `bayan lex --json` and `bayan types` contain no colour, symbols or progress, and their goldens do not move.
- R19 The guide is updated wherever the implementation settles something it leaves open: the R2 order, the R3 locale rule, the R6 `-v` phases, the R8 multi-failure rerun form, and the R13 `CI` rule.

## Acceptance criteria

- Given `NO_COLOR=1`, when `bayan build` fails, then stderr contains no escape sequences and still starts each diagnostic with `error BYN`.
- Given `FORCE_COLOR=1` and piped output, then colour is on. Given `--color never` as well, then it is off.
- Given `TERM=dumb`, then output has no colour and uses `*`, `x`, `|`.
- Given 25 errors, then 20 render, followed by `… and 5 more errors (use --all to show them)`. With `--all`, all 25 render.
- Given a passing build, then stdout is exactly one `◆ built …` line.
- Given a test suite with one failure and one `todo`, then the summary is `◆ N passed · ✗ 1 failed · 1 pending · …s` and a rerun line follows.
- Given `assert(net == -34)` with `net` = -33, then the failure shows `net is -33`.
- Given `bayan test --only cents`, then only tests whose names contain `cents` run.
- Given any colour in the guide's table, then its contrast against both backgrounds is at least 4.5:1.
- Given the existing JSON goldens, then they pass unchanged.

## Law candidates

- L011 (property): for every output shape and every environment (colour on/off, ASCII on/off), removing ANSI escape sequences from the coloured rendering gives exactly the `NO_COLOR` rendering. Colour adds escapes and nothing else (guide §1: meaning never depends on colour alone).

## Open questions

- None blocking. Conservative choices taken above: phases instead of modules for `-v` (R6), frames only when available (R8), assertion values only for top-level comparisons over printable types (R11), and `CI` suppressing progress (R13).

## Not met

All ten acceptance criteria were re-read against the code and the `tests/cases/style/` goldens and hold, with these exceptions and gaps in the requirements behind them:
- "Given the existing JSON goldens, then they pass unchanged" holds for the writers and renderer but not for the files: the help rewording (R15) moved `diag-json` goldens and `pending` added `summary.pending` to `results-fail.json` (handoff Q79).
- R4: paths are relative to the package root for projects but absolute for loose files (`--> <dir>/tally/main.byn`); `work/todo.md` has the item.
- R4: related (`---`) spans render, but no binder diagnostic carries one, so none appears in a real run (handoff Q81).
- R6: `-v` reports `parse`, `check`, `emit` and `compile`; link is folded into `compile` because the C compiler links in the same call (handoff Q80).
- R8: the `-v` pass line has no timing, and the `-v` pending line is `  ◆ <module>: <name> (pending)` rather than the plan's `  pending <module>: <name>: <message>`; `work/todo.md` has both.
- R8: panics show the runtime's single `at file:line` line, so the top-three-frames rule has nothing to cut yet (handoff Q85).
