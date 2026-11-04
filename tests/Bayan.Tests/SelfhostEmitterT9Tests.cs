using System.Reflection;
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
/// 011 T9: <c>selfhost/emitter/cprint/func.byn</c> (CEmitter.cs 1173-1236, 1254-1616) against the C#
/// <c>FunctionEmitter</c> as an in-process oracle. Both sides build the same lowered bodies by hand from the fixture's
/// bound nodes, so each statement kind is reached without a full lowering. The T10 stubs return "", so the C#
/// text of each callee still a stub on feat (T10 <c>Expr</c>/<c>Call</c>) is masked to "" (Q802).
/// </summary>
public sealed class SelfhostEmitterT9Tests
{
    // args: mode, ownership checks ("true"/"false"), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CFunc = cprint.CFunc
        type BFormatSpec = semantics.BFormatSpec
        type BSetOnPanic = semantics.BSetOnPanic
        type LClaim = ltree.LClaim
        type LFailingCall = ltree.LFailingCall
        type LGoto = ltree.LGoto
        type LLabel = ltree.LLabel
        type LLoop = ltree.LLoop
        type LPanic = ltree.LPanic
        type LPushPanicHandler = ltree.LPushPanicHandler
        type LReturnError = ltree.LReturnError
        type LWrite = ltree.LWrite
        type LOptionalHas = ltree.LOptionalHas
        type LOptionalValue = ltree.LOptionalValue
        type LErrorCode = ltree.LErrorCode
        type LErrorField = ltree.LErrorField
        type LAssignExpr = ltree.LAssignExpr
        type LSequence = ltree.LSequence
        type LIndexUnchecked = ltree.LIndexUnchecked
        type LNextChar = ltree.LNextChar
        type LVoid = ltree.LVoid

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 2..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            let mode = args[0]
            let checks = args[1] == "true"
            if mode == "emit" { Io.stdout.write(t9PrintEmit(ref c, by: checks)) }
            if mode == "bound" { Io.stdout.write(t9Fail(ref c)) }
            return 0
        }

        /// `Lowerer.Lower`'s filter: the functions it lowers, in order.
        List<FuncId> t9Functions(ref Ctx c) uses alloc {
            var r = List<FuncId>()
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    // The `formatInto` methods are there for the format calls, not to be emitted.
                    if c.functions[fn].body == -1 || c.functions[fn].kind == .testKeyword || semantics.functionIsGeneric(ref c, by: fn) || c.functions[fn].name == "formatInto" { continue }
                    r.append(fn)
                }
            }
            return r
        }

        BExpr t9Local(ref Ctx c, at TextSpan span, for LocalId l) uses alloc {
            c.bLocals.append(BLocal { span: span, local: l, type: c.locals[l].type })
            return semantics.makeBound(.local, at: c.bLocals.length - 1)
        }

        BExpr t9Field(ref Ctx c, at TextSpan span, by BExpr receiver, for semantics.FieldId field) uses alloc {
            c.bFields.append(BField { span: span, receiver: receiver, field: field, type: c.fields[field].type })
            return semantics.makeBound(.field, at: c.bFields.length - 1)
        }

        BExpr t9Tuple(ref Ctx c, at TextSpan span, by BExpr receiver) uses alloc {
            c.bTupleElements.append(BTupleElement { span: span, receiver: receiver, index: 0, type: semantics.prim(.i64) })
            return semantics.makeBound(.tupleElement, at: c.bTupleElements.length - 1)
        }

        BStmt t9ExprStmt(ref Ctx c, at TextSpan span, for BExpr e) uses alloc {
            c.bExprStmts.append(BExprStmt { span: span, expr: e })
            return semantics.makeBound(.exprStmt, at: c.bExprStmts.length - 1)
        }

        BStmt t9Assign(ref Ctx c, at TextSpan span, for BExpr e, by bool op) uses alloc {
            if op == true {
                c.bAssigns.append(BAssign { span: span, target: e, value: e, op: .add })
            } else {
                c.bAssigns.append(BAssign { span: span, target: e, value: e })
            }
            return semantics.makeBound(.assign, at: c.bAssigns.length - 1)
        }

        BStmt t9If(ref Ctx c, at TextSpan span, for BExpr e, by BStmt inner, to BStmt elseBranch) uses alloc {
            c.bIfs.append(BIf { span: span, condition: e, then: inner, elseBranch: elseBranch })
            return semantics.makeBound(.ifStmt, at: c.bIfs.length - 1)
        }

        BStmt t9SetOnPanic(ref Ctx c, at TextSpan span, for FuncId fn) uses alloc {
            c.bSetOnPanics.append(BSetOnPanic { span: span, handler: fn })
            return semantics.makeBound(.setOnPanic, at: c.bSetOnPanics.length - 1)
        }

        BInterpolationPart t9Part(BExpr value, by BFormatSpec spec) => BInterpolationPart { value: value, spec: spec }

        /// The body both sides emit (`Body` in the test): each statement kind, over the fixture's own bound nodes.
        void t9Build(ref Ctx c, through ref LTree t, for FuncId fn, to ref List<BStmt> stmts, at ref List<LocalId> locals) uses alloc {
            let body = c.functions[fn].body
            let span = semantics.nodeSpan(in c, for: body)
            if c.functions[fn].name == "e0" { return }
            var original = List<BStmt>()
            for s in c.bBlocks[body & 0xFFFFFFFF].statements { original.append(s) }
            var inner: BStmt = -1
            var calls = List<BExpr>()
            var assigns = List<BStmt>()
            for s in original {
                let k = semantics.boundKind(s)
                if k == .localDecl { locals.append(c.bLocalDecls[s & 0xFFFFFFFF].local) }
                if k == .block && inner == -1 { inner = s }
                if k == .exprStmt && semantics.boundKind(c.bExprStmts[s & 0xFFFFFFFF].expr) == .call { calls.append(c.bExprStmts[s & 0xFFFFFFFF].expr) }
                if k == .localDecl {
                    let init = c.bLocalDecls[s & 0xFFFFFFFF].initializer
                    if init != -1 && semantics.boundKind(init) == .tryExpr && semantics.boundKind(c.bTries[init & 0xFFFFFFFF].operand) == .call {
                        calls.append(c.bTries[init & 0xFFFFFFFF].operand)
                    }
                }
                if k == .assign { assigns.append(s) }
            }
            var params = List<LocalId>()
            for p in c.functions[fn].parameters { params.append(p) }
            var xs = List<BExpr>()
            for p in params { xs.append(t9Local(ref c, at: span, for: p)) }
            stmts.append(inner)
            stmts.append(ltree.addLLabel(ref t, from: LLabel { span: span, name: "L0" }))
            stmts.append(ltree.addLGoto(ref t, from: LGoto { span: span, label: "L0" }))
            stmts.append(ltree.addLLoop(ref t, from: LLoop { span: span, body: inner }))
            var labelled = List<BStmt>()
            labelled.append(ltree.addLLabel(ref t, from: LLabel { span: span, name: "L1" }))
            let labelBlock = semantics.addBlock(ref c, from: BBlock { span: span, statements: move labelled })
            stmts.append(ltree.addLLoop(ref t, from: LLoop { span: span, body: labelBlock }))
            if locals.length > 0 && c.locals[locals[0]].type == semantics.prim(.i64) { stmts.append(ltree.addLClaim(ref t, from: LClaim { span: span, local: locals[0] })) }
            stmts.append(ltree.addLSetContext(ref t, from: LSetContext { span: span, slot: 0 }))
            stmts.append(ltree.addLPushPanicHandler(ref t, from: LPushPanicHandler { span: span, slot: 0, body: inner }))
            stmts.append(ltree.addLPopPanicHandler(ref t, from: LPopPanicHandler { span: span, slot: 0 }))
            if params.length > 0 { stmts.append(ltree.addLClaim(ref t, from: LClaim { span: span, local: params[0] })) }
            stmts.append(ltree.addLPanic(ref t, from: LPanic { span: span, message: "boom" }))
            var skipped = List<BInterpolationPart>()
            skipped.append(BInterpolationPart { text: "" })
            skipped.append(BInterpolationPart {})
            stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .panic, parts: move skipped }))
            for a in assigns { stmts.append(a) }
            if xs.length > 0 {
                let q = xs[0]
                stmts.append(t9ExprStmt(ref c, at: span, for: q))
                stmts.append(t9Assign(ref c, at: span, for: q, by: false))
                stmts.append(t9Assign(ref c, at: span, for: q, by: true))
                stmts.append(ltree.addLReturnError(ref t, from: LReturnError { span: span, code: q }))
                stmts.append(ltree.addLSetContext(ref t, from: LSetContext { span: span, arena: q, slot: 0 }))
                stmts.append(ltree.addLFreeArena(ref t, from: LFreeArena { span: span, arena: q }))
                if locals.length > 0 { stmts.append(ltree.addLPushPanicHandler(ref t, from: LPushPanicHandler { span: span, slot: 0, panic: locals[0], body: inner })) }
                stmts.append(ltree.addLReturn(ref t, from: LReturn { span: span, value: q }))
            }
            if calls.length >= 2 && locals.length >= 2 {
                stmts.append(ltree.addLFailingCall(ref t, from: LFailingCall { span: span, error: locals[0], call: calls[0], result: locals[1] }))
                stmts.append(ltree.addLFailingCall(ref t, from: LFailingCall { span: span, error: locals[0], call: calls[1] }))
                for k in 2..calls.length { stmts.append(ltree.addLFailingCall(ref t, from: LFailingCall { span: span, error: locals[0], call: calls[k] })) }
            }
            if xs.length >= 7 {
                let q = xs[0]
                let s = xs[1]
                let b = xs[2]
                stmts.append(t9If(ref c, at: span, for: b, by: inner, to: -1))
                stmts.append(t9If(ref c, at: span, for: b, by: inner, to: inner))
                let elseIf = t9If(ref c, at: span, for: b, by: inner, to: -1)
                stmts.append(t9If(ref c, at: span, for: b, by: inner, to: elseIf))
                stmts.append(t9If(ref c, at: span, for: b, by: inner, to: ltree.addLLabel(ref t, from: LLabel { span: span, name: "L2" })))
                var parts = List<BInterpolationPart>()
                var guards = List<BExpr>()
                parts.append(BInterpolationPart { text: "a" })
                guards.append(-1)
                parts.append(BInterpolationPart { value: q })
                guards.append(b)
                parts.append(t9Part(q, by: BFormatSpec { radix: 16 }))
                parts.append(t9Part(b, by: BFormatSpec { radix: 2 }))
                parts.append(t9Part(q, by: BFormatSpec { digits: 2 }))
                parts.append(t9Part(q, by: BFormatSpec { radix: 8, width: 6 }))
                parts.append(t9Part(s, by: BFormatSpec { debug: true, width: 4 }))
                parts.append(t9Part(s, by: BFormatSpec { debug: true }))
                parts.append(t9Part(q, by: BFormatSpec { debug: true }))
                parts.append(t9Part(xs[3], by: BFormatSpec { debug: true }))
                parts.append(t9Part(xs[4], by: BFormatSpec { debug: true }))
                parts.append(t9Part(xs[5], by: BFormatSpec { debug: true }))
                parts.append(t9Part(xs[6], by: BFormatSpec { debug: true }))
                parts.append(t9Part(q, by: BFormatSpec { width: 2, fill: 42, align: 2, digits: 1 }))
                parts.append(t9Part(q, by: BFormatSpec { width: 3 }))
                for k in 2..parts.length { guards.append(-1) }
                stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .print, target: b, parts: move parts, guards: move guards }))
                var logged = List<BInterpolationPart>()
                logged.append(BInterpolationPart { text: "b" })
                stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .log, target: q, parts: move logged }))
                stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .strBuf, target: q, parts: List<BInterpolationPart>() }))
                let p = c.locals[locals[5]].type & 0xFFFFFFFF
                let owned = t9Field(ref c, at: span, by: t9Local(ref c, at: span, for: locals[5]), for: c.structs[p].fields[0])
                stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .strBuf, target: owned, parts: List<BInterpolationPart>() }))
                stmts.append(t9SetOnPanic(ref c, at: span, for: fn))
                // Structs with each `formatInto` form (`FormatWithSpec`'s spec call, `Debug` on fields).
                let kv = xs[7]
                let ks = c.locals[params[7]].type & 0xFFFFFFFF
                let fv = t9Field(ref c, at: span, by: kv, for: c.structs[ks].fields[0])
                var fparts = List<BInterpolationPart>()
                fparts.append(t9Part(fv, by: BFormatSpec { radix: 2 }))
                fparts.append(t9Part(fv, by: BFormatSpec { radix: 8, align: 1 }))
                fparts.append(t9Part(fv, by: BFormatSpec { radix: 16, debug: true }))
                fparts.append(t9Part(fv, by: BFormatSpec { width: 5 }))
                fparts.append(t9Part(kv, by: BFormatSpec { debug: true }))
                stmts.append(ltree.addLWrite(ref t, from: LWrite { span: span, sink: .print, target: b, parts: move fparts }))
                // Lowering's locals: temps, a void and a never one, pattern bindings that own and that borrow.
                c.locals.append(LocalSymbol { name: "t", type: semantics.prim(.i64), kind: .temp })
                locals.append(c.locals.length - 1)
                c.locals.append(LocalSymbol { name: "t", type: semantics.prim(.void), kind: .temp })
                locals.append(c.locals.length - 1)
                c.locals.append(LocalSymbol { name: "t", type: semantics.prim(.neverKind), kind: .temp })
                locals.append(c.locals.length - 1)
                c.locals.append(LocalSymbol { name: "pv", type: semantics.prim(.i64), kind: .patternVariable })
                locals.append(c.locals.length - 1)
                c.locals.append(LocalSymbol { name: "pb", type: semantics.prim(.i64), kind: .patternVariable, borrowsSubject: true })
                locals.append(c.locals.length - 1)
            }
            if c.functions[fn].name == "lm" { stmts.append(t9Lowered(ref c, through: ref t, at: span, for: xs[0], by: c.functions[fn].parameters[0])) }
            stmts.append(ltree.addLReturn(ref t, from: LReturn { span: span }))
        }

        /// A print of each lowered expression kind the C# `Expr` and `Format` accept, at width 1.
        BStmt t9Lowered(ref Ctx c, through ref LTree t, at TextSpan span, for BExpr q, by LocalId zq) uses alloc {
            var ua: semantics.VariantId = -1
            for v in 0..c.unionVariants.length { if c.unionVariants[v].name == "ua9" { ua = v } }
            var eb: semantics.VariantId = -1
            for v in 0..c.errorVariants.length { if c.errorVariants[v].name == "bad29" { eb = v } }
            var xs = List<BExpr>()
            xs.append(ltree.addLOptionalHas(ref t, from: LOptionalHas { span: span, optional: q }))
            xs.append(ltree.addLOptionalValue(ref t, from: LOptionalValue { span: span, optional: q, type: semantics.prim(.i64) }))
            xs.append(ltree.addLUnionTag(ref t, from: LUnionTag { span: span, unionValue: q }))
            xs.append(ltree.addLUnionField(ref t, from: LUnionField { span: span, unionValue: q, variant: ua, index: 0 }))
            xs.append(ltree.addLErrorCode(ref t, from: LErrorCode { span: span, error: q }))
            xs.append(ltree.addLErrorField(ref t, from: LErrorField { span: span, error: q, variant: eb, index: 0 }))
            xs.append(ltree.addLAssignExpr(ref t, from: LAssignExpr { span: span, target: q, value: q }))
            var effects = List<BExpr>()
            effects.append(q)
            xs.append(ltree.addLSequence(ref t, from: LSequence { span: span, effects: move effects, value: q }))
            xs.append(ltree.addLIndexUnchecked(ref t, from: LIndexUnchecked { span: span, target: q, index: q, type: semantics.prim(.i64) }) because "a print part; nothing reads it")
            xs.append(ltree.addLNextChar(ref t, from: LNextChar { span: span, text: q, position: zq }))
            var parts = List<BInterpolationPart>()
            for x in xs { parts.append(t9Part(x, by: BFormatSpec { width: 1 })) }
            return ltree.addLWrite(ref t, from: LWrite { span: span, sink: .print, target: q, parts: move parts })
        }

        string t9PrintEmit(ref Ctx c, by bool checks) uses alloc {
            var sb = StrBuf()
            var cc = CCtx { packageRoot: c.packageRoot, ownershipChecks: checks }
            sb.append("== emit\n")
            for fn in t9Functions(ref c) {
                var t = LTree {}
                var stmts = List<BStmt>()
                var locals = List<LocalId>()
                t9Build(ref c, through: ref t, for: fn, to: ref stmts, at: ref locals)
                let body = c.functions[fn].body
                c.bBlocks[body & 0xFFFFFFFF].statements = move stmts
                var fl = List<LocalId>()
                for l in locals { fl.append(l) }
                let f = LoweredFunction { symbol: fn, locals: move fl, contextSlots: 1, panicSlots: 1, body: body }
                if c.functions[fn].name == "e0" { c.functions[fn].file = -1 }
                sb.append($"fn {c.functions[fn].name}\n")
                sb.append($"sig {cprint.signature(ref cc, from: ref c, through: t, for: f)}\n")
                sb.append(cprint.emitFunction(ref cc, from: ref c, through: in t, for: in f))
                var fe = cprint.functionEmitter(ref cc, from: ref c, for: in f)
                var owned = List<LocalId>()
                for p in c.functions[fn].parameters { owned.append(p) }
                for l in locals { owned.append(l) }
                for l in owned {
                    let has = cprint.hasSlot(ref cc, for: ref fe, from: ref c, at: l)
                    sb.append($"slot {has ? "true" : "false"} {cprint.slot(ref cc, for: ref fe, from: ref c, at: l)}\n")
                }
                // A by-reference parameter of an owned type is stored through its pointer.
                if c.functions[fn].name == "rq" {
                    let span = semantics.nodeSpan(in c, for: body)
                    let zm = t9Local(ref c, at: span, for: c.functions[fn].parameters[1])
                    let zr = t9Local(ref c, at: span, for: c.functions[fn].parameters[0])
                    sb.append($"store {cprint.checkedStore(ref cc, for: ref fe, from: ref c, through: t, at: zm, to: zr) ?? "null"}\n")
                }
                if c.functions[fn].name == "w" {
                    let lv = ltree.addLVoid(ref t, from: LVoid { span: semantics.nodeSpan(in c, for: body) })
                    sb.append(t9Claims(ref cc, through: ref fe, from: ref c, by: ref t, at: locals, for: lv))
                }
            }
            sb.append("== helpers\n")
            for n in cc.helperOrder { sb.append($"{n} {cc.helpers[n].proto}\n{cc.helpers[n].body}\n") }
            return $"{sb.finish()}"
        }

        /// The ownership-check entry points T10 calls, on `w`'s frame.
        string t9Claims(ref CCtx cc, through ref CFunc f, from ref Ctx c, by ref LTree t, at List<LocalId> locals, for BExpr lv) uses alloc {
            var sb = StrBuf()
            let fn = f.fn
            let span = semantics.nodeSpan(in c, for: c.functions[fn].body)
            let q = t9Local(ref c, at: span, for: c.functions[fn].parameters[0])
            let p = c.locals[locals[5]].type & 0xFFFFFFFF
            let field = t9Field(ref c, at: span, by: t9Local(ref c, at: span, for: locals[5]), for: c.structs[p].fields[0])
            let tuple = t9Tuple(ref c, at: span, by: t9Local(ref c, at: span, for: locals[0]))
            sb.append($"claimed {cprint.claimed(ref cc, for: ref f, from: ref c, through: t, at: "x()", by: q, to: span)}\n")
            sb.append($"claimed {cprint.claimed(ref cc, for: ref f, from: ref c, through: t, at: "x()", by: field, to: span)}\n")
            sb.append($"claimed {cprint.claimed(ref cc, for: ref f, from: ref c, through: t, at: "x()", by: tuple, to: span)}\n")
            cprint.claimAfterCall(ref cc, for: ref f, from: ref c, through: t, at: q, to: span)
            cprint.claimAfterCall(ref cc, for: ref f, from: ref c, through: t, at: field, to: span)
            sb.append($"after {f.afterCalls.length}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: tuple, to: q) ?? "null"}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: q, to: q) ?? "null"}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: q, to: q, claim: false) ?? "null"}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: field, to: q) ?? "null"}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: lv, to: q) ?? "null"}\n")
            // A temp target needs no hook; a slotted local stored without a claim.
            let temp = t9Local(ref c, at: span, for: locals[locals.length - 5])
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: temp, to: q) ?? "null"}\n")
            let k8 = t9Local(ref c, at: span, for: locals[7])
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: k8, to: q, claim: false) ?? "null"}\n")
            // Targets of an owned type with no root: a lowered and a bound node (`Place`'s `Expr` arms).
            let mt = c.locals[locals[7]].type
            let lix = ltree.addLOptionalValue(ref t, from: LOptionalValue { span: span, optional: q, type: mt })
            c.bIndexes.append(BIndex { span: span, target: q, index: q, type: mt })
            let bix = semantics.makeBound(.index, at: c.bIndexes.length - 1)
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: lix, to: q) ?? "null"}\n")
            sb.append($"store {cprint.checkedStore(ref cc, for: ref f, from: ref c, through: t, at: bix, to: q, claim: false) ?? "null"}\n")
            // An owned local passed by pointer is claimed after the call.
            cprint.claimAfterCall(ref cc, for: ref f, from: ref c, through: t, at: k8, to: span)
            for x in f.afterCalls { sb.append($"afterc {x}\n") }
            var call: BExpr = -1
            for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                if call == -1 && !ltree.isLowered(s) && semantics.boundKind(s) == .assign { call = c.bAssigns[s & 0xFFFFFFFF].target }
            }
            sb.append($"claimed {cprint.claimed(ref cc, for: ref f, from: ref c, through: t, at: "x()", by: call, to: span)}\n")
            sb.append($"claimed {cprint.claimed(ref cc, for: ref f, from: ref c, through: t, at: "x()", by: lv, to: span)}\n")
            return $"{sb.finish()}"
        }

        /// `main`'s body as it is bound: a local declaration has no C.
        string t9Fail(ref Ctx c) uses alloc {
            var cc = CCtx { packageRoot: c.packageRoot }
            for fn in t9Functions(ref c) {
                if c.functions[fn].name != "main" { continue }
                let t = LTree {}
                let body = c.functions[fn].body
                let f = LoweredFunction { symbol: fn, locals: List<LocalId>(), contextSlots: 0, panicSlots: 0, body: body }
                return cprint.emitFunction(ref cc, from: ref c, through: in t, for: in f)
            }
            return ""
        }
        """;

    private const string Fixture = """
        union error E {
            bad
        }

        struct P {
            i64 x = 1
            i64 y = 2
            P(i64 zq) { { { } } }
            void m(ref this) { { { } } }
        }

        union U {
            ua9(i64 n)
            ub9
        }

        union error E2 {
            bad29(i64 code)
        }

        never nv() { { { } } panic("x") }

        never nf() fails E2 { { { } } panic("y") }

        void rq(ref i64 zr, to ref Map<i64, i64> zm, at ref List<i64> zl) {
            { { } }
            zm[1] = 2
            zm[1] += 2
            zl[0] = 1
        }

        void fc(string zs) fails ParseError, Utf8Error {
            { { } }
            let k1 = try Hex.decode(zs)
            let k2 = try i64.parse(zs)
            let k3 = try Utf8.decode(k1)
        }

        void lm(i64 zq) {
            { { } }
            let k = 1
            let g: i64 -> i64 = a => { { { } } return a + k }
            let g2: i64 -> i64 = a => { { { } } return a }
        }

        void e0() { { { } } }

        struct Q {
        }

        void h2() { { { } } }

        struct F {
            i64 fa = 0
            void formatInto(ref StrBuf output, as FormatSpec spec) { { { } } }
        }

        struct G {
            i64 ga = 0
            void formatInto(ref StrBuf output) { { { } } }
        }

        struct K {
            F kf
            G kg
        }

        void w(i64 zq, for string zs, by bool zb, at rune zc, to Symbol zy, from P zp, through Q zr, until K zk) {
            { { } }
            let k1 = zq
            let k2 = zs
            let k3 = zb
            let k4 = zc
            let k5 = zy
            let k6 = zp
            let k7 = zr
            var k8 = Map<i64, i64>()
            k8[zq] = zq
            k8.slot(zq) = zq
            h2()
            print("x")
        }

        void v() fails E { { { } } }

        i64 u(i64 zq) fails E {
            { { } }
            let k1 = zq
            return k1
        }

        void h(move List<i64> zl) { { { } } }

        void main() {
            { { } }
            let k = 1
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T9 main did not build:\n{error}");
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
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path)
    {
        var c = Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        return c;
    }

    private static (int Exit, string Out, string Err) Run(string mode, bool checks, string path) =>
        SelfhostParserTests.Run(Build(), [mode, checks ? "true" : "false", path]);

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(LoweredProgram p, bool checks) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, checks], null)!;

    /// <summary><c>Lowerer.Lower</c>'s filter: the functions it lowers, in order.</summary>
    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric && fn.Name != "formatInto").ToList();

    private static FunctionEmitter Frame(CEmitter emitter, LoweredFunction f) => new(emitter, f);

    /// <summary>The C# twin of the generated main's <c>t9Build</c>, collecting the text each stubbed callee gives in C#.</summary>
    private static (List<BoundStmt> Stmts, List<LocalSymbol> Locals) Body(Compilation c, CEmitter emitter, FunctionSymbol fn, List<string> masks)
    {
        var body = fn.Body!;
        var syn = body.Syntax;
        if (fn.Name == "e0") return ([], []);
        var locals = body.Statements.OfType<BoundLocalDecl>().Select(d => d.Local).ToList();
        var inner = body.Statements.OfType<BoundBlock>().First();
        var calls = new List<BoundCall>();
        foreach (var s in body.Statements)
        {
            if (s is BoundExprStmt { Expr: BoundCall call }) calls.Add(call);
            if (s is BoundLocalDecl { Initializer: BoundTry { Operand: BoundCall tried } }) calls.Add(tried);
        }
        // `slot`'s default value is an argument lowering adds (Lowerer.cs:1589); the C# `Expr` reads it.
        var assigns = body.Statements.OfType<BoundAssign>()
            .Select(a => a.Target is BoundBuiltinCall { Member: BuiltinMember.MapSlot, Arguments.Length: 1 } b ? a with { Target = b with { Arguments = [.. b.Arguments, new BoundDefault(syn, b.Type)] } } : a).ToList();
        var xs = fn.Parameters.Select(p => (BoundExpr)new BoundLocal(syn, p)).ToList();
        var stmts = new List<BoundStmt>
        {
            inner, new LLabel(syn, "L0"), new LGoto(syn, "L0"), new LLoop(syn, inner), new LLoop(syn, new BoundBlock(syn, [new LLabel(syn, "L1")])),
        };
        if (locals.Count > 0 && locals[0].Type == PrimitiveType.I64) stmts.Add(new LClaim(syn, locals[0]));
        stmts.AddRange([new LSetContext(syn, null, 0), new LPushPanicHandler(syn, 0, null, inner), new LPopPanicHandler(syn, 0)]);
        if (fn.Parameters.Count > 0) stmts.Add(new LClaim(syn, fn.Parameters[0]));
        stmts.Add(new LPanic(syn, "boom"));
        stmts.Add(new LWrite(syn, WriteSink.Panic, null, [new BoundInterpolationPart("", null), new BoundInterpolationPart(null, null)]));
        stmts.AddRange(assigns);
        var probe = Frame(emitter, new LoweredFunction(fn, locals, 1, 1, body));
        string Expr(BoundExpr e) => (string)Call(probe, "Expr", e)!;
        foreach (var a in assigns)
        {
            masks.Add(Expr(a.Target));
            masks.Add(Expr(a.Value));
        }
        foreach (var x in xs) masks.Add(Expr(x));
        if (xs.Count > 0)
        {
            var q = xs[0];
            stmts.Add(new BoundExprStmt(syn, q));
            stmts.Add(new BoundAssign(syn, q, q, null));
            stmts.Add(new BoundAssign(syn, q, q, BinaryOp.Add));
            stmts.Add(new LReturnError(syn, q));
            stmts.Add(new LSetContext(syn, q, 0));
            stmts.Add(new LFreeArena(syn, q));
            if (locals.Count > 0) stmts.Add(new LPushPanicHandler(syn, 0, locals[0], inner));
            stmts.Add(new LReturn(syn, q));
        }
        if (calls.Count >= 2 && locals.Count >= 2)
        {
            stmts.Add(new LFailingCall(syn, locals[0], calls[0], locals[1]));
            stmts.Add(new LFailingCall(syn, locals[0], calls[1], null));
            var names = (Dictionary<LocalSymbol, string>)Field(probe, "_names")!;
            masks.Add((string)Call(probe, "Call", calls[0], "&" + names[locals[1]])!);
            masks.Add((string)Call(probe, "Call", calls[1], null)!);
            foreach (var call in calls.Skip(2))
            {
                stmts.Add(new LFailingCall(syn, locals[0], call, null));
                masks.Add((string)Call(probe, "Call", call, null)!);
            }
        }
        if (xs.Count >= 7)
        {
            var (q, s, b) = (xs[0], xs[1], xs[2]);
            stmts.Add(new BoundIf(syn, b, inner, null));
            stmts.Add(new BoundIf(syn, b, inner, inner));
            stmts.Add(new BoundIf(syn, b, inner, new BoundIf(syn, b, inner, null)));
            stmts.Add(new BoundIf(syn, b, inner, new LLabel(syn, "L2")));
            BoundInterpolationPart Part(BoundExpr v, BoundFormatSpec spec) => new(null, v) { Spec = spec };
            BoundFormatSpec Spec(int radix = 0, bool debug = false, long width = 0, uint fill = 32, int align = -1, int digits = -1) =>
                new(radix, debug, width, fill, align, digits);
            BoundInterpolationPart[] parts =
            [
                new("a", null), new(null, q), Part(q, Spec(radix: 16)), Part(b, Spec(radix: 2)), Part(q, Spec(digits: 2)),
                Part(q, Spec(radix: 8, width: 6)), Part(s, Spec(debug: true, width: 4)), Part(s, Spec(debug: true)), Part(q, Spec(debug: true)),
                Part(xs[3], Spec(debug: true)), Part(xs[4], Spec(debug: true)), Part(xs[5], Spec(debug: true)), Part(xs[6], Spec(debug: true)),
                Part(q, Spec(width: 2, fill: 42, align: 2, digits: 1)), Part(q, Spec(width: 3)),
            ];
            var guards = new BoundExpr?[parts.Length];
            guards[1] = b;
            stmts.Add(new LWrite(syn, WriteSink.Print, b, parts, guards));
            stmts.Add(new LWrite(syn, WriteSink.Log, q, [new BoundInterpolationPart("b", null)]));
            stmts.Add(new LWrite(syn, WriteSink.StrBuf, q, []));
            var p = (StructSymbol)locals[5].Type;
            var owned = new BoundField(syn, new BoundLocal(syn, locals[5]), p.Fields[0]);
            stmts.Add(new LWrite(syn, WriteSink.StrBuf, owned, []));
            stmts.Add(new BoundSetOnPanic(syn, fn));
            var kv = xs[7];
            var fv = new BoundField(syn, kv, ((StructSymbol)kv.Type).Fields[0]);
            stmts.Add(new LWrite(syn, WriteSink.Print, b,
                [Part(fv, Spec(radix: 2)), Part(fv, Spec(radix: 8, align: 1)), Part(fv, Spec(radix: 16, debug: true)), Part(fv, Spec(width: 5)), Part(kv, Spec(debug: true))]));
            locals.Add(new LocalSymbol("t", null, PrimitiveType.I64, LocalKind.Temp));
            locals.Add(new LocalSymbol("t", null, PrimitiveType.Void, LocalKind.Temp));
            locals.Add(new LocalSymbol("t", null, PrimitiveType.Never, LocalKind.Temp));
            locals.Add(new LocalSymbol("pv", null, PrimitiveType.I64, LocalKind.PatternVariable));
            locals.Add(new LocalSymbol("pb", null, PrimitiveType.I64, LocalKind.PatternVariable) { BorrowsSubject = true });
            masks.Add(Expr(fv));
            masks.Add(Expr(owned));
        }
        if (fn.Name == "lm")
        {
            var q = xs[0];
            var ua = c.Modules.SelectMany(m => m.Unions).SelectMany(u => u.Variants).Single(v => v.Name == "ua9");
            var eb = c.Modules.SelectMany(m => m.ErrorSets).SelectMany(e => e.Variants).Single(v => v.Name == "bad29");
            BoundExpr[] lowered =
            [
                new LOptionalHas(syn, q), new LOptionalValue(syn, q, PrimitiveType.I64), new LUnionTag(syn, q), new LUnionField(syn, q, ua, 0),
                new LErrorCode(syn, q), new LErrorField(syn, q, eb, 0), new LAssignExpr(syn, q, q), new LSequence(syn, [q], q),
                new LIndexUnchecked(syn, q, q, PrimitiveType.I64), new LNextChar(syn, q, fn.Parameters[0]),
            ];
            stmts.Add(new LWrite(syn, WriteSink.Print, q, [.. lowered.Select(x => new BoundInterpolationPart(null, x) { Spec = new BoundFormatSpec(0, false, 1, 32, -1, -1) })]));
            foreach (var x in lowered) masks.Add(Expr(x));
        }
        stmts.Add(new LReturn(syn, null));
        return (stmts, locals);
    }

    /// <summary><paramref name="text"/> with each mask (longest first) replaced by the stub's "".</summary>
    private static string Mask(string text, List<string> masks)
    {
        foreach (var m in masks.Where(m => m.Length > 0).Distinct().OrderByDescending(m => m.Length)) text = text.Replace(m, "");
        return text;
    }

    private static List<string> Lines(string text) => text.Split('\n').Where(s => s.Length > 0).ToList();

    // The helpers T9 and the T8 `format` register; the C# `Expr` (T10 `emitExpr`) and ownership (T7) register the rest, which the stubs do not.
    private static bool T9Helper(string name) => name.StartsWith("bynh_w_", StringComparison.Ordinal) || name.StartsWith("bynh_debug_", StringComparison.Ordinal) || name.StartsWith("bynh_fmt_", StringComparison.Ordinal) || name.StartsWith("bynh_chk_", StringComparison.Ordinal);

    private static Dictionary<string, List<string>> EmitOracle(Compilation c, bool checks)
    {
        var p = Lowerer.Lower(c);
        var emitter = NewEmitter(p, checks);
        var functions = Functions(c);
        functions.Single(fn => fn.Name == "e0").File = null;
        var emit = new List<string>();
        foreach (var fn in functions)
        {
            var masks = new List<string>();
            var (stmts, locals) = Body(c, emitter, fn, masks);
            var f = new LoweredFunction(fn, locals, 1, 1, new BoundBlock(fn.Body!.Syntax, [.. stmts]));
            emit.Add($"fn {fn.Name}");
            emit.Add($"sig {Frame(emitter, f).Signature()}");
            var fe = Frame(emitter, f);
            var text = fe.Emit();
            var signature = text[..text.IndexOf('\n')];
            emit.AddRange(Lines(signature + Mask(text[text.IndexOf('\n')..], masks)));
            var slots = Frame(emitter, f);
            foreach (var l in fn.Parameters.Concat<LocalSymbol>(locals))
                emit.Add($"slot {((bool)Call(slots, "HasSlot", l)! ? "true" : "false")} {Call(slots, "Slot", l)}");
            if (fn.Name == "rq")
            {
                var (zm, zr) = (new BoundLocal(fn.Body!.Syntax, fn.Parameters[1]), new BoundLocal(fn.Body!.Syntax, fn.Parameters[0]));
                emit.Add($"store {Mask((string?)Call(slots, "CheckedStore", zm, zr, true) ?? "null", [(string)Call(slots, "Expr", zr)!])}");
            }
            if (fn.Name == "w") emit.AddRange(ClaimsOracle(emitter, slots, fn, locals));
        }
        var helpers = new List<string>();
        var order = (List<string>)Field(emitter, "_helperOrder")!;
        var bodies = (System.Collections.IDictionary)Field(emitter, "_helpers")!;
        var t9Masks = new List<string>();
        foreach (var fn in functions) Body(c, emitter, fn, t9Masks);
        foreach (var n in order.Where(T9Helper))
        {
            var (proto, body) = ((string, string))bodies[n]!;
            helpers.Add($"{n} {proto}");
            helpers.AddRange(Lines(Mask(body, t9Masks)));
        }
        return new() { ["emit"] = emit, ["helpers"] = helpers };
    }

    private static List<string> ClaimsOracle(CEmitter emitter, FunctionEmitter fe, FunctionSymbol fn, List<LocalSymbol> locals)
    {
        var syn = fn.Body!.Syntax;
        var q = new BoundLocal(syn, fn.Parameters[0]);
        var p = (StructSymbol)locals[5].Type;
        var field = new BoundField(syn, new BoundLocal(syn, locals[5]), p.Fields[0]);
        var tuple = new BoundTupleElement(syn, new BoundLocal(syn, locals[0]), 0, PrimitiveType.I64);
        var masks = new List<string>();
        string Claimed(BoundExpr r) => Mask((string)Call(fe, "Claimed", "x()", r, syn)!, masks);
        var lines = new List<string> { $"claimed {Claimed(q)}", $"claimed {Claimed(field)}", $"claimed {Claimed(tuple)}" };
        Call(fe, "ClaimAfterCall", q, syn);
        Call(fe, "ClaimAfterCall", field, syn);
        lines.Add($"after {((List<string>)Field(fe, "_afterCalls")!).Count}");
        lines.Add($"store {(string?)Call(fe, "CheckedStore", tuple, q, true) ?? "null"}");
        lines.Add($"store {(string?)Call(fe, "CheckedStore", q, q, true) ?? "null"}");
        lines.Add($"store {(string?)Call(fe, "CheckedStore", q, q, false) ?? "null"}");
        lines.Add($"store {(string?)Call(fe, "CheckedStore", field, q, true) ?? "null"}");
        var lv = new LVoid(syn);
        lines.Add($"store {(string?)Call(fe, "CheckedStore", lv, q, true) ?? "null"}");
        lines.Add($"store {(string?)Call(fe, "CheckedStore", new BoundLocal(syn, locals[^5]), q, true) ?? "null"}");
        lines.Add($"store {Mask((string?)Call(fe, "CheckedStore", new BoundLocal(syn, locals[7]), q, false) ?? "null", [(string)Call(fe, "Expr", q)!])}");
        var mt = locals[7].Type;
        BoundExpr lix = new LOptionalValue(syn, q, mt);
        BoundExpr bix = new BoundIndex(syn, q, q, false, mt);
        string Expr(BoundExpr e) => (string)Call(fe, "Expr", e)!;
        lines.Add($"store {Mask((string?)Call(fe, "CheckedStore", lix, q, true) ?? "null", [Expr(lix), Expr(q)])}");
        lines.Add($"store {Mask((string?)Call(fe, "CheckedStore", bix, q, false) ?? "null", [Expr(bix), Expr(q)])}");
        Call(fe, "ClaimAfterCall", new BoundLocal(syn, locals[7]), syn);
        foreach (var x in (List<string>)Field(fe, "_afterCalls")!) lines.Add($"afterc {x}");
        lines.Add($"claimed {Claimed(fn.Body.Statements.OfType<BoundAssign>().First().Target)}");
        lines.Add($"claimed {Claimed(lv)}");
        return lines;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emit_MatchesCSharp(bool checks) => WithFixture(Fixture, path =>
    {
        var expected = EmitOracle(Compile(path), checks);
        var (exit, stdout, stderr) = Run("emit", checks, path);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        var actual = SelfhostSemanticsHarness.Sections(stdout);
        SelfhostSemanticsHarness.AssertSection(actual, "emit", expected["emit"]);
        SelfhostSemanticsHarness.AssertSection(actual, "helpers", expected["helpers"]);
    });

    // A statement with no C arm fails on both sides: `main`'s bound local declaration.
    [Fact]
    public void Emit_NoArmFailsLikeCSharp() => WithFixture(Fixture, path =>
    {
        const string mode = "bound";
        var c = Compile(path);
        var fn = Functions(c).Single(f => f.Name == "main");
        var f = new LoweredFunction(fn, [], 0, 0, fn.Body!);
        var e = Assert.Throws<TargetInvocationException>(() => typeof(FunctionEmitter).GetMethod("Emit")!.Invoke(Frame(NewEmitter(Lowerer.Lower(c), false), f), null));
        var (exit, _, stderr) = Run(mode, false, path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.InnerException!.Message}\n", stderr);
    });
}
