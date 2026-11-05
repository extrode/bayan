# Round 4 draft, §10: Lessons intake

Date: 2026-11-05. Append to the round-4 draft as §10 (appended: [`docs/bayan-design-round-4-draft.md` §10](../bayan-design-round-4-draft.md#10-lessons-intake); edit both together). It lands with the round-4 batch after 012: one migration, one renumbering.

## Conventions

- **Diagnostic codes** are descriptor names only. Numbers are assigned in the round-4 renumbering (thousands by area, hundreds by category).
- **Signature clause order** follows the round-4 order: `where → expects → uses → becomes → fails`.
- **Already in the draft and not repeated:**
  - P1 benchmarks (lesson P09)
  - P2 proven-check removal (P04)
  - P3 one C body per layout (P02)
  - P7 compile budget
  - Closure escape rule with `fails` polymorphism (E03)
  - Clause order (T08)
- **Each item gives:**
  - (a) the decision
  - (b) spec, grammar and style text
  - (c) diagnostics
  - (d) implementation notes and goldens

## R4.1 Private signatures infer `fails`; `signatures` switch (E04). Decided 2026-11-05

**(a) Decision**

Non-public functions may omit `fails`; the compiler infers the set from the body. This is the rule SPEC §13 already applies to `uses` (public declares, private infers), extended to `fails`.

These always declare:
- public functions
- interface methods
- stored function types and event types

Inference stays inside one module.

`package.byn` gets a top-level key, `signatures: .inferred | .declared`, with default `.inferred`. Under `.declared`, every signature declares both `fails` and `uses`. `main`, scripts and tests stay exempt; they infer as today.

```cs
// package.byn
Package {
    name: "suq"
    signatures: .declared
}
```

```cs
// .inferred (default): fine, the set is inferred as ParseError
Line parseLine(string text) {
    let quantity = try parseQuantity(text)
    …
}

// .declared: SignatureIncomplete; the fix-it writes the inferred clause
Line parseLine(string text) fails ParseError {
    …
}

// public, either mode: must declare
public Order parseOrder(string text) uses alloc fails ParseError {
    …
}
```

**(b) Spec and style**

- SPEC: the `fails` section and §13 effects cite one shared rule, "public declares, private infers".
- SPEC §19.2 `package.byn` schema gains `signatures`.
- The key is **not** under `style`, because SPEC §21.5 says style is never compiler-enforced and this is. Hover and `bayan show` display inferred clauses.
- No dialect results: code written under `.declared` compiles unchanged under `.inferred`, and turning `.declared` on is one fix-it pass.

**(c) Diagnostics**

- `PublicFailsMissing`
  - Message: "`{0}` is public and can fail with `{1}`; declare it".
  - Label at the signature.
  - Help: "add `fails {1}`".
  - Fix: yes, inserted in clause order.
  - Mirror the existing public-`uses` diagnostic in the effects range.
- `SignatureIncomplete`
  - Message: "`signatures: .declared` requires `{0}` here".
  - Label at the signature.
  - Help: "add `{0}`".
  - Fix: yes.

**(d) Implementation**

- Reuse the `uses` inference pass for `fails`.
- Goldens:
  - an inferred private function
  - the public error
  - the `.declared` error and its fix-it
  - a test function exempt under `.declared`
  - one `.declared` project that builds unchanged after its key is removed

## R4.2 Conformance coherence, rule B (T06). Decided 2026-11-05

**(a) Decision**

A conformance of type `T` to interface `I` may be written only in the package that declares `T` or the package that declares `I`. Every other package wraps the type in a `type distinct` it owns. For a generic type, the owner is the package that declares the generic (`List`), not the packages of its arguments.

Two forms:
- A conformance written on the type itself stays as today: `struct Point : IHashable`.
- A new `extend T : I { … }` form writes a conformance away from the type's declaration. `extend` is a placeholder spelling. Before it is final, it must:
  - pass the R4.4 keyword budget
  - pass the GRAMMAR verb rules
  - be checked against the waṣl line-joining starters

Dependencies are acyclic, so a package can only conform types and interfaces it can see. For a `std` interface and a `geo` type, only `geo` can write the conformance.

```cs
// std/hash.byn: std depends on nothing
public interface IHashable {
    u64 hash()
}

// geo/point.byn: geo depends on std; allowed because geo owns Point
public struct Point : IHashable {
    public f64 x
    public f64 y
    public u64 hash() { … }
}

// json/encode.byn: json depends on std; allowed because json owns IJsonEncodable
public interface IJsonEncodable {
    void encode(ref JsonWriter w)
}

extend string : IJsonEncodable {
    public void encode(ref JsonWriter w) => w.write(this)
}

// app/wire.byn: app owns neither Point nor IJsonEncodable, so it wraps
type distinct JsonPoint = Point

extend JsonPoint : IJsonEncodable {
    public void encode(ref JsonWriter w) {
        w.write(this.value.x)
        w.write(this.value.y)
    }
}
```

**(b) Spec and grammar**

- SPEC §11 interfaces: a coherence subsection with the four-package example above.
- GRAMMAR keyword list: the final spelling of `extend`.
- SPEC §21.5: no input form.

**(c) Diagnostics**

- `ForeignConformance`
  - Message: "`{0}` can implement `{1}` only in the package that declares `{0}` or `{1}`".
  - Label at the conformance.
  - Help: "wrap it in a `type distinct` you own".
  - Fix: no.
- `DuplicateConformance`
  - Message: "`{0}` implements `{1}` twice".
  - Labels at both conformances.
  - Fix: no.

**(d) Implementation**

The check runs at conformance declaration.

Compile-fail goldens:
- a third package conforms another package's type to another package's interface
- a duplicate conformance across two files of one package

Run goldens:
- an interface owner conforming `string`
- an app-owned wrapper with its conformance

## R4.3 Deprecation (G06)

**(a) Decision**

`@deprecated(by: newName, until: edition)` on any public declaration.
- A use of the declaration is a warning.
- From the named edition on, the use is an error.
- `until` keeps its exclusive meaning: usable until that edition, not in it.
- `by` names the replacement.

**(b) Spec**

SPEC attributes and editions sections.

**(c) Diagnostics**

- `DeprecatedUse` (warning)
  - Message: "`{0}` is deprecated; use `{1}`".
  - Fix: yes, a rename to `by:`.
- `DeprecatedRemoved` (error)
  - Message: "`{0}` was removed in edition `{1}`; use `{2}`".
  - Fix: yes.

**(d) Implementation**

The attribute already parses (`@name(args)`); the binder adds the check. `bayan migrate --edition` (v0.3) applies the fix-its.

## R4.4 Keyword budget as a written rule (G04)

**(a) Decision**

GRAMMAR states the current keyword count and a cap. Every new keyword names what it replaces, or why no existing word or particle fits.

**(b) Grammar**

A GRAMMAR keyword section. STYLE needs no change.

**(c) Diagnostics**

None.

**(d) Implementation**

Docs only. Before merge, audit this batch's additions against the rule:
- subjectless `switch` (reuses an existing keyword)
- `when`
- `final`
- `dbg`
- `extend`

## R4.5 Progressive disclosure test (D04)

**(a) Decision**

Every new round-4 feature ships one golden written the way a newcomer would write it:
- no annotations
- no particles beyond what the call requires
- no typestate

**(b) Spec**

None.

**(c) Diagnostics**

None.

**(d) Implementation**

Goldens named `tests/cases/run/beginner-<feature>.byn`, one per feature. A feature fails review if its beginner golden needs an explanation of borrows.

## R4.6 `restrict` from borrow facts (P03)

**(a) Decision**

The emitter marks pointers with `BY_RESTRICT` where the checker proves exclusivity:
- `ref` parameters, since no other borrow of the value exists while it is borrowed
- `struct unique` values passed by pointer

`BY_RESTRICT` lives in `bayan-rt.h` beside the K2 macros. It expands to `restrict` on C99+ compilers and to nothing elsewhere.

**(b) Spec**

No semantic change. Add a line to the emitter notes.

**(c) Diagnostics**

None.

**(d) Implementation**

C printer module only (K1). Lands after K2's macro header.

Acceptance:
- Stage 2 equals stage 3.
- Goldens differ only in C text.
- `bench/` shows the delta.
- Any benchmark regression reverts the item.

## R4.7 Reflection is compile-time only (X05)

**(a) Decision**

No runtime type metadata. Any future reflection runs in comptime and derivations.

**(b) Spec**

The round-4 draft §9 already lists runtime reflection as not adopted. Add a positive sentence to the comptime section.

**(c) Diagnostics**

Reserve `RuntimeReflection`.

**(d) Implementation**

Docs only. The syntax is designed with derivations later (parking lot).

## R4.8 Every escape hatch carries a reason (M07)

**(a) Decision**

`because "…"` is required on every escape hatch:
- `unsafe`
- `@allow`
- `@json(expose)`
- calls to `…Unchecked` members (including `trustUnchecked()`)
- `@unformatted` (style standard, 2026-11-04)

**(b) Spec**

SPEC: the list in round-3 J6 gains `@unformatted`.

**(c) Diagnostics**

BYN8001 covers the rule; extend its list.

**(d) Implementation**

Binder list in BYN8001's check. `bayan audit` (round-3 step 5.5) lists every entry; v0.2a makes its output complete and stable.

## Open for round 4

- The final spelling of `extend` (R4.2).
