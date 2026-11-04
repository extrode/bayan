using System.Reflection;
using System.Text;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T4: <c>selfhost/emitter/lowerexpr.byn</c> and <c>lowerpat.byn</c> (Lowerer.cs 899-1088, 1173-1375, 1729-1926)
/// against the C# as an in-process oracle. Each `let` initializer of a fixture function goes through `Expr` on a fresh
/// `FunctionLowerer`; the statements it emits and the value it returns are dumped as a tree on both sides (Q773).
/// </summary>
public sealed class SelfhostEmitterT4Tests
{
    // args: mode, function, declaration ("*" for every `let`), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.ltree

        type BNode = semantics.BNode

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 3..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            if args[0] == "expr" || args[0] == "spread" || args[0] == "failing" || args[0] == "void" {
                Io.stdout.write(t4PrintExprs(ref c, through: ref l, for: args[1], at: args[2], by: args[0]))
            }
            if args[0] == "claim" { Io.stdout.write(t4PrintClaims(ref c, through: ref l, for: args[1])) }
            return 0
        }

        FuncId t4Func(ref Ctx c, for string name) {
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].name == name && c.functions[fn].body != -1 { return fn }
                }
            }
            return -1
        }

        void t4Reset(ref LCtx l, for FuncId fn) uses alloc {
            l.fn = fn
            l.outs = List<List<BStmt>>()
            l.outs.append(List<BStmt>())
            l.locals = List<LocalId>()
            l.seen = Map<LocalId, bool>()
            l.scopes = List<LScope>()
            l.labelCount = 0
        }

        string t4PrintExprs(ref Ctx c, through ref LCtx l, for string name, at string only, by string mode) uses alloc {
            var sb = StrBuf()
            sb.append("== expr\n")
            let fn = t4Func(ref c, for: name)
            var names = List<string>()
            var exprs = List<BExpr>()
            for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                if ltree.isLowered(s) { continue }
                let k = s & 0xFFFFFFFF
                if semantics.boundKind(s) == .localDecl && c.bLocalDecls[k].initializer != -1 {
                    names.append(c.locals[c.bLocalDecls[k].local].name)
                    exprs.append(c.bLocalDecls[k].initializer)
                }
                if semantics.boundKind(s) == .exprStmt {
                    names.append("stmt")
                    exprs.append(c.bExprStmts[k].expr)
                }
            }
            for d in 0..names.length {
                if only != "*" && names[d] != only { continue }
                t4Reset(ref l, for: fn)
                var target = exprs[d]
                if mode == "spread" { target = c.bCollections[target & 0xFFFFFFFF].elements[0] }
                if mode == "failing" { target = c.bTries[target & 0xFFFFFFFF].operand }
                if mode == "void" { target = ltree.addLVoid(ref l.tree, from: LVoid { span: semantics.nodeSpan(in c, for: target) }) }
                let r = lowerExpr(ref c, through: ref l, for: target)
                sb.append($"{names[d]}:")
                var outs = List<BStmt>()
                for s in l.outs[0] { outs.append(s) }
                for s in outs { sb.append($" {t4Dump(in c, through: in l, for: s)}") }
                sb.append($" => {t4Dump(in c, through: in l, for: r)} locals={l.locals.length}\n")
            }
            return $"{sb.finish()}"
        }

        /// `Claim` on every `switch` section pattern of `name`, in order.
        string t4PrintClaims(ref Ctx c, through ref LCtx l, for string name) uses alloc {
            var sb = StrBuf()
            sb.append("== claim\n")
            let fn = t4Func(ref c, for: name)
            var patterns = List<BPattern>()
            for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                if ltree.isLowered(s) || semantics.boundKind(s) != .switchStmt { continue }
                for sec in c.bSwitches[s & 0xFFFFFFFF].sections {
                    if sec.pattern != -1 { patterns.append(sec.pattern) }
                }
            }
            for p in patterns {
                t4Reset(ref l, for: fn)
                lowerClaim(ref c, through: ref l, for: p)
                sb.append("claim:")
                var outs = List<BStmt>()
                for s in l.outs[0] { outs.append(s) }
                for s in outs { sb.append($" {t4Dump(in c, through: in l, for: s)}") }
                sb.append($" locals={l.locals.length}\n")
            }
            return $"{sb.finish()}"
        }

        string t4Local(in Ctx c, through in LCtx l, for LocalId x) uses alloc {
            for k in 0..l.locals.length {
                if l.locals[k] == x { return $"{c.locals[x].name}#{k}" }
            }
            return $"{c.locals[x].name}#-"
        }

        string t4Dump(in Ctx c, through in LCtx l, for BNode n) uses alloc {
            if n == -1 { return "null" }
            var sb = StrBuf()
            var kids = List<BNode>()
            let i = n & 0xFFFFFFFF
            if ltree.isLowered(n) {
                sb.append($"{ltree.lKindOf(n)}")
                switch ltree.lKindOf(n) {
                    case .label: sb.append($" {l.tree.labels[i].name}")
                    case .gotoStmt: sb.append($" {l.tree.gotoStmts[i].label}")
                    case .returnError: kids.append(l.tree.returnErrors[i].code)
                    case .failingCall:
                        sb.append($" {t4Local(in c, through: in l, for: l.tree.failingCalls[i].error)}")
                        sb.append(l.tree.failingCalls[i].result == -1 ? " -" : $" {t4Local(in c, through: in l, for: l.tree.failingCalls[i].result)}")
                        kids.append(l.tree.failingCalls[i].call)
                    case .claim: sb.append($" {t4Local(in c, through: in l, for: l.tree.claims[i].local)}")
                    case .optionalHas: kids.append(l.tree.optionalHass[i].optional)
                    case .optionalValue: kids.append(l.tree.optionalValues[i].optional)
                    case .unionTag: kids.append(l.tree.unionTags[i].unionValue)
                    case .unionField:
                        sb.append($" {l.tree.unionFields[i].index}")
                        kids.append(l.tree.unionFields[i].unionValue)
                    case .errorCode: kids.append(l.tree.errorCodes[i].error)
                    case .errorField:
                        sb.append($" {l.tree.errorFields[i].index}")
                        kids.append(l.tree.errorFields[i].error)
                    case .assignExpr:
                        kids.append(l.tree.assignExprs[i].target)
                        kids.append(l.tree.assignExprs[i].value)
                    case .indexUnchecked:
                        kids.append(l.tree.indexUncheckeds[i].target)
                        kids.append(l.tree.indexUncheckeds[i].index)
                    case .functionValue:
                        sb.append($" {c.functions[l.tree.functionValues[i].function].name}")
                        if l.tree.functionValues[i].env != -1 { sb.append($" {t4Local(in c, through: in l, for: l.tree.functionValues[i].env)}") }
                    default: sb.append("")
                }
            } else {
                sb.append($"{semantics.boundKind(n)}")
                if semantics.boundKind(n) == .local { sb.append($" {t4Local(in c, through: in l, for: c.bLocals[i].local)}") }
                if semantics.boundKind(n) == .call { sb.append($" {c.functions[c.bCalls[i].function].name}") }
                for k in semantics.children(in c, for: n) { kids.append(k) }
            }
            if kids.length > 0 {
                sb.append("(")
                for k in 0..kids.length {
                    if k > 0 { sb.append(" ") }
                    sb.append(t4Dump(in c, through: in l, for: kids[k]))
                }
                sb.append(")")
            }
            return $"{sb.finish()}"
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T4 main did not build:\n{error}");
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
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t4-" + Guid.NewGuid().ToString("N"));
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

    private static (int Exit, string Out, string Err) Run(string mode, string fn, string decl, string path) =>
        SelfhostParserTests.Run(Build(), [mode, fn, decl, path]);

    private static Dictionary<string, List<string>> RunMode(string mode, string fn, string decl, string path)
    {
        var (exit, stdout, stderr) = Run(mode, fn, decl, path);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    // ── the C# side ─────────────────────────────────────────────────────────────

    private static readonly Type FunctionLowerer = typeof(Lowerer).Assembly.GetType("Bayan.Lowering.FunctionLowerer", throwOnError: true)!;

    private static object? Call(object target, string name, params object?[] args) =>
        FunctionLowerer.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        FunctionLowerer.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static object New(Compilation c, FunctionSymbol fn) =>
        Activator.CreateInstance(FunctionLowerer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [fn, c.Builtins, null, null], null)!;

    private static FunctionSymbol Function(Compilation c, string name) =>
        c.Modules.SelectMany(m => m.AllFunctions).First(f => f.Name == name && f.Body is not null);

    /// The selfhost `BoundKind` / `LKind` member for a C# node type (the C# record name, lowerCamel, with the
    /// keyword-suffixed names of `bound.byn` and `ltree.byn`).
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["Default"] = "defaultValue", ["This"] = "thisRef", ["With"] = "withExpr", ["Is"] = "isExpr", ["Move"] = "moveExpr",
        ["Try"] = "tryExpr", ["Catch"] = "catchExpr", ["Return"] = "returnStmt", ["Fail"] = "failStmt", ["Become"] = "becomeStmt",
        ["Break"] = "breakStmt", ["Continue"] = "continueStmt", ["If"] = "ifStmt", ["While"] = "whileStmt", ["For"] = "forStmt",
        ["Switch"] = "switchStmt", ["Defer"] = "deferStmt", ["Assert"] = "assertStmt", ["Errdefer"] = "errdeferStmt",
        ["Context"] = "contextStmt", ["LGoto"] = "gotoStmt", ["LReturn"] = "returnStmt", ["LNever"] = "neverValue",
        ["LVoid"] = "voidValue",
    };

    private static string KindName(BoundNode n)
    {
        var name = n.GetType().Name;
        if (Renamed.TryGetValue(name, out var r)) return r;
        var bare = name.StartsWith("Bound", StringComparison.Ordinal) ? name[5..] : name[1..];
        if (Renamed.TryGetValue(bare, out r)) return r;
        return char.ToLowerInvariant(bare[0]) + bare[1..];
    }

    private static string Dump(BoundNode? n, List<LocalSymbol> locals)
    {
        if (n is null) return "null";
        string Local(LocalSymbol x) => $"{x.Name}#{(locals.IndexOf(x) is var k && k >= 0 ? k.ToString() : "-")}";
        var sb = new StringBuilder(KindName(n));
        var kids = new List<BoundNode>();
        switch (n)
        {
            case LLabel x: sb.Append($" {x.Name}"); break;
            case LGoto x: sb.Append($" {x.Label}"); break;
            case LReturnError x: kids.Add(x.Code); break;
            case LFailingCall x:
                sb.Append($" {Local(x.Error)}").Append(x.Result is null ? " -" : $" {Local(x.Result)}");
                kids.Add(x.Call);
                break;
            case LClaim x: sb.Append($" {Local(x.Local)}"); break;
            case LOptionalHas x: kids.Add(x.Optional); break;
            case LOptionalValue x: kids.Add(x.Optional); break;
            case LUnionTag x: kids.Add(x.Union); break;
            case LUnionField x: sb.Append($" {x.Index}"); kids.Add(x.Union); break;
            case LErrorCode x: kids.Add(x.Error); break;
            case LErrorField x: sb.Append($" {x.Index}"); kids.Add(x.Error); break;
            case LAssignExpr x: kids.Add(x.Target); kids.Add(x.Value); break;
            case LIndexUnchecked x: kids.Add(x.Target); kids.Add(x.Index); break;
            case LFunctionValue x:
                sb.Append($" {x.Function.Name}");
                if (x.Env is not null) sb.Append($" {Local(x.Env)}");
                break;
            case LNever or LVoid or LSequence or LNextChar or LWrite or LPanic or LLoop or LReturn or LSetContext or LFreeArena
                or LPushPanicHandler or LPopPanicHandler:
                break;
            default:
                if (n is BoundLocal bl) sb.Append($" {Local(bl.Local)}");
                if (n is BoundCall bc) sb.Append($" {bc.Function.Name}");
                kids.AddRange(BoundTree.Children(n));
                break;
        }
        if (kids.Count > 0) sb.Append('(').Append(string.Join(' ', kids.Select(k => Dump(k, locals)))).Append(')');
        return sb.ToString();
    }

    /// The `let` initializers and expression statements of <paramref name="fn"/>'s body, in order.
    private static IEnumerable<(string Name, BoundExpr Expr)> Cases(FunctionSymbol fn)
    {
        foreach (var s in fn.Body!.Statements)
        {
            if (s is BoundLocalDecl { Initializer: { } init } d) yield return (d.Local.Name, init);
            if (s is BoundExprStmt x) yield return ("stmt", x.Expr);
        }
    }

    private static List<string> ExprOracle(Compilation c, string fnName, string only)
    {
        var fn = Function(c, fnName);
        var lines = new List<string>();
        foreach (var (name, e) in Cases(fn))
        {
            if (only != "*" && name != only) continue;
            var fl = New(c, fn);
            var r = (BoundExpr)Call(fl, "Expr", e)!;
            var locals = (List<LocalSymbol>)Field(fl, "_locals")!;
            var outs = (List<BoundStmt>)Field(fl, "_out")!;
            lines.Add($"{name}:{string.Concat(outs.Select(s => " " + Dump(s, locals)))} => {Dump(r, locals)} locals={locals.Count}");
        }
        return lines;
    }

    private static List<string> ClaimOracle(Compilation c, string fnName)
    {
        var fn = Function(c, fnName);
        var lines = new List<string>();
        foreach (var s in fn.Body!.Statements.OfType<BoundSwitch>())
            foreach (var sec in s.Sections)
            {
                if (sec.Pattern is null) continue;
                var fl = New(c, fn);
                Call(fl, "Claim", sec.Pattern);
                var locals = (List<LocalSymbol>)Field(fl, "_locals")!;
                var outs = (List<BoundStmt>)Field(fl, "_out")!;
                lines.Add($"claim:{string.Concat(outs.Select(o => " " + Dump(o, locals)))} locals={locals.Count}");
            }
        return lines;
    }

    private static void SameExprs(string source, string fn, string only = "*") => WithFixture(source, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("expr", fn, only, path), "expr", ExprOracle(Compile(path), fn, only)));

    // ── fixtures ────────────────────────────────────────────────────────────────

    private const string Decls = """
        union error E {
            x
            y(i64 n)
        }

        struct P {
            i64 a
            i64 b
        }

        union U {
            one(i64 v)
            two
        }

        i64 g() fails E => 1

        i64 h() => 2

        i64 f(i64 k, from P p, to i64? o, until (i64 x, i64 y) tu, at bool q, by List<i64> xs) {
            let a = 1
            let b = k
            let c = p.a
            let d = tu.x
            let e = -k
            let f1 = k + h()
            let g1 = q && k > 1
            let g2 = q || h() > 1
            let g3 = q ? 1 : 2
            let g4 = q ? h() : 2
            let g5 = o ?? 3
            let g6 = o ?? h()
            let g7 = P { a: 1, b: h() }
            let g8 = U.one(h())
            let g9 = p with { a: h() }
            let g10 = (x: 1, y: h())
            let g11 = 1..k
            let g13: List<i64> = [1, h()]
            let g16 = xs[k]
            let g17 = xs[1..k]
            let g18 = k is 1 or 2
            let g19 = k in 1..h()
            let g22: () -> i64 = h
            let g23 = E.y(h())
            return 0
        }

        i64 fails1() fails E {
            let a = try g()
            let b = g() catch 0
            let c = g() catch (.x) 7
            return a
        }

        void main() { }
        """;

    [Fact]
    public void Expr_DeclsMatchCSharp() => SameExprs(Decls, "f");

    [Fact]
    public void Expr_FailureMatchesCSharp() => SameExprs(Decls, "fails1");

    private const string Lazy = """
        union error E {
            x
            y(i64 n)
        }

        struct P {
            i64 a
            i64 b
            i64 sum => a + b
        }

        union U {
            one(i64 v)
            two(i64 v, i64 w)
            none
        }

        struct Q {
            P p
            P t
            string s
        }

        i64 g() fails E => 1

        void gv() fails E { }

        i64 h() => 2

        void hv() { }

        i64 lazy(i64 k, at bool q, to i64? o, from P p, until U u, by List<i64> xs, for i64[3] arr, through Q qq, as U? ou) fails E {
            let a1 = q && (g() catch 0) > 1
            let a2 = q || (g() catch 0) > 1
            let a3 = q ? (g() catch 0) : 2
            let a4 = q ? 1 : (g() catch 0)
            let a5 = o ?? (g() catch 0)
            let a6 = [..arr, 4]
            let a7: List<i64> = [..xs, 5]
            let a8: List<i64> = [..arr, 6]
            let a9 = [1, 2]
            let b1 = u is .one(let v) && v > 1
            let b2 = u is .two(1, let w)
            let b3 = p is { a: 1, b: > 2 }
            let b4 = k is > 1 and < 9
            let b5 = k is not 3
            let b6 = qq is { p: { a: 1 }, t: { a: 2 } }
            let b7 = o is 3
            let b8 = o is > 3
            let b9 = o is 1..5
            let c1 = ou is .one(_)
            let c2 = p is { sum: 3 }
            let c3 = qq.s is { length: 3 }
            let c4 = u is .none
            let c5 = k is 1..=5
            let c6 = k in h()..=9
            let d1: i64 -> i64 = x => x + k
            let d2: () -> i64 = () => 9
            let d3 = try g()
            gv() catch { }
            try gv()
            hv()
            let ys: List<i64> = [1]
            let d6 = move ys
            let d7 = i32(k)
                return 0
        }

        void main() { }
        """;

    [Fact]
    public void Expr_LazySidesSpreadsPatternsMatchCSharp() => SameExprs(Lazy, "lazy");

    private const string Claims = """
        union V {
            a(List<i64> xs)
            b(i64 n, List<i64> ys)
            c
        }

        struct W {
            List<i64> ys
            i64 n
        }

        union error F {
            bad(i64 n)
        }

        void sw(move V v, from move W w, to ref V r) {
            switch v {
                case .a(let xs): print("a")
                case .b(1, let ys): print("b")
                case not .c: print("x")
                case .c: print("c")
                default: print("d")
            }
            switch w {
                case { n: 1, ys: let ys }: print("w")
                case { n: let n } and not { n: 3 }: print("n")
                default: print("d")
            }
            switch r {
                case .a(let xs): print("r")
                default: print("d")
            }
        }

        void main() { }
        """;

    [Fact]
    public void Claim_MatchesCSharp() => WithFixture(Claims, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("claim", "sw", "*", path), "claim", ClaimOracle(Compile(path), "sw")));

    /// `LowerExpr`'s two panics and `Call`'s: a spread outside a collection, a node that is already lowered, and a failing
    /// call outside `try`/`catch`; `Expr` is called on the node directly, as no statement reaches it.
    [Theory]
    [InlineData("spread", "lazy", "a6")]
    [InlineData("void", "lazy", "a1")]
    [InlineData("failing", "fails1", "a")]
    public void Expr_PanicsLikeCSharp(string mode, string fnName, string decl) => WithFixture(fnName == "lazy" ? Lazy : Decls, path =>
    {
        var c = Compile(path);
        var fn = Function(c, fnName);
        var init = Cases(fn).First(x => x.Name == decl).Expr;
        BoundExpr target = mode switch
        {
            "spread" => ((BoundCollection)init).Elements[0],
            "failing" => ((BoundTry)init).Operand,
            _ => new LVoid(init.Syntax),
        };
        var e = Assert.Throws<TargetInvocationException>(() => Call(New(c, fn), "Expr", target));
        var message = Assert.IsType<InvalidOperationException>(e.InnerException).Message;
        var (exit, _, err) = Run(mode, fnName, decl, path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {message}", err);
    });
}
