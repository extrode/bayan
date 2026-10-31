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
/// T16: <c>selfhost/semantics/binder2.byn</c> against the C# <c>Binder</c> (Binder.cs 516–980). The same source goes to
/// <c>Compilation.Create</c>; the oracle then makes a fresh C# <c>Binder</c> (internal, so by reflection) and calls the
/// method under test, while the generated <c>main.byn</c> builds the same symbols by hand in a <c>Ctx</c> and calls the
/// Bayan port. Both print the bound tree, the new locals and the new diagnostics (<c>code line:col message |label|help|fix</c>).
/// Inputs avoid unported siblings: the branches that only reach them are listed in Q599 ("Not compared").
/// </summary>
public sealed class SelfhostSemanticsT16Tests
{
    private const string Source = """
        struct unique U {
            i32 v
        }

        struct H {
            U u
        }

        void decls(i32 p) {
            let a: i32 = 1
            var b: i32 = 2
            const k: i32 = 5
            const kp: i32 = p
            let u: i32
            const v: i32
            var w
            var x: i32
            using let n: i32 = 5
            using var l: List<i32>
            using var ar: Arena
            const ke: i32 = zz
        }

        void conds(bool p) {
            if true {
            }
            if p {
                let a: i32 = 1
            } else {
            }
            if p {
            } else if false {
            }
            if never p {
            }
            unless false {
            }
            unless p {
            }
            if 3 {
            }
        }

        void loops(bool p) {
            while p {
            }
            do {
            } while p
            outer: while p {
            }
            inner: do {
            } while false
            lab: {
            }
            top: while true {
                top: while p {
                }
            }
            while let x = p {
            }
            tag: while let y = p {
            }
            while let z = zz {
            }
        }

        void misc(bool p) {
            defer {
            }
            assert true
            assert p
            assert 3
            assert zz
            assert 3 fails E
            assert zz fails E
            assert (p) fails E.bad
        }

        void seeded() {
            var s
            var t
            var w: i32
        }

        void uq() {
            const u: U
            let w: U
            const h: H
            const l: List<i32>
            const n: i32
        }

        @exitCode(3)
        void x1() {
        }

        @exitCode(0)
        void x2() {
        }

        @exitCode(300)
        void x3() {
        }

        @exitCode(1, 2)
        void x4() {
        }

        @exitCode(code: 1)
        void x5() {
        }

        @exitCode(true)
        void x6() {
        }

        @exitCode(zz)
        void x7() {
        }

        @exitCode(255)
        void x8() {
        }

        @exitCode("x")
        void x9() {
        }

        @exitCode(256)
        void x10() {
        }

        @exitCode(1)
        void x11() {
        }

        void fors(List<i32> xs, string s) {
            for x in xs {
            }
            for t in s {
            }
            for e in zz {
            }
            lab: for z in xs {
            }
            for xs in xs {
            }
            for n in 3 {
            }
        }

        """;

    /// <summary>
    /// Inputs the C# parser reports (BYN1043, BYN1040, BYN1001, BYN1012), so <c>Compilation.Create</c> stops before declaring
    /// (Compilation.cs:107); the oracle binds them with a fresh C# <c>Binder</c> and no function.
    /// </summary>
    private const string BrokenSource = """
        void misc() {
            ;
            let p: bool = true
            i32 t = 3
            else
            p
            p if p
            p unless p
            p if 3
        }

        void dtsNull() {
            null
        }

        void dtsInt() {
            1
        }

        void dtsBool() {
            true
        }

        void forVar() {
            for (var y in "ab") {
            }
        }

        """;

    private static string Dir => Path.Combine(Golden.RepoRoot, "tmp", "t16-oracle");

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

        public object Binder(FunctionSymbol? fn, IReadOnlyDictionary<LocalDecl, TypeSymbol>? seeds = null) =>
            Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [C, File, fn, null, null, seeds], null)!;

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

    private static string Target(JumpTarget t) => $" target {t.Label ?? "-"} {Lower(t.IsLoop)}";

    /// <summary>The bound tree, one node a line: kind, span, type and constant, the local named, a loop's target, `if never`.</summary>
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
        if (n is BoundWhile w) sb.Append(Target(w.Target));
        if (n is BoundDoWhile dw) sb.Append(Target(dw.Target));
        if (n is BoundIf { Never: true }) sb.Append(" never");
        lines.Add(sb.ToString());
        foreach (var ch in BoundTree.Children(n)) Dump(lines, ch, depth + 1);
    }

    /// <summary>
    /// The locals from <paramref name="from"/> on: name, id, kind, type, `using`, the keyword span and the constant. A pattern
    /// variable's type comes from T34's <c>unwrap</c> (calls3.byn:27), a stub that returns its input; the cases bind pattern
    /// variables only on non-optional inputs, where the C# <c>Unwrap</c> returns its input too.
    /// </summary>
    private static void Locals(List<string> lines, FunctionSymbol fn, int from)
    {
        foreach (var l in fn.Locals.Skip(from)) lines.Add(LocalLine(l));
    }

    private static string LocalLine(LocalSymbol l)
    {
        var type = l.Type.DisplayName;
        var keyword = l.KeywordSpan is { } s ? $"{s.Start}..{s.End}" : "-";
        return $"local {l.Name} {l.Id} {(int)l.Kind} {type} using {Lower(l.IsUsing)} keyword {keyword} const {l.Constant?.ToString() ?? "-"}";
    }

    /// <summary>The Bayan expression for a type the cases use.</summary>
    private static string BayanType(TypeSymbol t) => t switch
    {
        PrimitiveType p => $"prim(.{p.DisplayName})",
        Bayan.Semantics.Symbols.ListType { Element: PrimitiveType { Kind: PrimitiveKind.I32 } } => "listI32",
        _ => throw new InvalidOperationException(t.DisplayName),
    };

    private static string Camel(string name) =>
        string.Concat(name.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Span(TextSpan s) => $"TextSpan {{ start: {s.Start}, length: {s.Length} }}";

    private static int IndexOf(Oracle o, Func<Decl, bool> pick) => Array.FindIndex(o.Members, d => pick(d));

    private static FunctionDecl Fn(Oracle o, string name) =>
        o.Members.OfType<FunctionDecl>().Single(f => f.Name.Text == name);

    private static int FnIndex(Oracle o, string name) => IndexOf(o, d => d is FunctionDecl f && f.Name.Text == name);

    /// <summary>Bayan statements that build function `name` of the oracle's compilation by hand, into `f`.</summary>
    private static void BuildFunction(StringBuilder sb, Oracle o, string name)
    {
        var fn = o.Module.Functions[name][0];
        sb.AppendLine($"    let f = newFunction(ref c, by: FunctionSymbol {{ name: \"{fn.Name}\", module: 0, kind: .function, file: 0, declIndex: {FnIndex(o, name)}, returnType: {BayanType(fn.ReturnType)} }})");
        foreach (var p in fn.Parameters)
            sb.AppendLine($"    tcParam(ref c, for: f, by: \"{p.Name}\", at: {Span(p.Location!.Value.Span)}, as: {BayanType(p.Type)})");
    }

    /// <summary>Opens `switch` on function `name`'s declaration, binding `d`; <see cref="CloseFunction"/> closes it.</summary>
    private static void OpenFunction(StringBuilder sb, Oracle o, string name)
    {
        sb.AppendLine($"    switch c.files[0].unit.members[{FnIndex(o, name)}] {{");
        sb.AppendLine("        case .functionDecl(let d):");
    }

    private static void CloseFunction(StringBuilder sb)
    {
        sb.AppendLine("        default:");
        sb.AppendLine("            break");
        sb.AppendLine("    }");
    }

    // ── Function bodies (BindFunctionBody → BindBlock → BindStatement) ───────────

    private static readonly string[] BodyFunctions = ["decls", "conds", "loops", "misc", "seeded"];

    /// <summary>The `seeded` case's re-bind types (C# `_seeds`), by statement index: one integer, one not (Binder.cs:823).</summary>
    private static readonly (int Statement, PrimitiveType Type)[] Seeds = [(0, PrimitiveType.I64), (1, PrimitiveType.String)];

    private static List<string> ExpectedBody(string name)
    {
        var o = Oracle.Of("main", Source);
        var fn = o.Module.Functions[name][0];
        var before = fn.Locals.Count;
        Dictionary<LocalDecl, TypeSymbol>? seeds = null;
        if (name == "seeded")
        {
            var statements = fn.Decl!.Body!.Statements;
            seeds = Seeds.ToDictionary(s => (LocalDecl)statements[s.Statement], s => (TypeSymbol)s.Type);
        }
        var binder = o.Binder(fn, seeds);
        Call(binder, "BindFunctionBody", fn.Decl);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, before);
        lines.AddRange(o.NewDiagnostics());
        var handled = (Expr?)BinderType.GetField("_handled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder);
        lines.Add($"handled {(handled is null ? "-" : $"{handled.Span.Start}..{handled.Span.End}")}");
        return lines;
    }

    private static string BodiesMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        foreach (var name in BodyFunctions)
        {
            sb.AppendLine($"void body{Camel(name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            BuildFunction(sb, o, name);
            sb.AppendLine("    var b = tcBinder(f)");
            if (name == "seeded")
            {
                var statements = Fn(o, name).Body!.Statements;
                foreach (var (s, t) in Seeds) sb.AppendLine($"    b.seeds.append(({statements[s].Span.Start}, {BayanType(t)}))");
            }
            sb.AppendLine($"    print(\"== body {name}\")");
            OpenFunction(sb, o, name);
            sb.AppendLine("            bindFunctionBody(ref b, through: ref c, for: in d.value)");
            CloseFunction(sb);
            sb.AppendLine("    tcBody(ref c, for: f)");
            sb.AppendLine("    var handled = \"-\"");
            sb.AppendLine("    if (b.handled is { } h) { handled = $\"{h.start}..{h.end}\" }");
            sb.AppendLine("    print($\"handled {handled}\")");
            sb.AppendLine("}");
        }
        sb.AppendLine("void bodiesAll() {");
        foreach (var name in BodyFunctions) sb.AppendLine($"    body{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Statements only the C# parser rejects (BindBlock with no function) ──────

    private static List<string> ExpectedBroken()
    {
        var o = Oracle.Of("broken", BrokenSource);
        var block = (BoundBlock)Call(o.Binder(null), "BindBlock", Fn(o, "misc").Body)!;
        var lines = new List<string>();
        Dump(lines, block);
        foreach (var d in block.Statements.OfType<BoundLocalDecl>()) lines.Add(LocalLine(d.Local));
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string BrokenMain()
    {
        var o = Oracle.Of("broken", BrokenSource);
        var sb = new StringBuilder();
        sb.AppendLine("void brokenAll() {");
        sb.AppendLine($"    var c = tcCtx({Literal(BrokenSource)})");
        sb.AppendLine("    var b = tcBinder(-1)");
        sb.AppendLine("    print(\"== broken\")");
        OpenFunction(sb, o, "misc");
        sb.AppendLine("            switch d.value.body {");
        sb.AppendLine("                case .blockStmt(let blk):");
        sb.AppendLine("                    let s = bindBlock(ref b, through: ref c, for: in blk.value)");
        sb.AppendLine("                    tcDump(ref c, for: s, at: 0)");
        sb.AppendLine("                    var decls = List<LocalId>()");
        sb.AppendLine("                    for st in c.bBlocks[boundIndex(s)].statements {");
        sb.AppendLine("                        if boundKind(st) == .localDecl { decls.append(c.bLocalDecls[boundIndex(st)].local) }");
        sb.AppendLine("                    }");
        sb.AppendLine("                    for l in decls { tcLocal(ref c, for: l) }");
        sb.AppendLine("                default:");
        sb.AppendLine("                    break");
        sb.AppendLine("            }");
        CloseFunction(sb);
        sb.AppendLine("    tcDiags(in c, from: 0)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── BindFor ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each `for` of a block: span, the loop variable (name, id, kind, `borrowsSubject`), the target and the bound iterable.
    /// The element type and the body come from T36's `declaredElement`/`bindForBody` stubs, so they are not printed (Q599);
    /// `for n in 3` reaches T36's `notIterable`, whose BYN2221 report is filtered out of the diagnostics for the same reason.
    /// </summary>
    private static void ForLines(List<string> lines, BoundBlock block)
    {
        foreach (var f in block.Statements.Cast<BoundFor>())
        {
            var v = f.Variable;
            lines.Add($"for {f.Syntax.Span.Start}..{f.Syntax.Span.End} {v.Name} {v.Id} {(int)v.Kind} borrows {Lower(v.BorrowsSubject)}{Target(f.Target)}");
            Dump(lines, f.Iterable, 1);
        }
    }

    private static List<string> ExpectedFors()
    {
        var o = Oracle.Of("main", Source);
        var fn = o.Module.Functions["fors"][0];
        Call(o.Binder(fn), "BindFunctionBody", fn.Decl);
        var lines = new List<string>();
        ForLines(lines, fn.Body!);
        lines.AddRange(o.NewDiagnostics().Where(d => !d.StartsWith("BYN2221 "))); // the notIterable report waits on T36's `notIterable` (Q599)
        var ob = Oracle.Of("broken", BrokenSource);
        ForLines(lines, (BoundBlock)Call(ob.Binder(null), "BindBlock", Fn(ob, "forVar").Body)!);
        lines.AddRange(ob.NewDiagnostics());
        return lines;
    }

    private const string ForLinesBayan = """
                    var ss = List<BStmt>()
                    for st in c.bBlocks[boundIndex(s)].statements { ss.append(st) }
                    for st in ss {
                        let i = boundIndex(st)
                        let v = c.bFors[i].variable
                        print($"for {c.bFors[i].span.start}..{c.bFors[i].span.end} {c.locals[v].name} {c.locals[v].id} {c.locals[v].kind.ordinal} borrows {c.locals[v].borrowsSubject}{tcTarget(in c, for: c.bFors[i].target)}")
                        tcDump(ref c, for: c.bFors[i].iterable, at: 1)
                    }

        """;

    private static string ForsMain()
    {
        var o = Oracle.Of("main", Source);
        var ob = Oracle.Of("broken", BrokenSource);
        var sb = new StringBuilder();
        sb.AppendLine("void forsAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    let listI32 = internList(ref c, by: prim(.i32))");
        BuildFunction(sb, o, "fors");
        sb.AppendLine("    var b = tcBinder(f)");
        sb.AppendLine("    print(\"== fors\")");
        OpenFunction(sb, o, "fors");
        sb.AppendLine("            bindFunctionBody(ref b, through: ref c, for: in d.value)");
        CloseFunction(sb);
        sb.AppendLine("    let s = c.functions[f].body");
        sb.Append(ForLinesBayan.Replace("                    ", "    "));
        sb.AppendLine("    tcDiags(in c, from: 0)");
        sb.AppendLine("    forVar()");
        sb.AppendLine("}");
        sb.AppendLine("void forVar() {");
        sb.AppendLine($"    var c = tcCtx({Literal(BrokenSource)})");
        sb.AppendLine("    var b = tcBinder(-1)");
        OpenFunction(sb, ob, "forVar");
        sb.AppendLine("            switch d.value.body {");
        sb.AppendLine("                case .blockStmt(let blk):");
        sb.AppendLine("                    let s = bindBlock(ref b, through: ref c, for: in blk.value)");
        sb.Append(ForLinesBayan);
        sb.AppendLine("                default:");
        sb.AppendLine("                    break");
        sb.AppendLine("            }");
        CloseFunction(sb);
        sb.AppendLine("    tcDiags(in c, from: 0)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── BindExitCode ─────────────────────────────────────────────────────────────

    private static readonly string[] ExitFunctions = ["x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11"];

    private static List<string> ExpectedExitCodes()
    {
        var o = Oracle.Of("main", Source);
        var lines = new List<string>();
        foreach (var name in ExitFunctions)
        {
            lines.Add($"exit {name} {Call(o.Binder(null), "BindExitCode", Fn(o, name).Attributes[0])}");
            lines.AddRange(o.NewDiagnostics());
        }
        return lines;
    }

    private static string ExitCodesMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        sb.AppendLine("void exitCodesAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    print(\"== exit codes\")");
        sb.AppendLine("    var mark = 0");
        foreach (var name in ExitFunctions)
        {
            OpenFunction(sb, o, name);
            sb.AppendLine("            var b = tcBinder(-1)");
            sb.AppendLine($"            print($\"exit {name} {{bindExitCode(ref b, through: ref c, for: in d.value.attributes[0])}}\")");
            sb.AppendLine("            tcDiags(in c, from: mark)");
            sb.AppendLine("            mark = c.diagnostics.length");
            CloseFunction(sb);
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── DefaultType ──────────────────────────────────────────────────────────────

    /// <summary>One bare literal each, since the C# parser drops the rest of a block after BYN1012.</summary>
    private static readonly string[] DefaultFunctions = ["dtsNull", "dtsInt", "dtsBool"];

    private static List<string> ExpectedDefaultTypes()
    {
        var o = Oracle.Of("broken", BrokenSource);
        var lines = new List<string>();
        foreach (var s in DefaultFunctions.Select(f => Fn(o, f).Body!.Statements[0]))
        {
            var b = o.Binder(null);
            var e = ((ExprStmt)s).Expr;
            var init = (BoundExpr)Call(b, "BindExpr", e, null)!;
            lines.Add($"default {((TypeSymbol)Call(b, "DefaultType", init, e)!).DisplayName}");
            lines.AddRange(o.NewDiagnostics());
        }
        return lines;
    }

    private static string DefaultTypesMain()
    {
        var o = Oracle.Of("broken", BrokenSource);
        var sb = new StringBuilder();
        sb.AppendLine("void defaultTypesAll() {");
        sb.AppendLine($"    var c = tcCtx({Literal(BrokenSource)})");
        sb.AppendLine("    print(\"== default types\")");
        sb.AppendLine("    var mark = 0");
        foreach (var name in DefaultFunctions)
        {
            OpenFunction(sb, o, name);
            sb.AppendLine("            switch d.value.body {");
            sb.AppendLine("                case .blockStmt(let blk):");
            sb.AppendLine("                    switch blk.value.statements[0] {");
            sb.AppendLine("                        case .exprStmt(let e):");
            sb.AppendLine("                            var b = tcBinder(-1)");
            sb.AppendLine("                            let init = bindExpr(ref b, through: ref c, for: in e.value.expr)");
            sb.AppendLine("                            let t = defaultType(ref b, through: ref c, for: init, from: in e.value.expr)");
            sb.AppendLine("                            print($\"default {typeDisplay(ref c, by: t)}\")");
            sb.AppendLine("                            tcDiags(in c, from: mark)");
            sb.AppendLine("                            mark = c.diagnostics.length");
            sb.AppendLine("                        default:");
            sb.AppendLine("                            break");
            sb.AppendLine("                    }");
            sb.AppendLine("                default:");
            sb.AppendLine("                    break");
            sb.AppendLine("            }");
            CloseFunction(sb);
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── CheckUniqueConst ─────────────────────────────────────────────────────────

    /// <summary>The `uq` statements, each with the type it is checked as: a `unique` struct, a struct holding one, a list, i32.</summary>
    private static readonly (int Statement, string Type)[] UniqueCases = [(0, "U"), (1, "U"), (2, "H"), (3, "List"), (4, "i32")];

    private static List<string> ExpectedUnique()
    {
        var o = Oracle.Of("main", Source);
        var statements = Fn(o, "uq").Body!.Statements;
        var lines = new List<string>();
        foreach (var (s, t) in UniqueCases)
        {
            TypeSymbol type = t switch
            {
                "List" => new Bayan.Semantics.Symbols.ListType(PrimitiveType.I32),
                "i32" => PrimitiveType.I32,
                _ => o.Module.Types[t],
            };
            lines.Add($"unique {s} {Lower((bool)Call(o.Binder(null), "CheckUniqueConst", statements[s], type)!)}");
            lines.AddRange(o.NewDiagnostics());
        }
        return lines;
    }

    private static string UniqueMain()
    {
        var o = Oracle.Of("main", Source);
        var u = IndexOf(o, d => d is StructDecl { Name.Text: "U" });
        var h = IndexOf(o, d => d is StructDecl { Name.Text: "H" });
        var sb = new StringBuilder();
        sb.AppendLine("void uniqueAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine($"    let u = newStruct(ref c, by: StructSymbol {{ name: \"U\", module: 0, file: 0, declIndex: {u}, modifiers: 64 }})");
        sb.AppendLine($"    c.structs[u].fields.append(newField(ref c, by: FieldSymbol {{ name: \"v\", owner: u, file: 0, declIndex: {u}, memberIndex: 0, type: prim(.i32) }}))");
        sb.AppendLine($"    let h = newStruct(ref c, by: StructSymbol {{ name: \"H\", module: 0, file: 0, declIndex: {h} }})");
        sb.AppendLine($"    c.structs[h].fields.append(newField(ref c, by: FieldSymbol {{ name: \"u\", owner: h, file: 0, declIndex: {h}, memberIndex: 0, type: structTypeOf(u) }}))");
        sb.AppendLine("    let list = internList(ref c, by: prim(.i32))");
        sb.AppendLine("    print(\"== unique\")");
        sb.AppendLine("    var mark = 0");
        foreach (var (s, t) in UniqueCases)
        {
            var type = t switch { "List" => "list", "i32" => "prim(.i32)", "U" => "structTypeOf(u)", _ => "structTypeOf(h)" };
            OpenFunction(sb, o, "uq");
            sb.AppendLine("            switch d.value.body {");
            sb.AppendLine("                case .blockStmt(let blk):");
            sb.AppendLine($"                    switch blk.value.statements[{s}] {{");
            sb.AppendLine("                        case .localDecl(let l):");
            sb.AppendLine("                            var b = tcBinder(-1)");
            sb.AppendLine($"                            print($\"unique {s} {{checkUniqueConst(ref b, through: ref c, for: in l.value, as: {type})}}\")");
            sb.AppendLine("                            tcDiags(in c, from: mark)");
            sb.AppendLine("                            mark = c.diagnostics.length");
            sb.AppendLine("                        default:");
            sb.AppendLine("                            break");
            sb.AppendLine("                    }");
            sb.AppendLine("                default:");
            sb.AppendLine("                    break");
            sb.AppendLine("            }");
            CloseFunction(sb);
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Null narrowing (IsNonNull, Narrowed, NarrowAfter, Exits, NonNullWhen) ────

    /// <summary>The locals the narrowing cases test, all `i32?` but the last: a let, a var, parameters by value, `ref` and `in`, a plain let.</summary>
    private static readonly (string Name, LocalKind Kind, ParamModifier? Modifier, bool Optional)[] NarrowLocals =
    [
        ("a", LocalKind.Let, null, true), ("v", LocalKind.Var, null, true), ("p", LocalKind.Parameter, ParamModifier.None, true),
        ("r", LocalKind.Parameter, ParamModifier.Ref, true), ("q", LocalKind.Parameter, ParamModifier.In, true), ("n", LocalKind.Let, null, false),
    ];

    private abstract record Cond;

    /// <summary>`local is null`, `local is not null` (<paramref name="Not"/>), or with <paramref name="Other"/> a constant pattern on local p rather than `null`.</summary>
    private sealed record IsNull(int Local, bool Not, bool Other = false) : Cond;

    private sealed record NotOf(Cond Operand) : Cond;

    private sealed record Logical(Cond Left, Cond Right, bool Or) : Cond;

    /// <summary>
    /// One step: push, pop, check (whether each local is narrowed), within (a check inside <c>Narrowed(cond, whenTrue)</c>), or
    /// after (<c>NarrowAfter</c> of an `if cond` with the given then and else statements, see <see cref="CsStmt"/>).
    /// </summary>
    private sealed record NStep(string Op, Cond? Cond = null, bool WhenTrue = false, string Then = "", string? Else = null);

    private static IsNull Present(int l) => new(l, true);

    private static IsNull Absent(int l) => new(l, false);

    private static NStep Within(Cond c, bool whenTrue) => new("within", c, whenTrue);

    private static NStep After(Cond c, string then, string? els = null) => new("after", c, Then: then, Else: els);

    private static readonly NStep Check = new("check");

    private static readonly NStep Push = new("push");

    private static readonly NStep Pop = new("pop");

    private static readonly string[] ExitKinds = ["ret", "brk", "cont", "empty", "blockret", "blockvalue", "never", "value", "ifboth", "ifnoelse", "ifhalf"];

    private static readonly (string Name, NStep[] Steps)[] NarrowCases =
    [
        ("present", [Within(Present(0), true), Within(Present(0), false), Check]),
        ("absent", [Within(Absent(0), true), Within(Absent(0), false), Check]),
        ("kinds", [Within(new Logical(Present(0), new Logical(Present(1), new Logical(Present(2), new Logical(Present(3), new Logical(Present(4), Present(5), false), false), false), false), false), true)]),
        ("not", [Within(new NotOf(Absent(0)), true), Within(new NotOf(Present(0)), true), Within(new NotOf(new NotOf(Present(2))), true)]),
        ("and", [Within(new Logical(Present(0), Present(2), false), true), Within(new Logical(Present(0), Present(2), false), false)]),
        ("or", [Within(new Logical(Absent(0), Absent(2), true), false), Within(new Logical(Absent(0), Absent(2), true), true)]),
        ("other-pattern", [Within(new IsNull(0, true, true), true), Within(new IsNull(0, false, true), false)]),
        ("after-then", [After(Absent(0), "ret"), Check]),
        ("after-else", [After(Present(2), "empty", "ret"), Check]),
        ("after-both", [After(Absent(0), "ret", "ret"), After(Absent(2), "empty", "empty"), Check]),
        ("exits", ExitKinds.SelectMany(k => new[] { Push, After(Absent(0), k), Check, Pop }).ToArray()),
        ("sibling", [Push, After(Absent(0), "ret"), Check, Pop, Check, Push, Check, Pop]),
        ("nested", [After(Absent(0), "ret"), Push, Push, Check, Pop, Pop, Check]),
        ("restore", [Push, After(Absent(0), "ret"), Within(Present(2), true), Check, Pop]),
    ];

    private sealed class CsNarrow(Oracle o)
    {
        public readonly Node Syntax = o.Members[0];
        public readonly LocalSymbol[] Locals = NarrowLocals.Select((l, i) =>
        {
            TypeSymbol t = l.Optional ? new Bayan.Semantics.Symbols.OptionalType(PrimitiveType.I32) : PrimitiveType.I32;
            return l.Modifier is { } m ? new ParameterSymbol(l.Name, null, t, m, i) : new LocalSymbol(l.Name, null, t, l.Kind);
        }).ToArray();

        public BoundExpr Expr(Cond c) => c switch
        {
            IsNull i => new BoundIs(Syntax, new BoundLocal(Syntax, Locals[i.Local]), Pattern(i)),
            NotOf n => new BoundUnary(Syntax, UnaryOp.Not, Expr(n.Operand), PrimitiveType.Bool),
            Logical l => new BoundBinary(Syntax, l.Or ? BinaryOp.LogicalOr : BinaryOp.LogicalAnd, Expr(l.Left), Expr(l.Right), PrimitiveType.Bool),
            _ => throw new InvalidOperationException(),
        };

        private BoundPattern Pattern(IsNull i)
        {
            var t = Locals[i.Local].Type;
            BoundExpr value = i.Other ? new BoundLocal(Syntax, Locals[2]) : new BoundDefault(Syntax, t);
            BoundPattern k = new BoundConstantPattern(Syntax, t, value);
            return i.Not ? new BoundNotPattern(Syntax, t, k) : k;
        }

        public BoundStmt Stmt(string k) => k switch
        {
            "ret" => new BoundReturn(Syntax, null),
            "brk" => new BoundBreak(Syntax, new JumpTarget(null, true)),
            "cont" => new BoundContinue(Syntax, new JumpTarget(null, true)),
            "empty" => new BoundBlock(Syntax, []),
            "blockret" => new BoundBlock(Syntax, [Stmt("value"), Stmt("ret")]),
            "blockvalue" => new BoundBlock(Syntax, [Stmt("ret"), Stmt("value")]),
            "never" => new BoundExprStmt(Syntax, new BoundDefault(Syntax, PrimitiveType.Never)),
            "value" => new BoundExprStmt(Syntax, new BoundDefault(Syntax, PrimitiveType.I32)),
            "ifboth" => new BoundIf(Syntax, new BoundDefault(Syntax, PrimitiveType.Bool), Stmt("ret"), Stmt("ret")),
            "ifnoelse" => new BoundIf(Syntax, new BoundDefault(Syntax, PrimitiveType.Bool), Stmt("ret"), null),
            "ifhalf" => new BoundIf(Syntax, new BoundDefault(Syntax, PrimitiveType.Bool), Stmt("ret"), Stmt("empty")),
            _ => throw new InvalidOperationException(k),
        };
    }

    private const string NarrowSource = "void f() {\n}\n";

    private static List<string> ExpectedNarrow(string name)
    {
        var o = Oracle.Of("narrow", NarrowSource);
        var cs = new CsNarrow(o);
        var b = o.Binder(null);
        var narrowed = BinderType.GetMethod("Narrowed", BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(string));
        string CheckLine() => "check" + string.Concat(cs.Locals.Select(l => $" {l.Name}={Lower((bool)Call(b, "IsNonNull", l)!)}"));
        var lines = new List<string>();
        foreach (var s in NarrowCases.Single(c => c.Name == name).Steps)
        {
            switch (s.Op)
            {
                case "push": Call(b, "PushScope"); break;
                case "pop": Call(b, "PopScope"); break;
                case "check": lines.Add(CheckLine()); break;
                case "within":
                    lines.Add("in " + (string)narrowed.Invoke(b, [cs.Expr(s.Cond!), s.WhenTrue, (Func<string>)CheckLine])!);
                    break;
                default:
                    Call(b, "NarrowAfter", new BoundIf(cs.Syntax, cs.Expr(s.Cond!), cs.Stmt(s.Then), s.Else is null ? null : cs.Stmt(s.Else)));
                    break;
            }
        }
        return lines;
    }

    /// <summary>Bayan statements that build `c` into a fresh `let`, returning its name.</summary>
    private static string BayanExpr(StringBuilder sb, Cond c, ref int n)
    {
        var x = $"x{n++}";
        switch (c)
        {
            case IsNull i:
                sb.AppendLine($"    let {x}t = c.locals[ls[{i.Local}]].type");
                sb.AppendLine($"    let {x}e = addLocal(ref c, from: BLocal {{ span: sp, local: ls[{i.Local}], type: {x}t }})");
                sb.AppendLine(i.Other
                    ? $"    let {x}v = addLocal(ref c, from: BLocal {{ span: sp, local: ls[2], type: c.locals[ls[2]].type }})"
                    : $"    let {x}v = addDefault(ref c, from: BDefault {{ span: sp, type: {x}t }})");
                sb.AppendLine($"    let {x}k = addConstantPattern(ref c, from: BConstantPattern {{ span: sp, inputType: {x}t, value: {x}v }})");
                var pattern = $"{x}k";
                if (i.Not)
                {
                    sb.AppendLine($"    let {x}n = addNotPattern(ref c, from: BNotPattern {{ span: sp, inputType: {x}t, operand: {x}k }})");
                    pattern = $"{x}n";
                }
                sb.AppendLine($"    let {x} = addIs(ref c, from: BIs {{ span: sp, value: {x}e, pattern: {pattern} }})");
                break;
            case NotOf no:
                var operand = BayanExpr(sb, no.Operand, ref n);
                sb.AppendLine($"    let {x} = addUnary(ref c, from: BUnary {{ span: sp, op: .notKeyword, operand: {operand}, type: prim(.bool) }})");
                break;
            case Logical l:
                var left = BayanExpr(sb, l.Left, ref n);
                var right = BayanExpr(sb, l.Right, ref n);
                sb.AppendLine($"    let {x} = addBinary(ref c, from: BBinary {{ span: sp, op: .{(l.Or ? "logicalOr" : "logicalAnd")}, left: {left}, right: {right}, type: prim(.bool) }})");
                break;
        }
        return x;
    }

    private static string BayanStmt(StringBuilder sb, string k, ref int n)
    {
        var x = $"s{n++}";
        switch (k)
        {
            case "ret": sb.AppendLine($"    let {x} = addReturn(ref c, from: BReturn {{ span: sp }})"); break;
            case "brk": sb.AppendLine($"    let {x} = addBreak(ref c, from: BBreak {{ span: sp, target: 0 }})"); break;
            case "cont": sb.AppendLine($"    let {x} = addContinue(ref c, from: BContinue {{ span: sp, target: 0 }})"); break;
            case "empty": sb.AppendLine($"    let {x} = addBlock(ref c, from: BBlock {{ span: sp }})"); break;
            case "blockret" or "blockvalue":
                var first = BayanStmt(sb, k == "blockret" ? "value" : "ret", ref n);
                var second = BayanStmt(sb, k == "blockret" ? "ret" : "value", ref n);
                sb.AppendLine($"    var {x}l = List<BStmt>()");
                sb.AppendLine($"    {x}l.append({first})");
                sb.AppendLine($"    {x}l.append({second})");
                sb.AppendLine($"    let {x} = addBlock(ref c, from: BBlock {{ span: sp, statements: move {x}l }})");
                break;
            case "never" or "value":
                sb.AppendLine($"    let {x}e = addDefault(ref c, from: BDefault {{ span: sp, type: {(k == "never" ? "prim(.neverKind)" : "prim(.i32)")} }})");
                sb.AppendLine($"    let {x} = addExprStmt(ref c, from: BExprStmt {{ span: sp, expr: {x}e }})");
                break;
            default:
                var then = BayanStmt(sb, "ret", ref n);
                var els = k switch { "ifboth" => BayanStmt(sb, "ret", ref n), "ifhalf" => BayanStmt(sb, "empty", ref n), _ => "-1" };
                sb.AppendLine($"    let {x}c = addDefault(ref c, from: BDefault {{ span: sp, type: prim(.bool) }})");
                sb.AppendLine($"    let {x} = addIf(ref c, from: BIf {{ span: sp, condition: {x}c, then: {then}, elseBranch: {els}, isNever: false }})");
                break;
        }
        return x;
    }

    private static string NarrowMain()
    {
        var sb = new StringBuilder();
        foreach (var (name, steps) in NarrowCases)
        {
            sb.AppendLine($"void narrow{Camel(name)}() {{");
            sb.AppendLine($"    var c = tcCtx({Literal(NarrowSource)})");
            sb.AppendLine("    var b = tcBinder(-1)");
            sb.AppendLine("    let sp = TextSpan { start: 0, length: 0 }");
            sb.AppendLine("    let opt = internOptional(ref c, by: prim(.i32))");
            sb.AppendLine("    var ls = List<LocalId>()");
            for (var i = 0; i < NarrowLocals.Length; i++)
            {
                var (local, kind, modifier, optional) = NarrowLocals[i];
                var type = optional ? "opt" : "prim(.i32)";
                var fields = modifier is { } m
                    ? $"kind: .parameter, isParameter: true, modifier: .{(m switch { ParamModifier.None => "none", ParamModifier.Ref => "refKeyword", _ => "inKeyword" })}, ordinal: {i}"
                    : $"kind: .{(kind == LocalKind.Let ? "letKeyword" : "varKeyword")}";
                sb.AppendLine($"    ls.append(newLocal(ref c, by: LocalSymbol {{ name: \"{local}\", type: {type}, {fields} }}))");
            }
            sb.AppendLine($"    print(\"== narrow {name}\")");
            var n = 0;
            foreach (var s in steps)
            {
                switch (s.Op)
                {
                    case "push": sb.AppendLine("    pushScope(ref b)"); break;
                    case "pop": sb.AppendLine("    popScope(ref b)"); break;
                    case "check": sb.AppendLine("    print(tcCheck(ref b, through: in c, for: in ls))"); break;
                    case "within":
                        var cond = BayanExpr(sb, s.Cond!, ref n);
                        var scope = $"w{n++}";
                        sb.AppendLine($"    let {scope} = narrowedBegin(ref b, through: ref c, for: {cond}, whenTrue: {Lower(s.WhenTrue)})");
                        sb.AppendLine("    print($\"in {tcCheck(ref b, through: in c, for: in ls)}\")");
                        sb.AppendLine($"    narrowedEnd(ref b, through: ref c, by: move {scope})");
                        break;
                    default:
                        var condition = BayanExpr(sb, s.Cond!, ref n);
                        var then = BayanStmt(sb, s.Then, ref n);
                        var els = s.Else is null ? "-1" : BayanStmt(sb, s.Else, ref n);
                        var i = $"i{n++}";
                        sb.AppendLine($"    let {i} = addIf(ref c, from: BIf {{ span: sp, condition: {condition}, then: {then}, elseBranch: {els}, isNever: false }})");
                        sb.AppendLine($"    let _ = narrowAfter(ref b, through: ref c, for: {i})");
                        break;
                }
            }
            sb.AppendLine("}");
        }
        sb.AppendLine("void narrowAll() {");
        foreach (var (name, _) in NarrowCases) sb.AppendLine($"    narrow{Camel(name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── IsBorrowedPlace ──────────────────────────────────────────────────────────

    /// <summary>
    /// A hand-built bound expression: `L`/`LH`/`LU` a local (i32, H, U), `D`/`DH`/`DS` a default (i32, H, `i32[]`), `this`, `bi:member`,
    /// `field` (H.u), `tuple`, `index`, `conv`, `cond`, `coal`, `switch` (one arm per operand), `with`/`withu` (no update, `u`
    /// replaced), `try`, `catch`. The binder reaches IsBorrowedPlace only through T36's `userElement` (Q599), so both sides build it.
    /// </summary>
    private sealed record PExpr(string Op, params PExpr[] Kids);

    private static readonly PExpr PL = new("L"), PD = new("D");

    private static PExpr P(string op, params PExpr[] kids) => new(op, kids);

    private static readonly (string Name, PExpr Expr)[] BorrowCases =
    [
        ("local", PL), ("default", PD), ("this", P("this")), ("owned", P("bi:OwnedValue", PD)),
        ("mapIndex", P("bi:MapIndex", PL)), ("mapIndexTemp", P("bi:MapIndex", PD)), ("resultValue", P("bi:ResultValue", PL)),
        ("mapTryGet", P("bi:MapTryGet", PL)), ("listTryGet", P("bi:ListTryGet", PL)), ("listTryGetTemp", P("bi:ListTryGet", PD)),
        ("stringLength", P("bi:StringLength", PL)), ("field", P("field", P("LH"))), ("fieldTemp", P("field", P("DH"))),
        ("tuple", P("tuple", PL)), ("tupleTemp", P("tuple", PD)), ("indexSpan", P("index", P("DS"))), ("index", P("index", PL)),
        ("indexTemp", P("index", PD)), ("conv", P("conv", PL)), ("convTemp", P("conv", PD)), ("condFalse", P("cond", PD, PL)),
        ("condTrue", P("cond", PL, PD)), ("condTemp", P("cond", PD, PD)), ("coalRight", P("coal", PD, PL)), ("coalLeft", P("coal", PL, PD)),
        ("coalTemp", P("coal", PD, PD)), ("switch", P("switch", PD, PL)), ("switchTemp", P("switch", PD, PD)), ("switchEmpty", P("switch")),
        ("with", P("with", P("LH"))), ("withReplaced", P("withu", P("LH"))), ("withTemp", P("with", P("DH"))), ("withI32", P("with", PL)), ("withPlain", P("with", P("LU"))),
        ("try", P("try", PL)), ("tryTemp", P("try", PD)), ("catchHandler", P("catch", PD, PL)), ("catchOperand", P("catch", PL, PD)),
        ("catchTemp", P("catch", PD, PD)),
    ];

    private static List<string> ExpectedBorrowed()
    {
        var o = Oracle.Of("main", Source);
        var syntax = o.Members[0];
        var h = (StructSymbol)o.Module.Types["H"];
        var u = h.Fields[0];
        var l = new LocalSymbol("l", null, PrimitiveType.I32, LocalKind.Let);
        var lh = new LocalSymbol("lh", null, h, LocalKind.Let);
        var lu = new LocalSymbol("lu", null, u.Type, LocalKind.Let);
        var isBorrowed = BinderType.GetMethod("IsBorrowedPlace", BindingFlags.Static | BindingFlags.NonPublic)!;
        BoundExpr D(TypeSymbol t) => new BoundDefault(syntax, t);
        BoundExpr Cs(PExpr p)
        {
            var k = p.Kids.Select(Cs).ToArray();
            return p.Op switch
            {
                "L" => new BoundLocal(syntax, l),
                "LH" => new BoundLocal(syntax, lh),
                "LU" => new BoundLocal(syntax, lu),
                "D" => D(PrimitiveType.I32),
                "DH" => D(h),
                "DS" => D(new SpanType(PrimitiveType.I32)),
                "this" => new BoundThis(syntax, h),
                "field" => new BoundField(syntax, k[0], u),
                "tuple" => new BoundTupleElement(syntax, k[0], 0, PrimitiveType.I32),
                "index" => new BoundIndex(syntax, k[0], D(PrimitiveType.I32), false, PrimitiveType.I32),
                "conv" => new BoundConversion(syntax, k[0], PrimitiveType.I32, ConversionKind.Identity),
                "cond" => new BoundConditional(syntax, D(PrimitiveType.Bool), k[0], k[1], PrimitiveType.I32),
                "coal" => new BoundCoalesce(syntax, k[0], k[1], PrimitiveType.I32),
                "switch" => new BoundSwitchExpr(syntax, D(PrimitiveType.I32),
                    k.Select(v => new BoundSwitchArm(syntax, new BoundConstantPattern(syntax, PrimitiveType.I32, D(PrimitiveType.I32)), null, v)).ToArray(),
                    PrimitiveType.I32),
                "with" => new BoundWith(syntax, k[0], []),
                "withu" => new BoundWith(syntax, k[0], [(u, D(u.Type))]),
                "try" => new BoundTry(syntax, k[0], PrimitiveType.I32),
                "catch" => new BoundCatch(syntax, k[0], null, null, k[1], PrimitiveType.I32),
                _ => new BoundBuiltinCall(syntax, Enum.Parse<BuiltinMember>(p.Op[3..]), k[0], [], PrimitiveType.I32),
            };
        }
        return BorrowCases.Select(c => $"borrowed {c.Name} {Lower((bool)isBorrowed.Invoke(null, [Cs(c.Expr)])!)}").ToList();
    }

    /// <summary>Bayan statements that build <paramref name="p"/> into a fresh `let`, returning its name.</summary>
    private static string BayanPlace(StringBuilder sb, PExpr p, ref int n)
    {
        var k = new List<string>();
        foreach (var kid in p.Kids) k.Add(BayanPlace(sb, kid, ref n));
        var x = $"x{n++}";
        var i32 = $"addDefault(ref c, from: BDefault {{ span: sp, type: prim(.i32) }})";
        switch (p.Op)
        {
            case "L": sb.AppendLine($"    let {x} = addLocal(ref c, from: BLocal {{ span: sp, local: l, type: prim(.i32) }})"); break;
            case "LH": sb.AppendLine($"    let {x} = addLocal(ref c, from: BLocal {{ span: sp, local: lh, type: structTypeOf(h) }})"); break;
            case "LU": sb.AppendLine($"    let {x} = addLocal(ref c, from: BLocal {{ span: sp, local: lu, type: structTypeOf(u) }})"); break;
            case "D": sb.AppendLine($"    let {x} = {i32}"); break;
            case "DH": sb.AppendLine($"    let {x} = addDefault(ref c, from: BDefault {{ span: sp, type: structTypeOf(h) }})"); break;
            case "DS": sb.AppendLine($"    let {x} = addDefault(ref c, from: BDefault {{ span: sp, type: internSpan(ref c, by: prim(.i32), readOnly: false) }})"); break;
            case "this": sb.AppendLine($"    let {x} = addThis(ref c, from: BThis {{ span: sp, type: structTypeOf(h) }})"); break;
            case "field": sb.AppendLine($"    let {x} = addField(ref c, from: BField {{ span: sp, receiver: {k[0]}, field: fu }})"); break;
            case "tuple": sb.AppendLine($"    let {x} = addTupleElement(ref c, from: BTupleElement {{ span: sp, receiver: {k[0]}, index: 0, type: prim(.i32) }})"); break;
            case "index":
                sb.AppendLine($"    let {x}i = {i32}");
                sb.AppendLine($"    let {x} = addIndex(ref c, from: BIndex {{ span: sp, target: {k[0]}, index: {x}i, type: prim(.i32) }})");
                break;
            case "conv": sb.AppendLine($"    let {x} = addConversion(ref c, from: BConversion {{ span: sp, operand: {k[0]}, type: prim(.i32), kind: .identity }})"); break;
            case "cond":
                sb.AppendLine($"    let {x}c = addDefault(ref c, from: BDefault {{ span: sp, type: prim(.bool) }})");
                sb.AppendLine($"    let {x} = addConditional(ref c, from: BConditional {{ span: sp, condition: {x}c, whenTrue: {k[0]}, whenFalse: {k[1]}, type: prim(.i32) }})");
                break;
            case "coal": sb.AppendLine($"    let {x} = addCoalesce(ref c, from: BCoalesce {{ span: sp, left: {k[0]}, right: {k[1]}, type: prim(.i32) }})"); break;
            case "switch":
                sb.AppendLine($"    var {x}a = List<BSwitchArm>()");
                for (var i = 0; i < k.Count; i++)
                {
                    sb.AppendLine($"    let {x}v{i} = {i32}");
                    sb.AppendLine($"    let {x}p{i} = addConstantPattern(ref c, from: BConstantPattern {{ span: sp, inputType: prim(.i32), value: {x}v{i} }})");
                    sb.AppendLine($"    {x}a.append(BSwitchArm {{ span: sp, pattern: {x}p{i}, value: {k[i]} }})");
                }
                sb.AppendLine($"    let {x}s = {i32}");
                sb.AppendLine($"    let {x} = addSwitchExpr(ref c, from: BSwitchExpr {{ span: sp, value: {x}s, arms: move {x}a, type: prim(.i32) }})");
                break;
            case "with" or "withu":
                sb.AppendLine($"    var {x}u = List<BWithUpdate>()");
                if (p.Op == "withu")
                {
                    sb.AppendLine($"    let {x}v = addDefault(ref c, from: BDefault {{ span: sp, type: structTypeOf(u) }})");
                    sb.AppendLine($"    {x}u.append(BWithUpdate {{ field: fu, value: {x}v }})");
                }
                sb.AppendLine($"    let {x} = addWith(ref c, from: BWith {{ span: sp, target: {k[0]}, updates: move {x}u }})");
                break;
            case "try": sb.AppendLine($"    let {x} = addTry(ref c, from: BTry {{ span: sp, operand: {k[0]}, type: prim(.i32) }})"); break;
            case "catch": sb.AppendLine($"    let {x} = addCatch(ref c, from: BCatch {{ span: sp, operand: {k[0]}, handler: {k[1]}, type: prim(.i32) }})"); break;
            default:
                sb.AppendLine($"    let {x} = addBuiltinCall(ref c, from: BBuiltinCall {{ span: sp, member: .{char.ToLowerInvariant(p.Op[3])}{p.Op[4..]}, receiver: {k[0]}, type: prim(.i32) }})");
                break;
        }
        return x;
    }

    private static string BorrowedMain()
    {
        var o = Oracle.Of("main", Source);
        var u = IndexOf(o, d => d is StructDecl { Name.Text: "U" });
        var h = IndexOf(o, d => d is StructDecl { Name.Text: "H" });
        var sb = new StringBuilder();
        sb.AppendLine("void borrowedAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    let sp = TextSpan { start: 0, length: 0 }");
        sb.AppendLine($"    let u = newStruct(ref c, by: StructSymbol {{ name: \"U\", module: 0, file: 0, declIndex: {u}, modifiers: 64 }})");
        sb.AppendLine($"    c.structs[u].fields.append(newField(ref c, by: FieldSymbol {{ name: \"v\", owner: u, file: 0, declIndex: {u}, memberIndex: 0, type: prim(.i32) }}))");
        sb.AppendLine($"    let h = newStruct(ref c, by: StructSymbol {{ name: \"H\", module: 0, file: 0, declIndex: {h} }})");
        sb.AppendLine($"    let fu = newField(ref c, by: FieldSymbol {{ name: \"u\", owner: h, file: 0, declIndex: {h}, memberIndex: 0, type: structTypeOf(u) }})");
        sb.AppendLine("    c.structs[h].fields.append(fu)");
        sb.AppendLine("    let l = newLocal(ref c, by: LocalSymbol { name: \"l\", type: prim(.i32), kind: .letKeyword })");
        sb.AppendLine("    let lh = newLocal(ref c, by: LocalSymbol { name: \"lh\", type: structTypeOf(h), kind: .letKeyword })");
        sb.AppendLine("    let lu = newLocal(ref c, by: LocalSymbol { name: \"lu\", type: structTypeOf(u), kind: .letKeyword })");
        sb.AppendLine("    print(\"== borrowed\")");
        var n = 0;
        foreach (var (name, expr) in BorrowCases)
        {
            var x = BayanPlace(sb, expr, ref n);
            sb.AppendLine($"    print($\"borrowed {name} {{isBorrowedPlace(ref c, for: {x})}}\")");
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

    private static string MainSource() => Prelude.Replace("$SOURCE", Literal(Source)) + BodiesMain() + BrokenMain() + ExitCodesMain()
        + DefaultTypesMain() + UniqueMain() + NarrowMain() + BorrowedMain() + ForsMain() + """
        i32 main() {
            bodiesAll()
            brokenAll()
            exitCodesAll()
            defaultTypesAll()
            uniqueAll()
            narrowAll()
            borrowedAll()
            forsAll()
            return 0
        }

        """;

    private const string Prelude = """
        import lexer
        import parser

        Ctx tcCtx(string text) {
            var bytes = List<u8>()
            for x in text.bytes { bytes.append(x) }
            var copy = List<u8>()
            for x in text.bytes { copy.append(x) }
            let source = lexer.SourceText.create("t16.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
            c.files.append(SourceFile { path: "t16.byn", displayPath: "t16.byn", text: move copy, unit: move unit, module: 0, isEntry: true, imports: Map<string, ModId>() })
            var files = List<FileId>()
            files.append(0)
            c.modules.append(Mod { name: "t16", files: move files, directory: ".", cName: "t16" })
            return c
        }

        Ctx tcSource() => tcCtx($SOURCE)

        Binder tcBinder(FuncId fn) => Binder {
            file: 0,
            fn: fn,
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

        string tcTarget(in Ctx c, for i64 t) {
            let label = c.jumpTargets[t].label ?? "-"
            return $" target {label} {c.jumpTargets[t].isLoop}"
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
            if k == .whileStmt { sb.append(tcTarget(in c, for: c.bWhiles[boundIndex(n)].target)) }
            if k == .doWhile { sb.append(tcTarget(in c, for: c.bDoWhiles[boundIndex(n)].target)) }
            if k == .ifStmt && c.bIfs[boundIndex(n)].isNever { sb.append(" never") }
            print($"{sb.finish()}")
            var kids = List<BNode>()
            for ch in children(in c, for: n) { kids.append(ch) }
            for ch in kids { tcDump(ref c, for: ch, at: depth + 1) }
        }

        void tcLocal(ref Ctx c, for LocalId l) {
            let type = typeDisplay(ref c, by: c.locals[l].type)
            var keyword = "-"
            if (c.locals[l].keywordSpan is { } s) { keyword = $"{s.start}..{s.end}" }
            var constant = "-"
            if (c.locals[l].constant is { } v) { constant = constantText(in v) }
            print($"local {c.locals[l].name} {c.locals[l].id} {c.locals[l].kind.ordinal} {type} using {c.locals[l].isUsing} keyword {keyword} const {constant}")
        }

        void tcLocals(ref Ctx c, for FuncId f) {
            var ls = List<LocalId>()
            for l in c.functions[f].locals { ls.append(l) }
            for l in ls { tcLocal(ref c, for: l) }
        }

        void tcBody(ref Ctx c, for FuncId f) {
            tcDump(ref c, for: c.functions[f].body, at: 0)
            tcLocals(ref c, for: f)
            tcDiags(in c, from: 0)
        }

        string tcCheck(ref Binder b, through in Ctx c, for in List<LocalId> ls) {
            var sb = StrBuf()
            sb.append("check")
            for l in ls { sb.append($" {c.locals[l].name}={isNonNull(ref b, for: l)}") }
            return $"{sb.finish()}"
        }

        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    public static TheoryData<string> BodyNames() => new(BodyFunctions);

    public static TheoryData<string> NarrowNames() => new(NarrowCases.Select(c => c.Name));

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void FunctionBodyMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"body {name}", ExpectedBody(name));

    [Fact]
    public void BrokenStatementsMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "broken", ExpectedBroken());

    [Fact]
    public void ExitCodesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "exit codes", ExpectedExitCodes());

    [Fact]
    public void DefaultTypesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "default types", ExpectedDefaultTypes());

    [Fact]
    public void UniqueConstMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "unique", ExpectedUnique());

    [Fact]
    public void ForStatementsMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "fors", ExpectedFors());

    [Fact]
    public void BorrowedPlacesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "borrowed", ExpectedBorrowed());

    [Theory]
    [MemberData(nameof(NarrowNames))]
    public void NarrowingMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"narrow {name}", ExpectedNarrow(name));
}
