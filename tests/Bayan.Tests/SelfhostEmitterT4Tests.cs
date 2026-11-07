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
            let m = args[0]
            if m == "expr" || m == "spread" || m == "failing" || m == "void" || m == "show" || m == "showran" {
                Io.stdout.write(t4PrintExprs(ref c, through: ref l, for: args[1], at: args[2], by: args[0]))
            }
            if args[0] == "claim" { Io.stdout.write(t4PrintClaims(ref c, through: ref l, for: args[1])) }
            if args[0] == "become" { Io.stdout.write(t4PrintBecomes(ref c, through: ref l, for: args[1])) }
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
            l.showing = false
            l.shown = Map<BExpr, BExpr>()
            l.ran = Map<BExpr, LocalId>()
        }

        /// The `let` initializers and expression statements of `block`, and of each `if`'s then block, in order.
        void t4Collect(in Ctx c, for BStmt block, to ref List<string> names, as ref List<BExpr> exprs) uses alloc {
            for s in c.bBlocks[block & 0xFFFFFFFF].statements {
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
                if semantics.boundKind(s) == .ifStmt {
                    let t = c.bIfs[k].then
                    if !ltree.isLowered(t) && semantics.boundKind(t) == .block { t4Collect(in c, for: t, to: ref names, as: ref exprs) }
                }
            }
        }

        string t4PrintExprs(ref Ctx c, through ref LCtx l, for string name, at string only, by string mode) uses alloc {
            var sb = StrBuf()
            sb.append("== expr\n")
            let fn = t4Func(ref c, for: name)
            var names = List<string>()
            var exprs = List<BExpr>()
            t4Collect(in c, for: c.functions[fn].body, to: ref names, as: ref exprs)
            for d in 0..names.length {
                if only != "*" && names[d] != only { continue }
                t4Reset(ref l, for: fn)
                var target = exprs[d]
                if mode == "spread" { target = c.bCollections[target & 0xFFFFFFFF].elements[0] }
                if mode == "failing" { target = c.bTries[target & 0xFFFFFFFF].operand }
                if mode == "void" { target = ltree.addLVoid(ref l.tree, from: T4LVoid { span: semantics.nodeSpan(in c, for: target) }) }
                if mode == "show" || mode == "showran" {
                    l.showing = true
                    l.shown[target] = -1
                    if mode == "showran" { l.ran[target] = newTemp(ref c, through: ref l, for: semantics.prim(.bool)) }
                }
                let r = lowerExpr(ref c, through: ref l, for: target)
                sb.append($"{names[d]}:")
                var outs = List<BStmt>()
                for s in l.outs[0] { outs.append(s) }
                for s in outs { sb.append($" {t4Dump(ref c, through: in l, for: s)}") }
                sb.append($" => {t4Dump(ref c, through: in l, for: r)} locals={l.locals.length}\n")
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
                for s in outs { sb.append($" {t4Dump(ref c, through: in l, for: s)}") }
                sb.append($" locals={l.locals.length}\n")
            }
            return $"{sb.finish()}"
        }

        /// `Become` on every `become` statement of `name`'s body, in order.
        string t4PrintBecomes(ref Ctx c, through ref LCtx l, for string name) uses alloc {
            var sb = StrBuf()
            sb.append("== become\n")
            let fn = t4Func(ref c, for: name)
            var becomes = List<BStmt>()
            for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .becomeStmt { becomes.append(s) }
            }
            for b in becomes {
                t4Reset(ref l, for: fn)
                lowerBecome(ref c, through: ref l, for: b)
                sb.append("become:")
                var outs = List<BStmt>()
                for s in l.outs[0] { outs.append(s) }
                for s in outs { sb.append($" {t4Dump(ref c, through: in l, for: s)}") }
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

        string t4Value(in semantics.Constant k) uses alloc {
            switch k.kind {
                case .integer: return $"{k.integer.hi}:{k.integer.lo}"
                case .text: return $"\"{k.text}\""
                case .flag: return k.flag ? "true" : "false"
                default: return "?"
            }
        }

        string t4Dump(ref Ctx c, through in LCtx l, for BNode n) uses alloc {
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
                    case .loop: kids.append(l.tree.loops[i].body)
                    case .sequence:
                        for x in l.tree.sequences[i].effects { kids.append(x) }
                        kids.append(l.tree.sequences[i].value)
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
                if semantics.boundKind(n) == .binary { sb.append($" op{c.bBinaries[i].op.ordinal}") }
                if semantics.boundKind(n) == .unary { sb.append($" op{c.bUnaries[i].op.ordinal}") }
                if semantics.boundKind(n) == .builtinCall { sb.append($" m{c.bBuiltinCalls[i].member.ordinal}") }
                if semantics.boundKind(n) == .literal { sb.append($" {t4Value(in c.bLiterals[i].value)}") }
                for k in semantics.children(in c, for: n) { kids.append(k) }
            }
            let isExpr = ltree.isLowered(n) ? ltree.lKindOf(n).ordinal >= ltree.LKind.optionalHas.ordinal : semantics.isExprKind(semantics.boundKind(n))
            if isExpr {
                sb.append($":{semantics.typeDisplay(ref c, by: ltree.typeOf(in c, through: in l.tree, for: n))}")
                if (semantics.exprConstant(in c, for: n) is { } k) { sb.append($"={t4Value(in k)}") }
            }
            if kids.length > 0 {
                sb.append("(")
                for k in 0..kids.length {
                    if k > 0 { sb.append(" ") }
                    sb.append(t4Dump(ref c, through: in l, for: kids[k]))
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

    private static void SetField(object target, string name, object? value) =>
        FunctionLowerer.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

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

    private static string Value(ConstantValue v) => v.Value switch
    {
        Int128 n => $"{(ulong)(n >> 64)}:{(ulong)n}",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => "?",
    };

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
            case LSequence x: kids.AddRange(x.Effects); kids.Add(x.Value); break;
            case LLoop x: kids.Add(x.Body); break;
            case LNever or LVoid or LNextChar or LWrite or LPanic or LReturn or LSetContext or LFreeArena
                or LPushPanicHandler or LPopPanicHandler:
                break;
            default:
                if (n is BoundLocal bl) sb.Append($" {Local(bl.Local)}");
                if (n is BoundCall bc) sb.Append($" {bc.Function.Name}");
                if (n is BoundBinary bb) sb.Append($" op{(int)bb.Op}");
                if (n is BoundUnary bu) sb.Append($" op{(int)bu.Op}");
                if (n is BoundBuiltinCall bbc) sb.Append($" m{(int)bbc.Member}");
                if (n is BoundLiteral lit) sb.Append($" {Value(lit.Value)}");
                kids.AddRange(BoundTree.Children(n));
                break;
        }
        if (n is BoundExpr e)
        {
            sb.Append($":{e.Type.DisplayName}");
            if (e.Constant is { } k) sb.Append($"={Value(k)}");
        }
        if (kids.Count > 0) sb.Append('(').Append(string.Join(' ', kids.Select(k => Dump(k, locals)))).Append(')');
        return sb.ToString();
    }

    /// The `let` initializers and expression statements of <paramref name="fn"/>'s body, in order.
    private static IEnumerable<(string Name, BoundExpr Expr)> Cases(FunctionSymbol fn) => Cases(fn.Body!.Statements);

    /// ... and of each `if`'s then block.
    private static IEnumerable<(string Name, BoundExpr Expr)> Cases(IEnumerable<BoundStmt> statements)
    {
        foreach (var s in statements)
        {
            if (s is BoundLocalDecl { Initializer: { } init } d) yield return (d.Local.Name, init);
            if (s is BoundExprStmt x) yield return ("stmt", x.Expr);
            if (s is BoundIf { Then: BoundBlock b })
                foreach (var y in Cases(b.Statements)) yield return y;
        }
    }

    private static List<string> ExprOracle(Compilation c, string fnName, string only, string mode = "expr")
    {
        var fn = Function(c, fnName);
        var lines = new List<string>();
        foreach (var (name, e) in Cases(fn))
        {
            if (only != "*" && name != only) continue;
            var fl = New(c, fn);
            if (mode is "show" or "showran")
            {
                SetField(fl, "_shown", new Dictionary<BoundExpr, BoundExpr?>(ReferenceEqualityComparer.Instance) { [e] = null });
                var ran = new Dictionary<BoundExpr, LocalSymbol>(ReferenceEqualityComparer.Instance);
                if (mode == "showran") ran[e] = (LocalSymbol)Call(fl, "NewTemp", PrimitiveType.Bool)!;
                SetField(fl, "_ran", ran);
            }
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

    private static void SameExprs(string source, string fn, string only = "*", string mode = "expr") => WithFixture(source, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode(mode, fn, only, path), "expr", ExprOracle(Compile(path), fn, only, mode)));

    private static List<string> BecomeOracle(Compilation c, string fnName)
    {
        var fn = Function(c, fnName);
        var lines = new List<string>();
        foreach (var b in fn.Body!.Statements.OfType<BoundBecome>())
        {
            var fl = New(c, fn);
            Call(fl, "Become", b);
            var locals = (List<LocalSymbol>)Field(fl, "_locals")!;
            var outs = (List<BoundStmt>)Field(fl, "_out")!;
            lines.Add($"become:{string.Concat(outs.Select(o => " " + Dump(o, locals)))} locals={locals.Count}");
        }
        return lines;
    }

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

    /// Never-returning operands (`nev`), the `Operands` orderings (`ops`), the remaining kinds (`kinds`), `requires` checks,
    /// never-returning failing calls, narrowed union and error locals, and the T5 kinds reached through `Expr` (`fromT5`).
    private const string More = """
        union error E {
            x
            y(i64 n)
        }

        struct P {
            i64 a
            i64 b
        }

        struct R {
            i64 n requires it > 0
        }

        union U {
            one(i64 v)
            two
        }

        struct Order {
            i64 id
            states {
                draft
                paid(i64 payment, i64 fee)
            }
            void pay(ref this is .draft) becomes .paid {
                become .paid(1, panic("no"))
            }
            void pay2(ref this is .draft) becomes .paid {
                become .paid(h(), 2)
            }
            i64 feeOf(ref this is .paid) {
                let f = this.fee
                return f
            }
        }

        i64 g() fails E => 1

        never dieF() fails E {
            fail .x
        }

        i64 h() => 2

        i64? ho() => 3

        List<i64> mkList() => [1]

        i64[3] mkArr() => [1, 2, 3]

        i64 kinds(i64 k, at bool q, by List<i64> xs, to i64? o, for i64[3] arr, from Span<i64> sp, until (i64 x, i64 y) tu, as P? op, through P p) fails E {
            let a1: Map<string, i64> = []
            let a2: Map<string, i64> = ["a": 1, "b": h()]
            let a3: Span<i64> = []
            let a4: Span<i64> = [1, h()]
            let a5: Span<i64> = [..arr, 1]
            let a6 = [..arr, h()]
            let a7: List<i64> = [..sp]
            let a8: List<i64> = []
            let a9 = xs[..k]
            let b1 = xs[k..]
            let b2 = arr[1..2]
            let b3 = mkArr()[1..2]
            let b4 = mkList()[1..2]
            let b5 = arr[k]
            let b6 = mkArr()[k]
            let b7 = 3 in 1..h()
            let b8 = h() is 1
            let b9 = ho() ?? 3
            let c1 = o ?? ho()
            let c2 = Order.paid { id: 1, payment: 5, fee: h() }
            print("a")
            log("b")
            let c4 = R { n: k } catch R { n: 2 }
            let c7 = o is null
            let c8 = op is { a: 1 }
            let c9 = p is { a: 1 } pq
            let d1 = p is { }
            let d2 = tu is { x: 1 }
            let d4 = stackalloc i64[4]
            let d5 = xs[k..=k]
            if o is null { return 0 }
            let d6: i64 = o
            let z1 = E.x
            let z2 = 1 + 2
            let z3 = -(3)
            return 0
        }

        void reqNever() fails ValidationError<R> {
            let a = try R { n: 1 + panic("x") }
        }

        void reqNever2() {
            let a = R { n: 1 + panic("x") } catch R { n: 2 }
        }

        i64 reqTry(i64 k) fails ValidationError<R> {
            let a = try R { n: k }
            return a.n
        }

        void dieTry() fails E {
            let a: i64 = try dieF()
        }

        i64 errs(E err, at E? oe) {
            let a = err is .y(let n)
            let b = err is .y(1)
            let c = err is .x
            return 0
        }

        interface IShape {
            i64 area()
        }

        struct Circle : IShape {
            i64 r

            i64 area() => r
        }

        i64 lv(i64 k, to i64? o, by List<i64[3]> ws, from Circle ci) {
            let l2: dyn ref readonly IShape = ci
            let l3 = ws[k][o ?? h()]
            let l4 = o ?? return 0
            let l5 = g() catch { return 1 }
            let l6 = ws[k][1..2]
            return l2.area() + l3 + l4 + l5
        }

        i64 nev(i64 k, at bool q, by List<i64> xs, to i64? o, from List<i64?> os, for i64[3] arr) {
            if q { let a1 = E.y(panic("x")) }
            if q { let a2 = P { a: 1, b: panic("x") }.a }
            if q { let a3 = (x: 1, y: 1 + panic("x")).x }
            if q { let a4 = -(1 + panic("x")) }
            if q { let a5 = 1 + panic("x") }
            if q { let a6 = U.one(panic("x")) }
            if q { let a7 = P { a: 1, b: panic("x") } with { a: 2 } }
            if q { let a8 = P { a: 1, b: 2 } with { a: panic("x") } }
            if q { let a9 = 1..panic("x") }
            if q { let b1 = xs[1 + panic("x")] }
            if q { let b2 = (1 + panic("x")) is 1 }
            if q { let b3 = k in panic("x")..3 }
            if q { let b4 = panic("x") && q }
            if q { let b5 = q && panic("x") }
            if q { let b6 = panic("x") ? 1 : 2 }
            if q { let b7 = q ? panic("a") : panic("b") }
            if q { let b8 = os[1 + panic("x")] ?? 3 }
            if q { let b9: List<i64> = [1, panic("x")] }
            if q { let c1 = [1, panic("x")] }
            if q { let c2: Map<string, i64> = ["a": panic("x")] }
            if q { let c3: Span<i64> = [1, panic("x")] }
            if q { let c4 = xs[(1 + panic("x"))..] }
            if q { let c5 = Order.paid { id: 1, payment: 1 + panic("x"), fee: 2 }.fee }
            if q { let c6 = k in 1..(1 + panic("x")) }
            if q { let c7 = [..arr, 1 + panic("x")] }
            return 0
        }

        i64 ops(at bool q) fails E {
            let a = P { a: (g() catch 0), b: 1 }
            let b = P { a: h(), b: h() }
            let c = P { a: h(), b: (g() catch 0) }
            let n: i64 = 3
            let e = (a: move n, b: q ? n : (g() catch 0))
            let f = (a: move n, b: n)
            let g2 = (a: h(), b: h(), c: (g() catch 0))
            return 0
        }

        i64 useSpan(ReadOnlySpan<i64> s) => 1

        i64 two(i64 a, at i64 b) => a

        i64 gk(i64 a) fails E => a

        i64 fromT5(i64 k, at bool q, for i64[3] arr) fails E {
            let t1 = u8.tryFrom(k)
            let t2 = useSpan(arr)
            let t3 = useSpan(mkList())
            let t4 = $"a{k}"
            print($"p{k}")
            log($"l{k}")
            let t5 = g() catch switch {
                E.x => 1,
                E.y => 2
            }
            if q { let t6 = $"{1 + panic("x")}" }
            if q { panic($"n{k}") }
            if q { let t7 = two(panic("x"), at: 1) }
            if q { let t8 = try gk(panic("x")) }
            return 0
        }

        void main() { }

        i64 uni(U u, by E err) {
            let a = u is .one uw
            if u is .one uq {
                let v = uq.v
            }
            if err is .y ey {
                let n = ey.n
            }
            let b = err is .y(1)
            let c = err is .y(let m) && m > 0
            return 0
        }
        """;

    [Theory]
    [InlineData("nev")]
    [InlineData("ops")]
    [InlineData("kinds")]
    [InlineData("reqNever")]
    [InlineData("reqNever2")]
    [InlineData("reqTry")]
    [InlineData("dieTry")]
    [InlineData("uni")]
    [InlineData("feeOf")]
    [InlineData("errs")]
    [InlineData("lv")]
    [InlineData("fromT5")]
    public void Expr_MoreMatchesCSharp(string fn) => SameExprs(More, fn);

    /// `Expr` while an `assert` shows each declaration's initializer, with and without a `ran` flag.
    [Theory]
    [InlineData("show", "ops")]
    [InlineData("showran", "ops")]
    [InlineData("show", "nev")]
    [InlineData("showran", "nev")]
    public void Expr_ShownMatchesCSharp(string mode, string fn) => SameExprs(More, fn, mode: mode);

    /// `Operands` through `Become`: a payload operand that never returns, and one with an effect.
    [Theory]
    [InlineData("pay")]
    [InlineData("pay2")]
    public void Become_MatchesCSharp(string fn) => WithFixture(More, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("become", fn, "*", path), "become", BecomeOracle(Compile(path), fn)));

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
                case { n: 2 } wb: print("wb")
                default: print("d")
            }
            switch r {
                case .a(let xs): print("r")
                default: print("d")
            }
        }

        void sw2(move V v, from move W w, to ref V r, by F f) {
            switch move v {
                case .a ax: print("a")
                case .b(_, let ys): print("b")
                default: print("d")
            }
            switch move w {
                case { n: 1 } wd: print("w")
                default: print("d")
            }
            switch r {
                case .a ra: print("r")
                case .b(let n, _): print("n")
                default: print("d")
            }
            switch f {
                case .bad(let n): print("f")
            }
        }

        void main() { }
        """;

    [Theory]
    [InlineData("sw")]
    [InlineData("sw2")]
    public void Claim_MatchesCSharp(string fn) => WithFixture(Claims, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("claim", fn, "*", path), "claim", ClaimOracle(Compile(path), fn)));

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
