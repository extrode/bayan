# HANDOFF — Bayan, design round 2

## Context

Bayan is a C#-derived, natively compiled language (no .NET runtime). `SPEC.md` exists and a v0.1 bootstrap compiler is in progress. This handoff records every decision from the second design review. **It replaces the round-1 HANDOFF** (unions, generics, async, generators, formatter, laws, GC). Where round 1 was already applied, reconcile with this document; this document wins on any conflict.

Work in this order for each area: update `SPEC.md`, implement in the compiler where the area is in v0.1 scope, add tests, add diagnostics with `BYN####` codes and `bayan explain` entries. All syntax below follows the decisions in this file; where it conflicts with existing `SPEC.md` text, update the spec.

Write new example files with the `.byn` extension. Code fences in docs use `csharp` highlighting until Bayan has its own grammar.

## Settled — do not re-propose

- One form, one meaning.
- No `!` shorthand for `fails` in source (an editor input form only, see §15).
- No symbols for `noalloc` / `nosuspend`.
- No `?` suffix on boolean members; `is`/`has`/`can` prefixes. `?` is reserved for `T?`, `?.`, `??`, ternary.
- Struct-only, no GC in v1. Fibers-only async in v1, colorless via `context.io`.
- Naming: PascalCase types, camelCase everything else, `I` prefix on interfaces.
- `package.byn` is a data-only Bayan struct literal. Not TOML.
- Laws are opt-in, never imposed.

---

## 1. Unions

- `union` and `error union` share one grammar. `error` only allows use in `fails` clauses and `catch switch`.
- Case names are camelCase.
- Payload-free unions are the enum form. Cases may carry data: `approved(string transactionId)`.
- Unions may declare members after a `;`: `union OrderStatus { pending, paid; bool isFinal => this is .paid; }`.
- Unions are closed: no integer casts. Outside data enters only through `tryParse` (fails on unknown input). `.name` gives the case name.
- `switch` on a union is exhaustive; a missing case is an error naming the case. A `_` arm on a union declared in the same package is a lint warning.
- Layout: tag + largest payload, inline, no heap.
- `Result<T, E>` and `never` are stdlib unions (`never` is the empty union). `fails` desugars onto `Result`.
- Payload-free cases may declare explicit values for C interop: `union Mode { read = 1, write = 2 }`.

**Flags:** there is no flags construct. `Set<T>` of a payload-free union with ≤ 64 cases is **guaranteed** to be a single bitmask. `Set` supports `|`, `&`, `-`, `contains`, `containsAll`, `.bits` (u64) and `tryFromBits` (fails on bits matching no case).

```csharp
union Permission { read, write, delete, admin }
Set<Permission> editor = [.read, .write];
return .forbidden unless user.grants.containsAll(editor);
```

## 2. Operators and equality

- Overloadable: `+ - * / %`, unary `-`, `[]`, and `compare`. Operators are `static`, cannot `fails`, cannot take `ref this`.
- Derived: `+=` from `+`; `== !=` field by field; `< <= > >=` from one `static Ordering compare(T a, T b)`.
- Not overloadable: `&& || ?? is = ?.`, and `+% +|` (built-in integers only in v1).
- No implicit conversions. Use `from` / `into`.
- `==` is derived for every struct whose fields all support `==`. Opt out with `[NoEquality]`. Distinct types compare only with the same distinct type. `Secret<T>` has no `==`; it has `equalsConstantTime(other)`.
- Lint: `==` against a partial struct literal ("missing fields default to …; did you mean `is`?").
- **No `+` on strings** (see §13).

## 3. Arithmetic

- Integer overflow **traps in every build mode**, including `fast`. (Replaces "wraps in fast".)
- `+% -% *%` wrap; `+| -| *|` saturate; compound forms `+%=` etc.
- `checked(expr)` evaluates a whole expression and returns `T?` (null on overflow). Docs must note this differs from C#, where `checked` throws.
- The compiler removes checks it can prove unnecessary (loop counters, values of distinct types with `requires` ranges).

## 4. Control flow syntax

**No parentheses around conditions; braces mandatory.** Parentheses only group. If a condition contains a brace at its top level (struct literal, property pattern), the formatter wraps the **whole condition** in parentheses; otherwise it removes them.

```csharp
if n > 0 { … } else if n < 0 { … } else { … }
if (p is { x: 0, y: 0 }) { … }
for x in xs { … }
for i in 0..n { … }
for (key, value) in prices { … }
outer: for row in grid { break outer if row.isDone; }
while queue.hasItems { … }
do { … } while try socket.hasMore();
switch day { case .sat or .sun: rest(); default: work(); }
```

- **Postfix modifiers** are the one-line form: `flush() if unlikely buf.isFull;`, `return .badRequest("Cart is empty") unless cart.hasItems;`.
- **`unless`**: no `else`; no `unless !x`; a top-level `&&`/`||` in its condition is an error with a fix-it to `if`. `bayan fmt` rewrites `if !x { … }` (no else, simple condition) to `unless x`. `unless x is null` is not allowed (`if x is not null` instead). `unless` takes no likelihood word.
- **`if unlikely`**: layout hint only (cold block); semantics unchanged. Lint when used on error returns (already cold).
- **`if never`**: panics if true **in every build mode** (replaces "safe builds only"), cold layout.
- **Null checks**: canonical `is null` / `is not null`. After `is not null` on a `let` binding or parameter, the value is narrowed to the non-null type. `x ?? return …` / `?? break` / `?? continue`.
- **Binding a call's non-null result**: `let order = db.find(id) ?? return .notFound;` (not the C# `is { } order` idiom).
- **Shadowing**: allowed with `let`, in the same scope, only when the type changes. Same-type shadowing is a lint warning.

**Fallible iteration:**
- `for try x in seq` — every step may fail; failures propagate. Iterating a fallible sequence without `for try` is a compile error with a fix-it.
- Queries and readers are **lazy**: opening happens on the first step, so one `try` covers both.
- If creating the sequence can fail eagerly, both are allowed: `for try line in try File.open(p).lines()`.
- The loop owns the sequence and drops it on every exit.
- `while try cond` — the condition may fail on each evaluation.
- `while let x = expr` — loops while `expr` (a `T?`) is non-null, binding `x`. Works with `try`: `while let row = try rows.next()`.

## 5. Errors

- Commas everywhere: `fails A, B`; `error set S = A, B, C;`.
- `catch switch` binds per arm; no `catch (e)`:
  ```csharp
  let resp = signUp(form) catch switch {
      EmailError      => .badRequest("Invalid email"),
      DbError.timeout => .unavailable,
      DbError e       => .serverError(e),
  };
  ```

## 6. Cleanup and panics

**The `defer` family** (head verb + ḥāl participle; `failing`/`panicking` are contextual keywords valid only after `defer`):

```csharp
defer arena.reset();                 // every exit
defer failing tx.rollback();         // exit by failure (replaces `errdefer`)
defer panicking (p) log.error(p);    // panic in progress
```

- `defer failing` is allowed only in functions that `fails`; it also runs on fiber cancellation. `defer failing (e) { … }` binds the error.
- `defer panicking` is allowed anywhere. On a panic, **only** `defer panicking` handlers run (innermost first); plain `defer` and `defer failing` do not, because a panic aborts without unwinding.
- Panic handler bodies are restricted: implicitly `noalloc` and `nosuspend`; no `try`; read only `let` bindings and parameters; write only to panic-safe sinks (stderr, panic-safe log sinks). `return`, `break` and assignment are errors ("panic handlers can't recover").
- Implementation: a per-fiber linked list of handler records, pushed/popped only in scopes that declare one.
- A panic inside a handler aborts immediately and prints both messages.

**Process-wide hook:**
```csharp
void main() {
    Panic.onPanic = p => crashReport.write(p);
    run();
}
```
- Allowed only as the **first statement of the entry method**; assigned at most once; libraries cannot set it. Anywhere else is a compile error.
- Same body restrictions as `defer panicking`.
- Order on panic: the panicking fiber's `defer panicking` handlers → `Panic.onPanic` → default trace → abort. The test runner is its own entry point with its own handler (fails the test).
- Fiber isolation (killing one fiber instead of the process) is **not** in v1.

## 7. Validation: `requires`

`requires` is a programmer contract. Violation is a bug: **panic in every build mode**, never `fails`.

```csharp
public Money discount(Money total, i32 percent)
    requires percent in 0..=100
    => …;

distinct type Quantity = i32
    requires value in 1..=100;

struct OrderForm {
    string? coupon requires coupon.chars.count == 8;   // checked only when non-null
}
```

- On functions: checked on entry. A constant argument that violates it is a **compile error**. If the caller has proven it (e.g. the argument's type carries the rule), the check is removed. Shown in docs and prose translation.
- On fields: checked at construction and on every write.
- On distinct types: checked on every construction path. `T.tryFrom(raw)` fails with `RangeError`; `T(constant)` is checked at compile time; `T(runtimeValue)` panics on violation. The value is immutable afterwards.
- **`requires` replaces `[Check]`** everywhere (including the `Validate` derivation and boundary-test generation).
- A distinct type with `requires` has **no default value** unless the default satisfies the rule.
- `x in a..=b` is a boolean range test.
- `Untrusted<T>.validated()` runs every field's construction rules and collects **all** failures into `ValidationError.fieldErrors`, producing `T.Validated`.

Use the right tool: non-null types (no check), `requires` (bugs), distinct types (rules true everywhere), `Untrusted<T>.validated()` (user input, all errors), `return .x unless …` (business outcomes).

## 8. Lambdas and function types

- Forms: `x => x * 2`, `(a, b) => a + b`, block bodies, and **`it`** for one-parameter lambdas: `users.where(it.isActive)`, `lines.sumBy(it.price * it.qty)`, `select(toDto(it))`. Nested `it` is an error; name one lambda.
- `.member` has one meaning: a member of the **expected type** (`return .notFound`, `color: .red`). It is never lambda shorthand.
- One function-type syntax replaces `Func`/`Action`/`delegate`: `i32 => i32`, `(i32, i32) => i32`, `User => void`, `() => void`, `() => T fails DbError`; aliases via `type Predicate<T> = T => bool;`.
- Lambdas passed as parameters are monomorphized and inlined; captures borrow; zero allocation.
- Stored lambdas must be non-capturing (function pointer) or explicit `dyn (A => B)` allocated from `context.allocator`.
- `move () => …` moves captures in. Capturing a `var` follows borrow rules.
- No multicast delegates and no `event`.

## 9. Mutating methods: `ref this`

Keep the spec's Form V. **`mut` is not a keyword.**

```csharp
public void add(ref this, CartLine item) fails CartError { … }
public bool isEmpty => lines.isEmpty;          // read-only: the default
```

- Read-only is the default for every member.
- Calling a `ref this` member on a `let` is an error with a fix-it to `var`.
- Passing for mutation uses `ref` at the call site: `fill(ref cart)`.
- Mutating a value while it is borrowed (e.g. inside `for line in cart.lines`) is an error.
- Map/list element access on a `var` collection yields the stored value in place (no silent copies): `stats.getOrAdd("api").record(12ms)` changes the stored value.
- Interfaces declare `ref this` members the same way; implementations must match.

## 10. Struct literals and initialization

- Field initialization uses `:` everywhere: `Money { cents: 250 }`, nested `unitPrice: { cents: 250 }` (type from the field), `with { lastName: "Doe" }`, named arguments `take: 10`.
- **This includes `package.byn`** — update SPEC §19.2 and the `bayan new` templates from `name = "…"` to `name: "…"` (see §18).
- `{ cents = 250 }` is an input form (§15).
- No VB-style `with` block and no standalone `.field = value` statements.

## 11. Collections

**Naming follows the grammar (§17):** a verb on `ref this` mutates; the same verb without `ref this` returns a new value; the `-ed` participle exists only as the copy form of a mutating verb.

| Mutating (`ref this`) | Result form (`-ed`, derived: a view if possible, otherwise a copy) |
|---|---|
| `append(x)`, `prepend(x)`, `insert(x, at:)`, `appendAll(xs)` | `appended`, `prepended` |
| `sort()`, `sort(by)`, `reverse()` | `sorted`, `reversed` |
| `removeAt(i)`, `removeWhere(f)`, `removeFirst()` → `T?`, `removeLast()` → `T?`, `replaceRange(r, with:)`, `fill(x)`, `clear()` | |

- Ordered collections (`List`, `Deque`) use `append`/`prepend`; unordered (`Set`, `Map`) use `add`.
- Docs state cost per method: `prepend`/`insert`/`removeFirst` are O(n) on `List`, O(1) on `Deque`.
- Queries: `count`, `isEmpty`, `hasItems`, `first`/`last` (`T?`), `xs[i]` (panics), `at(i)` (`T?`), slices `xs[1..4]`, `take`, `skip`, `contains`, `indexOf` (`i32?`), `indexWhere`.
- Lazy struct iterators with inlined lambdas: `where`, `select`, `sumBy`, `minBy`, `maxBy`, `any`, `all`, `groupBy`; nothing allocates until `toList()`.
- `join(sep)` and `join(sep, f)` on collections; `flatten()` (Form I, not participles).

**Literals:**
```csharp
List<i32> xs = [1, 2, 3];
Set<Permission> p = [.read, .write];
Span<i32> s = [1, 2, 3];                         // stack, no allocation
let all = [..xs, ..more, 4];                     // spread
Map<string, i32> stock = ["A-1": 12, "B-2": 0];  // map literal
Map<Sku, Money> prices = [];
let fixed = [1, 2, 3];                           // no target type → i32[3] on the stack
let headers = ["Accept": "*/*"];                // no target type → Map<string, string>
```

## 12. Strings

- Immutable UTF-8. **No `length`**: `s.bytes.count`, `s.chars.count`, `s.graphemes.count`. No single-index indexer (`s[i]` is an error); range slicing `s[a..b]` uses byte offsets, as ranges do everywhere (SPEC §6.1), and panics off a character boundary; `s.chars.take(n)`, `s.graphemes.take(n)`, `skip`, `takeLast`.
- Searching returns views/offsets: `contains`, `startsWith`, `endsWith`, `indexOf` (`i32?`), `lastIndexOf`.
- Splitting is lazy and returns views: `split(',')`, `split(", ")`, `split(predicate)`, `split(",", max: 2)`, `splitOnce('=')` → `(string, string)?`, `lines()`.
- New strings (Form I, allocate from `context.allocator`): `trim()`, `trimStart()`, `trimEnd(c)`, `lowercase()`, `uppercase()`, `replace(a, b)`, `padStart(n, c)`, `repeat(n)`.
- Comparing: `==` is ordinal; `a.equals(b, .ignoreCase)`; culture-aware only via `Collator`.
- `string.fromUtf8(bytes)` fails on invalid UTF-8; `toUtf16()` for interop.
- Raw strings `"""…"""`.

**No `+` on strings.** Concatenation uses interpolation (`$"{a}{b}"`), `[a, b, c].join("")`, `Path.join`, or `StrBuf` in loops. `a + b` on strings is an input form rewritten to interpolation (§15).

**`StrBuf`** (the builder; the spec's `xInto(ref StrBuf)` target):
- `StrBuf(capacity: n)` from `context.allocator`.
- `StrBuf.over(stackalloc u8[n] uninit)`: stack first, spills to the allocator only if outgrown.
- `append(x)`, `append($"…")` (interpolation writes directly into the buffer), `appendRepeated(c, n)`.
- `buf.view` — borrowed string view, no allocation.
- `buf.finish()` — consumes the buffer and returns it as a string **without copying**.

## 13. Formatting: `format()` replaces `toString()`

- There is **no `toString()`**. Display uses the spec's pattern: `format()` and `formatInto(ref StrBuf)`; the allocating form is derived from the `Into` form. Interpolation calls `formatInto` directly.
- C#-compatible specifiers: `D8`, `N0`, `N2`, `F2`, `X`, `X8`, `B`, `E3`, `P1`, alignment `{x,8}` / `{x,-12}`; `locale:` parameter for culture-aware output.
- Distinct types format as their underlying value; payload-free union cases as their name; error unions as their doc comment; `Secret<T>` as `***`.
- **Structs have no default `format()`.** Interpolating a struct without one is a compile error suggesting `format()` or `{x:debug}`.
- **Debug** (the automatic `debug` derivation): `dbg(x)` and `$"{x:debug}"` print the struct-literal form, which is valid Bayan and round-trips (snapshot tests can read it back). `Secret<T>` still prints `***`. `dbg` remains an error in release builds (SPEC §21.2).
- Calling `toString()` / `ToString()` gives a teaching diagnostic pointing to `format()`.

## 14. Async and `context`

- `async f(args)` is a call-site expression that schedules `f` on `context.io` and returns `Future<T>`; `await` joins; `try await` propagates errors. Functions are never colored.
- Under `Io.Blocking`, `async` runs eagerly (same code, sequential).
- `nosuspend` on a function is a compiler-checked guarantee; a violation names the suspending call chain.
- `async` is never a function modifier. If v0.1 accepts it as one, remove it and emit a teaching error showing the call-site form.
- Futures are scoped: a future cannot escape its block; leaving the block cancels unfinished futures, which runs their `defer failing` handlers.
- Implementation: `async f(x)` hands `fOp(x)` (the maṣdar) to `context.io`.

**`context (timeout: d) { … }`:**
- Sets a deadline for every suspending operation inside the block, including in called functions. No parameter plumbing.
- A nested `context (timeout: …)` can only shorten the remaining time (effective: min of remaining and new).
- Expiry fails the suspended operation with `TimeoutError`.
- CPU-bound loops check explicitly: `try context.checkDeadline()`.

## 15. Input forms (replaces terse/explicit formatter modes)

Input forms are alternate spellings the editor and `bayan fmt` accept and rewrite, one way, into canonical syntax. The compiler rejects them in source with a machine-applicable fix. One table in the spec lists all of them:

| Input form | Canonical |
|---|---|
| `int long short byte uint ulong float double` | `i32 i64 i16 u8 u32 u64 f32 f64` |
| `User!EmailError f()`, `User!(A, B) f()` | `User f() fails EmailError`, `fails A, B` |
| `for (let x in xs)`, `foreach (var x in xs)` | `for x in xs` |
| `if (x) {`, `while (x) {`, `switch (x) {` | no parentheses (whole-condition parentheses kept when a brace is present) |
| `x == null`, `x != null` | `x is null`, `x is not null` |
| `if !x { … }` (no else, simple condition) | `unless x { … }` |
| `{ cents = 250 }` | `{ cents: 250 }` |
| `"Hello " + name` (strings) | `$"Hello {name}"` |

**SPEC diff:**
- §21.2: `bayan fmt` loses `--explicit` and `--terse`; it formats to canonical form and rewrites input forms (`--check`, `--prose`, `--from-prose`, `--check-prose`).
- Remove the terse/explicit formatter-modes section; add the input-forms section above.
- Display-only views (inferred types, expanded derived members) are **language-server inlay hints**, never a stored form.
- Prose mode is unchanged.

## 16. Testing

Keep everything already in SPEC §20. Additions and clarifications:

- **`assert expr;`** is a statement (no parentheses). Failures print **power-assert** output: the value of every sub-expression.
  ```
  ✗ cart total after two adds                    cart.byn:31
    assert cart.subtotal == { cents: 750 }
           │    │        │
           │    │        false
           │    { cents: 500 }
           Cart { lines: [CartLine { sku: "A-1", qty: 2, … }], coupon: null }
  ```
- **No fluent assertion API** in the standard library; plain expressions plus power output cover it.
- **Parameterized tests** use the spec's `[Cases(...)]`, extended:
  ```csharp
  [Cases((0, false), (1, true), (100, true), (101, false))]
  test "quantity bounds" (i32 input, bool valid) { … }

  [Cases(discountCases)]                      // from a const list (xUnit MemberData)
  test "discount" (Money total, i32 percent, Money expected) { … }
  ```
  Each case is reported separately with its values: `✗ quantity bounds (input: 101, valid: false)`.
  Update the spec's §20 examples from `assert(...)` and expression-bodied `=> assert(...)` tests to block bodies with `assert expr;`.
- **`property` tests** (spec) generate inputs from the parameter types' `requires` rules and shrink failures into `testdata/`. Boundary tests come from `requires` (replacing `[Check]`).
- **Doctests**: fenced code in `///` comments is compiled and run by `bayan test`. `assert` lines are checked; a `// prints: …` comment is compared against the output.
- **Benchmarks**: `bench "name" { measure expr; }` reports time per op, allocations per op, and variance. Seeded randomness.
- Mutation-testing output names the mutant and suggests the missing case.

## 17. Grammar: GRAMMAR.md and the built-in checker

**Write `GRAMMAR.md`** — the developer-facing companion to SPEC §5, so other developers can name things the way the standard library does. Contents:

1. **Naming decision tree** for any new member:
   1. Changes `this`? → verb on `ref this` (`sort`); the `-ed` copy is derived (`sorted`).
   2. Returns a new value, changes nothing? → plain verb (`join`, `trim`, `split`).
   3. Boolean state, no arguments? → `is` / `has` / `can`.
   4. Can fail, and callers may want `bool`/`T?`? → write `x() fails`; `tryX` is derived.
   5. Writes into the caller's buffer? → `xInto(ref dst)`; the allocating `x()` is derived.
   6. The act as a value? → `xOp` (derived).
   7. A thing that performs the act? → noun (`Parser`, `Builder`, `Hasher`).
2. **Statement modifiers** follow the ḥāl rule: head verb, then a participle (`defer failing`). Future modifiers must follow it.
3. **One form, one meaning**, and the input-forms table.
4. **Conditionals**: `unless`, likelihood, `never`, postfix modifiers, with when-to-use guidance.
5. **Error-message voice** for diagnostics and error doc comments.
6. **The Arabic or Esperanto source of each rule.**
7. **Every rule links to the diagnostic that enforces it.**

**Built-in checker** (`BYN5xxx`):
- **Compiler errors** for pattern-contract violations (reserved names, SPEC §5.2): `-ed` without a mutating verb (`joined`), a state member with side effects, a hand-written `tryX` with the wrong return type.
- **Lint warnings** for word-class heuristics, using a word list (English first, Arabic templates later): a `ref this` member named with a noun, a boolean property without `is`/`has`/`can`. Lints never block builds.
- Every rule has a `bayan explain` entry, with text shared with GRAMMAR.md.

## 18. Style: STYLE.md and configuration

**Write `STYLE.md`** with the defaults below. Style is guidance: never compiler-enforced, configurable per project, applied by `bayan fmt`. Grammar rules (§17) are not style and cannot be configured away.

| Choice | Default |
|---|---|
| Braces | Same line |
| Indent | 4 spaces |
| Line length | 100 |
| Trailing commas | Always in multi-line literals and argument lists |
| Member order | Fields, state members, constructors, methods |
| Statements | One per line (postfix guards count as one) |

Configuration lives in `package.byn` as a Bayan struct literal:

```csharp
Package {
    name: "local/shop",
    version: "0.1.0",
    edition: 2027,
    kind: .app,
    capabilities: [.net],
    deps: [],
    style: Style {
        indent: 4,
        braces: .sameLine,        // or .nextLine
        maxLine: 100,
        trailingCommas: true,
    },
}
```

Update SPEC §19.2 and the `bayan new` templates to `:` syntax.

## 19. Derivations

- Use the spec's mechanism: `[Derive(Row)]`, `[Derive(Json)]`, `[Derive(Csv)]`, defined with `derivation Name for T { … }` (§12.5).
- Derivations only add members. `bayan expand T` shows the generated code, which must be identical to hand-written equivalents (e.g. ordinal reads for `Row`).
- Database row types: a `Row` derivation generates `readRow(ref Reader)`; a `CopyIn` derivation generates binary COPY writers. Add `CopyIn` to the standard derivations list in SPEC §5.3.

---

## Carried over from round 1

- **Ground rule**: performance first — no hidden allocations, no implicit boxing.
- **Unions** (round-1 item 1): folded into §1. Keep the `sizeof` tests confirming no boxing; niche optimization (e.g. `Result<Ref, E>`) is optional and later.
- **Generics** (round-1 item 2): no change beyond the spec (definition-site constraint checking and debug/release dispatch already specified). Dynamic dispatch is explicit only (`dyn`). Ordering uses `IOrder<T>`.
- **Async** (round-1 item 3): folded into §14, including scoped futures, `nosuspend`, and removing `async` as a function modifier.
- **Source generators** (round-1 item 4): **withdrawn.** The spec's derivations (§12.5) and tier-3 generators already cover this; follow the spec.
- **Formatter** (round-1 item 5): the terse/explicit canonical-mode question is replaced by input forms (§15). Keep the acceptance tests: `bayan fmt` is idempotent, and rewriting an input form then formatting again produces identical bytes.
- **Laws**: never feed the optimizer; the release binary must be byte-identical with laws on or off. When on, laws generate property tests, and a violated law fails `bayan test` with a minimal counterexample.
- **GC (future, spec only)**: an allocator plus `Gc<T>` handle, never a mode; not coupled to inheritance; a `gc` package capability; reserve `Gc` and `gc` now.

## Open — decide before running

- **`with { … }` vs the spec's derived `withF(v)`.** Both copy a value with a field changed: `u with { lastName: "Doe" }` and `u.withLastName("Doe")`. That is two forms for one meaning. Keep one (Sy to decide).

## Deferred (do not implement)

- `sql"…"` interpolation with taint tracking (to be designed later).
- Fiber isolation on panic.
- `if let`.

## Deliverables

1. `SPEC.md` updated for §1–19, each change with a one-line rationale and its diagnostic codes.
2. `GRAMMAR.md` (§17) and `STYLE.md` (§18).
3. Compiler changes, in this order:
   1. Parser: control flow without parentheses and the brace rule, the `defer` family, `for try` / `while try` / `while let`, `:` in literals and `package.byn`, `is not null`, `unless` rules, collection and map literals.
   2. Input-form rejection with fix-its.
   3. Semantics: `ref this` rules, `requires`, overflow traps in all modes, `if never` in all modes, union exhaustiveness, narrowing, shadowing, panic-handler restrictions and `Panic.onPanic` placement.
   4. Standard-library naming per §11–13.
   5. Testing additions per §16.
4. Tests for each item: positive, negative, and diagnostic snapshots.
5. An end-of-run summary: what changed, anything not done and why, and any spec conflicts that needed a judgment call.
