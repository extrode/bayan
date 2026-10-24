# 0010 — State changes are visible in signatures: `becomes` needs `ref this`

Status: accepted 2026-10-24 (user direction, from the 5.3c gate findings on `this` narrowing). Erratum (2026-10-24): rule C also covers passing `this` or a `ref` parameter of a stated type as an `out` argument, which writes a whole value (BYN7012 at the argument, same exemption when the type names one state and the callee's `out` parameter type is that state). Erratum (2026-10-24): under rule A, a `becomes` call whose failure is caught and handled in place does not count as `become` on the handler's path; that path keeps the old state (BYN7009 applies), matching §3.17 item 11 "a failed transition leaves the state unchanged".

## Problem

Typestate exists so that every place a value's state can change is visible in a signature: a caller trusts what it knows about a value's state without reading any method body.

```
if order is .placed {
    order.addNote("gift")
    order.applyDiscount(code)
    try order.pay(card)          // still .placed: neither call says `becomes`
}
```

SPEC §3.17 leaves four ways to change a state that no signature shows, and the 5.3c gate found each as stale reads (a payload read or a state-only call accepted after the state changed):

1. `becomes` on a `move this` or `this` receiver. The BYN7003 page's own example is `void pay(move this is .draft, i64 amount) becomes .paid`. A `move this` receiver ends the caller's ownership and a plain `this` is a copy, so the transition changes a state nobody can observe. It also costs flow analysis, since loop entry has to forget `this` facts for those receivers too. Round 3's design note says the same: "`move this` ends ownership (`tx.commit()`, `rows.all()`); typestate (`becomes`) moves a live value through a lifecycle."
2. A method without `becomes` calls a `becomes` method on `this` (`void erase(ref this) { cancel() }`).
3. A function calls a `becomes` method on one of its `ref` parameters.
4. A whole-value assignment to `this` or to a `ref` parameter swaps the state (`void wipe(ref Order o) { o = Order.draft { id: o.id } }`, then `erase(ref this) { wipe(ref this) }`).

The alternatives both fail. Reading method bodies at each call is whole-program analysis: checking stops being modular, and changing a helper's body can break callers in other files. Assuming every mutating call may change the state drops every fact after every `ref this` call, which makes typestate useless in practice.

## Decision

**A state changes in place only through `become`, or by consuming a value and returning a new one** (`Order.shipped ship(move Order.paid order, …)`). Both appear in a signature.

1. **`becomes` needs `ref this`.** A method that declares `becomes .s` takes `ref this` (with or without `is .s`). `becomes` on a `this` or `move this` receiver is an error (BYN7010). A `become` in a method whose `becomes` BYN7010 rejected reports nothing more (no BYN7003, no BYN7009), so the user sees one error, not a cascade. `move this is .s` and `this is .s` stay valid receivers for methods without `becomes` (§3.17 item 3).
2. **Rule A: a `becomes` call on `this` needs `becomes` on the caller.** Inside a method, calling a `becomes .t` method on `this` requires the enclosing method to declare `becomes` including `.t` (BYN7003, third form). For BYN7009 and for what is known about `this` afterwards, the call counts as `become .t`.
3. **Rule B: no `becomes` call on a `ref` parameter.** A function or method may not call a `becomes` method on one of its `ref` parameters (BYN7011). Transitions in place happen only through the receiver. A free function uses the consuming shape instead: `move Order.paid` in, `Order.shipped` out.
4. **Rule C: an assignment keeps the state.** A whole-value assignment to `this` or to a `ref` parameter whose type has states is allowed only when the target's type names exactly one state and the new value is in that state (BYN7012):
   - `ref Order o` (any state) can't be reassigned: the callee doesn't know which state the caller had.
   - `ref Order is .a or .b` can't be reassigned, for the same reason.
   - `ref Order.paid o` can be reassigned to another `Order.paid`.
   - `this = …` under `ref this is .s` follows the same rule. (The parser rejects `this = …` today, BYN1011, so this case is not reachable yet.)
5. **Callers keep their facts across calls without `becomes`.** Passing a value `ref` (a local or `this`) and calling a `ref this` method without `becomes` leave what the caller knows about its state unchanged: rules A–C guarantee the callee can't change it. An `out` argument still drops the facts, because the callee supplies a new value. Loop entry drops `this` facts only for `ref this` receivers, which are the only ones that can change state.

**Why `become` and not assignment.** Counting `this = Order.draft { … }` in a `becomes` method as the transition would give one act two spellings, and the worse one. `become .paid(payment: id)` states only what changes: the other fields stay, and the compiler checks the payload is complete. An assignment restates every field, so it's easy to forget one or copy a stale value. One spelling also gives one place for what must happen on every transition (events, logging, auditing).

## Sections

- §3.17 item 4: "Narrowing happens only through `is`, `switch`, or a `becomes` call on a `var`." Append: "A state changes in place only through `become`, or by consuming a value and returning a new one (`move Order.paid` → `Order.shipped`); so a call to a method without `becomes` leaves what the caller knows unchanged."
- §3.17 item 11, after the first sentence: "Only a `ref this` method may declare `becomes`: a `move this` or `this` receiver leaves the caller with no value whose state could change (BYN7010). A method that calls a `becomes .t` method on `this` declares `becomes` including `.t` itself, and the call counts as `become .t` (BYN7003). A `becomes` method can't be called on a `ref` parameter (BYN7011). A whole-value assignment to `this` or to a `ref` parameter keeps its state: allowed only when the target's type names one state and the new value is in it (BYN7012)."
- §3.17 closing note, Codes: add BYN7010, BYN7011, BYN7012.

## Codes

- Added: BYN7010 (plan, revision 0010) `BecomesNeedsRef` "`{0}` declares `becomes .{1}` but takes `{2}`; a transition needs `ref this`" (label at the receiver; help "change the receiver of `{0}` to `ref this`, or return the new state as a value"; fix yes: replace the receiver's `move`/plain mode with `ref`, keeping `is .s`).
- Added: BYN7011 (plan, revision 0010) `BecomesOnRefParameter` "`{0}` changes the state of `{1}`, a `ref` parameter; a state changes in place only through `ref this`" (label at the call; help "take `{1}` by `move` and return the new state as a value"; fix no).
- Added: BYN7012 (plan, revision 0010) `AssignmentChangesState` "`{0}` would change state here; a state changes only through `become`" (label at the assignment; help "assign a value in the same state, or move the transition into a `becomes` method"; fix no).
- Changed: BYN7003 gains a third form, "`{0}` calls `{1}`, which changes the state; declare `becomes` on `{0}`" (label at the call; help "add `becomes .{2}` to the signature of `{0}`"; fix yes when `{0}` has no `becomes`: add the clause; otherwise no). Its page's example and fix text use `ref this`.

## Effect on built work

- 5.3c (second half, in progress):
  - The binder reports BYN7010 at the signature, BYN7003's third form and BYN7011 at the call, and BYN7012 at the assignment.
  - `ForgetChangingStates` goes back to `ref this` only.
  - Run 7's "every `ref this` call drops the receiver's facts" and the `ref`-argument drop (for locals and `this`) are removed; `out` arguments still drop.
  - A `becomes` call on `this` counts as `become` for BYN7009 and sets `this`'s known state. That closes the `work/todo.md` line on becomes-method calls on `this`.
  - In `diag/this-narrowing`, the `move this` loop case is replaced by a BYN7010 case and a `ref this` loop case. The `erase`/`wipe`/`clear` cases become BYN7003/BYN7012 cases, and a case shows facts kept across a non-`becomes` `ref this` call and a `ref` argument.
  - `diag/payload-not-in-state`'s `reset(ref copy)` helper becomes a BYN7012 case or is rewritten to keep its state.
- Goldens whose `move this … becomes` is incidental (`diag/become-undeclared`, `diag/unknown-state`, `typed/state-types`) switch to `ref this`. Parse goldens (`parse/receiver-states`, `parse/clauses-line-start`) are syntax only and stay.
- Handoff Question 295's list of fact-dropping events is updated to match item 5.

## Sūq files

None. Sūq's typestate methods already use `ref this` (`suq/orders/order.byn:99-113`) and change state only through `become`.
