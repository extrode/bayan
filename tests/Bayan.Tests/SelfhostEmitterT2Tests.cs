using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T2: <c>selfhost/emitter/lower.byn</c> (Lowerer.cs 11-351) and <c>cprint/common.byn</c> (CEmitter.cs 397-476,
/// 588-599, 1237-1253) against the C# as an in-process oracle. The emitter's top-level files but <c>main.byn</c> are
/// built with a generated <c>main.byn</c> that prints what the ported functions return, by mode (Q763).
/// </summary>
public sealed class SelfhostEmitterT2Tests
{
    // args: mode, the C file name and `#line` path of the C# `FunctionEmitter` ("-" for none), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type BMove = semantics.BMove
        type CFunc = cprint.CFunc
        type CompilationOptions = semantics.CompilationOptions
        type LAssignExpr = ltree.LAssignExpr
        type LClaim = ltree.LClaim
        type LErrorCode = ltree.LErrorCode
        type LErrorField = ltree.LErrorField
        type LFailingCall = ltree.LFailingCall
        type LFunctionValue = ltree.LFunctionValue
        type LGoto = ltree.LGoto
        type LIndexUnchecked = ltree.LIndexUnchecked
        type LLabel = ltree.LLabel
        type LLoop = ltree.LLoop
        type LNever = ltree.LNever
        type LNextChar = ltree.LNextChar
        type LOptionalHas = ltree.LOptionalHas
        type LOptionalValue = ltree.LOptionalValue
        type LPanic = ltree.LPanic
        type LPushPanicHandler = ltree.LPushPanicHandler
        type LReturnError = ltree.LReturnError
        type LSequence = ltree.LSequence
        type LVoid = ltree.LVoid
        type LWrite = ltree.LWrite

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 3..args.length { rest.append(args[i]) }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            let mode = args[0]
            if mode == "lower" { Io.stdout.write(t2PrintLower(ref c, through: ref l)) }
            if mode == "lowertests" {
                var withTests = semantics.fromPath(args[3], by: in CompilationOptions { includeTests: true })
                Io.stdout.write(t2PrintLower(ref withTests, through: ref l))
            }
            if mode == "synth" { Io.stdout.write(t2PrintSynth(ref c, through: ref l)) }
            if mode == "helper" { Io.stdout.write(t2PrintHelper()) }
            if mode == "keyfail" { Io.stdout.write(t2KeyFail(ref c)) }
            if mode == "corpus" {
                Io.stdout.write(t2PrintCTypes(ref c))
                Io.stdout.write(t2PrintKeys(ref c))
                Io.stdout.write(t2PrintPredicates(ref c, through: ref l))
                Io.stdout.write(t2PrintLines(ref c, at: args[1], for: args[2]))
            }
            return 0
        }

        /// `Lowerer.Lower`'s filter: the functions it lowers, in order.
        List<FuncId> t2Functions(ref Ctx c) uses alloc {
            var r = List<FuncId>()
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].body == -1 || c.functions[fn].kind == .testKeyword || semantics.functionIsGeneric(ref c, by: fn) { continue }
                    r.append(fn)
                }
            }
            return r
        }

        string t2PrintLower(ref Ctx c, through ref LCtx l) uses alloc {
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            sb.append("== lower\n")
            for f in p.functions { sb.append(t2PrintFunction(in c, at: $"fn {c.functions[f.symbol].name}", for: in f)) }
            sb.append(p.main == -1 ? "main -\n" : $"main {c.functions[p.main].name}\n")
            for i in 0..p.tests.length { sb.append(t2PrintFunction(in c, at: $"test {c.tests[p.tests[i]].title}", for: in p.testFunctions[i])) }
            return $"{sb.finish()}"
        }

        string t2PrintFunction(in Ctx c, at string head, for in LoweredFunction f) uses alloc {
            return $"{head} locals={f.locals.length} ctx={f.contextSlots} panic={f.panicSlots} stmts={c.bBlocks[f.body & 0xFFFFFFFF].statements.length}\n"
        }

        string t2PrintHelper() uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== helper\n")
            sb.append($"{cprint.helper(ref cc, for: "a", from: "pa", as: "ba")}\n")
            sb.append($"{cprint.helper(ref cc, for: "a", from: "px", as: "bx")}\n")
            sb.append($"{cprint.helper(ref cc, for: "b", from: "pb", as: "bb")}\n")
            if cprint.helperReserve(ref cc, for: "o", from: "po") {
                sb.append($"{cprint.helper(ref cc, for: "i", from: "pi", as: "bi")}\n")
                sb.append($"{cprint.helper(ref cc, for: "o", from: "px", as: "bx")}\n")
                sb.append($"{cprint.helperFill(ref cc, for: "o", from: "po", as: "bo")}\n")
            }
            for n in cc.helperOrder { sb.append($"{n} {cc.helpers[n].proto} | {cc.helpers[n].body}\n") }
            return $"{sb.finish()}"
        }

        /// `Key` of a generic function's first parameter type, which has no C type.
        string t2KeyFail(ref Ctx c) uses alloc {
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if semantics.functionIsGeneric(ref c, by: fn) {
                        let t = c.locals[c.functions[fn].parameters[0]].type
                        return cprint.key(ref c, for: t)
                    }
                }
            }
            return ""
        }

        string t2PrintCTypes(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== ctype\n")
            for fn in t2Functions(ref c) {
                var types = List<Type>()
                for p in c.functions[fn].parameters { types.append(c.locals[p].type) }
                types.append(c.functions[fn].returnType)
                for t in types { sb.append($"{cprint.cType(ref cc, from: ref c, for: t)}\n") }
            }
            sb.append($"error {cprint.errorCType(ref cc, from: ref c)}\n")
            for n in cc.typeOrder { sb.append($"order {n}\n") }
            return $"{sb.finish()}"
        }

        string t2PrintPredicates(ref Ctx c, through ref LCtx l) uses alloc {
            var sb = StrBuf()
            sb.append("== pred\n")
            for fn in t2Functions(ref c) {
                l.fn = fn
                l.outs = List<List<BStmt>>()
                l.outs.append(List<BStmt>())
                l.locals = List<LocalId>()
                l.seen = Map<LocalId, bool>()
                let body = c.functions[fn].body
                var decls = List<i64>()
                for s in c.bBlocks[body & 0xFFFFFFFF].statements {
                    if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl { decls.append(s & 0xFFFFFFFF) }
                }
                var declared = List<LocalId>()
                for d in decls {
                    let init = c.bLocalDecls[d].initializer
                    let local = c.bLocalDecls[d].local
                    if init != -1 {
                        sb.append($"{isTrivial(in c, through: in l, for: init)} {isRepeatable(in c, through: in l, for: init)} {isLvalue(in c, through: in l, for: init)} {hasEffects(in c, through: in l, for: init)}")
                        for x in declared { sb.append($" {moves(in c, through: in l, for: init, by: x)}") }
                        let n0 = l.locals.length
                        captureBegin(ref l)
                        let sp = spill(ref c, through: ref l, for: init)
                        let spilled = captureEnd(ref l)
                        captureBegin(ref l)
                        emitEffect(ref c, through: ref l, for: init)
                        let effects = captureEnd(ref l)
                        sb.append($" spill={spilled.length} temps={l.locals.length - n0} effect={effects.length}\n")
                    }
                    declared.append(local)
                }
            }
            return $"{sb.finish()}"
        }

        string t2PrintLines(ref Ctx c, at string file, for string linePath) uses alloc {
            var sb = StrBuf()
            sb.append("== lines\n")
            for fn in t2Functions(ref c) {
                let span = semantics.nodeSpan(in c, for: c.functions[fn].body)
                var withPath = CFunc { fn: fn, file: file, linePath: linePath, lineStarts: cprint.lineStartsOf(in c, for: c.functions[fn].file) }
                if linePath == "-" { withPath.linePath = null }
                sb.append(t2LineCase(in c, through: ref withPath, at: span))
                var without = CFunc { fn: fn, file: file, linePath: null, lineStarts: cprint.lineStartsOf(in c, for: c.functions[fn].file) }
                sb.append(t2LineCase(in c, through: ref without, at: span))
            }
            return $"{sb.finish()}"
        }

        string t2LineCase(in Ctx c, through ref CFunc f, at semantics.TextSpan span) uses alloc {
            var buf = StrBuf()
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            cprint.line(ref buf, by: in f, for: "a;")
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            f.indent = 2
            cprint.line(ref buf, by: in f, for: "b;")
            return $"line {cprint.lineOf(in c, by: in f, at: span)} loc {cprint.loc(in c, by: in f, at: span)}\n{buf.finish()}"
        }

        /// `Key` and `CType` of each function's parameter, return and top-level local types.
        string t2PrintKeys(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== keys\n")
            for fn in t2Functions(ref c) {
                var types = List<Type>()
                for p in c.functions[fn].parameters { types.append(c.locals[p].type) }
                types.append(c.functions[fn].returnType)
                for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                    if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl { types.append(c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].type) }
                }
                for t in types { sb.append($"{cprint.key(ref c, for: t)} {cprint.cType(ref cc, from: ref c, for: t)}\n") }
            }
            return $"{sb.finish()}"
        }

        /// `new FunctionLowerer(fn, …)`'s fields.
        void t2Reset(ref LCtx l, for FuncId fn) uses alloc {
            l.fn = fn
            l.outs = List<List<BStmt>>()
            l.outs.append(List<BStmt>())
            l.locals = List<LocalId>()
            l.seen = Map<LocalId, bool>()
            l.scopes = List<LScope>()
            l.labelCount = 0
        }

        FuncId t2Func(ref Ctx c, for string name) uses alloc {
            for fn in t2Functions(ref c) {
                if c.functions[fn].name == name { return fn }
            }
            return -1
        }

        /// The index of `fn`'s top-level declaration of `name`.
        i64 t2Decl(in Ctx c, at FuncId fn, for string name) {
            for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl && c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].name == name { return s & 0xFFFFFFFF }
            }
            return -1
        }

        BExpr t2Init(in Ctx c, at FuncId fn, for string name) => c.bLocalDecls[t2Decl(in c, at: fn, for: name)].initializer

        LocalId t2Var(in Ctx c, at FuncId fn, for string name) => c.bLocalDecls[t2Decl(in c, at: fn, for: name)].local

        TextSpan t2At(in Ctx c, at FuncId fn, for string name) => semantics.nodeSpan(in c, for: t2Init(in c, at: fn, for: name))

        BExpr t2Read(ref Ctx c, at FuncId fn, for string name) uses alloc => localExpr(ref c, at: t2At(in c, at: fn, for: name), for: t2Var(in c, at: fn, for: name))

        bool t2IsTemp(in Ctx c, for BExpr e) => !ltree.isLowered(e) && semantics.boundKind(e) == .local && c.locals[c.bLocals[e & 0xFFFFFFFF].local].kind == .temp

        string t2Which(BExpr r, by BExpr a, as BExpr b) => r == a ? "a" : r == b ? "b" : "new"

        string t2StmtName(BStmt s) uses alloc {
            if ltree.isLowered(s) {
                switch ltree.lKindOf(s) {
                    case .setContext: return "LSetContext"
                    case .freeArena: return "LFreeArena"
                    case .popPanicHandler: return "LPopPanicHandler"
                    default: return "L?"
                }
            }
            switch semantics.boundKind(s) {
                case .assign: return "BoundAssign"
                case .exprStmt: return "BoundExprStmt"
                default: return "B?"
            }
        }

        string t2Names(in List<BStmt> xs) uses alloc {
            var sb = StrBuf()
            for x in xs { sb.append($" {t2StmtName(x)}") }
            return $"{sb.finish()}"
        }

        /// The lowered nodes and helpers of Lowerer.cs 95-351 on `g`'s and the `Order` methods' bound nodes.
        string t2PrintSynth(ref Ctx c, through ref LCtx l) uses alloc {
            var sb = StrBuf()
            sb.append("== synth\n")
            let g = t2Func(ref c, for: "g")
            t2Reset(ref l, for: g)
            let k = c.functions[g].parameters[0]
            let s = c.functions[g].parameters[1]
            let dInit = t2Init(in c, at: g, for: "d")
            let i64Type = c.locals[k].type
            let uVariant = c.bUnionNews[t2Init(in c, at: g, for: "u") & 0xFFFFFFFF].variant
            let eVariant = c.bErrorVariants[t2Init(in c, at: g, for: "er") & 0xFFFFFFFF].variant
            let h = t2Func(ref c, for: "h")
            let empty = block(ref c, at: t2At(in c, at: g, for: "a"), for: List<BStmt>())

            var items = List<BExpr>()
            items.append(ltree.addLOptionalHas(ref l.tree, from: LOptionalHas { span: t2At(in c, at: g, for: "o"), optional: t2Read(ref c, at: g, for: "o") }))
            items.append(ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t2At(in c, at: g, for: "o"), optional: t2Read(ref c, at: g, for: "o"), type: i64Type }))
            items.append(ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t2At(in c, at: g, for: "c"), optional: t2Init(in c, at: g, for: "c"), type: i64Type }))
            items.append(ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t2At(in c, at: g, for: "el"), optional: t2Init(in c, at: g, for: "el"), type: i64Type }))
            items.append(ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t2At(in c, at: g, for: "d"), optional: dInit, type: i64Type }))
            items.append(ltree.addLUnionTag(ref l.tree, from: LUnionTag { span: t2At(in c, at: g, for: "u"), unionValue: t2Read(ref c, at: g, for: "u") }))
            items.append(ltree.addLUnionField(ref l.tree, from: LUnionField { span: t2At(in c, at: g, for: "u"), unionValue: t2Read(ref c, at: g, for: "u"), variant: uVariant, index: 0 }))
            items.append(ltree.addLErrorCode(ref l.tree, from: LErrorCode { span: t2At(in c, at: g, for: "er"), error: t2Read(ref c, at: g, for: "er") }))
            items.append(ltree.addLErrorField(ref l.tree, from: LErrorField { span: t2At(in c, at: g, for: "er"), error: t2Read(ref c, at: g, for: "er"), variant: eVariant, index: 0 }))
            items.append(ltree.addLAssignExpr(ref l.tree, from: LAssignExpr { span: t2At(in c, at: g, for: "a"), target: t2Read(ref c, at: g, for: "a"), value: dInit }))
            var effects1 = List<BExpr>()
            effects1.append(t2Init(in c, at: g, for: "c"))
            items.append(ltree.addLSequence(ref l.tree, from: LSequence { span: t2At(in c, at: g, for: "b"), effects: move effects1, value: t2Read(ref c, at: g, for: "b") }))
            var effects2 = List<BExpr>()
            effects2.append(dInit)
            items.append(ltree.addLSequence(ref l.tree, from: LSequence { span: t2At(in c, at: g, for: "e"), effects: move effects2, value: t2Init(in c, at: g, for: "b") }))
            let unchecked = ltree.addLIndexUnchecked(ref l.tree, from: LIndexUnchecked { span: t2At(in c, at: g, for: "el"), target: t2Read(ref c, at: g, for: "xs"), index: t2Init(in c, at: g, for: "b"), type: i64Type }) because "a node for the predicates; it is never emitted"
            items.append(unchecked)
            items.append(ltree.addLNextChar(ref l.tree, from: LNextChar { span: t2At(in c, at: g, for: "str"), text: t2Read(ref c, at: g, for: "str"), position: t2Var(in c, at: g, for: "b") }))
            items.append(ltree.addLNever(ref l.tree, from: LNever { span: t2At(in c, at: g, for: "w"), type: i64Type }))
            items.append(ltree.addLFunctionValue(ref l.tree, from: LFunctionValue { span: t2At(in c, at: g, for: "hv"), function: h, env: -1, functionType: c.locals[t2Var(in c, at: g, for: "hv")].type }))
            items.append(ltree.addLVoid(ref l.tree, from: LVoid { span: t2At(in c, at: g, for: "f") }))
            // A tuple element; a move of a lowered node and of a field; assignments whose target, or neither side, reads `s`.
            items.append(t2Init(in c, at: g, for: "f"))
            let movedOptional = ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t2At(in c, at: g, for: "o"), optional: t2Read(ref c, at: g, for: "o"), type: i64Type })
            c.bMoves.append(BMove { span: t2At(in c, at: g, for: "o"), operand: movedOptional, type: i64Type })
            items.append(semantics.makeBound(.moveExpr, at: c.bMoves.length - 1))
            items.append(semantics.addMove(ref c, from: BMove { span: t2At(in c, at: g, for: "e"), operand: t2Init(in c, at: g, for: "e") }))
            items.append(ltree.addLAssignExpr(ref l.tree, from: LAssignExpr { span: t2At(in c, at: g, for: "a"), target: localExpr(ref c, at: t2At(in c, at: g, for: "a"), for: s), value: t2Init(in c, at: g, for: "b") }))
            items.append(ltree.addLAssignExpr(ref l.tree, from: LAssignExpr { span: t2At(in c, at: g, for: "a"), target: t2Read(ref c, at: g, for: "a"), value: t2Init(in c, at: g, for: "b") }))
            for x in c.bBlocks[c.functions[g].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(x) && semantics.boundKind(x) == .assign { items.append(c.bAssigns[x & 0xFFFFFFFF].target) }
            }
            for x in items {
                let span = t2Span(in c, through: in l, for: x)
                sb.append($"{cprint.key(ref c, for: t2Type(in c, through: in l, for: x))} {span.start} {span.length} {isTrivial(in c, through: in l, for: x)} {isRepeatable(in c, through: in l, for: x)} {isLvalue(in c, through: in l, for: x)} {hasEffects(in c, through: in l, for: x)} {t2IsExpr(x)} {moves(in c, through: in l, for: x, by: s)} {anyNode(in c, through: in l, for: x, by: .readsLocalMovedBy(dInit))}")
                captureBegin(ref l)
                emitEffect(ref c, through: ref l, for: x)
                let effected = captureEnd(ref l)
                let n0 = l.locals.length
                captureBegin(ref l)
                let spilled = spill(ref c, through: ref l, for: x)
                let spills = captureEnd(ref l)
                sb.append($" effect={effected.length} spill={spills.length} temps={l.locals.length - n0} same={spilled == x}\n")
            }

            var stmts = List<BStmt>()
            stmts.append(ltree.addLLabel(ref l.tree, from: LLabel { span: t2At(in c, at: g, for: "a"), name: "x" }))
            stmts.append(ltree.addLGoto(ref l.tree, from: LGoto { span: t2At(in c, at: g, for: "b"), label: "x" }))
            stmts.append(ltree.addLLoop(ref l.tree, from: LLoop { span: t2At(in c, at: g, for: "c"), body: empty }))
            stmts.append(ltree.addLReturn(ref l.tree, from: LReturn { span: t2At(in c, at: g, for: "d"), value: -1 }))
            stmts.append(ltree.addLReturnError(ref l.tree, from: LReturnError { span: t2At(in c, at: g, for: "e"), code: t2Read(ref c, at: g, for: "er") }))
            stmts.append(ltree.addLFailingCall(ref l.tree, from: LFailingCall { span: t2At(in c, at: g, for: "f"), error: t2Var(in c, at: g, for: "er"), call: dInit, result: -1 }))
            stmts.append(ltree.addLWrite(ref l.tree, from: LWrite { span: t2At(in c, at: g, for: "o"), sink: .print, target: -1, parts: List<semantics.BInterpolationPart>() }))
            stmts.append(ltree.addLPanic(ref l.tree, from: LPanic { span: t2At(in c, at: g, for: "u"), message: "m" }))
            stmts.append(ltree.addLSetContext(ref l.tree, from: LSetContext { span: t2At(in c, at: g, for: "er"), arena: -1, slot: 1 }))
            stmts.append(ltree.addLFreeArena(ref l.tree, from: LFreeArena { span: t2At(in c, at: g, for: "str"), arena: t2Read(ref c, at: g, for: "xs") }))
            stmts.append(ltree.addLPushPanicHandler(ref l.tree, from: LPushPanicHandler { span: t2At(in c, at: g, for: "xs"), slot: 2, panic: -1, body: empty }))
            stmts.append(ltree.addLPopPanicHandler(ref l.tree, from: LPopPanicHandler { span: t2At(in c, at: g, for: "el"), slot: 2 }))
            stmts.append(ltree.addLClaim(ref l.tree, from: LClaim { span: t2At(in c, at: g, for: "hv"), local: t2Var(in c, at: g, for: "a") }))
            for x in stmts {
                let span = t2Span(in c, through: in l, for: x)
                sb.append($"stmt {span.start} {span.length} {t2IsExpr(x)} {anyNode(in c, through: in l, for: x, by: .readsLocalMovedBy(dInit))}\n")
            }

            // Note: a parameter, a local twice, then a lambda's capture and a local of the lambda's.
            t2Reset(ref l, for: g)
            note(ref c, through: ref l, for: k)
            note(ref c, through: ref l, for: t2Var(in c, at: g, for: "a"))
            note(ref c, through: ref l, for: t2Var(in c, at: g, for: "a"))
            sb.append($"note {l.locals.length}")
            t2Reset(ref l, for: c.bLambdas[t2Init(in c, at: g, for: "lam") & 0xFFFFFFFF].lambda)
            note(ref c, through: ref l, for: t2Var(in c, at: g, for: "a"))
            note(ref c, through: ref l, for: t2Var(in c, at: g, for: "b"))
            sb.append($" {l.locals.length}\n")

            t2Reset(ref l, for: g)
            let label1 = newLabel(ref l, for: "loop")
            let label2 = newLabel(ref l, for: "end")
            sb.append($"label {label1} {label2}\n")
            let at = t2At(in c, at: g, for: "b")
            var ints = List<i64>()
            ints.append(5)
            ints.append(-5)
            ints.append(0)
            for v in ints {
                let lit = i64Literal(ref c, at: at, by: v)
                let value = semantics.exprConstant(in c, for: lit) ?? return "no constant"
                sb.append($"i64 {cprint.key(ref c, for: t2Type(in c, through: in l, for: lit))} {value.integer.lo} {value.integer.hi}\n")
            }
            let yes = boolLiteral(ref c, at: at, by: true)
            let no = boolLiteral(ref c, at: at, by: false)
            let five = i64Literal(ref c, at: at, by: 5)
            let bRead = t2Read(ref c, at: g, for: "b")
            let voidValue = ltree.addLVoid(ref l.tree, from: LVoid { span: at })
            sb.append($"and {t2Which(andExpr(ref c, through: in l, for: yes, by: bRead), by: yes, as: bRead)} {t2Which(andExpr(ref c, through: in l, for: bRead, by: yes), by: bRead, as: yes)} {t2Which(andExpr(ref c, through: in l, for: no, by: five), by: no, as: five)} {t2Which(andExpr(ref c, through: in l, for: voidValue, by: bRead), by: voidValue, as: bRead)}")
            var made = List<BExpr>()
            made.append(andExpr(ref c, through: in l, for: no, by: five))
            made.append(orExpr(ref c, through: in l, for: no, by: five))
            made.append(notExpr(ref c, through: in l, for: no))
            for x in made {
                let span = t2Span(in c, through: in l, for: x)
                sb.append($" {cprint.key(ref c, for: t2Type(in c, through: in l, for: x))} {span.start} {span.length}")
            }
            let yesValue = semantics.exprConstant(in c, for: yes) ?? return "no constant"
            let noValue = semantics.exprConstant(in c, for: no) ?? return "no constant"
            sb.append($" {yesValue.flag} {noValue.flag}\n")

            // EmitAssign: a never value, a never-typed jump, a void value, a call.
            let jump = c.bCoalesces[t2Init(in c, at: g, for: "w") & 0xFFFFFFFF].right
            var counts = List<i64>()
            var assigned = List<BExpr>()
            assigned.append(ltree.addLNever(ref l.tree, from: LNever { span: at, type: i64Type }))
            assigned.append(jump)
            assigned.append(voidValue)
            assigned.append(t2Init(in c, at: g, for: "c"))
            for v in assigned {
                captureBegin(ref l)
                emitAssign(ref c, through: ref l, to: t2Read(ref c, at: g, for: "b"), as: v)
                counts.append(captureEnd(ref l).length)
            }
            sb.append($"assign {counts[0]} {counts[1]} {counts[2]} {counts[3]}\n")

            // Operands: two effects; a move a later operand reads; an addressable operand; a plain one beside it.
            let cInit = t2Init(in c, at: g, for: "c")
            let yi = t2Init(in c, at: g, for: "yi")
            var ops1 = List<BExpr>()
            ops1.append(cInit)
            ops1.append(cInit)
            sb.append(t2Operands(ref c, through: ref l, for: in ops1, as: in List<bool>()))
            var ops2 = List<BExpr>()
            ops2.append(dInit)
            ops2.append(localExpr(ref c, at: at, for: s))
            sb.append(t2Operands(ref c, through: ref l, for: in ops2, as: in List<bool>()))
            var ops3 = List<BExpr>()
            ops3.append(yi)
            ops3.append(cInit)
            ops3.append(cInit)
            var lv3 = List<bool>()
            lv3.append(true)
            sb.append(t2Operands(ref c, through: ref l, for: in ops3, as: in lv3))
            var ops4 = List<BExpr>()
            ops4.append(localExpr(ref c, at: at, for: k))
            ops4.append(cInit)
            var lv4 = List<bool>()
            lv4.append(true)
            lv4.append(false)
            sb.append(t2Operands(ref c, through: ref l, for: in ops4, as: in lv4))
            var ops5 = List<BExpr>()
            ops5.append(localExpr(ref c, at: at, for: k))
            ops5.append(cInit)
            ops5.append(cInit)
            sb.append(t2Operands(ref c, through: ref l, for: in ops5, as: in List<bool>()))

            // Key and CType of the builtin and text view types no corpus signature has.
            var cc = CCtx {}
            var builtins = List<Type>()
            builtins.append(semantics.builtinType(.io))
            builtins.append(semantics.builtinType(.hex))
            builtins.append(semantics.builtinType(.utf8))
            builtins.append(semantics.builtinType(.ioStream))
            builtins.append(semantics.builtinType(.ioInput))
            builtins.append(semantics.builtinType(.path))
            builtins.append(semantics.textViewType(.runes))
            for t in builtins { sb.append($"type {cprint.key(ref c, for: t)} {cprint.cType(ref cc, from: ref c, for: t)}\n") }

            sb.append($"guard {captureGuard(ref c, through: ref l, for: empty) == empty}\n")

            // CaptureGuard on guarded defer bodies, a plain `if` with and without `else`, and a call.
            let gd = t2Func(ref c, for: "gd")
            t2Reset(ref l, for: gd)
            let gdBody = c.functions[gd].body & 0xFFFFFFFF
            for gi in 0..c.bBlocks[gdBody].statements.length {
                let d = c.bBlocks[gdBody].statements[gi]
                if ltree.isLowered(d) || semantics.boundKind(d) != .deferStmt { continue }
                let body = c.bDefers[d & 0xFFFFFFFF].body
                let o0 = l.outs[l.outs.length - 1].length
                let r = captureGuard(ref c, through: ref l, for: body)
                sb.append($"guard {r == body} {!ltree.isLowered(r) && semantics.boundKind(r) == .ifStmt}")
                if !ltree.isLowered(r) && semantics.boundKind(r) == .ifStmt && semantics.boundKind(body) == .ifStmt {
                    let ri = r & 0xFFFFFFFF
                    let bi = body & 0xFFFFFFFF
                    sb.append($" then={c.bIfs[ri].then == c.bIfs[bi].then} else={c.bIfs[ri].elseBranch == -1} temp={t2IsTemp(in c, for: c.bIfs[ri].condition)}")
                }
                sb.append($" out={l.outs[l.outs.length - 1].length - o0}\n")
            }

            // EmitCleanups over every cleanup kind, without and with a failing error.
            for round in 0..2 {
                let withError = round == 1
                t2Reset(ref l, for: g)
                var outer = List<LCleanup>()
                outer.append(LCleanup.deferCleanup(empty))
                outer.append(LCleanup.errdeferCleanup(-1, empty))
                outer.append(LCleanup.errdeferCleanup(t2Var(in c, at: g, for: "b"), empty))
                outer.append(LCleanup.restoreContext(at, 3))
                outer.append(LCleanup.freeArena(at, t2Var(in c, at: g, for: "a")))
                outer.append(LCleanup.popPanicHandler(at, 4))
                l.scopes.append(LScope { cleanups: move outer })
                var inner = List<LCleanup>()
                inner.append(LCleanup.deferCleanup(empty))
                l.scopes.append(LScope { cleanups: move inner })
                captureBegin(ref l)
                emitCleanups(ref c, through: ref l, at: 0, for: withError ? t2Var(in c, at: g, for: "er") : -1)
                let emitted = captureEnd(ref l)
                sb.append($"cleanup{t2Names(in emitted)} locals={l.locals.length} scopes={l.scopes.length}\n")
            }

            // Become: a payload to spill, a constant one, none.
            var methods = List<string>()
            methods.append("pay")
            methods.append("repay")
            methods.append("reset")
            for name in methods {
                let fn = t2Func(ref c, for: name)
                t2Reset(ref l, for: fn)
                captureBegin(ref l)
                var body = List<BStmt>()
                for x in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements { body.append(x) }
                for x in body {
                    if !ltree.isLowered(x) && semantics.boundKind(x) == .becomeStmt { lowerBecome(ref c, through: ref l, for: x) }
                }
                let emitted = captureEnd(ref l)
                sb.append($"become {name}{t2Names(in emitted)} locals={l.locals.length}\n")
            }

            // LineOf of a function with no file.
            for fn in 0..c.functions.length {
                if c.functions[fn].file == -1 {
                    let nofile = CFunc { fn: fn, file: "x", linePath: null }
                    sb.append($"nofile {cprint.lineOf(in c, by: in nofile, at: at)}\n")
                    break
                }
            }
            return $"{sb.finish()}"
        }

        string t2Operands(ref Ctx c, through ref LCtx l, for in List<BExpr> ops, as in List<bool> lvalues) uses alloc {
            let n0 = l.locals.length
            var values = List<BExpr>()
            captureBegin(ref l)
            let ok = operands(ref c, through: ref l, for: in ops, as: in lvalues, to: ref values)
            let emitted = captureEnd(ref l)
            var sb = StrBuf()
            sb.append($"operands {ok}{t2Names(in emitted)} temps={l.locals.length - n0}")
            for v in values { sb.append($" {t2IsTemp(in c, for: v)}") }
            sb.append("\n")
            return $"{sb.finish()}"
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T2 main did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var dir = SelfhostEmitterHarness.EmitterDir;
        var sources = Directory.GetFiles(dir, "*.byn")
            .Where(p => Path.GetFileName(p) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(dir, "main.byn"), Main));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Writes <paramref name="source"/> to a temporary file, then runs <paramref name="act"/> on its path.</summary>
    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    private static (int Exit, string Out, string Err) Run(string mode, string path, string file = "-", string linePath = "-") =>
        SelfhostParserTests.Run(Build(), [mode, file, linePath, path]);

    private static Dictionary<string, List<string>> RunMode(string mode, string path, string file = "-", string linePath = "-")
    {
        var (exit, stdout, stderr) = Run(mode, path, file, linePath);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    private static readonly Type FunctionLowerer = typeof(Lowerer).Assembly.GetType("Bayan.Lowering.FunctionLowerer", throwOnError: true)!;

    private static object? Call(Type type, object? target, string name, params object?[] args) =>
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    /// <summary><c>Lowerer.Lower</c>'s filter: the functions it lowers, in order.</summary>
    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric).ToList();

    private static string Flag(object? b) => (bool)b! ? "true" : "false";

    // ── lower (Lowerer.cs:18-93) ─────────────────────────────────────────────────

    private const string LowerFixture = """
        struct P {
            i64 x = 1
            i64 y
            P(i64 a) { }
        }

        void f() { { } }

        T id<T>(move T v) => move v

        test "nothing" { }

        void main() { }
        """;

    private static List<string> LowerOracle(Compilation c)
    {
        var p = Lowerer.Lower(c);
        var lines = new List<string>();
        foreach (var f in p.Functions) lines.Add($"fn {f.Symbol.Name} {Shape(f)}");
        lines.Add(p.Main is null ? "main -" : $"main {p.Main.Name}");
        foreach (var (t, f) in p.Tests) lines.Add($"test {t.Name} {Shape(f)}");
        return lines;
    }

    private static string Shape(LoweredFunction f) =>
        $"locals={f.Locals.Count} ctx={f.ContextSlots} panic={f.PanicSlots} stmts={f.Body.Statements.Length}";

    // Tests included, as `bayan test` compiles them.
    [Fact]
    public void Lower_ProgramShapeMatchesCSharp() => WithFixture(LowerFixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("lowertests", path), "lower",
            LowerOracle(Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!, new CompilationOptions(IncludeTests: true)))));

    // A warning does not stop the lowering; the functions' shapes are T3's, so only the program's `main` is compared.
    [Fact]
    public void Lower_WithWarningsLowersLikeCSharp() => WithFixture("void main() {\n    var x = 1\n}\n", path =>
    {
        var c = Compile(path);
        Assert.Contains(c.Diagnostics.Items, d => !d.IsError);
        var (exit, stdout, _) = Run("lower", path);
        Assert.Equal(0, exit);
        var main = LowerOracle(c).Single(s => s.StartsWith("main ", StringComparison.Ordinal));
        Assert.Contains(main, SelfhostSemanticsHarness.Sections(stdout)["lower"]);
    });

    /// The last three bind statements `bindStatement` reaches only with errors: `Panic.onPanic` (BYN2230), a `Panic` member
    /// that is not a handler, and a `Panic` local.
    [Theory]
    [InlineData("void main() { let x: i64 = \"s\" }\n")]
    [InlineData("void onCrash(Panic p) { }\n\nvoid main() {\n    Panic.onPanic = onCrash\n}\n")]
    [InlineData("void main() {\n    Panic.other = 1\n}\n")]
    [InlineData("struct Q {\n    i64 handler\n}\n\nvoid main() {\n    var Panic = Q { handler: 1 }\n    Panic.handler = 2\n}\n")]
    public void Lower_WithErrorsFailsLikeCSharp(string source) => WithFixture(source, path =>
    {
        var e = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(Compile(path)));
        var (exit, _, err) = Run("lower", path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });

    // ── helper (CEmitter.cs:591) ──────────────────────────────────────────────────

    [Fact]
    public void Helper_RegistersOnceLikeCSharp() => WithFixture("void main() { }\n", path =>
    {
        var c = Compile(path);
        var emitter = NewEmitter(Lowerer.Lower(c));
        var expected = new List<string>
        {
            emitter.Helper("a", "pa", () => "ba"),
            emitter.Helper("a", "px", () => "bx"),
            emitter.Helper("b", "pb", () => "bb"),
        };
        // A body that registers helpers: the outer name is reserved first (CEmitter.cs:593-597).
        expected.Add(emitter.Helper("o", "po", () =>
        {
            expected.Add(emitter.Helper("i", "pi", () => "bi"));
            expected.Add(emitter.Helper("o", "px", () => "bx"));
            return "bo";
        }));
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(emitter, "_helpers")!;
        foreach (var n in (List<string>)Field(emitter, "_helperOrder")!) expected.Add($"{n} {helpers[n].Proto} | {helpers[n].Body}");
        SelfhostSemanticsHarness.AssertSection(RunMode("helper", path), "helper", expected);
    });

    // ── Key's failure (CEmitter.cs:468) ───────────────────────────────────────────

    [Fact]
    public void Key_OfATypeParameterFailsLikeCSharp() => WithFixture("T id<T>(move T v) => move v\n\nvoid main() { }\n", path =>
    {
        var c = Compile(path);
        var emitter = NewEmitter(Lowerer.Lower(c));
        var fn = c.EntryModule.AllFunctions.First(f => f.IsGeneric);
        var e = Assert.Throws<InvalidOperationException>(() => emitter.Key(fn.Parameters[0].Type));
        var (exit, _, err) = Run("keyfail", path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });

    // ── The run/ corpus: CType/ErrorCType/Named order, the lowerer's predicates, Spill, EmitEffect, the line helpers ─

    private static (string File, string LinePath, Dictionary<string, List<string>> Sections) CorpusOracle(Compilation c)
    {
        var p = Lowerer.Lower(c);
        var emitter = NewEmitter(p);
        var functions = Functions(c);
        var ctype = new List<string>();
        foreach (var fn in functions)
        {
            foreach (var param in fn.Parameters) ctype.Add(emitter.CType(param.Type));
            ctype.Add(emitter.CType(fn.ReturnType));
        }
        ctype.Add($"error {emitter.ErrorCType}");
        foreach (var n in (List<string>)Field(emitter, "_typeOrder")!) ctype.Add($"order {n}");

        var keys = new List<string>();
        var keyEmitter = NewEmitter(p);
        foreach (var fn in functions)
        {
            var types = fn.Parameters.Select(x => x.Type).Append(fn.ReturnType).Concat(fn.Body!.Statements.OfType<BoundLocalDecl>().Select(d => d.Local.Type));
            foreach (var t in types) keys.Add($"{keyEmitter.Key(t)} {keyEmitter.CType(t)}");
        }

        var pred = new List<string>();
        foreach (var fn in functions)
        {
            var fl = Activator.CreateInstance(FunctionLowerer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [fn, c.Builtins, null, null], null)!;
            var output = (List<BoundStmt>)Field(fl, "_out")!;
            var locals = (List<LocalSymbol>)Field(fl, "_locals")!;
            var declared = new List<LocalSymbol>();
            foreach (var d in fn.Body!.Statements.OfType<BoundLocalDecl>())
            {
                if (d.Initializer is { } init)
                {
                    var sb = new StringBuilder();
                    sb.Append($"{Flag(Call(FunctionLowerer, null, "IsTrivial", init))} {Flag(Call(FunctionLowerer, null, "IsRepeatable", init))} ");
                    sb.Append($"{Flag(Call(FunctionLowerer, null, "IsLvalue", init))} {Flag(Call(FunctionLowerer, null, "HasEffects", init))}");
                    foreach (var x in declared) sb.Append($" {Flag(Call(FunctionLowerer, null, "Moves", init, x))}");
                    var n0 = locals.Count;
                    var o0 = output.Count;
                    Call(FunctionLowerer, fl, "Spill", init);
                    var spilled = output.Count - o0;
                    var temps = locals.Count - n0;
                    o0 = output.Count;
                    Call(FunctionLowerer, fl, "EmitEffect", init);
                    sb.Append($" spill={spilled} temps={temps} effect={output.Count - o0}");
                    pred.Add(sb.ToString());
                }
                declared.Add(d.Local);
            }
        }

        var lines = new List<string>();
        string file = "-", linePath = "-";
        foreach (var f in p.Functions)
        {
            var syntax = f.Symbol.Body!.Syntax;
            foreach (var withPath in new[] { true, false })
            {
                var fe = new FunctionEmitter(emitter, f);
                if (file == "-")
                {
                    file = (string)Field(fe, "_file")!;
                    // A program built without debug info has no `#line` path, so the C# is given the one the selfhost is.
                    linePath = (string?)Field(fe, "_linePath") ?? "\"a.byn\"";
                }
                typeof(FunctionEmitter).GetField("_linePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fe, withPath ? linePath : null);
                var t = typeof(FunctionEmitter);
                Call(t, fe, "MapLine", syntax);
                Call(t, fe, "MapLine", syntax);
                Call(t, fe, "Line", "a;");
                Call(t, fe, "MapLine", syntax);
                t.GetField("_indent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fe, 2);
                Call(t, fe, "Line", "b;");
                lines.Add($"line {Call(t, fe, "LineOf", syntax)} loc {Call(t, fe, "Loc", syntax)}");
                lines.AddRange(((StringBuilder)Field(fe, "_sb")!).ToString().Split('\n').Where(s => s.Length > 0));
            }
        }
        return (file, linePath, new() { ["ctype"] = ctype, ["keys"] = keys, ["pred"] = pred, ["lines"] = lines });
    }

    // The selfhost binder builds a `requires` function's body with one more local than the C# (Q766), so only the
    // `pred` and `keys` sections of these files are skipped.
    private static readonly HashSet<string> PredSkipped = ["positional-requires.byn"];

    [Fact]
    public void Corpus_MatchesCSharp()
    {
        var files = Directory.GetFiles(Path.Combine(Golden.RepoRoot, "tests", "cases", "run"), "*.byn").Order(StringComparer.Ordinal).ToList();
        var failures = new ConcurrentBag<string>();
        var compared = 0;
        Build();
        // Each file alone in a directory: bayan-check reads a file's directory as its package.
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, corpus => WithFixture(File.ReadAllText(corpus), path =>
        {
            var c = Compile(path);
            if (c.Diagnostics.HasErrors) return;
            var (file, linePath, expected) = CorpusOracle(c);
            // bayan-check's warnings go to stderr, so only the exit is checked.
            var (exit, stdout, stderr) = Run("corpus", path, file, linePath);
            if (exit != 0)
            {
                failures.Add($"{Path.GetFileName(corpus)}: exited {exit}: {stderr}");
                return;
            }
            var actual = SelfhostSemanticsHarness.Sections(stdout);
            foreach (var (section, lines) in expected)
            {
                if ((section == "pred" || section == "keys") && PredSkipped.Contains(Path.GetFileName(corpus))) continue;
                var got = actual.GetValueOrDefault(section) ?? [];
                for (var i = 0; i < Math.Max(got.Count, lines.Count); i++)
                {
                    var e = i < lines.Count ? lines[i] : "<end>";
                    var a = i < got.Count ? got[i] : "<end>";
                    if (e == a) continue;
                    failures.Add($"{Path.GetFileName(corpus)} {section} line {i + 1}\n expected: {e}\n   actual: {a}");
                    break;
                }
            }
            Interlocked.Increment(ref compared);
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(4))}");
        Assert.True(failures.IsEmpty, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(8))}");
    }
    // ── Synthesized nodes: the lowered kinds and helpers of Lowerer.cs 95-351 on bound nodes of `g` and `Order` ──

    private const string SynthFixture = """
        struct P {
            i64 x
        }

        union U {
            a(i64 v)
            b
        }

        union error E {
            bad(i64 code)
        }

        struct Order {
            i64 id
            states {
                draft
                paid(i64 payment)
            }
            void pay(ref this is .draft, i64 amount) becomes .paid {
                become .paid(amount)
            }
            void repay(ref this is .paid) becomes .paid {
                become .paid(5)
            }
            void reset(ref this is .paid) becomes .draft {
                become .draft
            }
        }

        i64 h() => 1

        i64 take(move string v) => 0

        void g(i64 k, from move string s, to ref Map<string, i64> m, by ref List<i64> ys) {
            let a = k
            let b = 1
            let c = h()
            let d = take(move s)
            let p = P { x: 1 }
            let e = p.x
            let t: (i64 q, i64 r) = (1, 2)
            let f = t.q
            let o: i64? = 2
            let u = U.a(1)
            let er: E = .bad(3)
            let str = "x"
            let xs = List<i64>()
            let el = xs[0]
            let yi = ys[0]
            let hv: () -> i64 = h
            let lam: i64 -> i64 = z => z + a
            let w = o ?? return
            m["a"] = 1
            m.slot(key: "b") += 2
        }

        void gd(bool f) {
            defer print("x") if f
            defer print("w") unless f
            defer if f { print("y") }
            defer if f { print("y") } else { print("v") }
            defer print("z")
            unless f { print("u") }
        }

        void main() { }
        """;

    private static List<string> SynthOracle(Compilation c)
    {
        var emitter = NewEmitter(Lowerer.Lower(c));
        var functions = Functions(c);
        var g = functions.First(f => f.Name == "g");
        var decls = g.Body!.Statements.OfType<BoundLocalDecl>().ToDictionary(d => d.Local.Name);
        BoundExpr Init(string n) => decls[n].Initializer!;
        LocalSymbol Var(string n) => decls[n].Local;
        Node At(string n) => Init(n).Syntax;
        BoundExpr Read(string n) => new BoundLocal(At(n), Var(n));
        object New(FunctionSymbol fn) =>
            Activator.CreateInstance(FunctionLowerer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [fn, c.Builtins, null, null], null)!;
        object? Do(object fl, string name, params object?[] args) => Call(FunctionLowerer, fl, name, args);
        List<BoundStmt> Out(object fl) => (List<BoundStmt>)Field(fl, "_out")!;
        List<LocalSymbol> Locals(object fl) => (List<LocalSymbol>)Field(fl, "_locals")!;
        string Names(IEnumerable<BoundStmt> xs) => string.Concat(xs.Select(x => " " + x.GetType().Name));
        string Key(BoundExpr e) => emitter.Key(e.Type);

        var lines = new List<string>();
        var fl = New(g);
        var k = g.Parameters[0];
        var s = g.Parameters[1];
        var dInit = Init("d");
        var i64 = PrimitiveType.I64;
        var empty = (BoundBlock)Call(FunctionLowerer, null, "Block", At("a"), Array.Empty<BoundStmt>())!;
        Func<BoundNode, bool> readsMoved = x => x is BoundLocal r && (bool)Call(FunctionLowerer, null, "Moves", dInit, r.Local)!;

        var items = new List<BoundExpr>
        {
            new LOptionalHas(At("o"), Read("o")),
            new LOptionalValue(At("o"), Read("o"), i64),
            new LOptionalValue(At("c"), Init("c"), i64),
            new LOptionalValue(At("el"), Init("el"), i64),
            new LOptionalValue(At("d"), dInit, i64),
            new LUnionTag(At("u"), Read("u")),
            new LUnionField(At("u"), Read("u"), ((BoundUnionNew)Init("u")).Variant, 0),
            new LErrorCode(At("er"), Read("er")),
            new LErrorField(At("er"), Read("er"), ((BoundErrorVariant)Init("er")).Variant, 0),
            new LAssignExpr(At("a"), Read("a"), dInit),
            new LSequence(At("b"), [Init("c")], Read("b")),
            new LSequence(At("e"), [dInit], Init("b")),
            new LIndexUnchecked(At("el"), Read("xs"), Init("b"), i64),
            new LNextChar(At("str"), Read("str"), Var("b")),
            new LNever(At("w"), i64),
            new LFunctionValue(At("hv"), functions.First(f => f.Name == "h"), null, (FunctionType)Var("hv").Type),
            new LVoid(At("f")),
            Init("f"),
            new BoundMove(At("o"), new LOptionalValue(At("o"), Read("o"), i64)),
            new BoundMove(At("e"), Init("e")),
            new LAssignExpr(At("a"), new BoundLocal(At("a"), s), Init("b")),
            new LAssignExpr(At("a"), Read("a"), Init("b")),
        };
        items.AddRange(g.Body.Statements.OfType<BoundAssign>().Select(a => a.Target));
        foreach (var x in items)
        {
            var sb = new StringBuilder();
            sb.Append($"{Key(x)} {x.Syntax.Span.Start} {x.Syntax.Span.Length} {Flag(Call(FunctionLowerer, null, "IsTrivial", x))} {Flag(Call(FunctionLowerer, null, "IsRepeatable", x))} ");
            sb.Append($"{Flag(Call(FunctionLowerer, null, "IsLvalue", x))} {Flag(Call(FunctionLowerer, null, "HasEffects", x))} {Flag(true)} ");
            sb.Append($"{Flag(Call(FunctionLowerer, null, "Moves", x, s))} {Flag(Call(FunctionLowerer, null, "Any", x, readsMoved))}");
            var o0 = Out(fl).Count;
            Do(fl, "EmitEffect", x);
            var effected = Out(fl).Count - o0;
            var n0 = Locals(fl).Count;
            o0 = Out(fl).Count;
            var spilled = Do(fl, "Spill", x);
            sb.Append($" effect={effected} spill={Out(fl).Count - o0} temps={Locals(fl).Count - n0} same={Flag(ReferenceEquals(spilled, x))}");
            lines.Add(sb.ToString());
        }

        var stmts = new List<BoundStmt>
        {
            new LLabel(At("a"), "x"),
            new LGoto(At("b"), "x"),
            new LLoop(At("c"), empty),
            new LReturn(At("d"), null),
            new LReturnError(At("e"), Read("er")),
            new LFailingCall(At("f"), Var("er"), (BoundCall)dInit, null),
            new LWrite(At("o"), WriteSink.Print, null, []),
            new LPanic(At("u"), "m"),
            new LSetContext(At("er"), null, 1),
            new LFreeArena(At("str"), Read("xs")),
            new LPushPanicHandler(At("xs"), 2, null, empty),
            new LPopPanicHandler(At("el"), 2),
            new LClaim(At("hv"), Var("a")),
        };
        foreach (var x in stmts)
            lines.Add($"stmt {x.Syntax.Span.Start} {x.Syntax.Span.Length} {Flag(false)} {Flag(Call(FunctionLowerer, null, "Any", x, readsMoved))}");

        fl = New(g);
        Do(fl, "Note", k);
        Do(fl, "Note", Var("a"));
        Do(fl, "Note", Var("a"));
        var noted = Locals(fl).Count;
        var lambda = New(((BoundLambda)Init("lam")).Lambda);
        Do(lambda, "Note", Var("a"));
        Do(lambda, "Note", Var("b"));
        lines.Add($"note {noted} {Locals(lambda).Count}");

        fl = New(g);
        lines.Add($"label {Do(fl, "NewLabel", "loop")} {Do(fl, "NewLabel", "end")}");
        var at = At("b");
        foreach (var v in new long[] { 5, -5, 0 })
        {
            var lit = (BoundLiteral)Call(FunctionLowerer, null, "I64", at, v)!;
            var value = (Int128)lit.Constant!.Value;
            lines.Add($"i64 {Key(lit)} {(ulong)(value & ulong.MaxValue)} {unchecked((ulong)(value >> 64))}");
        }
        var yes = (BoundLiteral)Call(FunctionLowerer, null, "Bool", at, true)!;
        var no = (BoundLiteral)Call(FunctionLowerer, null, "Bool", at, false)!;
        var five = (BoundExpr)Call(FunctionLowerer, null, "I64", at, 5L)!;
        var bRead = Read("b");
        var voidValue = new LVoid(at);
        BoundExpr And(BoundExpr a, BoundExpr b) => (BoundExpr)Call(FunctionLowerer, null, "And", a, b)!;
        string Which(BoundExpr r, BoundExpr a, BoundExpr b) => ReferenceEquals(r, a) ? "a" : ReferenceEquals(r, b) ? "b" : "new";
        var line = new StringBuilder($"and {Which(And(yes, bRead), yes, bRead)} {Which(And(bRead, yes), bRead, yes)} {Which(And(no, five), no, five)} {Which(And(voidValue, bRead), voidValue, bRead)}");
        foreach (var x in new[] { And(no, five), (BoundExpr)Call(FunctionLowerer, null, "Or", no, five)!, (BoundExpr)Call(FunctionLowerer, null, "Not", no)! })
            line.Append($" {Key(x)} {x.Syntax.Span.Start} {x.Syntax.Span.Length}");
        line.Append($" {Flag(yes.Constant!.Value)} {Flag(no.Constant!.Value)}");
        lines.Add(line.ToString());

        var counts = new List<int>();
        foreach (var v in new BoundExpr[] { new LNever(at, i64), ((BoundCoalesce)Init("w")).Right, voidValue, Init("c") })
        {
            var o0 = Out(fl).Count;
            Do(fl, "EmitAssign", Read("b"), v);
            counts.Add(Out(fl).Count - o0);
        }
        lines.Add($"assign {string.Join(' ', counts)}");

        string Operands(BoundExpr[] ops, Func<int, bool>? lvalues)
        {
            var n0 = Locals(fl).Count;
            var o0 = Out(fl).Count;
            var values = (BoundExpr[]?)Do(fl, "Operands", ops, lvalues);
            var r = $"operands {Flag(values is not null)}{Names(Out(fl).Skip(o0))} temps={Locals(fl).Count - n0}";
            return r + string.Concat((values ?? []).Select(v => " " + Flag(v is BoundLocal { Local.Kind: LocalKind.Temp })));
        }
        lines.Add(Operands([Init("c"), Init("c")], null));
        lines.Add(Operands([dInit, new BoundLocal(at, s)], null));
        lines.Add(Operands([Init("yi"), Init("c"), Init("c")], i => i == 0));
        lines.Add(Operands([new BoundLocal(at, k), Init("c")], i => i == 0));
        lines.Add(Operands([new BoundLocal(at, k), Init("c"), Init("c")], null));

        foreach (TypeSymbol t in new TypeSymbol[] { BuiltinType.Io, BuiltinType.Hex, BuiltinType.Utf8, BuiltinType.IoStream, BuiltinType.IoInput, BuiltinType.Path, TextViewType.Runes })
            lines.Add($"type {emitter.Key(t)} {emitter.CType(t)}");

        lines.Add($"guard {Flag(ReferenceEquals(Do(fl, "CaptureGuard", empty), empty))}");

        var gd = functions.First(f => f.Name == "gd");
        var guards = New(gd);
        foreach (var d in gd.Body!.Statements.OfType<BoundDefer>())
        {
            var o0 = Out(guards).Count;
            var r = (BoundStmt)Do(guards, "CaptureGuard", d.Body)!;
            var gl = new StringBuilder($"guard {Flag(ReferenceEquals(r, d.Body))} {Flag(r is BoundIf)}");
            if (r is BoundIf ri && d.Body is BoundIf bi)
                gl.Append($" then={Flag(ReferenceEquals(ri.Then, bi.Then))} else={Flag(ri.Else is null)} temp={Flag(ri.Condition is BoundLocal { Local.Kind: LocalKind.Temp })}");
            gl.Append($" out={Out(guards).Count - o0}");
            lines.Add(gl.ToString());
        }

        object Cleanup(string kind, params object?[] args) =>
            Activator.CreateInstance(FunctionLowerer.GetNestedType(kind, BindingFlags.NonPublic)!, args)!;
        foreach (var withError in new[] { false, true })
        {
            fl = New(g);
            var scopes = (System.Collections.IList)Field(fl, "_scopes")!;
            var scopeType = FunctionLowerer.GetNestedType("Scope", BindingFlags.NonPublic)!;
            var outer = Activator.CreateInstance(scopeType)!;
            var outerCleanups = (System.Collections.IList)scopeType.GetProperty("Cleanups")!.GetValue(outer)!;
            outerCleanups.Add(Cleanup("DeferCleanup", empty));
            outerCleanups.Add(Cleanup("ErrdeferCleanup", null, empty));
            outerCleanups.Add(Cleanup("ErrdeferCleanup", Var("b"), empty));
            outerCleanups.Add(Cleanup("RestoreContext", at, 3));
            outerCleanups.Add(Cleanup("FreeArena", at, Var("a")));
            outerCleanups.Add(Cleanup("PopPanicHandler", at, 4));
            scopes.Add(outer);
            var inner = Activator.CreateInstance(scopeType)!;
            ((System.Collections.IList)scopeType.GetProperty("Cleanups")!.GetValue(inner)!).Add(Cleanup("DeferCleanup", empty));
            scopes.Add(inner);
            var o0 = Out(fl).Count;
            Do(fl, "EmitCleanups", 0, withError ? Var("er") : null);
            lines.Add($"cleanup{Names(Out(fl).Skip(o0))} locals={Locals(fl).Count} scopes={scopes.Count}");
        }

        foreach (var name in new[] { "pay", "repay", "reset" })
        {
            var fn = functions.First(f => f.Name == name);
            fl = New(fn);
            foreach (var b in fn.Body!.Statements.OfType<BoundBecome>()) Do(fl, "Become", b);
            lines.Add($"become {name}{Names(Out(fl))} locals={Locals(fl).Count}");
        }

        var noFile = new FunctionSymbol("x", null, c.EntryModule, FunctionKind.Builtin);
        var fe = new FunctionEmitter(emitter, new LoweredFunction(noFile, [], 0, 0, empty));
        lines.Add($"nofile {Call(typeof(FunctionEmitter), fe, "LineOf", at)}");
        return lines;
    }

    [Fact]
    public void Synth_MatchesCSharp() => WithFixture(SynthFixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("synth", path), "synth", SynthOracle(Compile(path))));
}
