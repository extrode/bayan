# Changelog

## [Unreleased]

### Added
- Spec 004: arguments named like their parameter (`start`, `tok.start`) count as named, and swapped ones are BYN2307; contextual declaration keywords (`error`, `flags`, `type`, `law` and eight more usable as names; `law` no longer reserved); integer literal inference for unannotated `var`; `bayan types` typed dump; methods returning spans of their own fields; `string.from`/`tryFrom`/`invalidUtf8At` with `Utf8Error`; `catch { block }` (BYN2611); `const` string arrays; `List.removeRange`; integer `toHex()`; laws L009 and L010 enacted; a `catch` brace body of `name = value` fields with no `;` is a struct literal
- Spec 003: `x.take()` on `List`, `Map`, `StrBuf` and optionals of them (BYN2788); `switch (move x)` with owning pattern bindings (BYN2789, and BYN2790 for overlapping bindings that own storage, BYN2791 for a `when` guard that mutates an owning binding); a debug-build runtime ownership checker; a seeded ownership program generator; law L008 enacted with its proof, L006 amended with `take()` and `switch (move x)` rows
- Spec 002: `unique` collections with `move` and `.copy(to:)` (BYN2780-2784, BYN2762 retired); Map API per spec §16 (`map[k]`, `getOr`, `slot`, BYN2785); `bayan lex --json` and stdin for both lexers on shared Unicode tables; `Untrusted<T>`, `trust` and `Io.runBatch` (BYN2786, BYN2787); BYN2311 never-reassigned `var` lint; proofs for laws L001-L004
- Reserved keywords `law`, `requires`, `ensures` (BYN0012 as identifiers; handoff addendum 002)
- Solution, AOT CLI, diagnostics renderer, golden harness, CI workflow
- UTF-8 lexer, BYN0001-0016 with docs, bayan lex/explain
- M2 parser: AST, recovery, BYN1001-1016, parse/diag goldens
- M3 semantics: names, types, mutability, flow analysis, error sets; 95 diagnostics BYN2001-3001
- M4: lowering, C backend, runtime, bayan build/run, run goldens
- M5: error-handling run goldens, Result<T, E>, non-exhaustive catch diag
- M6: memory run goldens, region analysis BYN3002-3005
- M7: bayan test with per-test child processes
- M8: tally-lite example; v0.1 exit criterion met
- ReadOnlySpan<T>, BYN2310, shortest round-trip float formatting
- `--diagnostics json` and exit codes 0-4
- bayan new/init, package.byn reader, project builds, scripts
- out/ and dist/ layouts, --target, test --json, dist and clean (M10)
- shared build cache with worktree reuse, script reuse and cache stats (M11)
- M12b CLI completeness: color, bayan doctor, test --changed, --version with commit
- Spec 001 self-hosting step 1: `main` args and exit codes, `[ExitCode]`, attributes, `Io` files/streams/`listDir`/`run`
- Growable `StrBuf()`, `$"…"` as a string anywhere; plain enums, enums with data, static const arrays
- Tagged unions with payload patterns and exhaustive `switch`; user generics (monomorphized); `Map<K,V>`; `Owned<T>` recursive data
- BYN2762: copying a `Map` or `StrBuf` out of a place is rejected (shared heap storage)
- `tools/lex-diff` (first tool in Bayan), `tools/Bayan.UnicodeGen`, and the Bayan lexer in `selfhost/lexer/` checked against the C# lexer

### Fixed
- Windows `Io.run`: names with an extension are found, `.bat`/`.cmd` are refused; arguments are read as UTF-8 from `GetCommandLineW`
- I/O errors report the errno of the call that failed, including `fclose`; programs without tests never get the fixed test hash seed
