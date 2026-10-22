# Bayan: terminal style

Most people meet Bayan in a terminal, not on a website. This guide defines how `bayan` output looks and behaves. It applies to every command and complements the diagnostic format in the spec (§21.1) and the handoff.

## 1. Principles

1. **Meaning never depends on colour alone.** Every line that matters starts with a word (`error`, `warning`, `help`, `note`) or a symbol with an ASCII fallback. Output must be fully usable with colour off.
2. **Quiet by default.** Successful commands print one short summary line. Detail on request (`-v`), or when something fails.
3. **Calm and precise.** No exclamation marks, no jokes inside diagnostics, no blame ("you forgot"). Describe the code, not the person.
4. **The brand is light-touch.** The diamond dot (◆) is the only brand element in the terminal. No logo, no mascot art in normal output.
5. **Machines get JSON.** `--diagnostics json` and `--json` produce structured output with no colour, no symbols and no progress lines.

## 2. Colour roles

Colours are chosen to pass WCAG AA (4.5:1) on typical light and dark terminal backgrounds. Bayan uses truecolour when the terminal supports it, and falls back to the standard 16 ANSI colours otherwise.

| Role | Light terminals | Dark terminals | ANSI fallback | Used for |
|---|---|---|---|---|
| Error | `#B42318` (6.6:1 on white) | `#FF8A80` (7.2:1 on `#14202E`) | red | `error` labels, `^^^` markers |
| Warning | `#8A5300` (6.3:1) | `#F2C14E` (9.8:1) | yellow | `warning` labels |
| Help, fixes | `#1F3A5F` (11.5:1) | `#8DB8F2` (8.1:1) | blue | `help` labels, suggested code |
| Success | `#2F6B1F` (6.5:1) | `#8FD694` (9.6:1) | green | Summary lines, passed counts |
| Note, secondary | `#5F5E5A` (6.5:1) | `#B4B2A9` (7.8:1) | default, dim | Gutters, timings, paths |
| Code | default foreground | default foreground | default | Source lines in diagnostics |

Light versus dark is detected from `COLORFGBG` where available; otherwise Bayan assumes dark and the user can set `BAYAN_THEME=light|dark`.

**When colour is used.** Decided separately for stdout and stderr; the first rule that applies wins:

| # | Condition | Colour |
|---|---|---|
| 1 | `--color always` / `--color never` | On / Off |
| 2 | `NO_COLOR` set to a non-empty value (per no-color.org) | Off |
| 3 | `FORCE_COLOR` set to a non-empty value other than `0` | On |
| 4 | `TERM=dumb` | Off |
| 5 | The stream is not a terminal (piped, redirected, CI log without TTY) | Off |
| 6 | Otherwise | On |

`FORCE_COLOR` is for CI systems that render ANSI colour in logs. An empty `NO_COLOR` or `FORCE_COLOR` is treated as unset.

**Which colours.** Truecolour (the hex values above) when `COLORTERM` is `truecolor` or `24bit`, otherwise the 16-colour fallback. The light or dark column comes from `BAYAN_THEME=light|dark`, else from the background field of `COLORFGBG` (0–6 or 8 is dark, 7 or 9–15 is light), else dark.

## 3. Symbols

| Symbol | Meaning | ASCII fallback |
|---|---|---|
| `◆` | Bayan's dot: success summaries, section markers | `*` |
| `✗` | A failed test | `x` |
| `│` | Diagnostic gutter | `\|` |
| `·` | Separator between summary counts and timings | `-` |
| `…` | Elision, as in the error cap line | `...` |
| `^` | Span marker | `^` |
| `◆◇◇` → `◇◆◇` → `◇◇◆` | Progress (animated, terminals only) | `...` printed once |

The fallback is used with `--ascii` (a global option, like `--color`), with `TERM=dumb`, or when the locale isn't UTF-8: the first non-empty of `LC_ALL`, `LC_CTYPE`, `LANG` doesn't contain `UTF-8` or `utf8` (any case). With all three empty, symbols stay Unicode. On Windows, the fallback is used when the console output code page isn't 65001.

## 4. Output by command

### `bayan build`

Success, default verbosity:
```
◆ built tally (debug, linux-x64) in 0.84s
```

With `-v`, one line per phase, then the output path:
```
  parse      0.04s
  check      0.11s
  emit       0.02s
  compile    0.67s
  out/debug/linux-x64/tally
◆ built tally (debug, linux-x64) in 0.84s
```

A v0.1 program is one C translation unit, so `-v` reports phases, not modules; the C compiler compiles and links in one call, so `compile` includes the link. A cache hit prints `compile    from cache` and adds ` · from cache` to the summary.

Failure:
```
error BYN2104: `x` is immutable
  --> src/main.byn:3:5

  3 │     x = 2;
    │     ^ declared with `let` on line 2
    │
  help: declare it with `var` if it should change
    │ 2 │     var x = 1;
    │   │     +++

✗ build failed: 1 error, 0 warnings
  explain: bayan explain BYN2104
```

`check` prints the same footer on failure and `◆ checked <name> in <s>s` on success. `run` prints only build diagnostics and the footer; the footer's `explain:` line names the first error's code.

Rules for diagnostics:
- One blank line between diagnostics; never more.
- The header line is `severity CODE: message`, with only `severity CODE` coloured.
- Paths are relative to the package root, shown as `--> path:line:column`.
- Related spans (`-----`, secondary) use the note colour; the primary span (`^^^^`) uses the severity colour.
- Fixes show the changed line with `+++` or `---` markers under inserted text. A fix that only removes text shows the original line with `---` under the removed part.
- Related spans (`Diagnostic.Related`) render, but no binder diagnostic carries one yet; labels such as "declared with `let` on line 2" stay in the primary label.
- Bold is weight, not a colour role: `severity CODE`, `help:` and `^^^` are bold as well as coloured; the message is not.
- After 20 errors, stop and print `… and 34 more errors (use --all to show them)` (ASCII: `...`). Warnings are never capped, and JSON output has no cap.
- Prose lines (message, labels, help) wrap at the terminal width, or 100 columns when it's unknown, continuing under their own indent. Source lines and code are never wrapped.

### `bayan test`

```
running 142 tests in 9 modules

  ✗ ledger: leftover cents go to the first participants alphabetically
      assert total(net) == -34
             │          │
             │          false
             -33
      ledger.test.byn:27

◆ 141 passed · ✗ 1 failed · 0 pending · 2.31s
  rerun the failure: bayan test --only "leftover cents"
```

- Passing tests print nothing individually unless `-v`.
- With `-v`, the header is followed by one line per passing test, `  ◆ <module>: <name>`, and per pending test, `  ◆ <module>: <name> (pending)` (the dot in the warning colour). Neither line carries a timing yet.
- A failed `assert` shows the statement, then the power-assert diagram (HANDOFF §16): a row of `│` under each sub-expression that is not a literal and has an integer, float, bool, string or enum value (under a name's first character, a member's name, a call's function name, an operator), then the values hung from those columns, rightmost first. Each value is evaluated once. The right side of `&&`, `||` and `??` and the branches of `?:` may not run, so they get no column. A value with no printable form (a struct, a list, an optional) gets no column either; its printable members still do. `--ascii` draws `|`. Value lines are cut at the terminal width with `…` (ASCII: `...`). An assertion with nothing to show prints the statement alone.
- Panics show the panic message and the top three stack frames; `-v` shows all.
- Pending (`todo`) tests are counted, and listed with `-v`.
- With everything passing: `◆ 142 passed · 2.10s`, plus ` · 3 pending` when some are pending.
- Up to three failures get one rerun line using the full test names: `rerun the failures: bayan test --only "a" --only "b"` (`rerun the failure:` for one). With more than three, no rerun line. Names are quoted for a POSIX shell: in double quotes when nothing in them expands there (`$`, `` ` ``, `\`, `"`, `!`), else in single quotes with `'` written `'\''`; other shells (cmd, PowerShell) may need the name requoted.
- The header counts modules (`1 module`, `9 modules`) and tests (`1 test`); with `--changed`, tests in unchanged modules are noted as skipped.
- `--only` that matches no test in the package is a usage error (exit 2), with or without `--changed`; when `--changed` leaves none of its matches, it prints `running 0 tests in 0 modules` and exits 0.
- Test names and test output are shown with control characters (U+0000–U+001F, U+007F, U+0080–U+009F) and bidi controls (U+061C, U+200E, U+200F, U+202A–U+202E, U+2066–U+2069) written as `\u{1b}`, so a name or a panic message cannot move the cursor, clear the screen or reorder the text after it. JSON output keeps them as they are. A failing test whose name was changed this way is left out of the rerun line (its shown name would match nothing); a line says a failing test's name contains control characters and cannot be selected from a shell.
- `--only <substring>` is repeatable and selects the union; with `--changed`, the intersection.
- A test child process exits 4 when the test is pending (`todo`). This is the child's code, not the CLI's; it isn't 3 because `abort()` exits 3 on Windows.

### `bayan run`

Prints nothing of its own on success: the program's output is the output. Build diagnostics, if any, go to **stderr** before the program starts. The program's exit code passes through.

### `bayan doctor`

```
◆ bayan       0.1.0 (commit a1b2c3d4e5f6)
◆ host        linux-x64
✗ C compiler: none found
  help: install zig (https://ziglang.org), or set BAYAN_CC to a C compiler
◆ zig         not found; building for other targets needs it
  help: install zig (https://ziglang.org)
◆ git         /usr/bin/git (git version 2.47.0)
◆ cache       ~/.cache/bayan (writable, 12 programs, 3.4 MB)
```

A check that cannot pass starts with `✗` and gets a `help:` line; an optional tool that is missing keeps `◆` and its `help:` line.

## 5. Streams and exit codes

- **stdout:** results (summaries, `--json` output, `bayan run` program output).
- **stderr:** diagnostics, progress, warnings.
- Exit codes: `0` success · `1` compile errors · `2` usage errors · `3` test failures · `4` toolchain problems. `bayan run` passes the program's own exit code through once the program has started.

## 6. Progress

- Shown only when stderr is a terminal and an operation takes longer than 300 ms.
- One line, rewritten in place: `◆◇◇ compiling tally.ledger (14 of 31)`. It is cut to one column less than the terminal width, ending in `…`, so it never wraps.
- Removed when the operation finishes, so only the summary remains in scrollback.
- Never shown when `CI` is set to a non-empty value, in `--json` / `--diagnostics json` mode, or when stderr is piped.
- The ASCII form prints `... <phase>` once and doesn't rewrite.
- The phase is `compiling`, `building` or `testing <name>`.

## 7. Language

- Messages are short and specific: "`total` is immutable", not "Cannot assign to variable because it is not mutable".
- Use the code's own names in backticks.
- Help lines start with a verb: "declare it with `var`", "copy it with `.copy(to: alloc)`".
- Messages come from message tables, so they can be translated later (Arabic first) without code changes. Codes, paths and code snippets are never translated.

## 8. Testing

- Every output shape above has a golden test, rendered three ways: colour (`.color`), `NO_COLOR` (`.plain`), and `--ascii` (`.ascii`, colour on). Goldens live in `tests/cases/style/`; see its README row.
- Law L011: removing ANSI escapes from the coloured rendering gives exactly the `NO_COLOR` rendering, for every shape, with and without `--ascii`.
- A contrast test checks every colour in section 2 against white and `#14202E` and fails below 4.5:1.

## 9. Environment and flags

| Name | Effect |
|---|---|
| `--color auto\|always\|never` | Colour for this run; wins over every variable (§2). |
| `--ascii` | ASCII symbols (§3). Global, like `--color`. |
| `--all` | Show every error instead of the first 20 (`check`, `types`, `build`, `run`, `test`). |
| `-v` | `build`: phase times and output path. `test`: also list passing and pending tests. |
| `--diagnostics json`, `--json` | Machine output: no colour, symbols, cap or progress. |
| `NO_COLOR`, `FORCE_COLOR`, `TERM=dumb` | Colour decision (§2); `TERM=dumb` also selects ASCII symbols. |
| `COLORTERM`, `BAYAN_THEME`, `COLORFGBG` | Truecolour or 16 colours, and the light or dark column (§2). |
| `LC_ALL`, `LC_CTYPE`, `LANG` | Locale check for the ASCII fallback (§3). |
| `CI` | Non-empty: no progress line (§6). |
