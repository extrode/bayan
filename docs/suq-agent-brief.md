# Sūq → Bayan: round-3 review input and Sūq-0 goldens

From Sy, for the compiler agent. This follows `docs/bayan-status-for-suq.md` (commit `af964b5`, `feat/design-round-2`).

The round-3 plan (J1–J25) is awaiting Sy's approval. This document feeds Sūq's findings into that plan before approval, and adds a first compilable slice of Sūq as golden tests.

## What to do

1. **Part A.** Apply Sy's decisions to the round-3 docs on `feat/design-round-3`: the HANDOFF, plan, SPEC and GRAMMAR. If the round-3 docs already decide an item differently, keep the decision below and record the conflict in your response. Don't silently pick one.
2. **Part B.** Map each open friction item to a round-3 plan step, or propose a new one. Say whether the round-3 docs already answer it.
3. **Part C.** Check that every round-3 audit fix appears in the round-3 GRAMMAR/SPEC.
4. **Part D.** Add the Sūq-0 goldens on `feat/design-round-2` and make them pass without editing any expected output.
5. **Part E.** Write `docs/suq-round3-review-response.md` with everything listed there, and replace the two Sūq files in `docs/` with the versions attached to this message.

The attached `suq-reference-app.zip` and `bayan-reference-app-friction.md` replace the copies in `docs/`. They include a double-check pass: 15 fixed bugs plus one found while writing Sūq-0 (`-|` on signed types).

---

## Part A — Decisions for the round-3 plan

Each item gives what the compiler implements now, then Sy's decision. Apply the decision to the round-3 docs; it lands in code with the matching J step.

**A1. When a `defer` modifier is evaluated.**
- Now: `defer stmt if c` evaluates `c` at scope exit.
- **Decision: at registration, as round-3 J20 says.** Evaluated at exit, the modifier is just `defer { if c … }`, so it adds nothing. Sūq's restock script relies on registration timing: `defer log("dry run: rolled back") if options.dryRun`.
- Add the pending golden in Part D7.

**A2. `fail` vs `return .variant` for errors.**
- Now: `return .invalid`.
- **Decision: a `fail` statement.** In a function that returns a union value and also fails, `return .badRequest` (a value) and `return .overflow` (an error) look identical. Sūq has this in every HTTP handler (`-> Response fails …`).
- `fail .x if cond` and `?? fail .x` follow from this.
- `return .x` in a fallible function becomes an input form with a fix-it, but only when the return type isn't a union containing `.x`.

**A3. Binding a payload in a switch arm.**
- Now: positional, `.circle(let r)` (Q45).
- **Decision: keep positional, and add whole-value binding:** `.percentOff p => p.rate`, where `p` is narrowed to that case. This matches `catch switch { DbError e => … }` and reads better for payloads with three or more fields.

**A4. Numeric conversions.**
- Now: C# casts `(T)x`, checked. These already forced a parser workaround (`if (i32)big > 3`) against the no-parentheses-on-conditions rule.
- **Decision: `T(x)`, checked (traps when out of range), with `T.tryFrom(x)` returning `T?`.**
- `(T)x` becomes an input form with a fix-it.

**A5. Copying a borrowed string.**
- Now: there's no `owned()`; the idiom is `$"{s}"`.
- **Decision: `s.copy()`, matching collections' `.copy(to: arena)`. `s.copy(to: arena)` also exists.** Sūq will replace `owned()` with it.

**A6. Asserting that a call fails.**
- Now: missing. The workaround is `f() catch { return; }` followed by `panic`.
- **Decision: `assert f() fails E.case`**, also `assert f() fails E` for any case of the set. It prints a power-assert tree when the call succeeds or fails differently.

**A7. Adopt implemented behaviour into the round-3 SPEC.** These were questions in Sūq's friction log; the compiler already answers them, and the answers are right.
- **F-12:** a struct holding a unique value is unique without the keyword. Extend the inference from collections to any unique field, such as a `SecretKey`. The explicit `struct unique` stays for raw resources.
- **F-14:** owning values never copy implicitly (BYN2780). Copies are explicit with `.copy(to:)`.
- **F-19:** fallible constructors are `T(x)`, with `T.tryFrom(x)` (like `string.tryFrom`) and `T.tryParse(text)` for text.
- **F-26:** the empty map literal is `[]` with a target type, not `[:]`.

---

## Part B — Open friction items to place in the plan

Full text is in `docs/bayan-reference-app-friction.md`. For each item, give the plan step it belongs to (or "new step"), and whether the round-3 docs already answer it and how.

| # | Area (round-3 §) | Sūq's proposal |
|---|---|---|
| F-02 | Testing | A `measure { }` block inside `bench` separates setup from the measured, budgeted part |
| F-03 | Laws | Fix `law` syntax and where the level (tested/exhaustive/proved) is set |
| F-05, F-06 | §14 Typestate | Syntax for constructing a value in a given state (Sūq assumed `Order.paid { … }`), and which state a plain literal starts in |
| F-07 | §11 `requires` | `with` is infallible when no changed field has a rule and no struct-level rule reads it |
| F-08 | §11 | Literal arguments and `const`s of `requires` types are checked at compile time, with no `try` |
| F-09 | §15 Particles | Exempt `extern` signatures, operators, union payloads and laws from the particle rule |
| F-10 | §17 / sql | Arguments after a `sql"…"` literal are variadic and checked against its `$n` count |
| F-11 | §17 Trust | An `Untrusted<bool>` is usable directly as a condition |
| F-13 | §17 | Columns or tables declared `untrusted`, read back as `Untrusted<T>` (for stored customer input) |
| F-16 | §11 | A struct-level rule that reads a field which itself failed is skipped; only the field's violation is reported |
| F-17 | §16 Secret | `requires` may read a `Secret`, so password fields can be `Secret<string>` |
| F-18 | Derived forms / regions | What an `XOp` value may capture when stored in a long-lived container |
| F-21 | §2 Declarations | Block-bodied computed properties: `public total: Money { … }` |
| F-22 | std | `sort` is stable; an unstable sort gets its own name |
| F-23 | §13 Concurrency | `Io.group(timeout:)` can map its deadline into the caller's error set once |
| F-24, F-25 | §8 Units / O2 | Reserve `Bytes` for the unit (data is `Span<u8>`); record how O2's integer counts mix with byte sizes |
| F-27 | Derived forms | Naming of `@forms(Op)` on free functions (`BookWithRetryOp`) |
| F-28 | §13 | A `@blocking` marker that warns when a CPU-heavy function is called outside `Io.offload` |
| F-29 | §18 Errors | Block arms and `return` arms in `catch switch` (Sūq-0 `suq-quote` uses `return` arms) |
| F-30 | FFI | `extern "lib" { … }` block syntax, and user-defined `drop` for `unique` types |
| F-31 | §12 Naming | Exempt static factories that name their result (`Money.zero(c)`, `Outbox.empty()`), following the spec's `T.zero` |
| F-33 | §16 Secret | `Secret` owns its storage and wipes it on drop |
| F-34 | §5 Control flow | `for (a, b) in xs` is tuple destructuring; only `for (x in xs)` is the rejected input form |

---

## Part C — Round-3 audit fixes to verify

These came out of an audit of the round-3 HANDOFF before it reached you, and should be in the version with O1–O10 resolved (`e60ab6f`). For each, cite the GRAMMAR/SPEC line, or say how the docs differ.

1. Waqf termination: a newline ends a statement unless the line ends with a binary operator, or the next line starts with `.`, `?.`, `??`, `except` or `requires`. A blank line always ends one.
2. Head-first declarations: `struct unique`, `type distinct`, `union error X { … }`, and `union error S = A, B` for composition. No `unique struct`, `error enum` or `error set`.
3. `@attr` attributes, camelCase. `because "…"` is required on `@allow`, `unsafe`, `xUnchecked()`, `trustUnchecked()` and `Secret.expose()`.
4. `requires it …` (not `value`). A struct-level rule is attributed to a field with `at .field` (O4).
5. Function types use `->` (O5), e.g. `onPaid: Order -> void uses net`.
6. `case not .x` (no `case _ except .x`).
7. Unit conversions: `x.convert(to: KiB)` and `t.round(to: Seconds)` (no `.in(...)`, `ceil`, `floor`).
8. `log(msg, as: .level)` (no `Log.debug`, no `at:`).
9. `provide name: handler { }` for effect handlers. No `context (…)` blocks; `context` is only the value.
10. No `noalloc`, `nosuspend` or `suspends`; effects are `uses …`, and no clause means pure.
11. `format()` / `formatInto(ref dst)`; no `toString()`; no string `+`.
12. Each open question's resolution, in the round-3 docs:
    - O1: one union case per line.
    - O2: counts (`length`, `runeCount`) are plain integers; units are for sizes and offsets.
    - O3: a rule is field-level or struct-level by content.
    - O4: `at .field`.
    - O5: `->` in function types.
    - O6: the `for` particle.
    - O7: `Money + Money` traps on mixed currencies; `tryAdd` and `add(…) fails` exist.
    - O8: `state` is contextual.
    - O9: no bare literals for distinct types.
    - O10: a decode from untrusted input is trusted only when no field is free text or bytes.

    If any resolution differs, quote what the docs decided. Sūq assumed these.

---

## Part D — Sūq-0 goldens (current syntax)

Four programs and one test file, written in the round-2 syntax the compiler implements at `af964b5`. They cover what compiles today: pricing, money, packing and the order lifecycle. Units, distinct types, typestate and `requires` on fields aren't implemented, so the slices use `i64` grams and basis points, runtime state checks, and explicit validation. Every expected output was generated from Sūq's C# baseline, which has 37 passing tests, including against PostgreSQL. The programs aren't guesses about the domain; they're a port.

### Rules

- **D1.** Add them as `tests/cases/run/suq-money.byn`, `suq-quote.byn`, `suq-packing.byn` and `suq-order.byn`, each with its `.stdout`, plus `suq-money.test.byn`. `suq-quote` and `suq-order` use `Money`/`Currency`/`PricingError` from `suq-money`. If run goldens are single-file, copy those declarations in, without the top-level statements.
- **D2. Never edit a `.stdout`.** If the output differs, find out whether it's a compiler bug or a bug in the slice, fix that, and report the diff and the cause.
- **D3. Make the smallest syntax changes that get each file to compile, and log every one** (file, line, what was written, what was needed, why). The log is the point of the exercise: it measures how far this code sits from the implemented language. Don't change any logic.
- **D4.** If a feature is missing (for example `return` arms in `catch switch`, a `const` array of structs, or `stackalloc` of `i64`), use the smallest workaround, log it, and open a Question if it belongs in the language.
- **D5. Zero allocations.** If the suite has an allocation-counting harness (as `LexerTests` does), add a test that `pack` on the 40-item sample allocates nothing after the spans are set up.
- **D6.** The naming checker skips free functions, so names here are already verbs where round 3 will require it (`findSmallestBox`, `sortPicks`, `computeCheckDigit`). Don't rename them.
- **D7. Pending golden for A1.** Add it as expected-to-fail until J20 lands:

  ```csharp
  // defer-modifier-registration.byn — the condition is read when the defer is registered
  void f() {
      var c = true;
      defer print("cleanup") if c;
      c = false;
      print("body");
  }
  f();
  ```

  Expected (registration time):

  ```
  body
  cleanup
  ```

  Today it prints only `body`.

### Assumed syntax

These are taken from the status report. Where a detail is wrong, D3 applies.

- `T name(params)`, `;`, `let`/`var`.
- `public` on every field used by other modules.
- Methods inside structs, with `ref this` for mutators.
- Unions with positional payloads; error enums with `///` summaries; `error set S = A, B;`.
- `return .case if cond;`, `catch (e) { … }`, `catch switch { Type => … }`.
- `[Cases(…)]` and `assert(…)`.
- `stackalloc`, `.asSpan`, `+%`, `*%`, `-|`, `(T)x` casts, `StrBuf`.
- `print(string)`.
- `list.add(x)` and `.length`: use whatever the current names are and log it.

### `suq-money.byn`

```csharp
// suq-money.byn — Sūq-0 slice 1: currency-safe money, formatting, error enums
// Written in round-2 syntax (what the compiler implements at af964b5).

public enum Currency { myr, sgd, usd }

string code(Currency c) => c switch { .myr => "MYR", .sgd => "SGD", .usd => "USD" };

public error enum PricingError {
    /// the currencies differ
    currencyMismatch,
    /// the amount does not fit in i64
    overflow
}

const i64 maxCents = 9223372036854775807;

/// An amount in minor units (sen, cents) of one currency.
public struct Money {
    public i64 cents;
    public Currency currency;

    public Money plus(Money other) fails PricingError {
        return .currencyMismatch if other.currency != currency;
        return .overflow if other.cents > 0 && cents > maxCents - other.cents;
        return Money { cents: cents + other.cents, currency: currency };
    }

    public Money minus(Money other) fails PricingError {
        return .currencyMismatch if other.currency != currency;
        return Money { cents: cents - other.cents, currency: currency };
    }

    public Money times(i32 qty) => Money { cents: cents * qty, currency: currency };

    /// This amount at a rate in basis points (800 = 8%), rounded half up. Non-negative amounts.
    public Money atRate(i64 basisPoints) fails PricingError {
        return .overflow if basisPoints > 0 && cents > (maxCents - 5000) / basisPoints;
        return Money { cents: (cents * basisPoints + 5000) / 10000, currency: currency };
    }

    /// Writes "MYR 12.50". Drives `$"{m}"`.
    public void formatInto(ref StrBuf sb) {
        sb.append(code(currency));
        sb.append(" ");
        var whole = cents;
        if cents < 0 {
            sb.append("-");
            whole = -cents;
        }
        sb.append($"{whole / 100}.");
        sb.append("0") if whole % 100 < 10;
        sb.append($"{whole % 100}");
    }
}

void tryAdd(Money a, Money b) {
    let sum = a.plus(b) catch (e) {
        let name = e switch { .currencyMismatch => "currencyMismatch", .overflow => "overflow" };
        print($"add: {name}");
        return;
    };
    print($"add: {sum}");
}

print($"{Money { cents: 1250, currency: .myr }}");
print($"{Money { cents: 5, currency: .myr }}");
print($"{Money { cents: -300, currency: .myr }}");
tryAdd(a: Money { cents: 100, currency: .myr }, b: Money { cents: 100, currency: .myr });
tryAdd(a: Money { cents: 100, currency: .myr }, b: Money { cents: 100, currency: .sgd });
tryAdd(a: Money { cents: maxCents, currency: .myr }, b: Money { cents: 1, currency: .myr });
let tax = try Money { cents: 1063, currency: .myr }.atRate(800);
print($"8% of MYR 10.63: {tax}");
print($"3 x MYR 12.50: {Money { cents: 1250, currency: .myr }.times(3)}");
```

Expected `suq-money.stdout`:

```
MYR 12.50
MYR 0.05
MYR -3.00
add: MYR 2.00
add: currencyMismatch
add: overflow
8% of MYR 10.63: MYR 0.85
3 x MYR 12.50: MYR 37.50
```

### `suq-money.test.byn`

```csharp
// suq-money.test.byn — the same rules as tests: [Cases], power asserts

[Cases((1250, "MYR 12.50"), (5, "MYR 0.05"), (-300, "MYR -3.00"), (0, "MYR 0.00"))]
test "format" (i64 cents, string text) {
    assert($"{Money { cents: cents, currency: .myr }}" == text);
}

[Cases((1063, 85), (1000, 80), (1, 0), (7, 1))]
test "8% rounds half up" (i64 cents, i64 expected) {
    let tax = try Money { cents: cents, currency: .myr }.atRate(800);
    assert(tax.cents == expected);
}

test "mixed currencies fail" {
    let myr = Money { cents: 100, currency: .myr };
    let sgd = Money { cents: 100, currency: .sgd };
    let _ = myr.plus(sgd) catch { return; };
    panic("expected currencyMismatch");
}

test "add commutes for same-currency amounts" {
    for i in 0..200 {
        let a = Money { cents: i * 37 - 3000, currency: .myr };
        let b = Money { cents: i * 91 - 9000, currency: .myr };
        let ab = try a.plus(b);
        let ba = try b.plus(a);
        assert(ab.cents == ba.cents);
    }
}
```

Expected: all tests pass (four `format` cases, four rounding cases, two single tests).

### `suq-quote.byn`

```csharp
// suq-quote.byn — Sūq-0 slice 2: pricing a basket (promotions, weight bands, tax)
// Needs the Money declarations from suq-money.byn (same module, or copy them in).
// Units are not implemented, so weights are i64 grams and rates are i64 basis points.

public union Promotion {
    percentOff(i64 rateBp, i64 minimumCents),
    buyGetFree(string key, i32 buy, i32 free),
    freeShippingOver(i64 thresholdCents)
}

public struct ShippingBand {
    public i64 upToGrams;
    public i64 feeCents;
}

public struct PriceLine {
    public string key;
    public i32 qty;
    public Money unitPrice;
    public i64 unitGrams;
}

/// Holds lists, so it is unique: pass it with `in`.
public struct Policy {
    public Currency currency;
    public i64 taxBp;
    public List<Promotion> promotions;
    public List<ShippingBand> bands;
}

public struct Quote {
    public Money subtotal;
    public Money discount;
    public Money shipping;
    public Money tax;

    public Money total() fails PricingError {
        let afterDiscount = try subtotal.minus(discount);
        let withShipping = try afterDiscount.plus(shipping);
        return try withShipping.plus(tax);
    }
}

public error enum QuoteError {
    /// heavier than the largest shipping band
    overweight
}

public error set QuoteFailure = PricingError, QuoteError;

/// Prices a basket. Overweight fails even when shipping would be free.
public Quote quote(ReadOnlySpan<PriceLine> lines, in Policy policy) fails QuoteFailure {
    var subtotal = Money { cents: 0, currency: policy.currency };
    i64 grams = 0;
    for line in lines {
        subtotal = try subtotal.plus(line.unitPrice.times(line.qty));
        grams = grams + line.unitGrams * line.qty;
    }
    let discount = bestDiscount(subtotal, lines, policy);

    var freeShipping = false;
    for promo in policy.promotions {
        let applies = promo switch {
            .percentOff(_, _) => false,
            .buyGetFree(_, _, _) => false,
            .freeShippingOver(let threshold) => subtotal.cents >= threshold,
        };
        freeShipping = true if applies;
    }

    let fee = try shippingFee(grams, policy);
    let shipping = Money { cents: freeShipping ? 0 : fee, currency: policy.currency };
    let afterDiscount = try subtotal.minus(discount);
    let taxable = try afterDiscount.plus(shipping);
    let tax = try taxable.atRate(policy.taxBp);
    return Quote { subtotal: subtotal, discount: discount, shipping: shipping, tax: tax };
}

/// The single best promotion wins; promotions don't stack.
/// Bindings are named like the parameters so the implied-name rule covers same-typed arguments.
Money bestDiscount(Money subtotal, ReadOnlySpan<PriceLine> lines, in Policy policy) {
    i64 best = 0;
    for promo in policy.promotions {
        let amount = promo switch {
            .percentOff(let rateBp, let minimumCents) => percentDiscount(subtotal, rateBp, minimumCents),
            .buyGetFree(let key, let buy, let free) => freeUnitsDiscount(lines, key, buy, free),
            .freeShippingOver(_) => 0,
        };
        best = amount if amount > best;
    }
    return Money { cents: best, currency: subtotal.currency };
}

i64 percentDiscount(Money subtotal, i64 rateBp, i64 minimumCents) {
    return 0 if subtotal.cents < minimumCents;
    let off = subtotal.atRate(rateBp) catch { return 0; };
    return off.cents;
}

i64 freeUnitsDiscount(ReadOnlySpan<PriceLine> lines, string key, i32 buy, i32 free) {
    for line in lines {
        return line.unitPrice.cents * (line.qty / (buy + free) * free) if line.key == key;
    }
    return 0;
}

i64 shippingFee(i64 grams, in Policy policy) fails QuoteError {
    for band in policy.bands {
        return band.feeCents if grams <= band.upToGrams;
    }
    return .overweight;
}

string describe(string name, ReadOnlySpan<PriceLine> lines, in Policy policy) {
    let q = quote(lines, policy) catch switch {
        QuoteError => return $"{name}: overweight",
        PricingError => return $"{name}: pricing error",
    };
    let total = q.total() catch { return $"{name}: pricing error"; };
    return $"{name}: subtotal {q.subtotal}, discount {q.discount}, shipping {q.shipping}, tax {q.tax}, total {total}";
}

Money myr(i64 cents) => Money { cents: cents, currency: .myr };

let standard = Policy {
    currency: .myr,
    taxBp: 800,
    promotions: [
        .percentOff(rateBp: 1000, minimumCents: 20000),
        .buyGetFree("TEA-0001", buy: 2, free: 1),
        .freeShippingOver(15000),
    ],
    bands: [
        ShippingBand { upToGrams: 1000, feeCents: 800 },
        ShippingBand { upToGrams: 5000, feeCents: 1500 },
        ShippingBand { upToGrams: 20000, feeCents: 3500 },
    ],
};

List<PriceLine> tea = [PriceLine { key: "TEA-0001", qty: 3, unitPrice: myr(1200), unitGrams: 100 }];
List<PriceLine> rug = [PriceLine { key: "RUG-0001", qty: 1, unitPrice: myr(16000), unitGrams: 4000 }];
List<PriceLine> bed = [PriceLine { key: "BED-0001", qty: 1, unitPrice: myr(90000), unitGrams: 25000 }];
List<PriceLine> lamps = [PriceLine { key: "LMP-0001", qty: 2, unitPrice: myr(12000), unitGrams: 500 }];
List<PriceLine> mixed = [
    PriceLine { key: "TEA-0001", qty: 7, unitPrice: myr(1200), unitGrams: 100 },
    PriceLine { key: "MUG-0001", qty: 2, unitPrice: myr(2500), unitGrams: 400 },
];

print(describe("tea", tea.asSpan, standard));
print(describe("rug", rug.asSpan, standard));
print(describe("bed", bed.asSpan, standard));
print(describe("lamps", lamps.asSpan, standard));
print(describe("mixed", mixed.asSpan, standard));
```

Expected `suq-quote.stdout`:

```
tea: subtotal MYR 36.00, discount MYR 12.00, shipping MYR 8.00, tax MYR 2.56, total MYR 34.56
rug: subtotal MYR 160.00, discount MYR 0.00, shipping MYR 0.00, tax MYR 12.80, total MYR 172.80
bed: overweight
lamps: subtotal MYR 240.00, discount MYR 24.00, shipping MYR 0.00, tax MYR 17.28, total MYR 233.28
mixed: subtotal MYR 134.00, discount MYR 24.00, shipping MYR 15.00, tax MYR 10.00, total MYR 135.00
```

### `suq-packing.byn`

```csharp
// suq-packing.byn — Sūq-0 slice 3: zero-allocation box packing, pick order, check digits
// All working storage is caller-owned (`stackalloc`); the packing loop allocates nothing.

public struct PackItem {
    public string sku;
    public i64 grams;
    public u8 aisle;
    public u16 bay;
}

public struct BoxSize {
    public string name;
    public i64 capacity;
    public i64 tare;
}

const BoxSize[3] boxSizes = [
    BoxSize { name: "S", capacity: 2000, tare: 150 },
    BoxSize { name: "M", capacity: 5000, tare: 300 },
    BoxSize { name: "L", capacity: 12000, tare: 600 },
];

public error enum PackError {
    /// an item is heavier than the largest box can hold
    tooHeavy
}

/// Index of the smallest box an item fits in on its own, or -1.
i32 findSmallestBox(i64 grams) {
    for i in 0..3 {
        return i if boxSizes[i].capacity - boxSizes[i].tare >= grams;
    }
    return -1;
}

/// First-fit decreasing. Fills `boxOf` (per item), `boxKind` and `loads` (per box, load includes
/// tare) and returns the number of boxes. `order` is scratch. Ties keep input order.
public i32 pack(ReadOnlySpan<PackItem> items, Span<i32> order, Span<i32> boxOf, Span<i32> boxKind, Span<i64> loads) fails PackError {
    let n = items.length;
    for i in 0..n {
        order[i] = i;
    }
    // insertion sort, heaviest first; strict `<` keeps it stable
    for i in 1..n {
        let current = order[i];
        var j = i;
        while j > 0 && items[order[j - 1]].grams < items[current].grams {
            order[j] = order[j - 1];
            j = j - 1;
        }
        order[j] = current;
    }

    var count = 0;
    for k in 0..n {
        let i = order[k];
        let grams = items[i].grams;
        let size = findSmallestBox(grams);
        return .tooHeavy if size < 0;
        var box = -1;
        for b in 0..count {
            if box < 0 && loads[b] + grams <= boxSizes[boxKind[b]].capacity {
                box = b;
            }
        }
        if box < 0 {
            box = count;
            boxKind[box] = size;
            loads[box] = boxSizes[size].tare;
            count = count + 1;
        }
        loads[box] = loads[box] + grams;
        boxOf[i] = box;
    }
    return count;
}

/// Pick order: by aisle, then bay; stable.
public void sortPicks(ReadOnlySpan<PackItem> items, Span<i32> order) {
    for i in 0..items.length {
        order[i] = i;
    }
    for i in 1..items.length {
        let current = order[i];
        var j = i;
        while j > 0 && comesAfter(items[order[j - 1]], items[current]) {
            order[j] = order[j - 1];
            j = j - 1;
        }
        order[j] = current;
    }
}

bool comesAfter(PackItem a, PackItem b) => a.aisle > b.aisle || (a.aisle == b.aisle && a.bay > b.bay);

/// FNV-1a reduced to one digit. Wrapping multiply is the algorithm, so `*%`.
public u8 computeCheckDigit(string text) {
    u32 h = 2166136261;
    for c in text {
        h = (h ^ (u32)c) *% 16777619;
    }
    return (u8)(h % 10);
}

void report(string name, ReadOnlySpan<PackItem> items) {
    Span<i32> order = stackalloc i32[64];
    Span<i32> boxOf = stackalloc i32[64];
    Span<i32> boxKind = stackalloc i32[64];
    Span<i64> loads = stackalloc i64[64];
    let count = pack(items, order: order, boxOf: boxOf, boxKind: boxKind, loads: loads) catch (e) {
        print($"{name}: tooHeavy");
        return;
    };
    print($"{name}: {count} boxes");
    for b in 0..count {
        print($"  box {b}: {boxSizes[boxKind[b]].name} {loads[b]} g");
    }
}

List<PackItem> mixed = [
    PackItem { sku: "RUG-0001", grams: 4000, aisle: 3, bay: 1 },
    PackItem { sku: "TEA-0001", grams: 300, aisle: 1, bay: 4 },
    PackItem { sku: "MUG-0001", grams: 500, aisle: 1, bay: 2 },
];
List<PackItem> bed = [PackItem { sku: "BED-0001", grams: 15000, aisle: 9, bay: 1 }];

List<PackItem> sample = [];
for i in 0..40 {
    sample.add(PackItem { sku: $"ITM-{1000 + i}", grams: 100 + (i * 37) % 900, aisle: (u8)(1 + i % 6), bay: (u16)(i % 20) });
}

report("mixed", mixed.asSpan);
report("bed", bed.asSpan);
report("sample", sample.asSpan);

Span<i32> picks = stackalloc i32[64];
sortPicks(sample.asSpan, picks);
var first = StrBuf();
for k in 0..6 {
    first.append(" ");
    first.append(sample[picks[k]].sku);
}
print($"first picks:{first.asString()}");

print($"check digit EX123456789MY: {computeCheckDigit("EX123456789MY")}");
print($"check digit SUQ-42: {computeCheckDigit("SUQ-42")}");
u16 seq = 65534;
seq = seq +% 1;
seq = seq +% 1;
print($"label seq wraps to {seq}");
u32 room = 100;
print($"saturating u32 100 -| 250 = {room -| 250}");
i64 signedRoom = 100;
print($"saturating i64 100 -| 250 = {signedRoom -| 250}");
```

Expected `suq-packing.stdout`:

```
mixed: 2 boxes
  box 0: M 4800 g
  box 1: S 450 g
bed: tooHeavy
sample: 11 boxes
  box 0: S 1978 g
  box 1: S 1978 g
  box 2: S 1992 g
  box 3: S 1992 g
  box 4: S 1992 g
  box 5: S 1992 g
  box 6: S 1969 g
  box 7: S 1981 g
  box 8: S 1995 g
  box 9: S 1997 g
  box 10: S 1144 g
first picks: ITM-1000 ITM-1024 ITM-1006 ITM-1030 ITM-1012 ITM-1036
check digit EX123456789MY: 9
check digit SUQ-42: 5
label seq wraps to 0
saturating u32 100 -| 250 = 0
saturating i64 100 -| 250 = -150
```

### `suq-order.byn`

```csharp
// suq-order.byn — Sūq-0 slice 4: an order's lifecycle without typestate
// Typestate is not implemented, so the state is a union field and every transition checks it
// at run time, like the C# baseline. Needs Money from suq-money.byn.

public enum LineStatus { active, removed, backordered }

public struct Line {
    public string sku;
    public i32 qty;
    public Money unitPrice;
    public i64 unitGrams;
    public LineStatus status;
}

public union OrderState {
    draft,
    placed,
    paid(string payment),
    shipped(string payment, string tracking),
    cancelled(string reason)
}

public error enum OrderError {
    /// the order is not in a state that allows this
    wrongState,
    /// no active lines
    empty,
    /// more lines than an order may have
    tooManyLines,
    /// a line's currency is not the order's
    currencyMismatch,
    /// a line breaks its rules
    invalidLine
}

const i32 maxLines = 200;

/// Holds a list, so it is unique.
public struct Order {
    public i64 id;
    public Currency currency;
    public List<Line> lines;
    public OrderState state;

    public Money total() {
        i64 sum = 0;
        for line in lines {
            sum = sum + line.unitPrice.cents * line.qty if line.status == .active;
        }
        return Money { cents: sum, currency: currency };
    }

    public string statusLabel() => state switch {
        .draft => "Draft",
        .placed => "Awaiting payment",
        .paid(_) => "Paid",
        .shipped(_, _) => "On its way",
        .cancelled(_) => "Cancelled",
    };

    public string tracking() => state switch {
        .draft => "",
        .placed => "",
        .paid(_) => "",
        .shipped(_, let t) => t,
        .cancelled(_) => "",
    };

    public void addLine(ref this, Line line) fails OrderError {
        return .wrongState unless state is .draft;
        return .tooManyLines if lines.length >= maxLines;
        return .currencyMismatch if line.unitPrice.currency != currency;
        return .invalidLine if line.qty < 1 || line.qty > 999 || line.unitGrams <= 0;
        lines.add(line);
    }

    public void removeLine(ref this, string sku) fails OrderError {
        return .wrongState unless state is .draft || state is .placed;
        for i in 0..lines.length {
            lines[i] = lines[i] with { status: .removed } if lines[i].sku == sku;
        }
    }

    public void place(ref this) fails OrderError {
        return .wrongState unless state is .draft;
        var any = false;
        for line in lines {
            any = true if line.status == .active;
        }
        return .empty unless any;
        state = .placed;
    }

    public void markPaid(ref this, string payment) fails OrderError {
        return .wrongState unless state is .placed;
        state = .paid(payment);
    }

    public void markShipped(ref this, string tracking) fails OrderError {
        let payment = state switch {
            .draft => "",
            .placed => "",
            .paid(let p) => p,
            .shipped(_, _) => "",
            .cancelled(_) => "",
        };
        return .wrongState unless state is .paid;
        state = .shipped(payment, tracking);
    }

    public void cancel(ref this, string reason) fails OrderError {
        return .wrongState unless state is .draft || state is .placed;
        state = .cancelled(reason);
    }
}

string errorName(OrderError e) => e switch {
    .wrongState => "wrongState",
    .empty => "empty",
    .tooManyLines => "tooManyLines",
    .currencyMismatch => "currencyMismatch",
    .invalidLine => "invalidLine",
};

Line tea() => Line { sku: "TEA-0001", qty: 2, unitPrice: Money { cents: 1200, currency: .myr }, unitGrams: 100, status: .active };

// 1. the happy path
var order = Order { id: 7, currency: .myr, lines: [], state: .draft };
try order.addLine(tea());
try order.place();
print($"placed: {order.statusLabel()}, total {order.total()}");
try order.markPaid("pay_123");
try order.markShipped("EX123456789MY");
print($"shipped: {order.statusLabel()}, tracking {order.tracking()}");

// 2. a failed transition leaves the state unchanged
var empty = Order { id: 8, currency: .myr, lines: [], state: .draft };
empty.place() catch (e) { print($"place empty: {errorName(e)}"); };
print($"still: {empty.statusLabel()}");

// 3. wrong state is a run-time error here (a compile error with typestate)
var early = Order { id: 9, currency: .myr, lines: [], state: .draft };
early.markShipped("EX123456789MY") catch (e) { print($"ship a draft: {errorName(e)}"); };

// 4. removed lines don't count
var removed = Order { id: 10, currency: .myr, lines: [], state: .draft };
try removed.addLine(tea());
try removed.removeLine("TEA-0001");
print($"after remove: total {removed.total()}");
removed.place() catch (e) { print($"place removed-only: {errorName(e)}"); };

// 5. rules on lines
var bad = Order { id: 11, currency: .myr, lines: [], state: .draft };
bad.addLine(tea() with { qty: 0 }) catch (e) { print($"qty 0: {errorName(e)}"); };
bad.addLine(tea() with { unitPrice: Money { cents: 1200, currency: .sgd } }) catch (e) { print($"SGD line: {errorName(e)}"); };

// 6. cancel only before payment
var paidOrder = Order { id: 12, currency: .myr, lines: [], state: .draft };
try paidOrder.addLine(tea());
try paidOrder.place();
try paidOrder.markPaid("pay_9");
paidOrder.cancel("changed mind") catch (e) { print($"cancel paid: {errorName(e)}"); };
print($"final: {paidOrder.statusLabel()}");
```

Expected `suq-order.stdout`:

```
placed: Awaiting payment, total MYR 24.00
shipped: On its way, tracking EX123456789MY
place empty: empty
still: Draft
ship a draft: wrongState
after remove: total MYR 0.00
place removed-only: empty
qty 0: invalidLine
SGD line: currencyMismatch
cancel paid: wrongState
final: Paid
```

---

## Part E — Response

Write `docs/suq-round3-review-response.md` containing:

1. **Part A.** For each decision, the files and lines changed in the round-3 docs, and any conflict with what the docs said before.
2. **Part B.** A table with columns F-#, plan step (J-#, or "new"), whether the docs already answer it, what they say, and your recommendation where you disagree.
3. **Part C.** A table with columns item, GRAMMAR/SPEC reference, and matches or differs. For anything that differs, quote the text.
4. **Part D.**
   - Pass/fail for each Sūq-0 file, with the commit sha.
   - The syntax-change log (D3).
   - Any `.stdout` diffs with their cause (D2).
   - The workarounds and Questions you opened (D4).
   - The zero-allocation test result (D5).
5. **Attachments, as paths:**
   - The current round-3 HANDOFF.
   - The round-3 plan (J1–J25).
   - The Q89–Q127 list (specs 006 and 007) with the conservative choice made for each. Sy will review these against Sūq's use of interfaces and lambdas before they merge.

Commit message is set by the orchestrator.
