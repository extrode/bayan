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

<!-- M9–M12 come from docs/bayan-handoff-addendum-001.md; there `HANDOFF.md`/`SPEC.md` mean docs/bayan-handoff.md / docs/bayan-spec.md. -->

### M9 — `bayan new` and `bayan init`

```
bayan new <name>             # app (default)
bayan new <name> --lib       # library
bayan new <name> --script    # single-file script
bayan init [--lib|--script]  # in the current directory (refuses if package.byn exists)
  --vcs git|none             # default: git, when git is installed and not already inside a repository
  --ci github|none           # default: none
```

**Generated app layout:**

```
tally/
  package.byn                 manifest (data only)
  README.md                   name, one-line description, how to run and test
  .gitignore
  .gitattributes
  .editorconfig
  src/
    app/
      main.byn                print("Hello, world!");
  tests/
    app.test.byn              one passing test calling into the app module
  testdata/                   (empty, with .gitkeep)
  docs/                       (empty, with .gitkeep)
  out/                        not created until the first build; ignored
  dist/                       not created until `bayan dist`; ignored
  gen/                        not created until a generator runs; ignored
```

**Library layout** replaces `src/app/main.byn` with `src/<name>/<name>.byn` (one public function and a doc comment) and `tests/<name>.test.byn`. **Script layout** is a single `<name>.byn` with a shebang, `#:io threaded`, and nothing else (no manifest, no git).

**Generated `package.byn` (app):**

```csharp
Package {
    name    = "local/tally",      // `local/` until the user publishes under a verified namespace
    version = "0.1.0",
    edition = 2027,
    kind    = .app,
    capabilities = [],
    deps    = [],
    dist = Dist {
        targets = [.host],        // expands to the machine's own target; see M11
        mode    = .safe,
    },
}
```

**`.gitignore`:**
```
/out/
/dist/
/gen/
```
Lockfile policy: `bayan.lock` is committed for apps and libraries alike (reproducible builds); do not ignore it.

**`.gitattributes`:**
```
* text=auto eol=lf
*.byn linguist-language=C#
```
(The `linguist-language` line gives `.byn` files C# highlighting on GitHub until Bayan has its own grammar there.)

**`.editorconfig`:** UTF-8, LF, 4-space indentation, final newline, trim trailing whitespace.

**`--ci github`** adds `.github/workflows/bayan.yml` running `bayan build`, `bayan test`, and (once they exist) `bayan fmt --check` and `bayan fmt --check-prose`.

**Git behavior:** run `git init` only if `--vcs git` (the default), git is on `PATH`, and the target isn't already inside a work tree (`git rev-parse --is-inside-work-tree`). **Never commit, branch or configure anything.** If git is missing, print a note and continue.

**Acceptance:**
- Golden cases under `tests/cases/new/`: the full file tree plus every file's contents for app, lib and script, snapshot-tested.
- A freshly generated app passes `bayan run` and `bayan test` with no edits.
- `bayan init` in a non-empty directory adds missing files only and lists what it skipped.
- Names are validated (camelCase-or-kebab directory names allowed, package names lowercase with `-`), with teaching diagnostics.

### M10 — Build outputs: `out/` and `dist/`

Two different directories with different jobs:

| Directory | Contents | Lifetime |
|---|---|---|
| `out/` | Everything `bayan build`, `run` and `test` produce: emitted C, objects, binaries, test reports | Disposable; per worktree; ignored |
| `dist/` | Versioned release artifacts from `bayan dist` | Kept; immutable per version; ignored by git by default |

**`out/` layout:**
```
out/
  <mode>/<target>/            e.g. out/debug/linux-x64/
    c/                        emitted C (kept for inspection)
    obj/
    tally                     the binary
  test/
    results.json              machine-readable test results
```

**`dist/` layout:**
```
dist/
  0.1.0/
    linux-x64/tally
    linux-arm64/tally
    macos-arm64/tally
    windows-x64/tally.exe
    SHA256SUMS
    BUILDINFO.json
  0.2.0/
    …
  latest/                     a copy of the newest version directory
    VERSION                   contains "0.2.0"
    linux-x64/tally
    …
```

Rules:
- **Target names:** `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32`. `.host` in `package.byn` resolves to the current machine. `bayan dist --target all` builds every target `zig cc` supports from the current host.
- The version comes from `package.byn`. **A version directory is immutable:** `bayan dist` refuses to overwrite an existing `dist/<version>/` unless `--force`, and says to bump the version instead.
- `latest/` is a **copy, not a symlink** (symlinks need privileges on Windows and break in archives). It is rebuilt atomically: write to `dist/.latest.tmp/`, then rename. "Newest" means the highest semantic version present, not the most recent build.
- `SHA256SUMS` lists every artifact in the version directory.
- `BUILDINFO.json`: package name and version, compiler version, mode, targets, source commit hash and whether the tree was dirty (if in a git repository), and a hash of the inputs. This is the seed of the isnād provenance chain.
- `bayan dist` refuses to run from a **dirty git tree** unless `--allow-dirty` (recorded in `BUILDINFO.json`).
- Libraries: `dist/<version>/<target>/` contains the static and shared libraries plus the generated C header, once FFI export exists; until then, `bayan dist` on a library reports that library distribution arrives later.
- Scripts have no `dist`.
- `bayan clean` removes `out/`; `--dist` also removes `dist/`; `--cache` clears the global cache (M11). Nothing is removed without one of these commands.

**Acceptance:** golden tests for both layouts (tree listings, `VERSION`, `SHA256SUMS` format, `BUILDINFO.json` schema); immutability and `latest` selection tested with multiple versions; cross-compiling at least `linux-x64`, `macos-arm64` and `windows-x64` from one host in CI.

### M11 — Global cache and worktrees

- **Location:** `$BAYAN_CACHE` if set; otherwise `$XDG_CACHE_HOME/bayan` or `~/.cache/bayan` (Linux), `~/Library/Caches/bayan` (macOS), `%LOCALAPPDATA%\bayan\cache` (Windows).
- **Content-addressed:** keys are hashes of (compiler version, mode, target, flags, input content). In v0.1 the unit is the emitted C file and object per module; with DIR it becomes per function.
- **Safe under concurrency:** several worktrees and several agents may build at once. Write to a temporary file and rename atomically; never lock the whole cache; a corrupted entry is detected by its hash and rebuilt.
- **No absolute paths in any output.** `#line` directives and debug info use paths relative to the package root, so cache entries are valid in every worktree and every checkout location.
- `out/` is always local to the worktree; only the cache is shared.
- **Script caching:** `bayan run script.byn` caches the compiled binary under the cache key (source, directives, compiler version); a cache hit starts the binary directly.
- `bayan cache stats` and `bayan clean --cache`.

**Acceptance:** two worktrees of the same repository building the same commit — the second build compiles nothing (asserted from cache statistics); parallel builds of the same package in two directories never corrupt the cache (stress test); moving a project directory does not invalidate its cache hits.

### M12 — CLI completeness and machine-readable output

Things the original handoff implied but didn't list:

- **`--diagnostics json`** on `build`, `run`, `test`: one JSON object per diagnostic (code, severity, file, span, message, help, fixes). Coding agents and editors consume this instead of parsing text.
- **`bayan test --json`** writes results to stdout in the same format as `out/test/results.json`.
- **Exit codes:** `0` success · `1` compile errors · `2` usage errors · `3` test failures · `4` toolchain problems (missing `zig`, unwritable cache). Document them in `bayan --help`.
- **`bayan run`** exits with the CLI code (`1`) when compilation fails; once the program starts, its exit code passes through unchanged (a failing `main` exits `1`, or its `[ExitCode]`). So `bayan run` cannot tell a build failure from a program exiting `1`: anything that needs the distinction runs `bayan build` first or reads `--diagnostics json`. (Same as `cargo run` / `go run`.) CLI tests cover a build failure, a program exiting `0`, and one exiting with a custom code.
- **Color:** on for terminals; off when piped, when `NO_COLOR` is set, or with `--color never`.
- **`bayan doctor`:** reports compiler version, `zig` path and version, git availability, cache location and writability, and the host target. Every failure has a fix suggestion.
- **`bayan test --changed`** (simple version): uses `git diff --name-only` against `HEAD` (or `--base <ref>`) and runs tests in modules whose files changed. Read-only git use only.
- **`bayan --version`** prints the compiler version and the commit it was built from.

**Acceptance:** golden tests for JSON diagnostics and test output; exit codes asserted in `run/`-style CLI tests; `bayan doctor` tested with a missing `zig` (via `BAYAN_CC` pointing nowhere).

**Order of work (addendum 001):** Q34 `ReadOnlySpan<T>` and float formatting first; then M12's JSON diagnostics and exit codes; then M9 → M10 → M11; then the rest of M12.

## After v0.1 (for orientation only)

v0.2: DIR + interpreter (comptime, scripts), generics, interfaces, unions, enums with data, units, `Map`, iterators, `Io.Blocking`/`Threaded`, JSON/CSV, property and fuzz tests, borrowed struct fields with full region inference.
v0.3: fibers and `Io.Evented`, networking, HTTP, TLS through a C wrapper, WASM.
Formatter, LSP, prose tool and packages land across v0.2–v0.4.

## Self-hosting readiness

`docs/bayan-self-hosting.md` lists what a compiler needs from its own language. Spec 001 (`docs/specs/001-self-hosted-lexer/`) covers phases 1 and 2; milestones M13–M23 continue the numbering above. Correction to that doc's table: file I/O and program arguments were **not** in v0.1; M13 adds them.

| Needed for | Feature | Status |
|---|---|---|
| Reading source files | File I/O, UTF-8 strings, slicing | strings and slicing v0.1; file I/O and args M13 |
| Error reporting | `fails`, error unions, `defer` | v0.1; `[ExitCode]` M13 |
| Fast allocation of syntax trees | Arenas, `context` allocator | v0.1; growable strings M14 |
| Tokens and diagnostics | Structs, fixed arrays, `List<T>` | v0.1; enums M16–M17, static `const` arrays M16 |
| Syntax-tree nodes | Unions + exhaustive `switch` | M18; recursion through `Owned<T>` M21 |
| Symbol tables | `Map` / `HashMap`, `Symbol` interning | `Map` M20; `HashMap` and interning next spec |
| Reusable passes | Generics, interfaces, iterators | generics M19; interfaces and iterators next spec |
| Nodes referring to source text | Borrowed struct fields (full regions) | byte offsets (v0.1); full regions later spec |
| Running `zig cc` | Spawning processes | M15 (`Io.run`, `Io.listDir`) |
| Tests | `test` blocks, `property`, `fuzz` | `test` v0.1; `property`/`fuzz` next spec |

Phase 1 tool: `tools/lex-diff/` (M15). Phase 2: `selfhost/lexer/` checked against the C# lexer by the test suite (M22–M23).

## Testing the compiler itself

- Golden tests for every phase (above).
- The lexer and parser are the compiler's untrusted-input surface: add **SharpFuzz** on `bayan parse` once M2 is stable (nightly CI).
- Run **Stryker.NET** on `Bayan.Syntax` and `Bayan.Semantics` before v0.1 to find weak tests.
- Reproducibility: CI compiles `examples/` twice and compares the emitted C byte-for-byte.

## Questions

*(Add questions here; the designer answers them in this file.)*

1. **Doc locations.** The layout above puts `SPEC.md` and `HANDOFF.md` at the repo root; the house layout allows only `README.md`, `AGENTS.md`, `CHANGELOG.md` there and requires kebab-case names. They live at `docs/bayan-spec.md` and `docs/bayan-handoff.md`. *Current choice:* keep them there; references to `SPEC.md` in this file mean `docs/bayan-spec.md`.
2. **Diagnostic labels.** The rendered example shows text after the carets (`declared with \`let\` on line 9`), which the diagnostic data model list (code, span, message, help, fix) doesn't mention. *Current choice:* an optional `Label` on the primary span.
3. **`docs/diagnostics/BYN####.md` names** are uppercase. *Resolved (M1):* the kebab-case hook accepts all-uppercase names, so files are `BYN0001.md`. `bayan explain` matches codes case-insensitively and reads the pages embedded in the executable (works offline, no repo needed).
4. **Reserved words.** §2.4 lists keywords but not which are reserved. *Current choice:* every keyword is reserved (BYN0012), including future ones (`actor`, `effect`, `macro`, `select`, `become`). Loosening later is non-breaking.
5. **Interpolation nesting.** No limit in the spec. *Current choice:* 16 nested `$"{…}"` levels, reported once per file (BYN0016).
6. **Globalization.** `InvariantGlobalization=true` makes `string.IsNormalized` always return true, which would disable the NFC check (BYN0005). *Current choice:* ICU stays enabled; the AOT binary depends on system ICU.
7. **Range precedence.** The spec says "C# precedence plus `..`"; in C# `..` binds tighter than `*`, so `0..n-1` would parse as `(0..n)-1`. *Current choice:* `..`/`..=` sit just above relational and below shift/additive, so `0..n-1` is `0..(n-1)`.
8. **C#-style locals.** `i64 x = 1;` is accepted as a declaration. *Current choice:* it means `var` (mutable), matching C#. `let`/`const` stay the only immutable forms.
9. **Top-level statements.** *Current choice:* parsed as `GlobalStatement` declarations; M3/M4 treat them as the body of an implicit `main` (one file per program may contain them).
10. **Struct literal disambiguation.** `Name { … }` after an expression start is ambiguous with `if (x) Name { … }`-style blocks. *Current choice:* a struct literal needs an uppercase-initial type name followed by `{ }` or `{ ident =`; `when` stays contextual.
11. **Stacked case labels.** `case a: case b:` is reported (BYN1014) and merged into `case a or b:` so checking continues.
12. **Integer literal types.** *Current choice:* an untyped integer literal adopts the expected type; with none it is `i32` if it fits, else `i64`, else `u64` (C#). A char literal is `u8` where `u8` is expected. `.length` and indices are `i64`.
13. **Overloading and shadowing.** *Current choice:* neither; a local may not reuse a visible name (BYN2007). `let` needs an initializer; `T x;` is a `var` checked by definite assignment.
14. **Imports.** *Current choice:* `import a.b;` resolves to `<package root>/a/b/`; the package root is the entry file's directory. Diagnostics name imported files by package-relative path. Files with syntax errors stop compilation before binding (no cascades).
15. **Mutating methods.** The spec has no `mutating` keyword. *Current choice:* a method that writes `this` (directly or through another mutating method) is inferred mutating, to a fixed point; calling one on a `let` value or a temporary is BYN2104/BYN2106, and a getter may not mutate (BYN2107).
16. **Interpolation outside output.** v0.1 has no allocating string formatting. *Current choice:* `$"…"` is allowed only as an argument to `print`, `panic` and `StrBuf.append` (BYN2224).
17. **Error values.** *Current choice:* error variants are `u32` codes, globally numbered. Inferred `fails` (bare `fails`) is computed to a fixed point and allowed only on non-`public` functions (BYN2607). `main` and tests may let any error escape.
18. **Must-name rule scope.** *Current choice:* applies to user functions only (builtins like `print` are exempt); when a function has several parameters of one type, every one of them must be named, including the first.
19. **Strings in struct fields.** M6 forbids borrowed fields, but tally-lite's `Expense` needs a payer name. *Current choice:* `string` fields are allowed in v0.1 (the value must be a literal or an arena copy; not yet checked); `Span<T>` fields are BYN3001.
20. **Runtime file names.** The handoff says `bayan_rt.{h,c}`; the kebab-case hook forbids underscores. *Current choice:* `src/runtime/bayan-rt.{h,c}`, embedded in the backend and written next to `main.c` at build time.
21. **`StrBuf`.** *Current choice:* a `byn_writer` over caller memory in strict mode: an append that doesn't fit panics (`StrBuf overflow`); `.str` borrows the bytes.
22. **Default struct formatting.** *Current choice:* a struct with `formatInto(ref StrBuf)` formats through it; otherwise `Name { a: 1, b: 2 }`. Tuples print `(a, b)`, `null` for an empty optional, an error prints its doc-comment summary (else its name).
23. **Float formatting.** *Current choice:* the shortest round-trip digits, laid out like C# `double.ToString()`: fixed notation for decimal exponents in [-4, 15), else `1E+15` / `1E-05`; `-0` prints `-0`. *Answered (addendum 001):* shortest round-trip digits; fixed notation for `1e-5 ≤ |x| < 1e15`, exponential outside it, spelled lowercase without `+` or leading zeros (`1e15`, `1.5e-7`, `-2.25e20`); `nan`, `inf`, `-inf`; `-0`; integral values print without a decimal point. `{x:F2}` stays deferred.
24. **`(T)-x`.** `(i64)-3.99` parses as `(i64) - 3.99` (C# has the same rule for non-keyword types). *Current choice:* unchanged; write `(i64)(-3.99)`. Logged in `work/todo.md`.
25. **Integer limits.** The spec has no `i64.max`/`i64.min`. *Current choice:* none in v0.1; write the literal.
26. **Panic locations.** *Current choice:* `panic: <msg>\n  at <package-relative path>:<line>` on stderr, then `abort()` (exit 134; Windows 3 is mapped to 134 by the tests).
27. **Test entry.** *Current choice:* the program built with tests accepts `--test N` to run test N only (exit 0 pass, 1 escaped error, 134 panic, 2 bad index); `bayan test` (M7) runs each in its own process.
28. **`Result<T, E>` surface.** §3.8 says only "a plain struct". *Current choice:* `E` must be one error enum; `T` and `E` values convert implicitly (so `return .missing;` and `return 1;` both work), `.isOk`, `.value` (panics if failed), `.err` (panics if ok; `error` is a keyword so `.error` doesn't parse). It formats as the value or the error summary and compares by tag + value. No `try`/`catch` on a `Result` value yet.
29. **Region analysis precision.** *Current choice:* flow-insensitive per function. A local borrows from everything ever stored in it; a borrow stays alive until the holder's last textual use, extended to the end of any loop that uses it but doesn't declare it. Regions are blocks; by-value parameters count as function-local (returning a span of one is BYN3003). An element read (`xs[i]`) copies, so it doesn't hold a loan on `xs`. Lists created inside `context (allocator: a)` (directly, or returned by a call made there) borrow `a`.
30. **Structural modification.** *Current choice:* `List.add/clear/removeLast`, `StrBuf.clear`, `Arena.reset`, and passing a borrow-carrying local by `ref`. Element writes (`xs[0] = 9`) and `StrBuf.append` are not structural. BYN3005 fires only when the same local is passed by `ref` and as (or inside a borrow of) another argument; reading `x.length` in the same call is allowed.
31. **Default allocator.** *Current choice:* `List` outside any `context` uses the C heap (`General`) and is never freed in v0.1; no leak detection yet (spec §9.2's leak-checking `General` for tests is deferred).
32. **`bayan test` output.** The spec names the command but not its report. *Current choice:* `running N tests`, then one line per test (`ok` / `FAIL` = an error escaped / `PANIC` = panic or failed `assert` / `TIME` = over 60 s) with its wall time; a failing test's stdout and stderr are shown indented below it; a summary line; exit 1 if any test didn't pass. Tests run sequentially, one child process each. Tests from every module in the package are included. *Exit code (addendum 001):* test failures exit `3`.
33. **Arena parameters and rule 4.** §9.4 rule 1 makes function locals a region, but an `Arena` passed by value is a handle to the caller's arena, and our BYN3003 help says to pass the allocator in. *Current choice:* an `Arena` parameter's region is the caller's, so `xs.copy(to: arena)` may be returned. Rule 4 (a call result borrows from every argument) stays unnarrowed because the `borrows a` clause isn't implemented yet: passing a `Span` array literal into a function whose result is stored past the literal's block is reported, even when the callee never keeps it. tally-lite passes `List<string>` instead.
34. **Read-only spans.** The spec says strings are immutable and always valid UTF-8 but gives `s.bytes` no type. *Current choice:* `string.bytes` (and slices of it) is a `read-only Span<u8>`: its elements cannot be assigned, a writable `Span<T>` converts to it implicitly, and it does not convert to `Span<T>`, so it cannot be passed to a `Span<u8>` parameter (copy with `.copy(to:)` first). There is no syntax to name the type yet; a later `ReadOnlySpan<T>` or read-only-by-default parameter rule (§ parameters) would relax this. *Answered (addendum 001):* add `ReadOnlySpan<T>` with C# semantics: `Span<T>` → `ReadOnlySpan<T>` implicit; `List<T>` → `ReadOnlySpan<T>` and `string` → `ReadOnlySpan<u8>` implicit as arguments; `string.bytes : ReadOnlySpan<u8>`; read-only stdlib functions take `ReadOnlySpan<T>`; lint BYN2310 suggests it for a `Span<T>` parameter never written to. Span parameters stay mutable (zero-allocation C# must compile unchanged).

35. **Exit codes outside the table.** *Current choice:* a missing input path and an unknown `explain` code are usage errors (2); a C compiler that rejects the generated C is a toolchain problem (4), like a missing compiler or an unwritable cache. Toolchain messages stay plain `bayan: …` text on stderr even with `--diagnostics json` (they have no `BYN` code), so a consumer reads lines starting with `{` as diagnostics. A failing `main` exits with the variant's `[ExitCode(n)]`, else 1 (M13, Question 40).

36. **Project layout details (M9).** The addendum gives the generated files but not how commands find code in them. *Current choice:* a directory with `package.byn` is a project; imports resolve under `src/`; the entry module is `src/app/` (app) or `src/<camelCase name>/` (library, so `import myLib;` works for `my-lib`); test builds add every `tests/*.test.byn` to the entry module, so tests call the code directly; files are named relative to the project root in diagnostics. No path means `.` when it has a `package.byn`. `bayan build`/`run` on a library exit 2 (nothing to build until FFI export). A file starting with `#!` compiles alone, and `bayan <file>.byn` means `bayan run <file>.byn` so the shebang works. Target enum names in `package.byn` are identifiers (`.linuxX64`, `.macosArm64`) mapped to the kebab-case target names; manifest field names may be keywords (`test = Tests { … }`). The generated app prints via a `greeting()` function so its test has something to call; the library exports `twice(i64)`. A library named after a keyword (`test`) is rejected. The CI workflow runs `bayan check` instead of `build` for a library and leaves installing zig and bayan to the user (no official setup actions exist). `package.byn` diagnostics are BYN3100–3103.

37. **Outputs and `dist` details (M10).** The addendum leaves several choices open. *Current choices:* `out/` holds no `obj/` yet (the C compiler builds in the cache, and objects arrive with incremental builds in M11). `bayan build -o` still writes where it is told, and outside a project (a script, or a loose file or directory) builds go to the current directory as before. `out/test/results.json` and `bayan test --json` share one format: `package`, `mode`, `target`, `tests` (`name`, `file`, `line`, `status` pass/fail/panic/timeout, `durationMs`, `output` of a test that didn't pass) and `summary`. An `assert` failure is a panic, as in M7. `bayan dist` builds `--target` (a name, `host` or `all`), else `dist.targets`, else the host; `--mode`, else `dist.mode`. Builds are staged in `dist/.<version>.tmp/` and renamed into place, so a failed target leaves no partial version. `BUILDINFO.json` holds `package`, `version`, `compiler`, `mode`, `targets`, `source` (`commit`, `dirty`; null outside git or when the enclosing repository ignores the project) and `inputs` (SHA-256 over `package.byn` and `src/**/*.byn`, by relative path), with no timestamps. The dirty check covers the project directory only. Refusals (an existing version without `--force`, a dirty tree without `--allow-dirty`, a library, a non-project) exit 2. `latest/` is replaced by delete-then-rename, so it is briefly absent; Windows can't rename over a directory. Cross-compiling needs zig, and all seven targets build from macOS arm64; `dist/` binaries are not stripped.

38. **Cache details (M11).** *Current choices:* the cache unit is the whole program, not a module: v0.1 emits one C translation unit per program, so per-module objects wait for separate compilation. The key also covers the C compiler command, since a different `zig` or `cc` builds a different binary. Entries are `v1/programs/<key>/` (executable plus `program.sha256`); every hit re-hashes the executable (about a millisecond for current programs) and a mismatch discards and rebuilds the entry. Scripts are remembered under `v1/scripts/<key>`, where the key covers compiler version, mode, host and the script's bytes but not the C compiler, so a remembered script starts with no C compiler installed. Nothing evicts entries yet; `bayan clean --cache` empties the cache. Writes into `out/` go through a temporary file and a rename, because two builds of one worktree can run at once. Diagnostics for a loose file still print the path as given, which may be absolute; only the emitted C and the binaries must avoid absolute paths.
39. **CLI completeness details (M12).** *Current choices:* `--color` is a global option, stripped from the arguments before the command runs, except after `--` or when the first argument is a script, whose own arguments it would steal. Only diagnostics and `bayan test` labels are colored. `bayan doctor` exits 4 when a check fails. A missing `zig` with a working `cc`, or a missing git, is a note and doesn't fail. Checking the cache creates its directory. `bayan test --changed` maps a changed file to its module directory (`tests/x.test.byn` to the entry module, `src/<entry>`) and runs only those modules' tests. Dependents of a changed module are not run, since v0.1 has no module dependency graph in the CLI. Untracked files count as changed, and a changed `package.byn` runs every test. `--base` implies `--changed`. Outside a git repository, or with a bad base, it exits 2. The JSON report gains `summary.skipped`. `--version` prints the commit recorded at build time (`git rev-parse --short=12 HEAD`), or `unknown` when the compiler was built outside git. A test is stopped after 60 s (`BAYAN_TEST_TIMEOUT_MS` overrides it). Remembered scripts are also keyed by the compiler binary's size and modification time, so a rebuilt dev compiler doesn't reuse stale programs. `bayan clean --cache` removes only `<cache>/v1`.
40. `main` may return `i32` or take `ReadOnlySpan<string> args`; a failing `main` exits the variant's `[ExitCode(n)]`, else 1. Unknown attributes are errors.
41. Non-UTF-8 argv: the runtime reports `error: argument N is not valid UTF-8` and exits 1 before `main` runs.
42. `Io` is the blocking implementation and the only one; `context.io` indirection deferred. `IoError` is a builtin error enum with four variants; errno values outside the mapping are `other`.

## Progress log

*(Date — milestone — what changed — what's next.)*

- **2026-10-20 — M0 (local; CI not yet run).** Repo initialised (`main`/`dev`). `Bayan.slnx` with `src/Bayan.{Cli,Syntax,Semantics,Lowering,Backend.C}` and `tests/Bayan.Tests` (xUnit 2). `Directory.Build.props`: net10.0, warnings as errors, `IsAotCompatible` for `src/`. `bayan --version` via `CliApp`; NativeAOT publish verified on macOS arm64. `SourceText`/`TextSpan` (byte offsets, lazy line table), `Diagnostic` + `DiagnosticRenderer` reproducing the example above byte-for-byte. Golden harness `Golden.cs` (`BAYAN_UPDATE=1`, CRLF-insensitive, first-difference report). `.github/workflows/ci.yml` for linux-x64 / osx-arm64 / win-x64 (test, AOT publish, `--version`), unverified until pushed. 17 tests pass. **Next:** push to get CI green (M0's done-criterion), then M1 lexer.
- **2026-10-20 — M1.** Hand-written lexer (`Bayan.Syntax/Lexing`) over UTF-8 bytes: struct tokens with byte spans and trivia flags, doc comments attached to the next token, interpolation mode stack (stackalloc), `>>` left to the parser. Diagnostics BYN0001–0016 in `DiagnosticCodes` with `docs/diagnostics/BYN####.md`; tests enforce docs + a golden case per code. `bayan lex <file>`, `bayan explain <code>`. 21 `tests/cases/lex` goldens; 1 MB input allocates only the token array. 55 tests pass. **Next:** M2 parser.
- **2026-10-20 — M2.** Recursive-descent parser with precedence climbing (`Bayan.Syntax/Parsing`), immutable AST records (`Ast/Nodes.cs`), S-expression `AstDump` behind `bayan parse --ast`. Recovery at statement/declaration boundaries (newline-started keywords, modifiers, function headers close unclosed blocks); one missing `;` reports once with an insert fix. BYN1001–1016 with docs and fix-its (`unless`→`if`, `goto`→`break`, parenthesize ternary). 8 `parse/` and 21 `diag/` goldens, 35 precedence cases, random-fragment termination test. 123 tests pass. Questions 7–11 added. **Next:** M3 semantics.
- **2026-10-20 — M3.** `Bayan.Semantics`: `Compilation` loads the package (imports by directory, cycles BYN2010), declares symbols, resolves types, binds bodies (`Binder*`), then infers error sets and mutating methods and runs `FlowAnalysis` (definite assignment, `out` parameters, missing returns, unreachable code, `try` propagation). Constant folding with overflow/division errors. 95 semantic codes BYN2001–3001 with docs and one `diag/` case each, plus four `valid-*` cases that must stay clean. Questions 12–19 added. 222 tests pass. **Next:** M4 lowering + C backend.
- **2026-10-20 — M4.** `Bayan.Lowering` rewrites bound bodies into goto-and-temp form (hoisted locals, left-to-right spills, cleanups inlined at every exit, failing calls as `error = f(…, &out)`, patterns as boolean expressions). `Bayan.Backend.C`: `CEmitter` (topologically ordered types, equality/format helpers, `main` with `--test N`), runtime `bayan-rt.{h,c}` (checked arithmetic, panics with locations, writers, float/int formatting). `Toolchain` finds `zig cc` / `$BAYAN_CC` / `cc`, builds `debug`/`safe`/`fast` into a content-hashed cache (`$BAYAN_CACHE`). `bayan build [-o] [--mode] [--emit-c]`, `bayan run [--mode] [-- args]`. `examples/hello` runs; 10 `run/` goldens (arithmetic, overflow add/cast, fast-mode wrap, division by zero, structs + methods, control flow + labeled break, postfix modifiers proving the statement is skipped, switch). Questions 20–27. 239 tests pass (macOS arm64; Linux/Windows unverified until CI). **Next:** M5 errors.
- **2026-10-20 — M5.** Error handling already lowered in M4; this milestone adds `run/` goldens for every form (`try`, `catch value`, `catch return`, `catch (e) switch`, partial `catch (Set.variant)`, `?? return/break/continue`, multiple sets, inferred `fails`), cleanup order on success, failure and propagation (`defer`/`errdefer`/`errdefer (e)`, loop-scoped defers with `break`/`continue`), and a failing `main` (`error: <summary>`, exit 1, defers run). New `Result<T, E>` type (Question 28). `diag/non-exhaustive-catch`. 244 tests pass. **Next:** M6 memory.
- **2026-10-20 — M6.** Slicing, `^` indices, string byte slicing (panics mid-code-point), `stackalloc`, `Arena`/`context`/`using`, `List`, `.copy(to:)` already lowered in M4; this milestone adds `run/` goldens for them (including out-of-range and mid-code-point panics) and `RegionAnalysis` (spec §9.4 rules 3–6): BYN3002 borrow outlives its source, BYN3003 returned borrow of local storage, BYN3004 modified while borrowed (span alive, `for` over the list, `arena.reset()` with live copies), BYN3005 `ref` plus another use in one call; `diag/valid-borrows` guards against false positives. Questions 29–31. 254 tests pass. **Next:** M7 `bayan test`.
- **2026-10-20 — M7.** `bayan test <path> [--mode]`: compiles with `.test.byn` files and `test` blocks (stripped from normal builds), builds once, runs `program --test N` per test in a child process, reports pass / fail / panic with timings (Question 32). `assert` failures name the expression text. CLI tests cover a mixed suite and an all-pass suite. 256 tests pass. **Next:** M8 tally-lite.
- **2026-10-20 — M8 (v0.1 exit criterion met).** `examples/tally-lite`: `money` (`Cents`, `Money.formatInto`, `parseAmount fails MoneyError`), `ledger` (`Expense`, `splitEvenly` with leftover cents to the first participants, `balances`, greedy `settle`), `main` with an eight-expense trip; 8 tests across `money.test.byn`, `ledger.test.byn`, `main.test.byn`. `bayan run examples/tally-lite/main.byn` prints the three transfers; balances alice +15222, bob −128, chen −5912, dina −9182. Compiler fix found on the way: an `Arena` parameter's allocations belong to the caller (Question 33). CLI tests build and run `hello` and `tally-lite` and run `bayan test examples/tally-lite`. 259 tests pass (macOS arm64). **Next:** push to verify CI on Linux/Windows (needs zig on runners, `work/todo.md`), then the post-v0.1 list above.
- **2026-10-20 — Addendum 001, part 1.** Q34 `ReadOnlySpan<T>` (string → `ReadOnlySpan<u8>` arguments, `string.bytes` read-only, lint BYN2310) and float formatting (fixed notation for exponents in [-5, 15), else lowercase `e`). M12 first slice: `--diagnostics json` on `check`/`build`/`run`/`test` (`DiagnosticJson`, NDJSON on stderr, `tests/cases/diag-json/`), exit codes 0/1/2/3/4 in `--help` and §21.2, `bayan test` exits 3, `bayan run` passes the program's code through once it starts (Question 35). Toolchain failures (missing compiler, unwritable cache) return 4 instead of throwing. 283 tests pass. **Next:** M9 `bayan new`/`init`.
- **2026-10-20 — M9.** `bayan new <name> [--lib|--script] [--vcs git|none] [--ci github|none]` and `bayan init [<dir>]` (`Bayan.Cli/Scaffold.cs`); `package.byn` reader (`Bayan.Syntax/Packages/Manifest.cs`, data-only grammar, BYN3100–3103, `tests/cases/manifest/`); project-mode compilation (`Compilation.FromProject`), scripts compile alone, `bayan <file>.byn` (Question 36). Goldens `tests/cases/new/{app,lib,script}.tree`; fresh app, library and script build and pass their tests; `git init` only outside a work tree, never a commit. Spec §21.2 row and new §21.4. 322 tests pass. **Next:** M10 `out/` and `dist/`.
- **2026-10-20 — M10.** Project `build`/`run`/`test` write to `out/<mode>/<target>/` (binary, `<name>-test`, emitted C in `c/`); `bayan build --target <t>` cross-compiles through `zig cc -target` (`Bayan.Backend.C/Targets.cs`: seven targets, musl for Linux); `out/test/results.json` and `bayan test --json` (`TestReport`). `bayan dist [--target t|all] [--mode] [--force] [--allow-dirty]` and `bayan clean [--dist] [--cache]` (`Bayan.Cli/Dist.cs`). Goldens `tests/cases/out/{out.tree,dist.tree,results-fail.json}`; tests cover immutability, `latest/` by semantic version (0.0.9 after 0.2.0-rc.1 keeps the rc; 0.10.0 beats 0.2.0), dirty-tree refusal and checksums. Manually: all seven targets build, `file` confirms each format (Question 37). Spec §21.2 rows, §21.4 *Outputs*. 335 tests pass. **Next:** M11 global cache.
- **2026-10-20 — M11.** Shared build cache (`Toolchain`): platform location, content-addressed `v1/programs/<key>/` entries built in a private directory and renamed into place, SHA-256 check on every hit with rebuild on mismatch, compiler version in the key. Fixed emitted C naming project files relative to the working directory (`src/app/main.byn` from the root but `app/main.byn` from `src/`), which split cache entries; debug info maps the build directory to `.`. Script reuse (`bayan script.byn` starts the remembered binary without compiling), `bayan cache stats`, `bayan clean --cache` outside a project. `CacheTests`: a second git worktree adds no entries, a moved project adds none, 24 parallel builds across two directories leave 2 intact entries, a damaged entry is rebuilt, a remembered script runs with `BAYAN_CC` pointing nowhere. Manually: 12 concurrent `bayan build` processes all exit 0. Question 38. 343 tests pass. **Next:** rest of M12.
- **2026-10-20 — M12 (rest).** `--color auto|always|never` and `NO_COLOR` (`DiagnosticRenderer` color, colored test labels); `bayan doctor` (`Bayan.Cli/Doctor.cs`; `BAYAN_CC` pointing nowhere fails with a fix and exit 4); `bayan test --changed [--base <ref>]` via read-only `git diff --name-only` plus untracked files; `bayan --version` with the build commit (`BuildInfo.g.cs` generated by an MSBuild target). `CliEnvironmentTests` covers color, doctor (healthy, missing compiler, unwritable cache) and `--changed` over a real repository. Question 39. 353 tests pass. v0.1 milestones M0–M12 are done.
- **2026-10-20 — M22, T18.** `tools/Bayan.UnicodeGen` (console, outside `Bayan.slnx`, not AOT) downloads the UCD into `tmp/ucd/<version>/`, asserts that identifier classes from `UnicodeData.txt` equal `Rune.GetUnicodeCategory` over every scalar and that the table-driven reference NFC (`Nfc.cs`) agrees with `string.Normalize`/`IsNormalized(FormC)` on every identifier code point, every composition pair and 5000 random identifier strings, then emits `selfhost/lexer/unicode-tables.byn` (module-level `const u32[N]` arrays). Pinned Unicode 16.0.0 on .NET 10.0.12 (15.1.0 and 17.0.0 fail the category assertion), Q6/Q49. **Next:** T19 lexer port.
- **2026-10-20 — M13.** Attributes `[Name(args)]` on declarations and error variants (`AttributeSyntax`, AstDump, BYN2770); `main` returning `i32` and/or taking `ReadOnlySpan<string> args` (BYN2771), argv validated as UTF-8 before `main`; `[ExitCode(n)]` on variants, 1..255 constants (BYN2772), emitted as a `byn_exit_codes` table. Builtin `Io` (`readFile`, `readText`, `writeFile`, `stdout`/`stderr.write`, `monotonicNanos`) and `error enum IoError` (codes 1–4, so user error codes start at 5); results allocate from `context.allocator` and `readText` borrows the context region like `List` (Q29). Runtime in C11, POSIX and Windows (`_wfopen`, `QueryPerformanceCounter`). Run harness: `.args` files and a per-case scratch directory under `tmp/run/`. Questions 35, 40–42. 385 tests pass. **Next:** M14.
