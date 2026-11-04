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
/// 011 T5: <c>selfhost/emitter/lowerassert.byn</c> (Lowerer.cs 1376-1728 but <c>Assert</c>, which waits on T21) and
/// <c>powerassert.byn</c> (PowerAssert.cs 1-78) against the C# as an in-process oracle. The emitter's top-level files but
/// <c>main.byn</c> are built with a generated <c>main.byn</c> that prints a shape of what the ported functions return and
/// emit, by mode; the C# prints the same shape of what <c>FunctionLowerer</c> returns and emits.
/// </summary>
public sealed class SelfhostEmitterT5Tests
{
    // args: mode, "-", "-", then the fixture path and the cases ("fn:index:kind"), or the data of col, flat and diag.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type BInterpolated = semantics.BInterpolated
        type BSwitchExpr = semantics.BSwitchExpr
        type BTryFrom = semantics.BTryFrom

        i32 main(ReadOnlySpan<string> args) {
            let mode = args[0]
            if mode == "col" {
                Io.stdout.write("== col\n")
                for i in 3..args.length { Io.stdout.write($"{powerAssertColumn(args[i])}\n") }
                return 0
            }
            if mode == "flat" {
                Io.stdout.write("== flat\n")
                for i in 3..args.length { Io.stdout.write($"[{powerAssertFlatten(args[i])}]\n") }
                return 0
            }
            if mode == "diag" {
                Io.stdout.write("== diag\n")
                for i in 3..args.length { Io.stdout.write(t5Diagram(args[i])) }
                return 0
            }
            var rest = List<string>()
            rest.append(args[3])
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            if mode == "cases" {
                Io.stdout.write("== cases\n")
                for i in 4..args.length { Io.stdout.write(t5Case(ref c, through: ref l, for: args[i])) }
            }
            if mode == "synth" { Io.stdout.write(t5Synth(ref c, through: ref l)) }
            return 0
        }

        /// `columns/always` as "0,3,7/tft": the pieces of `Diagram`.
        string t5Diagram(string spec) uses alloc {
            var columns = List<i64>()
            var always = List<bool>()
            var n: i64 = 0
            var seen = false
            var flags = false
            for b in spec.bytes {
                if b == 47 {
                    if seen { columns.append(n) }
                    flags = true
                } else if flags {
                    always.append(b == 116)
                } else if b == 44 {
                    columns.append(n)
                    n = 0
                } else {
                    n = n * 10 + i64(b) - 48
                    seen = true
                }
            }
            var sb = StrBuf()
            sb.append($"diagram {spec}\n")
            for p in powerAssertDiagram(in columns, by: in always) {
                var from: i64 = 0
                for at in 0..p.text.length {
                    if p.text.bytes[at] == 10 {
                        sb.append(p.text[from..at])
                        sb.append("\\n")
                        from = at + 1
                    }
                }
                sb.append(p.text[from..p.text.length])
                sb.append($"|{p.value}|")
                for k in p.allShown { sb.append($"{k},") }
                sb.append("|")
                for k in p.noneShown { sb.append($"{k},") }
                sb.append($"|{p.anyFrom}\n")
            }
            return $"{sb.finish()}"
        }

        /// The nth field of `s`, split at ':'.
        string t5Field(string s, at i64 n) {
            var k: i64 = 0
            var start: i64 = 0
            var j: i64 = 0
            for b in s.bytes {
                if b == 58 {
                    if k == n { return s[start..j] }
                    k += 1
                    start = j + 1
                }
                j += 1
            }
            return s[start..s.length]
        }

        void t5Reset(ref LCtx l, for FuncId fn) uses alloc {
            l.fn = fn
            l.outs = List<List<BStmt>>()
            l.outs.append(List<BStmt>())
            l.locals = List<LocalId>()
            l.seen = Map<LocalId, bool>()
            l.scopes = List<LScope>()
            l.labelCount = 0
        }

        FuncId t5Func(ref Ctx c, for string name) uses alloc {
            for fn in 0..c.functions.length {
                if c.functions[fn].name == name && c.functions[fn].body != -1 { return fn }
            }
            return -1
        }

        /// The expression of statement `index` of `fn`: a declaration's initializer, an expression statement's expression, an assignment's target.
        BExpr t5Pick(in Ctx c, at FuncId fn, for i64 index) {
            let s = c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements[index]
            let i = s & 0xFFFFFFFF
            switch semantics.boundKind(s) {
                case .localDecl: return c.bLocalDecls[i].initializer
                case .exprStmt: return c.bExprStmts[i].expr
                default: return c.bAssigns[i].target
            }
        }

        string t5Local(in Ctx c, for LocalId x) => c.locals[x].kind == .temp ? "temp" : c.locals[x].name

        string t5Real(f64 r) {
            let hi = i64(r / 4294967296.0)
            return $"{hi}.{i64(r - f64(hi) * 4294967296.0)}"
        }

        string t5List(ref Ctx c, through in LCtx l, for in List<BExpr> xs) uses alloc {
            var sb = StrBuf()
            for x in xs { sb.append($"{t5Shape(ref c, through: in l, for: x)};") }
            return $"{sb.finish()}"
        }

        string t5Shape(ref Ctx c, through in LCtx l, for BExpr e) uses alloc {
            let key = cprint.key(ref c, for: ltree.typeOf(in c, through: in l.tree, for: e))
            let span = ltree.spanOf(in c, through: in l.tree, for: e)
            return $"{t5Body(ref c, through: in l, for: e)}:{key}@{span.start}+{span.length}"
        }

        string t5Body(ref Ctx c, through in LCtx l, for BExpr e) uses alloc {
            if ltree.isLowered(e) {
                switch ltree.lKindOf(e) {
                    case .voidValue: return "void"
                    case .neverValue: return "never"
                    case .optionalValue: return $"optval({t5Shape(ref c, through: in l, for: l.tree.optionalValues[ltree.lIndexOf(e)].optional)})"
                    case .optionalHas: return $"has({t5Shape(ref c, through: in l, for: l.tree.optionalHass[ltree.lIndexOf(e)].optional)})"
                    case .assignExpr: return $"set({t5Shape(ref c, through: in l, for: l.tree.assignExprs[ltree.lIndexOf(e)].target)};{t5Shape(ref c, through: in l, for: l.tree.assignExprs[ltree.lIndexOf(e)].value)})"
                    case .sequence: return $"seq({t5List(ref c, through: in l, for: in l.tree.sequences[ltree.lIndexOf(e)].effects)}{t5Shape(ref c, through: in l, for: l.tree.sequences[ltree.lIndexOf(e)].value)})"
                    case .indexUnchecked: return $"unchecked({t5Shape(ref c, through: in l, for: l.tree.indexUncheckeds[ltree.lIndexOf(e)].target)};{t5Shape(ref c, through: in l, for: l.tree.indexUncheckeds[ltree.lIndexOf(e)].index)})"
                    default: return "lowered?"
                }
            }
            let i = e & 0xFFFFFFFF
            switch semantics.boundKind(e) {
                case .literal:
                    let k = c.bLiterals[i].value
                    let hasK = c.constants.tryGet(at: e) is { } ? "#k" : ""
                    switch k.kind {
                        case .integer: return $"i{k.integer.lo}.{k.integer.hi}{hasK}"
                        case .real: return $"r{t5Real(k.real)}{hasK}"
                        case .flag: return $"b{k.flag}{hasK}"
                        case .text: return $"s{k.text}{hasK}"
                        default: return $"k{hasK}"
                    }
                case .defaultValue: return "default"
                case .local: return $"local({t5Local(in c, for: c.bLocals[i].local)})"
                case .field: return $"field({c.fields[c.bFields[i].field].name};{t5Shape(ref c, through: in l, for: c.bFields[i].receiver)})"
                case .tupleElement: return $"tuple({c.bTupleElements[i].index};{t5Shape(ref c, through: in l, for: c.bTupleElements[i].receiver)})"
                case .call:
                    let hasK = c.constants.tryGet(at: e) is { } ? "#k" : ""
                    let receiver = c.bCalls[i].receiver == -1 ? "-" : t5Shape(ref c, through: in l, for: c.bCalls[i].receiver)
                    var arguments = List<BExpr>()
                    for a in c.bCalls[i].arguments { arguments.append(a) }
                    return $"call({c.functions[c.bCalls[i].function].name}{hasK};{receiver};{t5List(ref c, through: in l, for: in arguments)})"
                case .builtinCall:
                    var arguments = List<BExpr>()
                    for a in c.bBuiltinCalls[i].arguments { arguments.append(a) }
                    return $"builtin({c.bBuiltinCalls[i].member};{t5Shape(ref c, through: in l, for: c.bBuiltinCalls[i].receiver)};{t5List(ref c, through: in l, for: in arguments)})"
                case .binary: return $"binary({c.bBinaries[i].op};{t5Shape(ref c, through: in l, for: c.bBinaries[i].left)};{t5Shape(ref c, through: in l, for: c.bBinaries[i].right)})"
                case .conversion: return $"conv({c.bConversions[i].kind};{t5Shape(ref c, through: in l, for: c.bConversions[i].operand)})"
                case .conditional: return $"cond({t5Shape(ref c, through: in l, for: c.bConditionals[i].condition)};{t5Shape(ref c, through: in l, for: c.bConditionals[i].whenTrue)};{t5Shape(ref c, through: in l, for: c.bConditionals[i].whenFalse)})"
                case .index: return $"index({t5Shape(ref c, through: in l, for: c.bIndexes[i].target)};{t5Shape(ref c, through: in l, for: c.bIndexes[i].index)})"
                case .structLiteral:
                    var fields = List<BExpr>()
                    for a in c.bStructLiterals[i].fields { fields.append(a) }
                    return $"struct({t5List(ref c, through: in l, for: in fields)})"
                default: return "bound?"
            }
        }

        string t5Parts(ref Ctx c, through in LCtx l, for in List<BInterpolationPart> parts) uses alloc {
            var sb = StrBuf()
            for p in parts {
                if p.value == -1 {
                    sb.append($"t'{p.text ?? ""}',")
                } else {
                    sb.append($"{t5Shape(ref c, through: in l, for: p.value)},")
                }
            }
            return $"{sb.finish()}"
        }

        string t5Stmt(ref Ctx c, through in LCtx l, for BStmt s) uses alloc {
            if ltree.isLowered(s) {
                let i = ltree.lIndexOf(s)
                switch ltree.lKindOf(s) {
                    case .write:
                        let target = l.tree.writes[i].target == -1 ? "-" : t5Shape(ref c, through: in l, for: l.tree.writes[i].target)
                        return $"write({l.tree.writes[i].sink};{target};{t5Parts(ref c, through: in l, for: in l.tree.writes[i].parts)})"
                    case .panic: return $"panic({l.tree.panics[i].message})"
                    case .label: return $"label({l.tree.labels[i].name})"
                    case .gotoStmt: return $"goto({l.tree.gotoStmts[i].label})"
                    default: return "lowered?"
                }
            }
            let i = s & 0xFFFFFFFF
            switch semantics.boundKind(s) {
                case .exprStmt: return $"expr({t5Shape(ref c, through: in l, for: c.bExprStmts[i].expr)})"
                case .assign: return $"assign({t5Shape(ref c, through: in l, for: c.bAssigns[i].target)};{t5Shape(ref c, through: in l, for: c.bAssigns[i].value)})"
                case .localDecl: return $"decl({t5Local(in c, for: c.bLocalDecls[i].local)})"
                case .ifStmt: return $"if({t5Shape(ref c, through: in l, for: c.bIfs[i].condition)};{t5Stmt(ref c, through: in l, for: c.bIfs[i].then)})"
                case .block:
                    var sb = StrBuf()
                    sb.append("block[")
                    var inner = List<BStmt>()
                    for x in c.bBlocks[i].statements { inner.append(x) }
                    for x in inner { sb.append($"{t5Stmt(ref c, through: in l, for: x)} ") }
                    sb.append("]")
                    return $"{sb.finish()}"
                default: return "stmt?"
            }
        }

        /// What a lowering returned and emitted, from a fresh lowerer.
        string t5Report(ref Ctx c, through ref LCtx l, for string head, at BExpr result) uses alloc {
            var sb = StrBuf()
            sb.append($"case {head}\n")
            sb.append(result == -1 ? "  result null\n" : $"  result {t5Shape(ref c, through: in l, for: result)}\n")
            for s in l.outs[0] { sb.append($"  out {t5Stmt(ref c, through: in l, for: s)}\n") }
            var temps: i64 = 0
            for x in l.locals {
                if c.locals[x].kind == .temp { temps += 1 }
            }
            sb.append($"  temps {temps}\n")
            return $"{sb.finish()}"
        }

        string t5Case(ref Ctx c, through ref LCtx l, for string spec) uses alloc {
            let fn = t5Func(ref c, for: t5Field(spec, at: 0))
            var index: i64 = 0
            for b in t5Field(spec, at: 1).bytes { index = index * 10 + i64(b) - 48 }
            let kind = t5Field(spec, at: 2)
            t5Reset(ref l, for: fn)
            let e = t5Pick(in c, at: fn, for: index)
            var result: BExpr = -1
            if kind == "callParts" { result = lowerCallParts(ref c, through: ref l, for: e) }
            if kind == "builtin" { result = lowerBuiltinCall(ref c, through: ref l, for: e) }
            if kind == "stable" { result = lowerStable(ref c, through: ref l, for: e) }
            if kind == "tryFrom" { result = lowerTryFrom(ref c, through: ref l, for: e) }
            if kind == "switch" { result = lowerSwitchExpr(ref c, through: ref l, for: e) }
            if kind == "assign" { lowerAssign(ref c, through: ref l, for: c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements[index]) }
            if kind == "write" { result = lowerWrite(ref c, through: ref l, for: e, by: .print, to: -1, as: c.bCalls[e & 0xFFFFFFFF].arguments[0]) }
            if kind == "levelTrivial" {
                let level = localExpr(ref c, at: c.bCalls[e & 0xFFFFFFFF].span, for: c.functions[fn].parameters[0])
                result = lowerWriteParts(ref c, through: ref l, for: e, by: .log, to: -1, as: c.bCalls[e & 0xFFFFFFFF].arguments[0], at: level)
            }
            if kind == "levelCall" {
                let level = t5Pick(in c, at: t5Func(ref c, for: "calls"), for: 0)
                result = lowerWriteParts(ref c, through: ref l, for: e, by: .log, to: -1, as: c.bCalls[e & 0xFFFFFFFF].arguments[0], at: level)
            }
            return t5Report(ref c, through: ref l, for: spec, at: result)
        }

        /// Hand-built nodes: calls with an operand that nv returns, a `Stable` optional value.
        string t5Synth(ref Ctx c, through ref LCtx l) uses alloc {
            let calls = t5Func(ref c, for: "calls")
            let donor = t5Pick(in c, at: calls, for: 0)
            let span = c.bCalls[donor & 0xFFFFFFFF].span
            let i64Type = c.bCalls[donor & 0xFFFFFFFF].type
            let id = c.bCalls[donor & 0xFFFFFFFF].function
            var sb = StrBuf()

            t5Reset(ref l, for: calls)
            // An empty never block, as the C# side: `lowerExpr` returns an `LNever` and emits nothing.
            let nv = semantics.addBlockExpr(ref c, from: BBlockExpr { span: span, block: semantics.addBlock(ref c, from: BBlock { span: span }), type: semantics.prim(.neverKind) })
            var args1 = List<BExpr>()
            args1.append(nv)
            let neverCall = semantics.addCall(ref c, from: BCall { span: span, function: id, arguments: move args1, type: i64Type })
            sb.append(t5Report(ref c, through: ref l, for: "neverCall", at: lowerCallParts(ref c, through: ref l, for: neverCall)))

            t5Reset(ref l, for: calls)
            let neverBuiltin = semantics.addBuiltinCall(ref c, from: BBuiltinCall { span: span, member: .listAdd, receiver: nv, type: semantics.prim(.void) })
            sb.append(t5Report(ref c, through: ref l, for: "neverBuiltin", at: lowerBuiltinCall(ref c, through: ref l, for: neverBuiltin)))

            var parts = List<BInterpolationPart>()
            parts.append(BInterpolationPart { text: "a" })
            let text = semantics.addInterpolated(ref c, from: BInterpolated { span: span, parts: move parts })
            var args2 = List<BExpr>()
            args2.append(text)
            let neverAppend = semantics.addBuiltinCall(ref c, from: BBuiltinCall { span: span, member: .strBufAppend, receiver: nv, arguments: move args2, type: semantics.prim(.void) })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverAppend", at: lowerBuiltinCall(ref c, through: ref l, for: neverAppend)))

            let neverTake = semantics.addBuiltinCall(ref c, from: BBuiltinCall { span: span, member: .listTake, receiver: nv, type: i64Type })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverTake", at: lowerBuiltinCall(ref c, through: ref l, for: neverTake)))

            var parts2 = List<BInterpolationPart>()
            parts2.append(BInterpolationPart { value: nv })
            let neverText = semantics.addInterpolated(ref c, from: BInterpolated { span: span, parts: move parts2 })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverWrite", at: lowerWrite(ref c, through: ref l, for: donor, by: .print, to: -1, as: neverText)))

            let neverSwitch = semantics.addSwitchExpr(ref c, from: BSwitchExpr { span: span, value: nv, type: i64Type })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverSwitch", at: lowerSwitchExpr(ref c, through: ref l, for: neverSwitch)))

            let neverTry = semantics.addTryFrom(ref c, from: BTryFrom { span: span, operand: nv, target: i64Type, type: i64Type })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverTry", at: lowerTryFrom(ref c, through: ref l, for: neverTry)))

            let optional = ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: span, optional: donor, type: i64Type })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "optStable", at: lowerStable(ref c, through: ref l, for: optional)))

            t5Reset(ref l, for: calls)
            var args3 = List<BExpr>()
            args3.append(nv)
            let neverArg = semantics.addBuiltinCall(ref c, from: BBuiltinCall { span: span, member: .strBufAppend, receiver: nv, arguments: move args3, type: semantics.prim(.void) })
            sb.append(t5Report(ref c, through: ref l, for: "neverArg", at: lowerBuiltinCall(ref c, through: ref l, for: neverArg)))

            t5Reset(ref l, for: calls)
            let noArgSlot = semantics.addBuiltinCall(ref c, from: BBuiltinCall { span: span, member: .mapSlot, receiver: nv, type: i64Type })
            sb.append(t5Report(ref c, through: ref l, for: "noArgSlot", at: lowerBuiltinCall(ref c, through: ref l, for: noArgSlot)))

            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "neverStable", at: lowerStable(ref c, through: ref l, for: ltree.addLNever(ref l.tree, from: LNever { span: span, type: semantics.prim(.neverKind) }))))

            t5Reset(ref l, for: calls)
            let unchecked = ltree.addLIndexUnchecked(ref l.tree, from: T3LIndexUnchecked { span: span, target: donor, index: donor, type: i64Type }) because "a hand-built place that Stable only inspects"
            sb.append(t5Report(ref c, through: ref l, for: "uncheckedStable", at: lowerStable(ref c, through: ref l, for: unchecked)))

            let lit = t5Literal(ref c, at: span, by: i64Type, as: Constant { kind: .flag, flag: true })
            var args4 = List<BExpr>()
            args4.append(lit)
            args4.append(lit)
            t5Reset(ref l, for: calls)
            let staticCall = semantics.addCall(ref c, from: BCall { span: span, function: id, receiver: lit, arguments: move args4, type: i64Type })
            sb.append(t5Report(ref c, through: ref l, for: "staticReceiver", at: lowerCallParts(ref c, through: ref l, for: staticCall)))

            t5Reset(ref l, for: calls)
            let kcall = semantics.addCall(ref c, from: BCall { span: span, function: id, type: i64Type })
            semantics.setConstant(ref c, for: kcall, to: Constant { kind: .flag, flag: true })
            sb.append(t5Report(ref c, through: ref l, for: "constantCall", at: lowerCallParts(ref c, through: ref l, for: kcall)))

            let builtinsFn = t5Func(ref c, for: "builtins")
            let strBuf = localExpr(ref c, at: span, for: c.functions[builtinsFn].parameters[2])
            var parts3 = List<BInterpolationPart>()
            parts3.append(BInterpolationPart { value: strBuf })
            let bufText = semantics.addInterpolated(ref c, from: BInterpolated { span: span, parts: move parts3 })
            t5Reset(ref l, for: calls)
            sb.append(t5Report(ref c, through: ref l, for: "builtinPart", at: lowerWrite(ref c, through: ref l, for: donor, by: .print, to: -1, as: bufText)))

            let kinds = t5Func(ref c, for: "kinds")
            for p in 0..c.functions[kinds].parameters.length {
                sb.append($"printable {p} {printable(c.locals[c.functions[kinds].parameters[p]].type)}\n")
            }
            return $"== synth\n{sb.finish()}"
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T5 main did not build:\n{error}");
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

    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t5-" + Guid.NewGuid().ToString("N"));
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

    private static Dictionary<string, List<string>> RunMode(string mode, params string[] data)
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), [mode, "-", "-", .. data]);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    private static readonly Type FunctionLowerer = typeof(Lowerer).Assembly.GetType("Bayan.Lowering.FunctionLowerer", throwOnError: true)!;

    private static object? Call(object? target, string name, params object?[] args) =>
        FunctionLowerer.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static object NewLowerer(Compilation c, FunctionSymbol fn) =>
        Activator.CreateInstance(FunctionLowerer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [fn, c.Builtins, null, null], null)!;

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric).ToList();

    private static string Lc(object name)
    {
        var s = name.ToString()!;
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    // ── The shapes both sides print ──────────────────────────────────────────────

    private sealed class Shapes(CEmitter emitter)
    {
        private string Real(double r)
        {
            var hi = (long)(r / 4294967296.0);
            return $"{hi}.{(long)(r - hi * 4294967296.0)}";
        }

        private string Local(LocalSymbol l) => l.Kind == LocalKind.Temp ? "temp" : l.Name;

        private string List(IEnumerable<BoundExpr> xs) => string.Concat(xs.Select(x => Expr(x) + ";"));

        public string Expr(BoundExpr e) => $"{Body(e)}:{emitter.Key(e.Type)}@{e.Syntax.Span.Start}+{e.Syntax.Span.Length}";

        private string Body(BoundExpr e)
        {
            switch (e)
            {
                case LVoid: return "void";
                case LNever: return "never";
                case LOptionalValue v: return $"optval({Expr(v.Optional)})";
                case LOptionalHas h: return $"has({Expr(h.Optional)})";
                case LAssignExpr a: return $"set({Expr(a.Target)};{Expr(a.Value)})";
                case LSequence q: return $"seq({List(q.Effects)}{Expr(q.Value)})";
                case LIndexUnchecked u: return $"unchecked({Expr(u.Target)};{Expr(u.Index)})";
                case BoundLiteral l:
                    return (l.Value.Value switch
                    {
                        Int128 v => $"i{(ulong)(v & ulong.MaxValue)}.{unchecked((ulong)(v >> 64))}",
                        long v => $"i{(ulong)v}.{(v < 0 ? ulong.MaxValue : 0)}",
                        double v => $"r{Real(v)}",
                        bool v => $"b{(v ? "true" : "false")}",
                        string v => $"s{v}",
                        _ => "k",
                    }) + (l.Constant is null ? "" : "#k");
                case BoundDefault: return "default";
                case BoundLocal l: return $"local({Local(l.Local)})";
                case BoundField f: return $"field({f.Field.Name};{Expr(f.Receiver)})";
                case BoundTupleElement t: return $"tuple({t.Index};{Expr(t.Receiver)})";
                case BoundCall c: return $"call({c.Function.Name}{(c.Constant is null ? "" : "#k")};{(c.Receiver is null ? "-" : Expr(c.Receiver))};{List(c.Arguments)})";
                case BoundBuiltinCall b: return $"builtin({Lc(b.Member)};{Expr(b.Receiver)};{List(b.Arguments)})";
                case BoundBinary b: return $"binary({Lc(b.Op)};{Expr(b.Left)};{Expr(b.Right)})";
                case BoundConversion c: return $"conv({Lc(c.Kind)};{Expr(c.Operand)})";
                case BoundConditional c: return $"cond({Expr(c.Condition)};{Expr(c.WhenTrue)};{Expr(c.WhenFalse)})";
                case BoundIndex i: return $"index({Expr(i.Target)};{Expr(i.Index)})";
                case BoundStructLiteral s: return $"struct({List(s.Fields)})";
                default: return "bound?";
            }
        }

        private string Parts(IEnumerable<BoundInterpolationPart> parts) =>
            string.Concat(parts.Select(p => p.Value is null ? $"t'{p.Text ?? ""}'," : Expr(p.Value) + ","));

        public string Stmt(BoundStmt s) => s switch
        {
            LWrite w => $"write({Lc(w.Sink)};{(w.Target is null ? "-" : Expr(w.Target))};{Parts(w.Parts)})",
            LPanic p => $"panic({p.Message})",
            LLabel l => $"label({l.Name})",
            LGoto g => $"goto({g.Label})",
            BoundExprStmt x => $"expr({Expr(x.Expr)})",
            BoundAssign a => $"assign({Expr(a.Target)};{Expr(a.Value)})",
            BoundLocalDecl d => $"decl({Local(d.Local)})",
            BoundIf i => $"if({Expr(i.Condition)};{Stmt(i.Then)})",
            BoundBlock b => "block[" + string.Concat(b.Statements.Select(x => Stmt(x) + " ")) + "]",
            _ => "stmt?",
        };
    }

    private static List<string> Report(Shapes shapes, object fl, string head, BoundExpr? result)
    {
        var lines = new List<string> { $"case {head}", result is null ? "  result null" : $"  result {shapes.Expr(result)}" };
        foreach (var s in (List<BoundStmt>)Field(fl, "_out")!) lines.Add($"  out {shapes.Stmt(s)}");
        lines.Add($"  temps {((List<LocalSymbol>)Field(fl, "_locals")!).Count(l => l.Kind == LocalKind.Temp)}");
        return lines;
    }

    private static BoundExpr Pick(FunctionSymbol fn, int index) => fn.Body!.Statements[index] switch
    {
        BoundLocalDecl d => d.Initializer!,
        BoundExprStmt x => x.Expr,
        BoundAssign a => a.Target,
        _ => throw new InvalidOperationException(),
    };

    // ── The fixture ──────────────────────────────────────────────────────────────

    private const string Fixture = """
        struct P {
            i64 a = 0
            string b = "x"

            void bump(ref this) {
                a += 1
            }

            i64 get() => a

            void add(ref this, i64 x, by i64 y) {
                a += x + y
            }
        }

        P mk() => P { a: 1 }

        List<i64> mkl() uses alloc => List<i64>()

        StrBuf mksb() uses alloc => StrBuf()

        i64 id(i64 v) => v

        i64 pair(i64 x, by i64 y) => x

        string ids(string s) => s

        enum Color {
            red
            green
        }

        void inc(ref i64 v) {
            v += 1
        }

        void kinds(i64 a, from f64 b, to bool c, by string d, as Color e, for P f) { }

        void calls(i64 k, from ref P p, to i64 -> i64 fv, by ref Map<string, P> m, as ref List<P> ps) {
            let c1 = id(k)
            let c2 = pair(id(k), by: id(k))
            let c3 = mk().get()
            let c4 = p.get()
            let c5 = fv(k)
            m["a"].bump()
            p.bump()
            var kk = 1
            inc(ref kk)
            let c6 = ps.removeLast().get()
            ps[k].add(id(k), by: id(k))
        }

        void builtins(ref List<i64> xs, to ref Map<string, P> m, by ref StrBuf sb, as ref Map<string, i64> mi, for string name) uses alloc {
            let k = 1
            xs.append(k)
            let r = xs.removeLast()
            let tk = xs.take()
            sb.append($"a{k}b")
            let ts = sb.take()
            let v = sb.finish()
            let rm = m.remove("a")
            let sl = m.slot("q")
            let sm = mi.slot("q")
            let mt = m.take()
            let sp = xs.span
            xs.append(id(k))
            let sn = (id(k)) switch { 1 => 10, _ => 20 }
            let sp2 = mkl().span
            let v2 = mksb().finish()
            sb.append(name)
            let ln = mkl().length
            xs.sort()
            xs.replace(at: 0..1, by: xs)
        }

        void opts(ref Owned<P>? o, to ref Map<string, List<i64>> ml, by ref Map<string, Owned<P>?> mo, as ref Map<string, P> m) uses alloc {
            let t = o.take()
            let t2 = ml["a"].take()
            let t3 = mo.slot("b").take()
            m.slot("c").bump()
        }

        void compound(ref Map<string, i64> mi, to ref List<i64> xs, by i64 k) uses alloc {
            mi["a"] += 1
            mi[ids("a")] += k
            xs[id(k)] += 1
            xs[k] += 1
            xs[0] = 1
            mi.slot("d") += 1
            mi.slot(ids("e")) += k
        }

        void arrs(ref i64[3] arr) {
            let k = 1
            arr[1] = 1
            arr[id(k)] = 2
        }

        void writes(i64 k, from Symbol sy, to string s) uses alloc, console {
            print($"a{k}b{s}")
            print($"x{sy}")
            print($"{id(k)}{id(k)}")
            print($"{k + 1}")
        }

        void places(ref List<P> ps, to ref Map<string, P> m, by ref List<i64> xs, as ref (i64 q, i64 r) t) {
            let k = 1
            ps[id(k)].a = 1
            m["a"] = mk()
            xs[id(k)] = 1
            t.q = 1
            xs[k] = 1
            m[ids("a")] = mk()
        }

        void tries(i64 k, from f64 d, to f32 f, by u8 b, as u64 u) {
            let t1 = u8.tryFrom(k)
            let t2 = i64.tryFrom(k)
            let t3 = u8.tryFrom(b)
            let t4 = i64.tryFrom(d)
            let t5 = i32.tryFrom(d)
            let t6 = i32.tryFrom(f)
            let t7 = u64.tryFrom(d)
            let t8 = i64.tryFrom(b)
            let t9 = u64.tryFrom(k)
            let t10 = i64.tryFrom(u)
            let t11 = u8.tryFrom(id(k))
            let t12 = u8.tryFrom(f)
            let sw = k switch { 1 => 10, _ => 20 }
            let t13 = f64.tryFrom(k)
        }

        void narrows(P? op, to i64 k) {
            if op is null { return }
            let n1 = op.get()
            let s1 = k switch { let x when x > 3 => x, _ => 0 }
            let s2 = k switch { let x when (x switch { 1 => true, _ => false }) => x, _ => 0 }
        }

        i64[3] mka() => [1, 2, 3]

        void spans() {
            let a = mka().span
        }

        void nevers(i64 k) {
            let s3 = k switch { 1 => panic("a"), _ => panic("b") }
        }

        void main() { }
        """;

    private static readonly string[] CaseSpecs =
    [
        "calls:0:callParts", "calls:1:callParts", "calls:2:callParts", "calls:3:callParts", "calls:4:callParts", "calls:5:callParts", "calls:6:callParts", "calls:8:callParts", "calls:9:callParts", "calls:10:callParts",
        "builtins:1:builtin", "builtins:2:builtin", "builtins:3:builtin", "builtins:4:builtin", "builtins:5:builtin", "builtins:6:builtin",
        "builtins:7:builtin", "builtins:8:builtin", "builtins:9:builtin", "builtins:10:builtin", "builtins:11:builtin", "builtins:12:builtin",
        "builtins:14:builtin", "builtins:15:builtin", "builtins:13:switch", "builtins:16:builtin", "builtins:17:builtin", "builtins:18:builtin", "builtins:19:builtin",
        "opts:0:builtin", "opts:1:builtin", "opts:2:builtin", "opts:3:callParts", "compound:0:assign", "compound:1:assign", "compound:2:assign", "compound:3:assign", "compound:4:assign", "compound:5:assign", "compound:6:assign", "builtins:2:stable", "builtins:8:stable", "builtins:9:stable", "arrs:1:stable", "arrs:2:stable",
        "writes:0:write", "writes:1:write", "writes:2:write", "writes:0:levelTrivial", "writes:2:levelTrivial", "writes:0:levelCall", "writes:2:levelCall", "writes:3:levelCall",
        "places:1:stable", "places:2:stable", "places:3:stable", "places:4:stable", "places:5:stable", "places:6:stable",
        "tries:0:tryFrom", "tries:1:tryFrom", "tries:2:tryFrom", "tries:3:tryFrom", "tries:4:tryFrom", "tries:5:tryFrom", "tries:6:tryFrom",
        "tries:7:tryFrom", "tries:8:tryFrom", "tries:9:tryFrom", "tries:10:tryFrom", "tries:11:tryFrom", "tries:12:switch", "tries:13:tryFrom",
        "narrows:1:callParts", "narrows:2:switch", "narrows:3:switch", "nevers:0:switch", "spans:0:builtin",
    ];

    private static List<string> CasesOracle(Compilation c)
    {
        var emitter = NewEmitter(Lowerer.Lower(c));
        var shapes = new Shapes(emitter);
        var functions = Functions(c);
        var lines = new List<string>();
        foreach (var spec in CaseSpecs)
        {
            var f = spec.Split(':');
            var fn = functions.First(x => x.Name == f[0]);
            var fl = NewLowerer(c, fn);
            var e = Pick(fn, int.Parse(f[1]));
            BoundExpr? result = null;
            switch (f[2])
            {
                case "callParts": result = (BoundExpr?)Call(fl, "CallParts", e); break;
                case "builtin": result = (BoundExpr)Call(fl, "BuiltinCall", e)!; break;
                case "stable": result = (BoundExpr)Call(fl, "Stable", e)!; break;
                case "tryFrom": result = (BoundExpr)Call(fl, "TryFrom", e)!; break;
                case "switch": result = (BoundExpr)Call(fl, "SwitchExpr", e)!; break;
                case "assign": Call(fl, "Assign", fn.Body!.Statements[int.Parse(f[1])]); break;
                case "write":
                    result = (BoundExpr)Call(fl, "Write", e, WriteSink.Print, null, ((BoundCall)e).Arguments[0])!;
                    break;
                case "levelTrivial":
                    result = (BoundExpr)Call(fl, "WriteParts", e, WriteSink.Log, null, ((BoundCall)e).Arguments[0], new BoundLocal(e.Syntax, fn.Parameters[0]))!;
                    break;
                case "levelCall":
                    result = (BoundExpr)Call(fl, "WriteParts", e, WriteSink.Log, null, ((BoundCall)e).Arguments[0], Pick(functions.First(x => x.Name == "calls"), 0))!;
                    break;
            }
            lines.AddRange(Report(shapes, fl, spec, result));
        }
        return lines;
    }

    private static List<string> SynthOracle(Compilation c)
    {
        var emitter = NewEmitter(Lowerer.Lower(c));
        var shapes = new Shapes(emitter);
        var calls = Functions(c).First(x => x.Name == "calls");
        var donor = (BoundCall)Pick(calls, 0);
        var syntax = donor.Syntax;
        var i64 = donor.Type;
        // C#'s LowerExpr of an empty never block is an LNever and emits nothing; the Bayan side builds the same block.
        var never = new BoundBlockExpr(syntax, new BoundBlock(syntax, []), PrimitiveType.Never);
        var lines = new List<string>();
        void Run(string head, Func<object, BoundExpr?> act)
        {
            var fl = NewLowerer(c, calls);
            var result = act(fl);
            lines.AddRange(Report(shapes, fl, head, result));
        }
        Run("neverCall", fl => (BoundExpr?)Call(fl, "CallParts", new BoundCall(syntax, donor.Function, null, [never], i64)));
        Run("neverBuiltin", fl => (BoundExpr)Call(fl, "BuiltinCall", new BoundBuiltinCall(syntax, BuiltinMember.ListAdd, never, [], PrimitiveType.Void))!);
        var text = new BoundInterpolated(syntax, [new BoundInterpolationPart("a", null)]);
        Run("neverAppend", fl => (BoundExpr)Call(fl, "BuiltinCall", new BoundBuiltinCall(syntax, BuiltinMember.StrBufAppend, never, [text], PrimitiveType.Void))!);
        Run("neverTake", fl => (BoundExpr)Call(fl, "BuiltinCall", new BoundBuiltinCall(syntax, BuiltinMember.ListTake, never, [], i64))!);
        var neverText = new BoundInterpolated(syntax, [new BoundInterpolationPart(null, never)]);
        Run("neverWrite", fl => (BoundExpr)Call(fl, "Write", donor, WriteSink.Print, null, neverText)!);
        Run("neverSwitch", fl => (BoundExpr)Call(fl, "SwitchExpr", new BoundSwitchExpr(syntax, never, [], i64))!);
        Run("neverTry", fl => (BoundExpr)Call(fl, "TryFrom", new BoundTryFrom(syntax, never, (PrimitiveType)i64, i64))!);
        Run("optStable", fl => (BoundExpr)Call(fl, "Stable", new LOptionalValue(syntax, donor, i64))!);
        Run("neverArg", fl => (BoundExpr)Call(fl, "BuiltinCall", new BoundBuiltinCall(syntax, BuiltinMember.StrBufAppend, never, [never], PrimitiveType.Void))!);
        Run("noArgSlot", fl => (BoundExpr)Call(fl, "BuiltinCall", new BoundBuiltinCall(syntax, BuiltinMember.MapSlot, never, [], i64))!);
        Run("neverStable", fl => (BoundExpr)Call(fl, "Stable", new LNever(syntax, PrimitiveType.Never))!);
        Run("uncheckedStable", fl => (BoundExpr)Call(fl, "Stable", new LIndexUnchecked(syntax, donor, donor, i64))!);
        var lit = new BoundLiteral(syntax, i64, new ConstantValue(true));
        Run("staticReceiver", fl => (BoundExpr?)Call(fl, "CallParts", new BoundCall(syntax, donor.Function, lit, [lit, lit], i64)));
        Run("constantCall", fl => (BoundExpr?)Call(fl, "CallParts", new BoundCall(syntax, donor.Function, null, [], i64) { Constant = new ConstantValue(true) }));
        var builtinsFn = Functions(c).First(x => x.Name == "builtins");
        var bufText = new BoundInterpolated(syntax, [new BoundInterpolationPart(null, new BoundLocal(syntax, builtinsFn.Parameters[2]))]);
        Run("builtinPart", fl => (BoundExpr)Call(fl, "Write", donor, WriteSink.Print, null, bufText)!);
        var printable = FunctionLowerer.GetMethod("Printable", BindingFlags.Static | BindingFlags.NonPublic)!;
        var kindsFn = Functions(c).First(x => x.Name == "kinds");
        for (var p = 0; p < kindsFn.Parameters.Count; p++)
            lines.Add($"printable {p} {((bool)printable.Invoke(null, [kindsFn.Parameters[p].Type])!).ToString().ToLowerInvariant()}");
        return lines;
    }

    [Fact]
    public void Cases_MatchCSharp() => WithFixture(Fixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("cases", [path, .. CaseSpecs]), "cases", CasesOracle(Compile(path))));

    [Fact]
    public void Synth_MatchesCSharp() => WithFixture(Fixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("synth", path), "synth", SynthOracle(Compile(path))));

    // ── PowerAssert.cs ───────────────────────────────────────────────────────────

    private static readonly string[] Texts =
    [
        "", "assert x == 1", "assert a\n    && b", "a  \t\n\t  b\r\n   c", "  lead\n   trail  \n", "assert f(\"日本\") == é", "assert s == \"😀😀\"\n  && x",
        "\n", "a\n\nb", "tab\t\n\tafter",
    ];

    [Fact]
    public void Column_MatchesCSharp() => AssertSection("col", RunMode("col", Texts), Texts.Select(t => PowerAssert.Column(t).ToString()));

    [Fact]
    public void Flatten_MatchesCSharp() => AssertSection("flat", RunMode("flat", Texts), Texts.SelectMany(t => $"[{PowerAssert.Flatten(t)}]".Split('\n')));

    private static readonly string[] Diagrams = ["0/t", "0/f", "2,6/tt", "2,6/ff", "1,4,9/tft", "0,2,4,6/ftft", "3,5/ft", "7/t", "0,1,2/fff", "4,8/tf", "1,3,5/ttt"];

    [Fact]
    public void Diagram_MatchesCSharp()
    {
        var expected = new StringBuilder();
        foreach (var spec in Diagrams)
        {
            var parts = spec.Split('/');
            var columns = parts[0].Split(',').Select(int.Parse).ToList();
            var always = parts[1].Select(ch => ch == 't').ToList();
            expected.Append($"diagram {spec}\n");
            foreach (var p in PowerAssert.Diagram(columns, always))
                expected.Append($"{p.Text.Replace("\n", "\\n")}|{p.Value}|{string.Concat(p.When.Select(k => k + ","))}|{string.Concat(p.Unless.Select(k => k + ","))}|{p.AnyFrom ?? -1}\n");
        }
        AssertSection("diag", RunMode("diag", Diagrams), expected.ToString().TrimEnd('\n').Split('\n'));
    }

    private static void AssertSection(string section, Dictionary<string, List<string>> actual, IEnumerable<string> expected) =>
        SelfhostSemanticsHarness.AssertSection(actual, section, expected.ToList());
}
