# HANDOFF — Bayan, design round 3, addendum 1 (2026-10-23)

Supplements `docs/bayan-design-round-3.md` and `docs/plans/2026-10-22-001-design-round-3.md`. This addendum wins on any conflict with them. Plan items for each section follow under "Plan".

Fixed in place in the two documents (inconsistencies, not new decisions): HANDOFF §24 and plan BYN3214 named `items.toList()`, which fails the grammar check; both now say `items.copy()` (§3 below).

---

## 1. Panic handler: `Panic.handler`

`Panic.onPanic` breaks the round-3 rule against the `on` prefix (HANDOFF §12) and repeats the type's name. It becomes a noun naming the role:

```csharp
type PanicHandler = Panic -> void uses console     // stored function type: an upper bound

public void main() {
    Panic.handler = p => Stderr.write(p)
    …
}
```

- The round-2 restrictions stay: assigned once, only as the first statement of the entry function.
- The handler's bound is `uses console`: it may write to stderr, and it may not allocate, suspend or do other I/O (round 2's "implicitly `noalloc` and `nosuspend`", expressed as effects).
- `defer panicking (p) { … }` stays the scoped form. `Panic.handler` is the process-wide hook.
- **GRAMMAR rule:** a stored function field is named with a noun for its role (`handler`, `pricer`, `formatter`), never `onX`. Events stay past participles or adjectives (`placed`, `low`).

## 2. O11 and O12 accepted

- **O11.** A public field is readable anywhere and writable only from the struct's own `ref this` methods or through `with`.
- **O12.** A particle appears at most once per signature. Group the values in a struct or choose another particle.

## 3. Views and copies: no `toX` or `asX` members

`asSpan()` and `toList()` both fail the grammar check: `as` and `to` are particles, and an instance member named `asX` or `toX` spells a conversion that already has a form.

| C# | Bayan | Why |
|---|---|---|
| `list.AsSpan()` (read-only use) | `xs.span` | A view is a noun; no arguments, so a property |
| `CollectionsMarshal.AsSpan(list)` | `xs.mutableSpan()` | Needs `ref this`, so only on a `var` |
| `span.ToList()`, `list.ToList()` | `xs.copy()` | Same verb as `s.copy()` (Sūq A5); returns an owned `List<T>`, `uses alloc` |
| `seq.ToList()` | `List(seq)` | A conversion is `T(x)` (Sūq A4) |
| `x.ToString()` | `string(x)` or `$"{x}"` | A conversion is `T(x)` |
| `reader.ToList()` | `rows.all()` | The cursor's own member: it consumes the cursor (`move this`) |

```csharp
public ReadOnlySpan<T> span                         // read-only receiver: allowed on a `let`
public Span<T> mutableSpan(ref this)                // mutating access: `var` only

let prices = try db.query<Money>(sql"SELECT price FROM items").all()
let total = subtotal(prices.span)                   // a ReadOnlySpan<Money> parameter: fine on a `let`
prices.mutableSpan().sort()                         // error: `prices` is a `let`

var scores = try db.query<i64>(sql"SELECT score FROM runs").all()
scores.mutableSpan().sort()                         // fine on a `var`
```

**On a `let`:** only `span` is allowed. A mutable view of a `let` would let code change a value its binding promised was immutable, which is exactly what C#'s `CollectionsMarshal.AsSpan` permits.

- Both views borrow `xs`. While a `mutableSpan()` view is live, no other access to `xs` is allowed (the existing borrow rules).
- **Nouns with no arguments are properties** applies to read-only receivers. A noun member that needs `ref this` is a call (`mutableSpan()`), because mutable access must be visible at the call site.
- **Static factories keep `from`:** `T.fromJson(…)`, `T.tryFrom(x)` and `fromBounds(start, until:)` are fine. A factory names the source a `T` is built from; the rule covers only instance members that convert `this`.

---

## Plan

### §1 Panic handler
- (a) `Panic.onPanic` → `Panic.handler`, type `PanicHandler = Panic -> void uses console`; round-2 placement rules unchanged; GRAMMAR: stored function fields are role nouns.
- (b) SPEC §7.6 (panic handlers; replaces the `noalloc`/`nosuspend` wording already reworded by C7), §2.4 none, §21.5 none. GRAMMAR §1 tree: the role-noun rule beside the event rule.
- (c) Reused: BYN2230 (renamed member) for `onPanic` → `handler`; BYN5010 `EventOnPrefix` now also covers stored function fields (message unchanged, help "name the field for its role, such as `handler`").
- (d) The `Panic` builtin (grep `onPanic` in `src/Bayan.Semantics` and `Builtins.cs`) and the first-statement check (grep the round-2 panic-hook code). Sūq `src/main.byn` uses `Panic.onPanic`: rename. v0.1: yes.

### §2 O11 and O12
- (a) As above.
- (b) SPEC §3.10 field visibility; §10 parameters. HANDOFF open-questions list: both resolved.
- (c) BYN3216 (plan) `FieldWriteOutside` "`{0}.{1}` can be written only by `{0}`'s own methods or with `with`" (label at the assignment; help "add a `ref this` method to `{0}`, or build a new value with `with`"; fix no). BYN5013 (plan) `RepeatedParticle` "`{0}` uses the particle `{1}` twice" (label at the second particle; help "group the values in a struct, or choose another particle"; fix no). BYN5012 is skipped: the HANDOFF uses it as the `@allow` example; check it is free with grep before allocating it later.
- (d) BYN3216 in the binder's assignment check (`BinderExpressions.cs`; grep `Assign`); BYN5013 in the particle check (5.3e). v0.1: yes for both.

### §3 Views and copies
- (a) `span` (read-only view, property), `mutableSpan()` (`ref this`), `copy()` for owned copies of spans and lists, `List(seq)` conversion; no instance `toX`/`asX` members; static `fromX` factories stay.
- (b) SPEC §16 collections (`span`, `mutableSpan()`, `copy()`); §6.2 conversions (`List(seq)`, `string(x)`); GRAMMAR §1 tree: the `toX`/`asX` rule and the `from` factory exception; the property rule's `ref this` exception.
- (c) BYN5009 `ParticleAsMemberName` gains a second message form (one code, J27): "`{0}` spells a conversion with `{1}`; write `{2}`" (label at the name; help "use a view noun, `copy()`, or a `T(x)` conversion"; fix no). Calling `mutableSpan()` on a `let` reuses the existing `ref this`-on-`let` error.
- (d) `BuiltinMembers.cs`: grep `asSpan`, `toList`, `toString`, `asString` (round 2 already renamed `asString` → `finish`) and rename under BYN2230 in 5.4. v0.1: yes.

### Totals
Codes: 71 new (25 syntax, 16 types, 9 grammar, 7 effects, 8 typestate, 6 security), 12 reserved, unchanged retirements. Steps: §1 and §3 land in 5.4; §2 lands in 5.3e (BYN5013) and 5.3b (BYN3216).
