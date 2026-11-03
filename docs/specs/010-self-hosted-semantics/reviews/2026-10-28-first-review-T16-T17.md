## T16/T17 fidelity audit (binder2.byn, express.byn)

I read both ports method by method against `src/Bayan.Semantics/Binding/Binder.cs` and `BinderExpressions.cs`. I also ran the two test classes: `dotnet test --filter SelfhostSemanticsT16Tests|T17Tests` gives 18/18 passing. No tracked files were modified.

### Findings (most severe first)

1. **HIGH – `isNonNull` uses scope depth instead of scope identity.**
   - Bayan: `binder2.byn:525-531` returns true whenever the recorded scope index is still below `b.scopes.length`.
   - C#: `Binder.cs:731-740` walks the parent chain looking for that exact scope object.
   - Effect: in `{ if x is null { return } } { use(x) }` the second block is wrongly narrowed in Bayan. `narrowAfter` adds entries that are never removed, and the sibling block reuses the same depth.

2. **HIGH – lambda body and block are dropped.**
   - Bayan: `express.byn:103-105` passes `null` for both to `bindLambda`.
   - C#: `BinderExpressions.cs:375` passes `l.Body` and `l.Block`.
   - The stub signature (`Expr?`, `BlockStmt?`) doesn't match the parser node (`Expr body`, `Stmt block`). That mismatch should have gone to *Questions* instead of being silently nulled.

3. **HIGH – `CaughtErrorExpr` always returns an error.**
   - Bayan: `express.byn:67-68`.
   - C#: `BinderExpressions.cs:357` returns `BoundLocal(ce, _caught)` when `_caught` is set.
   - `Binder` has no `caught` field, and no Question records the gap.

4. **HIGH – `it` reads the struct's first field.**
   - Bayan: `express.byn:314-322` uses `fields[0]` whenever `itDepth > 0`.
   - C#: `BinderExpressions.cs:516-519` uses `_it`, the specific field being checked (`Binder.cs:269`), and only when `_it` is non-null.
   - Invented behavior.

5. **HIGH – the implicit-this member guard is missing.**
   - Bayan: `express.byn:351-356` drops C#'s `Host?.Template?.Interface is not {} cloneOf || LookupMember(cloneOf.Self, name) is not null` (`BinderExpressions.cs:531`).

6. **HIGH – two `Convert` branches are missing.**
   - Bayan: `express.byn:145-226` has neither of them.
   - The `_printArgument && argument && EnumSymbol && target.IsString` → `EnumName` branch (C# `BinderExpressions.cs:93-94`) is absent.
   - The `_sinkArgument` + `ContainsUntrusted` → BYN-UntrustedSink branch (C# :115-120) is absent, and `ContainsUntrusted` (C# :259) is not ported at all.
   - No Question covers either.

7. **HIGH – sibling stand-ins remain in T16, against the rules.**
   - `bigParse` literal fallbacks in `bindExitCode` (`binder2.byn:320-342`; this one also builds a string literal), `bindAssertFails` (:395-409) and `bindAssert` (:494-509).
   - None of this is in C#. Q595 only discloses the exit-code one, and its premise ("while `bindExpr` is a stub") is now stale since T17 landed.
   - A leftover `// stub:` comment sits on every ported function.

8. **HIGH – sibling stand-ins/workarounds in T17.**
   - `bindConverted` (`express.byn:140`) uses `bound == -1 ? boundBefore : bound` to get around T22's `built` stub. T16's `bindLocalDecl` (`binder2.byn:684`) does not do this, so the two are inconsistent.
   - `bindName` (:347) short-circuits `capture` with `b.lambdas.length == 0 ||`. That is not in C# :526, and works around T35's `capture` stub, which returns false.
   - `bindName` (:363) falls back to `c.states.tryGet` because T?'s `statesOf` stub (`declare2.byn:27`) returns -1. The test's `readStateField` depends on this fallback.

9. **HIGH – `bindAssertFails` doesn't set `_handled = call`.**
   - Bayan: `binder2.byn:392-394`.
   - C#: `Binder.cs:689-698` strips parens and sets `_handled` around binding the call.
   - The `assert f() fails E` operand is therefore treated as an unhandled failing call. There is no `handled` field and no Question.

10. **HIGH – `DecodeChar` is re-implemented and diverges.**
    - Bayan: `express.byn:752-812`.
    - Unknown escape `'\q'`: Bayan returns `'q'` (:767); C# `LiteralDecoder.cs:45-53` + `Unescape` returns -1 (two runes).
    - `\u{110000}` and surrogates: Bayan accepts them, while C# checks `Rune.IsValid`.
    - It does not reuse `selfhost/parser/literals.byn:65 decodeChar`.

11. **HIGH – float parsing is not correctly rounded.**
    - Bayan: `t17ParseFloat`/`t17Pow10` at `express.byn:815-898`.
    - C#: `double.Parse` (:437).
    - Example: `1.5e-310` gives 0, because 10^310 overflows to infinity, whereas C# gives the subnormal. Large exponents also round twice.

12. **MEDIUM – `parseInteger` ignores `BigParse.overflow`.**
    - Bayan: `express.byn:1230-1236`.
    - C#: `BinderExpressions.cs:479-491` uses `BigInteger`.
    - For literals above 2^127-1 the value is unspecified, so BYN-LiteralOutOfRange may print a garbage number or be missed.
    - The `startsWith("-")` branch is invented, since C# never sees a sign. The test asserts `-0x10` → -16, which C# would throw on.

13. **MEDIUM – float suffix and `f32` target interact differently.**
    - Bayan: `express.byn:487-495` reports "the suffix" even when the target is `f32`.
    - C# :438-440 returns an F32 literal first and never reports when the target is `f32`.

14. **MEDIUM – candidate order in `reportUndefined` differs, so `suggest` ties resolve differently.**
    - Bayan: `express.byn:945-966` walks scopes outermost→innermost and lists types grouped by kind. Aliases and distinct types may be missing, since it doesn't use `Module.Types.Keys`.
    - C#: `BinderExpressions.cs:556-567` walks innermost→outward with `Types.Keys` in insertion order.
    - `suggest` keeps the first candidate when distances tie (`d < best`), so a different order gives a different suggestion.

15. **MEDIUM – `ConvertToFunction` help text and signature differ.**
    - Lambda args drop particles: `express.byn:735-739` vs C# :171 `particle: name`.
    - Error sets print `.name` instead of `DisplayName` (:637).
    - Modifiers are limited to ref/move/in/out, while C# prints whatever the enum's lowercase name is.

16. **MEDIUM – `suggest`/`Distance` work on UTF-8 bytes with ASCII-only lowercasing.**
    - Bayan: `express.byn:1055-1088`, `:1251`.
    - C# :576-598 uses UTF-16 chars, `ToLowerInvariant`, and `name.Length/3`. Results differ for non-ASCII names.

17. **MEDIUM – the T16 tests are hand-typed and never call the C# oracle.**
    - `SelfhostSemanticsT16Tests.cs:316-344`.
    - Diagnostics are checked only as "code present" booleans (e.g. `d2772: 4`), never as `code line:col message`.
    - Not exercised at all: `negate`, the `narrowAfter` effect, `whileLet` BYN-WhileLetNotOptional, every `bindAssertFails` path, `bindFor` dynRef/ReadOnlySpan diagnostics, `bindLabeled` with switch, `defaultType` never, and nonNull through `!`, `&&` or `||`.
    - `IsNonNullAndNarrowing` appends to `b.nonNull` by hand.

18. **MEDIUM – the T17 tests are hand-typed too, with no C# oracle.**
    - `SelfhostSemanticsT17Tests.cs:504-579`; they mostly check bound-kind booleans and a few codes.
    - `BindNameMatches` asserts `readStateField == -1`, i.e. a sibling stub's output.
    - `bindStatementExpression` only checks `!= -1`; BYN3201 is never checked.
    - No test at all for `bindExprFor`, `bindConverted`, `bindExprOrType` dispatch beyond literals/names/this, the two-argument `constrain`, or the generic type-args path of `bindName`.
    - No test for these failure paths: the `asValue` variant group, literal-out-of-range and distinct in `convert`, and `ReportMismatch` help/fix.

19. **MEDIUM – Q598 and Q604 break-test line references are false, and Q603 misdescribes `fits`.**
    - Q598 cites `binder2.byn:488` "exit code range", :273 "bindFor local decl" and :556 "narrowedBegin". At commit 9d092c9 and now, those lines are a `}`, `isBorrowedPlace` and `narrowedEnd`.
    - Q604 cites `express.byn:1131` as `suggest`, but that line is in `constrain`; `suggest` is at :1239. :901 is `t17CheckedIntegerLiteral`, not `fits`.
    - Q603 claims `fits` does per-kind `asI64()` checks. It is a two-line `range()` comparison (:1172-1175).
    - Q596's codes are correct (I checked BYN2408, BYN2772, BYN2775, BYN3209 and BYN2226 against `DiagnosticCodes.cs`).

20. **LOW – display names and ordering.**
    - `checkUniqueConst` and `bindAssertFails` use `.name` where C# uses `DisplayName` (`binder2.byn:663`, :466; `Binder.cs:872`, :720). This differs for generic or qualified names.
    - `convert` builds error and literal nodes on the conversion `span` instead of `value.Syntax` (`express.byn:178` and others).
    - Function order is not C#'s:
      - T16 puts the helpers first, `bindLocalDecl` after `checkUniqueConst`/`defaultType`, and `bindStatement` last.
      - T17 puts `asValue` after `convert`, and literals after names.

### Verdict

**T16:** The structure is mostly faithful: dispatch, `bindLocalDecl`, `bindFor`, `isBorrowedPlace` and `defaultType` match C#. But `isNonNull` behaves differently from C#, `bindAssertFails` drops `_handled`, and three `bigParse` stand-ins violate the rules. The tests are hand-typed checks for code presence, not oracle comparisons, and the break-test claims in the handoff can't be verified.

**T17:** It is wider-ranging but less faithful. Two `Convert` branches are missing, the lambda body/block and `CaughtErrorExpr` are dropped, `it` resolution is invented, and three sibling-stub workarounds sit in `bindName`/`bindConverted`. Char and float decoding are re-implemented and diverge from C#. None of the tests use the C# oracle.
