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
            i32 t = 3
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
            ;
            else
            assert true
            assert p
            assert 3
            assert zz
            assert 3 fails E
            assert zz fails E
            assert (p) fails E.bad
            p
            p if p
            p unless p
            p if 3
        }

        void seeded() {
            var s
            var t
            var w: i32
        }

        void dts() {
            null
            1
            true
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
    /// variable's type comes from T34's <c>unwrap</c> (calls3.byn:27), a stub, so it is not printed (Q599).
    /// </summary>
    private static void Locals(List<string> lines, FunctionSymbol fn, int from)
    {
        foreach (var l in fn.Locals.Skip(from))
        {
            var type = l.Kind == LocalKind.PatternVariable ? "-" : l.Type.DisplayName;
            var keyword = l.KeywordSpan is { } s ? $"{s.Start}..{s.End}" : "-";
            lines.Add($"local {l.Name} {l.Id} {(int)l.Kind} {type} using {Lower(l.IsUsing)} keyword {keyword} const {l.Constant?.ToString() ?? "-"}");
        }
    }

    /// <summary>The Bayan expression for a type the cases use.</summary>
    private static string BayanType(TypeSymbol t) => t switch
    {
        PrimitiveType p => $"prim(.{p.DisplayName})",
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
        Call(o.Binder(fn, seeds), "BindFunctionBody", fn.Decl);
        var lines = new List<string>();
        Dump(lines, fn.Body);
        Locals(lines, fn, before);
        lines.AddRange(o.NewDiagnostics());
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
            sb.AppendLine("}");
        }
        sb.AppendLine("void bodiesAll() {");
        foreach (var name in BodyFunctions) sb.AppendLine($"    body{Camel(name)}()");
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

    private static List<string> ExpectedDefaultTypes()
    {
        var o = Oracle.Of("main", Source);
        var lines = new List<string>();
        foreach (var s in Fn(o, "dts").Body!.Statements)
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
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        sb.AppendLine("void defaultTypesAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    print(\"== default types\")");
        sb.AppendLine("    var mark = 0");
        for (var i = 0; i < Fn(o, "dts").Body!.Statements.Length; i++)
        {
            OpenFunction(sb, o, "dts");
            sb.AppendLine("            switch d.value.body {");
            sb.AppendLine("                case .blockStmt(let blk):");
            sb.AppendLine($"                    switch blk.value.statements[{i}] {{");
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

    private static string MainSource() => Prelude.Replace("$SOURCE", Literal(Source)) + BodiesMain() + ExitCodesMain()
        + DefaultTypesMain() + UniqueMain() + NarrowMain() + """
        i32 main() {
            bodiesAll()
            exitCodesAll()
            defaultTypesAll()
            uniqueAll()
            narrowAll()
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
            for ch in children(in c, for: n) { tcDump(ref c, for: ch, at: depth + 1) }
        }

        void tcLocals(ref Ctx c, for FuncId f) {
            for l in c.functions[f].locals {
                var type = "-"
                if c.locals[l].kind != .patternVariable { type = typeDisplay(ref c, by: c.locals[l].type) }
                var keyword = "-"
                if (c.locals[l].keywordSpan is { } s) { keyword = $"{s.start}..{s.end}" }
                var constant = "-"
                if (c.locals[l].constant is { } v) { constant = constantText(in v) }
                print($"local {c.locals[l].name} {c.locals[l].id} {c.locals[l].kind.ordinal} {type} using {c.locals[l].isUsing} keyword {keyword} const {constant}")
            }
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
            return sb.finish()
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
    public void ExitCodesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "exit codes", ExpectedExitCodes());

    [Fact]
    public void DefaultTypesMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "default types", ExpectedDefaultTypes());

    [Fact]
    public void UniqueConstMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "unique", ExpectedUnique());

    [Theory]
    [MemberData(nameof(NarrowNames))]
    public void NarrowingMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"narrow {name}", ExpectedNarrow(name));
    [Fact]
    public void Debug()
    {
        var o = Oracle.Of("main", Source);
        Assert.Fail(string.Join("\n", o.Module.Functions.Keys) + "\n--\n" + string.Join("\n", o.C.Diagnostics.Items.Select(Diag)));
    }
}
