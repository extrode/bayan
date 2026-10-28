## Audit of T12 (declare.byn) and T13 (generics.byn)

I read all the code involved. I ran one probe, under `tmp/review-b/probe1`, which showed that `map[k]` on a missing key panics. Nothing tracked was modified.

### Findings (most severe first)

1. **HIGH, checkOverloads panics.** `declare.byn:662-671` collects names from `allFunctions`, then indexes `c.modules[module].functions[name]`. `allFunctions` also holds methods, getters and constructors (`declare.byn:105,182,228`), but `addFunction` puts only free functions into `functions` (`compile.byn:811-827`). `compile.byn:56-59` runs checkOverloads after declareMembers, so any struct with a method, property or constructor panics ("key not found in map"). C# iterates `module.Functions.Values` (`Compilation.cs:657`). The test calls checkOverloads before any declareMembers, so it never hits this.
2. **HIGH, bindAll never binds field defaults of ordinary structs.** `generics.byn:579` filters to `template == -1`, then calls `fieldDefault`, which binds only when `template >= 0` (`generics.byn:708`). C# `Compilation.cs:1378-1383` calls `BindFieldDefault` directly.
3. **HIGH, bindFunction is a stand-in.** `generics.byn:629-645` binds only a top-level `.functionDecl`. It does nothing for methods, getters, constructors, main or tests, although T15 has API entries for `bindGetter`, `bindConstructor`, `bindMain` and `bindTest`. It also has no first/second pass, no seeds, no rollback of `_firstPass`, `MutationChecks` or `Locals`, and does not re-lift lambdas (C# `Compilation.cs:1716-1795`). It also invents `body = 0` when the body stays -1 (`:638-640`). That fake handle is what makes the test's `regFnBound: true` pass.
4. **HIGH, checkMutation and checkRequires are empty** (`generics.byn:698-703`; C# `:1847-1888`). The progress log (handoff line 1084) says they were ported. That claim is false, and no Question records them as gaps.
5. **HIGH, bindAll skips most of C# BindAll** (`generics.byn:568-626` vs `Compilation.cs:1364-1418`). Missing:
   - `BindChecks`, `BindEnumArguments`, `BindExitCode`
   - `EffectAnalysis` and the per-function ownership, lint, panic-handler, flow, region and AnalyzeInstance loop
   - the internal-error throw and BYN NoMain

   Each of these has a sibling API entry (010-api.md lines 126, 134, 152, 270-285), and none is called.
6. **HIGH, exit codes are lost.** declareVariants sets `seenExitCode` but drops `_exitCodes.Add` (`declare.byn:289-290`; C# `:744`). Ctx has no field for it, and nothing in T12 or T13 ever sets `ErrorVariant.exitCode`. Not raised as a Question.
7. **HIGH, wrong forwarded-code list in instantiatingEnd** (`generics.byn:188-190`; C# `:1483-1486`).
   - C# forwards BYN2760, BYN2831, BYN2208, BYN2225 and BYN2807 always; BYN2822, BYN2022 and BYN2780 only when the same diagnostic was not already reported (`!Reported`); BYN7012 only with an instance and `!Reported`.
   - The port forwards BYN2761 (RecursiveByValue), BYN2201 (StringIndexer), BYN7010 (BecomesNeedsRef) and BYN2200, which is not a code in DiagnosticCodes.cs at all.
   - It drops BYN2831, BYN2208, BYN2225 and BYN2780.
   - It has no `Reported` dedup, and omits the BYN7012 `Related` span (C# `:1493`).
   - Q580 repeats the wrong list as if it were C#'s.
8. **HIGH, Q704 is not resolved.** The handoff still marks it Open (line 883). `InstantiatingScope` (`generics.byn:7-12`) has no outermost site, no `_instantiating` depth and no held `_instantiationErrors`. As a result:
   - `check = _instantiating == 0` is gone: constraints are always checked (`:318, 380`).
   - Diagnostics that are not forwarded are dropped instead of held, so the internal-error path is impossible. Q580's "suppressed per C# :1483" misstates C#, which keeps them (`:1497`).
   - `AfterMembersDeclared` retargeting has nowhere to read the site from.
   - No dispatcher decision is recorded.
9. **HIGH, instantiateType adds instances to module lists.** It appends to `c.modules[m].structs`, `.unions` and `.interfaces` and dedups by scanning those lists (`generics.byn:346-371, 433-458, 482-507`). C# keys a private `_instances` map and never adds instances to `module.Structs` (`:1617-1661`). Module loops (declare, bindAll field defaults, shape checks) then also visit instances. The same pattern appears for functions via `allFunctions` (`:274-279`).
10. **HIGH, type constraint checks are not wrapped or deferred.** C# `CheckConstraintsAt` (`:1585-1592`) returns early when no parameter has constraints, runs inside `Instantiating` (reporting at the use site with the instantiation label), and waits in `_pendingConstraints` until conformances exist. The port calls checkConstraints directly, outside any scope (`generics.byn:380, 467, 516`), so BYN2807 lands on the template's where-bound. It also passes `as: s` where C# passes a null owner.
11. **HIGH, other instantiateType gaps.**
    - `Conform` runs unconditionally; C# runs it only once conformances are checked (`_conformancesChecked`).
    - The `if (instance.IsGeneric) return` is missing.
    - Property getters are not queued for binding (C# `:1636`).
    - Interface members are declared immediately; C# defers to `_pendingInterfaces` until `_interfaceMembersDeclared`.
    - Unknown kinds return the template instead of throwing.
12. **HIGH, t13Conforms invents logic.** It also accepts `bases` (`generics.byn:69-71`); C# `Conforms` looks only at `s.Conformances` (`:1595-1601`). The test's `satOk: true` relies on this (the struct has bases only, no conformance).
13. **HIGH, t13MissingMember differs from C# MissingMember** (`generics.byn:87-132` vs `:1604-1610`). It does not sort by span, does not filter to abstract members, and checks methods only against methods and properties only against properties (C# checks both). Its fallback also differs: it returns `": {name}"`, where C# returns the first abstract member or first member. The test expects `miss2: : IConform`; C# would give `reqProp`, so the expectation encodes the divergence.
14. **HIGH, checkConstraints differences** (`generics.byn:225-262` vs `:1565-1580`).
    - It considers only `.namedType` bounds; C# resolves every bound.
    - `resolveBound` is not run quietly (C# wraps it in `Quietly`).
    - `break` exits only the inner bounds loop; the C# loop is flattened with `SelectMany`, so its `break` ends the whole check for that parameter.
    - Help text uses `.name` instead of `DisplayName`.
15. **HIGH, wrong label for `in`/`out` enum parameters.** `declare.byn:468` gives anything not `ref` the label "move". ParamModifier also has `outKeyword` and `inKeyword` (`nodes.byn:228-234`). C# prints the lower-cased modifier name (`:836`).
16. **HIGH, wrong plain-enum test in declareEnumFields.** `declare.byn:472` checks `fields.length == 0`; C# checks `Decl.Params: null` (`:840`). An enum with parameters that is declared later still has no fields when this runs, so the port accepts it where C# reports BYN2724. `enum E()` is affected too.
17. **HIGH, Overlap differs for parameters with no syntax.** The fallback in `declare.byn:1003-1012` counts synthesized parameters as positional. In C# `Positional`/`Required` (`:675-677`), a parameter with `Syntax == null` counts toward neither. So C# says `synth1` vs `synth3` overlap; the port says no, and the test asserts `synthDiff: false`. Q575 presents this fallback as equivalent.
18. **MEDIUM, associated-type binding order** (`declare.byn:198-201`). The port resolves before the uniqueness check and discards the result. C# checks uniqueness first, resolves only when unique, and stores `_associatedBindings` (`:1006`). Diagnostic order differs, and duplicates still get resolve errors. `_baseSpans` is also dropped (`declare.byn:40` vs C# `:946`), so conformance falls back to the struct's name span.
19. **MEDIUM, invented field copies in instantiateFunction.** It copies `iface`, `isAbstract` and `modifiers` (`generics.byn:291-297`), which C# `:1505-1509` does not. It also omits the `_firstPass` branch (`:1518-1527`) and the `!fn.IsGeneric` guard. It only handles a top-level `functionDecl`, so a method template gets no signature and no constraint check.
20. **MEDIUM, templateOf and inferErrorSets.** templateOf omits the twin lookup for members of a generic struct (`generics.byn:731-735` vs `:1678-1679`). inferErrorSets drops `fn.IsInstance` and `if (fn.Body is null) continue` (`:664-676` vs `:1812-1820`).
21. **MEDIUM, smaller T13 divergences.**
    - parameterDefault binds even when the parameter has no syntax default (`:756-767` vs `:1903`).
    - invokeSymbol guesses the entry module as `modules.length-1` and leaves `isParameter` unset (`:536-546`).
22. **MEDIUM, re-implemented integer parsing.** `t12ParseInt` (`declare.byn:822-860`) re-implements T17's `parseInteger` instead of calling it, and adds octal `0o`, which C# `ParseInteger` (`BinderExpressions.cs:479`) lacks. The bounds-checking wrapper `t12Text` (Q577) is also not in C#.
23. **MEDIUM, tests do not use C# as an oracle.**
    - Neither T12Tests nor T13Tests runs `Bayan.Semantics`; every expectation is hand-typed.
    - T12 checks diagnostics only as `Assert.Contains(code)`: no line:col, message or order (`T12Tests:453-466`).
    - Several expectations contradict C# (items 12, 13, 17).
    - Siblings are still stubs (for example `functionTypeNotHere => false`), so `unsupp: 3` and similar counts reflect stub behaviour.
    - No failure-path test for: checkMutation, checkRequires, bindFunction, fieldDefault, parameterDefault (untested), declarePayloads duplicate/function-type paths, checkComposition beyond a bool, and the `instance` (BYN7012) branch of instantiatingEnd.
24. **LOW, false or inaccurate Questions.**
    - Q577 says the cross-file label is `{displayPath}:line X`; the code (`declare.byn:920`) writes `{displayPath}:{line}`.
    - Q579 says declare2.byn is T13's; it belongs to T38.
    - Q578's line numbers are off by one (:1070 and :394 are closing braces).
    - Q581 and Q582 describe invented behaviour (module-list dedup, bases) as if it were a port.
25. **LOW, display names.** Struct, interface and error-set names are used where C# uses `DisplayName`: checkComposition `:646-647` vs `:787`, duplicateConformance `declare.byn:37` vs `:943`. Generic instances and `ValidationError<T>` will display differently.

### Verdict

**T12:** The structure follows C# order and most branches are present. It still has real behaviour bugs:
- a checkOverloads crash on any struct with members
- the wrong `in`/`out` label and the wrong plain-enum test
- Overlap differs for parameters with no syntax
- dropped `_exitCodes`, `_baseSpans` and `_associatedBindings` facts

Its tests are hand-typed with no C# oracle, and they check diagnostic codes only. Not done.

**T13:** Not a method-for-method port:
- checkMutation and checkRequires are empty
- bindFunction and bindAll are stand-ins that skip every sibling call, plus a fake `body = 0`
- non-generic field defaults are never bound
- the forwarded-code list is wrong
- instances are invented into module lists, and constraint deferral and conformance checks are invented or missing

Q704 is left unresolved (no depth, outermost site or held errors), and the tests encode several of these divergences. Not done.