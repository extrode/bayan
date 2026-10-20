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

**When colour is used:**

| Condition | Colour |
|---|---|
| `--color always` | On |
| `--color never`, or `NO_COLOR` set to a non-empty value (per no-color.org) | Off |
| stdout is not a terminal (piped, redirected, CI log without TTY) | Off |
| `TERM=dumb` | Off |
| Otherwise | On |

`FORCE_COLOR` is honoured for CI systems that render ANSI colour in logs. An empty `NO_COLOR` is treated as unset. `--color` always overrides both variables.

## 3. Symbols

| Symbol | Meaning | ASCII fallback |
|---|---|---|
| `◆` | Bayan's dot: success summaries, section markers | `*` |
| `✗` | A failed test | `x` |
| `│` | Diagnostic gutter | `\|` |
| `^` | Span marker | `^` |
| `◆◇◇` → `◇◆◇` → `◇◇◆` | Progress (animated, terminals only) | `...` printed once |

The fallback is used when the locale isn't UTF-8 or `TERM=dumb`, or with `--ascii`.

## 4. Output by command

### `bayan build`

Success, default verbosity:
```
◆ built tally (debug, linux-x64) in 0.84s
```

With `-v`:
```
  compiling tally.money      0.21s
  compiling tally.ledger     0.33s
  compiling tally.app        0.08s
  linking   out/debug/linux-x64/tally
◆ built tally (debug, linux-x64) in 0.84s · 12 of 31 modules from cache
```

Failure:
```
error BYN2104: `total` is immutable

  --> src/ledger/ledger.byn:12:9
   │
 9 │     let total = 0;
   │         ----- declared with `let` here
   │
12 │     total += line.price;
   │     ^^^^^ cannot be changed
   │
help: declare it with `var` if it should change
   │
 9 │     var total = 0;
   │     +++

✗ build failed: 1 error, 0 warnings
  explain: bayan explain BYN2104
```

Rules for diagnostics:
- One blank line between diagnostics; never more.
- The header line is `severity CODE: message`, with only `severity CODE` coloured.
- Paths are relative to the package root, shown as `--> path:line:column`.
- Related spans (`-----`, secondary) use the note colour; the primary span (`^^^^`) uses the severity colour.
- Fixes show the changed line with `+++` or `---` markers under inserted or removed text.
- After 20 errors, stop and print `… and 34 more errors (use --all to show them)`.
- Lines wrap at the terminal width, or 100 columns when it's unknown.

### `bayan test`

```
running 142 tests in 9 modules

  ✗ ledger: leftover cents go to the first participants alphabetically
      assert(net["alice"] == -34)
        net["alice"] is -33
      src/ledger/ledger.test.byn:27

◆ 141 passed · ✗ 1 failed · 0 pending · 2.31s
  rerun the failure: bayan test --only "leftover cents"
```

- Passing tests print nothing individually unless `-v`.
- A failed assertion shows the expression, then the actual values of its parts.
- Panics show the panic message and the top three stack frames; `-v` shows all.
- Pending (`todo`) tests are counted, and listed with `-v`.
- With everything passing: `◆ 142 passed · 2.10s`.

### `bayan run`

Prints nothing of its own on success: the program's output is the output. Build diagnostics, if any, go to **stderr** before the program starts. The program's exit code passes through.

### `bayan doctor`

```
◆ bayan 0.1.0 (a1b2c3d)
◆ zig 0.14.1        /usr/local/bin/zig
◆ git 2.47.0
◆ cache             ~/.cache/bayan (1.2 GiB, writable)
◆ host target       linux-x64
✗ macos-arm64 cross-compilation: zig not found for this target
  help: install zig 0.14 or set BAYAN_CC
```

## 5. Streams and exit codes

- **stdout:** results (summaries, `--json` output, `bayan run` program output).
- **stderr:** diagnostics, progress, warnings.
- Exit codes: `0` success · `1` compile errors · `2` usage errors · `3` test failures · `4` toolchain problems. `bayan run` passes the program's own exit code through once the program has started.

## 6. Progress

- Shown only when stderr is a terminal and an operation takes longer than 300 ms.
- One line, rewritten in place: `◆◇◇ compiling tally.ledger (14 of 31)`.
- Removed when the operation finishes, so only the summary remains in scrollback.
- Never shown in CI, in `--json` mode, or when output is piped.

## 7. Language

- Messages are short and specific: "`total` is immutable", not "Cannot assign to variable because it is not mutable".
- Use the code's own names in backticks.
- Help lines start with a verb: "declare it with `var`", "copy it with `.copy(to: alloc)`".
- Messages come from message tables, so they can be translated later (Arabic first) without code changes. Codes, paths and code snippets are never translated.

## 8. Testing

- Every output shape above has a golden test, rendered three ways: colour, `NO_COLOR`, and `--ascii`.
- A contrast test checks every colour in section 2 against white and `#14202E` and fails below 4.5:1.
