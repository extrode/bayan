using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T14: <c>selfhost/semantics/conformance.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT14Tests
{
    private static string MainSource() => """
        import lexer
        import parser

        i32 main() {
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
            }

            // ── 1. conformancesChecked & quietlyBegin/End ──
            let beforeChecked = conformancesChecked(ref c)
            let q = quietlyBegin(ref c)
            report(ref c, for: .missingAssociatedType, from: 0, at: TextSpan { start: 0, length: 0 }, by: ("S", "Item", "I", "", ""))
            let qDiags = c.diagnostics.length
            quietlyEnd(ref c, to: q)
            conform(ref c, for: StructId(c.structs.length))
            conform(ref c, for: -1)
            print("== smoke")
            print($"beforeChecked: {beforeChecked} qDiags: {qDiags}")

            // ── 2. Setup AST and Symbols ──
            let srcText = "interface IGood {\n    type Item\n    i32 getVal() fails E0\n    i32 count { get; }\n    void runDefault(i32 mode) => { }\n    void errMethod() fails E0\n}\nstruct SGood : IGood {\n    type Item = i32\n    i32 count\n    i32 getVal() => 42\n    void errMethod() fails E0 => { }\n}\nstruct SMissing : IGood {\n}\nstruct SMismatch : IGood {\n    type Item = i32\n    string count\n    void getVal(string s) => { }\n    void errMethod() fails E1 => { }\n}\nstruct SRefMismatch : IGood {\n    type Item = i32\n    i32 count\n    i32 getVal(ref this) => 42\n    void errMethod() fails E0 => { }\n}\ninterface ICollision {\n    void runDefault(i32 mode) => { }\n}\nstruct SCollision : IGood, ICollision {\n    type Item = i32\n    i32 count\n    i32 getVal() => 42\n    void errMethod() fails E0 => { }\n}\n"

            var bytes = List<u8>()
            for b in srcText.bytes { bytes.append(b) }
            var copy = List<u8>()
            for b in srcText.bytes { copy.append(b) }
            let source = lexer.SourceText.create("test.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unitNode = parser.parseCompilationUnit(ref p)
            let sf = SourceFile {
                path: "test.byn",
                displayPath: "test.byn",
                text: move copy,
                unit: move unitNode,
                module: 0,
                isEntry: true,
                isTestFile: false,
                imports: Map<string, ModId>(),
            }
            c.files.append(move sf)
            var modFiles = List<FileId>()
            modFiles.append(0)
            c.modules.append(Mod {
                name: "test",
                files: move modFiles,
                directory: ".",
                cName: "test",
            })

            let es0 = newErrorSet(ref c, by: ErrorSetSymbol { name: "E0", file: 0, module: 0 })
            let es1 = newErrorSet(ref c, by: ErrorSetSymbol { name: "E1", file: 0, module: 0 })
            c.modules[0].types["E0"] = makeType(.errorSet, at: es0)
            c.modules[0].types["E1"] = makeType(.errorSet, at: es1)

            // ── Setup IGood ──
            let iGood = newInterface(ref c, by: InterfaceSymbol {
                name: "IGood",
                file: 0,
                module: 0,
                declIndex: 0,
            })
            let tpItem = newTypeParam(ref c, by: TypeParam { name: "Item" })
            c.interfaces[iGood].associatedTypes.append(tpItem)
            var es0List = List<ErrorSetId>()
            es0List.append(es0)
            let fnGetVal = newFunction(ref c, by: FunctionSymbol {
                name: "getVal",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.i32),
                isAbstract: true,
                hasFails: true,
                errorSets: move es0List,
            })
            c.interfaces[iGood].methods.append(fnGetVal)
            let fnCount = newFunction(ref c, by: FunctionSymbol {
                name: "count",
                file: 0,
                module: 0,
                kind: .getter,
                returnType: prim(PrimitiveKind.i32),
                isAbstract: true,
            })
            c.interfaces[iGood].properties.append(fnCount)
            let pDef = newLocal(ref c, by: LocalSymbol {
                name: "mode",
                type: prim(PrimitiveKind.i32),
                isParameter: true,
            })
            var pDefList = List<LocalId>()
            pDefList.append(pDef)
            let fnDef = newFunction(ref c, by: FunctionSymbol {
                name: "runDefault",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                isAbstract: false,
                parameters: move pDefList,
            })
            c.interfaces[iGood].methods.append(fnDef)
            var es0List2 = List<ErrorSetId>()
            es0List2.append(es0)
            let fnErr = newFunction(ref c, by: FunctionSymbol {
                name: "errMethod",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                isAbstract: true,
                hasFails: true,
                errorSets: move es0List2,
            })
            c.interfaces[iGood].methods.append(fnErr)
            c.modules[0].interfaces.append(iGood)
            c.modules[0].types["IGood"] = makeType(.interfaceType, at: iGood)

            // ── Setup SGood ──
            let sGood = newStruct(ref c, by: StructSymbol {
                name: "SGood",
                file: 0,
                module: 0,
                declIndex: 1,
            })
            c.structs[sGood].bases.append(iGood)
            let fCount = newField(ref c, by: FieldSymbol {
                name: "count",
                file: 0,
                type: prim(PrimitiveKind.i32),
            })
            c.structs[sGood].fields.append(fCount)
            let fnSGetVal = newFunction(ref c, by: FunctionSymbol {
                name: "getVal",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.i32),
                hasFails: true,
                infersErrors: true,
            })
            c.structs[sGood].methods.append(fnSGetVal)
            var es0ListGood = List<ErrorSetId>()
            es0ListGood.append(es0)
            let fnSErr = newFunction(ref c, by: FunctionSymbol {
                name: "errMethod",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                hasFails: true,
                errorSets: move es0ListGood,
            })
            c.structs[sGood].methods.append(fnSErr)
            c.structs[sGood].template = StructId(c.structs.length)
            c.modules[0].structs.append(sGood)
            c.modules[0].types["SGood"] = makeType(.structType, at: sGood)

            // ── 3. associatedBinding ──
            let bound = associatedBinding(ref c, for: sGood, by: "Item")
            let boundMissing = associatedBinding(ref c, for: sGood, by: "NonExistent")
            let boundOob = associatedBinding(ref c, for: StructId(c.structs.length), by: "Item")
            let boundNeg = associatedBinding(ref c, for: -1, by: "Item")
            print("== associatedBinding")
            print($"boundPrimitive: {kindOf(bound) == .primitive} missingNotFound: {boundMissing == -1} oobNotFound: {boundOob == -1 && boundNeg == -1}")

            // ── 4. conform SGood (witness, field witness, cloned default) ──
            let preGood = c.diagnostics.length
            conform(ref c, for: sGood)
            let goodDiags = c.diagnostics.length - preGood
            let confGood = c.structs[sGood].conformances[0]
            let witnessFound = c.conformances[confGood].witnesses.contains(fnGetVal)
            let fieldWitnessFound = c.conformances[confGood].fieldWitnesses.contains(fnCount)
            let defaultCloned = c.conformances[confGood].witnesses.contains(fnDef)
            let clonedFn = c.conformances[confGood].witnesses[fnDef]
            let param0 = c.functions[clonedFn].parameters[0]
            let paramIsParam = c.locals[param0].isParameter
            print("== conformGood")
            print($"witness: {witnessFound} fieldWitness: {fieldWitnessFound} defaultCloned: {defaultCloned} diags: {goodDiags} paramIsParam: {paramIsParam}")

            // ── 5. conform SMissing (missing members & associated type) ──
            let sMissing = newStruct(ref c, by: StructSymbol {
                name: "SMissing",
                file: 0,
                module: 0,
                declIndex: 2,
            })
            c.structs[sMissing].bases.append(iGood)
            c.modules[0].structs.append(sMissing)
            let preMiss = c.diagnostics.length
            conform(ref c, for: sMissing)
            let missCount = c.diagnostics.length - preMiss
            var hasGetterFix = false
            for d in c.diagnostics {
                if d.code == "BYN2801" && d.hasFix {
                    if d.fixReplacement == "i32 count => todo(\"count\") " {
                        hasGetterFix = true
                    }
                }
            }
            print("== conformMissing")
            print($"missCount: {missCount} hasGetterFix: {hasGetterFix}")

            // ── 6. conform SMismatch (signature mismatch & field type mismatch) ──
            let sMismatch = newStruct(ref c, by: StructSymbol {
                name: "SMismatch",
                file: 0,
                module: 0,
                declIndex: 3,
            })
            c.structs[sMismatch].bases.append(iGood)
            let fBadCount = newField(ref c, by: FieldSymbol {
                name: "count",
                file: 0,
                type: prim(PrimitiveKind.string),
            })
            c.structs[sMismatch].fields.append(fBadCount)
            let pBad = newLocal(ref c, by: LocalSymbol {
                name: "s",
                type: prim(PrimitiveKind.string),
                isParameter: true,
            })
            var pBadList = List<LocalId>()
            pBadList.append(pBad)
            let fnBadGetVal = newFunction(ref c, by: FunctionSymbol {
                name: "getVal",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                parameters: move pBadList,
            })
            c.structs[sMismatch].methods.append(fnBadGetVal)
            var es1List = List<ErrorSetId>()
            es1List.append(es1)
            let fnBadErr = newFunction(ref c, by: FunctionSymbol {
                name: "errMethod",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                hasFails: true,
                errorSets: move es1List,
            })
            c.structs[sMismatch].methods.append(fnBadErr)
            c.modules[0].structs.append(sMismatch)
            let preMis = c.diagnostics.length
            conform(ref c, for: sMismatch)
            let misCount = c.diagnostics.length - preMis
            var hasE0Sig = false
            for d in c.diagnostics {
                if d.code == "BYN2801" && d.help.contains("fails E0") {
                    hasE0Sig = true
                }
            }
            print("== conformMismatch")
            print($"misCount: {misCount} hasE0Sig: {hasE0Sig}")

            // ── 7. conform SRefMismatch (ref this mismatch) ──
            let sRefMismatch = newStruct(ref c, by: StructSymbol {
                name: "SRefMismatch",
                file: 0,
                module: 0,
                declIndex: 4,
            })
            c.structs[sRefMismatch].bases.append(iGood)
            let fRefCount = newField(ref c, by: FieldSymbol {
                name: "count",
                file: 0,
                type: prim(PrimitiveKind.i32),
            })
            c.structs[sRefMismatch].fields.append(fRefCount)
            let fnRefGetVal = newFunction(ref c, by: FunctionSymbol {
                name: "getVal",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.i32),
                hasFails: true,
                infersErrors: true,
                isMutating: true,
                declIndex: 0,
            })
            c.structs[sRefMismatch].methods.append(fnRefGetVal)
            var es0ListRef = List<ErrorSetId>()
            es0ListRef.append(es0)
            let fnRefErr = newFunction(ref c, by: FunctionSymbol {
                name: "errMethod",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                hasFails: true,
                errorSets: move es0ListRef,
            })
            c.structs[sRefMismatch].methods.append(fnRefErr)
            c.modules[0].structs.append(sRefMismatch)
            let preRef = c.diagnostics.length
            conform(ref c, for: sRefMismatch)
            let refCount = c.diagnostics.length - preRef
            var refFound = false
            var refHasFix = false
            for d in c.diagnostics {
                if d.code == "BYN2814" {
                    refFound = true
                    refHasFix = d.hasFix
                }
            }
            print("== conformRefMismatch")
            print($"refCount: {refCount} refFound: {refFound} refHasFix: {refHasFix}")

            // ── 8. conform SCollision (two bases provide conflicting defaults) ──
            let iCollision = newInterface(ref c, by: InterfaceSymbol {
                name: "ICollision",
                file: 0,
                module: 0,
                declIndex: 5,
            })
            let pDefColl = newLocal(ref c, by: LocalSymbol {
                name: "mode",
                type: prim(PrimitiveKind.i32),
                isParameter: true,
            })
            var pDefCollList = List<LocalId>()
            pDefCollList.append(pDefColl)
            let fnCollDef = newFunction(ref c, by: FunctionSymbol {
                name: "runDefault",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                isAbstract: false,
                parameters: move pDefCollList,
            })
            c.interfaces[iCollision].methods.append(fnCollDef)
            c.modules[0].interfaces.append(iCollision)
            c.modules[0].types["ICollision"] = makeType(.interfaceType, at: iCollision)

            let sCollision = newStruct(ref c, by: StructSymbol {
                name: "SCollision",
                file: 0,
                module: 0,
                declIndex: 6,
            })
            c.structs[sCollision].bases.append(iGood)
            c.structs[sCollision].bases.append(iCollision)
            let fCollCount = newField(ref c, by: FieldSymbol {
                name: "count",
                file: 0,
                type: prim(PrimitiveKind.i32),
            })
            c.structs[sCollision].fields.append(fCollCount)
            let fnCollGetVal = newFunction(ref c, by: FunctionSymbol {
                name: "getVal",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.i32),
                hasFails: true,
                infersErrors: true,
            })
            c.structs[sCollision].methods.append(fnCollGetVal)
            var es0ListColl = List<ErrorSetId>()
            es0ListColl.append(es0)
            let fnCollErr = newFunction(ref c, by: FunctionSymbol {
                name: "errMethod",
                file: 0,
                module: 0,
                returnType: prim(PrimitiveKind.void),
                hasFails: true,
                errorSets: move es0ListColl,
            })
            c.structs[sCollision].methods.append(fnCollErr)
            c.modules[0].structs.append(sCollision)
            let preColl = c.diagnostics.length
            conform(ref c, for: sCollision)
            let collCount = c.diagnostics.length - preColl
            print("== conformCollision")
            print($"collCount: {collCount}")

            // ── 9. checkConformances & instance conformance ──
            print("== checkConformances")
            let preAllFns = c.modules[0].allFunctions.length
            checkConformances(ref c)
            let postAllFns = c.modules[0].allFunctions.length
            let checkedNow = conformancesChecked(ref c)
            print($"afterChecked: {checkedNow} fnsAdded: {postAllFns > preAllFns}")

            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void SmokeMatches()
    {
        var expected = new List<string> { "beforeChecked: false qDiags: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "smoke", expected);
    }

    [Fact]
    public void AssociatedBindingMatches()
    {
        var expected = new List<string> { "boundPrimitive: true missingNotFound: true oobNotFound: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "associatedBinding", expected);
    }

    [Fact]
    public void ConformGoodMatches()
    {
        var expected = new List<string> { "witness: true fieldWitness: true defaultCloned: true diags: 0 paramIsParam: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "conformGood", expected);
    }

    [Fact]
    public void ConformMissingMatches()
    {
        // Missing Item (BYN2803) + missing count (BYN2802) + missing getVal (BYN2802) + missing errMethod (BYN2802) = 4
        var expected = new List<string> { "missCount: 4 hasGetterFix: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "conformMissing", expected);
    }

    [Fact]
    public void ConformMismatchMatches()
    {
        // field type mismatch (BYN2801) + method sig mismatch (BYN2801) + errMethod error set mismatch (BYN2801) = 3
        var expected = new List<string> { "misCount: 3 hasE0Sig: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "conformMismatch", expected);
    }

    [Fact]
    public void ConformRefMismatchMatches()
    {
        // ref this mismatch (BYN2814) = 1
        var expected = new List<string> { "refCount: 1 refFound: true refHasFix: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "conformRefMismatch", expected);
    }

    [Fact]
    public void ConformCollisionMatches()
    {
        var expected = new List<string> { "collCount: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "conformCollision", expected);
    }

    [Fact]
    public void CheckConformancesMatches()
    {
        var expected = new List<string> { "afterChecked: true fnsAdded: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "checkConformances", expected);
    }
}
