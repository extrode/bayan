# Bayān — Design round 4 (draft)

Nov 1, 2026

## 1. Status and process

This is a draft. It is finalized only after 012 passes (self-hosted compiler, stage 2 equals stage 3, golden suite green). Until then, mid-flight changes go through addendum A1, not this document.

Order of work:

1. Retrospective (sections 2 to 5): what we promised, what drifted, what to revisit.
2. Feature decisions (sections 6 to 9), revised by what the retrospective finds.
3. A numbered plan in the round-3 format, handed to the agent after 012.

Where this draft cites the spec, it relies on the round-3 plan, its consolidated summary and recorded decisions.

Clause order changes in round 4 to `where → expects → uses → becomes → fails`; examples here use the new order. It lands as the first commit after 012: flip the canonical order in BYN1065, run `bayan format` over the repository (including the self-hosted compiler), regenerate goldens. No manual edits. Until then the order is proposed, not canonical. Round-4 edits: the BYN1065 message text (`ClauseOrder` in `DiagnosticCodes.cs`), `bayan-style.md:16` (signature clauses row), spec :147, :151 and :1592, and the order for function types (`() -> T fails DbError`, spec :556 and :595).

## 2. Retrospective: promises kept and broken

Most core promises held. Four bent: one form per construct, the spec freeze, consistent word order and the diagnostic numbering. Speed is unproven, not broken.

| Promise | Status | Evidence |
| --- | --- | --- |
| Zero-allocation patterns | Kept | `rest` on the caller frame, `readInto`, non-allocating `log`, struct closures |
| No ambient culture, codecs for bytes | Kept | Revision 0003 |
| Plain-English, fixable diagnostics | Kept | Every input form has a fix-it (5.2) |
| Rethink when evidence says so | Kept | Revision 0007 reversed the blanket field-write rule |
| One form, one meaning | Bent | Two per-project synonym pairs: `int`/`i32` and `!`/`not` |
| Spec freeze after round 3 | Bent | Revisions 0002 to 0007 in about a week; J54 reverted declaration order the same day |
| One consistent word order | Bent | Declarations type-first, locals and loop variables name-first |
| Codes grouped like HTTP status codes | Broken | J27 placed plan codes in free slots across categories |
| Laws in the language | Unfinished | L001 to L004 waiting since 001; `law` has no syntax |
| Rust/Zig-level speed | Unproven | No benchmark yet, and several checks run in every build mode |
| Small, guarded language | Under pressure | Round 3 alone added 78 diagnostics and over 90 decisions |

### One form, one meaning

Per-project style makes two dialects of the same language. A reader moving between projects sees different code for the same program:

```cs
// project style .csharp                 // project style .bayan
let n: int = count                       let n: i32 = count
if !done && n > 0 { … }                  if not done and n > 0 { … }
```

This is the dialect problem we cite against macros, in a smaller form. Proposal: keep the setting, but make the language server show either style (a display option or inlay hint, not a `bayan format` flag), and make docs and std use one.

### Word order

Round 3 chose head-first word order. The final result mixes two orders within one function:

```cs
public Grams shippingWeight(Order order) {      // type first
    let total: Grams = 0g                        // name first
    for line: Line in order.lines {              // name first
        total = total + line.weight
    }
    let heavy = order.lines.where((Line l) => l.weight > 1kg)   // type first
    return total
}
```

Each rule has a reason: declarations are read by type, locals by name. But the guide should state one sentence that explains the split, since newcomers will ask.

### Diagnostic numbering

Codes such as BYN1046 `PatternOutsideConst` and BYN3212 `ExceptOnNonSequence` sit where a slot was free, not where their category is. Renumber before 1.0 (after it, codes are public API): one hundred per category, with a mapping table and old codes kept as aliases for one edition.

### Spec churn

Six revisions in a week after a freeze means the freeze came too early. Every syntax change now costs a migration of the self-hosted lexer and parser as well. Rule for round 4: no syntax change lands between 012 and the round-4 plan, and round 4 batches all syntax changes into one migration.

## 3. Decisions to reevaluate

Each of these was accepted "for now" or carries a known risk. The self-hosted compiler is the first large Bayān codebase, so it supplies the evidence.

| Decision | Risk | Evidence to collect | When |
| --- | --- | --- | --- |
| Region-bound references in structs (over Hylo-style scoped views) | Checker complexity; soundness bugs | Bugs found by the region compile-fail corpus during 010 T25/T37 | After 010 |
| Particles and naming rules on public API | Friction for contributors | Count of `@allow`, rename attributes and BYN50xx fixes in the self-hosted source | After 012 |
| Line-joining starters (`expects`, `&&`, `catch`, …) | A line silently joins the one above | Incidents found while writing 009 and 010 | After 012 |
| Type-based overloads (wanted, reserved) | With target-typed literals and leading-dot names, inference can blow up, as in Swift | A type-check time budget per function, measured on goldens | Before enabling |
| `$n` SQL placeholders | Argument position drift in long queries | Sūq queries with five or more arguments | With `sql` interpolation |
| Two-layer trust model | Too much ceremony or too little | Sūq handlers: how often `trustUnchecked() because` appears | After Sūq runs |
| Typestate scope | Used for plain data, adding ceremony | Count of `states` blocks that are not lifecycles | After Sūq runs |
| `f64` text form: `1.0` prints as `1` (and `1e21`, where Python writes `1e+21`) | A float written to JSON or a report reads as an integer; readers that care about the type see it change | Spec 015 S8: quality reports compare floats as numbers after the port | Round 4 |

Interop is already decided and is not reopened here: C is the only live interop (`std.c`), and C# reaches Bayān two ways: the zero-allocation subset compiles as Bayān (spec §25), and the rest goes through `bayan migrate cs`, a one-time translator, post-v1 (bayan-library-tiers.md §10).

## 4. Performance and efficiency

The biggest gap is measurement: Bayān claims Rust/Zig speed with no benchmark. Start there, then remove the cost of always-on checks where the compiler can prove them unnecessary. None of these changes semantics.

### P1. A benchmark suite that shows losses

Write the same small programs in Bayān, C, Rust and Zig (parsing, hashing, a Sūq request path, the self-hosted lexer) and publish every result, including the ones Bayān loses. Run it in CI so regressions show up per commit.

### P2. Remove checks the compiler can prove

Overflow, bounds and `expects` checks run in every build mode. Keep that rule, but skip each check where flow facts already prove it holds:

```cs
i64 total(ReadOnlySpan<i64> xs) {
    var sum: i64 = 0
    for i in xs.indices {
        sum = sum + xs[i]      // bounds check removed: i comes from xs.indices
    }                          // overflow check stays: not provable
    return sum
}

Money split(Money bill, by i32 parts) expects parts > 0 { … }

let n = max(1, guests)
let share = split(bill, by: n)  // n > 0 is known here, so this call skips the entry check
```

Lowering: each function with `expects` gets two C entry points, `split` (checks, then calls the body) and `split__body`. A call site that proves the condition calls `split__body` directly. Pair it with the bounds report in section 7.

### P3. One C body for identical monomorphizations

Units and distinct types over the same primitive produce identical C. Emit one body per layout and operation set, and alias the rest:

```c
// today: three identical functions
i64 max__Millis(Span_i64 xs);  i64 max__Grams(Span_i64 xs);  i64 max__i64(Span_i64 xs);

// proposed: one body, the others are aliases
i64 max__i64(Span_i64 xs) { … }
#define max__Millis max__i64
#define max__Grams  max__i64
```

This cuts binary size and compile time where units are used heavily, as in Sūq.

### P4. Optionals that cost no extra space

When a type has a value that can never occur, `T?` uses it as "null" instead of adding a tag:

| Type | `T?` today (tag + value) | With the null niche |
| --- | --- | --- |
| `string`, `Span<T>` (pointer + length) | 24 bytes (16 + tag, padded) | 16 bytes: null pointer means none |
| `Box<T>` / pointer | 16 bytes | 8 bytes |
| `i64` | 16 bytes | 16 bytes (no spare value) |

The same applies to `Result` when the error set fits a value's spare bits. The byte figures assume a 64-bit target and the layouts named; confirm against the real layouts.

### P5. Automatic field order

Reorder fields by alignment by default, with `@layout(.declared)` for C interop and `@packed` for wire formats:

```cs
struct Line {
    bool gift          // as declared: 1 + 7 padding
    i64 quantity       // 8
    bool fragile       // 1 + 7 padding  → 24 bytes
}                      // reordered: 8 + 1 + 1 + 6 → 16 bytes
```

### P6. No state checks where the state is known

A value typed `Order.paid`, or a local whose state flow tracking already knows, needs no runtime tag check when a method reads its payload. The tag stays in memory so mixed-state collections keep working; only the checks go.

### P7. A compile-time budget

Set a target for building the self-hosted compiler from clean and from one changed file, and track both in CI. Monomorphization, effect inference and region inference all grow with program size, and Rust and Swift show how compile time becomes the main complaint.

## 5. Other ecosystems

The most relevant warnings are Java's checked exceptions, Swift's type checker and D's optional GC. Each matches a choice Bayān faces now.

### Mistakes to avoid

| Ecosystem | What went wrong | What it means for Bayān |
| --- | --- | --- |
| Java | Checked exceptions failed partly because lambdas could not be generic over them, so `stream().map(x -> throwing(x))` does not compile | Closures need `fails` polymorphism, like `uses` already has. Required before the closure escape rule ships. |
| Swift | Overloads plus literals plus implicit member names made type checking exponential ("expression too complex") | Keep type overloads reserved until a per-function type-check budget is measured |
| D | An optional GC split libraries into GC and `@nogc` worlds | No optional GC (section 8) |
| Rust | Async split the ecosystem; monomorphization made compiles slow | No async coloring; P3 and P7 |
| Scala 3 | A late syntax change split code style across the community | Batch all syntax changes into round 4, then stop |
| C# | Nullable references arrived as warnings after 20 years, so the guarantee is weak | `T?` is an error-checked type from day one; never add a suppression operator like `!` |
| Crystal | Whole-program inference hurt compile time and incremental builds | Keep effect and region inference inside a module; public signatures declare |
| V | Claims published before the implementation hurt trust | Publish benchmark losses (P1) |
| Zig | Frequent breaking changes before 1.0 | Editions plus fix-its; document a stability promise per edition |
| Kotlin | Inline lambdas allow `return` from the outer function, which surprises readers | `return` in a lambda always means the lambda |

### Ideas to take

| From | Idea | Fit |
| --- | --- | --- |
| Zig | Ship libc headers and sources, build them lazily per target, cache them | The model for toolchain independence (section 6) |
| Hare | A small backend (QBE) instead of LLVM | A possible long-term native backend |
| Go, Cargo | One tool for build, test, format and packages; a lockfile from day one | `bayan` already is the one tool; decide the lockfile and registry before 1.0 |
| Swift | Closure parameters are non-escaping by default | The closure escape rule (section 7) |
| Elm, Rust | Errors that name the cause and show the fix | Already the direction; keep the bar as the language grows |
| Odin | `#soa` layout for hot data | `@soa`, later (section 7) |

## 6. Toolchain independence

Zig is needed only until phase 3. From phase 2 any C compiler builds Bayān; from phase 3 Bayān ships the libc pieces Zig provides today.

| Phase | When | What it delivers | Gate to enter the next phase |
| --- | --- | --- | --- |
| 1. `zig cc` default | Now | Driver interface (K3), C inventory (K2), libc behind `rt` (K4) | 012 passes |
| 2. Any C compiler | After 012 | `cc bootstrap/bayan.c` (K5); clang, gcc and TinyCC backends; TinyCC for `bayan run` | Cross-builds work |
| 3. Own sysroots | Next | libc headers shipped; musl and mingw built lazily per target and cached; **Zig not required** | Evidence of need |
| 4. Own backend | Later, optional | Fast debug builds from a small QBE-style backend; LLVM/clang kept for release | — |

What `zig cc` gives today is clang, the lld linker, and libc headers and sources for many targets, built on first use and cached. Phase 3 reproduces that last part, which is the real work. Bayān would still use clang and lld at that point, but as ordinary tools with no Zig in between.

Phase 4 is the only step that removes LLVM, and only for debug builds. Optimized release builds stay on clang, since matching its optimizer is not a realistic goal.

Two risks to check before phase 3: licensing for redistributing macOS SDK headers, and the size of shipped libc sources per target. Windows (mingw-w64) and Linux (musl) sources are permissively licensed.

## 7. Candidate features by timeline

Round 4 holds every syntax change in one batch, landed after 012 with one migration of goldens and self-hosted source. Library and tooling work follows in v0.2, and anything costly waits for evidence.

### Round 4: syntax and checks, one batch

| Feature | Pros | Cons |
| --- | --- | --- |
| Replace `async`/`await` and `Io.group` with `scope`/`spawn` (spec §13, Appendix A) | One structured form; no function coloring; reopens Appendix A by user decision 2026-11-04 | Spec §13 and Appendix A rewritten by revision |
| Remove `provide timeout: d { … }` (spec §13 :1556) | The block deadline is `scope(timeout:)` / `until:`; also clashes with BYN6007 | Spec §13 edit |
| Structured concurrency with shared-nothing workers | Borrow safety across fibers by construction | Spec in round 4, runtime v0.2 (bayan-concurrency.md) |
| Subjectless `switch` | Replaces long `else if` chains; same C as the chain | One new parse form |
| `when` guards on pattern arms | Mixes patterns and conditions | Guarded arms do not count for exhaustiveness |
| `final` states (BYN7013) | Catches a forgotten commit or close at compile time | Path analysis over `fail`, `try`, `defer` |
| Closure escape rule + `fails` polymorphism | Zero-allocation closures by default; lambdas can call failing functions | Real checker work; needs compile-fail tests |
| `dbg(x)` | Daily debugging aid | Must be an error in release builds |
| `!(x is .s)` input form (BYN1070) | One spelling of negated `is` | One more code |
| Payload size-ratio lint | Keeps `Result` small | Heuristic threshold |
| Diagnostic renumbering | Codes grouped by category, as intended | One-time alias table |
| Consuming `finish()` (`move this`) | Hands the heap block to the caller, so a function can return a built string without a copy (revision 0018 made `finish()` a loan) | A second `finish` form, or a `move` overload rule; the buffer is unusable after it |
| Store and return summaries across packages (Q745) | A caller's acceptance depends on callee bodies (the Q745 per-callee store summaries, and the return summaries that replace rule 4 for calls with a body), so a public function's body change can break another package; decide: public functions declare their store and return summaries in the signature, or `bayan api diff` reports a summary change as breaking | Signature syntax, or a tooling rule that only catches the break at diff time |
| Clause order `where → expects → uses → becomes → fails` | Clauses read in the order they apply: compile time, entry, running, success, failure; matches Swift's `async throws` | Every signature with both `uses` and `fails` changes; automatic through the BYN1065 fix-it and `bayan format` |

```cs
// subjectless switch
let tier = switch {
    score >= 90 => .gold
    score >= 70 => .silver
    else        => .bronze
}

// final states
void save(Connection.open conn) uses net fails DbError {
    var tx = try conn.begin()
    defer failing tx.rollback()
    try tx.execute(sql"INSERT …")
    try tx.commit()
}   // without the commit: BYN7013, `tx` is still .active

// closures: borrowed by default, `fails` passes through
void retain<T>(ref List<T> xs, where (T -> bool fails E) keep) fails E { … }

try lines.retain(where: l => try isValid(l))   // no allocation; E inferred as ParseError
```

### v0.2: library and tooling

| Feature | Pros | Cons |
| --- | --- | --- |
| TinyCC in-process for `bayan run` | Instant scripts with no toolchain installed | Second C backend; weak on some targets |
| Verified decoding (`@derive(Decode)`) | No half-valid values from bytes | Derive machinery |
| `@packed` with endianness | Ends hand-written shifts in font and wire code | Emitter does shifts and masks |
| Bounds-check report | Shows what P2 could not remove | Depends on the C optimizer |
| `InlineList<T, N>`, `Pair<T>`, `sum(as: .saturating)` | Library only | Small wins each |
| Naming rules BYN5100/5101 | Consistent public APIs | More policing; wait for the friction count in section 3 |
| `then` stages | Reads top to bottom | A paraphrase of nested calls; only if Sūq shows long chains |
| Finish `law` blocks | Completes started work | Needs a property-test runner |

```cs
@packed(.big)
struct TableRecord {
    u32 tag
    u32 checksum
    u32 offset
    u32 length
}

let record: TableRecord = try TableRecord.decode(bytes)   // bytes: Untrusted<ReadOnlySpan<u8>>
```

### v0.3 and later: on evidence only

| Feature | Pros | Cons |
| --- | --- | --- |
| Compile-time evaluation of functions with empty `uses` | Lookup tables at build time; purity check is free | An interpreter inside the compiler |
| `@soa` layout | Large gains in hot loops | `ref xs[i]` becomes awkward |

## 8. Classes and GC

Verdict: no `class` keyword and no garbage collector in the language. Shared heap objects come from library types. Interop is C only, so there is no GC edge to accommodate.

### What a class would have to be

Without a GC, a class can only be a reference-counted heap object, as in Swift:

```cs
// hypothetical, not proposed
class Node {
    string name
    List<Node> children
    weak Node? parent
}

let root = Node { name: "root", children: [] }   // heap allocation, count 1
let alias = root                                 // count 2, same object
alias.name = "renamed"                           // root changes too
```

The last line is the problem. Bayān's safety rests on one owner and `ref` borrows that the compiler can see. A class creates shared mutable aliases the checker cannot track, so it needs either runtime exclusivity checks (Swift) or immutable classes. Every copy also costs a count update, atomic across threads.

### What we use instead

```cs
// shared ownership, explicit and visible: Rc<T> / Arc<T> in std
let config: Arc<Config> = Arc(try loadConfig(from: path))

// graphs and trees: a pool with handles, no counts, no cycles to leak
var tree: Pool<Node> = Pool()
let root: Handle<Node> = tree.add(Node { name: "root" })
let child: Handle<Node> = tree.add(Node { name: "a", parent: root })
tree[root].children.append(child)
```

For foreign objects only (C libraries with object-like APIs), an `extern class` declaration could wrap a handle owned by the other runtime. That is an interop feature, not a Bayān type.

### GC options

| Option | Who did it | Result | For Bayān |
| --- | --- | --- | --- |
| No GC; owners, arenas, pools | Rust, Zig, Odin | Predictable, fast; more design up front | Current model |
| Optional tracing GC | D | Libraries split into GC and no-GC | No |
| Reference counting everywhere | Swift | Predictable pauses; constant count traffic | Library `Rc`/`Arc` only |
| GC only across interop | Kotlin/Native, C# with native code | Contained cost | Not needed: interop is C only |
| Arena per run or request | Zig, Odin, game engines | GC-like convenience with no collector | Default for `bayan run` scripts and request handlers |

The last row gives scripts most of what people want from a GC. If the context allocator for `bayan run` is a single arena freed at exit, a script never frees anything by hand and never pauses for a collector.

## 9. Not adopted, and open questions

| Not adopted | Why |
| --- | --- |
| `otherwise` / `elif` | Renames the keyword; subjectless `switch` fixes the shape |
| `under` (one-statement `provide`) | Paraphrase of `provide` |
| `ensures` | A `requires` on the return type says it; would trap on hot paths |
| `never` as negation | `never` is the bottom type |
| `return … from f` | Cuts across `fail`/`catch` |
| Closure-based row API (`rows.each { }`) | Synonym of `for try`; loses `break`, `continue` and outer `return` |
| `class`, optional GC | Section 8 |
| Goose's heap-less model | Conflicts with `alloc`, arenas and collections |
| Macros, resumable effects, types-as-values comptime | Dialects, stack switching, slow compiles |
| Inheritance, exceptions, implicit conversions, runtime reflection, `async`/`await` (replaced by `scope`/`spawn`, bayan-concurrency.md), explicit lifetimes, dependent types | Each contradicts a recorded decision |

### Open questions

- [ ] How does 007 lower a function-typed parameter today: `{fn, env*}`, monomorphized, or boxed?
- [ ] Order of `fails` and `uses` in function types, e.g. `(T -> bool fails E uses net)`? (`() -> T fails DbError` is already specified, spec :551.)
- [ ] Renumber diagnostics before 1.0, with one edition of aliases?
- [ ] Synonym styles: keep per-project, or one canonical style with a display option?
- [ ] Lockfile and package registry: when?
- [ ] Single-arena default for `bayan run`?
- [ ] TinyCC coverage on macOS ARM64 and Windows ARM64, or system `cc` there?
