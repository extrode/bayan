---
id: L011
title: Colour adds only escapes
status: enacted
kind: property
scope: src/Bayan.Syntax/Diagnostics/Style.cs, src/Bayan.Syntax/Diagnostics/DiagnosticRenderer.cs, src/Bayan.Cli/CliApp.cs
proof: tests/Bayan.Tests/L011ColourAddsOnlyEscapesTests.cs
check: dotnet test --filter Law=L011
enacted: 2026-10-21
---

## Statement
For every output shape and every environment (colour on/off, ASCII on/off), removing ANSI escape sequences from the coloured rendering gives exactly the `NO_COLOR` rendering. Colour adds escapes and nothing else (guide §1: meaning never depends on colour alone).

## Counterexample looks like
A coloured `◆ built tally (safe, macos-arm64) in 0.84s` that reads `◆ built tally … in 0.84s ok` once the escapes are stripped, a `✗` or `help:` printed only when colour is on, or a coloured line wrapped at a different column than its `NO_COLOR` twin.

## Proof notes
`tests/Bayan.Tests/L011ColourAddsOnlyEscapesTests.cs`, tagged `[Trait("Law", "L011")]`, strips `\e[…m` and compares. Unicode symbols: every `tests/cases/style/*.color` golden (written by `StyleTests`: a TTY of width 100, `COLORTERM=truecolor`, `BAYAN_THEME=dark`, `LANG=en_US.UTF-8`, `CI=1`) must equal its `.plain` twin (`NO_COLOR=1`), and the test checks there is one `.color` per shape. ASCII: each shape is rendered in memory with `--ascii` and colour on, and with `--ascii` and `NO_COLOR=1`. The 18 shapes cover diagnostics with an insertion fix, a removal fix and a related span, the error cap with and without `--all`, `build` success (cold, cached, `-v`) and failure, `check`, `run` failure, `test` (pass, fail, pending, `-v`, assertion values) and `doctor` (ok, failing); seconds, the host target, the version and temp paths are masked before comparing. Measured 2026-10-22 on macOS arm64: 18 shapes, 54 goldens, 19 test cases, about 10 s. Mutation: appending `" ok"` to `Style.Paint`'s colour branch for `Role.Success` failed the in-memory ASCII case for 6 shapes (`build-ok`, `build-ok-cache`, `build-ok-verbose`, `check-ok`, `doctor-ok`, `doctor-fail`; first `* built tally (safe, <host>) in 0.00s ok`); editing `built` to `built!` in `style/build-ok.color` failed the goldens case at line 4. On failure the test prints the shape and its first differing line.
