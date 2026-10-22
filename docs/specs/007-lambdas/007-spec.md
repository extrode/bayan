# 007 — Lambdas and function types
Status: draft
Created: 2026-10-22
Revised: —
Depends on: design round 2 on dev (spec §3.16 as rewritten by round 2 §8; `Panic.onPanic` taking a named function from step 3.3); spec 006 is built at the same time on `feat/006-interfaces-iterators` and 007 does not depend on it (see the plan for the files both touch).

## Problem

The parser, binder and emitter ports (specs 009–011) are written around small callbacks: a predicate over tokens, a comparison for a sort, a visitor action per node, a "format this into that buffer" hook. C# spells these with `Func<…>`, `Action<…>` and lambdas. Bayan has none of that today: the lexer knows `=>` (`TokenKind.FatArrow`) only for expression bodies and switch arms, `delegate` is a reserved token nobody parses, the AST has no lambda or function-type node, the binder treats a function name as a callee only (`BoundMethodGroup`, "only valid as a callee"), and the one place a function is a value (`Panic.onPanic = f`) is a special case in `BindOnPanic`. The port either copies every callback into a hand-written `switch` or waits for function types. The handoff's "Road to self-hosting" row 007 decides the subset: function types, named functions as values, non-capturing lambdas, and lambdas that capture `let` values and parameters by copy; `ref` captures of `var` and escaping closures wait.

## Goals

- A function type is written one way (`A => B`, `(A, B) => C`, `() => void`, `() => T fails E`) and can be named with `type Name = A => B;`.
- A named function can be passed where its function type is expected, and called through the value.
- A lambda (`x => …`, `(a, b) => …`, `() => …`, with an expression or block body) is typed from where it is used, needs no parameter types, and may read the enclosing `let` values and parameters by copy.
- `it` names the parameter of a one-parameter lambda written as a bare expression (`any(xs, it > 2)`).
- Every misuse (capturing a `var`, storing a function value where it cannot live, `delegate`, nested `it`, `.member` as a lambda) is a diagnostic at the use, never a C compiler error.
- A call through a function value and a direct call of the same code give the same result, and a capture never aliases its source.

## Non-goals

- Capture by reference (`ref` captures of `var` locals): a lambda that names a `var` is a diagnostic that says to copy it into a `let` first (handoff decision: "`ref` captures of `var` … wait").
- Closures that outlive their frame: a function value is a borrow of the frame that made it; it cannot be stored in a struct field, returned, or kept in an `Owned`. The spec requires nothing more for v0.1 (§3.16: stored lambdas are non-capturing or `dyn (A => B)`, and `dyn` of a function type is not in v0.1).
- Generic lambdas (`<T>(T x) => …`) and lambdas whose parameter types are inferred from their body: a lambda takes its types only from the expected function type.
- `dyn (A => B)`, `move () => …`, multicast and `event` (spec §3.16 "Not in v0.1").
- Function types in struct fields, return types, type arguments, optionals and collections (`List<i32 => bool>`): each is reported as not supported yet.
- Built-in collection methods that take a lambda (`where`, `select`, `sumBy`, `removeWhere`, `sort(by:)`): spec §16 lists them as not in v0.1; 007 gives user functions function-typed parameters so a port can write its own.
- Type-argument inference from a lambda argument: a lambda never drives inference; the type parameters it mentions must be inferable from other arguments.
- Explicitly typed lambda parameters (`(i32 x) => …`) and parameter modifiers on lambdas (`ref`, `move`, `out`, `in`).
- Function value equality, printing a function value, `default` of a function type.
- Capturing `this` inside a method: reported; the fields needed are copied into `let` values first.

## User stories

- As the author of the Bayan parser port, I want `bool any(Span<Token> ts, Token => bool p)` and to call it with `any(ts, it.kind == TokenKind.Ident)`, so that scanning loops read like the C# they come from.
- As the port author, I want to pass `isKeyword` (a named function) where a `string => bool` is expected, so that existing helpers become predicates without a wrapper.
- As the port author, I want `(a, b) => a.start < b.start` to read the enclosing `let` offset by copy, so that a comparison can depend on local state without threading extra parameters.
- As a library author, I want `type Predicate = Token => bool;` so that a signature names the role of its callback instead of spelling the arrow type each time.
- As any Bayan user, I want a lambda that captures a `var` to be told to copy it into a `let`, so that "by copy" is never a surprise at run time.

## Requirements

- R1 Function type syntax. A function type is `P => R` where `P` is one type, `(A, B)` for two or more, or `()` for none, and `R` is any type including `void`; `fails E` (one or more error sets, comma separated) may follow `R`. `=>` in a type is right-associative, and the postfix `?` and `[N]` bind to the type they follow (`A => B?` returns `B?`). A function type is accepted as a parameter type, as the target of `type Name = …;`, and as the type of a local (`Pred f = …`). An optional, span, list, map, array or tuple of a function type, a function type as a struct field, as a return type or as a type argument, is a diagnostic naming the position. Its display name is the source form (`i32 => bool`, `(i32, i32) => i32`, `() => void`, `() => Token fails LexError`).
- R2 Aliases. `type Name = A => B;` declares an alias that is the function type everywhere the alias is named, as other aliases are today. Aliases have no type parameters in 007 (the alias grammar has none); `type Predicate<T> = T => bool;` from the spec is a later round.
- R3 Function type identity. Two function types are the same type when their parameter types, return type and `fails` sets are equal pairwise; there is no variance, and a function that cannot fail is not a value of a `fails` function type.
- R4 Named functions as values. A module-level, non-generic function named where a function type is expected is a value of that type when its signature matches R3 exactly; a mismatch is a type mismatch showing both signatures. A built-in, a generic function, a method (static or instance), a constructor or a union variant named as a value is a diagnostic that says to wrap it in a lambda. `Panic.onPanic = f` keeps its own rule and codes (round 2 §6) and is unchanged.
- R5 Lambda syntax. A lambda is `name => body`, `(name, name) => body` or `() => body`, where `body` is an expression or a block. Parameter names only: a type, a modifier or a default on a lambda parameter is a parse error. `=>` in an expression binds lower than every operator, so `x => x + 1` is one lambda and a lambda in an argument list ends at the next `,` or `)`. Switch arms, expression-bodied members and function types keep their `=>` unchanged.
- R6 Lambda typing. A lambda has no type of its own: it takes the parameter types, return type and `fails` set of the function type expected where it appears. A lambda with no expected function type (`let f = x => x;`) is a diagnostic; a lambda whose parameter count differs from the expected type's is a diagnostic naming both counts. A block body returns with `return`; an expression body returns its value, which must convert to the return type (`void` bodies discard a `void` expression). `break`, `continue` and `return` inside the body refer to the lambda, never to the enclosing function. The existing rules for `try`, `fails` and `defer` apply to the body as to a named function with the expected signature.
- R7 `it`. An argument expression whose syntax mentions the name `it` (outside any explicit lambda nested in it), in a position whose expected type is a one-parameter function type, is a one-parameter lambda with the parameter `it`. Inside such a lambda, another `it` shorthand is a diagnostic (BYN2029 reserved by round 2) that says to name one lambda. `it` anywhere else is an undefined name as today, and declaring a local named `it` stays allowed. `.member` is never a lambda: `.name` where a function type is expected is a diagnostic with a fix to `it.name`.
- R8 Captures by copy. A lambda body may read the enclosing function's locals that are not mutable (`let`, `const`, `for` and pattern bindings, `catch` bindings) and its parameters without a modifier; each is copied into the lambda when the lambda expression is evaluated, and the lambda sees that copy as a `let`. Reading a `var`, a `var` pattern binding, a `ref`/`out`/`in` parameter, or `this` (explicitly or through an unqualified field or method) is a diagnostic naming the local and why. A capture of a `unique` value is the existing implicit-copy diagnostic (BYN2780); a capture of a borrow (`string`, `Span<T>`, `ref`) copies the borrow, and the lambda value then borrows what the capture borrows (R10).
- R9 Calls through function values. `f(args)` where `f` has a function type calls the function the value holds with positional arguments converted to the parameter types; named arguments are a diagnostic (function types have no parameter names). The call has the type's return type; when the type has `fails`, the call must be handled with `try` or `catch` like any failing call, and the error sets are those of the type. Calling a value of any other type stays BYN2220.
- R10 Lifetime. A function value is a borrow: it may be held in a local, passed as an argument and called, but it cannot be stored in a struct field, returned, moved into an `Owned` or kept past the region of any capture, and the existing region diagnostics (BYN3001–3004) say so. While a lambda that copied a borrow of `x` is alive, `x` cannot be modified (BYN3004). A capture is never a borrow of its source: changing or dropping the source after the lambda was created does not change what the lambda sees.
- R11 Unsupported forms are diagnostics, not crashes: `delegate` declarations (BYN1028 reserved by round 2, with the function-type rewrite), function types in the positions R1 excludes, `dyn (A => B)`, `move` before a lambda, a lambda as a `ref`/`out` argument, a typed or modified lambda parameter, and a lambda in a `const` initializer each say what is not supported and, where one exists, the supported alternative.
- R12 Generics. A lambda or `it` argument never contributes to type-argument inference; the type parameters in its expected type are read from the other arguments or written out, and an uninferable one is BYN2740 as today. A lambda inside a generic function is bound and emitted once per instantiation, and a function type that mentions a type parameter substitutes like any other constructed type.
- R13 Emission. A function value is a pair of a code pointer and an environment pointer in the generated C; a non-capturing lambda and a named function have a null environment; a capturing lambda's environment is a tuple of its captures that lives in the frame that created the lambda. No heap allocation is introduced, and the runtime (`bayan-rt.c/.h`) is unchanged.
- R14 Diagnostics. Every new diagnostic has a code in 1000–3999, a page in `bayan explain`, a `diag/` golden and a verb-first help (spec 005 R15). Every runtime behaviour (a named function as a value, a non-capturing lambda, a capturing lambda, `it`, a block body with `return`, a `fails` function type with `try` and `catch`, a lambda passed through two levels of calls, a lambda held in a local and called twice, a lambda inside a generic function) has a `run/` golden; `bayan types` lists a lambda as a function of its enclosing module with a deterministic name (L009).
- R15 Self-hosting reach. A Bayan program in the shape of the parser port's helpers (a token struct, `bool any(Span<Token> ts, Token => bool p)` and `i64 indexWhere(Span<Token> ts, Token => bool p)` called with `it` and with a named function, a `(Token, Token) => bool` comparison capturing a `let` offset, a `string => string fails LexError` hook called through `try`, and a predicate forwarded through two user functions) compiles and runs, as a `run/` golden.

## Acceptance criteria

- Given `type Pred = i32 => bool;` and `bool any(Span<i32> xs, Pred p) { for x in xs { if p(x) { return true; } } return false; }`, when `main` calls `any(xs, it > 2)` and `any(xs, x => x > 2)` and `any(xs, isBig)` with `bool isBig(i32 x) => x > 2;`, then all three print the same value.
- Given `let base = 10;` and `i32 add(i32 x, i32 => i32 f) => f(x);`, when `add(1, x => x + base)` is called, then it prints `11`, and the emitted C allocates nothing for it.
- Given `var n = 1;` and `x => x + n`, then the diagnostic names `n`, says it is a `var`, and its help says to copy it into a `let`.
- Given `let f = x => x;`, then the diagnostic says the lambda needs a known type; given `(a, b) => a` where `i32 => i32` is expected, then the diagnostic names 2 and 1.
- Given `any(xs, any(ys, it == it))` where both parameters are one-parameter function types, then BYN2029 is reported on the inner `it` and its help says to name one lambda.
- Given `any(xs, .isEven)`, then BYN2823 is reported with a fix to `it.isEven`.
- Given `delegate bool OrderFilter(Order o);`, then BYN1028 is reported with the fix `type OrderFilter = Order => bool;`.
- Given `struct S { i32 => bool p; }`, `(i32 => bool) f()`, `List<i32 => bool>` or `(i32 => bool)? g`, then each is a diagnostic naming the position and saying function types are not supported there yet.
- Given `() => Token fails LexError next` as a parameter and a body calling `next()` without `try`, then BYN2221 is reported as for any failing call; with `try next()` the program runs and a failing lambda body's error reaches the caller's `catch`.
- Given a lambda that captures a `let s = buf.str;` (a borrow of `var buf`), when `buf` is appended to while the lambda is alive, then BYN3004 names `buf`.
- Given `let xs = List<i32>();` captured by `() => xs.count`, then BYN2780 is reported as for any implicit copy of a `unique` value.
- Given `T pick<T>(Span<T> xs, T => bool p)` called as `pick(ints, it > 0)`, then `T` is inferred from `ints`; called as `pick([], it > 0)` with no other typed argument, then BYN2740 is reported.
- Given a lambda whose block body contains `return`, `break` inside its own loop, and a `defer`, then the lambda returns its value, the loop breaks within the lambda, and the deferred statement runs once per call.
- Given the R15 program, then it runs and prints the expected lines, and `bayan types` on it is byte-identical across two runs.

## Law candidates

- L013 (property): for every program in the lambda program generator's grammar (named functions and lambdas of zero to two parameters over `i64`, `bool` and `string`, non-capturing or capturing `let` locals and plain parameters, called directly, through a function value in a local, and through a function value passed through one or two user functions), the direct and the indirect calls print the same output; and every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone, since the build toolchain silences warnings).
- L014 (guard): a table of capture sites and the verdict each must get. A `var`, a `var` pattern binding, a `ref`/`out`/`in` parameter and `this` are BYN2821; a `unique` value is BYN2780; a borrow of a `var` that is then modified while the lambda lives is BYN3004; a `let` of a copyable type or a plain parameter is accepted, and the program prints the value the capture had when the lambda was created, whatever happens to the source afterwards (a later `let` shadow in a nested scope, dropping the source's scope, reassigning the `var` the `let` was copied from).

## Open questions

- (none; gaps are recorded as handoff Questions with the conservative choice, per AGENTS.md)
