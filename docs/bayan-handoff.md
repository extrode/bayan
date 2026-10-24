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
- **Color:** on for terminals; off when piped, when `NO_COLOR` is set to a non-empty value, or with `--color never`; `--color` overrides the environment.
- **`bayan doctor`:** reports compiler version, `zig` path and version, git availability, cache location and writability, and the host target. Every failure has a fix suggestion.
- **`bayan test --changed`** (simple version): uses `git diff --name-only` against `HEAD` (or `--base <ref>`) and runs tests in modules whose files changed. Read-only git use only.
- **`bayan --version`** prints the compiler version and the commit it was built from.

**Acceptance:** golden tests for JSON diagnostics and test output; exit codes asserted in `run/`-style CLI tests; `bayan doctor` tested with a missing `zig` (via `BAYAN_CC` pointing nowhere).

**Order of work (addendum 001):** Q34 `ReadOnlySpan<T>` and float formatting first; then M12's JSON diagnostics and exit codes; then M9 → M10 → M11; then the rest of M12.

**Output style (addendum 002 §6), open:** `docs/bayan-terminal-style.md` is normative for this milestone's output and for the future `bayan laws` report. The output shipped so far predates it; implementing the guide in full is remaining work of M12 — CLI completeness and machine-readable output, with golden tests in colour, with `NO_COLOR` and with `--ascii` (Q57).

## After v0.1 (for orientation only)

v0.2: DIR + interpreter (comptime, scripts), generics, interfaces, unions, enums with data, units, `Map`, iterators, `Io.Blocking`/`Threaded`, JSON/CSV, property and fuzz tests, borrowed struct fields with full region inference.
v0.3: fibers and `Io.Evented`, networking, HTTP, TLS through a C wrapper, WASM.
Formatter, LSP, prose tool and packages land across v0.2–v0.4.
Addendum 002 (`docs/bayan-handoff-addendum-002.md`): opt-in `law` declarations and `bayan laws` (v0.2), `requires`/`ensures` contracts (runtime v0.2+, `bayan verify` v1.x), `.par()` parallel iterators (v0.3). Design only until the v0.1 exit criterion and addendum 001 are done; its §1 (reserve `law requires ensures`) is done.

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

**Friction found porting the lexer (T19).** The port needed no compiler changes, but these cost the most and are candidates for the next spec:
- Must-name rule (BYN2305): ~100 errors; it fires even when argument names equal the parameter names (`fromBounds(start, end)`).
- Returning borrows (BYN3003): a method can't return a span borrowed from its own `List` field; `return sb.asString()` from a local growable `StrBuf` is rejected (workaround `$"{sb.asString()}"`); `string f(span) => utf8(span)` is treated as borrowing the argument.
- No span fields (BYN3001), so source bytes live in a `List<u8>`. Ref aliasing (BYN3005) rejected `renderAll(ref sb, ref result.source, result.diagnostics.asSpan())`.
- `var x = 0` is `i32` while `.length` is `i64`: every position needs an explicit `i64`.
- `error` and `flags` are keywords, so they can't be names.
- No `catch { block }`; no bytes→string conversion (decode appends `$"{(char)cp}"` per scalar); no hex format specifier; no `const string[]`; no delegates; no `List.removeRange`.

### Road to self-hosting (decided 2026-10-21, revised the same day)

Run unattended, one spec at a time, each merged into `dev` (unpushed) with its worker spend in the progress log. Decisions:

- **Interfaces (§3.15):** static dispatch on constraints (monomorphized), `dyn ref I` fat pointers, default methods, and associated types (`IEnumerator` with `Item`, so `for … in` works on user types). `static abstract` only if the port needs it; retroactive conformance waits.
- **Delegates (§3.16):** `delegate` types, named functions as values, non-capturing lambdas, and lambdas that capture `let` values and parameters **by copy** (a struct with fields). `ref` captures of `var` and escaping closures wait.
- **Regions (§9.4):** deferred past bootstrap; syntax nodes and tokens keep byte offsets into a source buffer, as `selfhost/lexer/` does.
- **Finish line:** the Bayan compiler does `lex`, `parse`, `check`, `build` (incl. `--emit-c`) and `run`; stage 2's generated C is byte-identical to stage 3's; the full golden suite passes against the Bayan-built compiler; `bootstrap/bayan.c` is committed. `new`, `init`, `test`, the cache and `--json` stay in the C# CLI for now.
- **Argument names (BYN2305):** an argument that is an identifier equal to its parameter's name, or a member access whose last segment is (`tok.start` for `start`), counts as named. Arguments whose names match the parameters in a swapped order are an error.
- **Contextual keywords:** every declaration-only keyword is contextual: `error`, `flags`, `unit`, `distinct`, `open`, `extensible`, `set`, `type`, `law`, `derivation`, `attribute`, `embed`. Control-flow and expression keywords stay reserved. Both lexers change together (L001).
- **Integer literal typing:** an untyped integer literal initializing an unannotated `var` takes its type from the variable's first constraining use in the same function (assignment, comparison, arithmetic or argument involving a typed value); unconstrained, it is `i32`. Spec update, plus a typed dump of bound declarations (no typed dump exists yet; only the syntax `AstDump` used by `parse/` goldens) that records each local's resolved type, with goldens for: constrained to `i64`, unconstrained `i32`, constrained by a comparison, by an argument, and by an assignment. `fmt --explicit` shows the type once `bayan fmt` exists; spec 004 records that as a Question. The typed dump is stable and deterministic, with a documented JSON form (`docs/` page): entries in declaration order, then by source position; no addresses, hash-order output or run-specific IDs (symbols are named by path and span). Spec 010 uses it for a binder-equivalence law between the C# and Bayan binders, as L001 does for the lexers.
- **Laws:** a law a spec proposes is enacted in the same change as its passing proof (standing authorization, 2026-10-21); a law without a proof stays proposed.

Spec order (numbers are provisional; a spec may split):

| Spec | Scope |
|---|---|
| 003 | **Done 2026-10-21.** Ownership verification: ownership program generator, debug runtime ownership checker, `take()`, `switch (move x)`; L006 `take()` row back as an amendment; prove L008 |
| 004 | **Done 2026-10-21.** Port friction: implied and swapped argument names, contextual declaration keywords, integer literal inference, returning borrows from a method's own fields, fallible bytes→string decode, `catch { block }`, `const` string arrays, `List.removeRange`, `toHex()` (format specifiers stay deferred, Appendix B) |
| 005 | **Done 2026-10-22.** Terminal style (remaining work of M12 — CLI completeness and machine-readable output), before any renderer is ported again |
| 006 | Interfaces and iterators (subset above) |
| 007 | Delegates and lambdas (non-capturing and capture by copy) |
| 008 | `HashMap` and `Symbol` interning (§3.3) |
| 009 | Parser in Bayan (`selfhost/parser/`), checked against `bayan parse --ast` on every parse golden |
| 010 | Semantics in Bayan (binder, flow, ownership), checked against every `diag/` golden and, through spec 004's typed dump, a binder-equivalence law (C# and Bayan dumps identical on every input) |
| 011 | Lowering and the C emitter in Bayan, checked against `--emit-c` on every `run/` golden |
| 012 | Driver (`check`/`build`/`run`) and bootstrap: stage 1/2/3, byte-identical stage 2 and 3, golden suite against the Bayan-built compiler, `bootstrap/bayan.c` |

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
4. **Reserved words.** §2.4 lists keywords but not which are reserved. *Current choice:* every keyword is reserved (BYN0012), including future ones (`actor`, `effect`, `macro`, `select`, `become`). Loosening later is non-breaking. *Superseded (spec 004, Q73):* the twelve declaration-only keywords (`error`, `flags`, `unit`, `distinct`, `open`, `extensible`, `set`, `type`, `law`, `derivation`, `attribute`, `embed`) are names outside declaration position; every other keyword stays reserved.
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
16. **Interpolation outside output.** v0.1 has no allocating string formatting. *Current choice:* `$"…"` is allowed only as an argument to `print`, `panic` and `StrBuf.append` (BYN2224). *Superseded (M14, Q43):* `$"…"` is a `string` expression anywhere; BYN2224 is no longer reported.
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
43. **Growable strings (M14).** *Current choice:* `StrBuf()` grows from `context.allocator` (runtime `BYN_WRITE_GROW`: capacity doubles from 16 into a fresh block and the old block is left to its allocator, so text taken earlier with `asString()` stays valid and `append` stays non-structural, Q30). `StrBuf(buf)` stays strict. `$"…"` is a `string` expression anywhere; outside `print`/`panic`/`StrBuf.append`, which keep their zero-allocation writes, it formats into a temporary growable `StrBuf` from `context.allocator` (never freed under `General`, Q31), and its result borrows that allocator's region as a `List` does (Q29). BYN2224 is no longer reported; its code and docs page are kept. The read is `asString()` (the plan says `.str`).
44. **Enums (M16).** *Current choice:* `i32` ordinal, declaration order; camelCase members, trailing `,` or `;` allowed; `count` (i64 constant), `values` (`ReadOnlySpan<E>` over a static table), `fromOrdinal(i64): E?`, `parse(string): E?` (exact, case-sensitive linear scan), `e.name`, `e.ordinal`; `==`/`!=`, formatting and `print(e)` print the member name; switches over an enum must be exhaustive (BYN2410); duplicate member BYN2721, empty enum BYN2722, unknown member BYN2720. No methods, properties or constant-specific bodies on enums yet; no `flags`/`open`/`extensible`.
51. **Static data (M16).** *Current choice:* module-level `const T[N]` with `T` a number type, `bool` or `char` and every element a literal or constant is static data (C `static const`, read in place; indexing is bounds-checked; slices, `asSpan` and arguments are `ReadOnlySpan<T>`; passing it as a mutable `Span<T>` is an error). Any other module-level `const` array or collection is BYN2229. `bayan check` on 25k elements takes 0.15 s.
53. **Processes and directories (M15).** *Current choice:* `Io.listDir` returns names sorted by byte order, without `.` and `..`; a path that is not a directory is `notADirectory`, a missing one `notFound`, a name that is not UTF-8 `invalidUtf8`. `Io.run` never uses a shell; it looks `program` up on `PATH` when it has no path separator (Windows also tries `PATHEXT`), and a missing program is `notFound`. It captures both streams completely (no streaming) and inherits the environment, working directory and stdin. A child killed by a signal reports exit code 128+signal, output that is not UTF-8 is `invalidUtf8`, and there is no timeout yet. `IoError` codes are 1–7 since spec 002 (it added two variants for `Io.runBatch`), so user error codes start at 8. A `// os: unix` line in a `run/` case's first two lines skips it on Windows.
54. **`lex-diff` (M15).** *Current choice:* `tools/lex-diff/main.byn`, built by the bootstrap compiler. A `<cmd>` is split on spaces with no quoting, and the `.byn` path is appended as the last argument. It compares stdout only, line by line, after normalizing CRLF and ignoring one trailing newline, as `Golden.Compare` does; a missing golden reads as empty. Exit 0 if all files agree, 1 if any differ, 2 on usage errors or a command that cannot start; the summary `N files, M differ` goes to stderr.
55. **Copying a `Map` or `StrBuf` (review of spec 001).** A `Map` or growable `StrBuf` is a header over heap storage, so a by-value copy shares it and a rehash or growth through one copy frees what the other uses (ASan use-after-free). *Current choice (conservative stopgap):* BYN2762 rejects storing an existing `Map`/`StrBuf`, or a value holding one by value, through `let`/`var`, assignment, construction, collection element, `List.add`/`Map.set`, a `var` `for` variable, `with` that keeps the field, or a non-local `return`. Fixed-buffer `StrBuf` is rejected too (the type does not distinguish them); a struct constructor cannot store a map parameter; returning a by-value parameter still aliases. `List<T>` has the same aliasing since v0.1 and is unchanged (`work/todo.md`). *Answered 2026-10-20 (spec 002):* allocator-backed collections (`List`, `Map`, `StrBuf`) are `unique`; a struct with a `unique` field is `unique`; copying is explicit with `.copy(to: alloc)`. An implicit copy is BYN2780 with `move`/`.copy` fixes; BYN2762 is retired. Spec §3.5, §9.
56. **Batch files in `Io.run` on Windows (review of spec 001).** `CreateProcessW` runs `.bat`/`.cmd` through `cmd.exe`, which re-parses arguments that MSVC quoting does not protect (BatBadBut, CVE-2024-24576). *Current choice:* PATHEXT lookup never picks `.BAT`/`.CMD`, and a program resolving to one is refused with `IoError.permissionDenied`. *Answered 2026-10-20 (spec 002):* `Io.run` keeps refusing; `Io.runBatch(path, args)` runs a `.bat`/`.cmd` only when every argument can be passed unchanged and otherwise fails without running anything; `Untrusted<T>` arguments are BYN2786. Law L005 (proposed) states it.
57. **Terminal style vs shipped output of M12 — CLI completeness and machine-readable output (addendum 002 §6). Answered 2026-10-20.** The current output stays until the guide is implemented as remaining work of M12 — CLI completeness and machine-readable output, which implements `docs/bayan-terminal-style.md` in full, with golden tests rendered in colour, with `NO_COLOR` and with `--ascii`. Output written before then follows the guide where it costs nothing. The guide is corrected: `NO_COLOR` disables colour only when non-empty (no-color.org; the CLI already reads it that way), and `--color` overrides both `NO_COLOR` and `FORCE_COLOR`.
58. **Type parameters in generic bodies (spec 002).** *Current choice:* ownership analysis treats a type parameter `T` as `unique` inside a generic body, so a generic function may move a `T` but not copy it implicitly; instantiations with plain types are not re-checked more leniently. Loosening needs constraints (`T: Copy`).
59. **Borrowed loop and pattern bindings (spec 002).** *Current choice:* a `for` variable or pattern binding of a `unique` type borrows from its subject even when declared `var`: it cannot be moved or mutated (BYN2783), because the binding holds a copy of the header and growth through it would leave the subject dangling. `var` bindings of other types stay mutable; BYN2311 suggests `let` for any `var` binding never reassigned.
60. **Taint sources (spec 002).** *Current choice:* `Untrusted<T>` is a type plus sinks only: `Io.runBatch` and `Io.run` reject it in the program, path or any argument (BYN2786; `Io.run` since 2026-10-21, Q67) and `trust(x, reason:)` unwraps it (BYN2787 for an empty reason). Sources (`args`, `Io.stdin`, file reads, environment) return plain values, not `Untrusted<T>`. *Open:* which sources wrap their results, and which other sinks (SQL, paths) reject it.
61. **What `Io.runBatch` refuses (spec 002).** *Current choice:* any argument, or the path, containing NUL, CR, LF, `"` or `%` makes `runBatch` fail with `invalidInput` before anything runs, and so does a path without a `/` or `\` separator, so `cmd.exe` never searches the current directory or `PATH` (`.\build.cmd` and absolute paths are accepted); every other argument is passed quoted to `cmd.exe /d /e:ON /v:OFF /s /c`. `!` is not refused: `/v:OFF` turns delayed expansion off, but a batch file that enables it itself (`setlocal enabledelayedexpansion`) can still expand `!…!` in its arguments. The Windows path is cross-compiled and unit-tested on the command-line builder, but not run on Windows in CI.
62. **Map slots and defaultable values (spec 002).** *Current choice:* `map.slot(k)` inserts the value type's default for a missing key, so it needs a defaultable `V` (numbers, `bool`, `char`, `string`, optionals, structs whose fields all have initializers); otherwise BYN2785. Compound assignment (`map[k] += v` and the other `op=` forms), `map[k]++`, `map[k]--` and `map[k] ??= v` read `map[k]` first, so like a plain read it panics on a missing key; `map.slot(k) += v` is the insert-and-accumulate form. A plain `map[k] = v` inserts or updates.
63. **Never-reassigned `var` lint (spec 002).** *Current choice:* BYN2311 is a warning on `var` locals and `var` pattern bindings never assigned, incremented, passed by `ref`/`out` or mutated through; not on parameters, `for` variables, or a `var` with no initializer. It runs once per generic template, not per instance. The parse goldens under `tests/cases/parse/` keep `var` bindings, since the parser tests never bind.
64. **`take()` (laws revision, 2026-10-20).** *Answered 2026-10-21 (spec 003):* `take()` exists on `List`, `Map`, `StrBuf` and optionals of them (BYN2788 on anything else); an unwritable place gives the existing mutation diagnostic (BYN2104/BYN2106); `map[k].take()` panics on a missing key. A `StrBuf` cannot be a struct field (BYN3001), so `take()` on it is reached only through a local or `ref`. *Open:* `take()` on a `unique struct` whose fields all have empty values.
65. **`switch (move x)` (laws revision, 2026-10-20).** *Answered 2026-10-21 (spec 003):* `switch (move x)` and `(move x) switch { … }` give the pattern bindings ownership; `x` is moved afterwards; the subject must be a local or a `move` parameter (BYN2789). The lowering moves the subject once into a temp and tests every arm against it. Without `move`, bindings borrow (BYN2783), unchanged. See Q69 for the expression form's precedence.
66. **Runtime ownership checker and program generator (L008).** *Answered 2026-10-21 (spec 003):* debug builds (`-DBYN_DEBUG`) keep an owner table keyed by storage address plus a moved slot per owning local; `move`, `take()` and `switch (move x)` transfer ownership, growth sites rekey, and a second owner or a read or move through a moved-from local panics with both locations. Safe and fast C is byte-identical to before (checked over every `run/` golden). `CompilationOptions.SkipOwnershipCheck` is the test-only switch. `OwnershipGenerator` (splitmix64, grammar in its doc comment) feeds `L008OwnershipRuntimeTests`; L008 is enacted. *Open:* the checker does not track storage inside optionals, unions, arrays or collection elements on its own (ASan still catches a use after free there); the panic exit status on Windows is untested.
67. **`Io.run` as an `Untrusted` sink (L007).** *Answered 2026-10-21:* `Io.run` is a sink for the program and every argument, under the same rule as `runBatch` (BYN2786, spec §15.1). The deep check does not look inside structs: a struct holding an `Untrusted` field can't reach either API anyway, since a struct never converts to `string` (BYN2202). L007 is enacted with `L007UntrustedSinkTests.cs`.
68. **L006's accepted `.copy` clause (review, 2026-10-21).** L006 accepts "the same programs written with an explicit `.copy(to: alloc)`", but only `List`, `Map` and `StrBuf` have `.copy`; a struct, union, tuple or optional holding a unique part does not (`BYN2003: copy is not a member of Ledger`). *Current choice:* the statement stays as the user wrote it; the proof checks `.copy` for the three types that have it, and the proof notes say so. *Open:* narrow the statement to "types that have `.copy`", or give user types a derived `.copy` (spec 003 or later).
69. **`move x switch { … }` precedence (spec 003).** *Current choice:* `move` is a prefix operator binding looser than the postfix `switch`, so `move x switch { … }` parses as `move (x switch { … })` and the expression-form owning switch needs `(move x) switch { … }`. The statement form `switch (move x)` has no such issue. *Open:* whether `move x switch` should parse as `(move x) switch`; the self-hosted parser (spec 009) must match whichever is chosen.
70. **Mutating an owning binding in a `when` guard (spec 003 review).** In `switch (move s) { case .Full(var x) when x.grow() > 50: … case .Full(var y): … }`, `x` is a copy of the subject's part, so a guard that mutates it and then fails leaves the next arm's binding with the subject's old header; when the guard grew the list past capacity, `main`'s allocator (libc `realloc`) had freed that buffer, and growing `y` was a double free in safe mode. *Current choice (conservative):* inside the guard of an owning switch the arm's bindings are read-only; a mutation, assignment, `ref`/`out` pass or move there is BYN2791. *Open:* allow guard mutations with defined semantics (a failed guard's changes given back to the subject) if a port needs them.
71. **Decode naming and offset (spec 004, R7).** *Decided 2026-10-21 (spec 004 plan):* `string.from(bytes) fails Utf8Error` and `string.tryFrom(bytes)` per spec §3.2; the offset is `string.invalidUtf8At(bytes)` returning `i64?`, because `error enum` variants have no payload and `error union` is not implemented. Fold the offset into the error when payloads exist. `Utf8Error { invalidSequence }` is declared from source like `IoError` and numbered right after it (code 8), so user error codes now start at 9 (Q53 said 8). L010 ties the three functions to `Rune.DecodeFromUtf8`.
72. **Swapped implied names, scope (spec 004, R2).** *Decided 2026-10-21 (spec 004 plan):* BYN2307 fires only when all unnamed arguments carry implied names that match parameters; a mixed call falls back to BYN2305 for the others. In the code, "all unnamed arguments" means those before the first named one, each naming a different parameter.
73. **How keywords become contextual (spec 004, R3).** *Decided 2026-10-21 (spec 004 plan):* the twelve declaration-only keywords are still lexed as keyword kinds and the parser decides, so token dumps are unchanged; `law` leaves the reserved range in both lexers (only the `lex/reserved` golden changed, and L001 holds). Statement recovery (`Parser.IsStatementKeyword`) keeps `error` and `type`, so recovery after an error may skip a statement that starts with `error =`. This supersedes Q4 for those twelve keywords.
74. **Integer literal inference (spec 004, R4).** *Decided 2026-10-21 (spec 004 plan):* two passes per function body, the second only when the first finds an unannotated `var` integer literal constrained to an integer type other than `i32`; "first constraining use" is bind order. Only integer targets retype a pending literal; a float or string constraint leaves `i32` and reports as today. `bayan fmt --explicit` will show the inferred type once `fmt` exists. Known gaps in `work/todo.md`: an optional target (`i64?`) does not constrain, and a value passed through a generic call fixes the local at `i32`.
75. **Borrows of a method's own fields (spec 004, R6).** *Decided 2026-10-21 (spec 004 plan):* applies to span return types only. A `string` view of an own field and `return sb.asString()` from a local `StrBuf` stay BYN3003, as recorded under *Friction found porting the lexer*.
76. **Typed dump scope (spec 004, R5).** *Decided 2026-10-21 (spec 004 plan):* `bayan types` covers generic templates, not instances; paths are relative to the compilation root; the JSON `version` field starts at 1 for spec 010. Format in `docs/typed-dump.md`.
77. **`catch { }` typing (spec 004, R8).** *Decided 2026-10-21 (spec 004 plan):* a `catch { … }` block on a non-`void` call is typed `never`, so the `catch` keeps the call's value type; a block that can complete normally is BYN2611 from `FlowAnalysis`, not the binder.
78. **Mutating methods and live borrows (spec 004, from T8).** *Decided 2026-10-21:* a call to a mutating method counts as a mutation of the receiver's root local, so it is BYN3004 while any borrow of that root is alive. This closes `lexer.push(t)` while a `tokens()` span is alive, and the same pre-existing gap for `for` loops. Loans stay per root local, not per field path, so `for (t in r.tokens) { r.source.mutate(); }` is a false positive; `selfhost/lexer/dump.byn` avoids it by indexing. *Open:* per-field-path loans, likely needed before the parser port (spec 009).
79. **JSON goldens under R18 (spec 005).** *Decided 2026-10-21 (spec 005 plan):* R18 scope: the JSON writers and renderer are untouched; `diag-json` goldens for reworded help lines and `results-fail.json` (new `summary.pending`) are regenerated. Any other JSON golden moving is a bug. *Answered 2026-10-22 (spec 005):* as decided; 2 JSON golden files moved (`diag-json/fix-and-help.jsonl` help rewording, `out/results-fail.json` `pending`), listed under "Not met" in the spec.
80. **`-v` phases (spec 005).** *Decided 2026-10-21 (spec 005 plan):* `-v` phases are `parse`, `check`, `emit`, `compile` (the C compiler compiles and links in one call, `Toolchain.cs:206`; a cache hit prints `compile    from cache`). The guide's per-module example is replaced. *Answered 2026-10-22:* built as decided; the guide's `-v` example now shows the four phases.
81. **Secondary spans (spec 005).** *Decided 2026-10-21 (spec 005 plan):* `Diagnostic.Related` exists and renders, but no binder site is converted in 005: converting `BYN2104` from label "declared with `let` on line 9" to a secondary span changes its label text and so its JSON. Conversion of the 8 "on line {n}" sites is a follow-up with its own JSON decision. *Answered 2026-10-22:* built as decided; `Related` renders (golden `diag-secondary`) and no binder site uses it. The follow-up stays open.
82. **Bold (spec 005).** *Decided 2026-10-21 (spec 005 plan):* `severity CODE`, `help:`, and `^^^` keep bold plus colour; the message loses bold. Bold is weight, not a second colour role, and L011 holds either way. *Answered 2026-10-22:* built as decided; the guide states it in §4.
83. **Pending exit code (spec 005).** *Decided 2026-10-21 (spec 005 plan):* Test child exit code 4 means pending (not 3: `abort()` exits 3 on Windows, so a panic would read as pending); `todo(message)` is typed `never`, accepted anywhere, and in a normal program prints `pending: <message>` and exits 4. *Answered 2026-10-22:* built as decided; the guide states child exit 4.
84. **Rerun hint and `--only` edges (spec 005).** *Decided 2026-10-21 (spec 005 plan):* Rerun hint uses the full test name; with more than three failures no hint is printed. `--only` with no match and no `--changed` is a usage error; `--only` ∩ `--changed` empty prints `running 0 tests in 0 modules` and exits 0. *Answered 2026-10-22:* built as decided, and the guide records the multi-failure form and the `--only` edges.
85. **Panic frames (spec 005).** *Decided 2026-10-21 (spec 005 plan):* Panic "frames" are the runtime's `at file:line` lines (`bayan-rt.c:40`); there is one today, so the top-three rule is implemented over `at` lines and takes effect when the runtime grows a backtrace. *Answered 2026-10-22:* built as decided; the runtime still prints one `at` line, so no cut happens yet.
86. **ASCII goldens (spec 005).** *Decided 2026-10-21 (spec 005 plan):* `.ascii` goldens are rendered with colour on; the fourth combination (ASCII, no colour) is checked only by the L011 proof, in memory. *Answered 2026-10-22:* built as decided; the L011 proof covers the fourth combination, and `tests/cases/README.md` documents the triple.
87. **Renderer copies and removal fixes (spec 005, from T5/T7).** *Decided 2026-10-21:* `selfhost/lexer/diagnostics.byn` is already a port of the diagnostic renderer, so it gets the same fix markers and wrapping in the same change, keeping `lex/` goldens shared by both lexers. A fix that only removes text shows the original line with `---` under the removed part, since the fixed line no longer contains it.
88. **Unused.** Q88 is intentionally unused: branch 005 ends at Q87 and the rebased handoff and `docs/` show no Q88; spec 006 starts at Q89.
89. **`where` placement (spec 006).** *Decided 2026-10-21 (spec 006 plan):* `where` placement: functions `… (params) [fails …] where T : I, J { }`; types `struct S<T> : I where T : J {`. Kept.
90. **`current` and `get` by value (spec 006).** *Decided 2026-10-21 (spec 006 plan):* `current` and `get` return `Item` by value; the loop variable is a `let` copy; `unique` items are BYN2022 until ref-returning getters exist. Kept, extended to `IIndexable` (D1); amended by round-2 C3: the member is `get(i64 index)`, not `at`.
91. **Default methods (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Default methods are cloned per conformer; the body is checked once on the interface with `Self` as a constrained type parameter. Kept.
92. **Vtable slots (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Vtables have a slot per method and getter, defaults included; one `static const` table per (conformer, interface instance); no sharing. Kept; slot signatures in Q110.
93. **Dyn safety (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Dyn-safe: no generic methods, no associated types. Amended: R1 has no `Self` syntax, so the `Self` clause is dropped; the interface's own name used as a type is BYN2816 (Q113).
94. **Enum conformance (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Enum conformance. Superseded by Q100.
95. **Generic interface methods (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Generic methods are declarable in interfaces solely so BYN2808 can name them; calling one is BYN2022. Kept.
96. **Implicit `ToDynRef` (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Implicit `ToDynRef` at arguments, `let`/`var` with a declared type, and returns; no `dyn ref J` → `dyn ref I`. Amended: "field initializers" dropped (a `dyn ref` field is BYN3001).
97. **Built-in interfaces (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Built-in interfaces are synthesized, not prelude source; a user redeclaration is a duplicate-name diagnostic. Amended: the pair is `IEnumerator` and `IIndexable`; `IEnumerable` is dropped (D1).
98. **Specialization (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Specialization: a conformer's own member beats the default; no other ranking. Kept.
99. **`for … in` and enumerators (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Enumerators own their state, no borrowed fields. `for … in` accepts `IEnumerator` (rvalue spilled to a hidden `var` LocalSymbol declared by `BindFor`; a named place must be a writable `var`, a `let` fails `CheckWritable` through a `MutationCheck` on `moveNext`) and `IIndexable` (looped `0..count`, `count` read once, iterable loaned for the loop via RegionAnalysis.cs:135). The enumerator root is loaned too, since only the loop advances it.
100. **Struct-only conformance (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Conformance is structs only; bases on a union or enum are BYN2815 "not supported yet". Kept through round 2 (C6): unions stay non-conforming until HANDOFF §1 union members land.
101. **Member mutability (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Superseded by design round 2 C1 (HANDOFF §9): members are read-only unless declared `ref this`; no `readonly` member marker; a witness must match `ref this` in both directions (BYN2814); `dyn ref readonly I` permits only members without `ref this` (BYN2813). Was: (D3) Abstract members marked `readonly` are non-mutating; unmarked ones may mutate. A mutating witness for a `readonly` member is BYN2814; `dyn ref readonly I` permits only `readonly` members and non-mutating defaults (BYN2813). The parser accepts `readonly` as an interface member modifier.
102. **L012 C compile (spec 006).** *Decided 2026-10-21 (spec 006 plan):* L012 compiles generated C standalone with `-Wall -Werror` (OwnershipCheckerTests.cs:364-384 pattern); Toolchain.cs:197 keeps `-w`. Kept (new in this plan; no earlier answer, not changed by round 2).
103. **Primitives (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Primitives do not conform to user interfaces; BYN2807's help says so. Kept (new in this plan; no earlier answer, not changed by round 2).
104. **Code registration (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Each code is registered in the task that first raises it, with its diag golden and explain page; `EveryCodeAppearsInAGoldenCase` stays green after every task. Kept (new in this plan; no earlier answer, not changed by round 2).
105. **Associated types (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Associated types are `type Item;` only, no bounds. Kept (new in this plan; no earlier answer, not changed by round 2).
106. **TypedDump (spec 006).** *Decided 2026-10-21 (spec 006 plan):* No acceptance criterion relies on TypedDump (it dumps functions and locals only); run/diag goldens or unit tests instead. Kept (new in this plan; no earlier answer, not changed by round 2).
107. **`this` plumbing (spec 006).** *Decided 2026-10-21 (spec 006 plan):* `this` plumbing: `FunctionSymbol.Owner` stays `StructSymbol?`; a default template has `Interface` set and `Owner = null`; `BoundThis` takes a `TypeSymbol`; the `Binder` derives `ThisType`. Alternative rejected: widening `Owner` to `TypeSymbol` touches every `Owner.` member access in the emitter for no emitted case. Kept (new in this plan; no earlier answer, not changed by round 2).
108. **Instance-time conformance (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Instance-time conformance: BYN2807 is forwarded at Compilation.cs:851 and checked inside the signature's `Instantiating` call so `_firstPass` dedupes it; generic conformers are checked once on the template and instance witnesses are built in `InstantiateType` by `Decl` twin lookup, defaults cloned with Self→instance. Kept (new in this plan; no earlier answer, not changed by round 2).
109. **`ToDynRef` ownership (spec 006).** *Decided 2026-10-21 (spec 006 plan):* `ToDynRef` ownership: `Conversions.Implicit` decides from `StructSymbol.Conformances`/`TypeParameterType.Constraints` (no `Compilation`); the lowerer keeps the operand a place (`Lvalue`); RegionAnalysis lends the operand root; a writable target requires a writable source place. Kept (new in this plan; no earlier answer, not changed by round 2).
110. **Vtable emission (spec 006).** *Decided 2026-10-21 (spec 006 plan):* Vtable emission: slots mirror `Signature`'s `fails` convention (`uint32_t` return, `ret* byn_out` last); thunks through `Helper`, vtables through a separate data list emitted after helper prototypes; `dyn ref readonly I` shares `byn_dyn_<I>`. Kept (new in this plan; no earlier answer, not changed by round 2).
111. **`let` enumerator (spec 006).** *Decided 2026-10-21 (spec 006 plan):* A `let` enumerator reuses `CheckWritable` through `MutationChecks` with the abstract `moveNext` as callee; no new code. Amended by round-2 C1: `moveNext` is mutating because it declares `ref this`; the error is BYN2104 with round 2's fix-it to `var`.
112. **Member alias (spec 006).** *Decided 2026-10-21 (spec 006 plan):* `type Item = T;` in a struct body is the existing `TypeAliasDecl` member; `DeclareMembers` treats a member alias whose name matches a base's associated type as the binding, which also makes `Circle.Item` resolvable in instances. Kept (new in this plan; no earlier answer, not changed by round 2).
113. **Interface name as a type (spec 006).** *Decided 2026-10-21 (spec 006 plan):* An interface name used as a plain type (outside a base list, `where` bound or `dyn ref`) is BYN2816; a default template is `IsGeneric` and never lowered; a type-parameter argument satisfies a constraint iff its own constraints include it; vtables and thunks are emitted on first `ToDynRef` use; `IIndexable` wins over `IEnumerator`; keyword bounds (`where T : struct`) stay parse errors. Kept (new in this plan; no earlier answer, not changed by round 2).
114. **`asSpan()` on a `let` List (found in the 006 review, 2026-10-23).** *Open:* `asSpan()` returns a writable `Span<T>` whatever the receiver, so `let k = List<i64>(); k.asSpan()[0] = 5;` compiles and a `let` List can be changed through a span. The spec (§ conversions table, rule on spans) does not say. Conservative option: on a non-writable place `asSpan()` returns `ReadOnlySpan<T>`. Not implemented; waiting for the user.
52. **Token kind names in the Bayan lexer (M22).** *Current choice:* the Bayan dump derives C# `TokenKind` names by upper-casing the first byte of the enum member name; `SelfhostLexerTests` asserts every C# kind name has that form.
45. **Unions (M18).** *Current choice:* `union Shape { Circle(f32 r), Rect(f32 w, f32 h), None }`; variants PascalCase; positional payload fields with names, read only through patterns (`.V(let x, _)`, `.V(var x)`, `Shape.V(...)`, unit `.None`); tag = declaration order; values stored inline; format `Circle(r: 1)` and unit `None`; equality by tag then payload; switches over a union must cover every variant or have a default (BYN2410 names the missing ones); unions in `catch (e) switch` unchanged (error unions deferred); property patterns on payloads deferred. Diagnostics BYN2700–2708. *Round 2 (§1):* cases are camelCase (BYN2009 with a rename fix), payload-free cases may declare `= <integer>` used as the C tag (a case without one takes the previous tag plus one), BYN2709–2711 check the values, and BYN2712 warns on a `_` arm over a union of this package.
46. **Generics (M19).** *Current choice:* `fn f<T>(…)`, `struct Pair<A, B>`, `union Option2<T>`; no constraints, so a `T` value may only be copied, passed, stored, returned, compared with `==`/`!=` and formatted (anything else is BYN2743, member access BYN2744). Inference by unification over arguments only (no target-type inference); explicit `f<i64>(…)` allowed; BYN2740–2742 for inference and arity. Monomorphized in every build mode (spec's debug witness tables deferred), C names mangled `byn_<mod>__f__<key>…`. Rules only a type argument can break (Map key, `==`/`!=`, formatting) are reported at the call or type-reference site; any other instantiation error is an internal error. No generic methods on non-generic structs (rejected as Unsupported); members of generic structs and free generic functions only.
47. **`Map` (M20).** *Superseded by spec 002:* the API is now spec §16's (`map[k]`, `map[k] = v`, `getOr`, `slot`, `remove`, `clear`, `contains`, `length`); the text below is the spec 001 state. *Current choice:* keys are integers, `char`, `bool`, `string`, plain enums (other key types are BYN2760); no indexer; `Map<K,V>()` allocates from `context.allocator` with `List` region rules; members `set`, `get` (`V?`), `containsKey`, `remove` (`bool`), `count`, `clear`; `set`/`remove`/`clear` are structural modifications (BYN3004 while iterating); iteration yields entries `(K key, V value)` in first-insertion order (updates keep position, re-insert after remove appends); formats as `{k: v, …}`; not comparable with `==`; hashing is FNV-1a 64 seeded 0 under `--test`, else time⊕pid; `HashMap` not provided. `set` is a keyword (`error set`) but parses as a member name.
48. **`Owned<T>` and recursive types (M21).** *Current choice:* `Owned(value)` allocates from `context.allocator`; `.value` reads; members auto-deref; a copyable pointer, never null, freed only with its arena; region rules as `List`. No `unique`/`move`. Read-only: no mutation through `.value` or auto-deref (BYN2106). Equality is deep, formatting prints the pointee. Direct by-value recursion in a struct or union is BYN2761, naming the cycle (replaces BYN2113 and BYN2707, now retired); recursion is allowed only through `Owned<…>`, `List<…>` or `Map<…>`. A struct field named `value` behind an `Owned` is reached as `o.value.value`.
49. **Unicode pinning (M22).** *Superseded by spec 002:* both lexers read the same generated tables (`Nfc.cs`/`UnicodeTables.g.cs` and `unicode-tables.byn`), so L001 no longer depends on system ICU. Earlier text: *Current choice:* L1 (lexer equivalence) holds per pinned Unicode version. `selfhost/lexer/unicode-tables.byn` is generated by `tools/Bayan.UnicodeGen` (Unicode 16.0.0, .NET 10.0.12) and regenerates only deliberately; the C# lexer's NFC answer follows system ICU (Q6) and may differ on another machine, which the `nfc` golden and corpus NFC cases then report.
50. **Corpus and timing (M23).** *Current choice:* the corpus generator (xorshift64, fixed seed) and the 10,000 inputs live in `SelfhostLexerTests`; `bayan-lex --many` also checks every span lies inside its input (L2). Timing: median of 5 on a 1 MB file, Bayan ≤ 10× C#, opt-out `BAYAN_SKIP_TIMING=1` (early return; xUnit 2.9.3 has no runtime skip). Measured: Bayan `fast` 6.75 ms vs C# 57 ms (C# side runs in the Debug test build).
51. **Round 3 step 0 (docs): `effect` and `rest` as words.** The plan writes `effect db = net, fs` as a declaration and `rest ReadOnlySpan<T> items` as a parameter modifier, while SPEC §2.4 reserved `effect`. *Current choice:* `effect` moves from the reserved list to a contextual declaration keyword; `rest` is a keyword (a call to a function named `rest` would now be an error, so the SPEC `switch` example calls `relax()`). Revisit if the designer wants `effect` reserved.
52. **Round 3 step 0: existing `toX` members.** Addendum 1 §3 bans instance `toX`/`asX` members (BYN5009 second form), but SPEC already has `n.toHex()` and `toUtf16()`, and `fromUtf8`. *Resolved 2026-10-23 (HANDOFF §27, plan J81–J82):* `toHex` → `string(n, as: .hex)`, `toUtf16()` → the `utf16` view, `fromUtf8` → `try string(bytes)` / `string.tryFrom(bytes)`; old spellings get BYN2230 in step 5.4.
53. **Round 3 step 0: conversion wording.** SPEC §6.2 said "use `from` / `into`" for conversions. *Current choice:* replaced by the `T(x)` conversion rule (Sūq A4), which is what the addendum's table uses.
54. **Round 3 step 0: historical documents keep round-2 fences.** `docs/suq-agent-brief.md` (Sūq-0 goldens written for the round-2 compiler), `docs/bayan-status-for-suq.md` (a status snapshot whose "Implemented" syntax is round 2) and `docs/bayan-terminal-style.md` (rendered compiler output) describe the compiler as it is today, so their code fences are unchanged. Only `bayan fmt` → `bayan format` was applied. *Current choice:* sweep them when the compiler implements the syntax.
55. **Round 3 step 0: commits.** The HANDOFF asks for a commit per document; the run harness commits the whole worktree at the end, so the edits are not split.
56. **Round 3 step 0: GRAMMAR §12 sources.** The plan gives no Arabic source for `until`/`through`, role-noun function fields or the `Untrusted<T>` status. *Current choice:* no source rows were added for them.
115. **Round 3 step 0b: `string.from(bytes)`.** O13 and J82 rename `fromUtf8` to `try string(bytes)` / `string.tryFrom(bytes)`, but the implemented v0.1 form is `string.from(bytes) fails Utf8Error` (Q71), which J82 does not name. *Current choice:* SPEC §3.2, §6.2 and §8 state `try string(bytes)`; the compiler keeps `string.from` until step 5.4, which should rename it under BYN2230 like `fromUtf8`.
116. **Round 3 step 0b: generics in debug builds.** SPEC §11 said debug builds share generic code through witness tables, with `@specialize` / `@shared` per function; HANDOFF §30 and J90 say generics are monomorphized and interface values are the explicit dynamic form. *Current choice:* SPEC §11 states monomorphization in every build mode, and `@specialize` / `@shared` are dropped. Revisit if a debug-build compile-time budget needs shared code.
117. **Round 3 step 0b: the FFI wide-string type.** HANDOFF §27 says nul-terminated UTF-16 follows the same form as `cstr` but gives no type name. *Current choice:* SPEC §18 describes the rule without naming a type; `string16` (§3.2) is unchanged.
118. **Round 3 step 0b: `Handle<T>`.** SPEC §9.6 lists `Pool<T>` + `Handle<T>` as present, while J88 keeps generational handles out of round 3. *Current choice:* §9.6 is kept and gains the typed-index-first rule from HANDOFF §31; handles are added only when the selfhost AST or Sūq shows the need.
119. **Round 3 step 0b: docs ahead of the compiler.** The `onPanic` and `asSpan()` mentions in `docs/diagnostics/BYN2019`, `BYN2612`, `BYN2613`, `BYN2614`, `BYN2786`, `BYN3004` and `BYN3006` now use `Panic.handler`, `span`, `mutableSpan()` and `copy()`, although the compiler's BYN2614 message still names `Panic.onPanic` and the builtins are renamed only in step 5.4; the rest of those pages keep round-2 syntax. The call forms in historical records are renamed too (`docs/bayan-status-for-suq.md`, `docs/suq-0-goldens-report.md`, `CHANGELOG.md`, `work/todo.md`, `work/status/previous.md`); their code fences stay round 2, as in Q54. *Current choice:* step 5.4 updates the BYN2614 message to match its page.
120. **Pointer type spelling in type-first declarations.** §18 writes `Sqlite3* db`; the friction log F-37/F-63 suggests prefix `*Sqlite3 db` / `out *Sqlite3 db`. *Decided 2026-10-24 (user):* postfix `T*` (`Sqlite3* db`); the prefix forms `*T` and `*mut T` from the old Sūq drafts become input forms. A pointer C must not write through (C `const char*`) is `readonly u8*`. SPEC §18 records it; the parser has no pointer types yet, so the input-form code is allocated when they are parsed (work/todo.md). Revision 0001 (2026-10-24) allocates BYN1066 `PrefixPointerInput` and adds `*const T` → `readonly T*`.
121. **The `using` keyword after `using let` is gone.** SPEC §2.4 (line 108) still lists `using` among the control keywords, and the C#-comparison row (line 2131) still names it. Plan C46 removes `using let` but does not say whether `using` stays reserved. *Current choice:* the keyword list and the row are unchanged.
122. **Round 3 step 5.1b: `let` or `var` in the BYN1043 local fix (J55).** J55 says the fix writes `var` when the local is reassigned, but the parser has no binder. *Current choice:* a syntactic scan of the rest of the enclosing block picks `var` when the name is assigned (`=`, compound assignment, through `.field` or `[i]`), incremented, passed as `ref`/`out`, or declared without a value; a change through a mutating method (`xs.append(…)`) is not seen, so the fix writes `let` and BYN2104's fix then turns it into `var`. Revisit if the fix moves to the binder.
123. **Round 3 step 5.1b: `const T x` and `let T x`.** Neither form is named in the plan. *Current choice:* both are BYN1043 like a bare `T x`, and the fix keeps the written keyword (`const i64 n = 1` → `const n: i64 = 1`), since SPEC writes `const` as a name-first binding.
124. **Round 3 step 5.1b: `except .case` narrowing before case-level error sets.** SPEC §3.14 narrows a `union error` with `except .rejected`, but a function's error sets are whole `union error` declarations, so `fails Retryable` cannot leave a case out of the `fail`, propagation, conformance and exhaustiveness checks without new binder state. *Current choice:* `union error R = A, B except .x, .y` parses (one or more `.case`, after the whole member list) and BYN3202 checks the composition, then BYN2022 rejects the `except` so no program gets a wider set than it declared. Revisit when error sets carry excluded cases.
125. **Round 3 step 5.1b: `type distinct` before step 5.3b.** J5 binds `type distinct X = T` as a nominal wrapper in 5.3b. *Current choice:* 5.1b parses it (`TypeAliasDecl.Distinct`) and the binder rejects it with BYN2022 rather than treating it as a plain alias.
126. **Round 3 step 5.1b: BYN1029 wording.** The plan keeps `error set` under BYN1029 but gives no new message. *Current choice:* any `error set`, and `|` between the members of a `union error` composition, is BYN1029 "an error set is declared `union error {0} = A, B`", with one fix that writes `union error` and `,`.
127. **Round 3 step 5.1b: which `unique` types BYN3209 covers.** SPEC §3.11 says a `unique` type cannot be `const`, but a module `const` of a list is a static array today (BYN2229). *Current choice:* BYN3209 fires when the unique part of the type is a `struct unique` (declared or held by a field), not for `List`, `Map`, `StrBuf` or `Owned`.
128. **Round 3 step 5.1b: receiver forms before typestate.** HANDOFF §2 lists `this is .s`, `ref this is .s`, `move this`, `move this is .s` and `this is .a or .b`; the row scopes 5.1b to `this is .s` (parse only). *Current choice:* `this is .s` and `ref this is .s` parse as the first parameter and set `FunctionDecl.ReceiverState`; the binder ignores the state (the receiver stays read-only or `ref`). A `this is .s` after other parameters is not a receiver and fails as a parameter type; `move this`, `or` states and BYN1061 wait for their steps.
129. **Round 3 step 5.1b: what follows `pattern`.** SPEC §6.5 shows only `pattern T { … }`. *Current choice:* `pattern` is the keyword only when a type name follows on the same line (`pattern` followed by a name is never an expression), then any pattern parses; `pattern .a or .b` without a type stays a name. After `=` in a `const` (module or local) it is a `PatternExpr`, which the binder rejects with BYN2022 until named patterns bind (with `except`); elsewhere BYN1046. A property pattern's designation no longer crosses a line break, so `const p = pattern T { … }` does not swallow the next line's first name.
130. **Round 3 step 5.1c: `requires` rules before step 5.3b.** Rules on fields, structs and `type distinct` now parse (`FieldDecl.Requires`, `StructDecl.Rules`, `TypeAliasDecl.Requires`), but construction-time checking is 5.3b. *Current choice:* `Compilation.CheckRequires` reports BYN2322 on each rule so none is silently unchecked; it is a binder diagnostic, not a parse one, so `bayan format` can still apply BYN1050. 5.3b deletes the check.
131. **Round 3 step 5.1c: classifying a rule by content (O3).** *Current choice:* a rule is struct-level when it has `at .f` or names a field of the same struct as a bare identifier (not after `.`); otherwise it stays on the field above. A rule under a field that uses both `it` and another field (`requires it != password`) moves to the struct with `it` unresolved; 5.3b should give it the field above as `at`, or reject it.
132. **Round 3 step 5.1c: where `because` and attribute arguments go.** *Current choice:* `because "…"` must be on the line of its attribute or call; `@name(args)` takes `(args)` only on its own line, so `@cases` alone followed by a `(`-line is not misread. The parse part of BYN8001 covers `@allow` without a reason and any empty or blank reason; `@json(expose)`, `…Unchecked()` and `unsafe` wait for 5.3a. `unsafe because` is not parsed: no `unsafe` block statement exists yet.
133. **Round 3 step 5.1c: what BYN1048 covers.** *Current choice:* `char` in a type position (including a `(char)x` cast) is BYN1048 and the tree holds `rune`; the char-literal lexer messages (BYN0007-0009) keep "char literal" because the selfhost lexer must match them byte for byte, and `PrimitiveKind.Char` keeps its C# name. `rune` is a type name, not a keyword.
134. **Round 3 step 5.1d: `except` is an expression node.** The plan says `BinaryOp.Except`, but the right side is a pattern, not an expression. *Current choice:* `ExceptExpr` (value, pattern), parsed at the `is` level (J17) and dumped as `(except v p)`.
135. **Round 3 step 5.1d: where BYN1051 applies.** *Current choice:* only `case` labels and `switch` arms report it; `_ except p` gets the fix `not p`, any other left side parses as `left and not p` with no fix. Other pattern positions are not checked.
136. **Round 3 step 5.1d: `allocator` stays.** J11 renames the allocator effect to `alloc`. *Current choice:* `provide allocator: arena { }` keeps the existing name until the effect rename is scheduled.
137. **Round 3 step 5.1d: particles.** *Current choice:* all eight (`from`, `to`, `until`, `through`, `at`, `by`, `for`, `as`) are accepted before a parameter's passing mode and as argument labels (`for:` and `as:`); any other word there is BYN5008 with no fix. `dyn` is never read as a particle.
138. **Round 3 step 5.1d: per-line `state`.** Not implemented; the `states` block of 5.1e replaces it. `become .s` parses into `BecomeStmt` and the binder ignores it.
139. **Round 3 step 5.1d: BYN6001 position.** *Current choice:* `noalloc`, `nosuspend` and `suspends` are reported only in signature position (after the parameters and again after `uses`), with a fix that deletes the word.
140. **Round 3 step 5.2b: `style.primitives: .csharp` while the parser still raises BYN1030.** SPEC §3.1 accepts the C# names with no diagnostic, but `Parser.cs` still reports eight of them in type positions as BYN1030, and no plan step retires it (`Parser.cs`, `Keywords.cs` and `TypeResolver.cs` are outside the 5.2b row, as is J70's type printer). *Current choice:* the parser is unchanged; `bayan format` under `.csharp` rewrites the sized names to C# names and does not apply BYN1030's fix, so a `.csharp` project formats cleanly and idempotently but does not build until a step retires BYN1030, reserves the C# names in the lexer and renders types in the project style. *Decided 2026-10-24 (user):* retire BYN1030, since §28 has the compiler accept both spellings with no diagnostic; tombstone like BYN1049 ("retired, the rewrite moved to `bayan format`; never reuse"). Done as a small step after 5.1d.
141. **Round 3 step 5.2b: which tokens `bayan format` respells.** *Current choice:* a primitive name is rewritten wherever it is an identifier not after `.` or `?.` (types, `int(x)`, `int.tryFrom(x)`), since SPEC reserves the C# names everywhere; under `.csharp` a local named `i32` would be renamed too. A boolean operator is rewritten only when the file parses to the same tree and the same diagnostic codes afterwards, so `is not null`, `case 1 or 2` and the `!` of `T!E` stay; `!x` becomes `not x` and `a&&b` gets spaces (`a and b`); `not x` becomes `!x` unless a comment or line break follows `not`.
142. **Round 3 step 5.2b: where `bayan format` finds `style`, and what `--check` prints.** *Current choice:* each file uses the `package.byn` in its directory or the nearest one above it; no manifest means the defaults (`.symbols`, `.sized`); a manifest with errors is reported and its files are left alone (exit 1). `--check` writes nothing, prints `would format <file>` per file that would change, and exits 1 if any would; input forms without a fix are reported as in a normal run.

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
- **2026-10-20 — M14.** Growable `StrBuf()` (`BuiltinFunction.StrBufGrow`, runtime `BYN_WRITE_GROW` over `byn_alloc`), borrowing the context allocator like `List` (Q29). `$"…"` is a `string` expression anywhere, lowered through a temp growable writer; BYN2224 retired (docs page kept, golden dropped). `run/strbuf-grow` (100,000 appends), `run/interpolation-expression`, two BYN3002 region goldens. Questions 16, 43. 388 tests pass. **Next:** M15.
- **2026-10-20 — M15.** `Io.listDir` and `Io.run` with builtin `ProcessResult { exitCode, stdout, stderr }`; `IoError` gains `notADirectory`. Runtime: `posix_spawnp` + `poll`, `CreateProcessW` with MSVC quoting and a reader thread; `Io` results no longer borrow their arguments. `run/` cases `io-run` (unix only), `io-run-missing`, `io-list-dir`; a CLI test spawns `bayan --version`. Phase-1 tool `tools/lex-diff` + `LexDiffTests`: `bayan lex` vs the `lex/` goldens exits 0, a tampered copy exits 1 naming the file and line. Questions 53, 54.
- **2026-10-20 — M16.** Plain enums (members, `count`/`values`/`fromOrdinal`/`parse`, `name`/`ordinal`, exhaustive switch, BYN2720–2722) and module-level static `const` arrays (BYN2229), parser to C. Questions 44, 51. 405 tests pass. **Next:** M17 enums with data, M18 unions.
- **2026-10-20 — M17.** Enums with data (`enum Planet(f64 mass, f64 radius) { … }`): read-only fields backed by per-field `static const` tables; arguments must fold (BYN2723), parameter types limited (BYN2724). `run/data-enums`.
- **2026-10-20 — M22, T19–T20.** The Bayan lexer `selfhost/lexer/{main,source,tokens,lexer,diagnostics,dump,unicode-tables}.byn` (`bayan-lex <path>`, `--many <dir>`, `--time <path>`), built by the bootstrap compiler with no compiler changes; `lex-diff` against the 21 `lex/` goldens: 0 differ. `SelfhostLexerTests` builds it once and checks every golden. Friction list under *Self-hosting readiness*. Question 52. 434 tests pass. **Next:** M18 unions, M19 generics, M20–M21, M23 corpus and timing.
- **2026-10-20 — M18, M20, M23.** Tagged unions with payload patterns and exhaustive `switch` (BYN2700–2708, `run/unions`); `Map<K,V>` (compact dict in the runtime, BYN2760, `MapModelTests` checks iteration order against a C# model over 5,000 random operations); the lexer corpus: 10,000 generated inputs lexed by one `bayan-lex --many` run, identical to the C# lexer, no panics, spans in range; timing 0.12× the C# lexer. Questions 45, 47, 50. 455 tests pass. **Next:** M19 generics, M21 `Owned<T>`.
- **2026-10-20 — M21, T17.** `Owned<T>` (`Owned(x)`, `.value`, auto-deref, deep equality, pointee format; C `T*` from `bynh_own_<T>`), region analysis treats it like `List`, by-value recursion is BYN2761 with the cycle named. `run/owned-expr` (expression tree), `run/owned-list`, `diag/owned-escapes-arena`, `diag/owned-read-only`, `diag/recursive-cycle`. 460 tests pass.
- **2026-10-20 — M19, T14–T15.** User generics: generic functions, structs and unions, unification-based inference, per-instantiation binding with a worklist, templates skipped by flow/region analysis and lowering; BYN2740–2744. `run/generics-functions` (one function at `i64`, `string`, a struct; `GenericsTests` asserts exactly three instantiations in the emitted C), `run/generics-types` (`Pair<A,B>`, `Option2<T>`, `Tree<T>`, `Box<T>`), `parse/generics`, seven `diag/` cases. 472 tests pass.
- **2026-10-20 — T23, spec sections touched by spec 001.** Implemented subsets of §3.5 (`Map`), §3.12 (unions), §3.13 (enums), §8 (`main`, `[ExitCode]`), §9.1 (`Owned<T>`), §11 (generics), §13.1 (`Io` blocking); each subset's limits are Q40–54. `docs/bayan-spec.md` itself is unchanged: the spec describes the full language, and the handoff Questions record where the bootstrap stops short.
- **2026-10-20 — Spec 001 review.** Two-lens review of the branch, 10 confirmed findings fixed: `Map`/`StrBuf` copies shared heap storage (ASan use-after-free) → BYN2762 (Q55); Windows `Io.run` lookup, batch-file refusal (Q56) and UTF-8 argv; errno after `fclose`; test hash seed only for programs with tests. Run-golden timeout raised to 120 s (cold parallel builds after a runtime change timed out at 20 s). Three low findings and `List` copy aliasing in `work/todo.md`. All spec 001 acceptance criteria are met; laws L001–L004 are proposed and await enactment. 476 tests pass.
- **2026-10-20 — Addendum 002 §1.** `law`, `requires`, `ensures` reserved in both lexers and spec §2.4 (`lex/reserved` golden); addendum filed and referenced from M12 and *After v0.1*; Q57 records that the terminal-style guide is normative but not yet implemented.
- **2026-10-20 — Q57 answered.** Updated `docs/bayan-terminal-style.md` (`NO_COLOR` only when non-empty; `--color` overrides both variables). Current output stays until M12, which implements the guide in full with colour, `NO_COLOR` and `--ascii` goldens.
- **2026-10-20 — Q57 milestone reference.** M12 — CLI completeness and machine-readable output was not renumbered or split; the terminal-style work is attached to it as open work, and Q57 and addendum 002 §6 name it by number and name.
- **2026-10-20 — Spec 002.** Ownership and law alignment: `unique` collections with `move` and `.copy(to:)` (BYN2780–2783, BYN2784, BYN2762 retired); Map API per §16 (BYN2785); shared Unicode tables and `bayan lex --json`/stdin; `Untrusted<T>`, `trust` and `Io.runBatch` (BYN2786, BYN2787); L004 amended for irrefutable arms; BYN2311 never-reassigned `var` lint. Laws L001–L004 enacted and proven; L005 and L006 proposed. Q55, Q56 answered; Q58–Q63 added; Q47, Q49 superseded.
- **2026-10-20 — Spec 002 review.** Two-lens review with refutation, three fix loops: `defer`/`errdefer` bodies are checked against the ownership state at each exit where they run (`errdefer` only at `try`, error `return` and a `catch` naming one variant), a failed `when` guard's moves reach later sections, `map[k] op= v`/`++`/`--`/`??=` read through the panicking index (`??=` on a map no longer crashes the emitter), formattability checks every part of a type for `Untrusted<T>`, and `Io.runBatch` requires a path separator. 4 candidates refuted in loop 1; loop 3 clean.
- **2026-10-20 — Laws revision.** L005 restated (Unicode comparison under code page 65001, `shift` past `%9`, AutoRun/DelayedExpansion settings, CR/LF/NUL always fail, `unsupported` off Windows); its `Untrusted` sentence moved to new guard L007. L006 split into guard L006 (the compiler rejects every second owner) and property L008 (no runtime aliasing, checked by ASan and a debug ownership checker). Spec §9.3: no moves out of fields (`take()` instead), pattern bindings borrow and `switch (move x)` moves, debug ownership checker, all outside `unsafe`. Questions 64–67 record what is not implemented yet.
- **2026-10-21 — Laws L005–L007 enacted.** `Io.run` is now an `Untrusted` sink for the program and every argument (BYN2786, spec §15.1, golden `diag/run-untrusted`). L005 is proven by `L005RunBatchTests` (33 edge cases plus 4,000 generated through `byn_batch_cmdline` and a `cmd.exe` read-back model; off Windows, refused and accepted calls return `unsupported`). L006 is proven by `L006OwnershipGuardTests` (29 rejected, 14 accepted). L007 is proven by `L007UntrustedSinkTests` (24 cases over both APIs). L008 stays proposed until spec 003. The review found and fixed: wrong row counts in the L006 notes, a missing off-Windows refused case and a narrow L005 alphabet. It also recorded Q68 (`.copy` for user types) and a todo for a real `cmd.exe` run. 696 tests pass. Worker spend: $6.56 (writers $2.21, gates $3.50, review $0.85). Next: spec 003 (ownership verification).
- **2026-10-21 — Spec 003 (ownership verification).** `x.take()` (BYN2788), `switch (move x)` with owning bindings (BYN2789; BYN2790 for two bindings whose full paths overlap and both own storage; BYN2791 for a `when` guard that mutates or moves its arm's binding, Q70), a debug-build runtime ownership checker, and a seeded ownership program generator (with `when` guards). L008 enacted with its proof; L006 amended (43 rejected, 26 accepted rows). Checker details:
  - A `switch (move x)` binding is claimed by a lowered `LClaim` at the start of the selected arm.
  - `Operands` spills a `move` that a later operand reads.
  - Owned places passed by `ref`/`out` or as a method receiver claim their storage after the call. Element receivers are not tracked.
  - `byn_own_rekey` releases the target before the lookup; `byn_own_remove` clears the slot.
  - The checker is compiled into debug builds only: the toolchain passes `-DBYN_NDEBUG` for safe and fast, debug `--emit-c` defines `BYN_DEBUG`, and `bayan-rt.c` defaults to the checker when neither is set. `SafeAndFastExecutablesHaveNoChecker` asserts no `byn_own_` symbol in safe/fast executables.
  - The ASan probe honours `BAYAN_CC`, with compile/run timeouts of 120 s and 10 s.

  The review ran three two-lens loops with refutation; every confirmed finding was fixed. Going past the 3-loop cap of `/code-review` was deliberate: loop 3 found a double free in safe mode (guard mutation plus `realloc`), and the conservative fix (BYN2791) came from AGENTS.md's spec-gap rule. Safe/fast `--emit-c` of every `run/` golden is byte-identical to dev (116 files). 829 tests pass; all 8 laws hold. Open: `work/todo.md` items F (`StrBuf.take()` refill context), the `_ = move a` help text, and a claim skipped for a root moved on a branch that does not run. Worker spend: $98.22 (writers $29.79, gates $45.20, think $7.51, reviews $12.57, audits $3.15). **Next:** spec 004 (port friction), already in progress on `feat/004-port-friction`.
- **2026-10-21 — Spec 004 (port friction).** Every item of the lexer port's friction list that the road assigns to this spec (R1–R12):
  - Arguments named like their parameter (`start`, `tok.start`) count as named for BYN2305; implied names in a swapped order are BYN2307 (Q72).
  - The twelve declaration-only keywords are contextual in the parser, and `law` left the reserved range in both lexers (Q73; L001 holds).
  - An unannotated `var` integer literal takes the type of its first constraining use, by a two-pass bind (spec §2.3, Q74).
  - `bayan types` prints the typed dump (`docs/typed-dump.md`, `tests/cases/typed/`, Q76).
  - A method returning a span may lend its own fields, and a mutating method call mutates the receiver's root (spec §9.4, Q75, Q78).
  - `string.from` / `string.tryFrom` / `string.invalidUtf8At` with `Utf8Error` (Q71).
  - `catch { … }` and `catch (e) { … }` blocks, BYN2611 when one on a value-returning call can complete normally (Q77).
  - `const` string arrays (BYN2229 reworded), `List.removeRange`, integer `toHex()`.

  L009 (typed dump deterministic) and L010 (decode accepts exactly valid UTF-8) enacted with their proofs. Spec §2.3, §2.4, §3.2, §3.5, §8, §9.4 and §10 updated. The branch review (two lenses, five refutation audits, then two `low` re-review loops) fixed seven defects: pending `var`s that meet each other now share one inferred type (Q74 kept per member, unary `-` included); a `catch` brace body of `name = value` fields with no `;` is a struct literal again (`catch {}` stays a block, an empty struct handler is `catch P{}`); BYN2307 needs two or more swapped arguments; generic instances made in a rolled-back first pass are removed. One review finding was refuted. 979 tests pass. Open in `work/todo.md`: R4 gaps (group order dependence, a group mixing a narrow integer and a float use), an untyped array literal as a `for` source, and a load-sensitive allocation test. Worker spend: about $105 (plan think $7.18; writers about $17; gates about $66, T16 the most at $17.71 over two runs; reviews $5.97; audits $0.91). **Next:** spec 005 (terminal style), in progress on `feat/005-terminal-style`.
- **2026-10-22 — Spec 005 (terminal style).** `bayan` output now follows `docs/bayan-terminal-style.md`: one `Style` (palette and symbols) for every human-facing line; colour per stream from `--color`, `NO_COLOR`, `FORCE_COLOR`, `TERM=dumb`, `COLORTERM`, `BAYAN_THEME`, `COLORFGBG`; `--ascii` and a locale fallback (`*`, `x`, `|`, separator `-`); fix markers and related spans, wrapping, the 20-error cap with `--all`; build/check/run summaries and `explain:` footers, `-v` phase times; `bayan test` header, failure blocks with assertion values, summary and rerun hint, `--only`, `-v` listing, `todo()` pending (child exit 4, `pending` in JSON); `doctor` layout; a progress line; CLI text in the `Messages` table; help text starts with a verb; goldens in `tests/cases/style/` rendered colour, `NO_COLOR` and `--ascii`; contrast test; law L011 enacted. Q79–Q86 answered, Q87 recorded. Not met, all listed in the spec: JSON goldens moved (Q79), loose-file paths are absolute, no binder diagnostic uses a related span, `-v` pass lines have no timing and the pending line differs from the plan. Laws L001/L002 exceed `laws-check`'s 600 s per-law limit under load and were verified by uncapped filter runs. The branch review (two lenses, 14 claims, 6 refuted by audit, then two re-review loops) fixed: shell-safe quoting of the rerun hint, control and bidi characters escaped in test names and output, the progress line cut to the terminal width (wide characters count two columns), `parse`/`lex` dumps in the chosen style, no wrapping inside backticks, `-v` only on `build`/`test`, the `--only` no-match error with `--changed`, and global options no longer taking a script's own arguments. Left in `work/todo.md`: keypad escapes (A1), ASCII progress (A5), emoji outside two ranges counting one column, the unselectable-rerun notice wording. 1097 tests pass, L001/L002 included. Worker spend: about $105 (writers and gates about $86, T18 docs $5.43, review finders and audits $6.71, fixes and their gates $6.06, re-reviews $0.71). **Next:** design round 2 (`feat/design-round-2`), then spec 006 (interfaces and iterators).
- **2026-10-22 — Round 2 §1 (unions).** Union cases are checked as values (camelCase, BYN2009 with a rename fix); every golden, C# inline source and diagnostic page that names a union case migrated (`.Circle` → `.circle`, formatted output too). Payload-free cases accept `name = <integer literal>` (`UnionVariant.Value`, shown by the AST dump); the value is the emitted C tag, and a case without one takes the previous tag plus one. New codes BYN2709 (value not an `i32` integer literal), BYN2710 (value on a case with data), BYN2711 (two cases share a value, implicit ones included), BYN2712 (warning: `_` arm over a union declared in this package; help lists the uncovered cases). The existing BYN2410 already names missing cases (`diag/non-exhaustive-union`). Spec-only and not done here: spec §3.12–3.14 text, union members after `;`, `Set<T>`, `tryParse`, closedness.
- **2026-10-22 — Overflow traps in every build mode (design round 2 §3).** `fast` no longer passes `-DBYN_FAST`, and `bayan-rt.h` lost its `#ifdef BYN_FAST` wrapping branch, so `+ - *`, negation, `++`/`--` and narrowing casts trap in `debug`, `safe` and `fast` alike; `-fwrapv` stays. `+% -% *%` still wrap and `+| -| *|` still saturate. Golden `run/overflow-fast` now expects the panic; new golden `run/wrapping-fast` shows the explicit operators in `fast`. `checked(expr)` stays spec-only.
- **2026-10-22 — Collection and map literals (design round 2 §11, step 3.1).** Map literals `["A-1": 12]` (`MapEntryExpr` inside `CollectionExpr`, dumped as `(map (entry k v))`) bind to the target `Map<K, V>` or, untyped, to the first entry's types (`Map<string, string>`); `Map<K, V> m = []` is an empty map. An untyped `[1, 2, 3]` is `i32[3]`. Spreads `..xs` (`BoundSpread`) go into `List<T>` (a copy loop), and into `T[N]`/`Span<T>` (stack storage) only from fixed arrays, since the length must be known; a list or span spread there is BYN2219 (existing code, no new one). New BYN2235 `SpreadNotSequence`, BYN2236 `MapLiteralMixed`. Goldens `run/collection-literals`, `parse/collection-literals`, `diag/spread-not-sequence`, `diag/map-literal-mixed`, `diag/spread-run-time-length`; `CollectionLiteralTests` checks span literals emit a stack array. `Set<T>` literals and the `length`/`add` renames (3.4) are not done.
- **2026-10-22 — Design round 2 closed.** Input forms (§15, a72c866), the plan §8 outcome (ba8428e), the Sūq-0 goldens with the BYN2019 method-group crash fix and `.pending` markers (Sūq brief Part D, 75c5560), and the final two-lens review: 13 findings, 1 refuted, 12 fixed (30503d6): input-form reports no longer resync and drop the rest of the line; one fix per overlapping span (BYN1032/BYN1007 over BYN1017, innermost BYN1018); `i32!E f()` shorthand accepts qualified names and unions with `fails`; a `defer` body that is a declaration is an error again; postfix `unless (…)` looks through parentheses; a BYN1017 fix on a braceless body also braces it; BYN2027, BYN2209, BYN2225 and BYN3104 help and fixes give code that compiles. Full suite 1368/1368 (SelfhostLexerTests included), all 11 laws hold; break-tests on the resync and literal-merge fixes fail as expected. Spend this stretch: gates r2-wrapup $6.49, suq-abc $7.06, suq-d $2.03 (+ $3.39 continue), r2-finalfix $15.04; review lenses $4.10. Open items in `work/todo.md`.
- **2026-10-22 — Spec 006 (interfaces and iterators).** Interfaces work as constraints (`where T : I, J`, monomorphized static dispatch) and as `dyn ref I` / `dyn ref readonly I` fat pointers with one `static const` vtable per (conformer, interface), default methods cloned per conformer, associated types (`type Item;`), struct-only conformance, and `for … in` over user `IIndexable` and `IEnumerator` conformers (`current` and `get` return `Item` by value). Diagnostics BYN2800–BYN2816. Law L012 (static and dynamic dispatch agree) enacted with its proof (compiled with `-Wall -Werror`, Q102). Q89–Q113 recorded, Q88 unused. Deferred, listed in spec §3.15 "Self-hosting gaps": retroactive conformance, `static abstract`, generic interface methods called through constraints, interface inheritance, union/enum conformance, borrowed enumerators, associated-type bounds. Worker spend: T8 $2.85, T9 $1.49 (gate), T10 $1.34, T11/T12 $4.58; T1–T7, the review fixes and T13 not recorded in the commit bodies.
- **2026-10-23 — Spec 006 re-review fixes (C–G) and merge.** Region analysis now tracks borrows of `this` fields, `dyn ref` for-variables, element writes and by-value element arguments (C1–C4). It also tracks borrows of `ref`/`out`/`in` arguments by calls returning a span or `dyn ref`, a `ref this` call that structurally changes a borrowed field (a bodiless callee changes every field), `ref items` while borrowed, loops over a `this` List field, and span `dyn ref` loops (E1–E5). `for dyn ref` mutates element slots. A `unique` field cannot witness a getter (BYN2780), and a `unique` `Item` under a constraint is reported at instantiation (D1–D3). A `for dyn ref` loop that mutates elements is a write to the array: it is BYN2104 on `let`, and BYN2311 no longer fires on `var` (F). Before this fix, the `let` form compiled and mutated the array. `move` and `.take()` while a span or `dyn ref` still borrows the value is the new error BYN3006 "moved while borrowed" (G1, the review's B5 hole). A borrow's loan now starts where the expression first lends the owner, not at the end of the initializer, so `first(w.items.asSpan(), w.grow())` is BYN3004 (G2). Spec R2 states the field-witness rule as built. A generic stand-in audit found region analysis sound for instantiated generics; uninstantiated generic structs are not region-checked (`work/todo.md`). The cross-check of the 007–012 specs against 006 as built is applied on the feat/007, feat/008 and feat/009 branches, all unmerged. A final review of the fixes (9e2da65..eb4d97d, `low`, three loops) found 6, then 4, then 4 more, all confirmed by probe, none refuted. They are fixed as H, I and J. H: `ref this` arguments change every field; `for dyn ref` over a `ReadOnlySpan` is BYN2106; changed fields are memoized per method (Tarjan SCCs on `Compilation`), so 200 `ref this` methods check in 0.17s instead of 18.4s; ref arguments lend only when their type can hold storage; the loan starts at the stored borrow; a writable `for dyn ref` over a temporary is accepted again. I: place-ness by shape; a `dyn ref` result keeps its ref arguments' regions (BYN3002); writable `dyn ref` arguments count as writes. J: span-index, `with`, `try` and `catch` places; `this` outlives the method body; passing or calling through a writable `dyn ref` value mutates its owners. Open, in `work/todo.md`: `asSpan()` on a `let` (Q114), and two narrower `dyn ref` tracking gaps. Full suite 1587/1587 after J (one unnamed failure in two of five full runs under load, gone on rerun; `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray` is the known load flake). All 12 laws hold. The C–J break-tests fail as expected. Spend: C $2.51, D $6.81, E gates $2.14 + $2.33, F $0.24 + gates $3.50 + $2.01, G $1.73 + gates $4.60 + $1.82, spec cross-check $2.43, 009 doc writer gates $2.52 + $1.92, generic audit $0.62, final review loops $0.88 + $0.93 + $1.33, H $6.50 gate, I $0.92 + $1.10 gate, J $1.90 + $7.80 gate.
- **2026-10-24 — Round 3 revision 0001.** User's updated HANDOFF and plan adopted: postfix pointers and `readonly T*` (§2, J91, BYN1066), BYN1030 retired (§28, J92), `#line` debug mapping (§30, J93), spec freeze and `docs/revisions/` (§32, J94). Diff and effect on built work in `docs/revisions/0001-round-3-pointers-line-freeze.md`. Next: `#line` step in parallel; pointers after 5.1e; then the freeze once deliverables 1–3, 5 and 9 are done.
- **2026-10-24 — Round 3 step 5.1d (parse part).** `except` (`ExceptExpr`, BYN1051, BYN1052), `uses`, `becomes`, `provide` (BYN6006 for `context (…)`), `become`, parameter particles (BYN5008), BYN6001 for `noalloc`/`nosuspend`/`suspends`. Diagnostics pages and `diag/` goldens for the five new codes, four `parse/` goldens, all `context (` sources converted to `provide`, `uses` field renamed in `run/generics-types.byn`. `become` is now a keyword (`lex/reserved.tokens` lost its BYN0012). Questions 134–139. Next: 5.1e.
