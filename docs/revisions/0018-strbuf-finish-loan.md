# 0018 — `StrBuf.finish()` returns a loan on the buffer

Status: accepted (user, 2026-11-03, handoff Q745)

## Problem

§3.5 (`docs/bayan-spec.md:303`) says `buf.finish()` "consumes the buffer and returns it as a string without copying". Nothing implements consumption: the checker (`src/Bayan.Semantics/Binding/BuiltinMembers.cs:123-124`), the emitter (`src/Bayan.Backend.C/CEmitter.cs:2242-2243`) and the runtime (`src/runtime/bayan-rt.h:432`) return a view into the buffer's storage and leave the buffer usable. The Q745 region rule needs one meaning to check against.

## Decision

The spec follows the implementation.

- `finish()` returns the text as a loan on the buffer, without copying. The string borrows what the buffer borrows (its allocator, arena or stack storage) plus a loan on the buffer.
- The buffer stays usable after `finish()` and after `view`.
- `append` and `appendRepeated` keep text taken earlier valid. When the buffer grows, it moves to a fresh block and leaves the old one where it is (`src/runtime/bayan-rt.c:362`, `BYN_WRITE_GROW`). The checker relies on this, and the native test `RuntimeNativeTests.StrBufGrowthKeepsTheOldBlock` pins it.
- While a string from `view` or `finish()` is live:
  - refused: `clear` and `insert` (BYN3004), and `take` (BYN3006);
  - allowed: `append`, `appendRepeated`, `length`, `view`, `finish()`, `copy(to:)`, and assigning a new buffer to the variable.

  `StrBuf` has no `truncate`, `remove`, `replace`, length setter or index setter. Goldens: `diag/strbuf-finish-then-clear`, `diag/strbuf-loan-refusals`, `diag/strbuf-finish-non-local`, `run/strbuf-finish-then-append`, `run/strbuf-loan-safe-members`.
- A consuming `finish()` (`move this`), which hands the heap block to the caller so a function can return a built string without a copy, is a round-4 candidate (`docs/bayan-design-round-4-draft.md` §7).

## Sections

§3.5 (`StrBuf` bullet).

## Codes

None added, changed or retired. BYN3004 and BYN3006 already exist.

## Sūq files

None affected. `examples/tally-lite`, the Sūq stand-in, checks clean under the Q745 checker (`chore/worker-q745-csharp`, round 3).

## Effect on work already done

None in the code: the C# checker, the emitter and the runtime already behave this way. The selfhost checker takes the rule with the Q745 port.

## Diff

```diff
-… `buf.view` is a borrowed string view, no allocation; `buf.finish()` consumes the buffer and returns it as a string without copying. `StrBuf.over` is not in v0.1.
+… `buf.view` is a borrowed string view, no allocation; `buf.finish()` returns the text as a loan on the buffer, without copying (revision 0018). The buffer stays usable: `append` and `appendRepeated` keep text taken earlier valid, because growth moves to a fresh block and leaves the old one. While a string from `view` or `finish()` is live, `clear` and `insert` (BYN3004) and `take` (BYN3006) are refused. `StrBuf.over` is not in v0.1.
```
