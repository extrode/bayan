Audit of T10 (manifest.byn) and T11 (compile.byn): T10 is a faithful port, T11 is not

I read every file named and compared each method in scope line by line. I did not run the test suites. Everything below comes from reading the code. Bayan paths are under `selfhost/semantics/`. C# paths are `Manifest.cs` = `src/Bayan.Syntax/Packages/Manifest.cs` and `Compilation.cs` = `src/Bayan.Semantics/Compilation.cs`.

## High: wrong behavior compared with C# (all in T11)

1. **Entry points not ported.** compile.byn has no `FromPath`, `FromProject`, `Create` or `ModuleFiles` (Compilation.cs:69-121).
   - Nothing calls `removeRepeats`, although the task row requires it (C# :112).
   - `Ctx` has no IncludeTests, PackageRoot or DisplayRoot.
   - The task's "file, directory and project loading give the C# module/file order" is therefore not met.

2. **`declare` (compile.byn:7-82) leaves out much of C# `Declare` (Compilation.cs:184-241).** Missing:
   - `DeclareBuiltinVariants`, both the IoError/Utf8Error call and the ParseError call;
   - every `DeclareInterfaceMembers` call (IEnumerator, IIndexable, module interfaces) and `_interfaceMembersDeclared`;
   - `_pendingInterfaces`;
   - the `Rule` variants loop (:211-217);
   - `CheckStateNames`;
   - `DeclareSignature` for free functions (:229-230);
   - `MembersDeclared`;
   - `CheckStructShape` and `CheckUnionShape`.

   The sibling functions exist (declare2.byn) but are never called. In C#, each module runs all `DeclareMembers`, then all `CheckStateNames`, then all `CheckMembers`. Bayan instead runs `declareMembers` and `checkMembers` together per struct (:55-58), which changes diagnostic order. The pending-keys loop (:61-74) duplicates `runWork` instead of dispatching through it.

3. **`t11PackageRoot` is invented (compile.byn:300-310).** It takes the directory of `files[0]` and strips a trailing `/app`. C# uses the compilation's `PackageRoot` (:150). This gives the wrong import directory for a library entry (`src/myLib`) and for any FromPath directory named `app`.

4. **Cycle detection is a heuristic (compile.byn:205-240).**
   - Bayan treats any file with `module < 0` whose path starts with `dir/` as "loading". C# uses the `_loading` list (:153-161).
   - The chain is built from directory base names plus the import text. C# uses module names (the entry module is `"main"`) plus `module.Name`, so the message text differs.
   - `loadModule` registers the module only at the end (:167-178). C# registers it in `_modulesByDir`/`_loading` up front (:128-129).

5. **Imported modules are loaded differently from C#.**
   - `includeTests: false` is hard-coded (compile.byn:260). C# uses `Options.IncludeTests` (:165).
   - The file path is `{dir}/{name}`. C# makes it relative to `DisplayRoot` (:172), so rendered paths differ.
   - Unreadable files are silently skipped (:267, `catch continue`), which C# does not do.

6. **`t11CheckUnique` (compile.byn:784-797) diverges from `CheckUnique`/`ReportDuplicate` (Compilation.cs:683-699).**
   - It does not look up `Functions` (:688), so a type declared after a function of the same name is not reported.
   - It drops the label ("already declared on line N" or "path:line").
   - The duplicate report in `declareTypeParameters` (compile.byn:840) also lacks that label (C# :580).

7. **Duplicate `main` function.** C# reports BYN2xxx DuplicateMain and `break`s before `AddFunction` (Compilation.cs:491-495). Bayan reports it and still calls `addFunction` (compile.byn:683-689), so the second `main` joins the overload set.

8. **Top-level statements (compile.byn:760-779).** C# has three branches (:529-548):
   - DuplicateMain with the label "`main` is also declared on line N", only when Main is a function;
   - a synthesized main otherwise;
   - BYN StatementOutsideEntry with help "keep top-level statements in one file of the entry module" when Main is another file's synthesized main.

   Bayan reports DuplicateMain with no label in both error cases, so the code is wrong in the third case.

9. **Tests (compile.byn:729-753) diverge from C# :518-524.**
   - Tests are declared even without IncludeTests.
   - A test with parameters is declared once, not once per `@cases` case with `CaseTitle`.
   - `CheckCases` (:322-352, BYN2773/2774) is unported.
   - The "test with params but no @cases" BYN1016 (:307-313) is missing.

10. **`t11CheckAttributes` (compile.byn:349-380) is a partial port of `CheckAttributes` (:273-316).** Missing:
    - the `@positional` on a struct with states error (:299-300);
    - `Positional.Add` (:302);
    - recursion into struct members (:314-315);
    - `_testCases` population.

11. **Missing synthesized declarations.**
    - The J23 `S_Field` enum for structs with rules or `requires` (Compilation.cs:389-397) is not created.
    - A distinct `type` alias gets no synthetic `value` field carrying `Requires` (C# :455-456, Bayan compile.byn:607-626).

12. **Diagnostic order inside `DeclareFile`.** `declareTypeParameters` runs before `addType`/`checkType` for struct (compile.byn:463-473), union (:537-547) and interface (:570-584). C# runs it after `AddType`/`CheckType` (:386-398, :421-423, :431-436). Type-parameter diagnostics therefore come out first.

13. **Aliases kept after a failed `addType`.** Plain aliases and error sets are recorded in `c.aliases` even when `addType` fails (compile.byn:631-641, :647-657; `let _ = addType`). C# adds them to `_aliases` only on success (:469, :476). `checkComposition` then runs on duplicates.

14. **`associatedType` identity is wrong (compile.byn:922-931).** It linearly scans all type params for the name `"T.Item"`. C# keys a dictionary on (owner identity, name) (:562-567). Two different owners' `T` therefore share one `T.Item`.

## Medium: invented or unverified behavior (T11)

15. **`afterMembersDeclared` (compile.byn:934-947)** ignores the `_instantiationSite` wrapping (C# :1549-1550), so a deferred key check reports at its own span, not at the instantiation site.

16. **`reportInto` (compile.byn:950-952)** returns nothing, while C# returns the outer sink (:1922-1927). This follows the API signature, but no caller can restore the previous sink. Low.

17. **`t11CheckMainSignature`** stands in `!c.locals[p].written` for the C# `Modifier == ParamModifier.None` check (compile.byn:431 vs C# :265).

18. **Logic not in C#:**
    - `declareWhere` reports BYN2804 "a type" for a non-named bound (compile.byn:894-896); C# bounds are `NamedType[]`.
    - `loadModule` returns early when the directory is already loaded (:95-97).
    - Lexer and parser diagnostics get a hard-coded `isWarning: false` (:120).
    - `addType` returns false when `modId < 0` (:805).

19. **T11 tests have no C# oracle at all** (SelfhostSemanticsT11Tests.cs).
    - `using Bayan.Semantics` is imported but unused, and every expectation is hand-typed (:276-377).
    - Only codes are compared, never `code line:col message`.
    - `"added: 27"` and "10 BYN1016 across 2 passes" lock in a double declaration of the same file (`t11DeclareFile` at :181, then `declare` at :256).
    - The fixture uses `fn foo() { }`, which is not Bayan syntax (:55).
    - The cycle test fakes a loading module with a hand-built `module: -1` file (:225-235).
    - Untested: the duplicate-import alias, test declarations, the main-statement branches, cycle chain text, the unknown-module help text, distinct aliases, extension BYN diagnostics, and `declareFile` as a public function.

20. **Several Questions entries for T11 are false:**
    - **Q571** says `t11CheckUnique` checks types, structs, interfaces, enums, unions and constants. It checks only `types` and `constants` (compile.byn:788-795), and not `functions` as C# does.
    - **Q574** says `loadModule` checks importer chains and in-flight modules. `loadModule` does no cycle checking. `resolveImport` does, using the path heuristic.
    - **Q572** says non-test files get `isEntry`. `isEntry = importer < 0` is set on test files too (:105).
    - **Q573**'s break-test at ":805" is `if modId < 0 { return false }`. Flipping it cannot fail `AddTypeMatches`, because modId is 0 there; the actual duplicate return is at :803.

## T10 findings

21. **Q565's claim is false (medium).** An unreadable or empty file reports BYN3100 "expected a string, an integer, `true`, `false`, `.name`, `[…]` or `Type { … }`" (the default `Fail`, Manifest.cs:458; manifest.byn:727). It does not report "expected `Package { … }`" as Q565 says. The test oracle substitutes `Parse("")` for the C# throw (T10Tests:337); that substitution is documented.

22. **Q568's break-test line numbers are stale (low).** `n += 1` is at manifest.byn:95 (not :86), `buf.append(0x2D)` at :452 (not :460), and `if slash < 0` at :517 (not :511).

23. **`CompareVersions`/`SplitVersion` are unported (low).** Manifest.cs:309-327; only `Dist.cs` in the CLI uses them.

24. **`camelOf` uppercases ASCII only (low).** manifest.byn:472 vs C# `ToUpperInvariant` (:267). This is documented in Q566, and the test special-cases `a-é` (T10Tests:422).

I checked the rest of T10 branch by branch against Manifest.cs and found no other divergences: `Parse`, `Bind` and its style/grammar/dist parts, `Fields`, `Required`, `Text`, `Invalid`, `Report`, the `Reader`, the string decoder, integer parsing, and the kebab/package/semver predicates. Codes, messages, help, label, fix spans and order all match.

## Verdict

- **T10:** A faithful, method-for-method port. Its tests use a real in-process oracle (`Manifest.Read` plus `DiagnosticRenderer`) over about 55 manifests, including failure paths. The only issues are the unported `CompareVersions` and inaccurate wording and line numbers in Q565 and Q568.
- **T11:** Not a faithful port. The entry points are missing, `declare` skips about half of C# `Declare`, and the import, cycle, unique-name, main and test logic is partly invented. Every function still carries a `// stub:` header. The tests are hand-typed with no C# comparison, and Q571, Q572, Q573 and Q574 make claims the code contradicts.
