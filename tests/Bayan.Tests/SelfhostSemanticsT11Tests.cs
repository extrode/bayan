using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T11: <c>selfhost/semantics/compile.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT11Tests
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

            // ── 1. associatedType ──────────────────────────────────────────
            let tp0 = newTypeParam(ref c, by: TypeParam { name: "T", constraints: List<InterfaceId>() })
            let a1 = associatedType(ref c, for: tp0, by: "Item")
            let a2 = associatedType(ref c, for: tp0, by: "Item")
            let a3 = associatedType(ref c, for: tp0, by: "Other")
            print("== associated")
            print($"{c.typeParams[a1].name} same: {a1 == a2} diff: {a1 != a3}")

            // ── 2. sinks (reportInto) ──────────────────────────────────────
            c.bags.append(DiagBag { items: List<Diag>(), previous: -1 })
            let bagId = c.bags.length - 1
            reportInto(ref c, to: bagId)
            report(ref c, for: .unknownAttribute, from: -1, at: TextSpan { start: 0, length: 0 }, by: ("foo", "", "", "", ""))
            print("== sinks")
            print($"outer: {c.diagnostics.length} bag: {c.bags[bagId].items.length}")
            reportInto(ref c, to: -1)

            // ── 3. afterMembersDeclared & runWork (.keyCheck) ──────────────
            var work = makeWork(.keyCheck, subject: prim(PrimitiveKind.void))
            work.span = TextSpan { start: 5, length: 4 }
            afterMembersDeclared(ref c, for: in work)
            print("== afterMembersDeclared")
            print($"pending: {c.pendingKeys.length} kind: {c.pendingKeys[0].kind == .keyCheck}")
            runWork(ref c, for: in work)
            print("== keyCheck")
            for i in 0..c.diagnostics.length {
                if c.diagnostics[i].code == "BYN2760" { print($"keyCheck error: {c.diagnostics[i].code}") }
            }

            // Setup a dummy file and module for declaration tests
            var bytes = List<u8>()
            let srcText = "struct S<T> where T : I, S where Bad : I { }\ninterface I { }\nfn foo() { }\ntype TAlias = i32\n"
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

            var spanS = TextSpan { start: 0, length: 0 }
            var spanI = TextSpan { start: 0, length: 0 }
            var spanF = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[0] {
                case .structDecl(let n): spanS = n.value.name
                default: break
            }
            switch c.files[0].unit.members[1] {
                case .interfaceDecl(let n): spanI = n.value.name
                default: break
            }
            switch c.files[0].unit.members[2] {
                case .functionDecl(let n): spanF = n.value.name
                default: break
            }

            // ── 4. addType (success and duplicate) ─────────────────────────
            let sId = newStruct(ref c, by: StructSymbol { name: "S", nameSpan: spanS, span: spanS, module: 0, file: 0, declIndex: 0, memberIndex: -1, access: .module, modifiers: 0 })
            let ok1 = addType(ref c, from: 0, at: spanS, as: structTypeOf(sId))
            let ok2 = addType(ref c, from: 0, at: spanS, as: structTypeOf(sId))
            print("== addType")
            print($"first: {ok1} second: {ok2}")

            // ── 5. addFunction ─────────────────────────────────────────────
            let fnId = newFunction(ref c, by: FunctionSymbol { name: "foo", nameSpan: spanF, span: spanF, module: 0, kind: .function, access: .module, modifiers: 0, file: 0, declIndex: 2, memberIndex: -1 })
            addFunction(ref c, from: 0, at: spanF, for: fnId)
            let fnList = copyIds(in c.modules[0].functions["foo"])
            print("== addFunction")
            print($"count: {fnList.length} fn: {fnList[0] == fnId}")

            // ── 6. declareTypeParameters (unique and duplicate) ────────────
            var tpParams = List<TypeParamId>()
            var tpNames = List<TextSpan>()
            switch c.files[0].unit.members[0] {
                case .structDecl(let sNode):
                    for tp in sNode.value.typeParams { tpNames.append(tp) }
                default:
                    break
            }
            tpNames.append(spanF)
            let tpMap = declareTypeParameters(ref c, from: 0, for: in tpNames, to: ref tpParams)
            var dupNames = List<TextSpan>()
            dupNames.append(spanS)
            dupNames.append(spanS)
            var dupParams = List<TypeParamId>()
            let dupMap = declareTypeParameters(ref c, from: 0, for: in dupNames, to: ref dupParams)
            let hasMap = tpMap is not null
            print("== declareTypeParameters")
            print($"unique: {tpParams.length} hasMap: {hasMap} dup: {dupParams.length}")

            // ── 7. resolveInterface & declareWhere ─────────────────────────
            let ifaceId = newInterface(ref c, by: InterfaceSymbol { name: "I", nameSpan: spanI, span: spanI, module: 0, file: 0, declIndex: 1, memberIndex: -1, access: .module, modifiers: 0 })
            c.modules[0].interfaces.append(ifaceId)
            let _ = addType(ref c, from: 0, at: spanI, as: interfaceTypeOf(ifaceId))
            let types = TypeResolver { file: 0, container: -1, typeArgs: Map<string, Type>() }
            var resIface = -1
            var resStruct = -1
            switch c.files[0].unit.members[0] {
                case .structDecl(let sNode):
                    switch sNode.value.where[0].bounds[0] {
                        case .namedType(let nt):
                            resIface = resolveInterface(ref c, through: in types, from: 0, for: in nt.value)
                        default:
                            break
                    }
                    switch sNode.value.where[0].bounds[1] {
                        case .namedType(let nt):
                            resStruct = resolveInterface(ref c, through: in types, from: 0, for: in nt.value)
                        default:
                            break
                    }
                default:
                    break
            }
            print("== resolveInterface")
            print($"iface: {resIface == ifaceId} structAsIface: {resStruct}")

            // ── 8. declareWhere: valid ────────────────────────────────────
            switch c.files[0].unit.members[0] {
                case .structDecl(let sNode):
                    declareWhere(ref c, from: 0, for: in sNode.value.where, to: in tpParams, by: in tpMap, as: "S")
                default:
                    break
            }
            print("== declareWhere")
            print($"constraints: {c.typeParams[tpParams[0]].constraints.length}")

            // ── 9. module loading & declareFile via loadModule ─────────────
            var srcList = List<lexer.SourceText>()
            var mBytes = List<u8>()
            let modSrc = "@cases\nstruct Point {\n    i32 x\n}\n\n@positional\nstruct Pos {\n    i32 x\n}\n\n@sealed\nenum Color {\n    red\n}\n\n@cases\nunion error BadErr {\n    failed\n}\n\ninterface BaseI {\n}\n\ninterface ISub<T> where T : BaseI {\n}\n\nvoid fnWithWhere<T>() where T : BaseI {\n}\n\n@cases\nextension Ext : IShape {\n}\n\n@cases\nconst badConst = 10\n\nconst k = 10\n\ni32 main() => 0\n"
            for b in modSrc.bytes { mBytes.append(b) }
            srcList.append(lexer.SourceText.create("app/main.byn", from: move mBytes))
            let loadedMod = loadModule(ref c, for: "app", at: "app", from: move srcList, by: -1)
            print("== loadModule")
            print($"modId: {loadedMod} name: {c.modules[loadedMod].name} files: {c.modules[loadedMod].files.length}")

            let fileId = c.modules[loadedMod].files[0]
            var wheres = List<Work>()
            t11DeclareFile(ref c, for: fileId, to: ref wheres)
            for wIdx in 0..wheres.length {
                runWork(ref c, for: in wheres[wIdx])
            }
            print("== declareFile")
            let ifaceId = c.modules[loadedMod].interfaces[1]
            let fnId = c.modules[loadedMod].functions["fnWithWhere"][0]
            let iSubC = c.typeParams[c.interfaces[ifaceId].typeParams[0]].constraints.length
            let fnC = c.typeParams[c.functions[fnId].typeParams[0]].constraints.length
            print($"structs: {c.modules[loadedMod].structs.length} enums: {c.modules[loadedMod].enums.length} interfaces: {c.modules[loadedMod].interfaces.length} errorSets: {c.modules[loadedMod].errorSets.length} constants: {c.modules[loadedMod].allConstants.length} main: {c.modules[loadedMod].main >= 0} iSub: {iSubC} fn: {fnC}")

            // ── 10. resolveImport (single segment + cycle) ────────────────
            var impBytes = List<u8>()
            var impCopy = List<u8>()
            let impSrc = "import missing\nimport cycleMod\n"
            for b in impSrc.bytes {
                impBytes.append(b)
                impCopy.append(b)
            }
            let impSource = lexer.SourceText.create("app/imp.byn", from: move impBytes)
            var impParser = parser.Parser.create(lexer.lex(move impSource, by: lexer.Keywords.build()))
            let impUnit = parser.parseCompilationUnit(ref impParser)
            let impFile = SourceFile {
                path: "app/imp.byn",
                displayPath: "app/imp.byn",
                text: move impCopy,
                unit: move impUnit,
                module: 0,
                isEntry: false,
                isTestFile: false,
                imports: Map<string, ModId>(),
            }
            c.files.append(move impFile)
            let impFileId = c.files.length - 1

            var cycBytes = List<u8>()
            var cycCopy = List<u8>()
            for b in impSrc.bytes {
                cycBytes.append(b)
                cycCopy.append(b)
            }
            let cycSource = lexer.SourceText.create("cycleMod/cycle.byn", from: move cycBytes)
            var cycParser = parser.Parser.create(lexer.lex(move cycSource, by: lexer.Keywords.build()))
            let cycUnit = parser.parseCompilationUnit(ref cycParser)
            let cycFile = SourceFile {
                path: "cycleMod/cycle.byn",
                displayPath: "cycleMod/cycle.byn",
                text: move cycCopy,
                unit: move cycUnit,
                module: -1,
                isEntry: false,
                isTestFile: false,
                imports: Map<string, ModId>(),
            }
            c.files.append(move cycFile)

            switch c.files[impFileId].unit.members[0] {
                case .importDecl(let n):
                    resolveImport(ref c, from: impFileId, for: in n.value)
                default:
                    break
            }
            switch c.files[impFileId].unit.members[1] {
                case .importDecl(let n):
                    resolveImport(ref c, from: impFileId, for: in n.value)
                default:
                    break
            }
            print("== resolveImport")
            print($"fileImports: {c.files[impFileId].imports.length}")

            // ── 11. declare ───────────────────────────────────────────────
            afterMembersDeclared(ref c, for: in makeWork(.keyCheck, subject: prim(PrimitiveKind.void)))
            let prePending = c.pendingKeys.length
            let preDiags = c.diagnostics.length
            declare(ref c)
            let diagsAdded = c.diagnostics.length - preDiags
            print("== declare")
            print($"pending: {prePending} added: {diagsAdded}")

            // Check diagnostics emitted across the whole run
            print("== diagnostics")
            for i in 0..c.diagnostics.length {
                print($"{c.diagnostics[i].code}")
            }

            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void AssociatedTypeMatches()
    {
        var expected = new List<string> { "T.Item same: true diff: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "associated", expected);
    }

    [Fact]
    public void SinksMatches()
    {
        var expected = new List<string> { "outer: 0 bag: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "sinks", expected);
    }

    [Fact]
    public void AfterMembersDeclaredMatches()
    {
        var expected = new List<string> { "pending: 1 kind: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "afterMembersDeclared", expected);
    }

    [Fact]
    public void KeyCheckMatches()
    {
        var expected = new List<string> { "keyCheck error: BYN2760" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "keyCheck", expected);
    }

    [Fact]
    public void AddTypeMatches()
    {
        var expected = new List<string> { "first: true second: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "addType", expected);
    }

    [Fact]
    public void AddFunctionMatches()
    {
        var expected = new List<string> { "count: 1 fn: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "addFunction", expected);
    }

    [Fact]
    public void DeclareTypeParametersMatches()
    {
        var expected = new List<string> { "unique: 2 hasMap: true dup: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "declareTypeParameters", expected);
    }

    [Fact]
    public void ResolveInterfaceMatches()
    {
        var expected = new List<string> { "iface: true structAsIface: -1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "resolveInterface", expected);
    }

    [Fact]
    public void DeclareWhereMatches()
    {
        var expected = new List<string> { "constraints: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "declareWhere", expected);
    }

    [Fact]
    public void LoadModuleMatches()
    {
        var expected = new List<string> { "modId: 1 name: app files: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "loadModule", expected);
    }

    [Fact]
    public void DeclareFileMatches()
    {
        var expected = new List<string> { "structs: 2 enums: 1 interfaces: 2 errorSets: 1 constants: 2 main: true iSub: 1 fn: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "declareFile", expected);
    }

    [Fact]
    public void ResolveImportMatches()
    {
        var expected = new List<string> { "fileImports: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "resolveImport", expected);
    }

    [Fact]
    public void DeclareMatches()
    {
        var expected = new List<string> { "pending: 2 added: 24" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "declare", expected);
    }

    [Fact]
    public void DiagnosticsMatches()
    {
        var diags = Sections.Value["diagnostics"];
        Assert.Contains("BYN2760", diags); // invalidMapKey
        Assert.Contains("BYN2006", diags); // duplicateDeclaration
        Assert.Contains("BYN2804", diags); // notAnInterface
        Assert.Contains("BYN2812", diags); // whereUnknownParameter
        Assert.Contains("BYN1016", diags); // modifierNotAllowed (@cases on struct, @sealed on enum, @cases on extension, @cases on const, @cases on union error)
        Assert.Contains("BYN2004", diags); // unknownModule
        Assert.Contains("BYN2010", diags); // importCycle
        Assert.Contains("BYN2811", diags); // unsupported
        Assert.DoesNotContain("BYN2001", diags); // unknownAttribute must never be emitted for valid @positional
        Assert.Equal(10, diags.Count(d => d == "BYN1016")); // 10 modifierNotAllowed across 2 passes
    }
}
