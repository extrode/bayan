using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T16: <c>selfhost/semantics/binder2.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT16Tests
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

            let srcText = "@exitCode(42) enum Code42 { a }\n@exitCode(0) enum Code0 { b }\n@exitCode(300) enum Code300 { c }\n@exitCode enum CodeNoArgs { d }\n@exitCode(\"bad\") enum CodeStr { e }\nunique struct UniqueRes {\n    i32 id\n}\nstruct PlainRes {\n    i32 id\n}\nvoid testFunc(i32? maybeVal) {\n    {\n        let a = 1\n        var b = 2\n    }\n    let x = 10\n    var y: i32 = 20\n    const K = 30\n    x = 11\n    x++\n    if x > 0 {\n        let t = 1\n    } else {\n        let e = 2\n    }\n    unless x == 0 {\n        let u = 3\n    }\n    x = 5 if x > 0\n    while x > 0 {\n        x--\n    }\n    while let v = maybeVal {\n        let inside = v\n    }\n    do {\n        x++\n    } while x < 10\n    for i in 0..10 {\n        let z = i\n    }\n    loopLabel: while x > 0 {\n        loopLabel: while x > 0 {\n            break\n        }\n    }\n    badLabel: x = 1\n    defer {\n        let d = 1\n    }\n    assert x >= 0\n    assert 42\n    assert 42 fails MyError\n    ;\n    using let notDisp: i32 = 100\n    const uConst: UniqueRes = UniqueRes { id: 1 }\n    let uninitLet: i32\n    var uninitVar: i32\n}\n"

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
            let uStructId = newStruct(ref c, by: StructSymbol {
                name: "UniqueRes",
                span: dummySpan,
                file: 0,
                declIndex: 5,
                modifiers: 64, // unique modifier bit (parser's modifierUnique)
            })
            let pStructId = newStruct(ref c, by: StructSymbol {
                name: "PlainRes",
                span: dummySpan,
                file: 0,
                declIndex: 6,
            })
            c.modules[0].types["UniqueRes"] = makeType(.structType, at: uStructId)
            c.modules[0].types["PlainRes"] = makeType(.structType, at: pStructId)

            var b = Binder {
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
            pushScope(ref b)

            // ── 1. BindExitCode ───────────────────────────────────────────
            var code42 = 0
            var code0 = 0
            var code300 = 0
            var codeNoArgs = 0
            var codeStr = 0
            switch c.files[0].unit.members[0] {
                case .enumDecl(let en):
                    if en.value.attributes.length > 0 {
                        code42 = bindExitCode(ref b, through: ref c, for: in en.value.attributes[0])
                    }
                default:
            }
            switch c.files[0].unit.members[1] {
                case .enumDecl(let en):
                    if en.value.attributes.length > 0 {
                        code0 = bindExitCode(ref b, through: ref c, for: in en.value.attributes[0])
                    }
                default:
            }
            switch c.files[0].unit.members[2] {
                case .enumDecl(let en):
                    if en.value.attributes.length > 0 {
                        code300 = bindExitCode(ref b, through: ref c, for: in en.value.attributes[0])
                    }
                default:
            }
            switch c.files[0].unit.members[3] {
                case .enumDecl(let en):
                    if en.value.attributes.length > 0 {
                        codeNoArgs = bindExitCode(ref b, through: ref c, for: in en.value.attributes[0])
                    }
                default:
            }
            switch c.files[0].unit.members[4] {
                case .enumDecl(let en):
                    if en.value.attributes.length > 0 {
                        codeStr = bindExitCode(ref b, through: ref c, for: in en.value.attributes[0])
                    }
                default:
            }
            print("== bindExitCode")
            print($"code42: {code42} code0: {code0} code300: {code300} codeNoArgs: {codeNoArgs} codeStr: {codeStr}")

            // ── 2. BindBlock and BindBody ─────────────────────────────────
            var blockBoundOk = false
            var bodySingleOk = false
            switch c.files[0].unit.members[7] {
                case .functionDecl(let fnDecl):
                    switch fnDecl.value.body {
                        case .blockStmt(let blk):
                            let bStmt = bindBlock(ref b, through: ref c, for: in blk.value)
                            blockBoundOk = boundKind(bStmt) == .block
                            if blk.value.statements.length > 0 {
                                let single = bindBody(ref b, through: ref c, for: in blk.value.statements[0])
                                bodySingleOk = boundKind(single) == .block
                            }
                        default:
                    }
                default:
            }
            print("== bindBlockAndBody")
            print($"blockOk: {blockBoundOk} bodyOk: {bodySingleOk}")

            // ── 3. BindStatements ─────────────────────────────────────────
            var stmtsCount = 0
            var hasBlock = false
            var hasLocal = false
            var hasIf = false
            var hasWhile = false
            var hasFor = false
            var hasDefer = false
            var hasAssert = false
            var varKindOk = false
            var forBorrows = false
            switch c.files[0].unit.members[7] {
                case .functionDecl(let fnDecl):
                    switch fnDecl.value.body {
                        case .blockStmt(let blk):
                            stmtsCount = blk.value.statements.length
                            for s in blk.value.statements {
                                let bound = bindStatement(ref b, through: ref c, for: in s)
                                switch boundKind(bound) {
                                    case .block: hasBlock = true
                                    case .localDecl:
                                        hasLocal = true
                                        let ldIdx = boundIndex(bound)
                                        let loc = c.bLocalDecls[ldIdx].local
                                        if c.locals[loc].name == "y" && c.locals[loc].kind == .varKeyword {
                                            varKindOk = true
                                        }
                                    case .ifStmt: hasIf = true
                                    case .whileStmt or .doWhile: hasWhile = true
                                    case .forStmt:
                                        hasFor = true
                                        let fIdx = boundIndex(bound)
                                        let forVar = c.bFors[fIdx].variable
                                        if c.locals[forVar].borrowsSubject {
                                            forBorrows = true
                                        }
                                    case .deferStmt: hasDefer = true
                                    case .assertStmt: hasAssert = true
                                    default:
                                }
                            }
                        default:
                    }
                default:
            }
            print("== bindStatements")
            print($"stmtsCount: {stmtsCount} block: {hasBlock} local: {hasLocal} varKindOk: {varKindOk} if: {hasIf} while: {hasWhile} for: {hasFor} forBorrows: {forBorrows} defer: {hasDefer} assert: {hasAssert}")

            // ── 4. Diagnostics Checks ─────────────────────────────────────
            var has2772 = 0
            var has2006 = false
            var has3209 = false
            var has2408 = false
            var has2101 = false
            var has2775 = false
            var has2226 = false
            var has2605 = false
            for d in c.diagnostics {
                if d.code == "BYN2772" { has2772 += 1 }
                if d.code == "BYN2006" { has2006 = true }
                if d.code == "BYN3209" { has3209 = true }
                if d.code == "BYN2408" { has2408 = true }
                if d.code == "BYN2101" { has2101 = true }
                if d.code == "BYN2775" { has2775 = true }
                if d.code == "BYN2226" { has2226 = true }
                if d.code == "BYN2605" { has2605 = true }
            }
            print("== diagnostics")
            print($"d2772: {has2772} d2006: {has2006} d3209: {has3209} d2408: {has2408} d2101: {has2101} d2775: {has2775} d2226: {has2226} d2605: {has2605}")

            // ── 5. IsNonNull and Narrowing ─────────────────────────────────
            let locOpt = declareLocal(ref b, through: ref c, at: dummySpan, as: internOptional(ref c, by: prim(PrimitiveKind.i32)), for: .letKeyword)
            let nonNullBefore = isNonNull(ref b, for: locOpt)
            let sBegin = narrowedBegin(ref b, through: ref c, for: -1, whenTrue: true)
            b.nonNull.append((b.scopes.length - 1, locOpt))
            let nonNullDuring = isNonNull(ref b, for: locOpt)
            narrowedEnd(ref b, through: ref c, by: move sBegin)
            let nonNullAfter = isNonNull(ref b, for: locOpt)

            let locOptNode = addLocal(ref c, from: BLocal { span: dummySpan, local: locOpt, type: internOptional(ref c, by: prim(PrimitiveKind.i32)) })
            let nullDefNode = addDefault(ref c, from: BDefault { span: dummySpan, type: nullType() })
            let cpNode = addConstantPattern(ref c, from: BConstantPattern { span: dummySpan, inputType: nullType(), value: nullDefNode })
            let notPNode = addNotPattern(ref c, from: BNotPattern { span: dummySpan, inputType: nullType(), operand: cpNode })
            let isExpr = addIs(ref c, from: BIs { span: dummySpan, value: locOptNode, pattern: notPNode })
            let sWhen = narrowedBegin(ref b, through: ref c, for: isExpr, whenTrue: true)
            let isNonNullWhen = isNonNull(ref b, for: locOpt)
            narrowedEnd(ref b, through: ref c, by: move sWhen)

            print("== isNonNullAndNarrowing")
            print($"before: {nonNullBefore} during: {nonNullDuring} after: {nonNullAfter} whenTrue: {isNonNullWhen}")

            // ── 6. DefaultType ────────────────────────────────────────────
            let litI32 = addLiteral(ref c, from: BLiteral {
                span: dummySpan,
                type: prim(PrimitiveKind.i32),
                value: Constant { kind: .integer, integer: Big { lo: 42, hi: 0 } },
            })
            var defI32: Type = -1
            var defNull: Type = -1
            var defVoid: Type = -1
            switch c.files[0].unit.members[7] {
                case .functionDecl(let fnDecl):
                    switch fnDecl.value.body {
                        case .blockStmt(let blk):
                            switch blk.value.statements[1] {
                                case .localDecl(let ld):
                                    defI32 = defaultType(ref b, through: ref c, for: litI32, from: in ld.value.init)
                                    let nullDef = addDefault(ref c, from: BDefault { span: dummySpan, type: nullType() })
                                    defNull = defaultType(ref b, through: ref c, for: nullDef, from: in ld.value.init)
                                    let voidDef = addDefault(ref c, from: BDefault { span: dummySpan, type: prim(PrimitiveKind.void) })
                                    defVoid = defaultType(ref b, through: ref c, for: voidDef, from: in ld.value.init)
                                default:
                            }
                        default:
                    }
                default:
            }

            print("== defaultType")
            print($"defI32: {defI32 == prim(PrimitiveKind.i32)} defNullError: {isError(defNull)} defVoidError: {isError(defVoid)}")

            // ── 7. EntryPoints and Helpers ────────────────────────────────
            let presPat = bindPresent(ref b, through: ref c, at: dummySpan, as: internOptional(ref c, by: prim(PrimitiveKind.i32)))
            let presValType = c.bPropertyPatterns[boundIndex(presPat)].valueType

            let retS = addReturn(ref c, from: BReturn { span: dummySpan, value: -1 })
            let exprS = addExprStmt(ref c, from: BExprStmt { span: dummySpan, expr: litI32 })
            let ifBranching = addIf(ref c, from: BIf { span: dummySpan, condition: -1, then: retS, elseBranch: exprS })
            let exitsBranching = exits(in c, for: ifBranching)

            var swArms = List<BSwitchArm>()
            swArms.append(BSwitchArm { span: dummySpan, pattern: -1, guard: -1, value: litI32 })
            let swExpr = addSwitchExpr(ref c, from: BSwitchExpr { span: dummySpan, value: -1, arms: move swArms, type: prim(PrimitiveKind.i32) })
            let isSwBp = isBorrowedPlace(ref c, for: swExpr)

            print("== entryPointsAndHelpers")
            print($"presUnwrapped: {presValType == prim(PrimitiveKind.i32)} exitsBranching: {exitsBranching} isSwBp: {isSwBp}")

            popScope(ref b)
            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void BindExitCodeMatches()
    {
        var expected = new List<string> { "code42: 42 code0: 1 code300: 1 codeNoArgs: 1 codeStr: 1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindExitCode", expected);
    }

    [Fact]
    public void BindBlockAndBodyMatches()
    {
        var expected = new List<string> { "blockOk: true bodyOk: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindBlockAndBody", expected);
    }

    [Fact]
    public void BindStatementsMatches()
    {
        var expected = new List<string> { "stmtsCount: 23 block: true local: true varKindOk: true if: true while: true for: true forBorrows: true defer: true assert: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindStatements", expected);
    }

    [Fact]
    public void DiagnosticsMatches()
    {
        var expected = new List<string> { "d2772: 4 d2006: true d3209: true d2408: true d2101: true d2775: true d2226: true d2605: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "diagnostics", expected);
    }

    [Fact]
    public void IsNonNullAndNarrowingMatches()
    {
        var expected = new List<string> { "before: false during: true after: false whenTrue: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "isNonNullAndNarrowing", expected);
    }

    [Fact]
    public void DefaultTypeMatches()
    {
        var expected = new List<string> { "defI32: true defNullError: true defVoidError: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "defaultType", expected);
    }

    [Fact]
    public void EntryPointsAndHelpersMatches()
    {
        var expected = new List<string> { "presUnwrapped: true exitsBranching: false isSwBp: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "entryPointsAndHelpers", expected);
    }
}
