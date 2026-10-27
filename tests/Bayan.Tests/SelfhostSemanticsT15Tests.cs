using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T15: <c>selfhost/semantics/binder.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT15Tests
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

            // ── 1. Setup AST and Symbols ──────────────────────────────────
            let srcText = "struct Point {\n    i32 x\n    Point() { }\n    i32 count => 42\n}\nfn compute(i32 a, i32 b = 10) requires a > 0 => a + b\ntest \"sample\" (i32 x) { }\nfn checkSingle(i32 x) requires x > 0 => x\nenum Color(i32 val) {\n    red(10)\n}\n"

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

            let dummySpan = TextSpan { start: 0, length: 5 }
            let ptStructId = newStruct(ref c, by: StructSymbol {
                name: "Point",
                span: dummySpan,
                file: 0,
                declIndex: 0,
            })
            let ptFieldId = newField(ref c, by: FieldSymbol {
                name: "x",
                span: dummySpan,
                owner: ptStructId,
                file: 0,
                declIndex: 0,
                memberIndex: 0,
                type: prim(PrimitiveKind.i32),
            })
            c.structs[ptStructId].fields.append(ptFieldId)

            // ── 2. Hold and Release ───────────────────────────────────────
            var bPass = Binder {
                file: 0,
                fn: -1,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            let heldBag = hold(ref bPass, through: ref c)
            report(ref bPass, through: ref c, for: .notConstant, at: dummySpan, by: ("held error", "", "", "", ""))
            let heldCount = c.bags[heldBag].items.length
            let heldFixFalse = c.bags[heldBag].items[0].hasFix == false
            let diagsBeforeRelease = c.diagnostics.length

            // Setup constraints for release seeds
            bPass.pending[0] = 100 // local 0 maps to decl span 100
            bPass.constraints[100] = prim(PrimitiveKind.i64) // i64 constraint
            let seeds = release(ref bPass, through: ref c)
            let seedsCount = seeds.length
            var seedTypeIsI64 = false
            for (loc, t) in seeds {
                if t == prim(PrimitiveKind.i64) { seedTypeIsI64 = true }
            }
            let diagsAfterRelease = c.diagnostics.length

            print("== holdAndRelease")
            print($"heldCount: {heldCount} fixFalse: {heldFixFalse} diagsBefore: {diagsBeforeRelease} seeds: {seedsCount} i64Seed: {seedTypeIsI64} diagsAfter: {diagsAfterRelease}")

            // ── 3. Scopes, Locals, Shadowing ──────────────────────────────
            var bScope = Binder {
                file: 0,
                fn: -1,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            pushScope(ref bScope)
            let spanX = TextSpan { start: 0, length: 1 }
            let locX = declareLocal(ref bScope, through: ref c, at: spanX, as: prim(PrimitiveKind.i32), for: .letKeyword)
            let locDiscard = declareLocal(ref bScope, through: ref c, at: TextSpan { start: 0, length: 0 }, as: prim(PrimitiveKind.i32), for: .letKeyword)

            // Inner scope shadowing
            pushScope(ref bScope)
            let locShadow = declareLocal(ref bScope, through: ref c, at: spanX, as: prim(PrimitiveKind.string), for: .letKeyword)
            popScope(ref bScope)

            // Same-scope let shadowing with same type (reports error)
            let locSameType = declareLocal(ref bScope, through: ref c, at: spanX, as: prim(PrimitiveKind.i32), for: .letKeyword)
            popScope(ref bScope)

            var hasShadow = false
            var hasSameTypeShadow = false
            for d in c.diagnostics {
                if d.code == "BYN2007" { hasShadow = true }
                if d.code == "BYN2028" { hasSameTypeShadow = true }
            }

            print("== scopesAndLocals")
            print($"locX: {locX >= 0} shadow: {hasShadow} sameTypeShadow: {hasSameTypeShadow}")

            // ── 4. ResolveType ────────────────────────────────────────────
            var bType = Binder {
                file: 0,
                fn: -1,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            var resResolved = false
            switch c.files[0].unit.members[1] {
                case .functionDecl(let fNode):
                    let resI32 = resolveType(ref bType, through: ref c, for: in fNode.value.returnType)
                    resResolved = resI32 != -1
                default:
                    break
            }
            print("== resolveType")
            print($"resolved: {resResolved}")

            // ── 5. BindArraySize ──────────────────────────────────────────
            var bArr = Binder {
                file: 0,
                fn: -1,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            var sizeRes: i64 = 0
            switch c.files[0].unit.members[1] {
                case .functionDecl(let fNode):
                    sizeRes = bindArraySize(ref bArr, through: ref c, for: in fNode.value.expressionBody)
                default:
                    break
            }
            print("== bindArraySize")
            print($"sizeRes: {sizeRes}")

            // ── 6. BindExpressionBody ─────────────────────────────────────
            // fnRetId (0) has i32 returnType to kill fn >= 0 mutations on line 553
            var fnRet = FunctionSymbol {
                name: "doRet",
                nameSpan: dummySpan,
                span: dummySpan,
                module: 0,
                kind: .function,
                returnType: prim(PrimitiveKind.i32),
            }
            let fnRetId = newFunction(ref c, by: move fnRet)
            var bExprRet = Binder {
                file: 0,
                fn: fnRetId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            let retStmt = bindExpressionBody(ref bExprRet, through: ref c, for: in parser.Expr.none)

            var fnVoid = FunctionSymbol {
                name: "doVoid",
                nameSpan: dummySpan,
                span: dummySpan,
                module: 0,
                kind: .function,
                returnType: prim(PrimitiveKind.void),
            }
            let fnVoidId = newFunction(ref c, by: move fnVoid)
            var bExprVoid = Binder {
                file: 0,
                fn: fnVoidId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            let voidStmt = bindExpressionBody(ref bExprVoid, through: ref c, for: in parser.Expr.none)

            print("== bindExpressionBody")
            print($"voidKind: {boundKind(voidStmt) == .exprStmt} retKind: {boundKind(retStmt) == .returnStmt}")

            // ── 7. BindGetter and BindConstructor ─────────────────────────
            // fnRetId (0) is repurposed for getter testing to kill fn >= 0 mutations
            c.functions[0].returnType = prim(PrimitiveKind.i32)
            c.functions[0].body = -1
            var bGetter = Binder {
                file: 0,
                fn: 0,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }

            var ctorFn = FunctionSymbol {
                name: "init",
                nameSpan: dummySpan,
                span: dummySpan,
                module: 0,
                kind: .constructor,
                returnType: prim(PrimitiveKind.void),
            }
            let ctorFnId = newFunction(ref c, by: move ctorFn)
            var bCtor = Binder {
                file: 0,
                fn: ctorFnId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }

            switch c.files[0].unit.members[0] {
                case .structDecl(let sNode):
                    for sm in sNode.value.members {
                        switch sm {
                            case .constructorDecl(let cNode):
                                bindConstructor(ref bCtor, through: ref c, for: in cNode.value)
                            case .propertyDecl(let pNode):
                                bindGetter(ref bGetter, through: ref c, for: in pNode.value)
                            default:
                                break
                        }
                    }
                default:
                    break
            }

            print("== bindGetterAndCtor")
            print($"getterBody: {boundKind(c.functions[0].body) == .returnStmt} ctorBody: {c.functions[ctorFnId].body >= 0}")

            // ── 8. BindMain and BindTest ──────────────────────────────────
            var mainFn = FunctionSymbol {
                name: "main",
                nameSpan: dummySpan,
                span: dummySpan,
                module: 0,
                kind: .main,
                returnType: prim(PrimitiveKind.i32),
            }
            let mainFnId = newFunction(ref c, by: move mainFn)
            var bMain = Binder {
                file: 0,
                fn: mainFnId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            var gStmts = List<parser.GlobalStatement>()
            bindMain(ref bMain, through: ref c, for: in gStmts)

            // Test on function 0 to kill b.fn >= 0 mutations
            c.functions[0].body = -1
            var bTest = Binder {
                file: 0,
                fn: 0,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            switch c.files[0].unit.members[2] {
                case .testDecl(let tNode):
                    var testVals = List<parser.Expr>()
                    testVals.append(parser.Expr.none)
                    testVals.append(parser.Expr.none)
                    bindTest(ref bTest, through: ref c, for: in tNode.value, by: in testVals)
                default:
                    break
            }

            // Bind compute (2 params, requires) -> uniqueReads is 2, no BYN5011
            var bCompute = Binder {
                file: 0,
                fn: fnRetId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            switch c.files[0].unit.members[1] {
                case .functionDecl(let fNode):
                    bindFunctionBody(ref bCompute, through: ref c, for: in fNode.value)
                default:
                    break
            }
            var compute5011 = false
            for d in c.diagnostics {
                if d.code == "BYN5011" { compute5011 = true }
            }

            // Bind checkSingle (1 param, requires, !hasThis) -> uniqueReads is 1, reports BYN5011
            var bSingle = Binder {
                file: 0,
                fn: fnRetId,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            switch c.files[0].unit.members[3] {
                case .functionDecl(let fNode):
                    bindFunctionBody(ref bSingle, through: ref c, for: in fNode.value)
                default:
                    break
            }
            var single5011 = false
            for d in c.diagnostics {
                if d.code == "BYN5011" { single5011 = true }
            }

            let testBodyOk = c.functions[0].body >= 0 && c.bBlocks[boundIndex(c.functions[0].body)].statements.length == 1
            print("== bindMainAndTest")
            print($"mainBody: {c.functions[mainFnId].body >= 0} mainInfers: {c.functions[mainFnId].infersErrors} testBody: {testBodyOk} compute5011: {compute5011} single5011: {single5011}")

            // ── 9. Other Entry Points ─────────────────────────────────────
            var bSym = Binder {
                file: 0,
                fn: mainFnId,
                owner: ptStructId,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            checkValueName(ref bSym, through: ref c, at: dummySpan)

            let valErrId = newErrorSet(ref c, by: ErrorSetSymbol {
                name: "ValidationError",
                span: dummySpan,
                module: 0,
                file: 0,
                declIndex: 0,
            })
            let invVar = newErrorVariant(ref c, by: ErrorVariantSymbol {
                name: "invalid",
                span: dummySpan,
                set: valErrId,
            })
            let vFld = newLocal(ref c, by: LocalSymbol {
                name: "violations",
                nameSpan: dummySpan,
                span: dummySpan,
                type: prim(PrimitiveKind.i32),
                kind: .varKeyword,
            })
            c.errorVariants[invVar].fields.append(vFld)
            c.errorSets[valErrId].variants.append(invVar)
            c.validationErrors[structTypeOf(ptStructId)] = valErrId
            let stChecks = StructChecks {
                check: mainFnId,
            }
            bindChecks(ref bSym, through: ref c, for: in stChecks)
            let chkBody = c.functions[mainFnId].body
            let chkStmtsOk = chkBody >= 0 && c.bBlocks[boundIndex(chkBody)].statements.length > 0

            let pLoc = newLocal(ref c, by: LocalSymbol {
                name: "p",
                nameSpan: dummySpan,
                span: dummySpan,
                type: prim(PrimitiveKind.i32),
                kind: .letKeyword,
                isParameter: true,
            })
            let defP = bindParameterDefault(ref bSym, through: ref c, for: pLoc)

            let fldId = newField(ref c, by: FieldSymbol {
                name: "fld",
                span: dummySpan,
                owner: ptStructId,
                file: 0,
                declIndex: 0,
                memberIndex: 0,
                type: prim(PrimitiveKind.i32),
            })
            let defF = bindFieldDefault(ref bSym, through: ref c, for: fldId)
            let fldOob = newField(ref c, by: FieldSymbol {
                name: "fldOob",
                span: dummySpan,
                owner: c.structs.length,
                file: 0,
                declIndex: 0,
                memberIndex: 0,
                type: prim(PrimitiveKind.i32),
            })
            let defFOob = bindFieldDefault(ref bSym, through: ref c, for: fldOob)

            let eId = newEnum(ref c, by: EnumSymbol {
                name: "Color",
                span: dummySpan,
                module: 0,
                file: 0,
                declIndex: 4,
            })
            let eFld = newEnumField(ref c, by: EnumFieldSymbol {
                name: "val",
                span: dummySpan,
                owner: eId,
                type: prim(PrimitiveKind.i32),
            })
            c.enums[eId].fields.append(eFld)
            let eFldErr = newEnumField(ref c, by: EnumFieldSymbol {
                name: "errVal",
                span: dummySpan,
                owner: eId,
                type: errorType(),
            })
            c.enums[eId].fields.append(eFldErr)
            let eMem = newEnumMember(ref c, by: EnumMemberSymbol {
                name: "red",
                span: dummySpan,
                owner: eId,
                ordinal: 0,
            })
            c.enums[eId].members.append(eMem)
            bindEnumArguments(ref bSym, through: ref c, for: eId)
            var enumDiagCount = 0
            for d in c.diagnostics {
                if d.code == "BYN2723" { enumDiagCount += 1 }
            }
            let hasEnumDiag = enumDiagCount == 1

            let kId = newConst(ref c, by: ConstSymbol {
                name: "K",
                span: dummySpan,
                module: 0,
                file: 0,
                declIndex: 0,
                type: prim(PrimitiveKind.i32),
            })
            bindModuleConstant(ref bSym, through: ref c, for: kId)

            print("== entryPoints")
            print($"entryOk: {pLoc >= 0 && fldId >= 0 && defFOob == -1 && eId >= 0 && kId >= 0 && chkStmtsOk && hasEnumDiag}")

            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void HoldAndReleaseMatches()
    {
        var expected = new List<string> { "heldCount: 1 fixFalse: true diagsBefore: 0 seeds: 1 i64Seed: true diagsAfter: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "holdAndRelease", expected);
    }

    [Fact]
    public void ScopesAndLocalsMatches()
    {
        var expected = new List<string> { "locX: true shadow: true sameTypeShadow: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "scopesAndLocals", expected);
    }

    [Fact]
    public void ResolveTypeMatches()
    {
        var expected = new List<string> { "resolved: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "resolveType", expected);
    }

    [Fact]
    public void BindArraySizeMatches()
    {
        var expected = new List<string> { "sizeRes: -1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindArraySize", expected);
    }

    [Fact]
    public void BindExpressionBodyMatches()
    {
        var expected = new List<string> { "voidKind: true retKind: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindExpressionBody", expected);
    }

    [Fact]
    public void BindGetterAndCtorMatches()
    {
        var expected = new List<string> { "getterBody: true ctorBody: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindGetterAndCtor", expected);
    }

    [Fact]
    public void BindMainAndTestMatches()
    {
        var expected = new List<string> { "mainBody: true mainInfers: true testBody: true compute5011: false single5011: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindMainAndTest", expected);
    }

    [Fact]
    public void EntryPointsMatches()
    {
        var expected = new List<string> { "entryOk: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "entryPoints", expected);
    }
}
