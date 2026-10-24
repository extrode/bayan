# Sūq-0 goldens: Part D report

The brief is `docs/suq-agent-brief.md` (Sūq → Bayan, Part D). Goldens are single-file, so `suq-quote.byn`, `suq-order.byn` and `suq-money.test.byn` carry a copy of the `Currency`/`code`/`PricingError`/`maxCents`/`Money` declarations from `suq-money.byn` (without its top-level statements), marked by a comment.

## Pass/fail

All at HEAD of this branch.

| File | Result |
|---|---|
| `tests/cases/run/suq-money.byn` | pass |
| `tests/cases/run/suq-money.test.byn` | pass: 10 tests (four `format`, four rounding, two single tests); expected output `suq-money.test.stdout` in the `bayan test -v` form the other test-file goldens use |
| `tests/cases/run/suq-quote.byn` | pass |
| `tests/cases/run/suq-packing.byn` | pass |
| `tests/cases/run/suq-order.byn` | pass |
| `tests/cases/run/defer-modifier-registration.byn` | pending until J20 (prints only `body`; expected `body`, `cleanup`) |

## D3: syntax-change log

Line numbers are in the committed golden files.

| File | Line | Written | Needed | Why |
|---|---|---|---|---|
| suq-money | 56–65 | `void tryAdd` with `catch (e) { let name = e switch {…}; print(…); return; }` | `string addText(a, b)` whose `catch switch { PricingError e => return e switch {…} }` returns the name, and `tryAdd` printing `$"add: {addText(a, b)}"` | `catch (e)` is BYN1025; a `catch switch` arm cannot hold a block, so print-then-return needs a helper (D4, Q191) |
| suq-quote | 202–206 | `tea.asSpan` (and four more) | `tea.span` | the round-2 span member was a method; without parentheses it crashed the compiler (D2) |
| suq-packing | 17–21 | `const BoxSize[3] boxSizes = [BoxSize {…}, …];` | `BoxSize boxSizes(i64 i) => i switch { 0 => …, 1 => …, _ => … };` | module-level `const` arrays hold only numbers, `bool`, `char`, `string` (BYN2229) (D4, Q192) |
| suq-packing | 31, 62, 69, 116 | `boxSizes[i]`, `boxSizes[boxKind[b]]`, `boxSizes[size]` | `boxSizes(i)`, `boxSizes(boxKind[b])`, `boxSizes(size)` | follows the workaround above |
| suq-packing | 41, 81 | `order[i] = i;` | `order[i] = (i32)i;` | `for i in 0..n` with `n = items.length` gives an `i64` index; storing it in `Span<i32>` needs a cast (BYN2202) |
| suq-packing | 86 | `comesAfter(items[…], items[current])` | `comesAfter(a: items[…], b: items[current])` | several same-typed parameters need named arguments (BYN2305) |
| suq-packing | 110 | `catch (e) {` | `catch {` | BYN1025; `e` was unused |
| suq-packing | 129 | `sample.add(…)` | `sample.append(…)` | `add` is now `append` (BYN2230) |
| suq-packing | 132–134, 137 | `.asSpan` | `.span` | as in suq-quote |
| suq-packing | 143 | `first.asString()` | `first.finish()` | `asString()` is now `finish()` (BYN2230) |
| suq-order | 127 | `lines.add(line);` | `lines.append(line);` | BYN2230 |
| suq-order | 131, 165 | `return .wrongState unless state is .draft \|\| state is .placed;` | `return .wrongState if !(state is .draft \|\| state is .placed);` | `unless` with `\|\|` is BYN1022 |
| suq-order | 160 | `unless state is .paid;` | `unless state is .paid(_);` | `.paid` carries a payload, so the pattern needs one (BYN2705) |
| suq-order | 191, 196, 203, 207, 208, 215 | `x catch (e) { print(…errorName(e)…); };` | `x catch switch { OrderError e => print(…errorName(e)…) };` | BYN1025 |

`suq-money.test.byn` needed no change beyond the copied declarations. No logic changed.

## D2: `.stdout` diffs

No `.stdout` diff once each file compiled. One compiler bug was found and fixed on the way:

- **`.asSpan` without parentheses crashed the compiler.** `bayan run suq-quote.byn` threw `InvalidOperationException: cannot lower BoundBuiltinGroup` in `Lowerer.LowerExpr`. Cause: a method named without its call (`xs.asSpan`, also a user method such as `box.weight`) bound to a method group with the error type, so every type check stayed silent and lowering met it. Fix: `BindExpr` (`src/Bayan.Semantics/Binding/BinderExpressions.cs`) now reports BYN2019 (a method used as a value, with help to call it) for both group kinds (round 3 makes the view the `span` property); `BindOnPanic` (`Binder.cs`) still accepts a named function. Regression golden: `tests/cases/diag/method-group-not-a-value.{byn,diag}`; before the fix `bayan check` on it reported no diagnostics, so the golden fails when the fix is reverted. `docs/diagnostics/BYN2019.md` mentions the method case.

## D4: workarounds and Questions

Highest existing Question: Q190 (specs 012 in `feat-009-selfhost-parser`). Opened (not added to the handoff):

- **Q191. Handling a caught error with statements.** `catch switch` arms are expressions and `catch { … }` has no binding, so "print the error's name, then return" from a `void` function needs a helper returning the text. Options: block arms (`E e => { …; return; }`), or a binding on `catch { … }` typed by the call's error set. Conservative choice meanwhile: helper functions. Belongs in SPEC §8.
- **Q192. Module-level `const` arrays of structs.** `const BoxSize[3] boxSizes = [BoxSize {…}, …]` is BYN2229 (Q51 allows numbers, `bool`, `char`, `string`). Workaround: a function with a `switch` over the index. Belongs in SPEC §2 (static data), extending Q51 to structs whose fields are all static-data types.

`stackalloc i64[64]` and `return` arms in `catch switch` already work; no workaround needed.

## D5: zero-allocation test

Not added: the suite has no allocation-counting harness for compiled Bayan programs. Searched `tests/Bayan.Tests/*.cs` for `alloc` (only `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray`, which measures the C# lexer with `GC.GetAllocatedBytesForCurrentThread`) and `src/runtime/bayan-rt.{h,c}` for an allocator counter or debug hook (only `byn_general`, `byn_context` and the arena; nothing counts). `noalloc` is a keyword in `Keywords.cs` only, with no checker. In the slice, `pack` takes only spans and calls no allocating function.

## D6, D7

- No functions renamed. The `addText` and `boxSizes(i)` helpers are additions (D4).
- `defer-modifier-registration.byn` with `.stdout` `body`, `cleanup` and a `.pending` marker naming J20. The golden harness had no pending mechanism, so `RunTests.RunGolden` now reads `<case>.pending`: the output must still differ (when it matches, the test fails saying to delete the marker), and `BAYAN_UPDATE=1` leaves a pending case's expected files alone. Documented in `tests/cases/README.md` and the `RunTests` summary.

## Reference app comparison

`suq/src/warehouse/packing.byn.cs` (the updated reference) has these fixes that the brief's `suq-packing` slice lacks:

- `computeCheckDigit` hashes `text.bytes`; the slice iterates `for c in text` and casts `(u32)c`, which matches only for ASCII.
- `measureRoom` clamps a box's remaining room with `max(capacity - load, 0g)` and notes that `-|` on a signed type saturates at the minimum, not zero; the slice has no room measure and its last line shows that `-|` result (`-150`).
- Storage is `FixedList` with a capacity, so more items than storage fail with `CapacityError`; the slice's fixed 64-element `stackalloc` spans panic on an index past 64.
- `tooHeavy` carries `sku` and `weight`; the slice's `PackError.tooHeavy` has no payload.
- `boxOf` gets an entry for every item before packing; the slice writes only the entries it packs.

## Checks

- `dotnet build -v q`: 0 warnings, 0 errors.
- `dotnet test -v q --filter "FullyQualifiedName!~SelfhostLexerTests"`: 1324 of 1325 passed. The one failure was `ToolchainSanitizerTests.ProbeKillsAndRejectsACandidateThatHangsAndTakesTheNextOne` (a timing test, under full-suite load); it passes when rerun alone (4 of 4 in `ToolchainSanitizerTests`).
- Laws: L003–L011 hold (`--filter Law=L003|…|Law=L011`, 344 passed); L001/L002 not run (time limit).
