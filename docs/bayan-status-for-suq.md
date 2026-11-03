# Bayan status for porting Sūq

Commit: `af964b51e36bf17ffc5d5ea2597a2323112a478b` (`feat/design-round-2`) — snapshot 2026-10-22 ~14:00; branches move after that

Test run (`dotnet test -v q`): `Failed!  - Failed: 1, Passed: 1315, Skipped: 0, Total: 1316, Duration: 22 m 35 s`. The failure is `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray`, an allocation-count test, on a machine loaded by other test runs; re-run alone it passes (1/1).

Answers come from code and passing tests on `feat/design-round-2` (dev plus design round 2, gated, not merged into dev). Items marked *(round 2, unmerged)* exist here but not on dev. Work on other branches is reported as "in progress on <branch>" and is never counted as Implemented. Statuses were checked by running the CLI (`bayan new`, `check`, `run`) on small programs under `tmp/`.

Paths: `run/x` means `tests/cases/run/x.byn` (with `.stdout`), `diag/x` means `tests/cases/diag/x.byn` (with `.diag`), `parse/x` means `tests/cases/parse/x.byn` (with `.ast`). `*.cs` files are under `tests/Bayan.Tests/`.

**Syntax note.** Everything Implemented uses round-2 syntax: C#-style declarations with the type first, `;` terminators, `[Attr]` attributes, `fn`-less `T name(params)` functions, and struct literals with `=` or `:`. No round-3 syntax is implemented. The Sūq sources (`suq/*.byn.cs`) are written in round-3 syntax, so none of them compile today.

```csharp
public union Shape { circle(f32 r), rect(f32 w, f32 h), none }
i64 total(ReadOnlySpan<i64> xs) fails ParseError { … }
let p = Point { x = 1, y: 2 };
```

## 1. Milestones

### v0.1 and self-hosting milestones (docs/bayan-handoff.md)

| Milestone | Scope | Status | Evidence |
|---|---|---|---|
| M0 Skeleton | solution, `--version`, golden runner, diagnostics model | Implemented. CI workflow exists (Linux, macOS, Windows); the progress log says CI was not yet run | `DiagnosticRendererTests.cs` |
| M1 Lexer | UTF-8 tokens, NFC, bidi, literals, interpolation, recovery | Implemented | `tests/cases/lex/`, `LexerTests.cs` |
| M2 Parser | v0.1 grammar, recovery, `parse --ast` | Implemented | `tests/cases/parse/` |
| M3 Semantics I | names, types, mutability, casing, must-name args | Implemented | `diag/valid-types`, `diag/immutable-assignment` |
| M4 C backend | lowering, C emission, `build`/`run`, modes | Implemented | `run/hello`, `run/arithmetic` |
| M5 Errors | error enums, `fails`, `try`/`catch`, `defer`/`errdefer` | Implemented | `run/errors-catch`, `run/errors-cleanup` |
| M6 Memory basics | slicing, `stackalloc`, `Arena`, `context`, region rules | Implemented | `run/memory-slicing`, `run/memory-arena` |
| M7 Tests | `test` blocks, `assert`, `bayan test` | Implemented | `tests/cases/style/test-ok.plain` |
| M8 tally-lite | v0.1 exit criterion | Implemented | `examples/tally-lite/main.test.byn` |
| M9 `new`/`init` | project scaffolding | Implemented | `tests/cases/new/`, `ScaffoldTests.cs` |
| M10 `out/`, `dist/` | build outputs and releases | Implemented | `OutputTests.cs` |
| M11 Cache | global content-addressed cache | Implemented | `CacheTests.cs` |
| M12 CLI completeness | JSON diagnostics, exit codes, `doctor`, `--changed`, colour | Implemented | `tests/cases/diag-json/`, `CliTests.cs` |
| M13–M23 (spec 001) | file I/O and args, growable strings, processes, enums, static arrays, unions, generics, `Map`, `Owned<T>`, self-hosted lexer | Implemented | `run/io-copy-file`, `run/strbuf-grow`, `run/io-run`, `run/enums`, `run/static-arrays`, `run/unions`, `run/generics-functions`, `run/map-basics`, `run/owned-expr`, `SelfhostLexerTests.cs` |

### Specs (docs/specs/ and "Road to self-hosting")

| Spec | Scope | Status |
|---|---|---|
| 001 | self-hosted lexer, compiler-writing subset (M13–M23) | Implemented |
| 002 | ownership for collections, `Map` API, `Io.runBatch`, laws L001–L004 proofs | Implemented |
| 003 | `take()`, `switch (move x)`, runtime ownership checker, L008 | Implemented (`run/take`, `run/switch-move`, `L008OwnershipRuntimeTests.cs`) |
| 004 | port friction: implied/swapped argument names, contextual keywords, literal inference, `string.from`, `catch { }`, `removeRange`, `toHex` | Implemented (`diag/argument-implied-name`, `run/string-from`, `run/to-hex`) |
| 005 | terminal style for CLI output | Implemented (`tests/cases/style/`) |
| 006 | interfaces and iterators | In progress on `feat/006-interfaces-iterators`: T1–T4 of 13 landed (parse, symbols, declarations, `dyn ref` type rules); T5 (conformance) in progress on `chore/worker-006-t5`, not merged; dispatch, `for … in` user types, codegen not landed |
| 007 | delegates and lambdas | In progress on `feat/007-lambdas` (spec, plan and tasks only, revised for round 3 at `c27ec58`; no compiler code) |
| 008 | `HashMap` and `Symbol` interning | Not started in code; spec, plan and tasks on `feat/008-hashmap-symbols` (`3911393`) |
| 009 | parser in Bayan | Not started in code; spec docs being written on `feat/009-selfhost-parser` |
| 010 | semantics in Bayan, binder-equivalence law | Not started |
| 011 | lowering and C emitter in Bayan | Not started |
| 012 | driver and bootstrap | Not started |

### Design handoffs

| Handoff | Status |
|---|---|
| Round 1 (`bayan-handoff.md`, addendum 001) | Applied |
| Addendum 002 (laws, contracts, parallel iterators) | Partial: §1 keyword reservation only; `law` declarations, `bayan laws`, `ensures`, `.par()` not started |
| Round 2 (`bayan-design-round-2.md`) | Partial *(round 2, unmerged)*, per section below |
| Round 3 (`feat/design-round-3`) | Not applied in code. Docs in progress on `feat/design-round-3`: the HANDOFF with O1–O10 resolved (`e60ab6f`), the plan (status "awaiting approval"), and GRAMMAR/STYLE updated. SPEC.md updated there too (`76583ce`). Code alignment of plan and GRAMMAR to the spec merged (`b538763`) |

Round 2 per section *(round 2, unmerged)*:

| § | Status | Evidence |
|---|---|---|
| 1 Unions (camelCase cases, value cases, wildcard lint) | Implemented | `run/union-values`, `diag/wildcard-on-local-union` |
| 2 Operators and equality | Spec only | — |
| 3 Arithmetic (traps in every mode) | Implemented | `run/overflow-add`, `run/wrapping-fast` |
| 4 Control flow (paren-less, `for x in`, `while let`, `is not null`, narrowing, `?? return`) | Implemented | `run/for-in`, `run/while-let`, `run/null-narrowing`, `parse/control-flow-bare` |
| 5 Errors (`catch switch` without binding, `error set`) | Implemented | `run/errors-catch-switch` |
| 6 Cleanup and panics | Implemented (panic handlers not run on wasi) | `run/errors-defer-failing`, `run/panic-handler-order`, `run/on-panic-order` |
| 7 `requires` | Partial: functions only | `run/requires`, `run/requires-block` |
| 8 Lambdas | Spec only (spec 007 on `feat/007-lambdas`) | — |
| 9 `ref this` | Implemented | `run/ref-this`, `diag/mutates-without-ref-this` |
| 10 `:` struct literals | Implemented (both `=` and `:` accepted; BYN1021 not raised) | `run/struct-literal-colon` |
| 11 Collections (literals) | Partial: literals and map literals done; `count`/`append` renames in progress on `chore/worker-r2-stdlib` | `run/collection-literals` |
| 12 Strings (members, `+` ban, `StrBuf` API) | In progress on `chore/worker-r2-stdlib` | — |
| 13 `format()` | In progress on `chore/worker-r2-stdlib` | — |
| 14 Async and `context` | Spec only | — |
| 15 Input forms | In progress on `feat/r2-inputform-base` / `chore/worker-r2-inputform` (stage 1 partial) | — |
| 16 Testing (`[Cases]`, `assert` statement, power-assert) | Implemented | `run/test-cases.test.byn`, `run/assert-values`, `PowerAssertTests.cs` |
| 17 Naming checker | Implemented (fields, free functions, generic instances not checked) | `NamingGrammarTests.cs`, `diag/noun-on-mutator` |
| 18 STYLE / `style` in `package.byn` | Partial: manifest field validated, read by nothing; no formatter | `tests/cases/manifest/style-invalid.byn` |
| 19 Derivations | Spec only | — |

Review fixes for round 2 (findings 1–6, 8, 10) are merged here at `af964b5` *(round 2, unmerged)*: `for try` over a `var` sequence advances that variable, `defer failing(x);` calls a function named `failing` instead of opening a `defer failing` block, `requires n > Limit {}` parses the block as the body, conditions may start with a parenthesis (`if (i32)big > 3`, `if (a) in 0..=3`, `for (x) in xs`), and the new BYN2615 (a `defer panicking` body reads a local moved after it). Evidence: `run/for-try-var`, `run/defer-call-named-failing`, `parse/requires-bare-condition`, `run/condition-cast`, `run/condition-paren-in`, `run/for-paren-single-variable`, `diag/panic-handler-reads-moved`.

Round-3 sections not applied in code: all of them, §1–§21. Docs status on `feat/design-round-3`: HANDOFF and plan written; GRAMMAR and STYLE updated for §1, §5, §12, §13, §15; SPEC updated (`76583ce`). The request's "audit fixes" did not match a named section in the round-3 docs; "audit" there means the `bayan audit` command (§4) and the Sūq naming audit in the friction log.

## 2. Toolchain

Tried on a fresh `bayan new demo` project in `tmp/`.

| Command | Status | What it does |
|---|---|---|
| `build` | Implemented | Emits C, compiles through `zig cc` (else `cc`/`clang`/`gcc`) into `out/<mode>/<target>/`; `--mode debug|safe|fast`, `--target`, `--emit-c`, `-o` |
| `run` | Implemented | build then run; also `bayan script.byn` |
| `test` | Implemented | each test in its own process; `--json`, `--changed`, `--only`, `-v` |
| `check` | Implemented | diagnostics only |
| `explain` | Implemented | `bayan explain BYN2104` prints the embedded page |
| `new` / `init` | Implemented | app, `--lib`, `--script`; `init` adds missing files |
| also present | Implemented | `lex`, `parse --ast`, `types` (typed dump JSON), `dist`, `clean`, `cache stats`, `doctor` |
| `format` | Not started | `unknown command` (round 3 plan step 5.1a/5.2b; renamed from `fmt` by round 3 §29) |
| `expand` | Not started | `unknown command` |
| `audit` | Not started | `unknown command` (round 3 §4) |
| `features --used` | Not started | `unknown command` (round 3 J22) |

Evidence: `CliTests.cs`, `ScaffoldTests.cs`, `OutputTests.cs`, `tests/cases/style/`.

- Codegen target: **C**, compiled by `zig cc` (cross-compiling needs zig). No LLVM, no direct native backend. Targets: `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32` (wasi). Verified here on `macos-arm64`. CI is configured for ubuntu, macOS and Windows.
- Multi-file, multi-module project with `package.byn`: Implemented. Modules are directories under `src/`; `import money;` and `money.twice(21)` worked in the probe. Evidence: `examples/tally-lite/`, `diag/import-cycle/`, `diag/not-visible/`.
- Path dependencies between packages: Not started. `deps` and `capabilities` are accepted manifest field names and read by nothing.

## 3. Syntax (round 3)

| Item | Status | Actual syntax |
|---|---|---|
| Waqf termination (no `;`) | Not started | `;` required (BYN1005) |
| Name-first `let`/`var` locals; type-first declarations (J54) | Declarations: already what the compiler does. Locals: not started. `->` declaration input form: not started | `i64 x = 1;`, `let x = 1;`, `struct P { i64 x; }`, `i64 f(i64 a) { … }`; `->` does not lex |
| `struct unique` | Changed from spec | modifier-first `unique struct S { … }`; also inferred: a struct holding `List`/`Map`/`StrBuf` is unique (`diag/unique-struct-containment`) |
| `type distinct` | Not started | `distinct` is a contextual keyword; `distinct type` does not parse |
| `union error` | Changed from spec | `error enum E { a, b }` and `error set S = A, B;` (`run/errors-catch-switch`, `parse/error-set`) |
| `@` attributes, `because` | Not started | `[ExitCode(2)]`, `[Cases(…)]` (`parse/attributes`, `diag/unknown-attribute`); `@` is an error |
| `//` only | Not started | `/* */` still accepted |
| `///` doc comments | Implemented (as error/variant summaries) | `run/errors-catch` (`$"{e}"` prints the summary) |
| Doctests | Not started | — |
| Booleans: both spellings, `style.booleans` | Not started | `&&`, `||`, `!` only; `and`/`or`/`not` are pattern combinators (`run/switch`) |
| Postfix `if`/`unless` | Implemented | `return 0 if n == 0;` (`run/postfix-modifiers`, `parse/postfix`) |
| Block `unless` | Implemented; `else` after `unless` is rejected (BYN1006, `diag/unless-else`), `unless (!x)` rejected (`diag/unless-negated`) | `unless ready { … }` |
| `else` placement | Implemented as C#: `} else {` or next line | `parse/control-flow-same-line` |
| Argument particles | Not started | Round-1 rule instead: same-typed and `bool` params must be named (`diag/argument-must-be-named`); an argument whose name equals the parameter counts as named (`diag/argument-implied-name`); swapped names are an error (`diag/arguments-swapped`) |
| One-unlabeled-parameter rule and its exemptions | Not started | — |
| Visibility | Implemented | Default is module (directory). `public` exposes a type, a field, a function or a member to other modules and packages; `private` limits to the declaring type. Each field needs its own `public` (probe: reading a non-`public` field of a `public struct` across modules is BYN2005). `diag/not-visible/` |

## 4. Types and values

| Item | Status | Actual syntax / test |
|---|---|---|
| Structs, methods, getters, constructors | Implemented | `run/structs` |
| Unions with payloads | Implemented | `union Shape { circle(f32 r), rect(f32 w, f32 h), none }` (`run/unions`) |
| Unions with members (methods) | Not started | — |
| Exhaustive `switch` | Implemented | `diag/non-exhaustive-union`, `diag/non-exhaustive-enum` |
| Payload binding in an arm | Changed from spec | positional: `.circle(let r) => r * r`, `Shape.rect(var w, _) => w` (`parse/unions`). No `.percentOff p => p.rate` |
| Enums with data | Implemented | `run/data-enums` |
| Distinct types | Not started | `type Cents = i64;` is a plain alias |
| Units, suffixed literals | Parsed only | `5kg` lexes; binder gives BYN2022 "the unit suffix `kg` is not supported in v0.1" |
| `T(x)` constructors | Not started | `u8(x)` is BYN2220; conversion is a C# cast `(u8)x`, which traps when out of range (`run/overflow-cast`) |
| Try forms | Partial | only `string.tryFrom(bytes)` (`run/string-from`); no general `tryX` |
| `requires` on functions | Implemented *(round 2, unmerged)* | `i32 f(i32 p) requires p in 0..=100 => …;` (`run/requires`, `diag/requires-violated`, `parse/requires-bare-condition`) |
| `requires` on fields | Parsed only | `i32 quantity requires quantity > 0;` parses (`diag/requires-field`) |
| `requires` on distinct types, struct-level `at`, `ValidationError<T>`, `T.Patch`, `T.rules` | Not started | — |
| `with` | Implemented, no re-checking | `p with { y = 4 }` (`diag/valid-types`, `diag/map-copy`) |
| Typestate (`state`, `becomes`, `become`, `T is .state`) | Not started | `become` is a reserved keyword only |
| Generics | Implemented, monomorphized, no constraints | `T pick<T>(bool first, T a, T b)`, `struct Pair<A, B>` (`run/generics-functions`, `run/generics-types`, `GenericsTests.cs`); a `T` value cannot be used beyond copy/compare/format (`diag/type-parameter-member`); generic methods are BYN2022 (`diag/generic-method`) |
| Constraints, `where` | Not started here; in progress on `feat/006-interfaces-iterators` (T1 parses `where`, T4 resolves `where T : I`) | — |
| Interfaces, default methods, `dyn`, `extension`, `embed` (milestone 006) | Not started here. On `feat/006-interfaces-iterators`: interface declarations, conformance lists, `dyn ref I` type rules, built-in `IEnumerator`/`IIndexable` (T1–T4). Not done: conformance check, default-method instances, static/dyn dispatch, codegen, `for` over user types (T5–T13). `dyn I` (owning) is BYN2809 there; `extension` parses there; `embed` is a contextual keyword only | — |
| `for x in` over ranges, spans, lists, maps | Implemented | `for x in xs { … }`, `for (let x in xs)` (`run/for-in`) |
| `for try` | Implemented *(round 2, unmerged)* | over a value with `T? next(ref this) fails E` (`run/for-try`, `run/for-try-var`) |
| `while let` | Implemented *(round 2, unmerged)* | `while let h = half(n) { … }` (`run/while-let`) |
| `while try` | Changed from spec | `while let line = try lines.next() { … }` (`run/while-let`) |
| Lazy queries (where/map/any/first/count/sum/collected) | Not started | needs lambdas (007) |
| `except`, named patterns, `yield` | Not started | `yield` is a reserved keyword |
| `List`, fixed arrays, `Span`/`ReadOnlySpan`, `Map` | Implemented | `run/collection-literals`, `run/map-basics`, `run/static-arrays`, `run/memory-readonly-span` |
| `FixedList`, `Set` | Not started | `Set` is an unknown type |
| List and map literals | Implemented *(round 2, unmerged)* | `["A-1": 12, "B-2": 3]`; empty map is `[]` with a map target type, not `[:]` (`run/collection-literals`) |
| `.asSpan` | Implemented | `run/collection-literals` |
| `^1`, slicing | Implemented | `xs[^1]`, `xs[1..^1]` (`run/memory-slicing`) |
| Typed indices `List<T, I>`, `.indices` | Not started | — |
| Strings: UTF-8, interpolation | Implemented | `run/interpolation-expression`, `diag/string-indexer` |
| `rune` | Not started | `char` is the 32-bit scalar type |
| `StrBuf` | Implemented | `StrBuf()`, `.append`, `.asString()` (`run/strbuf-grow`) |
| `xInto` with derived allocating form | Partial | `void formatInto(ref StrBuf sb)` drives `$"{p}"`; no derived allocating form |
| `owned()` | Not started | `owned` is not a member of `string`. Copy a borrowed string with `$"{s}"`, or a collection with `.copy(to: arena)` (`run/collection-copy`) |
| Copy/move of structs owning collections | Implemented | collections are unique: implicit copy is BYN2780 (`diag/unique-implicit-copy`); `move x`, `move` params (`run/unique-move`); `take()` (`run/take`) |
| `unique` and drop | Partial | `unique struct` and ownership checks are implemented; no user `drop`; `let a = Arena();` frees owners (`run/memory-arena`, `diag/not-disposable`) |
| Overflow traps | Implemented, every mode *(round 2, unmerged)* | `run/overflow-add`, `run/overflow-cast` |
| `checked` | Not started | reserved keyword |
| `+%`, `-%`, `*%`, `+|`, `-|` | Implemented | `run/wrapping-fast` |
| Numeric conversions | Changed from spec | C# casts `(T)x`, checked; implicit widening only |
| `Secret<T>` | Not started | unknown type |
| `Untrusted<T>` and taint | Partial | `Untrusted(x)`, `trust(x, reason: "…")`; sinks: `Io.run`, `Io.runBatch` (BYN2786) (`run/untrusted-trust`, `diag/run-untrusted`, `L007UntrustedSinkTests.cs`). No sources, no sink types, no `trustUnchecked() because` |

## 5. Errors and control flow

| Item | Status | Actual syntax / test |
|---|---|---|
| `fails` | Implemented | `i64 f(string s) fails ParseError { … }`; bare `fails` infers for non-`public` functions (`run/errors-catch-block`, `diag/public-inferred-fails`) |
| `try` | Implemented as a prefix on one call | `let d = try digit(s);` (`run/errors-catch-block`). Round-3 `try` scope (§18) not started |
| `catch e =>` | Changed from spec | `f() catch (e) expr`, `f() catch (e) { … }`, `f() catch { … }` (`run/errors-catch`, `run/errors-catch-block`) |
| `catch switch` | Implemented | `f() catch switch { EmailError => 10, DbError e => code(e), _ => … }` (`run/errors-catch-switch`) |
| `fail` | Changed from spec | `return .invalid;` (or `return .invalid if n == 1;`) |
| `?? return` | Implemented | `let v = h() ?? return .a;` (`run/null-narrowing`) |
| `?? fail` | Not started | — |
| Composed error sets | Implemented *(round 2, unmerged)* | `error set SignupError = EmailError, DbError;` (`run/errors-catch-switch`) |
| `Result<T, E>` | Implemented (one error enum) | `run/errors-result` |
| `never` | Implemented | `never stop() { panic("x"); }`, `if never` (`run/if-never`, `diag/never-returns`) |
| `defer`, `errdefer` | Implemented | `run/errors-cleanup` |
| `defer failing (e)`, `defer panicking (p)` | Implemented *(round 2, unmerged)* | `run/errors-defer-failing`, `run/panic-handler-order`, `parse/defer-family`, `run/defer-call-named-failing`; a handler reading a local moved after it is BYN2615 (`diag/panic-handler-reads-moved`) |
| When `defer` modifiers are evaluated | Changed from spec | `defer stmt if c;` parses and runs, but `c` is evaluated at scope exit, not at registration (probe; no golden). Round 3 J20 wants registration time |
| `panic`, `Panic.handler` | Implemented *(round 2, unmerged; renamed to `handler` in round 3)* | `Panic.handler = onCrash` with `p.message` (`run/on-panic-order`, `run/panic-handler-binding`, `diag/on-panic-signature`); not on wasi |
| `todo(message)` | Implemented | `run/todo` |

## 6. Effects and concurrency

| Item | Status |
|---|---|
| `uses` clauses, closed effect list, private inference, bounds on interfaces/function types, package capabilities | Not started (`uses` does not parse; `capabilities` in `package.byn` is unread) |
| `provide`, ambient `log` | Not started |
| `context (allocator: a) { … }` | Implemented (`run/memory-arena`) |
| `async`/`await` | Not started (reserved keywords) |
| `Io.group`, `Mutex`, guard-across-suspension check, `Io.offload`, `Clock.sleep` | Not started. `Io` is blocking only: `readFile`, `readText`, `writeFile`, `listDir`, `run`, `runBatch`, `stdin/stdout/stderr`, `monotonicNanos` (`run/io-copy-file`, `run/io-list-dir`, `run/io-run`, `run/io-stdin`) |

## 7. Compile time and metaprogramming

| Item | Status |
|---|---|
| comptime functions, comptime file access, `Json.typeFrom` | Not started (`comptime` reserved; module-level `const` values and `const T[N]` arrays work: `run/static-arrays`, `run/const-string-array`) |
| `@derive` (Json, Row, Config, Args) | Not started; no derivation exists |
| Derived forms `tryX`, `xInto`, `-ed`, `XOp`, `@forms` | Not started (only the hand-written `string.tryFrom` and `formatInto`; the naming checker knows `-ed` and `try` names: `NamingGrammarTests.cs`) |
| Laws | Not started as a language feature (`law` is a contextual keyword; `law f(…) { … }` does not parse). `docs/laws/` holds compiler laws L001–L012 proved by C# tests, not Bayan `law` declarations |

## 8. Testing

| Item | Status | Actual syntax / test |
|---|---|---|
| `test` | Implemented | `test "name" { … }` in the file or in `x.test.byn` (`examples/tally-lite/main.test.byn`) |
| `@cases` | Changed from spec *(round 2, unmerged)* | `[Cases((0, false), (1, true))] test "bounds" (i32 input, bool valid) { … }` (`run/test-cases.test.byn`) |
| Power asserts | Implemented *(round 2, unmerged)* | `assert(net == cents(2));` prints a value tree (`tests/cases/style/test-assert-values.plain`, `PowerAssertTests.cs`, `run/assert-values`) |
| Assert that a call fails | Not started | write `f() catch { return; }` then `panic(…)` |
| `bench`, setup vs measured part, `@budget` | Not started |
| Mutation testing, coverage | Not started (for Bayan programs) |

## 9. Standard library and interop

| Module | Status |
|---|---|
| io/fs | Partial: `Io.readFile`, `readText`, `writeFile`, `listDir`, stdio (`run/io-copy-file`, `run/io-list-dir`, `run/io-stdin`) |
| processes | Implemented: `Io.run`, `Io.runBatch` (`run/io-run`, `run/io-run-batch`) |
| http client and server | Not started |
| json | Not started (`Json` undefined) |
| db/pg (COPY, readers, transactions) | Not started |
| `sql` literals | Not started |
| regex, html, url, crypto | Not started |
| hex | Partial: integer hex formatting (`run/to-hex`); round 3 spells it `string(n, as: .hex)` |
| time/clock | Partial: `Io.monotonicNanos` only |
| log | Not started |

| FFI / scripting | Status |
|---|---|
| `extern` | Parsed only as a modifier: `extern i32 abs(i32 x);` is BYN1016 "`extern` is not allowed here" |
| `unsafe because` | Not started (`unsafe` is accepted as a function modifier and ignored; no `because`, no `unsafe { }` block) |
| Raw pointers | Not started |
| Linking a native library through `package.byn` | Not started |
| Shebang | Implemented: `#!/usr/bin/env bayan` then `bayan script.byn`, with cached script binaries (`CacheTests.cs` `ARememberedScriptStartsWithoutCompiling`, `tests/cases/new/script.tree`) |
| `#:io` and other `#:` directives | Parsed only: lexed as trivia at the top of the file, not interpreted |
| Top-level `try` | Implemented: top-level statements form `main`; an escaping error prints and exits 1 (`run/while-let`, `run/errors-main-fails`) |

## 10. Decisions taken differently, and open questions

Decisions on this branch that differ from SPEC or a HANDOFF:

- `catch (e) expr` / `catch (e) { … }` / `catch { … }` instead of `catch e =>`; `catch { }` on a non-`void` call is typed `never` (Q77).
- `return .variant` instead of `fail`; no `?? fail`.
- Union payloads are positional patterns (`.circle(let r)`), not named bindings (Q45).
- Casts `(T)x` (checked) instead of `T(x)` constructors.
- Empty map literal is `[]` with a target type, not `[:]`.
- `while let x = try e` instead of `while try`.
- `defer stmt if c` evaluates `c` at exit (round 3 J20 wants registration).
- `unique struct` is modifier-first (round 3: `struct unique`); a struct holding a collection is unique without the keyword.
- `error enum` / `error set S = A, B` instead of `union error`; `error set` also accepts `|` until BYN1029 lands.
- `for try` works over a fallible sequence (`T? next() fails E`) rather than BYN2022 (round 2 judgment call; SPEC §7.4 updated).
- Struct literals accept both `=` and `:` (BYN1021 not raised yet).
- Narrowing applies to `let` locals and by-value/`in` parameters only.
- Overflow: fast and safe builds both trap (round 2 §3); `+%` etc. for wrapping.
- `requires` on fields and distinct types is spec only; BYN2322 has a descriptor and page only.
- Panic handlers do not run on wasi.
- `[Cases]`: values must be literals; `ref`/`move` modifiers and defaults on test parameters parse and are ignored; `[Cases]` with no arguments gives zero tests silently.
- Naming checker skips fields, free functions and generic instances.
- Generic methods are BYN2022; generics have no constraints (Q46).
- `string` fields are allowed in structs (Q19); `Span` fields are BYN3001.
- Every keyword is reserved except twelve declaration keywords that are contextual (Q73).
- Untyped integer literal in an unannotated `var` takes its type from first constraining use (Q74).
- `Io` is blocking only; `context.io` indirection deferred (Q42).

Open questions still waiting:

- Round 2 HANDOFF "Open": `with { … }` vs `withF(v)`. Answered in the round-2 plan §5 (keep `with`); SPEC text pending.
- Round 2 J19: GRAMMAR "Conditionals" row has no documented source ("to be confirmed").
- Round 3: O1–O10 resolved 2026-10-22; the round-3 plan (J1–J25) is "awaiting approval".
- Handoff Questions on in-flight branches, recorded with conservative choices and not yet merged: Q89–Q113 (spec 006, `feat/006-interfaces-iterators`), Q114–Q127 (spec 007, `feat/007-lambdas`). Q88 is intentionally unused (handoff Q88 line, 006 T13).
- Specs 001–007 list no blocking open questions; their gaps are handoff Questions with the conservative option implemented.
- Sūq friction items not yet decided (from `bayan-reference-app-friction.md`): F-01 (`assert … fails`), F-02 (`bench` setup), F-03 (law syntax and levels), F-04 (payload binding), F-05/F-06 (typestate literals), F-07 (`with` re-check scope), F-08 (compile-time check of literal `requires`), F-09 (particle exemptions for `extern`, operators, payloads, laws), F-10 (variadic `sql` args), F-11 (`Untrusted<bool>` as condition), F-12/F-18 (unique containment, long-lived borrows), F-13 (untrusted columns), F-14 (owning-struct copy), F-15 (`owned()`), F-16, F-17 (`requires` on `Secret`), F-19 (distinct-type constructor names), F-20 (numeric conversions), F-21 (block-bodied computed properties), F-22 (stable sort), F-23 (group timeout mapping), F-24/F-25 (`Bytes`), F-26 (map literal), F-27 (`@forms` naming), F-28 (`@blocking`), F-29 (block arms in `catch switch`), F-30 (`extern` block and `drop`), F-31 (static factory naming), F-32, F-33 (`Secret` storage), F-34 (`for (a, b) in`).
