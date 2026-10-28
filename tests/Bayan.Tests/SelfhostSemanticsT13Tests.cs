using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T13: <c>selfhost/semantics/generics.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT13Tests
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

            // ── 1. Instantiating Begin / End & Diagnostic Forwarding ──
            let scope0 = instantiatingBegin(ref c, from: 0, at: TextSpan { start: 10, length: 5 })
            report(ref c, for: .constraintNotSatisfied, from: 0, at: TextSpan { start: 1, length: 2 }, by: ("arg", "iface", "missing", "", ""))
            report(ref c, for: .duplicateDeclaration, from: 0, at: TextSpan { start: 3, length: 4 }, by: ("dup", "", "", "", ""))
            instantiatingEnd(ref c, by: scope0)
            print("== instantiating")
            print($"diags: {c.diagnostics.length}")
            for d in c.diagnostics {
                print($"{d.code} label: {d.label}")
            }

            // ── 2. invokeSymbol ──
            var pTypes = List<Type>()
            pTypes.append(prim(PrimitiveKind.i32))
            pTypes.append(prim(PrimitiveKind.string))
            var fData = FunctionData {
                parameters: move pTypes,
                ret: prim(PrimitiveKind.bool),
                errorSets: List<ErrorSetId>(),
                effects: List<string>(),
            }
            c.functionTypes.append(move fData)
            let fnType = makeType(.function, at: c.functionTypes.length - 1)
            let inv1 = invokeSymbol(ref c, for: fnType)
            let inv2 = invokeSymbol(ref c, for: fnType)
            print("== invokeSymbol")
            print($"same: {inv1 == inv2} name: {c.functions[inv1].name} params: {c.functions[inv1].parameters.length} retBool: {kindOf(c.functions[inv1].returnType) == .primitive}")

            // ── 3. templateOf ──
            let tFn = newFunction(ref c, by: FunctionSymbol { name: "tFn", template: -1 })
            let iFn = newFunction(ref c, by: FunctionSymbol { name: "iFn", template: tFn })
            print("== templateOf")
            let hasTmpl = templateOf(ref c, for: tFn) is not null
            let matchTmpl = templateOf(ref c, for: iFn) == tFn
            print($"tFnHasTemplate: {hasTmpl} iFnTemplateMatches: {matchTmpl}")

            // ── 4. errorSetsOf ──
            var es1 = List<ErrorSetId>()
            es1.append(0)
            let fnDeclared = newFunction(ref c, by: FunctionSymbol { name: "fDec", errorSets: move es1, infersErrors: false })
            var es2 = List<ErrorSetId>()
            es2.append(0)
            let fnInferred = newFunction(ref c, by: FunctionSymbol { name: "fInf", errorSets: move es2, infersErrors: true })
            print("== errorSetsOf")
            print($"decCount: {errorSetsOf(ref c, for: fnDeclared).length} infCount: {errorSetsOf(ref c, for: fnInferred).length}")

            // ── 5. bindConstant & circular detection ──
            let k0 = newConst(ref c, by: ConstSymbol { name: "kNorm", state: 0, file: 0, nameSpan: TextSpan { start: 1, length: 1 } })
            bindConstant(ref c, for: k0)
            let kCirc = newConst(ref c, by: ConstSymbol { name: "kCirc", state: 1, file: 0, nameSpan: TextSpan { start: 2, length: 1 } })
            bindConstant(ref c, for: kCirc)
            print("== bindConstant")
            print($"kNormState: {c.consts[k0].state} kCircState: {c.consts[kCirc].state} isError: {isError(c.consts[kCirc].type)}")

            // ── 6. reportHeld ──
            var held = List<Diag>()
            held.append(Diag {
                code: "HELD01",
                message: "held msg",
                span: TextSpan { start: 0, length: 0 },
                fixSpan: TextSpan { start: 0, length: 0 },
                related: List<Related>(),
            })
            reportHeld(ref c, for: in held)
            print("== reportHeld")
            print($"heldFound: {c.diagnostics[c.diagnostics.length - 1].code == "HELD01"}")

            // ── 7. Setup AST and checkConstraints ──
            let srcText = "struct StructTarget : IConform { }\nstruct StructNonConform { }\ninterface IConform { }\nvoid genFn<T>() where T : IConform { }\nstruct GenStruct<T> where T : IConform { }\nvoid regFn() { }\n"
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

            let ifaceId = newInterface(ref c, by: InterfaceSymbol { name: "IConform", file: 0, module: 0 })
            c.modules[0].interfaces.append(ifaceId)
            c.modules[0].types["IConform"] = makeType(.interfaceType, at: ifaceId)
            let sConfId = newStruct(ref c, by: StructSymbol { name: "StructTarget", file: 0, module: 0 })
            c.structs[sConfId].bases.append(ifaceId)
            c.modules[0].structs.append(sConfId)
            c.modules[0].types["StructTarget"] = makeType(.structType, at: sConfId)
            let sNonConfId = newStruct(ref c, by: StructSymbol { name: "StructNonConform", file: 0, module: 0 })
            c.modules[0].structs.append(sNonConfId)
            c.modules[0].types["StructNonConform"] = makeType(.structType, at: sNonConfId)

            let propId = newProperty(ref c, by: PropertySymbol { name: "reqProp", file: 0 })
            c.interfaces[ifaceId].properties.append(propId)
            let sWithProp = newStruct(ref c, by: StructSymbol { name: "StructWithProp", file: 0, module: 0 })
            let sPropId = newProperty(ref c, by: PropertySymbol { name: "reqProp", file: 0 })
            c.structs[sWithProp].properties.append(sPropId)
            let miss1 = t13MissingMember(in c, for: makeType(.structType, at: sNonConfId), to: ifaceId)
            let miss2 = t13MissingMember(in c, for: makeType(.structType, at: sWithProp), to: ifaceId)

            var tpList = List<TypeParamId>()
            var tpConstraints = List<InterfaceId>()
            tpConstraints.append(ifaceId)
            let tpId = newTypeParam(ref c, by: TypeParam { name: "T", constraints: move tpConstraints })
            tpList.append(tpId)

            var okArgs = List<Type>()
            okArgs.append(makeType(.structType, at: sConfId))
            var badArgs = List<Type>()
            badArgs.append(makeType(.structType, at: sNonConfId))

            switch c.files[0].unit.members[3] {
                case .functionDecl(let fDecl):
                    let satOk = checkConstraints(ref c, for: in tpList, through: in fDecl.value.where, from: 0, as: -1, by: in null, to: in okArgs)
                    let satBad = checkConstraints(ref c, for: in tpList, through: in fDecl.value.where, from: 0, as: -1, by: in null, to: in badArgs)
                    print("== checkConstraints")
                    print($"satOk: {satOk} satBad: {satBad} miss1: {miss1} miss2: {miss2}")
                default:
                    break
            }

            // ── 8. instantiateFunction ──
            let fnTmpl = newFunction(ref c, by: FunctionSymbol {
                name: "genFn",
                file: 0,
                module: 0,
                declIndex: 3,
                typeParams: copyIds(in tpList),
            })
            c.modules[0].allFunctions.append(fnTmpl)
            let fnInst1 = instantiateFunction(ref c, for: fnTmpl, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })
            let fnInst2 = instantiateFunction(ref c, for: fnTmpl, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })
            print("== instantiateFunction")
            let instSame = fnInst1 == fnInst2
            let tmplMatches = c.functions[fnInst1].template == fnTmpl
            print($"fnInstCreated: {fnInst1 >= 0} fnDeduplicated: {instSame} templateMatches: {tmplMatches}")

            // ── 9. instantiateType ──
            let sTmpl = newStruct(ref c, by: StructSymbol {
                name: "GenStruct",
                file: 0,
                module: 0,
                declIndex: 4,
                typeParams: copyIds(in tpList),
            })
            c.modules[0].structs.append(sTmpl)
            let sTmplType = makeType(.structType, at: sTmpl)
            let typeInst1 = instantiateType(ref c, for: sTmplType, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })
            let typeInst2 = instantiateType(ref c, for: sTmplType, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })

            let uTmpl = newUnion(ref c, by: UnionSymbol {
                name: "GenUnion",
                file: 0,
                module: 0,
                declIndex: -1,
                typeParams: copyIds(in tpList),
            })
            c.modules[0].unions.append(uTmpl)
            let uTmplType = makeType(.unionType, at: uTmpl)
            let uInst1 = instantiateType(ref c, for: uTmplType, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })
            let uInst2 = instantiateType(ref c, for: uTmplType, by: in okArgs, from: 0, at: TextSpan { start: 0, length: 0 })

            print("== instantiateType")
            let typeDiff = typeInst1 != sTmplType
            let typeDedupe = typeInst1 == typeInst2
            let unionDedupe = uInst1 == uInst2
            print($"typeInstCreated: {typeDiff} typeDeduplicated: {typeDedupe} unionDeduplicated: {unionDedupe}")

            // ── 10. inferErrorSets ──
            var errSetIds = List<ErrorSetId>()
            errSetIds.append(0)
            let tmplWithErrors = newFunction(ref c, by: FunctionSymbol {
                name: "fTmplErrors",
                errorSets: move errSetIds,
            })
            let instOfErrors = newFunction(ref c, by: FunctionSymbol {
                name: "fInstErrors",
                template: tmplWithErrors,
                infersErrors: true,
            })
            let chainFn = newFunction(ref c, by: FunctionSymbol {
                name: "fChainErrors",
                template: instOfErrors,
                infersErrors: true,
            })
            c.modules[0].allFunctions.append(chainFn)
            c.modules[0].allFunctions.append(instOfErrors)
            let preCount = c.functions[instOfErrors].errorSets.length
            inferErrorSets(ref c)
            let postCount = c.functions[instOfErrors].errorSets.length
            let chainCount = c.functions[chainFn].errorSets.length
            print("== inferErrorSets")
            print($"preCount: {preCount} postCount: {postCount} chainCount: {chainCount}")

            // ── 11. bindAll & worklist draining ──
            let regFn = newFunction(ref c, by: FunctionSymbol {
                name: "regFn",
                file: 0,
                module: 0,
                declIndex: 5,
                template: -1,
                body: -1,
            })
            c.modules[0].allFunctions.append(regFn)
            print("== bindAll")
            let preWork = c.instantiationWork.length
            bindAll(ref c)
            let postWork = c.instantiationWork.length
            let regFnBound = c.functions[regFn].body >= 0
            print($"preWork: {preWork} postWork: {postWork} regFnBound: {regFnBound}")

            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void InstantiatingMatches()
    {
        var expected = new List<string> {
            "diags: 1",
            "BYN2807 label: the generic code needs this of its type arguments",
        };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "instantiating", expected);
    }

    [Fact]
    public void InvokeSymbolMatches()
    {
        var expected = new List<string> { "same: true name: invoke params: 2 retBool: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "invokeSymbol", expected);
    }

    [Fact]
    public void TemplateOfMatches()
    {
        var expected = new List<string> { "tFnHasTemplate: false iFnTemplateMatches: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "templateOf", expected);
    }

    [Fact]
    public void ErrorSetsOfMatches()
    {
        var expected = new List<string> { "decCount: 1 infCount: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "errorSetsOf", expected);
    }

    [Fact]
    public void BindConstantMatches()
    {
        var expected = new List<string> { "kNormState: 2 kCircState: 2 isError: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindConstant", expected);
    }

    [Fact]
    public void ReportHeldMatches()
    {
        var expected = new List<string> { "heldFound: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportHeld", expected);
    }

    [Fact]
    public void CheckConstraintsMatches()
    {
        var expected = new List<string> { "satOk: true satBad: false miss1: reqProp miss2: : IConform" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "checkConstraints", expected);
    }

    [Fact]
    public void InstantiateFunctionMatches()
    {
        var expected = new List<string> { "fnInstCreated: true fnDeduplicated: true templateMatches: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "instantiateFunction", expected);
    }

    [Fact]
    public void InstantiateTypeMatches()
    {
        var expected = new List<string> { "typeInstCreated: true typeDeduplicated: true unionDeduplicated: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "instantiateType", expected);
    }

    [Fact]
    public void InferErrorSetsMatches()
    {
        var expected = new List<string> { "preCount: 0 postCount: 1 chainCount: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "inferErrorSets", expected);
    }

    [Fact]
    public void BindAllMatches()
    {
        var expected = new List<string> { "preWork: 1 postWork: 0 regFnBound: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindAll", expected);
    }
}
