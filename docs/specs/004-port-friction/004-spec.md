# 004 — Port friction: argument names, contextual keywords, literal inference, borrows of own fields, decode, `catch { }`, string arrays, `removeRange`, `toHex`
Status: draft
Created: 2026-10-21

## Problem

Porting the lexer to Bayan (spec 001, T19) needed no compiler changes, but a handful of rules cost the most effort, and the parser, binder and emitter ports (specs 009–011) are each several times the lexer's size. The handoff's *Friction found porting the lexer* list names them:
- The must-name rule (BYN2305) fired about 100 times, even when the argument already had the parameter's name.
- `error` and `flags` can't be used as names.
- `var x = 0` is `i32` while `.length` is `i64`, so every position needed an explicit `i64`.
- A method can't return a span borrowed from its own `List` field.
- There is no bytes→string decode, so the lexer appended `$"{(char)cp}"` per scalar.
- The language lacks `catch { block }`, `const string[]`, `List.removeRange` and a hex conversion.

The decisions for each are recorded in the handoff's *Road to self-hosting*; this spec states them as requirements.

## Goals

- Every item of the friction list that the road assigns to this spec works, with diagnostics and goldens.
- The type the binder gives each local is visible as a stable, documented dump, ready for spec 010's binder-equivalence law.
- The self-hosted lexer stays equivalent to the C# lexer (L001) after the keyword change.

## Non-goals

- Format specifiers (`{x:x8}`) stay deferred (Appendix B). `toHex()` is a method, not a specifier.
- Span or borrowed struct fields (BYN3001) and full regions (§9.4): deferred past bootstrap.
- `return sb.asString()` from a local `StrBuf` and `string f(span) => utf8(span)` being read as a borrow: recorded as handoff Questions, unchanged here.
- Ref-aliasing rule changes (BYN3005).
- `bayan fmt` and `fmt --explicit` (a handoff Question records that `--explicit` shows inferred literal types once `fmt` exists).
- Delegates, interfaces (specs 006, 007).

## User stories

- As someone porting the compiler, I want `fromBounds(start, end)` to compile when the parameters are named `start` and `end`, so I name arguments only where the name adds something.
- As someone porting the compiler, I want a call whose arguments swap two same-named parameters to be an error, so implied names can't hide a swap.
- As a Bayan programmer, I want `error`, `flags` and the other declaration-only keywords usable as identifiers.
- As a Bayan programmer, I want `var i = 0; while (i < xs.length) …` to make `i` an `i64` without writing the type.
- As a compiler maintainer, I want a typed dump of every local's resolved type, so two binders can be compared output for output.
- As someone porting the compiler, I want a method to return a span of its own `List` field, the way `tokens()` does in the C# code.
- As a Bayan programmer, I want to decode a byte span to a string, with an error naming where the bytes stop being UTF-8.
- As a Bayan programmer, I want `f() catch { … }` with a block, `const string[]` tables, `list.removeRange(a, b)` and `n.toHex()`.

## Requirements

- R1 An argument with no name counts as named when it is an identifier equal to its parameter's name, or a member access whose last segment equals it (`tok.start` for `start`). BYN2305 no longer fires for such arguments.
- R2 A call whose unnamed arguments carry implied names (R1) that match parameters in a different order than the arguments' positions is a compile error with its own diagnostic, naming the two parameters. It has a `diag/` golden.
- R3 The declaration-only keywords are contextual: `error`, `flags`, `unit`, `distinct`, `open`, `extensible`, `set`, `type`, `law`, `derivation`, `attribute`, `embed` can be used as identifiers (locals, parameters, fields, functions, members) wherever the grammar does not need them as keywords, and every existing program keeps its meaning. Control-flow and expression keywords stay reserved. The C# lexer and `selfhost/lexer/` change together, and L001 holds.
- R4 An untyped integer literal initializing an unannotated `var` takes its type from the variable's first constraining use in the same function: an assignment, comparison, arithmetic, or argument involving a typed value. Unconstrained, it is `i32`, as today. A literal that doesn't fit the inferred type is the existing out-of-range diagnostic at the literal. The spec text (§ on literals) is updated.
- R5 A typed dump of bound declarations records each local's (and parameter's) resolved type. It has a documented JSON form (a `docs/` page) and is stable and deterministic:
  - entries in declaration order, then by source position;
  - no addresses, hash-order output or run-specific IDs;
  - symbols named by path and span.
  
  Goldens cover a literal constrained to `i64`, an unconstrained `i32`, and a literal constrained by a comparison, by an argument and by an assignment.
- R6 A method may return a borrow (a span, slice or `string` view) of storage owned by its own receiver's fields. At the call site the result borrows the receiver: the existing region rules apply to it as to any borrow of the receiver (it may not outlive the receiver, and the receiver may not be modified while it is alive). Returning a borrow of a local's storage stays BYN3003.
- R7 A fallible decode turns a byte span into a `string`:
  - It fails with an error naming the byte offset of the first invalid UTF-8 sequence.
  - On success, the result does not borrow the argument.
  - An empty span decodes to the empty string. Overlong forms, surrogates and code points above U+10FFFF are invalid.
- R8 `f() catch { block }` and `f() catch (e) { block }` run the block when `f` fails. For a call of type `void`, the block may complete normally. Otherwise the block must not complete normally (it ends in `return`, `break`, `continue`, a panic or another `never` expression), and a block that can complete normally is a compile error. Flow and ownership analysis see the block as one path.
- R9 `const` arrays of `string` are accepted wherever `const` arrays of numbers are, with literal elements; they can be indexed, iterated and given a `.length`.
- R10 `List.removeRange(start, end)` removes the elements in `[start, end)`, keeps the order of the rest, and panics on `start > end` or `end > length`. It is a structural modification: it gets BYN3004 under the same conditions as `remove`.
- R11 `toHex()` on every integer type returns a lowercase hexadecimal `string` with no prefix and no leading zeros (`0` gives `"0"`). A negative value prints its two's-complement bits at the type's width.
- R12 Every new diagnostic has a `docs/diagnostics/BYN####.md` page and a `diag/` golden; every runtime feature has a `run/` golden.

## Acceptance criteria

- Given `Span fromBounds(i64 start, i64 end)` and locals `start`, `end`, `fromBounds(start, end)` compiles; `fromBounds(end, start)` fails with the R2 diagnostic naming `start` and `end`; `fromBounds(tok.start, tok.end)` compiles.
- Given `let error = 3; struct S { i64 flags; }`, the program compiles. `error set E { … }` still declares an error set. `bayan lex` output for the new forms is identical between the C# and Bayan lexers.
- Given `var i = 0; while (i < xs.length) { i += 1; }`, the typed dump shows `i: i64`; given `var n = 0; n += 1;`, it shows `n: i32`. Two runs of the dump on the same input are byte-identical.
- Given `struct Lexer { List<Token> toks; Span<Token> tokens() => toks.asSpan(); }`, it compiles. A caller that keeps `tokens()` alive across a `lexer.toks.add(…)` gets BYN3004.
- Given bytes `[0x68, 0x69]`, decode returns `"hi"`; given `[0x68, 0xC0, 0x80]`, it fails naming offset 1.
- Given `let n = parse(s) catch { return -1; };`, it compiles and returns `-1` on failure; given `let n = parse(s) catch { log("x"); };`, compile fails with the R8 diagnostic.
- Given `const string[] NAMES = ["a", "b"];`, `NAMES[1]` is `"b"` and `NAMES.length` is 2.
- Given `[1,2,3,4,5]`, `removeRange(1, 3)` leaves `[1,4,5]`; `removeRange(3, 2)` panics.
- `255.toHex()` is `"ff"`; `(0 as i64).toHex()` is `"0"`; `(-1 as i8).toHex()` is `"ff"`.

## Law candidates

- L009 (property): for every program, the typed dump is byte-identical across two runs, and across a run on a copy of the program in another directory with the same relative path (R5). Spec 010 builds the binder-equivalence law on it.
- L010 (property): for every byte sequence, the decode (R7) succeeds exactly when the sequence is valid UTF-8, and then encoding the result gives back the input; on failure, the offset it names is where the first invalid sequence starts.

## Open questions

None blocking. Recorded as handoff Questions when the plan lands, with the conservative option implemented:
- The decode's name and error type: chosen in the plan, consistent with §15 (`Io`) naming.
- R2's swapped-names diagnostic fires only when every implied name matches some parameter. A mix of implied names and other arguments falls back to BYN2305 for the others.
