# 008 — HashMap and Symbol interning
Status: done
Created: 2026-10-22
Revised: 2026-10-25 (revisions 0002–0011, specs 006–007; Questions renumbered 371–381; type-first declarations (J54) in every example; `Symbol.from` renamed `Symbol.intern`, since static functions are verbs (revision 0003 §2) and particles are not member names (BYN5009); round 3 complete, so the step-order dependencies are gone; 5.4's `tryGet(at:)` recorded in Q381; codes BYN2830–2832 confirmed free, no code changed; plan references re-located on feat/007-lambdas at cc96838)
Depends on: feat/007-lambdas at cc96838 (dev, round 3, spec 006 and 007 merged); merges into dev after 007. Round 3 is complete there (every step of `docs/plans/2026-10-22-002-design-round-3.md` §4 landed), and round 3 and its revisions 0002–0011 win on any conflict. Spec 006 (interfaces) is not a feature dependency: it built no hashing or equality interface (only `IEnumerator` and `IIndexable`), so 008 keeps `Map` keys a closed structural set (Q376). Spec 007 (lambdas) is not used: no 008 member takes a function; what a port of the symbol tables would want from 007's deferred parts is in the plan's Risks.

## Problem

The binder and emitter ports (specs 010–011) are built on symbol tables: scopes map names to locals, modules map names to types and functions, the lexer maps identifier text to keyword kinds, and the emitter maps symbols to C names. The C# compiler spells these `Dictionary<string, TypeSymbol>`, `Dictionary<string, …>` and `HashSet<string>` keyed by text, and `Dictionary<(…), …>` or reference-keyed tables elsewhere. Bayan has `Map<K, V>` (handoff decision 47, L003): an insertion-ordered hash map whose keys are integers, `rune`, `bool`, `string` and plain enums, hashed byte-wise with seeded FNV-1a. Three gaps remain. Every name lookup hashes and compares the whole text, though the compiler compares the same few thousand identifiers millions of times; spec §3.3 defines `Symbol` (an interned identifier, a 4-byte ID) for this, and nothing implements it. A key made of two parts (a scope and a name, an owner and a member) cannot be a `Map` key at all, because tuples and structs are not valid keys (BYN2760), and hashing a struct's raw bytes would hash its padding. And spec §3.5 lists `HashMap<K, V>` ("unordered and faster") beside `Map`, which the handoff's "Road to self-hosting" row 008 names, but an unordered map whose hash is seeded from the clock and the process id iterates differently on every run, which the finish line (stage 2 and stage 3 C byte-identical) and L009 (deterministic typed dump) cannot tolerate. The selfhost lexer shows the cost today: `Keywords` in `selfhost/lexer/tokens.byn` is a hand-built table of keyword ordinals bucketed by first letter, scanned linearly per identifier.

## Goals

- `Symbol` exists as spec §3.3 defines it: a 4-byte interned identifier, compared and hashed by ID, ordered by text, with string literals converted at compile time.
- Text read at run time (an identifier sliced from source) becomes a `Symbol` with one call, and equal texts always give the same `Symbol`.
- `Map` accepts `Symbol`, tuples and structs of valid keys as keys, with equality and hashing derived field by field.
- Every symbol-table shape the binder port needs (scopes, members by owner, keyword tables) is a `Map` with deterministic iteration.
- `HashMap` is a diagnostic with a fix to `Map`, not an unknown name and not a second, nondeterministic map.
- Every misuse is a diagnostic at the use, never a C compiler error.

## Non-goals

- A separate unordered `HashMap<K, V>` type (Q371). `Map` already hashes; insertion order costs one dense entry array, and determinism is worth more to self-hosting than the difference.
- User-defined hashing or equality: no `IHash`/`IEquatable` interface, no `T.Hasher`, no `@equality(by: …)` on keys, no constraint on a type parameter used as a key (Q376). Keys stay a closed structural set checked at instantiation, as today.
- Floats, optionals, unions, enums with data, arrays, spans, collections, `Owned` and `Untrusted` as keys, alone or inside a tuple or struct.
- `Set<T>`, `Deque<T>`, `Pool<T>` (spec §16 "Not in v0.1"); a set of keys is a `Map<K, bool>` until `Set` lands (Q378).
- Freeing interned text, symbol tables scoped to a value (several independent tables), and a symbol's numeric ID as a value (Q372, Q373).
- `const` arrays of `Symbol` and `Symbol` in `static` data beyond literals (Q379).
- Interning from several threads (no threads in v0.1, Q380).
- Changing the `Map` lookup members: 008 uses what round-3 step 5.4 shipped (`map[k]`, `tryGet(at:)`, `contains`, `slot`; Q381).

## User stories

- As the author of the Bayan binder port, I want a field `List<Map<Symbol, LocalId>> scopes` and to look a name up with `scope.contains(name)` and `scope[name]`, so that every lookup compares 4-byte IDs, not text.
- As the port author, I want `let name = Symbol.intern(source.text[start..end])` when the lexer finishes an identifier, so that every later phase carries a `Symbol` instead of a slice.
- As the port author, I want `let keywords: Map<Symbol, TokenKind> = ["let": .letKeyword, "var": .varKeyword]`, so that the keyword table is one literal, not a bucket scan.
- As the port author, I want a field `Map<MemberKey, FunctionId> members` keyed by a struct `MemberKey` with fields `TypeId owner` and `Symbol name`, so that a member lookup is one probe.
- As any Bayan user who writes `HashMap<string, i32>`, I want a fix to `Map<string, i32>` and the reason, so that I am not left with an unknown type.

## Requirements

- R1 The `Symbol` type. `Symbol` is a built-in type: a 4-byte interned identifier, copyable (not `unique`), not a borrow. `==` and `!=` compare IDs. `<`, `<=`, `>` and `>=` compare the texts by UTF-8 bytes, as for `string` (spec §3.3). `Symbol` is defaultable; its default is the symbol of `""`. It displays as `Symbol` in diagnostics and `bayan types`.
- R2 Literals. A string literal without interpolation (plain or raw) in a position whose expected type is `Symbol` (a typed `let`/`var`/`const`, an argument, a `return`, a field initializer, a collection or map literal element or key, the other operand of `==`, `!=` or an ordering against a `Symbol`) is a `Symbol` fixed at compile time. The program holds one table of the distinct literal texts in `.rodata`, sorted by UTF-8 bytes with `""` first; a literal's ID is its index there, so literal IDs depend only on the program text. Any other `string` expression where a `Symbol` is expected (a local, a parameter, a slice, an interpolated string, a call result) is BYN2832 with a fix to `Symbol.intern(…)`.
- R3 Interning at run time. `Symbol Symbol.intern(string text)` returns the symbol for `text`, adding it to the program's one symbol table if it is new; a text equal to a literal's gets the literal's symbol. A new text is copied into storage the runtime owns until the program exits, so the result never borrows `text` and `text` may be dropped or changed afterwards. Interning allocates, so `Symbol.intern` is `uses alloc` (round 3 §13, checked by the effect pass), and a panic handler that calls it gets the existing allocation restriction (BYN2612).
- R4 Text. `sym.text` (a `string`) is the symbol's text; it borrows nothing (the storage lives as long as a literal) and allocates nothing. Interpolation writes the text (`$"{sym}"`), and a `Map` holding symbols formats them as their texts. There is no other conversion: no cast between `Symbol` and `string` or any integer, and no ID member (Q372).
- R5 Key types. A `Map` key may be an integer type, `rune`, `bool`, `string`, a plain enum (as today), `Symbol`, a tuple whose elements are all valid keys, or a struct whose fields are all valid keys and which is not `@noEquality`. Two keys are equal when `==` says so (derived field by field, §6.7). A struct or tuple that is not a valid key names the first field or element that disqualifies it and that part's type (BYN2831); any other invalid key stays BYN2760, whose help now lists the new kinds.
- R6 Hashing. A key's hash depends only on its value: an integer, `rune`, `bool` or enum hashes its bytes, a `string` its text, a `Symbol` its ID, and a tuple or struct combines its parts' hashes in declaration order. Padding bytes never take part, so two `==` keys built in different ways are one entry. All hashing stays seeded FNV-1a (fixed seed under `--test`), and no output depends on the seed (L003 order is insertion order).
- R7 `Map` behaviour. With the new keys, every `Map` member and rule is unchanged: L003's insertion order, `map[k]` panicking on a missing key, `map[k] = v`, `ref map.slot(k)`, `contains`, `remove`, `clear`, `length`, `copy(to:)`, `take()`, `for (key, value) in m`, map literals (string literal keys converted per R2), structural modification while iterating (BYN3004), and the region rules for `string` keys, which apply to every `string` inside a composite key. A `Symbol` key or a composite key without a `string` borrows nothing.
- R8 `HashMap`. `HashMap<K, V>` is BYN2830 with a fix that rewrites `HashMap` to `Map` (type arguments kept), at every place a type is written; the type then resolves as `Map<K, V>` so one diagnostic is reported, not a cascade. `HashMap` with the wrong number of type arguments is BYN2830 and then the existing argument-count diagnostic for `Map`.
- R9 Generics. A type parameter used as a key is accepted in the template and checked at each instantiation (handoff decision 46); an instantiation whose key fails R5 is BYN2760 or BYN2831 at the use site, as today.
- R10 Runtime. The runtime (`bayan-rt.c/.h`) gains the symbol table (seeded open addressing over the text, entries in ID order) and its three entry points: start-up with the literal table, intern, and text by ID. The table and its text storage come from the runtime's general allocator, never `context.allocator`, are not tracked by the debug ownership checker (spec 003) and are never freed. A program that uses no `Symbol` emits and links none of it.
- R11 Diagnostics. Every new diagnostic has a code in 1000–3999, a page in `bayan explain`, a `diag/` golden and a help that starts with a verb from `HelpLanguageTests`' list (spec 005 R15, revision 0006). Every runtime behaviour (literal symbols, `Symbol.intern`, a literal and a runtime symbol comparing equal, ordering by text, `sym.text`, interpolation, a `Symbol`-keyed map, a tuple key, a struct key with padding, a map literal with symbol keys, `copy(to:)` of a symbol-keyed map) has a `run/` golden.
- R12 Self-hosting reach. A Bayan program in the shape of the binder port's symbol tables (identifiers sliced from a source string and interned with `Symbol.intern`, a keyword `Map<Symbol, TokenKind>` written as a literal, a scope chain `List<Map<Symbol, i32>>` searched innermost first, a `(i32 scope, Symbol name)` tuple key, a `MemberKey` struct key (fields `i32 owner` and `Symbol name`), and the declared names printed in declaration order) compiles and runs, as a `run/` golden, and `bayan types` on it is byte-identical across two runs.

## Acceptance criteria

- Given `let a: Symbol = "total"` and `let b = Symbol.intern(text)` where `text` is `"total"` read from a `StrBuf`, when `main` prints `a == b`, then it prints `true`, and `Symbol.intern` of `"totals"` compared with `a` prints `false`.
- Given `let x: Symbol = "b"` and `let y: Symbol = "a"`, when `main` prints `y < x` and `$"{x}{y}"`, then it prints `true` and `ba`, whatever order the two were interned in.
- Given `var buf = StrBuf(capacity: 16)` holding `"name"`, `let s = Symbol.intern(buf.view)`, then `buf.clear()` and `buf.append("other")`, when `main` prints `s.text`, then it prints `name` and no BYN3004 is reported.
- Given `let name: string = readName()` and `let s: Symbol = name`, then BYN2832 is reported at `name` with the fix `Symbol.intern(name)`; given `let s: Symbol = $"{prefix}x"`, then BYN2832 is reported the same way.
- Given `let kw: Map<Symbol, TokenKind> = ["let": .letKeyword, "var": .varKeyword]`, when `main` looks up `Symbol.intern(src[0..3])` for `src = "let x"`, then it finds `.letKeyword`, and iterating `kw` prints `let` then `var`.
- Given a struct `Pad` with fields `bool flag` and `i64 id`, and two `Pad` values built separately with equal fields, when both are used as keys of one `Map<Pad, i32>` (`m[p] = 1`, then `m[q] = 2`), then `m.length` is `1` and `m[p]` is `2`.
- Given `var m: Map<(i32 scope, Symbol name), i32>` with three entries inserted, one removed and re-inserted, then iteration follows L003 (the re-inserted entry last).
- Given `Map<Weight, i32>` where `struct Weight { f64 grams }`, then BYN2831 names `grams` and `f64`; given `Map<(i32, List<i32>), i32>`, then BYN2831 names element 2 and `List<i32>`; given `Map<f64, i32>`, then BYN2760 is reported with help listing `Symbol`, tuples and structs.
- Given `HashMap<string, i32> counts` as a field, then BYN2830 is reported at `HashMap` with the fix `Map<string, i32>`, no other diagnostic is reported, and after applying the fix the program runs.
- Given `V largest<K, V>(Map<K, V> m)` called with a `Map<Symbol, i32>` and with a `Map<Weight, i32>`, then the first is accepted and the second is BYN2831 at the call.
- Given a panic handler that calls `Symbol.intern(p.message)`, then the existing allocation diagnostic for panic handlers (BYN2612) is reported.
- Given a public function that calls `Symbol.intern` without `uses alloc`, then BYN6002 (`EffectNotDeclared`, round-3 plan J13) is reported; with `uses alloc` it is accepted, a function using only literal symbols and `sym.text` stays pure, and the entry function needs no `uses` (revision 0002 item 5).
- Given the R12 program, then it runs and prints the expected lines, and `bayan types` on it is byte-identical across two runs.

## Law candidates

- L015 (property): for every sequence of texts from the symbol generator (ASCII, multi-byte UTF-8, `""`, texts equal to the program's literals, prefixes and extensions of each other, texts built at run time from bytes), `Symbol.intern(a) == Symbol.intern(b)` exactly when `a == b`; `Symbol.intern(s).text == s`; a literal symbol equals `Symbol.intern` of the same text built at run time; `Symbol.intern(a) < Symbol.intern(b)` exactly when `a < b`; and the program's output is identical under two different hash seeds.
- L016 (property): for every key type from the key generator (tuples and structs mixing `bool`, `i8`, `i64`, `rune`, `string`, `Symbol`, plain enums and nested structs, in field orders that leave padding), two keys that are `==` but built separately are one entry (`length` counts them once and the last write wins), two keys that differ are two entries, and L003's model (MapModelTests) holds over these key types; every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone).

## Open questions

Each records the conservative choice 008 takes; the handoff gets them in T12.

- Q371 `HashMap` or `Map`: 008 extends `Map` (new key types, `Symbol`) and does not add `HashMap`; `HashMap<K, V>` is BYN2830 with a fix to `Map<K, V>`. Conservative: one map with deterministic iteration serves self-hosting (stage 2 and 3 byte-identical, L009); an unordered `HashMap` can be added later without breaking any program, while removing one could not.
- Q372 Symbol identity: one table per program run; IDs are not observable (no `id` member, no casts), so their values can change between versions; literal IDs are the sorted literal table's indexes. Conservative: nothing can depend on an ID.
- Q373 Interned storage: allocated from the runtime's general allocator, never from `context.allocator`, never freed; `Symbol.intern` is `uses alloc`. Conservative: a symbol is valid everywhere and forever, so it needs no region rule; freeing can be added with an explicit table type later.
- Q374 Symbol semantics: `==` by ID, ordering by text (spec §3.3), default the symbol of `""`. Conservative: matches the spec; the default lets `slot` and defaultable structs hold symbols.
- Q375 Conversions: only a non-interpolated string literal converts to `Symbol` implicitly; everything else uses `Symbol.intern`; `Symbol` to `string` is `sym.text`, never implicit. Conservative: interning, which allocates, is always visible.
- Q376 Key types: a closed structural set (R5); no user hashing or equality interface on spec 006, no `@equality(by:)` keys, no float keys (spec §17's `totalOrder` hashing waits). Conservative: every accepted key has one obvious equality; 006 as built adds no equality or hash interface, so this stands; an interface-based key can be added later without changing these.
- Q377 Composite hashing: field by field in declaration order, combined into the seeded FNV-1a state, never over raw struct bytes; `Symbol` hashes its ID. Conservative: correct for padding without layout assumptions; a faster hash is an internal change.
- Q378 `Set<T>`: stays out of v0.1 (spec §16); the port writes `Map<T, bool>`. Conservative: no new collection in a spec about keys.
- Q379 `const` arrays of `Symbol`: not in 008 (BYN2229 as today); keyword tables are map literals built at start-up. Conservative: static data stays the four kinds spec 004 allows.
- Q380 Threads: the symbol table is single-threaded; spec §13 revisits it when threads land. Conservative: v0.1 has no threads.
- Q381 `Map` lookup members: round-3 step 5.4 shipped `map.tryGet(at: k) ?? fallback` in place of `getOr` (J24, spec §16); 008 adds no lookup member, the existing ones work unchanged on the new key types, and its examples use `map[k]`, `contains` and `slot`. Conservative: 008 does not reopen a round-3 decision.
