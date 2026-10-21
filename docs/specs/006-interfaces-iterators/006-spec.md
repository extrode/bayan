# 006 — Interfaces and iterators
Status: draft
Created: 2026-10-21
Revised: 2026-10-22 for design round 2 (HANDOFF §1, §4, §9, §11)
Depends on: design-round-2 steps 3.1–3.3 on dev (`for x in` parsing from 3.1, `ref this` parsing and declared mutability from 3.3); 006 implementation starts after they land.

## Problem

The parser, binder and emitter are next to be ported to Bayan (specs 009–011), and they are written around abstractions Bayan does not have yet. Syntax nodes are visited through a common shape, symbol tables are walked, and collections of nodes are iterated in loops. Today Bayan has generics without constraints, no interfaces, and `for … in` only over built-in types (ranges, spans, lists, maps, arrays, strings). The C# compiler expresses these through interfaces and enumerators, so the port either hand-expands every visit into a `switch` and every loop into index arithmetic, or waits for interfaces. The handoff's "Road to self-hosting" decides the subset: static dispatch on constraints, `dyn ref I`, default methods and associated types, so that `for … in` works on user types.

## Goals

- A generic function or type can require its type parameters to implement interfaces, and calls through those constraints compile to direct calls (no runtime dispatch).
- A value can be passed as `dyn ref I` and its methods called through it, without allocation.
- `for … in` iterates any user type that provides an enumerator.
- Every misuse is a diagnostic at definition or use, never a C compiler error.
- The subset is enough for the parser port's node visitors and collections; anything beyond it is reported as not supported yet, with the reason.

## Non-goals

- `static abstract` interface members (`IParse<TSelf>`): no port need shown; reported as not supported yet.
- Retroactive conformance (`extension T : I`): deferred by the handoff; reported as not supported yet.
- `dyn I` by value (owned dynamic values); only the borrowed `dyn ref I` form.
- Specialization rules beyond "a conformer's own method beats the interface default" (spec §11's general ranking).
- Witness-table sharing in debug builds (spec §11): every build monomorphizes for now.
- Delegates and lambdas (spec 007), `HashMap` (spec 008).
- Constraints other than interface constraints (`T : struct`, `T : unique`, `T : default`, `const` parameters).
- `yield`-based iterators.
- Conformance by unions and enums: reported as not supported yet (BYN2815). HANDOFF §1 gives unions members after `;`, but union members are spec-only in design round 2, so unions stay non-conforming until union members land. Structs conform.
- Enumerators that borrow their collection (borrowed struct fields are not in the language yet); `IEnumerable`-style "make me an enumerator" protocols. User collections iterate through an index protocol instead (R9).
- Interface inheritance, callable generic interface methods, bounds on associated types.

## User stories

- As the author of the Bayan parser port, I want a `ISyntaxNode` interface with a default `describe()`, so that every node kind implements its members once and shared logic lives in one place.
- As the port author, I want `for tok in tokens` over my own `TokenList` and over a token stream that produces tokens as it goes, so that loops read like the C# they come from.
- As a library author, I want `T max<T>(T a, T b) where T : IOrder<T>`, so that one definition serves every ordered type with no dispatch cost.
- As the port author, I want a `dyn ref IVisitor` parameter, so that a visitor can be chosen at run time without allocation.
- As any Bayan user, I want a missing interface member to name the member and offer its signature as a fix, so that conforming a type is quick.

## Requirements

- R1 Interface declarations. `interface IName { … }` declares abstract methods, read-only properties (`{ get; }`), default methods (a body or `=>` expression), and associated types (`type Item;`, no bounds). Every member is read-only by default; a member that mutates its receiver declares `ref this` as its first parameter (`bool moveNext(ref this);`), abstract and default alike, as on structs (HANDOFF §9). There is no `readonly` marker on interface members. Interfaces may take type parameters (`interface IOrder<T>`). Interface names start with `I` followed by an uppercase letter; any other name is a diagnostic with a rename fix.
- R2 Conformance. A struct lists interfaces after `:` (`struct Circle : IShape, IDescribe`). The type must provide every abstract member with the same signature (parameter types, return type, `fails` clause); the witness must match the abstract member's `ref this` (HANDOFF §9: implementations must match), so a `ref this` member cannot satisfy a read-only one and a read-only member cannot satisfy a `ref this` one; and it must bind every associated type (`type Item = Token;`). A missing member is a diagnostic naming it, with a fix that inserts its signature; a mismatched member names both signatures. Default methods are inherited unless the type provides its own.
- R3 Default methods may call the interface's other members on `this`; they cannot read fields. A default method body is checked once against the interface, not per conformer.
- R4 Constraints. Generic functions and types accept `where T : IA, IB` (several clauses allowed). Inside the generic body, members of `T` are exactly those of its constraints (and of `I<T>` instantiations, with `T` substituted); using any other member is a diagnostic at definition. An associated type is named through the parameter (`T.Item`).
- R5 Instantiation. Calling a generic function or naming a generic type with a type argument that does not conform to a constraint is a diagnostic at the use site naming the type, the interface and the first missing member. Type-argument inference works as today; a constraint never changes which type is inferred.
- R6 Static dispatch. A call through a constrained type parameter runs the conformer's method (or the default), chosen at compile time. Programs using only constraints contain no runtime dispatch.
- R7 `dyn ref I`. A parameter or local of type `dyn ref I` (and `dyn ref readonly I` for read-only access) holds a borrow of any conforming value; through `dyn ref readonly I` only members without `ref this` (abstract or default) can be called. Calls through it run the conformer's method. Converting a conforming value or borrow to `dyn ref I` is implicit where `dyn ref I` is expected. Borrow and lifetime rules are those of `ref` today (spec §9): a `dyn ref` cannot outlive the value it borrows, and it cannot be stored in a struct field (borrowed fields are not supported, as for `Span` today).
- R8 Object safety. An interface with generic methods or associated types cannot be used with `dyn`; the diagnostic names the member that prevents it. An interface name is not a type on its own: outside a base list, a `where` bound or `dyn ref`, it is a diagnostic offering `dyn ref I` or a constraint.
- R9 Iteration. `for x in e` (and the typed form `for T x in e`, e.g. `for dyn ref I x in e`) accepts two user protocols, both built-in interfaces:
  - `IIndexable` (`type Item; i64 count { get; } Item get(i64 index);`): `get` returns the item by value and panics out of range, like `xs[i]`; it is not named `at`, because on collections `at(i)` returns `T?` (HANDOFF §11). The loop visits `get(0)` … `get(count - 1)`, reading `count` once before the loop. The iterable is borrowed for the loop as a `List` is today, so nested loops over the same collection work.
  - `IEnumerator` (`type Item; bool moveNext(ref this); Item current { get; }`): the loop calls `moveNext()` until it returns false and reads `current` after each true. The enumerator owns its state. A temporary is iterated in a hidden variable; a named enumerator must be a `var` (iterating calls the `ref this` member `moveNext`), and a `let` one is a diagnostic.
  In both, the loop variable is a `let` copy of `Item` that cannot be moved out; a `unique` `Item` is a diagnostic until ref-returning getters exist (spec §3.15's `ref readonly Item current`, handoff Q90). `break`, `continue`, labels and `defer` behave as in other loops. Built-in iterables keep their current behaviour.
- R10 Unsupported forms are diagnostics, not crashes: `static abstract` members, `extension T : I`, `dyn I` without `ref`, interfaces listed on a union or enum, and non-interface types in `where` bounds each say what is not supported yet and, where one exists, the supported alternative.
- R11 Unique types. A `unique` type can conform and be used through a constraint or a `dyn ref`; passing it through either never copies or moves it unless the signature says `move`.
- R12 Diagnostics. Every new diagnostic has a code in 2000–3999, a page in `bayan explain`, a `diag/` golden and a verb-first help (spec 005 R15). Every runtime behaviour (static dispatch, default methods, `dyn ref` dispatch, user iteration, nested loops over a user `IIndexable`, an `IEnumerator` loop, `break` out of each) has a `run/` golden.
- R13 Self-hosting reach. A Bayan program in the shape of the parser port's core (an interface for nodes with a default method, at least three conforming node types, a visitor over them through a constraint and through `dyn ref`, a user token collection iterated with `for … in` through `IIndexable`, and a token stream iterated through `IEnumerator`) compiles and runs, as a `run/` golden.

## Acceptance criteria

- Given `interface IShape { f32 area(); string describe() => $"area {area()}"; }` and `struct Circle : IShape` providing only `area`, when the program calls `c.describe()`, then it prints the default text using `Circle.area`.
- Given `struct Square : IShape` without `area`, when checked, then one diagnostic names `area` and its fix inserts `f32 area()`.
- Given `T max<T>(T a, T b) where T : IOrder<T>` and a call `max(p, q)` where `p` and `q` are a struct type that does not conform (primitives do not conform to user interfaces), then the diagnostic is at the call, names the type and `IOrder`.
- Given a generic body calling `t.missing()` with `where T : IShape`, then the diagnostic is at the definition even with no call site.
- Given `void show(dyn ref IShape s)` called with a `Circle` and a `Square`, then each call runs that type's methods, and the emitted program allocates nothing for the call.
- Given `interface IMap { T get<T>(); }`, when used as `dyn ref IMap`, then the diagnostic names `get` as generic.
- Given a user `TokenList : IIndexable` over three tokens, when looped with `for t in list` with a `break` after the second, then exactly two are seen and deferred statements run once; a loop nested inside it over the same list sees all three each time.
- Given a `TokenStream : IEnumerator` producing three tokens, when a `var` stream is looped, then three are seen; looping a `let` stream is a diagnostic.
- Given `interface ICounter { i64 next(ref this); i64 peek(); }` and a conformer declaring `i64 peek(ref this)`, or `i64 next()` without `ref this`, then the conformance is a diagnostic naming the member and its `ref this` mismatch.
- Given `void show(dyn ref readonly ICounter c)` whose body calls `c.next()`, then the call is a diagnostic naming `next` as `ref this`.
- Given `static abstract` in an interface, `extension Money : IShape`, or `dyn IShape`, then each is a diagnostic saying it is not supported yet.

## Law candidates

- L012 (property): for every program in the interface program generator's grammar (interfaces with abstract and default methods, conforming structs, calls through constraints and through `dyn ref`), calling a method through a constraint and through `dyn ref` on the same value gives the same observable output as calling it directly on the value; and every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone, since the build toolchain silences warnings).

## Open questions

- (none; gaps are recorded as handoff Questions with the conservative choice, per AGENTS.md)
