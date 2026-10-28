Audit of T14 (conformance.byn) and T15 (binder.byn). I read every function and test in scope and ran no tests. Note: the C# binder file is `src/Bayan.Semantics/Binding/Binder.cs`, not `src/Bayan.Semantics/Binder.cs`. Bayan paths are relative to `selfhost/semantics/`.

## Findings (most severe first)

**High: wrong behaviour compared with C#**

1. **The static check reads the private bit.** conformance.byn:91 and :116 test `modifiers & 2`, which is `modifierPrivate`. `modifierStatic` is 4 (parser/nodes.byn:153-154), and `FunctionSymbol` has its own `isStatic` field, which `Expected` and `CloneDefault` never copy (conformance.byn:219, :278).
   - Effect: a private witness always fails the shape check (a false BYN2802) and its signature shows a stray "static ".
   - A static witness is accepted.
   - C#: Conformance.cs:197, :213.

2. **The `ref this` fix (BYN2814) is never produced for a method.** `t14RefThisFix` (conformance.byn:404-424) looks up `unit.members[declIndex]`. For a method nested in a struct, that index is the enclosing struct (symbols.byn:4), so the `functionDecl` case never matches and `memberIndex` is never read.
   - When `ref this` is being removed, it also drops `refThis` alone instead of the span from `refThis` to `params[0]`, which leaves ", " behind.
   - The test enshrines the bug: it expects `refHasFix: false` (T14Tests:441).
   - C#: Conformance.cs:231-237.

3. **The missing-member fix (BYN2801) ignores the brace line.** `t14InsertMember` (conformance.byn:394-398) always inserts `text + " "` at the brace. C# inserts `    {text}\n` at the line start when only whitespace precedes the brace, which is the usual multi-line struct (Conformance.cs:224-227).

4. **Associated types are resolved fresh, with no cache.** Ctx has no associated-bindings cache. C#'s `Conform` only reads `_associatedBindings`, which `DeclareMembers` fills (Compilation.cs:1006); `AssociatedBinding` memoizes it (Conformance.cs:30-33). Bayan instead:
   - resolves quietly on every call, with no memo (conformance.byn:637-671);
   - treats a binding that resolves to an error type as missing and reports BYN2803 (conformance.byn:533-538), where C# keeps the error type and reports nothing (Conformance.cs:67-70);
   - first looks in `c.conformances`, which is not in C#.

5. **The expected signature is substituted, not re-resolved.** `t14SubstType` (conformance.byn:163-195) substitutes only type parameters, `T?` and `List<T>`. C# re-resolves the declaration with the map (`DeclareSignature`, Conformance.cs:146-150). Members using `Map<K, Item>`, `Item[]`, tuples, spans or generic instances keep the raw type parameter, giving false BYN2802 and wrong signature text.

6. **Signature text drops excluded errors and uses raw names.** conformance.byn:146-158 omits " except .x" and uses raw error-set names instead of the error union's display name (Conformance.cs:212, Types.cs:559-561).

7. **`bindFunctionBody` departs from C# throughout** (binder.byn:72-116; C# Binder.cs:166-198):
   - It re-resolves parameter types and declares new `let` locals through `declareLocal`, instead of reusing `_fn.Parameters`. That creates duplicate locals and re-runs naming and shadowing checks.
   - Receiver states (`ReceiverStates`/`LearnState`) are missing, and `_firstStatement` is never set.
   - Parameter defaults are bound after the body; C# binds them before the requires clauses.
   - It adds a save/restore of `b.thisType` that C# does not have.

8. **`t15BindExpects` is invented logic** (binder.byn:118-159; C# Binder.cs:229-248):
   - It binds every requires clause a second time, so BYN2xxx "requires not bool" is reported twice.
   - It treats every parameter as read, so BYN8006 fires for any `Untrusted` parameter and BYN5011 fires whenever there is exactly one parameter.
   - "Has `this`" is approximated as `owner >= 0`, where C# checks for a `BoundThis` node.
   - It reports at the last parameter's span instead of the `expects` keyword.

9. **`t15BindRequires` builds an empty panic check** (binder.byn:178-185; C# :217-221):
   - The panic call has `function: -1` and no "contract violated: …" message argument.
   - `fn.Requires` is never stored.
   - The help text says "`count>0`" where C# says "`count > 0`"; the same drift is in `bindChecks` ("`it>0`").

10. **`addLocal` shadowing (BYN2007) differs from C#** (binder.byn:345-360; C# :155-159):
    - C# `Lookup` includes the current scope. Bayan searches only outer scopes, so `var x` redeclared in the same scope, or a `var` over a `let`, gives no BYN2007.
    - It adds error-type guards that C# lacks.
    - Label and help are copied from BYN2028. C# says "already declared on line N" and "pick a different name; Bayan does not let a local hide another one".

11. **`bindParameterDefault` binds nothing** (binder.byn:542-549; C# :354-361). It binds `parser.Expr.none` instead of the parameter's default syntax, never reports NotConstant ("a parameter default"), and has a dead `let d`.

12. **`bindEnumArguments` never binds the written arguments** (binder.byn:679-745; C# :424-444):
    - The call it builds always has an empty argument list (`callArgs`); `d.args` is never passed.
    - It invents arguments: when none come back it adds i32 `BLocal`s, which then trip BYN2723. The test's `hasEnumDiag` depends on this.
    - The `Values.Length: 0` guard is missing, so values can be appended twice.
    - Values are appended instead of placed by position, so indexes shift after an error or non-constant.
    - The `BoundEnumMember` → ordinal branch is missing, so an enum-member argument gets a false BYN2723.

13. **`bindChecks` misses the violation logic** (binder.byn:378-535; C# :258-324):
    - The Violation entry (field enum member plus `RuleOf`) is never added to the list, so the final `!isEmpty` fail can never fire.
    - `RuleOf` is not ported, and `checks.Rules.Add` is missing (the parameter is `in`).
    - Struct-level rules fail at once instead of recording a violation, and the J43 skip for rules reading a broken field is missing.
    - `_it` (the field) is never set; only `itDepth` is.
    - Flag declarations come out in the opposite order to C#'s `Insert(0)`, and there is an extra `c.validationErrors` fallback.

14. **`bindConstructor` has a sibling stand-in and the wrong parameter handling** (binder.byn:590-605; C# :380-385):
    - It falls back to `addBlock` when the sibling `bindBlock` returns a negative handle (Q592).
    - It re-declares parameters as new `let` locals with `resolveType`.
    - It never calls `BindParameterDefaults`.
    - It adds a push/pop scope.

15. **`bindGetter` structure differs** (binder.byn:564-587; C# :370-378): C# checks the block first, and the expression body is wrapped in a `BoundBlock` containing a `BoundReturn`. Bayan checks the body first, adds a void-statement branch, stores a bare return statement as the body, and adds push/pop scope.

16. **`bindArraySize` prints the wrong number for a negative length** (binder.byn:855-856). A negative N is printed as `n.lo` (a u64), so BYN2xxx LiteralOutOfRange shows 18446744073709551615 instead of -1 (C# :510).

17. **`t15ExprSpan` returns 0:0 for most expressions** (binder.byn:39-53). Member, index, lambda and similar expressions get span 0:0, so diagnostics from requires, rules, constants and array sizes land at offset 0.

18. **`bindMain` changes scope and span** (binder.byn:608-619; C# :387-394). It adds a push/pop scope, uses the function span instead of `statements[0]`, and never sets `_firstStatement`.

**Medium: unverified or latent**

19. **Neither test file uses the C# oracle.** SelfhostSemanticsT14Tests.cs and SelfhostSemanticsT15Tests.cs never run `Bayan.Semantics`, and both have hand-typed expectations: booleans and counts, never `code line:col message`. Their doc comments claim a comparison "against the C# Compilation" (line 8 in each).

20. **One T14 assertion is vacuous.** T14Tests:260 checks `BYN2801` with help containing "fails E0" in the mismatch section. Mismatch is BYN2802, so this matches SMissing's earlier diagnostic. The comments at :424 and :432 also mislabel the codes.

21. **Several T15 functions have no real assertion or failure path:**
    - `bindParameterDefault`, `bindModuleConstant`, `bindFieldDefault` (only the out-of-bounds case is checked), `bindTest`'s `@cases` path, `bindChecks` (only "statements > 0"), `checkValueName`, the same-scope BYN2007 case and `resolveType`'s type map.
    - The fixture source uses `fn compute(…)`, which is not Bayan syntax, so member 1 probably misparses. `bindArraySize`'s -1 comes from the error-type path, not the NotConstant path.

22. **`t14Expected` writes into the shared arenas.** It calls `newFunction`/`newLocal` for every member of every base (conformance.byn:197-241), so temporary "expected" symbols stay in `c.functions` and `c.locals`. C# creates a throwaway object.

23. **Base spans can be misaligned, and constraint args are dropped.**
    - The base span is found by indexing the syntax bases with the resolved-base index (conformance.byn:496-506). After a duplicate or unresolved base the spans are off by one, and only `namedType` is handled. C# uses `_baseSpans` (Conformance.cs:63).
    - The pending-constraint drain rebuilds `Work` with empty `args` (conformance.byn:478), dropping the queued args. No producer exists yet.

24. **`resolveType` picks a different type map.** It uses the function's map even when it is empty (binder.byn:278-282). C# uses `fn.TypeMap ?? container.TypeMap` (Binder.cs:55), so an instance method misses the struct's map.

**Low**

25. **Comments and line numbers have drifted.**
    - The "// stub: T14/T15 ports …" comments are still in place, with wrong C# lines (for example BindFunctionBody "Binder.cs:40" when it is :176).
    - Q594's break-test lines have moved: :266 is now :270, :333 is :337, and :833 is in `t15BindStaticArray`.
    - `bindTest` clamps to the shorter of params and values, and uses `d.span` instead of `decl.Body`.

## Questions checked

- **True:** Q585 (map key 0), Q590 (1-based `held`), Q591 (BYN2007 and BYN2028).
- **False:** Q587 says default collisions report BYN2801; the code uses `.interfaceMemberMismatch`, which is BYN2802. Q592 says `bindConstructor` "binds parameter defaults"; it does not.

## Verdicts

**T14:** The main `Conform` and `CheckConformances` flow and the diagnostic texts follow C#. However, the static check reads the private bit, the type substitution is partial, and the `ref this` and missing-member fixes are wrong or never produced. The tests are hand-typed counts with no C# oracle, and one is vacuous. Not acceptable as a faithful port yet.

**T15:** This is not a method-for-method port. `bindFunctionBody`, the requires/expects handling, `bindParameterDefault`, `bindEnumArguments`, `bindChecks`, `bindConstructor` and `bindGetter` contain invented or missing logic, and there is a sibling fallback. The shadowing diagnostics differ from C#, and the tests enshrine invented behaviour without any comparison to C#. It needs a rework.