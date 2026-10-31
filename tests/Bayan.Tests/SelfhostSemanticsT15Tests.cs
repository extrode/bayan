using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using TextSpan = Bayan.Syntax.Text.TextSpan;

namespace Bayan.Tests;

/// <summary>
/// T15: <c>selfhost/semantics/binder.byn</c> against the C# <c>Binder</c> (Binder.cs 1–514). The same source goes to
/// <c>Compilation.Create</c>; the oracle then makes a fresh C# <c>Binder</c> (it is internal, so by reflection) over a
/// symbol of that compilation and calls the method under test, while the generated <c>main.byn</c> builds the same
/// symbols by hand in a <c>Ctx</c> and calls the Bayan port. Both print the bound tree, the locals and the new
/// diagnostics (<c>code line:col message |label|help</c>). Inputs avoid unported siblings (Q594): every parameter has a
/// constant default, and no case reaches <c>built</c>; <c>bindChecks</c> and <c>bindEnumArguments</c> are blocked (Q594) and have no case.
/// </summary>
public sealed class SelfhostSemanticsT15Tests
{
    private const string Source = """
        struct Point {
            i32 x = 4
            bool on = true
            bool bad = 3
            void m2(bool k = true) expects k {
            }
            i32 twice => 8
            i32 blocky {
            }
            bool wrong => 3
            Point(i32 a = 1, i32 b = zz) {
            }
        }

        struct Holder {
            i32 v
        }

        void empty() {
        }

        i32 one() => 1

        void flagged(bool flag = true) expects flag {
        }

        void two(bool a = true, bool b = false) expects a expects 5 {
        }

        void single(i32 n = 0) expects n {
        }

        void vd() => 1

        void defaults(i32 x = 3, i32 y = zz) {
        }

        void negs(rune r = 'r', f64 d = 1.5, string t = "t") {
        }

        void g(T a, List<i32> b, Zyzzyva c, U d) {
        }

        const K: i32 = 5
        const S: string = "s"
        const W: i32 = zz
        const M: i64

        const SA: i32[3] = zz
        const SB: i32[0] = zz
        const SC: i32[zz] = zz
        const SE: i32[99999999999999999999] = zz

        test "plain" {
        }

        @cases((1, true))
        test "cased" (i32 n, bool f) {
        }

        @cases((1, 2))
        test "dup" (i32 n, i32 n) {
        }

        @cases((true, 1))
        test "mism" (i32 n, bool f) {
        }

        @cases(5)
        test "lone" (bool f) {
        }

        {
            let q: bool = 3
        }

        {
        }

        """;

    /// <summary>Names on their own lines (in comments), for the scope and local cases.</summary>
    private const string Names = "// x\n// x\n// x\n// x\n// x\n// _\n// _\n// Bad\n// Bad\n// y\n// y\n// z\n";

    private static string Dir => Path.Combine(Golden.RepoRoot, "tmp", "t15-oracle");

    private static string PathOf(string name) => Path.Combine(Dir, name + ".byn");

    // ── The C# side ──────────────────────────────────────────────────────────────

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private sealed class Oracle
    {
        public Compilation C = null!;
        public FileContext File = null!;
        public int Mark;

        public static Oracle Of(string name, string text)
        {
            var path = PathOf(name);
            var c = Compilation.Create([SourceText.FromString(path, text)], packageRoot: Dir);
            var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
            return new Oracle { C = c, File = file, Mark = c.Diagnostics.Items.Count };
        }

        public ModuleSymbol Module => File.Module;

        public Decl[] Members => File.Tree.Root.Members;

        public object Binder(FunctionSymbol? fn, StructSymbol? container, IReadOnlyDictionary<string, TypeSymbol>? typeArgs = null) =>
            Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [C, File, fn, container, typeArgs, null], null)!;

        /// <summary>The diagnostics reported since the last call, and moves the mark past them.</summary>
        public IEnumerable<string> NewDiagnostics()
        {
            var items = C.Diagnostics.Items;
            var lines = new List<string>();
            for (var i = Mark; i < items.Count; i++) lines.Add(Diag(items[i]));
            Mark = items.Count;
            return lines;
        }
    }

    private static object? Call(object binder, string name, params object?[] args)
    {
        var m = BinderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        return m.Invoke(binder, args);
    }

    private static T Field<T>(object binder, string name) =>
        (T)BinderType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder)!;

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static readonly Dictionary<string, string> KindRenames = new()
    {
        ["default"] = "defaultValue", ["this"] = "thisRef", ["with"] = "withExpr", ["is"] = "isExpr", ["move"] = "moveExpr",
        ["try"] = "tryExpr", ["catch"] = "catchExpr", ["return"] = "returnStmt", ["fail"] = "failStmt", ["become"] = "becomeStmt",
        ["break"] = "breakStmt", ["continue"] = "continueStmt", ["if"] = "ifStmt", ["while"] = "whileStmt", ["for"] = "forStmt",
        ["switch"] = "switchStmt", ["defer"] = "deferStmt", ["assert"] = "assertStmt", ["errdefer"] = "errdeferStmt",
        ["context"] = "contextStmt",
    };

    private static string KindName(System.Type t)
    {
        var n = char.ToLowerInvariant(t.Name["Bound".Length]) + t.Name["Bound".Length..][1..];
        return KindRenames.GetValueOrDefault(n, n);
    }

    private static string Lower(bool b) => b ? "true" : "false";

    /// <summary>The bound tree, one node a line: kind, span, type and constant, the local or function named.</summary>
    private static void Dump(List<string> lines, BoundNode? n, int depth = 0)
    {
        if (n is null)
        {
            lines.Add(new string(' ', depth * 2) + "none");
            return;
        }
        var sb = new StringBuilder(new string(' ', depth * 2));
        sb.Append($"{KindName(n.GetType())} {n.Syntax.Span.Start}..{n.Syntax.Span.End}");
        if (n is BoundExpr e)
        {
            sb.Append($" : {e.Type.DisplayName}");
            if (e.Constant is { } k) sb.Append($" = {k}");
        }
        if (n is BoundLocal l) sb.Append($" {l.Local.Name}");
        if (n is BoundCall call) sb.Append($" {call.Function.Name}");
        lines.Add(sb.ToString());
        foreach (var ch in BoundTree.Children(n)) Dump(lines, ch, depth + 1);
    }

    private static void Locals(List<string> lines, FunctionSymbol fn, int from)
    {
        foreach (var l in fn.Locals.Skip(from)) lines.Add($"local {l.Name} {l.Id} {(int)l.Kind}");
        lines.Add($"requires {fn.Requires.Length} infers {Lower(fn.InfersErrors)}");
    }

    /// <summary>The Bayan expression for a type the cases use.</summary>
    private static string BayanType(TypeSymbol t) => t switch
    {
        PrimitiveType { DisplayName: "never" } => "prim(.neverKind)",
        PrimitiveType { DisplayName: "rune" } => "prim(.char)",
        PrimitiveType p => $"prim(.{p.DisplayName})",
        ErrorType => "errorType()",
        StructSymbol => "structTypeOf(s)",
        _ => throw new InvalidOperationException(t.DisplayName),
    };

    private static string Camel(string name) =>
        string.Concat(name.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Span(TextSpan s) => $"TextSpan {{ start: {s.Start}, length: {s.Length} }}";

    private static int IndexOf(Oracle o, Func<Decl, bool> pick) => Array.FindIndex(o.Members, d => pick(d));

    private static FunctionDecl Fn(Oracle o, string name) =>
        o.Members.OfType<FunctionDecl>().Single(f => f.Name.Text == name);

    // ── Hold and Release ─────────────────────────────────────────────────────────

    private const string HoldSource = "var a = 1\nvar b = 2\nvar c = 3\nvar d = 4\n";

    private static readonly string InstantiationLabel = "the generic code needs this of its type arguments";

    /// <summary>One first pass: reports held (decl index, labeled), pending `var`s (key, decl), constraints and peers.</summary>
    private sealed record HoldCase(
        string Name, (int Decl, bool Labeled)[] Reports, (int Key, int Decl)[] Pending,
        (int Decl, PrimitiveType Type)[] Constraints, (int Decl, int Peer)[] Peers);

    private static readonly HoldCase[] HoldCases =
    [
        new("wide", [(0, false), (1, true)], [(100, 0), (101, 1)], [(0, PrimitiveType.I64)], []),
        new("narrow", [(0, false), (1, true)], [(100, 0)], [(0, PrimitiveType.I32)], []),
        new("peer", [(2, true), (3, false)], [(100, 0), (102, 2)], [(1, PrimitiveType.U8), (2, PrimitiveType.String)], [(1, 0)]),
        new("float-member", [(0, false)], [(100, 0)], [(0, PrimitiveType.I64), (1, PrimitiveType.F64)], [(1, 0)]),
        new("first-wins", [], [(100, 0), (101, 1)], [(0, PrimitiveType.I16), (1, PrimitiveType.I64)], [(1, 0)]),
        new("chain", [(3, true)], [(103, 3)], [(1, PrimitiveType.U16)], [(3, 2), (2, 1)]),
        new("none", [(0, false)], [], [], []),
    ];

    private static List<string> ExpectedHold(HoldCase h)
    {
        var o = Oracle.Of("hold", HoldSource);
        var decls = o.Members.OfType<GlobalStatement>().Select(g => (LocalDecl)g.Statement).ToArray();
        var b = o.Binder(null, null);
        Call(b, "Hold");
        foreach (var (d, labeled) in h.Reports)
            Call(b, "Report", DiagnosticCodes.NotConstant, decls[d].Name.Span, new object?[] { decls[d].Name.Text }, labeled ? InstantiationLabel : null, null, null);
        var held = (DiagnosticBag)BinderType.GetField("_held", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(b)!;
        var lines = new List<string> { $"bag {held.Count} reported {o.NewDiagnostics().Count()}" };
        var pending = Field<Dictionary<LocalSymbol, LocalDecl>>(b, "_pending");
        foreach (var (key, d) in h.Pending) pending[new LocalSymbol($"l{key}", null, PrimitiveType.I32, LocalKind.Var)] = decls[d];
        var constraints = Field<Dictionary<LocalDecl, TypeSymbol>>(b, "_constraints");
        foreach (var (d, t) in h.Constraints) constraints[decls[d]] = t;
        var peers = Field<Dictionary<LocalDecl, LocalDecl>>(b, "_peers");
        foreach (var (d, p) in h.Peers) peers[decls[d]] = decls[p];
        var seeds = (IReadOnlyDictionary<LocalDecl, TypeSymbol>?)Call(b, "Release");
        if (seeds is null) lines.Add("seeds none");
        else foreach (var (d, t) in seeds) lines.Add($"seed {d.Span.Start} {t.DisplayName}");
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string HoldMain()
    {
        var o = Oracle.Of("hold", HoldSource);
        var decls = o.Members.OfType<GlobalStatement>().Select(g => (LocalDecl)g.Statement).ToArray();
        var sb = new StringBuilder();
        foreach (var h in HoldCases)
        {
            sb.AppendLine($"void hold{Camel(h.Name)}() {{");
            sb.AppendLine($"    var c = tcCtx({Literal(HoldSource)})");
            sb.AppendLine("    var b = tcBinder(-1, by: -1)");
            sb.AppendLine("    let bag = hold(ref b, through: ref c)");
            foreach (var (d, labeled) in h.Reports)
                sb.AppendLine($"    report(ref b, through: ref c, for: .notConstant, at: {Span(decls[d].Name.Span)}, by: (\"{decls[d].Name.Text}\", \"\", \"\", \"\", \"\"), label: \"{(labeled ? InstantiationLabel : "")}\")");
            sb.AppendLine($"    print(\"== hold {h.Name}\")");
            sb.AppendLine("    print($\"bag {c.bags[bag].items.length} reported {c.diagnostics.length}\")");
            foreach (var (key, d) in h.Pending) sb.AppendLine($"    b.pending[{key}] = {decls[d].Span.Start}");
            foreach (var (d, t) in h.Constraints) sb.AppendLine($"    b.constraints[{decls[d].Span.Start}] = {BayanType(t)}");
            foreach (var (d, p) in h.Peers) sb.AppendLine($"    b.peers[{decls[d].Span.Start}] = {decls[p].Span.Start}");
            sb.AppendLine("    let seeds = release(ref b, through: ref c)");
            sb.AppendLine("    if seeds.length == 0 { print(\"seeds none\") }");
            sb.AppendLine("    for (d, t) in seeds { print($\"seed {d} {typeDisplay(ref c, by: t)}\") }");
            sb.AppendLine("    tcDiags(in c, from: 0)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void holdAll() {");
        foreach (var h in HoldCases) sb.AppendLine($"    hold{Camel(h.Name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Scopes and locals ────────────────────────────────────────────────────────

    /// <summary>One step: push, pop, hold, release, count (the diagnostics reported so far), scope (the current scope, numbered
    /// by first appearance, so a reopened depth shows a new number), or declare name #i with a type and kind (in a function when `fn`).</summary>
    private sealed record Step(string Op, int Name = 0, PrimitiveType? Type = null, LocalKind Kind = LocalKind.Let);

    private static readonly (string Name, bool InFunction, Step[] Steps)[] ScopeCases =
    [
        ("same-scope", true, [
            new("decl", 0, PrimitiveType.I32), new("decl", 1, PrimitiveType.I32), new("decl", 2, PrimitiveType.String),
            new("decl", 3, PrimitiveType.I32, LocalKind.Var), new("push"), new("decl", 4, PrimitiveType.I32), new("pop"),
        ]),
        ("discard-and-names", false, [
            new("decl", 5, PrimitiveType.I32), new("decl", 6, PrimitiveType.I32), new("push"), new("decl", 7, PrimitiveType.I32),
            new("decl", 8, PrimitiveType.Bool, LocalKind.Temp), new("pop"), new("decl", 9, null), new("decl", 10, null),
        ]),
        ("held", true, [
            new("hold"), new("decl", 7, PrimitiveType.I32), new("decl", 8, PrimitiveType.I32), new("count"), new("release"),
            new("push"), new("push"), new("decl", 11, PrimitiveType.I64, LocalKind.Const), new("pop"), new("decl", 0, PrimitiveType.I64, LocalKind.Var),
            new("pop"), new("decl", 1, PrimitiveType.I64, LocalKind.Var),
        ]),
        ("outer-first", false, [new("decl", 0, PrimitiveType.I32), new("push"), new("decl", 1, PrimitiveType.I32), new("pop")]),
        ("identity", false, [
            new("scope"), new("push"), new("scope"), new("pop"), new("scope"), new("push"), new("scope"), new("push"), new("scope"),
            new("pop"), new("scope"), new("pop"), new("scope"),
        ]),
    ];

    private static TextSpan[] NameSpans()
    {
        var spans = new List<TextSpan>();
        var i = 0;
        foreach (var line in Names.Split('\n'))
        {
            if (line.Length > 0) spans.Add(new TextSpan(i + 3, line.Length - 3));
            i += line.Length + 1;
        }
        return spans.ToArray();
    }

    private static List<string> ExpectedScopes((string Name, bool InFunction, Step[] Steps) sc)
    {
        var o = Oracle.Of("names", Names);
        var spans = NameSpans();
        var fn = sc.InFunction ? new FunctionSymbol("f", null, o.Module, FunctionKind.Function) : null;
        var b = o.Binder(fn, null);
        var lines = new List<string>();
        var seen = new List<object>();
        foreach (var s in sc.Steps)
        {
            switch (s.Op)
            {
                case "scope":
                    var scope = Field<object>(b, "_scope");
                    var at = seen.FindIndex(x => ReferenceEquals(x, scope));
                    if (at < 0)
                    {
                        at = seen.Count;
                        seen.Add(scope);
                    }
                    lines.Add($"scope {at}");
                    break;
                case "push": Call(b, "PushScope"); break;
                case "pop": Call(b, "PopScope"); break;
                case "hold": Call(b, "Hold"); break;
                case "release": Call(b, "Release"); break;
                case "count": lines.Add($"reported {o.C.Diagnostics.Items.Count - o.Mark}"); break;
                default:
                    var span = spans[s.Name];
                    var local = (LocalSymbol)Call(b, "DeclareLocal", new Name(span, Names.Substring(span.Start, span.Length)), (TypeSymbol?)s.Type ?? ErrorType.Instance, s.Kind)!;
                    lines.Add($"declared {local.Name} {local.Id}");
                    break;
            }
        }
        if (fn is not null) Locals(lines, fn, 0);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string ScopesMain()
    {
        var spans = NameSpans();
        var sb = new StringBuilder();
        foreach (var (name, inFunction, steps) in ScopeCases)
        {
            sb.AppendLine($"void scopes{Camel(name)}() {{");
            sb.AppendLine($"    var c = tcCtx({Literal(Names)})");
            sb.AppendLine($"    let f = {(inFunction ? "newFunction(ref c, by: FunctionSymbol { name: \"f\", module: 0 })" : "-1")}");
            sb.AppendLine("    var b = tcBinder(f, by: -1)");
            sb.AppendLine($"    print(\"== scopes {name}\")");
            if (steps.Any(s => s.Op == "scope")) sb.AppendLine("    var seen = List<i64>()");
            foreach (var s in steps)
            {
                switch (s.Op)
                {
                    case "scope":
                        sb.AppendLine("    {");
                        sb.AppendLine("        let _ = t15Scope(ref b)");
                        sb.AppendLine("        let id = b.scopeIds[b.scopeIds.length - 1]");
                        sb.AppendLine("        var at = -1");
                        sb.AppendLine("        for j in 0..seen.length { if seen[j] == id { at = j } }");
                        sb.AppendLine("        if at < 0 {");
                        sb.AppendLine("            at = seen.length");
                        sb.AppendLine("            seen.append(id)");
                        sb.AppendLine("        }");
                        sb.AppendLine("        print($\"scope {at}\")");
                        sb.AppendLine("    }");
                        break;
                    case "push": sb.AppendLine("    pushScope(ref b)"); break;
                    case "pop": sb.AppendLine("    popScope(ref b)"); break;
                    case "hold": sb.AppendLine("    let _ = hold(ref b, through: ref c)"); break;
                    case "release": sb.AppendLine("    let _ = release(ref b, through: ref c)"); break;
                    case "count": sb.AppendLine("    print($\"reported {c.diagnostics.length}\")"); break;
                    default:
                        var kind = s.Kind switch
                        {
                            LocalKind.Let => "letKeyword", LocalKind.Var => "varKeyword", LocalKind.Const => "constKeyword",
                            LocalKind.Temp => "temp", _ => throw new InvalidOperationException(),
                        };
                        sb.AppendLine("    {");
                        sb.AppendLine($"        let l = declareLocal(ref b, through: ref c, at: {Span(spans[s.Name])}, as: {(s.Type is null ? "errorType()" : BayanType(s.Type))}, for: .{kind})");
                        sb.AppendLine("        print($\"declared {c.locals[l].name} {c.locals[l].id}\")");
                        sb.AppendLine("    }");
                        break;
                }
            }
            if (inFunction) sb.AppendLine("    tcLocals(ref c, for: f)");
            sb.AppendLine("    tcDiags(in c, from: 0)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void scopesAll() {");
        foreach (var (name, _, _) in ScopeCases) sb.AppendLine($"    scopes{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── ResolveType ──────────────────────────────────────────────────────────────

    /// <summary>Who supplies the type map: the binder's `typeArgs`, the function's, or the container's.</summary>
    private sealed record ResolveCase(string Name, PrimitiveType? Args, PrimitiveType? FnMap, string FnKey, PrimitiveType? StructMap, bool Held, bool Lambda = false);

    private static readonly ResolveCase[] ResolveCases =
    [
        new("type-args", PrimitiveType.String, PrimitiveType.Bool, "T", PrimitiveType.I64, false),
        new("function-map", null, PrimitiveType.Bool, "T", PrimitiveType.I64, false),
        new("struct-map", null, null, "T", PrimitiveType.I64, false),
        new("function-map-first", null, PrimitiveType.U8, "U", PrimitiveType.I64, false),
        new("no-map", null, null, "T", null, false),
        new("held", null, null, "T", null, true),
        new("lambda-keeps-map", null, PrimitiveType.Bool, "T", PrimitiveType.I64, false, Lambda: true),
    ];

    private static List<string> ExpectedResolve(ResolveCase r)
    {
        var o = Oracle.Of("main", Source);
        var holder = (StructSymbol)o.Module.Types["Holder"];
        holder.TypeMap = r.StructMap is null ? null : new Dictionary<string, TypeSymbol> { ["T"] = r.StructMap };
        FunctionSymbol? fn = null;
        if (r.FnMap is not null || r.Name == "struct-map")
            fn = new FunctionSymbol("f", null, o.Module, FunctionKind.Method)
            {
                TypeMap = r.FnMap is null ? null : new Dictionary<string, TypeSymbol> { [r.FnKey] = r.FnMap },
            };
        var b = o.Binder(fn, r.StructMap is null ? null : holder, r.Args is null ? null : new Dictionary<string, TypeSymbol> { ["T"] = r.Args });
        if (r.Held) Call(b, "Hold");
        var lines = new List<string>();
        var g = Fn(o, "g");
        for (var i = 0; i < g.Params.Length; i++)
            lines.Add($"resolve {i} {((TypeSymbol)Call(b, "ResolveType", g.Params[i].Type)!).DisplayName}");
        if (r.Lambda)
        {
            // A lambda body: C# moves `_fn` to the lifted lambda, which has no type map (BinderExpressions.cs:1911-1920).
            BinderType.GetField("_fn", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(b, new FunctionSymbol("$lambda0", null, o.Module, FunctionKind.Lambda));
            for (var i = 0; i < g.Params.Length; i++)
                lines.Add($"lambda {i} {((TypeSymbol)Call(b, "ResolveType", g.Params[i].Type)!).DisplayName}");
        }
        if (r.Held)
        {
            lines.Add($"held {((DiagnosticBag)BinderType.GetField("_held", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(b)!).Count}");
            Call(b, "Release");
        }
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string ResolveMain()
    {
        var o = Oracle.Of("main", Source);
        var holder = IndexOf(o, d => d is StructDecl { Name.Text: "Holder" });
        var g = IndexOf(o, d => d is FunctionDecl { Name.Text: "g" });
        var sb = new StringBuilder();
        foreach (var r in ResolveCases)
        {
            sb.AppendLine($"void resolve{Camel(r.Name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            sb.AppendLine($"    let s = {(r.StructMap is null ? "-1" : $"newStruct(ref c, by: StructSymbol {{ name: \"Holder\", module: 0, file: 0, declIndex: {holder}, typeMap: [\"T\": {BayanType(r.StructMap)}] }})")}");
            var fnMap = r.FnMap is null ? "[]" : $"[\"{r.FnKey}\": {BayanType(r.FnMap)}]";
            sb.AppendLine($"    let f = {(r.FnMap is not null || r.Name == "struct-map" ? $"newFunction(ref c, by: FunctionSymbol {{ name: \"f\", module: 0, kind: .method, typeMap: {fnMap} }})" : "-1")}");
            sb.AppendLine("    var b = tcBinder(f, by: s)");
            if (r.Args is not null) sb.AppendLine($"    b.typeArgs = [\"T\": {BayanType(r.Args)}]");
            if (r.Held) sb.AppendLine("    let bag = hold(ref b, through: ref c)");
            sb.AppendLine($"    print(\"== resolve {r.Name}\")");
            sb.AppendLine($"    switch c.files[0].unit.members[{g}] {{");
            sb.AppendLine("        case .functionDecl(let d):");
            sb.AppendLine("            for i in 0..d.value.params.length {");
            sb.AppendLine("                let t = resolveType(ref b, through: ref c, for: in d.value.params[i].type)");
            sb.AppendLine("                print($\"resolve {i} {typeDisplay(ref c, by: t)}\")");
            sb.AppendLine("            }");
            if (r.Lambda)
            {
                sb.AppendLine("            b.fn = newFunction(ref c, by: FunctionSymbol { name: \"$lambda0\", module: 0, kind: .lambda })");
                sb.AppendLine("            for i in 0..d.value.params.length {");
                sb.AppendLine("                let t = resolveType(ref b, through: ref c, for: in d.value.params[i].type)");
                sb.AppendLine("                print($\"lambda {i} {typeDisplay(ref c, by: t)}\")");
                sb.AppendLine("            }");
            }
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
            if (r.Held)
            {
                sb.AppendLine("    print($\"held {c.bags[bag].items.length}\")");
                sb.AppendLine("    let _ = release(ref b, through: ref c)");
            }
            sb.AppendLine("    tcDiags(in c, from: 0)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void resolveAll() {");
        foreach (var r in ResolveCases) sb.AppendLine($"    resolve{Camel(r.Name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Bodies (BindFunctionBody, BindExpressionBody, BindGetter, BindConstructor, BindMain, BindTest) ──

    /// <summary>A function with neither a block nor an expression body: the parser reports it, and the binder gives it an empty block.</summary>
    private const string NoBodySource = "void nobody()\n\nvoid other() {\n}\n";

    private static List<string> ExpectedNoBody()
    {
        var o = Oracle.Of("nobody", NoBodySource);
        var decl = o.Members.OfType<FunctionDecl>().First();
        var fn = new FunctionSymbol("nobody", null, o.Module, FunctionKind.Function);
        Call(o.Binder(fn, null), "BindFunctionBody", decl);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, 0);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string NoBodyMain() => $$"""
        void bodyNobody() {
            var c = tcCtx({{Literal(NoBodySource)}})
            let f = newFunction(ref c, by: FunctionSymbol { name: "nobody", module: 0, file: 0, declIndex: 0 })
            var b = tcBinder(f, by: -1)
            print("== body nobody")
            switch c.files[0].unit.members[0] {
                case .functionDecl(let d):
                    bindFunctionBody(ref b, through: ref c, for: in d.value)
                default:
                    break
            }
            tcBody(ref c, for: f)
        }

        """;

    private static readonly string[] BodyFunctions = ["empty", "one", "flagged", "two", "single", "vd"];

    private static List<string> ExpectedFunctionBody(string name)
    {
        var o = Oracle.Of("main", Source);
        var fn = o.Module.Functions[name][0];
        var before = fn.Locals.Count;
        Call(o.Binder(fn, null), "BindFunctionBody", fn.Decl);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, before);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    /// <summary>Bayan statements that build function `fn` of the oracle's compilation by hand, into `f`.</summary>
    private static void BuildFunction(StringBuilder sb, Oracle o, FunctionSymbol fn, string kind, int decl, int member, string owner)
    {
        sb.AppendLine($"    let f = newFunction(ref c, by: FunctionSymbol {{ name: \"{fn.Name}\", module: 0, kind: .{kind}, file: 0, declIndex: {decl}, memberIndex: {member}, owner: {owner}, returnType: {BayanType(fn.ReturnType)} }})");
        foreach (var p in fn.Parameters)
            sb.AppendLine($"    tcParam(ref c, for: f, by: \"{p.Name}\", at: {Span(p.Location!.Value.Span)}, as: {BayanType(p.Type)})");
    }

    private static string BodiesMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        foreach (var name in BodyFunctions)
        {
            var fn = o.Module.Functions[name][0];
            var decl = IndexOf(o, d => d is FunctionDecl f && f.Name.Text == name);
            sb.AppendLine($"void body{Camel(name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            BuildFunction(sb, o, fn, "function", decl, -1, "-1");
            sb.AppendLine("    var b = tcBinder(f, by: -1)");
            sb.AppendLine($"    print(\"== body {name}\")");
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .functionDecl(let d):");
            sb.AppendLine("            bindFunctionBody(ref b, through: ref c, for: in d.value)");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
            sb.AppendLine("    tcBody(ref c, for: f)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void bodiesAll() {");
        foreach (var name in BodyFunctions) sb.AppendLine($"    body{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly string[] PointMembers = ["twice", "blocky", "wrong", "Point", "m2"];

    private static FunctionSymbol MemberFunction(StructSymbol point, string name) =>
        name == "Point" ? point.Constructors[0] : point.FindProperty(name)?.Getter ?? point.Methods.Single(m => m.Name == name);

    private static List<string> ExpectedMember(string name)
    {
        var o = Oracle.Of("main", Source);
        var point = (StructSymbol)o.Module.Types["Point"];
        var fn = MemberFunction(point, name);
        var before = fn.Locals.Count;
        // Compilation.Create already bound the defaults; the Bayan side binds them on first use, so clear the cache.
        foreach (var p in fn.Parameters) p.Default = null;
        if (name == "Point") Call(o.Binder(fn, point), "BindConstructor", point.Decl.Members.OfType<ConstructorDecl>().Single());
        else if (fn.Kind == FunctionKind.Method) Call(o.Binder(fn, point), "BindFunctionBody", fn.Decl);
        else Call(o.Binder(fn, point), "BindGetter", point.FindProperty(name)!.Decl);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, before);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string MembersMain()
    {
        var o = Oracle.Of("main", Source);
        var point = (StructSymbol)o.Module.Types["Point"];
        var decl = IndexOf(o, d => d is StructDecl { Name.Text: "Point" });
        var sb = new StringBuilder();
        foreach (var name in PointMembers)
        {
            var fn = MemberFunction(point, name);
            var member = Array.FindIndex(point.Decl.Members, m => m is PropertyDecl p && p.Name.Text == name || m is FunctionDecl fd && fd.Name.Text == name || m is ConstructorDecl && name == "Point");
            sb.AppendLine($"void member{Camel(name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            sb.AppendLine($"    let s = newStruct(ref c, by: StructSymbol {{ name: \"Point\", module: 0, file: 0, declIndex: {decl} }})");
            foreach (var field in point.Fields)
            {
                var x = Array.FindIndex(point.Decl.Members, m => m is FieldDecl fd && fd.Name.Text == field.Name);
                sb.AppendLine($"    c.structs[s].fields.append(newField(ref c, by: FieldSymbol {{ name: \"{field.Name}\", owner: s, file: 0, declIndex: {decl}, memberIndex: {x}, type: {BayanType(field.Type)}, hasDefault: true }}))");
            }
            BuildFunction(sb, o, fn, fn.Kind switch { FunctionKind.Constructor => "constructor", FunctionKind.Method => "method", _ => "getter" }, decl, member, "s");
            sb.AppendLine("    var b = tcBinder(f, by: s)");
            sb.AppendLine("    b.thisType = structTypeOf(s)");
            sb.AppendLine($"    print(\"== member {name}\")");
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .structDecl(let sd):");
            sb.AppendLine($"            switch sd.value.members[{member}] {{");
            sb.AppendLine("                case .propertyDecl(let d):");
            sb.AppendLine("                    bindGetter(ref b, through: ref c, for: in d.value)");
            sb.AppendLine("                case .constructorDecl(let d):");
            sb.AppendLine("                    bindConstructor(ref b, through: ref c, for: in d.value)");
            sb.AppendLine("                case .functionDecl(let d):");
            sb.AppendLine("                    bindFunctionBody(ref b, through: ref c, for: in d.value)");
            sb.AppendLine("                default:");
            sb.AppendLine("                    break");
            sb.AppendLine("            }");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
            sb.AppendLine("    tcBody(ref c, for: f)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void membersAll() {");
        foreach (var name in PointMembers) sb.AppendLine($"    member{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The indexes of the source's top-level blocks, the statements `main` binds.</summary>
    private static int[] MainIndexes(Oracle o) =>
        Enumerable.Range(0, o.Members.Length).Where(i => o.Members[i] is GlobalStatement { Statement: BlockStmt }).ToArray();

    private static List<string> ExpectedMain()
    {
        var o = Oracle.Of("main", Source);
        var fn = new FunctionSymbol("main", null, o.Module, FunctionKind.Main);
        var statements = MainIndexes(o).Select(i => (GlobalStatement)o.Members[i]).ToList();
        Call(o.Binder(fn, null), "BindMain", statements);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, 0);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string MainMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        sb.AppendLine("void mainBody() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    let f = newFunction(ref c, by: FunctionSymbol { name: \"main\", module: 0, kind: .main })");
        sb.AppendLine("    var b = tcBinder(f, by: -1)");
        sb.AppendLine("    var statements = List<SyntaxRef>()");
        foreach (var i in MainIndexes(o)) sb.AppendLine($"    statements.append(SyntaxRef {{ file: 0, declIndex: {i} }})");
        sb.AppendLine("    print(\"== main\")");
        sb.AppendLine("    bindMain(ref b, through: ref c, for: in statements)");
        sb.AppendLine("    tcBody(ref c, for: f)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly string[] Tests = ["plain", "cased", "dup", "mism", "lone"];

    private static int CasesAttribute(TestDecl t) => Array.FindIndex(t.Attributes, a => a.Name.Text == "cases");

    /// <summary>The first case's values as Compilation.cs:334 makes them: a tuple's element values, or the value alone; null without `@cases`.</summary>
    private static Expr[]? CaseValues(TestDecl t) =>
        CasesAttribute(t) is var a && a < 0 ? null
            : t.Attributes[a].Args[0].Value is TupleExpr tuple ? tuple.Elements.Select(e => e.Value).ToArray() : [t.Attributes[a].Args[0].Value];

    private static List<string> ExpectedTest(string title)
    {
        var o = Oracle.Of("main", Source);
        var decl = o.Members.OfType<TestDecl>().Single(t => t.Title == title);
        var fn = new FunctionSymbol("test", null, o.Module, FunctionKind.Test);
        Call(o.Binder(fn, null), "BindTest", decl, CaseValues(decl));
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, 0);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string TestsMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        foreach (var title in Tests)
        {
            var decl = IndexOf(o, d => d is TestDecl t && t.Title == title);
            sb.AppendLine($"void case{Camel(title)}() {{");
            sb.AppendLine("    var c = tcSource()");
            sb.AppendLine("    let f = newFunction(ref c, by: FunctionSymbol { name: \"test\", module: 0, kind: .testKeyword })");
            sb.AppendLine("    var b = tcBinder(f, by: -1)");
            var attribute = CasesAttribute((TestDecl)o.Members[decl]);
            var values = attribute < 0 ? "SyntaxRef {}" : $"SyntaxRef {{ file: 0, declIndex: {decl}, memberIndex: {attribute}, itemIndex: 0 }}";
            sb.AppendLine($"    print(\"== test {title}\")");
            sb.AppendLine($"    bindTest(ref b, through: ref c, for: TestBody {{ decl: SyntaxRef {{ file: 0, declIndex: {decl} }}, hasValues: {Lower(attribute >= 0)}, values: {values} }})");
            sb.AppendLine("    tcBody(ref c, for: f)");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    // ── Defaults, constants, array sizes, enum arguments ─────────────────────────

    /// <summary>The functions whose parameter defaults are bound: two top-level ones, then Point's constructor and method.</summary>
    private static readonly string[] DefaultFunctions = ["defaults", "negs", "Point", "m2"];

    private static FunctionSymbol DefaultFunction(Oracle o, string name)
    {
        var point = (StructSymbol)o.Module.Types["Point"];
        return name switch
        {
            "Point" => point.Constructors[0],
            "m2" => point.Methods.Single(m => m.Name == "m2"),
            _ => o.Module.Functions[name][0],
        };
    }

    private static List<string> ExpectedDefaults()
    {
        var o = Oracle.Of("main", Source);
        var lines = new List<string>();
        foreach (var name in DefaultFunctions)
        {
            var fn = DefaultFunction(o, name);
            foreach (var p in fn.Parameters)
            {
                lines.Add($"parameter {name} {p.Name}");
                Dump(lines, (BoundExpr)Call(o.Binder(null, fn.Owner, fn.TypeMap), "BindParameterDefault", p)!);
                lines.AddRange(o.NewDiagnostics());
            }
        }
        var point = (StructSymbol)o.Module.Types["Point"];
        foreach (var field in point.Fields)
        {
            lines.Add($"field {field.Name}");
            Dump(lines, (BoundExpr)Call(o.Binder(null, point), "BindFieldDefault", field)!);
            lines.AddRange(o.NewDiagnostics());
        }
        return lines;
    }

    private static string DefaultsMain()
    {
        var o = Oracle.Of("main", Source);
        var point = IndexOf(o, d => d is StructDecl { Name.Text: "Point" });
        var pointDecl = (StructDecl)o.Members[point];
        var sb = new StringBuilder();
        sb.AppendLine("void defaultsAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    print(\"== defaults\")");
        sb.AppendLine("    var mark = 0");
        sb.AppendLine($"    let s = newStruct(ref c, by: StructSymbol {{ name: \"Point\", module: 0, file: 0, declIndex: {point} }})");
        foreach (var name in DefaultFunctions)
        {
            var fn = DefaultFunction(o, name);
            sb.AppendLine("    {");
            if (fn.Owner is null)
                BuildFunction(sb, o, fn, "function", IndexOf(o, d => d is FunctionDecl f && f.Name.Text == name), -1, "-1");
            else
                BuildFunction(sb, o, fn, name == "Point" ? "constructor" : "method", point,
                    Array.FindIndex(pointDecl.Members, m => name == "Point" ? m is ConstructorDecl : m is FunctionDecl f && f.Name.Text == name), "s");
            sb.AppendLine("    var ps = List<LocalId>()");
            sb.AppendLine("    for p in c.functions[f].parameters { ps.append(p) }");
            sb.AppendLine("    for p in ps {");
            sb.AppendLine($"        var b = tcBinder(-1, by: {(fn.Owner is null ? "-1" : "s")})");
            sb.AppendLine($"        print($\"parameter {name} {{c.locals[p].name}}\")");
            sb.AppendLine("        tcDump(ref c, for: bindParameterDefault(ref b, through: ref c, for: p), at: 0)");
            sb.AppendLine("        tcDiags(in c, from: mark)");
            sb.AppendLine("        mark = c.diagnostics.length");
            sb.AppendLine("    }");
            sb.AppendLine("    }");
        }
        foreach (var field in ((StructSymbol)o.Module.Types["Point"]).Fields)
        {
            var x = Array.FindIndex(pointDecl.Members, m => m is FieldDecl fd && fd.Name.Text == field.Name);
            sb.AppendLine("    {");
            sb.AppendLine($"    let x = newField(ref c, by: FieldSymbol {{ name: \"{field.Name}\", owner: s, file: 0, declIndex: {point}, memberIndex: {x}, type: {BayanType(field.Type)}, hasDefault: true }})");
            sb.AppendLine("    var b = tcBinder(-1, by: s)");
            sb.AppendLine($"    print(\"field {field.Name}\")");
            sb.AppendLine("    tcDump(ref c, for: bindFieldDefault(ref b, through: ref c, for: x), at: 0)");
            sb.AppendLine("    tcDiags(in c, from: mark)");
            sb.AppendLine("    mark = c.diagnostics.length");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly string[] Constants = ["K", "S", "W", "M", "SA"];

    private static List<string> ExpectedConstant(string name)
    {
        var o = Oracle.Of("main", Source);
        var k = o.Module.Constants[name];
        Call(o.Binder(null, null), "BindModuleConstant", k);
        var lines = new List<string>
        {
            $"const {k.Name} {k.Type.DisplayName} value {k.Value?.ToString() ?? "-"} static {Lower(k.IsStaticArray)} elements {k.Elements.Length}",
        };
        Dump(lines, k.Bound);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string ConstantsMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        foreach (var name in Constants)
        {
            var decl = IndexOf(o, d => d is GlobalStatement { Statement: LocalDecl l } && l.Name.Text == name);
            sb.AppendLine($"void const{Camel(name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            sb.AppendLine($"    let k = newConst(ref c, by: ConstSymbol {{ name: \"{name}\", module: 0, file: 0, declIndex: {decl} }})");
            sb.AppendLine("    var b = tcBinder(-1, by: -1)");
            sb.AppendLine($"    print(\"== const {name}\")");
            sb.AppendLine("    bindModuleConstant(ref b, through: ref c, for: k)");
            sb.AppendLine("    var value = \"-\"");
            sb.AppendLine("    if (c.consts[k].value is { } v) { value = constantText(in v) }");
            sb.AppendLine("    print($\"const {c.consts[k].name} {typeDisplay(ref c, by: c.consts[k].type)} value {value} static {c.consts[k].isStaticArray} elements {c.consts[k].elements.length}\")");
            sb.AppendLine("    tcDump(ref c, for: c.consts[k].bound, at: 0)");
            sb.AppendLine("    tcDiags(in c, from: 0)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void constantsAll() {");
        foreach (var name in Constants) sb.AppendLine($"    const{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly string[] SizeConstants = ["SA", "SB", "SC", "SE"];

    private static List<string> ExpectedSizes()
    {
        var o = Oracle.Of("main", Source);
        var lines = new List<string>();
        foreach (var name in SizeConstants)
        {
            var type = (Bayan.Syntax.Ast.ArrayType)o.Module.Constants[name].Decl.Type!;
            lines.Add($"size {name} {Call(o.Binder(null, null), "BindArraySize", type.Size)}");
            lines.AddRange(o.NewDiagnostics());
        }
        return lines;
    }

    private static string SizesMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        sb.AppendLine("void sizesAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    print(\"== sizes\")");
        sb.AppendLine("    var mark = 0");
        foreach (var name in SizeConstants)
        {
            var decl = IndexOf(o, d => d is GlobalStatement { Statement: LocalDecl l } && l.Name.Text == name);
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .globalStatement(let g):");
            sb.AppendLine("            switch g.value.statement {");
            sb.AppendLine("                case .localDecl(let d):");
            sb.AppendLine("                    switch d.value.type {");
            sb.AppendLine("                        case .arrayType(let a):");
            sb.AppendLine("                            var b = tcBinder(-1, by: -1)");
            sb.AppendLine($"                            print($\"size {name} {{bindArraySize(ref b, through: ref c, for: in a.value.size)}}\")");
            sb.AppendLine("                            tcDiags(in c, from: mark)");
            sb.AppendLine("                            mark = c.diagnostics.length");
            sb.AppendLine("                        default:");
            sb.AppendLine("                            break");
            sb.AppendLine("                    }");
            sb.AppendLine("                default:");
            sb.AppendLine("                    break");
            sb.AppendLine("            }");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── The generated main ───────────────────────────────────────────────────────

    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '\n') sb.Append("\\n");
            else
            {
                if (ch is '\\' or '"') sb.Append('\\');
                sb.Append(ch);
            }
        }
        return sb.Append('"').ToString();
    }

    private static string MainSource() => Prelude.Replace("$SOURCE", Literal(Source)) + HoldMain() + ScopesMain() + ResolveMain() + BodiesMain()
        + MembersMain() + MainMain() + TestsMain() + DefaultsMain() + ConstantsMain() + SizesMain() + NoBodyMain() + """
        i32 main() {
            holdAll()
            scopesAll()
            resolveAll()
            bodiesAll()
            membersAll()
            mainBody()
            casePlain()
            caseCased()
            caseDup()
            caseMism()
            caseLone()
            defaultsAll()
            constantsAll()
            sizesAll()
            bodyNobody()
            return 0
        }

        """;

    private const string Prelude = """
        import lexer
        import parser

        type GlobalStatement = parser.GlobalStatement
        type BlockStmt = parser.BlockStmt

        Ctx tcCtx(string text) {
            var bytes = List<u8>()
            for x in text.bytes { bytes.append(x) }
            var copy = List<u8>()
            for x in text.bytes { copy.append(x) }
            let source = lexer.SourceText.create("t15.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
            c.files.append(SourceFile { path: "t15.byn", displayPath: "t15.byn", text: move copy, unit: move unit, module: 0, isEntry: true, imports: Map<string, ModId>() })
            var files = List<FileId>()
            files.append(0)
            c.modules.append(Mod { name: "t15", files: move files, directory: ".", cName: "t15" })
            return c
        }

        Ctx tcSource() => tcCtx($SOURCE)

        Binder tcBinder(FuncId fn, by StructId owner) => Binder {
            file: 0,
            fn: fn,
            owner: owner,
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

        void tcParam(ref Ctx c, for FuncId f, by string name, at TextSpan span, as Type type) {
            let ordinal = c.functions[f].parameters.length
            let p = newLocal(ref c, by: LocalSymbol { name: name, nameSpan: span, span: span, type: type, kind: .parameter, isParameter: true, ordinal: ordinal })
            c.functions[f].parameters.append(p)
        }

        void tcDiags(in Ctx c, from i64 mark) {
            for i in mark..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                var fix = ""
                if c.diagnostics[i].hasFix { fix = $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" }
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fix}")
            }
        }

        void tcDump(ref Ctx c, for BNode n, at i64 depth) {
            var sb = StrBuf()
            for i in 0..depth { sb.append("  ") }
            if n == -1 {
                sb.append("none")
                print($"{sb.finish()}")
                return
            }
            let k = boundKind(n)
            let span = nodeSpan(in c, for: n)
            sb.append($"{k} {span.start}..{span.end}")
            if isExprKind(k) {
                sb.append($" : {typeDisplay(ref c, by: exprType(in c, for: n))}")
                if (exprConstant(in c, for: n) is { } v) { sb.append($" = {constantText(in v)}") }
            }
            if k == .local { sb.append($" {c.locals[c.bLocals[boundIndex(n)].local].name}") }
            if k == .call { sb.append($" {c.functions[c.bCalls[boundIndex(n)].function].name}") }
            print($"{sb.finish()}")
            for ch in children(in c, for: n) { tcDump(ref c, for: ch, at: depth + 1) }
        }

        void tcLocals(ref Ctx c, for FuncId f) {
            for l in c.functions[f].locals { print($"local {c.locals[l].name} {c.locals[l].id} {c.locals[l].kind.ordinal}") }
            print($"requires {c.functions[f].requirements.length} infers {c.functions[f].infersErrors}")
        }

        void tcBody(ref Ctx c, for FuncId f) {
            tcDump(ref c, for: c.functions[f].body, at: 0)
            tcLocals(ref c, for: f)
            tcDiags(in c, from: 0)
        }

        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    public static TheoryData<string> HoldNames() => new(HoldCases.Select(h => h.Name));

    public static TheoryData<string> ScopeNames() => new(ScopeCases.Select(s => s.Name));

    public static TheoryData<string> ResolveNames() => new(ResolveCases.Select(r => r.Name));

    public static TheoryData<string> BodyNames() => new(BodyFunctions);

    public static TheoryData<string> MemberNames() => new(PointMembers);

    public static TheoryData<string> TestNames() => new(Tests);

    public static TheoryData<string> ConstantNames() => new(Constants);

    [Theory]
    [MemberData(nameof(HoldNames))]
    public void HoldAndReleaseMatch(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"hold {name}", ExpectedHold(HoldCases.Single(h => h.Name == name)));

    [Theory]
    [MemberData(nameof(ScopeNames))]
    public void ScopesAndLocalsMatch(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"scopes {name}", ExpectedScopes(ScopeCases.Single(s => s.Name == name)));

    [Theory]
    [MemberData(nameof(ResolveNames))]
    public void ResolveTypeMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"resolve {name}", ExpectedResolve(ResolveCases.Single(r => r.Name == name)));

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void FunctionBodyMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"body {name}", ExpectedFunctionBody(name));

    [Theory]
    [MemberData(nameof(MemberNames))]
    public void GetterAndConstructorMatch(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"member {name}", ExpectedMember(name));

    [Fact]
    public void MissingBodyMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "body nobody", ExpectedNoBody());

    [Fact]
    public void MainMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "main", ExpectedMain());

    [Theory]
    [MemberData(nameof(TestNames))]
    public void TestBodyMatches(string title) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"test {title}", ExpectedTest(title));

    [Fact]
    public void DefaultsMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "defaults", ExpectedDefaults());

    [Theory]
    [MemberData(nameof(ConstantNames))]
    public void ModuleConstantMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"const {name}", ExpectedConstant(name));

    [Fact]
    public void ArraySizesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "sizes", ExpectedSizes());
}
