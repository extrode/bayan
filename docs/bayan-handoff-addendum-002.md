# Bayan handoff addendum 002 — laws (opt-in), contracts, parallel iterators

Place in `docs/` and merge into `docs/bayan-handoff.md` as before. Spec edits go into `docs/bayan-spec.md`. Same ground rules as the original handoff.

**Scope note:** this addendum designs features for v0.2 and later. **Do not start v0.2 work before the v0.1 exit criterion and addenda 001 are done.** The only item to do now is §1.

---

## 1. Do now: reserve keywords

Add to the reserved-keyword list (spec §2.4) and the lexer, with golden tests that they're rejected as identifiers:

`law requires ensures`

(Following the future-proofing rule: reserving them now means adding the features later breaks nothing.)

---

## 2. Laws: an opt-in language feature (v0.2)

### 2.1 Principle

Laws are **opt-in, chosen by the developer, never imposed by the language or the compiler.**

- A project with no laws behaves exactly as if the feature didn't exist.
- `bayan build` never runs laws.
- Whether declared laws **block** anything is a project setting, **off by default**.
- Once a developer declares a law **and** enables the gate, the tooling enforces it strictly. A law that can fail quietly would just be a comment.

This is separate from the repository's own Markdown laws process (`docs/laws/`) for the C# compiler. That process stays as it is. Bayan's `law` feature is for programs written in Bayan, including the self-hosted compiler later.

### 2.2 Syntax

```csharp
// laws.byn (convention: at the package root or per module; any .byn file is allowed)

/// The sum of all balances is always zero.
law balancesSumToZero(Span<Expense> expenses) {
    let ledger = try Ledger.of(.myr, expenses);
    assert(ledger.balances().values.sum() == 0);
}

/// No sequence of moves can win the game.
[Exhaustive(maxLength: 8)]
law youCantWin(Span<Game.MoveOp> moves) {
    var board = Game.start();
    for (let m in moves) board = m.applyTo(board);
    assert(!board.isWon);
}

[Retired(since: "0.4.0", reason: "superseded by settlingZeroesEveryone")]
law oldSettleRule(…) { … }
```

Rules:
- A `law` is declared like a `property` test: parameters are generated from their types and `[Check]` contracts, with shrinking.
- The doc comment's first line is the law's human statement and is shown in reports.
- Laws have module visibility (like tests) and are stripped from release builds.
- `try` inside a law: a failure counts as a law failure, reported with the error.
- `[Retired(…)]` keeps the declaration for the record; retired laws never run.
- Attributes: `[Exhaustive(...)]` (bounds for exhaustive checking), `[Prove]` (request the `proved` level, §3), `[Cases(n)]` (number of generated cases; default 10,000).

### 2.3 Levels of evidence

| Level | Meaning | Available |
|---|---|---|
| `tested` | Property-based testing with generated inputs and shrinking | v0.2; every law |
| `exhaustive` | Every input within the `[Exhaustive]` bounds is checked | v0.3 |
| `proved` | A solver proved the law for all inputs | v1.x (§3) |

A law's reported level must never overstate the evidence. A solver timeout or an `unsafe`/FFI dependency reports `tested`, with the reason.

### 2.4 Configuration (`package.byn`)

```csharp
laws = Laws {
    gate            = .off,     // .off (default) | .ci | .ciAndRelease
    minLevel        = .tested,  // a gated law below this level fails the gate
    failOnDowngrade = false,    // fail when a law's level drops from its last recorded level
},
```

| Setting | Effect |
|---|---|
| `gate = .off` | `bayan test` does **not** run laws. `bayan laws` runs them on request. Nothing is blocked. |
| `gate = .ci` | `bayan test` runs laws too; a failing law is a test failure (exit code 3). |
| `gate = .ciAndRelease` | Also `bayan dist` refuses to build while any active law fails. |

### 2.5 CLI

```
bayan laws                    run all active laws, report each with its level
bayan laws --json             machine-readable report (same schema family as `bayan test --json`)
bayan laws diff [--base REF]  list laws added, changed, retired or removed since REF (for review)
bayan test --laws             run tests and laws together regardless of the gate
```

Report format (follows `docs/bayan-terminal-style.md`):

```
◆ balancesSumToZero      tested      10,000 cases · 0.31s
◆ youCantWin             exhaustive  all 87,381 sequences up to length 8 · 1.20s
✗ splitIsFair            FAILED      counterexample (shrunk): total = -1, ways = 3
                                     src/ledger/laws.byn:22

◆ 2 hold · ✗ 1 failed · 1 retired
```

`bayan laws` exits 0 when all active laws hold and 3 when any fails, **whatever the gate setting**: running the command explicitly asks for the verdict. The gate only decides whether other commands (`test`, `dist`) include laws.

`bayan laws diff` exists so that changes to laws are visible in review. It doesn't block anything by itself; whether law changes need special approval is the project's process.

### 2.6 Milestone (v0.2)

- Parse `law` declarations, attributes, doc-comment statements.
- Run laws at the `tested` level, reusing the `property` generator and shrinker.
- `bayan laws`, `--json`, `diff`; `bayan test --laws`; `package.byn` `Laws` settings and gate behaviour.
- Golden tests: passing, failing (with shrunk counterexample), retired, gate on and off, exit codes, JSON schema.
- Diagnostics (allocate codes in the semantics range): a law with no parameters warns "this law checks a single case; consider a test"; `[Exhaustive]` without bounds is an error.

---

## 3. Contracts and verification (v1.x; design only)

> Superseded in part (2026-11-06, `docs/bayan-design-round-4-inventory.md` batch 1): `ensures` is not adopted and is unreserved in round 4; preconditions are `expects`, return guarantees are a `requires` on the return type (round-4 draft §9).

```csharp
public (Cents share, int extra) splitEvenly(Cents total, int ways)
    requires total >= 0 && ways > 0
    ensures result.share * ways + result.extra == total
    ensures 0 <= result.extra && result.extra < ways
{ … }
```

- `requires` (preconditions) and `ensures` (postconditions; `result` names the return value) are **opt-in** like laws.
- At runtime they behave like `[Check]` invariants: checked in debug and safe builds, skipped in fast builds, panicking with the clause text on violation.
- `bayan verify` (v1.x) sends contracts and `[Prove]` laws in the decidable subset (integer arithmetic, arrays, enums, unions) to Z3. Results: proved, counterexample, or unknown (timeout). Unknown falls back to `tested`.
- Out of scope for proofs: floating point, `unsafe` blocks, FFI calls. Proofs are about Bayan's semantics, not the C compiler's output; state this in the spec.
- Contract clauses must be pure, `noalloc` and `nosuspend` (inferred), like `[Check]`.

---

## 4. Parallel iterators (v0.3; design only)

```csharp
let revenue = orders.par().where(o => o.isPaid).sum(o => o.total);
```

- `.par()` on any `ReadOnlySpan<T>`, `Span<T>` or `List<T>` returns a parallel view; operators mirror the sequential iterator API.
- Runs on the `Io.Threaded` pool (or the evented runtime's worker pool).
- **Safety is checked at compile time** using existing effect and borrow inference. A closure passed to a parallel operator may not modify captured variables, perform `Io`, or structurally modify shared collections:

```
error BYN3405: a parallel closure modifies the captured `count`
  help: use a reduction (`orders.par().count(o => …)`) or an Atomic<int>
```

- Reductions (`sum`, `count`, `min`, `max`, `aggregate`) require an associative operation; `aggregate` takes a combiner.
- Results are deterministic for associative reductions; `forEach` order is unspecified (documented).

---

## 5. Spec changes to apply

- **§2.4** — add `law`, `requires`, `ensures` to the reserved keywords now; move them to the active keyword list when their milestones land.
- **New §20.x "Laws (opt-in)"** — §2 of this addendum: principle, syntax, levels, configuration, CLI, exit codes.
- **New §14.x "Contracts"** — §3: `requires`/`ensures` runtime semantics now; verification marked as v1.x.
- **§16** — add `.par()` and the parallel-closure rule, marked v0.3.
- **Appendix B** — replace "`bayan verify` (contract proofs)" with a pointer to §14.x.

## 6. Also add to the repository

`docs/bayan-terminal-style.md` (provided separately) is **normative for the output of M12 — CLI completeness and machine-readable output** and for the `bayan laws` report format above. Add it to `docs/` and reference it from the handoff's section for M12 — CLI completeness and machine-readable output.
