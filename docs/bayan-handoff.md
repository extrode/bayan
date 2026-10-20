# HANDOFF — Bayan v0.1 bootstrap compiler

You're picking up a language that has been designed but not yet built. **Read `SPEC.md` first.** This file says what to build for v0.1, in what order, and how to know each step is done.

## Ground rules for this work

1. **`SPEC.md` is the source of truth.** If something in the spec looks wrong or underspecified, don't silently decide: add it to *Questions* at the bottom of this file and pick the most conservative option until it's answered.
2. **Appendix A of the spec lists settled decisions. Do not re-propose them.**
3. Keep this file current: update *Progress log* at the end of every working session.
4. Prefer small, test-backed steps. Every milestone has acceptance criteria; a milestone isn't done until they pass in CI.

## What v0.1 is

A bootstrap compiler, written in **C# on .NET 10, published with NativeAOT**, that compiles a useful subset of Bayan to C and then to a native executable via `zig cc`.

**Exit criterion:** `bayan run examples/hello/main.byn` prints `Hello, world!`, and `bayan test examples/tally-lite` passes (a reduced version of the tally example, defined in M8).

The bootstrap compiler is temporary: Bayan will eventually self-host. Optimize for clarity and correctness first, then speed.

### Explicitly out of scope for v0.1

Comptime beyond constant folding · the DIR interpreter · async and `Io.Evented` · user-defined generics (built-in `Span<T>` and `List<T>` only) · derivations (except automatic equality) · interfaces and `dyn` · unions · Java-style enum data · units · regions for borrowed **fields** (see M6) · packages and `package.byn` beyond a minimal form · FFI beyond the runtime's own C · prose translation · LSP · formatter.

## Stack and constraints

- C# / .NET 10, `PublishAot=true`, trimming warnings as errors. No reflection, no `Reflection.Emit`.
- Hand-written lexer and recursive-descent parser with Pratt-style expression parsing. No parser generators.
- Performance-minded code where it matters: the lexer works over `ReadOnlySpan<byte>` (UTF-8 source), tokens are structs, and source positions are offsets into the file rather than copied strings.
- C output is **C11** and is compiled with `zig cc` (found on `PATH`, or via the `BAYAN_CC` environment variable). Default flags: `-std=c11 -fwrapv -ffp-contract=off` (`-fwrapv` applies to `fast` builds; `debug`/`safe` use checked arithmetic; see M4).
- Target platforms for CI: Linux x64, macOS arm64, Windows x64.

## Repository layout

```
bayan/
  SPEC.md
  HANDOFF.md
  src/
    Bayan.Cli/              bayan executable: run, build, test, explain, --version
    Bayan.Syntax/           source text, lexer, tokens, parser, AST, diagnostics
    Bayan.Semantics/        binder, symbols, types, checks
    Bayan.Lowering/         typed AST → lowered tree (seam for DIR later)
    Bayan.Backend.C/        C emitter, toolchain invocation
    runtime/                bayan_rt.h / bayan_rt.c: panic, print, bounds checks, arena, list
  tests/
    Bayan.Tests/            test runner for golden files
    cases/
      lex/        *.byn → *.tokens
      parse/      *.byn → *.ast
      diag/       *.byn → *.diag        (expected diagnostics, exact text)
      run/        *.byn → *.stdout      (+ optional *.exitcode)
  docs/
    diagnostics/BYN0001.md …            one file per diagnostic code (`bayan explain`)
  examples/
    hello/main.byn
    tally-lite/…
```

## Conventions

### Diagnostics
- Every diagnostic has a code `BYN####`, a span, a message, an optional **help** line, and an optional **fix** (replacement text plus span).
- Codes are allocated in ranges: `0001–0999` lexer, `1000–1999` parser, `2000–3999` semantics, `4000–4999` backend/toolchain.
- Rendered format (snapshot-tested):

```
error BYN2104: `total` is immutable

  12 │     total += line.price;
     │     ^^^^^ declared with `let` on line 9
     │
  help: declare it with `var` if it should change
     │  9 │     var total = 0;
```

- Each code has `docs/diagnostics/BYN####.md` (why it exists, an example, the fix). `bayan explain BYN2104` prints it.

### Golden tests
- One input file per case; expected output beside it. `dotnet test` runs everything.
- `BAYAN_UPDATE=1 dotnet test` rewrites expected files; diffs are reviewed like code.
- Every new diagnostic needs at least one `diag/` case. Every runtime feature needs a `run/` case.

### C emission
- Names: `byn_<module>_<name>`; struct `T` → `typedef struct byn_<module>_T byn_<module>_T;`.
- `string` → `typedef struct { const uint8_t* ptr; int64_t len; } byn_str;`. Literals are static arrays.
- `Span<T>` → `{ T* ptr; int64_t len; }`, one instantiation per element type.
- Panics: `byn_panic(const char* msg, const char* file, int line)` prints `panic: <msg>` plus location and calls `abort()`.
- `debug`/`safe`: arithmetic through `__builtin_add_overflow` and friends, panicking on overflow. `fast`: plain arithmetic with `-fwrapv`.
- **Bounds checks in every mode** (spec principle 9). Remove only checks the compiler proves redundant (start with `for (let i in 0..xs.length) xs[i]`).
- Emit `#line` directives so debuggers point at `.byn` lines.

## Milestones

### M0 — Skeleton
- Solution layout above; `bayan --version`; NativeAOT publish in CI on all three platforms.
- Golden test runner with update mode.
- Diagnostic data model and renderer (with a hand-built diagnostic in a unit test).

**Done when:** CI is green on all platforms and `bayan --version` runs from the AOT binary.

### M1 — Lexer
- UTF-8 input; NFC check on identifiers (normalize or report, BYN0005); reject bidi control characters outside string literals (BYN0001).
- All tokens in spec §2: keywords (including reserved ones, which must be rejected as identifiers), identifiers (Unicode), integer literals with `_`, hex, binary; floats with exponents; char literals (32-bit scalars, `\u{…}`); strings, raw `"""` strings, `$"…{expr}…"` interpolation (lex nested expressions), `c"…"`; comments; `///` doc comments kept as trivia attached to the next token.
- Unit suffixes are lexed as a suffix token on numeric literals (semantics come later); reject them on hex/binary literals.
- Recovery: an invalid character produces a diagnostic and lexing continues.

**Done when:** `lex/` golden cases cover every token kind and every lexer diagnostic; lexing a 1 MB file allocates only the token array.

### M2 — Parser
Grammar for the v0.1 subset:
- Module-level functions; `struct` with fields (defaults, `required`, `readonly`), properties (`=>` getters), constructors, methods, `public`/`private`.
- Statements: `let`/`var`/`const`, assignment and compound assignment, `++`/`--`, expression statements, `return`, `if`/`else`, `unless` (reject `else`, reject `unless (!x)`), **postfix modifiers** (simple statements only, spec §7.3), `while`, `for (let x in a..b)` / `for (let x in xs)`, labeled `break`/`continue`, `switch` statements (no fall-through, optional trailing `break`), `defer`, `errdefer`, blocks.
- Expressions with C# precedence plus `..`, `..=`, `^`, `+%`, `+|`, `??`, `catch`, `try`, `is` with patterns (constant, relational, `and`/`or`/`not`, `var`, `_`, property, list with slices), `switch` expressions, calls with named arguments, member access, indexing, struct literals `T { f = v }` and target-typed `{ f = v }`, collection literals `[…]`, `$"…"` interpolation, casts `(T)x`, tuples.
- `new` accepted (and noted for the formatter); `goto` produces BYN1010 with the labeled-break fix-it.
- Error recovery at statement and declaration boundaries; one missing `;` must not produce a cascade.
- `bayan parse --ast file.byn` dumps the AST (used by golden tests).

**Done when:** `parse/` cases cover every construct above, and `diag/` cases cover the common syntax errors with helpful messages.

### M3 — Semantics I: names, types, mutability
- Module = directory; `import`; name resolution across files in a module; visibility (`private` / module / `public`).
- Types: primitives (sized names canonical; C# aliases accepted), `string`, `char`, `bool`, structs, tuples, `T?`, `never`, fixed arrays `T[N]`, built-in `Span<T>` and `List<T>`.
- Untyped integer literals adopt their context type; out-of-range literals are errors.
- Implicit lossless widening only; explicit narrowing casts.
- Casing rules (spec §5.1) as **errors**.
- `let` immutability, immutable parameters, definite assignment, `required` fields, unreachable code after `never`.
- Named-argument rules: same-typed parameters and `bool` parameters must be named at call sites.
- `string` has no indexer (BYN2201 with the four fix-its from spec §3.2).

**Done when:** every rule above has passing and failing `diag/` cases.

### M4 — C backend: hello world
- Lower the typed AST to a simpler tree (`Bayan.Lowering`) and emit C from that. Keep this seam: DIR replaces it later.
- Runtime: `print`, `panic`, string literals, integer formatting for interpolation, checked arithmetic.
- `bayan build` / `bayan run` invoke `zig cc`; build modes `debug`, `safe`, `fast`.
- Interpolated strings write into a stack buffer and print (no allocation for `print($"…")`).

**Done when:** `examples/hello/main.byn` runs on all platforms; `run/` cases cover arithmetic, overflow panics in `safe`, structs, methods, control flow, postfix modifiers (with the condition evaluated first — include a case proving the statement isn't evaluated when the condition is false), labeled break, and `switch`.

### M5 — Errors
- `error enum` declarations with doc comments; `fails` clauses (explicit sets; inferred sets for private functions only); multiple sets; `Result<T, E>` as a type.
- `try`, `catch value`, `catch return …`, `catch (e) switch { … }` with exhaustiveness, `?? return / break / continue`.
- `defer` and `errdefer` with reverse-order execution; bodies may not contain `try` (BYN2601).
- `$"{e}"` prints the doc-comment summary.
- `main` that fails prints the error and exits with code 1.

**Done when:** `run/` cases cover every form, including cleanup order on success and failure, and `diag/` cases cover non-exhaustive `catch` and `try` inside `defer`.

### M6 — Memory basics
- Slicing: `xs[a..b]`, `xs[^1]`, `xs[1..^1]`, strings by byte offset (panic mid-code-point), with bounds checks.
- `stackalloc T[N]` (constant `N` only in v0.1).
- `Arena` and `context (allocator: …) { … }`; `List<T>` allocates from the context allocator; `General` allocator as the default for `main`.
- `.copy(to: alloc)`.
- **Region rules, conservative version:** borrowed values (`Span<T>`, string slices) may be locals, parameters and return values, but **not struct fields** in v0.1 (BYN3001, "storing borrows in structs arrives in a later version; copy the data instead"). Enforce spec §9.4 rules 3, 4 (conservative: all arguments) and 5 (no structural mutation while a local borrow is alive).

**Done when:** `run/` cases cover slicing and arenas; `diag/` cases cover a borrow escaping its region, mutation while borrowed, and `List` modified while a span of it is alive.

### M7 — Tests
- `test "name" { … }` blocks, in the same file or `x.test.byn`; `assert(cond)` with the expression text in the failure message; `bayan test` compiles test binaries (tests stripped from normal builds), runs each test, catches panics per test (run each test in a child process for v0.1), and reports pass/fail with timing.

**Done when:** `bayan test` runs a suite with passing, failing and panicking tests and reports each correctly.

### M8 — tally-lite (exit criterion)
A reduced version of the tally example in `examples/tally-lite`, using only v0.1 features:
- `money`: `Cents` as a plain `i64` alias (no units yet), `Money` struct, `parseAmount` with `fails MoneyError`, `formatInto` into a stack buffer.
- `ledger`: `Expense` storing participants as `List<string>` copied into the arena (no borrowed fields), `splitEvenly`, `balances` (use a `List` of `(string name, i64 cents)` pairs if `Map` isn't ready), `settle`.
- A `main` that reads a hard-coded ledger (file I/O optional) and prints transfers.
- Tests: the unit tests from the original tally example that apply.

Expected output for the sample trip:
```
dina pays alice 91.82 MYR
chen pays alice 59.12 MYR
bob pays alice 1.28 MYR
```
(Verified against a reference implementation: balances alice +15222, bob −128, chen −5912, dina −9182 cents.)

**Done when:** `bayan run` prints the output above and `bayan test examples/tally-lite` passes.

## After v0.1 (for orientation only)

v0.2: DIR + interpreter (comptime, scripts), generics, interfaces, unions, enums with data, units, `Map`, iterators, `Io.Blocking`/`Threaded`, JSON/CSV, property and fuzz tests, borrowed struct fields with full region inference.
v0.3: fibers and `Io.Evented`, networking, HTTP, TLS through a C wrapper, WASM.
Formatter, LSP, prose tool and packages land across v0.2–v0.4.

## Testing the compiler itself

- Golden tests for every phase (above).
- The lexer and parser are the compiler's untrusted-input surface: add **SharpFuzz** on `bayan parse` once M2 is stable (nightly CI).
- Run **Stryker.NET** on `Bayan.Syntax` and `Bayan.Semantics` before v0.1 to find weak tests.
- Reproducibility: CI compiles `examples/` twice and compares the emitted C byte-for-byte.

## Questions

*(Add questions here; the designer answers them in this file.)*

1. **Doc locations.** The layout above puts `SPEC.md` and `HANDOFF.md` at the repo root; the house layout allows only `README.md`, `AGENTS.md`, `CHANGELOG.md` there and requires kebab-case names. They live at `docs/bayan-spec.md` and `docs/bayan-handoff.md`. *Current choice:* keep them there; references to `SPEC.md` in this file mean `docs/bayan-spec.md`.
2. **Diagnostic labels.** The rendered example shows text after the carets (`declared with \`let\` on line 9`), which the diagnostic data model list (code, span, message, help, fix) doesn't mention. *Current choice:* an optional `Label` on the primary span.
3. **`docs/diagnostics/BYN####.md` names** are uppercase, which the repo's kebab-case hook rejects for new doc files. *Current choice:* decide when the first one is written (M1); options are an ignore pattern for that directory or lowercase `byn0001.md` with `bayan explain` matching case-insensitively.

## Progress log

*(Date — milestone — what changed — what's next.)*

- **2026-10-20 — M0 (local; CI not yet run).** Repo initialised (`main`/`dev`). `Bayan.slnx` with `src/Bayan.{Cli,Syntax,Semantics,Lowering,Backend.C}` and `tests/Bayan.Tests` (xUnit 2). `Directory.Build.props`: net10.0, warnings as errors, `IsAotCompatible` for `src/`. `bayan --version` via `CliApp`; NativeAOT publish verified on macOS arm64. `SourceText`/`TextSpan` (byte offsets, lazy line table), `Diagnostic` + `DiagnosticRenderer` reproducing the example above byte-for-byte. Golden harness `Golden.cs` (`BAYAN_UPDATE=1`, CRLF-insensitive, first-difference report). `.github/workflows/ci.yml` for linux-x64 / osx-arm64 / win-x64 (test, AOT publish, `--version`), unverified until pushed. 17 tests pass. **Next:** push to get CI green (M0's done-criterion), then M1 lexer.
