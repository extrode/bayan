using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T17: <c>selfhost/semantics/express.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT17Tests
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

            let srcText = "fn testExprs() {\n    let a = 42\n    let b = 100u\n    let c = 200l\n    let d = 300ul\n    let f1 = 1.5f\n    let f2 = 3.14\n    let f3 = 1e2\n    let f4 = 1e-2\n    let ch = 'a'\n    let chEsc = '\\n'\n    let chU = '\\u{41}'\n    let s = \"hello\"\n    let bT = true\n    let bF = false\n    let nl = null\n    let oob = 99999999999999999999\n    let badSuff = 10xyz\n    let locRead = myLocal\n    let undef = prnt\n    this\n    let stFld = receiptNo\n}\n"

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

            // ── 1. parseInteger ──────────────────────────────────────────
            let pi0 = parseInteger("0")
            let pi42 = parseInteger("42")
            let piNeg = parseInteger("-100")
            let piHex = parseInteger("0x2A")
            let piBin = parseInteger("0b1010")
            let piNegHex = parseInteger("-0x10")
            print("== parseInteger")
            print($"pi0: {pi0.lo} pi42: {pi42.lo} piNeg: {bigCmp(piNeg, as: bigFromI64(-100)) == 0} piHex: {piHex.lo} piBin: {piBin.lo} piNegHex: {bigCmp(piNegHex, as: bigFromI64(-16)) == 0}")

            // ── 2. fits ──────────────────────────────────────────────────
            let fI8Pos = fits(bigFromI64(127), for: .i8)
            let fI8Oob = fits(bigFromI64(128), for: .i8)
            let fI8Neg = fits(bigFromI64(-128), for: .i8)
            let fI8NegOob = fits(bigFromI64(-129), for: .i8)
            let fU8Pos = fits(bigFromI64(255), for: .u8)
            let fU8Oob = fits(bigFromI64(256), for: .u8)
            let fI32Pos = fits(bigFromI64(2147483647), for: .i32)
            let fI32Oob = fits(bigParse("2147483648").value, for: .i32)
            print("== fits")
            print($"fI8Pos: {fI8Pos} fI8Oob: {fI8Oob} fI8Neg: {fI8Neg} fI8NegOob: {fI8NegOob} fU8Pos: {fU8Pos} fU8Oob: {fU8Oob} fI32Pos: {fI32Pos} fI32Oob: {fI32Oob}")

            // ── 3. suggest ───────────────────────────────────────────────
            var cands = List<string>()
            cands.append("print")
            cands.append("panic")
            cands.append("todo")
            let sugPrint = suggest("prnt", by: in cands) ?? "none"
            let sugPanic = suggest("panc", by: in cands) ?? "none"
            let sugNone = suggest("totallyunrelated", by: in cands) ?? "none"
            let sugExact = suggest("print", by: in cands) ?? "none"
            print("== suggest")
            print($"sugPrint: {sugPrint} sugPanic: {sugPanic} sugNone: {sugNone} sugExact: {sugExact}")

            // ── 5. constrain, findPeer, isPending ────────────────────────
            let dummySpan = TextSpan { start: 0, length: 1 }
            let pendLocId = newLocal(ref c, by: LocalSymbol { name: "pendLoc", type: prim(.i32) })
            let dummyLocalNode = addLocal(ref c, from: BLocal { span: dummySpan, local: pendLocId, type: prim(.i32) })
            let pendBefore = isPending(ref b, through: ref c, for: dummyLocalNode)
            b.pending[pendLocId] = 100
            let pendAfter = isPending(ref b, through: ref c, for: dummyLocalNode)
            constrain(ref b, through: ref c, for: dummyLocalNode, to: prim(.i64))
            let constraintRecorded = (b.constraints.tryGet(at: 100) ?? -1) == prim(.i64)

            b.peers[10] = 20
            b.peers[20] = 30
            let rootPeer = findPeer(ref b, for: 10)
            print("== constrain_peers_pending")
            print($"pendBefore: {pendBefore} pendAfter: {pendAfter} constraint: {constraintRecorded} rootPeer: {rootPeer}")

            // ── Setup symbols for bindName and convert ───────────────────
            let optI32 = internOptional(ref c, by: prim(.i32))
            let myLocId = newLocal(ref c, by: LocalSymbol { name: "myLocal", type: optI32 })
            b.scopes[0]["myLocal"] = myLocId

            let myConstId = newLocal(ref c, by: LocalSymbol { name: "myConst", type: prim(.i32), kind: .constKeyword, constant: Constant { kind: .integer, integer: bigFromI64(999) } })
            b.scopes[0]["myConst"] = myConstId

            let mySymId = newLocal(ref c, by: LocalSymbol { name: "mySym", type: builtinType(.symbol), kind: .constKeyword, constant: Constant { kind: .text, text: "symValue" } })
            b.scopes[0]["mySym"] = mySymId

            let modConstId = newConst(ref c, by: ConstSymbol { name: "MOD_CONST", type: prim(.i32), value: Constant { kind: .integer, integer: bigFromI64(555) } })
            c.modules[0].constants["MOD_CONST"] = modConstId
            c.modules[0].allConstants.append(modConstId)

            let modFuncId = newFunction(ref c, by: FunctionSymbol { name: "modFunc", returnType: prim(.void) })
            var fnList = List<FuncId>()
            fnList.append(modFuncId)
            c.modules[0].functions["modFunc"] = move fnList
            c.modules[0].allFunctions.append(modFuncId)

            // ── Process AST statements ───────────────────────────────────
            switch c.files[0].unit.members[0] {
                case .functionDecl(let fd):
                    switch fd.value.body {
                        case .blockStmt(let blk):
                            // ── 4. isUntypedLiteral ──────────────────────
                            var isUntypedInt = false
                            var isUntypedSuff = true
                            var isUntypedFloat = false
                            var isUntypedChar = false
                            var isUntypedNull = false
                            var isUntypedLoc = true

                            switch blk.value.statements[0] {
                                case .localDecl(let ld):
                                    isUntypedInt = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[1] {
                                case .localDecl(let ld):
                                    isUntypedSuff = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[5] {
                                case .localDecl(let ld):
                                    isUntypedFloat = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[8] {
                                case .localDecl(let ld):
                                    isUntypedChar = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[14] {
                                case .localDecl(let ld):
                                    isUntypedNull = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[17] {
                                case .localDecl(let ld):
                                    isUntypedLoc = isUntypedLiteral(in ld.value.init)
                                default:
                                    break
                            }

                            print("== isUntypedLiteral")
                            print($"int: {isUntypedInt} suff: {isUntypedSuff} float: {isUntypedFloat} char: {isUntypedChar} null: {isUntypedNull} loc: {isUntypedLoc}")

                            // ── 6. bindLiteral ───────────────────────────
                            var litA: BExpr = -1
                            var litB: BExpr = -1
                            var litC: BExpr = -1
                            var litD: BExpr = -1
                            var litF1: BExpr = -1
                            var litF2: BExpr = -1
                            var litF3: BExpr = -1
                            var litF4: BExpr = -1
                            var litCh: BExpr = -1
                            var litChEsc: BExpr = -1
                            var litChU: BExpr = -1
                            var litS: BExpr = -1
                            var litBT: BExpr = -1
                            var litBF: BExpr = -1
                            var litNlOpt: BExpr = -1
                            var litNlBare: BExpr = -1
                            var litOob: BExpr = -1
                            var litBadSuff: BExpr = -1

                            switch blk.value.statements[0] {
                                case .localDecl(let ld):
                                    litA = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[1] {
                                case .localDecl(let ld):
                                    litB = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[2] {
                                case .localDecl(let ld):
                                    litC = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[3] {
                                case .localDecl(let ld):
                                    litD = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[4] {
                                case .localDecl(let ld):
                                    litF1 = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[5] {
                                case .localDecl(let ld):
                                    litF2 = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[6] {
                                case .localDecl(let ld):
                                    litF3 = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[7] {
                                case .localDecl(let ld):
                                    litF4 = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[8] {
                                case .localDecl(let ld):
                                    litCh = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[9] {
                                case .localDecl(let ld):
                                    litChEsc = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[10] {
                                case .localDecl(let ld):
                                    litChU = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[11] {
                                case .localDecl(let ld):
                                    litS = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[12] {
                                case .localDecl(let ld):
                                    litBT = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[13] {
                                case .localDecl(let ld):
                                    litBF = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[14] {
                                case .localDecl(let ld):
                                    litNlOpt = bindExpr(ref b, through: ref c, for: in ld.value.init, to: optI32)
                                    litNlBare = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[15] {
                                case .localDecl(let ld):
                                    litOob = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            switch blk.value.statements[16] {
                                case .localDecl(let ld):
                                    litBadSuff = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }

                            let f3Val = exprConstant(in c, for: litF3)
                            let f4Val = exprConstant(in c, for: litF4)
                            let f3Is100 = f3Val is { } k3 && k3.kind == .real && k3.real > 99.9 && k3.real < 100.1
                            let f4Is001 = f4Val is { } k4 && k4.kind == .real && k4.real > 0.009 && k4.real < 0.011

                            let chVal = exprConstant(in c, for: litCh)
                            let chIs97 = chVal is { } kc && kc.kind == .rune && kc.rune == 97
                            let chEscVal = exprConstant(in c, for: litChEsc)
                            let chEscIs10 = chEscVal is { } ke && ke.kind == .rune && ke.rune == 10
                            let chUVal = exprConstant(in c, for: litChU)
                            let chUIs65 = chUVal is { } ku && ku.kind == .rune && ku.rune == 65

                            let f1Val = exprConstant(in c, for: litF1)
                            let f1Is15 = f1Val is { } k1 && k1.kind == .real && k1.real > 1.49 && k1.real < 1.51
                            let f2Val = exprConstant(in c, for: litF2)
                            let f2Is314 = f2Val is { } k2 && k2.kind == .real && k2.real > 3.13 && k2.real < 3.15

                            print("== bindLiteral")
                            print($"litA: {exprType(in c, for: litA) == prim(.i32)} litB: {exprType(in c, for: litB) == prim(.u32)} litC: {exprType(in c, for: litC) == prim(.i64)} litD: {exprType(in c, for: litD) == prim(.u64)} litF1: {exprType(in c, for: litF1) == prim(.f32)} litF2: {exprType(in c, for: litF2) == prim(.f64)} f3: {f3Is100} f4: {f4Is001} ch: {chIs97} chEsc: {chEscIs10} chU: {chUIs65} str: {exprType(in c, for: litS) == prim(.string)} bT: {exprType(in c, for: litBT) == prim(.bool)} bF: {exprType(in c, for: litBF) == prim(.bool)} nlOpt: {boundKind(litNlOpt) == .defaultValue} nlBare: {boundKind(litNlBare) == .literal} oobErr: {boundKind(litOob) == .errorExpr} badSuffErr: {boundKind(litBadSuff) == .errorExpr} f1Val: {f1Is15} f2Val: {f2Is314}")

                            // ── 7. bindName ──────────────────────────────
                            var readLoc: BExpr = -1
                            switch blk.value.statements[17] {
                                case .localDecl(let ld):
                                    readLoc = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            let readLocKind = boundKind(readLoc)

                            b.nonNull.append((0, myLocId))
                            var readNarrow: BExpr = -1
                            switch blk.value.statements[17] {
                                case .localDecl(let ld):
                                    readNarrow = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            let readNarrowKind = boundKind(readNarrow)

                            var readUndef: BExpr = -1
                            switch blk.value.statements[18] {
                                case .localDecl(let ld):
                                    readUndef = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }

                            let sStateId = newStruct(ref c, by: StructSymbol { name: "Order", span: dummySpan, file: 0, declIndex: 10 })
                            let unionId = newUnion(ref c, by: UnionSymbol { name: "OrderState", span: dummySpan, file: 0, declIndex: 11 })
                            let varId = newUnionVariant(ref c, by: UnionVariantSymbol { name: "Placed", span: dummySpan, file: 0, owner: unionId })
                            let fVarId = newField(ref c, by: FieldSymbol { name: "receiptNo", span: dummySpan, file: 0, owner: unionId, type: prim(.i32) })
                            c.unionVariants[varId].fields.append(fVarId)
                            c.unions[unionId].variants.append(varId)
                            c.states[sStateId] = unionId

                            b.thisType = makeType(.structType, at: sStateId)
                            var readStateField: BExpr = -99
                            switch blk.value.statements[20] {
                                case .localDecl(let ld):
                                    readStateField = bindExpr(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }
                            b.thisType = -1

                            print("== bindName")
                            print($"readLoc: {readLocKind == .local} readNarrow: {readNarrowKind == .conversion} readUndef: {boundKind(readUndef) == .errorExpr} readStateField: {readStateField == -1}")

                            // ── 8. thisExpr ──────────────────────────────
                            var thisOutside: BExpr = -1
                            switch blk.value.statements[19] {
                                case .exprStmt(let es):
                                    thisOutside = bindExpr(ref b, through: ref c, for: in es.value.expr)
                                default:
                                    break
                            }
                            let thisOutsideIsErr = boundKind(thisOutside) == .errorExpr

                            let structId = newStruct(ref c, by: StructSymbol { name: "HostClass", span: dummySpan, file: 0, declIndex: 2 })
                            let mId = newFunction(ref c, by: FunctionSymbol { name: "hostMethod", span: dummySpan, file: 0, declIndex: 3, owner: structId, kind: .method, returnType: prim(.void) })
                            b.fn = mId
                            b.thisType = makeType(.structType, at: structId)

                            var thisInside: BExpr = -1
                            switch blk.value.statements[19] {
                                case .exprStmt(let es):
                                    thisInside = bindExpr(ref b, through: ref c, for: in es.value.expr)
                                default:
                                    break
                            }
                            let thisInsideIsThis = boundKind(thisInside) == .thisRef

                            b.fn = -1
                            b.thisType = -1
                            print("== thisExpr")
                            print($"outsideErr: {thisOutsideIsErr} insideThis: {thisInsideIsThis}")

                            // ── 9. asValue & 11. bindStatementExpression ──
                            var asValTRef: BExpr = -1
                            var asValMRef: BExpr = -1
                            var asValMg: BExpr = -1
                            var asValStmtExpr: BExpr = -1

                            switch blk.value.statements[0] {
                                case .localDecl(let ld):
                                    let tRef = addTypeRef(ref c, from: BTypeRef { span: dummySpan, referenced: prim(.i32) })
                                    asValTRef = asValue(ref b, through: ref c, for: tRef, from: in ld.value.init)

                                    let mRef = addModuleRef(ref c, from: BModuleRef { span: dummySpan, module: 0 })
                                    asValMRef = asValue(ref b, through: ref c, for: mRef, from: in ld.value.init)

                                    let mg = addMethodGroup(ref c, from: BMethodGroup { span: dummySpan, receiver: -1, function: modFuncId })
                                    asValMg = asValue(ref b, through: ref c, for: mg, from: in ld.value.init)

                                    asValStmtExpr = bindStatementExpression(ref b, through: ref c, for: in ld.value.init)
                                default:
                                    break
                            }

                            print("== asValue")
                            print($"tRefErr: {boundKind(asValTRef) == .errorExpr} mRefErr: {boundKind(asValMRef) == .errorExpr} mgErr: {boundKind(asValMg) == .errorExpr}")

                            // ── 10. convert ──────────────────────────────
                            let convIdent = convert(ref b, through: ref c, for: litA, to: prim(.i32), at: dummySpan)
                            let convWidenConst = convert(ref b, through: ref c, for: litA, to: prim(.i64), at: dummySpan)
                            let convWidenNonConst = convert(ref b, through: ref c, for: dummyLocalNode, to: prim(.i64), at: dummySpan)
                            let convWidenReal = convert(ref b, through: ref c, for: litA, to: prim(.f64), at: dummySpan)
                            let convOpt = convert(ref b, through: ref c, for: litA, to: optI32, at: dummySpan)
                            let convStrSym = convert(ref b, through: ref c, for: litS, to: builtinType(.symbol), at: dummySpan)
                            let convStrSymCode = c.diagnostics.length > 0 ? c.diagnostics[c.diagnostics.length - 1].code : "none"

                            let convStrInt = convert(ref b, through: ref c, for: litS, to: prim(.i32), at: dummySpan)
                            let convStrIntCode = c.diagnostics.length > 0 ? c.diagnostics[c.diagnostics.length - 1].code : "none"

                            var emptyP = List<Type>()
                            var emptyE = List<ErrorSetId>()
                            var emptyEff = List<string>()
                            let ftVoid = internFunction(ref c, for: in emptyP, to: prim(.void), by: in emptyE, as: in emptyEff)
                            let mgToConv = addMethodGroup(ref c, from: BMethodGroup { span: dummySpan, receiver: -1, function: modFuncId })
                            let convFn = convert(ref b, through: ref c, for: mgToConv, to: ftVoid, at: dummySpan)

                            let mgMethod = addMethodGroup(ref c, from: BMethodGroup { span: dummySpan, receiver: -1, function: mId })
                            let convMethodErr = convert(ref b, through: ref c, for: mgMethod, to: ftVoid, at: dummySpan)
                            let convMethodDiagCode = c.diagnostics.length > 0 ? c.diagnostics[c.diagnostics.length - 1].code : "none"
                            let convMethodIsErr = boundKind(convMethodErr) == .errorExpr

                            let errSet1 = newErrorSet(ref c, by: ErrorSetSymbol { name: "IoError", span: dummySpan, file: 0, declIndex: 20 })
                            let errSet2 = newErrorSet(ref c, by: ErrorSetSymbol { name: "NetError", span: dummySpan, file: 0, declIndex: 21 })
                            var ftErrSets = List<ErrorSetId>()
                            ftErrSets.append(errSet1)
                            ftErrSets.append(errSet2)

                            var emptyP2 = List<Type>()
                            var emptyEff2 = List<string>()
                            let ftFailsTwo = internFunction(ref c, for: in emptyP2, to: prim(.void), by: in ftErrSets, as: in emptyEff2)

                            let fnFailsBare = newFunction(ref c, by: FunctionSymbol { name: "bareFailsFn", span: dummySpan, file: 0, declIndex: 22, kind: .function, returnType: prim(.void), hasFails: true })
                            let mgBare = addMethodGroup(ref c, from: BMethodGroup { span: dummySpan, receiver: -1, function: fnFailsBare })
                            let convBareErr = convert(ref b, through: ref c, for: mgBare, to: ftFailsTwo, at: dummySpan)
                            let convBareHelp = c.diagnostics.length > 0 ? c.diagnostics[c.diagnostics.length - 1].help : ""
                            let bareHelpOk = convBareHelp == "declare the error sets of `bareFailsFn`: `fails IoError, NetError`"

                            print("== convert")
                            print($"ident: {convIdent == litA} widenConst: {boundKind(convWidenConst) == .literal} widenNonConst: {boundKind(convWidenNonConst) == .conversion} widenReal: {boundKind(convWidenReal) == .literal} opt: {boundKind(convOpt) == .conversion} strSymErr: {boundKind(convStrSym) == .errorExpr} strSymDiag: {convStrSymCode} strIntDiag: {convStrIntCode} fnRef: {boundKind(convFn) == .functionRef} methodFnErr: {convMethodIsErr} methodFnDiag: {convMethodDiagCode} bareHelpOk: {bareHelpOk}")

                            // ── 11. bindStatementExpression ──────────────
                            print("== bindStatementExpression")
                            print($"nonVoid: {asValStmtExpr != -1}")

                        default:
                            break
                    }
                default:
                    break
            }

            popScope(ref b)
            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void ParseIntegerMatches()
    {
        var expected = new List<string> { "pi0: 0 pi42: 42 piNeg: true piHex: 42 piBin: 10 piNegHex: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "parseInteger", expected);
    }

    [Fact]
    public void FitsMatches()
    {
        var expected = new List<string> { "fI8Pos: true fI8Oob: false fI8Neg: true fI8NegOob: false fU8Pos: true fU8Oob: false fI32Pos: true fI32Oob: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "fits", expected);
    }

    [Fact]
    public void SuggestMatches()
    {
        var expected = new List<string> { "sugPrint: print sugPanic: panic sugNone: none sugExact: none" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "suggest", expected);
    }

    [Fact]
    public void IsUntypedLiteralMatches()
    {
        var expected = new List<string> { "int: true suff: false float: true char: true null: true loc: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "isUntypedLiteral", expected);
    }

    [Fact]
    public void ConstrainPeersPendingMatches()
    {
        var expected = new List<string> { "pendBefore: false pendAfter: true constraint: true rootPeer: 30" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "constrain_peers_pending", expected);
    }

    [Fact]
    public void BindLiteralMatches()
    {
        var expected = new List<string> { "litA: true litB: true litC: true litD: true litF1: true litF2: true f3: true f4: true ch: true chEsc: true chU: true str: true bT: true bF: true nlOpt: true nlBare: true oobErr: true badSuffErr: true f1Val: true f2Val: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindLiteral", expected);
    }

    [Fact]
    public void BindNameMatches()
    {
        var expected = new List<string> { "readLoc: true readNarrow: true readUndef: true readStateField: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindName", expected);
    }

    [Fact]
    public void ThisExprMatches()
    {
        var expected = new List<string> { "outsideErr: true insideThis: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "thisExpr", expected);
    }

    [Fact]
    public void AsValueMatches()
    {
        var expected = new List<string> { "tRefErr: true mRefErr: true mgErr: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "asValue", expected);
    }

    [Fact]
    public void ConvertMatches()
    {
        var expected = new List<string> { "ident: true widenConst: true widenNonConst: true widenReal: true opt: true strSymErr: true strSymDiag: BYN2832 strIntDiag: BYN2202 fnRef: true methodFnErr: true methodFnDiag: BYN2824 bareHelpOk: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "convert", expected);
    }

    [Fact]
    public void BindStatementExpressionMatches()
    {
        var expected = new List<string> { "nonVoid: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "bindStatementExpression", expected);
    }
}
