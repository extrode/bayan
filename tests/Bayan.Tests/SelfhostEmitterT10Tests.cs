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
/// 011 T10: <c>selfhost/emitter/cprint/expr.byn</c> (CEmitter.cs 1617-1944, minus the D3 branch at 1696-1699) against the C#
/// <c>FunctionEmitter.Expr</c> and <c>Call</c> as an in-process oracle. A generated <c>main.byn</c> emits the same expressions of
/// one fixture function as the C# does and prints the text (Q803). The T6 helpers run; the T7, T9 and T11 siblings the two
/// functions call are stubs until those tasks land, so the fixture reaches none of them (Q806).
/// </summary>
public sealed class SelfhostEmitterT10Tests
{
    // args: mode, the C file name and `#line` path of the C# `FunctionEmitter` ("-" for none), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CFunc = cprint.CFunc
        type LAssignExpr = ltree.LAssignExpr
        type LErrorCode = ltree.LErrorCode
        type LNextChar = ltree.LNextChar
        type LOptionalHas = ltree.LOptionalHas
        type LOptionalValue = ltree.LOptionalValue
        type LSequence = ltree.LSequence
        type LIndexUnchecked = ltree.LIndexUnchecked
        type LVoid = ltree.LVoid
        type LErrorField = ltree.LErrorField
        type LFunctionValue = ltree.LFunctionValue
        type BMove = semantics.BMove

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
            if args[0] == "expr" { Io.stdout.write(t10Print(ref c, through: ref l, at: args[1], for: args[2])) }
            if args[0] == "fail" { Io.stdout.write(t10Fail(ref c, through: ref l, at: args[1], for: args[2])) }
            if args[0] == "failbound" { Io.stdout.write(t10FailBound(ref c, through: ref l, at: args[1], for: args[2], as: t10Init(in c, for: "itp"))) }
            if args[0] == "failspan" { Io.stdout.write(t10FailBound(ref c, through: ref l, at: args[1], for: args[2], as: c.bCalls[t10Init(in c, for: "vc") & 0xFFFFFFFF].arguments[0])) }
            return 0
        }

        FuncId t10G(in Ctx c) {
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].name == "g" { return fn }
                }
            }
            return -1
        }

        /// The index of `g`'s top-level declaration of `name`.
        i64 t10Decl(in Ctx c, for string name) {
            for s in c.bBlocks[c.functions[t10G(in c)].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl && c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].name == name { return s & 0xFFFFFFFF }
            }
            return -1
        }

        /// A `try`'s operand: the call `Call` takes once lowering has split the `try` off.
        BExpr t10Bare(in Ctx c, at BExpr e) {
            if !ltree.isLowered(e) && semantics.boundKind(e) == .tryExpr { return c.bTries[e & 0xFFFFFFFF].operand }
            return e
        }

        BExpr t10Init(in Ctx c, for string name) => t10Bare(in c, at: c.bLocalDecls[t10Decl(in c, for: name)].initializer)

        LocalId t10Var(in Ctx c, for string name) => c.bLocalDecls[t10Decl(in c, for: name)].local

        semantics.TextSpan t10At(in Ctx c, for string name) => semantics.nodeSpan(in c, for: t10Init(in c, for: name))

        BExpr t10Read(ref Ctx c, for string name) uses alloc => localExpr(ref c, at: t10At(in c, for: name), for: t10Var(in c, for: name))

        CFunc t10Func(in Ctx c, at string file, for string linePath) uses alloc {
            let g = t10G(in c)
            var f = CFunc { fn: g, file: file, linePath: linePath }
            if linePath == "-" { f.linePath = null }
            for p in c.functions[g].parameters { f.names[p] = $"p_{c.locals[p].name}" }
            var n = 0
            for s in c.bBlocks[c.functions[g].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl {
                    let local = c.bLocalDecls[s & 0xFFFFFFFF].local
                    f.names[local] = $"l{n}_{c.locals[local].name}"
                    n += 1
                }
            }
            return f
        }

        string t10Fail(ref Ctx c, through ref LCtx l, at string file, for string linePath) uses alloc {
            var f = t10Func(in c, at: file, for: linePath)
            var cc = CCtx {}
            let v = ltree.addLVoid(ref l.tree, from: LVoid { span: t10At(in c, for: "a") })
            return cprint.emitExpr(ref cc, for: ref f, from: ref c, through: l.tree, at: v)
        }

        /// `e`, a bound node lowering removes (an interpolated string, a `rest` span literal), emitted unlowered.
        string t10FailBound(ref Ctx c, through ref LCtx l, at string file, for string linePath, as BExpr e) uses alloc {
            var f = t10Func(in c, at: file, for: linePath)
            var cc = CCtx {}
            return cprint.emitExpr(ref cc, for: ref f, from: ref c, through: l.tree, at: e)
        }

        string t10Print(ref Ctx c, through ref LCtx l, at string file, for string linePath) uses alloc {
            var f = t10Func(in c, at: file, for: linePath)
            var cc = CCtx {}
            let g = t10G(in c)
            let i64Type = c.locals[c.functions[g].parameters[0]].type
            var items = List<BExpr>()
            for n in ["a", "v", "mv", "pr", "tp", "te", "cd", "c0", "u0", "er", "fc", "ar", "p0", "lst", "nn", "sb", "on", "un", "mn", "hx", "fo", "arr", "q0", "mp", "o2", "e2", "sbn", "sbo", "rf", "rt", "ld", "rn", "rb", "ud", "ut", "ur", "hd", "ip", "pi", "pre", "pf", "iu", "so", "se", "si", "tr", "sd", "ea", "sb0", "up", "li", "lb", "lf", "ls", "kd", "cs", "sy", "qx", "sp", "ua", "cv", "sa", "sn", "cp", "hc", "qg", "rc", "bn", "ng", "nt", "cvn", "ix", "sl", "sw", "dz"] { items.append(t10Init(in c, for: n)) }
            for s in c.bBlocks[c.functions[g].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .exprStmt { items.append(t10Bare(in c, at: c.bExprStmts[s & 0xFFFFFFFF].expr)) }
            }
            items.append(semantics.addMove(ref c, from: BMove { span: t10At(in c, for: "te"), operand: t10Init(in c, for: "te") }))
            items.append(semantics.addThis(ref c, from: BThis { span: t10At(in c, for: "a"), type: i64Type }))
            items.append(ltree.addLOptionalHas(ref l.tree, from: LOptionalHas { span: t10At(in c, for: "o"), optional: t10Read(ref c, for: "o") }))
            items.append(ltree.addLOptionalValue(ref l.tree, from: LOptionalValue { span: t10At(in c, for: "o"), optional: t10Read(ref c, for: "o"), type: i64Type }))
            items.append(ltree.addLUnionTag(ref l.tree, from: LUnionTag { span: t10At(in c, for: "u0"), unionValue: t10Read(ref c, for: "u0") }))
            items.append(ltree.addLErrorCode(ref l.tree, from: LErrorCode { span: t10At(in c, for: "er"), error: t10Read(ref c, for: "er") }))
            items.append(ltree.addLAssignExpr(ref l.tree, from: LAssignExpr { span: t10At(in c, for: "a"), target: t10Read(ref c, for: "a"), value: t10Init(in c, for: "v") }))
            var effects = List<BExpr>()
            effects.append(t10Init(in c, for: "cd"))
            items.append(ltree.addLSequence(ref l.tree, from: LSequence { span: t10At(in c, for: "a"), effects: move effects, value: t10Read(ref c, for: "v") }))
            var none = List<BExpr>()
            items.append(ltree.addLSequence(ref l.tree, from: LSequence { span: t10At(in c, for: "a"), effects: move none, value: t10Read(ref c, for: "v") }))
            let u0 = ltree.addLIndexUnchecked(ref l.tree, from: LIndexUnchecked { span: t10At(in c, for: "a"), target: t10Read(ref c, for: "lst"), index: t10Read(ref c, for: "a"), type: i64Type }) because "a node for the emitter; it is never run"
            items.append(u0)
            let u1 = ltree.addLIndexUnchecked(ref l.tree, from: LIndexUnchecked { span: t10At(in c, for: "a"), target: t10Read(ref c, for: "arr"), index: t10Read(ref c, for: "a"), type: i64Type }) because "a node for the emitter; it is never run"
            items.append(u1)
            items.append(ltree.addLNextChar(ref l.tree, from: LNextChar { span: t10At(in c, for: "a"), text: t10Read(ref c, for: "str"), position: t10Var(in c, for: "a") }))
            let ev = c.bErrorVariants[t10Init(in c, for: "er") & 0xFFFFFFFF].variant
            items.append(ltree.addLErrorField(ref l.tree, from: LErrorField { span: t10At(in c, for: "er"), error: t10Read(ref c, for: "er"), variant: ev, index: 0 }))
            let uv = c.bUnionNews[t10Init(in c, for: "ua") & 0xFFFFFFFF].variant
            items.append(ltree.addLUnionField(ref l.tree, from: LUnionField { span: t10At(in c, for: "ua"), unionValue: t10Read(ref c, for: "ua"), variant: uv, index: 0 }))
            let lam = c.bCalls[t10Init(in c, for: "ap") & 0xFFFFFFFF].arguments[0] & 0xFFFFFFFF
            items.append(ltree.addLFunctionValue(ref l.tree, from: LFunctionValue { span: t10At(in c, for: "ap"), function: c.bLambdas[lam].lambda, functionType: c.bLambdas[lam].functionType }))
            items.append(ltree.addLFunctionValue(ref l.tree, from: LFunctionValue { span: t10At(in c, for: "ap"), function: c.bLambdas[lam].lambda, env: t10Var(in c, for: "a"), functionType: c.bLambdas[lam].functionType }))
            items.append(ltree.addLFunctionValue(ref l.tree, from: LFunctionValue { span: t10At(in c, for: "hc"), function: c.bCalls[t10Init(in c, for: "hc") & 0xFFFFFFFF].function, functionType: c.locals[c.functions[g].parameters[4]].type }))
            items.append(semantics.addLiteral(ref c, from: BLiteral { span: t10At(in c, for: "lb"), type: semantics.prim(.bool), value: Constant { kind: .flag, flag: true } }))
            let kl = t10Read(ref c, for: "lb")
            semantics.setConstant(ref c, for: kl, to: Constant { kind: .flag, flag: false })
            items.append(kl)
            let kp = localExpr(ref c, at: t10At(in c, for: "a"), for: c.functions[g].parameters[0])
            semantics.setConstant(ref c, for: kp, to: Constant { kind: .flag, flag: true })
            items.append(kp)
            for kt in [c.locals[t10Var(in c, for: "q0")].type, semantics.prim(.void), semantics.prim(.neverKind)] {
                let kth = semantics.addThis(ref c, from: BThis { span: t10At(in c, for: "a"), type: kt })
                semantics.setConstant(ref c, for: kth, to: Constant { kind: .flag, flag: true })
                items.append(kth)
            }
            var sb = StrBuf()
            sb.append("== expr\n")
            for i in 0..items.length {
                let e = items[i]
                sb.append($"{i} {cprint.emitExpr(ref cc, for: ref f, from: ref c, through: l.tree, at: e)}\n")
                if !ltree.isLowered(e) && semantics.boundKind(e) == .call {
                    sb.append($"{i} call {cprint.call(ref cc, for: ref f, from: ref c, through: l.tree, at: e & 0xFFFFFFFF, to: null)}\n")
                    sb.append($"{i} call-result {cprint.call(ref cc, for: ref f, from: ref c, through: l.tree, at: e & 0xFFFFFFFF, to: "R")}\n")
                }
            }
            sb.append("== helpers\n")
            for name in cc.helperOrder {
                if name.startsWith("bynh_from_ordinal_") { sb.append(cc.helpers[name].body) }
            }
            return $"{sb.finish()}"
        }
        """;

    private const string Fixture = """
        enum Color {
            red
            green
        }

        union U {
            a(i64 v)
            b
        }

        union error E {
            bad(i64 code)
            gone
        }

        struct P { }

        struct R {
            i64 x

            R(i64 x) {
                this.x = x
            }
        }

        struct Q {
            i64 x

            i64 get() => x

            void bump(ref this) {
                x += 1
            }
        }

        const tbl: i64[3] = [1, 2, 3]

        interface ISized {
            i64 size()
        }

        struct Sq : ISized {
            i64 side

            i64 size() => side
        }

        struct St {
            i64 id
            states {
                draft
                paid(i64 payment)
            }
        }

        void g(i64 k, from move string s, to ref i64 ys, at (i64 q, i64 r) pair, by i64 -> i64 fv, for bool flag, as ReadOnlySpan<u8> bytes, through ReadOnlySpan<string> argv) fails uses alloc, clock, console, fs, process {
            var a = k
            var v = a
            var mv = move s
            var pr = ys
            var tp = (k, a)
            var te = pair.q
            var cd = flag ? k : a
            var c0 = Color.red
            var u0 = U.b
            var er: E = .bad(k)
            var fc = fv(k)
            var ar = Arena()
            var p0 = P {}
            var lst: List<i64> = List<i64>()
            var nn = flag
            var o: i64? = a
            var str = "x"
            var arr: i64[2] = [a, v]
            var sb = StrBuf(capacity: k)
            var on = Owned(a)
            var un = Untrusted(str)
            var mn = Io.monotonicNanos()
            var hx = Hex.encode(bytes)
            var fo = Color.fromOrdinal(k)
            var q0 = Q { x: k }
            var mp = Map<string, i64>()
            var o2: i64? = null
            var e2: E = .gone
            var buf = stackalloc u8[4]
            var sbn = StrBuf(buf)
            var sbo = StrBuf.over(buf)
            var rf = try Io.readFile(str)
            var rt = try Io.readText(str)
            var ld = try Io.listDir(str)
            var rn = try Io.run(str, argv)
            var rb = try Io.runBatch(str, argv)
            var ud = try Utf8.decode(bytes)
            var ut = Utf8.tryDecode(bytes)
            var ur = Utf8.decode(bytes, invalid: .replace)
            var hd = try Hex.decode(str)
            var ip = try i64.parse(str)
            var pi = Path.isRooted(str)
            var pre = Path.relative(str, str)
            var pf = Path.fileName(str)
            var iu = string.invalidUtf8At(bytes)
            var so = Io.stdout
            var se = Io.stderr
            var si = Io.stdin
            var tr = un.trustUnchecked() because "a fixture"
            var itp = $"{a}"
            var sd = St.draft { id: k }
            var ea: i64[0] = []
            var sb0 = StrBuf()
            var up = try u8.parse(str)
            var vc = total(k, a)
            var li = 5
            var lb = true
            var lf = 1.5
            var ls = "lit"
            let kc = 3
            var kd = kc
            var cs = 2 + 3
            var sy: Symbol = "sym"
            var qx = q0.x
            var sp = St.paid { id: k, payment: a }
            var ua = U.a(k)
            var cv = Color.values
            var sa = tbl
            var sn = Symbol.intern(str)
            var cp = Color.parse(str)
            var hc = h(k)
            var qg = q0.get()
            var ap = apply(x => x)
            inc(ref a)
            put(out v)
            q0.bump()
            put(out q0.x)
            var rc = R(k)
            var lg = 2.5
            var bn = lf - lg
            var ng = -lf
            var nt = !flag
            var cvn = f64(a)
            var ix = lst[a]
            var sl = str[a..v]
            var sw = str.startsWith(ls)
            var sq = Sq { side: k }
            let dr: dyn ref ISized = sq
            var dz = dr.size()
            try Io.writeFile(str, bytes)
            print(str)
            log(str)
            panic(str)
            todo(str)
        }

        i64 total(rest ReadOnlySpan<i64> xs) => 0

        i64 h(i64 x) => x

        i64 apply(i64 -> i64 fn) => fn(1)

        void inc(ref i64 x) {
            x += 1
        }

        void put(out i64 x) {
            x = 1
        }

        void main() { }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T10 main did not build:\n{error}");
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
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t10-" + Guid.NewGuid().ToString("N"));
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

    private static object? Call(Type type, object? target, string name, params object?[] args) =>
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    /// <summary>A <c>FunctionEmitter</c> for <c>g</c> whose locals are its top-level declarations, as the generated main names them.</summary>
    private static (object Fe, FunctionSymbol G, Dictionary<string, BoundLocalDecl> Decls) NewEmitterFor(Compilation c)
    {
        var emitter = NewEmitter(Lowerer.Lower(c));
        var g = c.Modules.SelectMany(m => m.AllFunctions).First(f => f.Name == "g");
        var decls = g.Body!.Statements.OfType<BoundLocalDecl>().ToDictionary(d => d.Local.Name);
        var lowered = new LoweredFunction(g, g.Body.Statements.OfType<BoundLocalDecl>().Select(d => d.Local).ToList(), 0, 0, g.Body);
        return (new FunctionEmitter(emitter, lowered), g, decls);
    }

    private static string Expr(object fe, BoundExpr e) => (string)Call(typeof(FunctionEmitter), fe, "Expr", e)!;

    private static (string File, string LinePath) FileOf(object fe) =>
        ((string)Field(fe, "_file")!, (string?)Field(fe, "_linePath") ?? "\"a.byn\"");

    /// <summary>A <c>try</c>'s operand: the call <c>Call</c> takes once lowering has split the <c>try</c> off.</summary>
    private static BoundExpr Bare(BoundExpr e) => e is BoundTry t ? t.Operand : e;

    /// <summary>The <c>expr</c> section, and in <paramref name="helpers"/> the <c>enumFromOrdinal</c> helpers it registered.</summary>
    private static List<string> ExprOracle(Compilation c, out List<string> helpers)
    {
        var (fe, g, decls) = NewEmitterFor(c);
        BoundExpr Init(string n) => Bare(decls[n].Initializer!);
        Node At(string n) => Init(n).Syntax;
        BoundExpr Read(string n) => new BoundLocal(At(n), decls[n].Local);
        var i64 = PrimitiveType.I64;
        var items = new List<BoundExpr>();
        foreach (var n in new[] { "a", "v", "mv", "pr", "tp", "te", "cd", "c0", "u0", "er", "fc", "ar", "p0", "lst", "nn", "sb", "on", "un", "mn", "hx", "fo", "arr", "q0", "mp", "o2", "e2", "sbn", "sbo", "rf", "rt", "ld", "rn", "rb", "ud", "ut", "ur", "hd", "ip", "pi", "pre", "pf", "iu", "so", "se", "si", "tr", "sd", "ea", "sb0", "up", "li", "lb", "lf", "ls", "kd", "cs", "sy", "qx", "sp", "ua", "cv", "sa", "sn", "cp", "hc", "qg", "rc", "bn", "ng", "nt", "cvn", "ix", "sl", "sw", "dz" }) items.Add(Init(n));
        items.AddRange(g.Body!.Statements.OfType<BoundExprStmt>().Select(s => Bare(s.Expr)));
        items.Add(new BoundMove(At("te"), Init("te")));
        items.Add(new BoundThis(At("a"), i64));
        items.Add(new LOptionalHas(At("o"), Read("o")));
        items.Add(new LOptionalValue(At("o"), Read("o"), i64));
        items.Add(new LUnionTag(At("u0"), Read("u0")));
        items.Add(new LErrorCode(At("er"), Read("er")));
        items.Add(new LAssignExpr(At("a"), Read("a"), Init("v")));
        items.Add(new LSequence(At("a"), [Init("cd")], Read("v")));
        items.Add(new LSequence(At("a"), [], Read("v")));
        items.Add(new LIndexUnchecked(At("a"), Read("lst"), Read("a"), i64));
        items.Add(new LIndexUnchecked(At("a"), Read("arr"), Read("a"), i64));
        items.Add(new LNextChar(At("a"), Read("str"), decls["a"].Local));
        items.Add(new LErrorField(At("er"), Read("er"), ((BoundErrorVariant)Init("er")).Variant, 0));
        items.Add(new LUnionField(At("ua"), Read("ua"), ((BoundUnionNew)Init("ua")).Variant, 0));
        var lam = (BoundLambda)((BoundCall)Init("ap")).Arguments[0];
        items.Add(new LFunctionValue(At("ap"), lam.Lambda, null, lam.FunctionType));
        items.Add(new LFunctionValue(At("ap"), lam.Lambda, decls["a"].Local, lam.FunctionType));
        items.Add(new LFunctionValue(At("hc"), ((BoundCall)Init("hc")).Function, null, (FunctionType)g.Parameters[4].Type));
        items.Add(new BoundLiteral(At("lb"), PrimitiveType.Bool, new ConstantValue(true)));
        items.Add(new BoundLocal(At("lb"), decls["lb"].Local) { Constant = new ConstantValue(false) });
        items.Add(new BoundLocal(At("a"), g.Parameters[0]) { Constant = new ConstantValue(true) });
        foreach (var kt in new TypeSymbol[] { decls["q0"].Local.Type, PrimitiveType.Void, PrimitiveType.Never })
            items.Add(new BoundThis(At("a"), kt) { Constant = new ConstantValue(true) });
        var lines = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            lines.Add($"{i} {Expr(fe, items[i])}");
            if (items[i] is BoundCall call)
            {
                lines.Add($"{i} call {Call(typeof(FunctionEmitter), fe, "Call", call, null)}");
                lines.Add($"{i} call-result {Call(typeof(FunctionEmitter), fe, "Call", call, "R")}");
            }
        }
        var emitter = Field(fe, "_c")!;
        var registered = (System.Collections.IDictionary)Field(emitter, "_helpers")!;
        var bodies = ((List<string>)Field(emitter, "_helperOrder")!).Where(h => h.StartsWith("bynh_from_ordinal_", StringComparison.Ordinal))
            .Select(h => (((string, string))registered[h]!).Item2);
        helpers = string.Concat(bodies).Split('\n').SkipLast(1).ToList();
        return lines;
    }

    private static Dictionary<string, List<string>> RunMode(string mode, string path, string file, string linePath, out int exit, out string stderr)
    {
        var (code, stdout, err) = SelfhostParserTests.Run(Build(), [mode, file, linePath, path]);
        exit = code;
        stderr = err;
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    [Fact]
    public void TheEmitterBuilds() => SelfhostEmitterHarness.Build();

    [Fact]
    public void Build_TheT10MainBuilds() => Build();

    [Fact]
    public void Expr_MatchesCSharp() => WithFixture(Fixture, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items.Where(d => d.Severity == Severity.Error).ToList(), includePath: true));
        var (file, linePath) = FileOf(NewEmitterFor(c).Fe);
        var actual = RunMode("expr", path, file, linePath, out var exit, out var stderr);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        SelfhostSemanticsHarness.AssertSection(actual, "expr", ExprOracle(c, out var helpers));
        SelfhostSemanticsHarness.AssertSection(actual, "helpers", helpers);
    });

    [Fact]
    public void Expr_OfAnUnemittableNodeFailsLikeCSharp() => WithFixture(Fixture, path =>
    {
        var c = Compile(path);
        var (fe, _, decls) = NewEmitterFor(c);
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => Expr(fe, new LVoid(decls["a"].Initializer!.Syntax)));
        Assert.Equal("cannot emit LVoid", thrown.InnerException!.Message);
        var (file, linePath) = FileOf(fe);
        RunMode("fail", path, file, linePath, out var exit, out var stderr);
        Assert.NotEqual(0, exit);
        Assert.Equal(thrown.InnerException!.Message, PanicMessage(stderr));
    });

    [Theory]
    [InlineData("failbound")]
    [InlineData("failspan")]
    public void Expr_OfAnUnloweredBoundNodeFailsLikeCSharp(string mode) => WithFixture(Fixture, path =>
    {
        var c = Compile(path);
        var (fe, _, decls) = NewEmitterFor(c);
        // An interpolated string has no case in `Expr`; a `rest` span literal is a `BoundCollection` of no list, map or array type.
        var node = mode == "failbound" ? decls["itp"].Initializer! : ((BoundCall)decls["vc"].Initializer!).Arguments[0];
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => Expr(fe, node));
        Assert.Equal(mode == "failbound" ? "cannot emit BoundInterpolated" : "cannot emit BoundCollection", thrown.InnerException!.Message);
        var (file, linePath) = FileOf(fe);
        RunMode(mode, path, file, linePath, out var exit, out var stderr);
        Assert.NotEqual(0, exit);
        Assert.Equal(thrown.InnerException!.Message, PanicMessage(stderr));
    });

    // The selfhost panic's message: its stderr line holding "cannot emit", from there to the end of the line.
    private static string PanicMessage(string stderr)
    {
        var line = stderr.Split('\n').First(l => l.Contains("cannot emit"));
        return line[line.IndexOf("cannot emit", StringComparison.Ordinal)..].TrimEnd('\r');
    }
}
