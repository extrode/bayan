## The four laws

All four are good laws: each one has a checkable statement and a concrete counterexample. Three need small changes before you enact them.

### L001: lexer equivalence. Enact after one change.

**The issue:** the law depends on the *system* ICU. Normalization behaves differently across machines, operating systems and .NET globalization modes (NativeAOT builds often run with invariant globalization). A law whose outcome depends on the machine running it will fail intermittently.

**The fix:** make **both** lexers use the same generated Unicode tables, rather than having the C# lexer call ICU. Then the pinned-version clause becomes a property of the repository, not of the machine. I'd also compare a **structured dump** (JSON) rather than rendered text, so a whitespace or color difference in rendering can't fail the law.

> **Statement:** For every byte sequence (including invalid UTF-8, unbalanced interpolation, non-NFC identifiers and bidi controls), `bayan-lex` and the C# lexer produce identical structured token dumps (`--json`: kinds, spans, values, diagnostics with codes, spans and fixes). Both lexers read Unicode data from the same generated tables in the repository; neither consults the system's ICU.

### L002: the lexer never panics. Enact as written, with one clarification.

Say how the input arrives and exclude I/O failures, so that an unreadable file (exit code 4) doesn't count as a counterexample:

> …for every byte sequence **supplied on stdin**, `bayan-lex` terminates with exit code 0 or 1…

### L003: map insertion order. Enact after aligning names with the spec.

The law uses `count`, but the spec makes **`length`** the only size name (§16). It also uses `get(k)` returning null, while the Bayan API is `map[k]` (which panics when the key is missing), `getOr` and `slot`. Decide which layer the law is about:

- **Recommended: the Bayan API.** That's what users rely on, and the C functions can be renamed freely. Prove it with Bayan test programs.

> **Statement:** After any sequence of `map[k] = v`, `remove`, `clear` and `slot(k)` on a `Map<K, V>`, iteration visits exactly the live keys, each once, in the order each was first inserted since it was last removed (an update keeps its position; a key re-inserted after removal goes to the end); `getOr(k, d)` returns the last value set for every live key and `d` otherwise; `map[k]` panics exactly for keys that aren't live; `length` equals the number of live keys.

### L004: union switches must be exhaustive. Enact after closing three gaps.

1. **Guards:** an arm with a `when` guard must not count as covering its variant. As written, `.B(var x) when x > 0` would wrongly satisfy the law.
2. **`or` patterns:** `.A or .C` should cover both variants.
3. **Pattern syntax:** the counterexample uses `.B(let x)`, but the spec's patterns use `var` (`Circle(var r)`). Either the implementation accepts `let` in patterns, which should be decided and added to the spec, or the law should say `var`.

Requiring nested subpatterns to be only bindings or `_` is conservative: it rejects some genuinely exhaustive nested matches. That's acceptable for now, but the law should say so, so that loosening it later is a deliberate revision.

> **Statement:** For every `union` and every `switch` statement or expression whose subject has that union type, BYN2410 is reported if and only if some variant is not covered, where a variant is covered by an arm with no `when` guard whose pattern (or one alternative of an `or` pattern) is that variant with only bindings or `_` as subpatterns, or by a default arm (`default:` or `_ =>`). When reported, BYN2410 names every uncovered variant. (Nested refutable subpatterns never count as covering; this is deliberately conservative.)

## Q55: copying a `Map`

**Recommendation: make it move-only, with an explicit copy.**

| Option | Problem |
|---|---|
| Implicit deep copy | A hidden allocation, which violates the first principle |
| Implicit shallow copy | Two values sharing one buffer, which means double frees and aliased mutation |
| **Move-only (`unique`) plus an explicit `.copy(to: alloc)`** | No hidden cost and no aliasing. It reuses the copying convention strings already have |

Two rules come with this:
1. **It applies to every allocator-backed collection** (`List`, `Map`, `HashMap`, `Set`), not just `Map`.
2. **A struct containing a `unique` field is itself `unique`.** This needs to be stated in the spec, because it affects ordinary code. In the tally example, `Ledger` contains a `List`, so `Ledger` becomes move-only automatically. Reading a collection without owning it goes through `ReadOnlySpan<T>` or a `ref`.

An attempted implicit copy is a compile error with fix-its: `move m`, `m.copy(to: alloc)`, or pass it by `ref`.

## Q56: `Io.run` and Windows batch files

**Recommendation: keep refusing by default, and add a narrow explicit path.**

The reason for refusing is real. On Windows, running a `.bat` or `.cmd` file goes through `cmd.exe`, which reparses the arguments with its own rules. That makes argument injection possible even when the caller escaped everything correctly. Rust's standard library had exactly this vulnerability ("BatBadBut", CVE-2024-24576) and fixed it by escaping carefully and refusing arguments it can't escape safely.

Bayan should follow that fix:
- `Io.run` refuses `.bat` and `.cmd` files, with an error explaining why and pointing to the explicit API.
- **`Io.runBatch(path, args)`** runs a batch file. It accepts only arguments it can escape safely for `cmd.exe` and **fails on any other argument**, rather than guessing.
- `Untrusted<T>` arguments are rejected by `runBatch` at compile time, consistent with shell commands being a taint sink.
- Add a law for it, for example: *"`runBatch` either passes each argument to the batch file unchanged, or fails without running anything."*
