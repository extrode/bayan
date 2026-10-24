# 0007 — Field writes by invariant, and `with` reassignment as a move

Status: accepted. Date: 2026-10-24. Amends HANDOFF O11 (§26), the plan's BYN3216 entry and J78, and the 5.3b criteria.

## Problem

1. O11 as built rejects outside field writes on every struct. The 5.3b golden rewrites show the cost: plain data (`Mode.braceDepth` in the selfhost lexer, `Particle` in a game loop) needs `with` or one-off methods, with no invariant protected. In hot loops it relies on the optimizer turning `with` back into a write.
2. `x = x with { … }` on a `unique` struct is reported as an implicit copy (BYN2780). The old value is dead once reassigned, so this is a move, and `with` is the update path for `requires` and typestate structs.
3. Whether a step's Files column binds strictly was undecided.

## Decision

### 1. O11 applies only to structs with invariants

A field may be written from outside its struct's own methods unless the struct:

- has a `requires` rule (field- or struct-level),
- has a `states` block, or
- is marked `@sealed`.

For those structs, outside writes are BYN3216, and updates go through the struct's `ref this` methods or `with` (which re-checks `requires`). Plain data structs allow direct field writes everywhere.

```csharp
modes[depth].braceDepth += 1                     // Mode is plain data: allowed
particles[i].life -= dt                          // allowed
form.password = "x"                              // BYN3216: SignupForm has requires
order.total = 0myr                               // BYN3216: Order has states

@sealed
public struct HttpClientOptions {
    Millis timeout
    i32 retries
}
options.retries = 5                              // BYN3216: HttpClientOptions is @sealed
```

- **`@sealed`** is a struct attribute with no arguments and no `because`. It is a library author's opt-in for encapsulation without invariants: the representation can change later without breaking callers.
- **BYN3216 message** names the reason: "`{0}.{1}` can be written only by `{0}`'s own methods or with `with`: `{0}` {2}", where `{2}` is "has `requires`", "has states" or "is `@sealed`". Help unchanged.
- **Precedence with BYN3208** (0002 item 23) is unchanged: from outside, BYN3216 only; inside the struct's own methods, a field of a struct with `requires` is BYN3208.

### 2. `x = x with { … }` consumes the old value

When the target of an assignment is the same variable the `with` expression reads (`x = x with { … }`), the assignment consumes the old value: it is a move, not a copy, for `unique` structs and for every other struct. It is not BYN2780. Any other use of a `unique` value in a `with` expression whose result is assigned elsewhere (`y = x with { … }`) still needs an explicit `move x`.

```csharp
var tx = try db.begin()                          // Transaction is unique
tx = tx with { label: "nightly" }                // move: allowed
let copy = tx with { label: "copy" }             // BYN2780: write `move tx with { … }`
```

### 3. The Files column

- Editing a file that another active step owns is forbidden.
- Editing a file no active step owns is allowed; the gate summary lists each such file with a one-line reason.

### 4. Golden restorations in 5.3b

Restore each golden rewritten for BYN3216 to its original text when its struct has no `requires`, no `states` block and no `@sealed`, and the original still raises the code the test asserts. Keep a rewrite only where restoring would change what the test checks, and say why in the gate summary. This includes `selfhost/lexer/lexer.byn`, `run/call-result-ref-plain-struct`, `diag/var-never-reassigned`, `run/map-copy-allowed` and `diag/map-copy`. `run/take` may return to its original form or use `with` after §2.

## Sections

- HANDOFF §26 (O11 text; `@sealed`), §4 (attribute list adds `@sealed`), changelog.
- SPEC §3.10 field visibility; §9 ownership (`x = x with { … }` is a move); §4 attributes (`@sealed`).
- Plan: BYN3216 entry (message with `{2}`), J78 (invariant scope), §4 header (Files rule), 5.3b row (`@sealed`, the `with` move, restorations).

## Codes

- Changed: BYN3216 message gains the reason `{2}`; BYN2780 no longer fires for `x = x with { … }`.
- No new codes. Totals unchanged.

## Sūq files

None yet. The regeneration (C46) writes plain data structs with direct field writes, and uses `@sealed` only for public library option types.
