# Bayān: lessons from other languages and platforms

Status: reference, as of 2026-11-05. Not spec. Spec changes still go through `docs/revisions/` or a numbered design round.

**Purpose:** record what other ecosystems got right, what they could not fix, and Bayān's answer. A lesson counts as learned only when it becomes a compiler rule, a tool behaviour or a test, not a style note.

**Status key:**
- **Adopted**: decided or in the spec.
- **Planned**: decided, scheduled.
- **Proposed**: needs a decision.
- **Watch**: no action until there is evidence.

Handoff mapping: `docs/plans/2026-11-05-000-lessons-index.md`.

## 1. Performance and code generation

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| P01 | Zero-cost abstractions | C++, Rust | Abstractions that allocate or dispatch invisibly | Monomorphized generics; interface values `{data, vtable}` are the only dynamic dispatch | Adopted |
| P02 | Generic code bloat | C++, Rust | Binary size and compile time explode | One C body per layout and operation set (round-4 draft P3) | Planned (round 4) |
| P03 | No aliasing enables optimization | Fortran, Rust `noalias` | C's pessimistic aliasing blocks vectorization | `BY_RESTRICT` on `ref` parameters and `struct unique` values the checker proves exclusive | Planned (round 4) |
| P04 | Safety checks must be cheap | Rust, Zig | Checks dropped in release, or slow everywhere | Checks in every build mode (compare plus cold `noreturn` trap); proven checks removed (round-4 draft P2) | Adopted / Planned (v0.2a) |
| P05 | First-class SIMD | Zig `@Vector`, C# `Vector<T>`, Mojo | SIMD only through intrinsics | `core` vector types lowering to C vector extensions, with a scalar fallback | Planned (v0.4) |
| P06 | Data-oriented layout | Jai, Odin `#soa` | Rewriting AoS ↔ SoA by hand | `@soa` (round-4 draft, v0.3+) | Watch |
| P07 | JIT warm-up and memory | JVM, CLR, V8, Julia | Slow start, large resident memory | Ahead-of-time only; no JIT or VM | Adopted |
| P08 | Binary size matters | Go, Rust | Multi-MB "hello world" | Size baseline (A2), then a CI budget (v0.2a) | Planned |
| P09 | Performance claims need evidence | V; Rust/Zig benchmark culture | Marketing ahead of reality | Public benchmarks including losses (round-4 draft P1); CI gates | Planned (A2, v0.2a) |
| P10 | Allocation is explicit | Zig | Hidden heap traffic | Allocators through `provide`/`context`; `General` default for `main`; `alloc` effect | Adopted |

## 2. Memory and safety

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| M01 | Safety without a GC is possible | Rust | Lifetime annotations put newcomers off | Region-bound references, per-callee escape summaries, inference first | Adopted |
| M02 | One memory model only | Nim (several GC modes), D (optional GC) | Ecosystem splits by memory mode | One model; .NET only through C ABI exports of NativeAOT libraries; `core`/`std`/`kit` never depend on it | Adopted |
| M03 | Null safety | Hoare, Kotlin, Swift | Null-pointer exceptions | `T?` with narrowing (`is not null`, `?? return`); no suppression operator | Adopted |
| M04 | Value semantics | Hylo/Val, Swift | Shared mutable references | Structs only; `struct unique`; `ref this` for mutation | Adopted |
| M05 | Few ownership modes | Austral, Rust | Too many modes to learn | `struct unique`, `move`, borrows; no extra capability kinds | Adopted |
| M06 | Thread-safety by type | Pony, Rust `Send`/`Sync` | Data races | Auto-derived `ISendable` / `IShareable` | Adopted |
| M07 | Auditable escape hatches | Rust `unsafe` | Unsafe code spreads unnoticed | `because` required on every hatch; `bayan audit` lists them | Adopted (round 3); completeness in round 4 |
| M08 | Fuzzing and sanitizers built in | Go, cargo-fuzz, Clang | Bolted-on tooling | `bayan test --fuzz`, `--sanitize` | Planned (v0.2a) |
| M09 | Taint and secrets in types | Perl taint mode | Injection, leaked secrets | `Untrusted<T>`, `Secret<T>`, two-layer trust model | Adopted |
| M10 | Validated types | Ada/SPARK | Validation repeated at every boundary | `type distinct … requires …`, units, `expects` | Adopted |

## 3. Errors and failure

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| E01 | Errors as values | Rust, Go, Zig | Invisible exception control flow | `fails` clause, `Result<T, E>` | Adopted |
| E02 | Boilerplate kills discipline | Go `if err != nil` | Errors ignored to save keystrokes | `try`, `catch switch`, `?? return`, `?? fail` | Adopted |
| E03 | Checked errors must compose | Java checked exceptions | No generics over exceptions in lambdas | `union error` composition; `fails` polymorphism for closures (round 4) | Adopted / Planned |
| E04 | Error sets should be inferable | Zig inferred error sets | Huge, stale error lists | Private signatures infer `fails` (as they infer `uses`); `signatures: .declared` requires every signature to declare | Adopted (2026-11-05), round 4 |
| E05 | Crash recovery as a pattern | Erlang/OTP | One fault takes down the process | Supervisor pattern in `kit` on `scope` | Watch |
| E06 | One panic strategy | Rust abort vs unwind | Mixed strategies | One global `Panic.handler` in the entry method | Adopted |
| E07 | No runtime exceptions in practice | Elm | Run-time surprises | Exhaustiveness, typestate and narrowing; remaining traps are named 9xxx codes | Adopted |

## 4. Concurrency

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| C01 | Function coloring | Rust, JS, C# async | Duplicate sync and async APIs (Jaunty's overload explosion) | Fibers only | Adopted |
| C02 | Structured concurrency | Trio, Kotlin, Swift | Orphaned tasks | `spawn` only inside `scope`; implicit cancellation | Adopted |
| C03 | Locks across suspension | JVM Loom pinning | Hidden throughput collapse | No lock held across a suspend point; `@blocking`, compute offload | Adopted |
| C04 | Thread-per-core | Seastar, Glommio | Cross-core contention | Thread-per-core, fibers pinned to their thread | Adopted |
| C05 | Message passing | Erlang, Go channels | Shared-state bugs | Channels and actors in `std`/`kit` | Watch |

## 5. Type system and language design

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| T01 | Too many ways to do it | Perl, C++, Scala | Dialects | "One form, one meaning", with guarded variety | Adopted |
| T02 | Implicit magic | Scala implicits, C++ ADL | Code that can't be read locally | Explicit `provide`; target-typed `.Member` only against one expected type | Adopted |
| T03 | Turing-complete types | TypeScript, C++ templates | Undebuggable type-level programs | Computation lives in comptime with a step budget | Adopted |
| T04 | Tracked effects | Koka, Haskell | Hidden I/O | `uses` effects and aliases; ambient logging | Adopted |
| T05 | Typestate | Rust builder patterns | States by convention | `states { … }`, `Order.paid`, `becomes` | Adopted |
| T06 | Conformance coherence | Rust orphan rule | Conflicting implementations | Rule B: a conformance lives with the type's owner or the interface's owner; others wrap with `type distinct` | Adopted (2026-11-05), round 4 |
| T07 | Laziness surprises | Haskell space leaks | Unpredictable memory | Strict by default; lazy only in queries and cursors | Adopted |
| T08 | Readable signatures | Rust/C++ generic noise | Line-long signatures | Clause order `where → expects → uses → becomes → fails` (round 4); `T[]` view shorthand | Adopted / Planned |
| T09 | Culture-sensitive defaults | .NET culture bugs | Locale-dependent behaviour | No ambient culture; `kit.intl` with `for:` | Adopted |
| T10 | String model | C# UTF-16, Python 2 | Encoding bugs | UTF-8 strings, codecs, `utf16` view | Adopted |

## 6. Compile time and metaprogramming

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| X01 | Comptime in the same language | Zig, Jai | Separate macro language | Comptime with a step budget | Adopted |
| X02 | Generated code must be visible | C# source generators | Debugging invisible code | Staged generators; readable output in `gen/`, never committed, always regenerated | Adopted |
| X03 | Macros hurt tooling | Rust proc macros | IDEs can't analyse them | Generators run on the same semantic model as the IDE | Planned (v0.2b) |
| X04 | Build scripts in the language | Zig `build.zig` | Make/CMake sprawl | `package.byn` stays data; scripted builds only on evidence | Watch |
| X05 | Reflection without runtime metadata | Zig `@typeInfo`; .NET AOT trim warnings | Reflection that breaks AOT | Reflection is compile-time only | Adopted (round 4 rule); syntax Watch |

## 7. Tooling, build speed and inspection

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| B01 | One toolchain binary | Go, Cargo, Zig | Fragmented tools | Single `bayan` CLI | Adopted |
| B02 | Formatter ends style wars | `gofmt`, `rustfmt` | Review bikeshedding | `bayan format`; style source of truth in `package.byn` | Adopted |
| B03 | Check without codegen | `cargo check` | Slow edit loop | `bayan check` | Planned (v0.2a) |
| B04 | Slow backend for debug builds | Zig, Rust | Edit-compile loop dominates | Toolchain phase 4 (QBE-style debug backend) | Watch |
| B05 | Incremental compilation | Zig, Rust, OCaml | Full rebuilds | Content-hash module cache | Planned (v0.2a) |
| B06 | Content-addressed code | Unison | Rename churn | Content hashes in the build cache only | Watch |
| B07 | Show what the code became | Compiler Explorer | "Trust me, it's fast" | `bayan show c`, `bayan show assembly` | Planned (v0.2a) |
| B08 | Hot reload | Dart, Erlang | Restart loops | With `kit.ui` | Watch |
| B09 | REPL and scripting | Python, Ruby, Kiln | Ceremony for small tasks | Scripting mode; REPL when builds are instant | Planned / Watch |
| B10 | Use mature IDEs | Successful languages | Weak tooling | Language server, debug adapter, thin Visual Studio extension, `#line` mapping | Planned (v0.2b) |
| B11 | Stable syntax API | Roslyn, rust-analyzer | Every tool reparses | Public, versioned syntax and semantic API | Planned (v0.2b) |
| B12 | Hermetic cross-compilation | Zig, Go | Per-target SDK installs | `zig cc` now; own sysroots in toolchain phase 3 | Adopted / Planned (v0.4) |
| B13 | Reproducible builds | Nix, Debian, Go | Unverifiable binaries | Deterministic output (K6); `bayan verify` | Planned (v0.2a) |

## 8. Diagnostics and learnability

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| D01 | Errors that teach | Elm, Rust | Cryptic errors | Plain-English diagnostics, BYN codes, `bayan explain` | Adopted; page coverage gate in v0.2a |
| D02 | Library-defined compile errors | Rust `#[diagnostic]` | APIs that fail only at run time | Library authors define diagnostics | Adopted |
| D03 | Backend errors never leak | Nim, C++ | Users debugging generated C | C compiler errors are internal compiler errors | Adopted |
| D04 | Progressive disclosure | Swift | Ownership on day one | A beginner golden per new feature | Adopted (round 4 rule) |
| D05 | Readability first | Python, Ruby | Clever syntax | Arabic-derived grammar, grammar checker | Adopted |
| D06 | Docs in the toolchain | `cargo doc`, pkg.go.dev | Docs drift | Doctests now; Markdown doc comments (leaning); doc site later | Adopted / Watch |

## 9. Packages and supply chain

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| S01 | Dependencies get full authority | npm event-stream, PyPI malware, xz | Arbitrary code with full access | Effect grants via `uses:` in `package.byn` | Adopted |
| S02 | Install-time code execution | npm `postinstall`, `setup.py` | Compromise at install | No install scripts; comptime and generators sandboxed | Adopted; verification in v0.3 |
| S03 | Deletion breaks the world | npm left-pad | Broken builds | Immutable versions; yank, never delete | Planned (v0.3) |
| S04 | Integrity | Go checksum DB, Sigstore | Silent tampering | Hashed lockfile, signed publishes, transparency log | Planned (v0.3) |
| S05 | Typosquatting | npm, PyPI | Look-alike packages | Scoped names; similarity check at publish | Planned (v0.3) |
| S06 | Packaging chaos | Python | Fragmentation | One tool and format from day one | Adopted |
| S07 | Tiny-dependency culture | npm | Huge trees | Rich `core`/`std`; `bayan dependencies` | Planned (v0.3) |
| S08 | SBOM | EU CRA, US guidance | Manual compliance | `bayan dependencies --sbom` (CycloneDX) | Planned (v0.3) |

## 10. Interop and platforms

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| I01 | Seamless C import | Zig `@cImport`, Swift | Hand-written bindings | Header import into `extern c` blocks | Planned (v0.4) |
| I02 | Successor languages need interop | Kotlin, Carbon, TypeScript | Rewrite-everything adoption | C only: C ABI export; .NET through NativeAOT C exports (JauntyQ pilot); `bayan migrate cs` for source | Adopted (2026-11-05), v0.4 |
| I03 | ABI stability | Swift, C++ | Rebuild-everything or frozen layouts | Source packages by default; stable C ABI only for `--library` exports | Planned (v0.4) |
| I04 | Embeddability | Lua | Can't live inside other apps | Static library builds without global state | Planned (v0.4) |
| I05 | WebAssembly | Rust, Zig, Go | Web as an afterthought | `wasm32-wasi` for fiber-free code | Planned (v0.4) |
| I06 | AOT breaks reflection | .NET NativeAOT | Late publish surprises | Comptime reflection only (X05) | Adopted |

## 11. Evolution and governance

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| G01 | Breaking changes without a fork | Python 2→3 | Community split | Editions in `package.byn` | Adopted |
| G02 | Automatic migration | `cargo fix --edition`, `go fix` | Manual upgrades | `bayan migrate --edition` | Planned (v0.3) |
| G03 | Public design process | Rust RFCs, PEPs | Opaque decisions | Numbered revisions and design rounds; settled list | Adopted |
| G04 | Feature creep | C++, Scala, C# | Ever-growing language | Written keyword budget (round 4) | Adopted (round 4 rule) |
| G05 | Superset promises | Mojo, Carbon | Overpromised compatibility | No C# compatibility promise | Adopted |
| G06 | Deprecation policy | Java, .NET | APIs that never die or vanish | `@deprecated(by:, until:)` | Adopted (round 4) |

## 12. Testing and quality

| ID | Lesson | From | Failure to avoid | Bayān's answer | Status |
|---|---|---|---|---|---|
| Q01 | Tests live with code | Rust, Zig, Go | Drifting test projects | Inline tests; effects inferred | Adopted |
| Q02 | Coverage lies | PIT, Stryker | Weak asserts at 100% coverage | Mutation testing with CI score gates | Adopted |
| Q03 | Power asserts | Groovy, Swift | Fluent assert APIs | Power asserts, doctests, parameterized tests, benchmarks | Adopted |
| Q04 | Property-based testing | QuickCheck, Hypothesis | Missed edge cases | Generators from `requires` and `type distinct`; runner also finishes `law` | Planned (v0.2a) |
| Q05 | Laws as specs | Bend, Haskell | Undocumented properties | Opt-in `law` declarations | Adopted |

## Things others couldn't solve, and Bayān's bet

| Problem | Why others are stuck | Bayān's bet |
|---|---|---|
| Safety vs learnability | Lifetimes in signatures | Inference-first regions, typestate, plain-English diagnostics |
| Async splits APIs | Retrofitted | Fibers from day one with `scope` |
| Supply-chain attacks | Permissions added late | Effect grants in the type system and the manifest |
| Readable systems code | Syntax inherited from C or ML | A grammar with rules, enforced by a checker |
| Fast and safe together | Checks off in release | Checks always on, made cheap |
| Fast edit loop for native code | LLVM-bound toolchains | `bayan check` and caching now; a fast debug backend later |
