# 008 — HashMap and Symbol interning
Status: draft
Created: 2026-10-22
Depends on: design round 3 plan (`docs/plans/2026-10-22-001-design-round-3.md` §4) steps 5.1a–5.1d (parser: no semicolons, name-first bindings and fields, `->` returns, `@` attributes, `char` → `rune`, `uses`, particles) land before 008 T1, and round 3 wins on any conflict; step 5.4 (stdlib particle sweep, J24, which rewrites the `Map` members in `BuiltinMembers.cs`) lands before 008 T4; the `uses alloc` half of R3 needs step 5.3d's effect pass and lands with whichever of 008 T5 and 5.3d comes second. Spec 006 (interfaces) is not a feature dependency: 008 adds no hashing or equality interface and keeps `Map` keys a closed structural set (Q137); 006 and 008 touch the same files (see the plan). Spec 007 (lambdas) is not used: no 008 member takes a function.

## Problem

The binder and emitter ports (specs 010–011) are built on symbol tables: scopes map names to locals, modules map names to types and functions, the lexer maps identifier text to keyword kinds, and the emitter maps symbols to C names. The C# compiler spells these `Dictionary<string, TypeSymbol>` (14 sites), `Dictionary<string, …>` and `HashSet<string>` keyed by text, and `Dictionary<(…), …>` or reference-keyed tables elsewhere. Bayan has `Map<K, V>` (handoff decision 47, L003): an insertion-ordered hash map whose keys are integers, `rune`, `bool`, `string` and plain enums, hashed byte-wise with seeded FNV-1a. Three gaps remain. Every name lookup hashes and compares the whole text, though the compiler compares the same few thousand identifiers millions of times; spec §3.3 defines `Symbol` (an interned identifier, a 4-byte ID) for this, and nothing implements it. A key made of two parts (a scope and a name, an owner and a member) cannot be a `Map` key at all, because tuples and structs are not valid keys (BYN2760), and hashing a struct's raw bytes would hash its padding. And spec §3.5 lists `HashMap<K, V>` ("unordered and faster") beside `Map`, which the handoff's "Road to self-hosting" row 008 names, but an unordered map whose hash is seeded from the clock and the process id iterates differently on every run, which the finish line (stage 2 and stage 3 C byte-identical) and L009 (deterministic typed dump) cannot tolerate. The selfhost lexer shows the cost today: `Keywords` in `selfhost/lexer/tokens.byn` is a hand-built table of keyword ordinals bucketed by first letter, scanned linearly per identifier.

## Goals

- `Symbol` exists as spec §3.3 defines it: a 4-byte interned identifier, compared and hashed by ID, ordered by text, with string literals converted at compile time.
- Text read at run time (an identifier sliced from source) becomes a `Symbol` with one call, and equal texts always give the same `Symbol`.
- `Map` accepts `Symbol`, tuples and structs of valid keys as keys, with equality and hashing derived field by field.
- Every symbol-table shape the binder port needs (scopes, members by owner, keyword tables) is a `Map` with deterministic iteration.
- `HashMap` is a diagnostic with a fix to `Map`, not an unknown name and not a second, nondeterministic map.
- Every misuse is a diagnostic at the use, never a C compiler error.

## Non-goals

- A separate unordered `HashMap<K, V>` type (Q132). `Map` already hashes; insertion order costs one dense entry array, and determinism is worth more to self-hosting than the difference.
- User-defined hashing or equality: no `IHash`/`IEquatable` interface, no `T.Hasher`, no `@equality(by: …)` on keys, no constraint on a type parameter used as a key (Q137). Keys stay a closed structural set checked at instantiation, as today.
- Floats, optionals, unions, enums with data, arrays, spans, collections, `Owned` and `Untrusted` as keys, alone or inside a tuple or struct.
- `Set<T>`, `Deque<T>`, `Pool<T>` (spec §16 "Not in v0.1"); a set of keys is a `Map<K, bool>` until `Set` lands (Q139).
- Freeing interned text, symbol tables scoped to a value (several independent tables), and a symbol's numeric ID as a value (Q133, Q134).
- `const` arrays of `Symbol` and `Symbol` in `static` data beyond literals (Q140).
- Interning from several threads (no threads in v0.1, Q141).
- Changing the `Map` lookup members: 008 uses what round-3 step 5.4 ships (Q142).

## User stories

- As the author of the Bayan binder port, I want `scopes: List<Map<Symbol, LocalId>>` and to look a name up with `scope.contains(name)` and `scope[name]`, so that every lookup compares 4-byte IDs, not text.
- As the port author, I want `let name = Symbol.from(source.text[start..end])` when the lexer finishes an identifier, so that every later phase carries a `Symbol` instead of a slice.
- As the port author, I want `keywords: Map<Symbol, TokenKind> = ["let": .letKeyword, "var": .varKeyword]`, so that the keyword table is one literal, not a bucket scan.
- As the port author, I want `members: Map<MemberKey, FunctionId>` keyed by `struct MemberKey { owner: TypeId, name: Symbol }`, so that a member lookup is one probe.
- As any Bayan user who writes `HashMap<string, i32>`, I want a fix to `Map<string, i32>` and the reason, so that I am not left with an unknown type.

## Requirements

- R1 The `Symbol` type. `Symbol` is a built-in type: a 4-byte interned identifier, copyable (not `unique`), not a borrow. `==` and `!=` compare IDs. `<`, `<=`, `>` and `>=` compare the texts by UTF-8 bytes, as for `string` (spec §3.3). `Symbol` is defaultable; its default is the symbol of `""`. It displays as `Symbol` in diagnostics and `bayan types`.
- R2 Literals. A string literal without interpolation (plain or raw) in a position whose expected type is `Symbol` (a typed `let`/`var`/`const`, an argument, a `return`, a field initializer, a collection or map literal element or key, the other operand of `==`, `!=` or an ordering against a `Symbol`) is a `Symbol` fixed at compile time. The program holds one table of the distinct literal texts in `.rodata`, sorted by UTF-8 bytes with `""` first; a literal's ID is its index there, so literal IDs depend only on the program text. Any other `string` expression where a `Symbol` is expected (a local, a parameter, a slice, an interpolated string, a call result) is BYN2832 with a fix to `Symbol.from(…)`.
- R3 Interning at run time. `Symbol.from(text: string) -> Symbol` returns the symbol for `text`, adding it to the program's one symbol table if it is new; a text equal to a literal's gets the literal's symbol. A new text is copied into storage the runtime owns until the program exits, so the result never borrows `text` and `text` may be dropped or changed afterwards. Interning allocates, so `Symbol.from` is `uses alloc` (round 3 §13) once step 5.3d exists, and a panic handler that calls it gets the existing allocation restriction.
- R4 Text. `sym.text: string` is the symbol's text; it borrows nothing (the storage lives as long as a literal) and allocates nothing. Interpolation writes the text (`$"{sym}"`), and a `Map` holding symbols formats them as their texts. There is no other conversion: no cast between `Symbol` and `string` or any integer, and no ID member (Q133).
- R5 Key types. A `Map` key may be an integer type, `rune`, `bool`, `string`, a plain enum (as today), `Symbol`, a tuple whose elements are all valid keys, or a struct whose fields are all valid keys and which is not `@noEquality`. Two keys are equal when `==` says so (derived field by field, §6.7). A struct or tuple that is not a valid key names the first field or element that disqualifies it and that part's type (BYN2831); any other invalid key stays BYN2760, whose help now lists the new kinds.
- R6 Hashing. A key's hash depends only on its value: an integer, `rune`, `bool` or enum hashes its bytes, a `string` its text, a `Symbol` its ID, and a tuple or struct combines its parts' hashes in declaration order. Padding bytes never take part, so two `==` keys built in different ways are one entry. All hashing stays seeded FNV-1a (fixed seed under `--test`), and no output depends on the seed (L003 order is insertion order).
- R7 `Map` behaviour. With the new keys, every `Map` member and rule is unchanged: L003's insertion order, `map[k]` panicking on a missing key, `map[k] = v`, `ref map.slot(k)`, `contains`, `remove`, `clear`, `length`, `copy(to:)`, `take()`, `for (key, value) in m`, map literals (string literal keys converted per R2), structural modification while iterating (BYN3004), and the region rules for `string` keys, which apply to every `string` inside a composite key. A `Symbol` key or a composite key without a `string` borrows nothing.
- R8 `HashMap`. `HashMap<K, V>` is BYN2830 with a fix that rewrites `HashMap` to `Map` (type arguments kept), at every place a type is written; the type then resolves as `Map<K, V>` so one diagnostic is reported, not a cascade. `HashMap` with the wrong number of type arguments is BYN2830 and then the existing argument-count diagnostic for `Map`.
- R9 Generics. A type parameter used as a key is accepted in the template and checked at each instantiation (handoff decision 46); an instantiation whose key fails R5 is BYN2760 or BYN2831 at the use site, as today.
- R10 Runtime. The runtime (`bayan-rt.c/.h`) gains the symbol table (seeded open addressing over the text, entries in ID order) and its three entry points: start-up with the literal table, intern, and text by ID. The table and its text storage come from the runtime's general allocator, never `context.allocator`, are not tracked by the debug ownership checker (spec 003) and are never freed. A program that uses no `Symbol` emits and links none of it.
- R11 Diagnostics. Every new diagnostic has a code in 1000–3999, a page in `bayan explain`, a `diag/` golden and a verb-first help (spec 005 R15). Every runtime behaviour (literal symbols, `Symbol.from`, a literal and a runtime symbol comparing equal, ordering by text, `sym.text`, interpolation, a `Symbol`-keyed map, a tuple key, a struct key with padding, a map literal with symbol keys, `copy(to:)` of a symbol-keyed map) has a `run/` golden.
- R12 Self-hosting reach. A Bayan program in the shape of the binder port's symbol tables (identifiers sliced from a source string and interned with `Symbol.from`, a keyword `Map<Symbol, TokenKind>` written as a literal, a scope chain `List<Map<Symbol, i32>>` searched innermost first, a `(scope: i32, name: Symbol)` tuple key, a `MemberKey { owner: i32, name: Symbol }` struct key, and the declared names printed in declaration order) compiles and runs, as a `run/` golden, and `bayan types` on it is byte-identical across two runs.

## Acceptance criteria

- Given `let a: Symbol = "total"` and `let b = Symbol.from(text)` where `text` is `"total"` read from a `StrBuf`, when `main` prints `a == b`, then it prints `true`, and `Symbol.from` of `"totals"` compared with `a` prints `false`.
- Given `let x: Symbol = "b"` and `let y: Symbol = "a"`, when `main` prints `y < x` and `$"{x}{y}"`, then it prints `true` and `ba`, whatever order the two were interned in.
- Given `var buf = StrBuf(capacity: 16)` holding `"name"`, `let s = Symbol.from(buf.view)`, then `buf.clear()` and `buf.append("other")`, when `main` prints `s.text`, then it prints `name` and no BYN3004 is reported.
- Given `let name: string = readName()` and `let s: Symbol = name`, then BYN2832 is reported at `name` with the fix `Symbol.from(name)`; given `let s: Symbol = $"{prefix}x"`, then BYN2832 is reported the same way.
- Given `let kw: Map<Symbol, TokenKind> = ["let": .letKeyword, "var": .varKeyword]`, when `main` looks up `Symbol.from(src[0..3])` for `src = "let x"`, then it finds `.letKeyword`, and iterating `kw` prints `let` then `var`.
- Given `struct Pad { flag: bool, id: i64 }` and two `Pad` values built separately with equal fields, when both are used as keys of one `Map<Pad, i32>` (`m[p] = 1`, then `m[q] = 2`), then `m.length` is `1` and `m[p]` is `2`.
- Given `m: Map<(scope: i32, name: Symbol), i32>` with three entries inserted, one removed and re-inserted, then iteration follows L003 (the re-inserted entry last).
- Given `Map<Weight, i32>` where `struct Weight { grams: f64 }`, then BYN2831 names `grams` and `f64`; given `Map<(i32, List<i32>), i32>`, then BYN2831 names element 2 and `List<i32>`; given `Map<f64, i32>`, then BYN2760 is reported with help listing `Symbol`, tuples and structs.
- Given `counts: HashMap<string, i32>` as a field, then BYN2830 is reported at `HashMap` with the fix `Map<string, i32>`, no other diagnostic is reported, and after applying the fix the program runs.
- Given `largest<K, V>(m: Map<K, V>) -> …` called with a `Map<Symbol, i32>` and with a `Map<Weight, i32>`, then the first is accepted and the second is BYN2831 at the call.
- Given a panic handler that calls `Symbol.from(p.message)`, then the existing allocation diagnostic for panic handlers is reported.
- Given round-3 step 5.3d has landed and a public function that calls `Symbol.from` without `uses alloc`, then BYN6002 (`EffectNotDeclared`, round-3 plan J13) is reported; with `uses alloc` it is accepted, and a function using only literal symbols and `sym.text` stays pure.
- Given the R12 program, then it runs and prints the expected lines, and `bayan types` on it is byte-identical across two runs.

## Law candidates

- L015 (property): for every sequence of texts from the symbol generator (ASCII, multi-byte UTF-8, `""`, texts equal to the program's literals, prefixes and extensions of each other, texts built at run time from bytes), `Symbol.from(a) == Symbol.from(b)` exactly when `a == b`; `Symbol.from(s).text == s`; a literal symbol equals `Symbol.from` of the same text built at run time; `Symbol.from(a) < Symbol.from(b)` exactly when `a < b`; and the program's output is identical under two different hash seeds.
- L016 (property): for every key type from the key generator (tuples and structs mixing `bool`, `i8`, `i64`, `rune`, `string`, `Symbol`, plain enums and nested structs, in field orders that leave padding), two keys that are `==` but built separately are one entry (`length` counts them once and the last write wins), two keys that differ are two entries, and L003's model (MapModelTests) holds over these key types; every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone).

## Open questions

Each records the conservative choice 008 takes; the handoff gets them in T12.

- Q132 `HashMap` or `Map`: 008 extends `Map` (new key types, `Symbol`) and does not add `HashMap`; `HashMap<K, V>` is BYN2830 with a fix to `Map<K, V>`. Conservative: one map with deterministic iteration serves self-hosting (stage 2 and 3 byte-identical, L009); an unordered `HashMap` can be added later without breaking any program, while removing one could not.
- Q133 Symbol identity: one table per program run; IDs are not observable (no `id` member, no casts), so their values can change between versions; literal IDs are the sorted literal table's indexes. Conservative: nothing can depend on an ID.
- Q134 Interned storage: allocated from the runtime's general allocator, never from `context.allocator`, never freed; `Symbol.from` is `uses alloc`. Conservative: a symbol is valid everywhere and forever, so it needs no region rule; freeing can be added with an explicit table type later.
- Q135 Symbol semantics: `==` by ID, ordering by text (spec §3.3), default the symbol of `""`. Conservative: matches the spec; the default lets `slot` and defaultable structs hold symbols.
- Q136 Conversions: only a non-interpolated string literal converts to `Symbol` implicitly; everything else uses `Symbol.from`; `Symbol` to `string` is `sym.text`, never implicit. Conservative: interning, which allocates, is always visible.
- Q137 Key types: a closed structural set (R5); no user hashing or equality interface on spec 006, no `@equality(by:)` keys, no float keys (spec §17's `totalOrder` hashing waits). Conservative: every accepted key has one obvious equality; 006 as built adds no equality or hash interface, so this stands; an interface-based key can be added later without changing these.
- Q138 Composite hashing: field by field in declaration order, combined into the seeded FNV-1a state, never over raw struct bytes; `Symbol` hashes its ID. Conservative: correct for padding without layout assumptions; a faster hash is an internal change.
- Q139 `Set<T>`: stays out of v0.1 (spec §16); the port writes `Map<T, bool>`. Conservative: no new collection in a spec about keys.
- Q140 `const` arrays of `Symbol`: not in 008 (BYN2229 as today); keyword tables are map literals built at start-up. Conservative: static data stays the four kinds spec 004 allows.
- Q141 Threads: the symbol table is single-threaded; spec §13 revisits it when threads land. Conservative: v0.1 has no threads.
- Q142 `Map` lookup members: spec §16 on this branch says `getOr(k, by: fallback)`, the round-3 plan (J24, step 5.4) says `at(k) ?? fallback`; 008 adds no lookup member and uses whatever 5.4 ships, and its examples use `map[k]`, `contains` and `slot` only. Conservative: 008 does not decide a round-3 question.
