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
/// T18: <c>selfhost/semantics/express2.byn</c> against the C# <c>BinderExpressions</c> (BinderExpressions.cs ~600-1155).
/// Every expected line is computed from the C# implementation in-process via reflection on a fresh <c>Binder</c>
/// and <c>Compilation.Create</c>.
/// </summary>
public sealed class SelfhostSemanticsT18Tests
{
    private const string Dir = "tmp/t18-cases";

    private const string Source = """
        const k: i64 = 42
        const arr: i32[2] = [1, 2]

        enum Color {
            red
            green
            blue
        }

        enum Status(i32 code) {
            ok(200)
            err(500)
        }

        union error MyError {
            notFound
            badInput
        }

        union error OtherError {
            timeout
            badInput
        }

        union Shape {
            circle(f64 radius)
            point
        }

        struct Point {
            i32 x = 10
            i32 y = 20
            i32 twice => 20
            static Point origin() => Point { x: 0, y: 0 }
            void clear() {
            }
            void m() {
                let a = x
                let b = twice
                clear()
            }
            static void s() {
                let p = 0
            }
        }

        interface IHasArea {
            f64 area()
        }

        void runGeneric<T>(T item) where T : IHasArea {
            let a = item.area()
        }
        """;

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
            var c = Compilation.Create([SourceText.FromString(path, text)], packageRoot: Dir, options: new CompilationOptions(Library: true));
            if (c.Diagnostics.HasErrors)
            {
                var errs = string.Join("\n", c.Diagnostics.Items.Select(d => $"{d.Code} {d.Span.Start}: {d.Message}"));
                throw new InvalidOperationException($"Compilation failed:\n{errs}");
            }
            var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
            return new Oracle { C = c, File = file, Mark = c.Diagnostics.Items.Count };
        }

        public ModuleSymbol Module => File.Module;

        public Decl[] Members => File.Tree.Root.Members;

        public object Binder(FunctionSymbol? fn = null, StructSymbol? container = null, IReadOnlyDictionary<string, TypeSymbol>? typeArgs = null) =>
            Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [C, File, fn, container, typeArgs, null], null)!;

        public IEnumerable<string> NewDiagnostics()
        {
            var items = C.Diagnostics.Items;
            var lines = new List<string>();
            for (var i = Mark; i < items.Count; i++)
            {
                foreach (var line in Diag(items[i]).Split('\n'))
                    lines.Add(line);
            }
            Mark = items.Count;
            return lines;
        }
    }

    private static string PathOf(string name) => Path.Combine(Dir, name + ".byn");

    private static object? Call(object? binder, string name, params object?[] args)
    {
        var methods = BinderType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == name && m.GetParameters().Length == args.Length)
            .ToArray();
        if (methods.Length == 1) return methods[0].Invoke(methods[0].IsStatic ? null : binder, args);
        // Fallback matching parameter types
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            var match = true;
            for (var i = 0; i < ps.Length; i++)
            {
                if (args[i] is not null && !ps[i].ParameterType.IsInstanceOfType(args[i]))
                {
                    match = false;
                    break;
                }
            }
            if (match) return m.Invoke(m.IsStatic ? null : binder, args);
        }
        throw new MissingMethodException(BinderType.FullName, name);
    }

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static string NodeKindName(BoundNode node) => node switch
    {
        BoundLiteral => "literal",
        BoundSymbolLiteral => "symbolLiteral",
        BoundDefault => "defaultValue",
        BoundLocal => "local",
        BoundThis => "thisRef",
        BoundField => "field",
        BoundTupleElement => "tupleElement",
        BoundCall => "call",
        BoundBuiltinCall => "builtinCall",
        BoundUnary => "unary",
        BoundBinary => "binary",
        BoundConversion => "conversion",
        BoundTryFrom => "tryFrom",
        BoundConditional => "conditional",
        BoundCoalesce => "coalesce",
        BoundStructLiteral => "structLiteral",
        BoundWith => "withExpr",
        BoundTuple => "tuple",
        BoundCollection => "collection",
        BoundSpread => "spread",
        BoundIndex => "index",
        BoundSlice => "slice",
        BoundRange => "range",
        BoundIs => "isExpr",
        BoundSwitchExpr => "switchExpr",
        BoundInterpolated => "interpolated",
        BoundMove => "moveExpr",
        BoundTry => "tryExpr",
        BoundCatch => "catchExpr",
        BoundJump => "jump",
        BoundBlockExpr => "blockExpr",
        BoundErrorVariant => "errorVariant",
        BoundEnumMember => "enumMember",
        BoundUnionNew => "unionNew",
        BoundEnumValues => "enumValues",
        BoundStaticArray => "staticArray",
        BoundStackalloc => "stackalloc",
        BoundErrorExpr => "errorExpr",
        BoundTypeRef => "typeRef",
        BoundModuleRef => "moduleRef",
        BoundLambda => "lambda",
        _ => node.GetType().Name.StartsWith("Bound")
            ? char.ToLowerInvariant(node.GetType().Name[5]) + node.GetType().Name[6..]
            : node.GetType().Name,
    };

    private static void DumpNode(List<string> lines, BoundNode? node)
    {
        if (node is null)
        {
            lines.Add("null");
            return;
        }
        var kind = NodeKindName(node);
        var type = node is BoundExpr e ? e.Type.DisplayName : "";
        lines.Add($"{kind} {type}".TrimEnd());
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void ConstantRefMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var k = o.Module.Constants["k"];
        var node = (BoundNode)Call(b, "ConstantRef", k.Decl, k)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "constantRef", expected);
    }

    [Fact]
    public void ConstantRefStaticArrayMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var arr = o.Module.Constants["arr"];
        var node = (BoundNode)Call(b, "ConstantRef", arr.Decl, arr)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "constantRefArray", expected);
    }

    [Fact]
    public void LookupMemberStructMatches()
    {
        var o = Oracle.Of("main", Source);
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var fx = (Symbol)Call(o.Binder(), "LookupMember", pt, "x")!;
        var expected = new List<string> { fx.Name };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "lookupMember", expected);
    }

    [Fact]
    public void LookupMemberMissingMatches()
    {
        var o = Oracle.Of("main", Source);
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var res = Call(o.Binder(), "LookupMember", pt, "nonexistent");
        var expected = new List<string> { res is null ? "null" : res.ToString()! };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "lookupMemberMissing", expected);
    }

    private static Name MkName(string text, int start = 0, int length = 0) =>
        new Name(new TextSpan(start, length > 0 ? length : text.Length), text);

    private static NameExpr MkNameExpr(string text, int start = 0, int length = 0) =>
        new NameExpr(new TextSpan(start, length > 0 ? length : text.Length), MkName(text, start, length), []);

    private static MemberExpr MkMemberExpr(Expr target, string member, int start = 0, int length = 0, int memberStart = 0) =>
        new MemberExpr(new TextSpan(start, length > 0 ? length : 10), target, MkName(member, memberStart > 0 ? memberStart : start + 6, member.Length), false);

    private static LiteralExpr MkStringLiteral(string text, int start = 0, int length = 0) =>
        new LiteralExpr(new TextSpan(start, length > 0 ? length : text.Length), LiteralKind.String, text, null);

    private static LiteralExpr MkIntLiteral(long val, int start = 0, int length = 0) =>
        new LiteralExpr(new TextSpan(start, length > 0 ? length : val.ToString().Length), LiteralKind.Integer, val.ToString(), null);

    [Fact]
    public void ImplicitThisWithoutThisMatches()
    {
        var o = Oracle.Of("main", Source);
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var sfn = pt.Methods.First(m => m.Name == "s");
        var b = o.Binder(sfn, pt);
        var nameExpr = MkNameExpr("x", 10, 1);
        var f = pt.FindField("x")!;
        var node = (BoundNode)Call(b, "BindImplicitThisMember", nameExpr, f)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "implicitThisWithoutThis", expected);
    }

    [Fact]
    public void MemberEnumStaticMatches()
    {
        var o = Oracle.Of("main", Source);
        var en = o.Module.Enums.First(e => e.Name == "Color");
        var b = o.Binder();
        var red = en.Find("red")!;
        var target = MkNameExpr("Color", 0, 5);
        var mExpr = MkMemberExpr(target, "red", 0, 9, 6);
        var node = (BoundNode)Call(b, "BindEnumStatic", mExpr, en)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "enumStatic", expected);
    }

    [Fact]
    public void MemberEnumCountMatches()
    {
        var o = Oracle.Of("main", Source);
        var en = o.Module.Enums.First(e => e.Name == "Color");
        var b = o.Binder();
        var target = MkNameExpr("Color", 0, 5);
        var mExpr = MkMemberExpr(target, "count", 0, 11, 6);
        var node = (BoundNode)Call(b, "BindEnumStatic", mExpr, en)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "enumCount", expected);
    }

    [Fact]
    public void MemberEnumValuesMatches()
    {
        var o = Oracle.Of("main", Source);
        var en = o.Module.Enums.First(e => e.Name == "Color");
        var b = o.Binder();
        var target = MkNameExpr("Color", 0, 5);
        var mExpr = MkMemberExpr(target, "values", 0, 12, 6);
        var node = (BoundNode)Call(b, "BindEnumStatic", mExpr, en)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "enumValues", expected);
    }

    [Fact]
    public void MemberEnumUnknownMatches()
    {
        var o = Oracle.Of("main", Source);
        var en = o.Module.Enums.First(e => e.Name == "Color");
        var b = o.Binder();
        var target = MkNameExpr("Color", 0, 5);
        var mExpr = MkMemberExpr(target, "reed", 0, 10, 6);
        var node = (BoundNode)Call(b, "BindEnumStatic", mExpr, en)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "enumUnknown", expected);
    }

    [Fact]
    public void MemberUnionVariantMatches()
    {
        var o = Oracle.Of("main", Source);
        var un = o.Module.Unions.First(u => u.Name == "Shape");
        var b = o.Binder();
        var tm = new TargetMemberExpr(new TextSpan(0, 11), MkName("point", 6, 5));
        var node = (BoundNode)Call(b, "BindVariant", tm, tm.Member, un)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "unionVariantPoint", expected);
    }

    [Fact]
    public void MemberUnionVariantPayloadGroupMatches()
    {
        var o = Oracle.Of("main", Source);
        var un = o.Module.Unions.First(u => u.Name == "Shape");
        var b = o.Binder();
        var tm = new TargetMemberExpr(new TextSpan(0, 12), MkName("circle", 6, 6));
        var node = (BoundNode)Call(b, "BindVariant", tm, tm.Member, un)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "unionVariantCircle", expected);
    }

    [Fact]
    public void MemberUnionVariantUnknownMatches()
    {
        var o = Oracle.Of("main", Source);
        var un = o.Module.Unions.First(u => u.Name == "Shape");
        var b = o.Binder();
        var tm = new TargetMemberExpr(new TextSpan(0, 10), MkName("circ", 6, 4));
        var node = (BoundNode)Call(b, "BindVariant", tm, tm.Member, un)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "unionVariantUnknown", expected);
    }

    [Fact]
    public void MemberSuggestionSizeMatches()
    {
        var o = Oracle.Of("main", Source);
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var sug = (string?)Call(o.Binder(), "MemberSuggestion", pt, "size");
        var expected = new List<string> { sug ?? "null" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberSuggestionSize", expected);
    }

    [Fact]
    public void MemberSuggestionTypoMatches()
    {
        var o = Oracle.Of("main", Source);
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var sug = (string?)Call(o.Binder(), "MemberSuggestion", pt, "twce");
        var expected = new List<string> { sug ?? "null" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberSuggestionTypo", expected);
    }

    [Fact]
    public void ReportRenamedToStringMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("toString", 0, 8);
        var res = (bool)Call(b, "ReportRenamed", PrimitiveType.I64, name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedToString", expected);
    }

    [Fact]
    public void ReportRenamedAddMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("add", 0, 3);
        var res = (bool)Call(b, "ReportRenamed", new ListType(PrimitiveType.I32), name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedAdd", expected);
    }

    [Fact]
    public void StringIndexerMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        // Trigger string indexer report directly through BindIndex on a string target
        var target = MkStringLiteral("\"abc\"", 0, 5);
        var idx = MkIntLiteral(0, 6, 1);
        var iExpr = new IndexExpr(new TextSpan(0, 8), target, idx);
        var node = (BoundNode)Call(b, "BindIndex", iExpr)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "stringIndexer", expected);
    }

    [Fact]
    public void InterpolationSourceMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var lit = MkStringLiteral("\"hello\"", 0, 7);
        var s = (string)Call(b, "InterpolationSource", lit)!;
        var expected = new List<string> { s };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "interpolationSource", expected);
    }

    [Fact]
    public void ReportRenamedOffsetMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("offset", 0, 6);
        var res = (bool)Call(b, "ReportRenamed", PrimitiveType.String, name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedOffset", expected);
    }

    [Fact]
    public void ReportRenamedReplaceRangeMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("replaceRange", 0, 12);
        var res = (bool)Call(b, "ReportRenamed", new ListType(PrimitiveType.I32), name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedReplaceRange", expected);
    }

    [Fact]
    public void ReportRenamedGetOrMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("getOr", 0, 5);
        var res = (bool)Call(b, "ReportRenamed", new MapType(PrimitiveType.String, PrimitiveType.I32), name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedGetOr", expected);
    }

    [Fact]
    public void ReportRenamedToListMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var name = MkName("toList", 0, 6);
        var res = (bool)Call(b, "ReportRenamed", new ListType(PrimitiveType.I32), name)!;
        var expected = new List<string> { res.ToString() };
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportRenamedToList", expected);
    }

    [Fact]
    public void TargetMemberAmbiguousVariantMatches()
    {
        var o = Oracle.Of("main", Source);
        var myErr = o.Module.ErrorSets.First(e => e.Name == "MyError");
        var otherErr = o.Module.ErrorSets.First(e => e.Name == "OtherError");
        var errUnion = new ErrorUnionType([myErr, otherErr]);
        var tm = new TargetMemberExpr(new TextSpan(0, 9), MkName("badInput", 1, 8));
        var node = (BoundNode)Call(o.Binder(), "BindTargetMember", tm, errUnion)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "targetMemberAmbiguousVariant", expected);
    }

    [Fact]
    public void MemberIoStdoutMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var target = MkNameExpr("Io", 0, 2);
        var m = MkMemberExpr(target, "stdout", 0, 9, 3);
        var node = (BoundNode)Call(b, "BindMember", m)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberIoStdout", expected);
    }

    [Fact]
    public void MemberDynInterfaceMethodMatches()
    {
        var o = Oracle.Of("main", Source);
        var iface = o.Module.Interfaces.First(i => i.Name == "IHasArea");
        var dynType = new DynRefType(iface, readOnly: false);
        var b = o.Binder();
        var nameExpr = MkNameExpr("item", 0, 4);
        var locSym = new LocalSymbol("item", null, dynType, LocalKind.Let);
        var target = new BoundLocal(nameExpr, locSym);
        var node = (BoundNode)Call(b, "BindValueMember", nameExpr, target, MkName("area", 5, 4))!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberDynInterfaceMethod", expected);
    }

    [Fact]
    public void MemberModuleFunctionMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var target = MkNameExpr("test", 0, 4);
        var m = MkMemberExpr(target, "runGeneric", 0, 15, 5);
        var node = (BoundNode)Call(b, "BindMember", m)!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberModuleFunction", expected);
    }

    [Fact]
    public void MemberUnionNarrowedPayloadMatches()
    {
        var o = Oracle.Of("main", Source);
        var b = o.Binder();
        var sh = o.Module.Unions.First(u => u.Name == "Shape");
        var circleVar = sh.Variants.First(v => v.Name == "circle");
        var nameExpr = MkNameExpr("s", 0, 1);
        var locSym = new LocalSymbol("s", null, sh, LocalKind.Let);
        var bLoc = new BoundLocal(nameExpr, locSym);
        var narrowedField = BinderType.GetField("_narrowed", BindingFlags.Instance | BindingFlags.NonPublic);
        if (narrowedField is not null)
        {
            var dict = (System.Collections.IDictionary)narrowedField.GetValue(b)!;
            dict[locSym] = circleVar;
        }
        var node = (BoundNode)Call(b, "BindValueMember", nameExpr, bLoc, MkName("radius", 2, 6))!;
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "memberUnionNarrowedPayload", expected);
    }

    private static string MainSource()
    {
        return """
            import lexer
            import parser

            type Expr = parser.Expr
            type MemberExpr = parser.MemberExpr
            type TargetMemberExpr = parser.TargetMemberExpr
            type NameExpr = parser.NameExpr
            type IndexExpr = parser.IndexExpr
            type LiteralExpr = parser.LiteralExpr
            type TypeSyntax = parser.TypeSyntax

            SourceFile makeFile(string path, from string text) {
                var bytes = List<u8>()
                for b in text.bytes { bytes.append(b) }
                return SourceFile {
                    path: path,
                    displayPath: path,
                    text: move bytes,
                    unit: CompilationUnit { span: TextSpan { start: 0, length: 0 }, members: List<parser.Decl>() },
                    module: 0,
                    isEntry: true,
                    isTestFile: false,
                    imports: Map<string, ModId>(),
                }
            }

            Ctx makeCtx(string text) {
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                }
                c.files.append(makeFile("tmp/t18-cases/main.byn", from: text))
                var modFiles = List<FileId>()
                modFiles.append(0)
                c.modules.append(Mod {
                    name: "test",
                    files: move modFiles,
                    directory: ".",
                    cName: "test",
                })
                return c
            }

            Binder makeBinder(FileId file = 0, FuncId fn = -1, StructId owner = -1, Type thisType = -1) {
                return Binder {
                    file: file,
                    fn: fn,
                    owner: owner,
                    thisType: thisType,
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
            }

            void tcDump(ref Ctx c, for BExpr e) {
                if e == -1 {
                    print("null")
                    return
                }
                let k = boundKind(e)
                let t = exprType(in c, for: e)
                let typeName = t == -1 ? "" : typeDisplay(ref c, by: t)
                let s = $"{k} {typeName}"
                print(s.trimmed())
            }

            void tcDiags(ref Ctx c) {
                for d in c.diagnostics {
                    let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
                    let fixText = d.hasFix ? $"fix {d.fixSpan.start}..{d.fixSpan.end} {d.fixReplacement}" : ""
                    print($"{d.code} {pos.line}:{pos.column} {d.message} |{d.label}|{d.help}|{fixText}")
                }
            }

            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    // Section constantRef
                    {
                        print("== constantRef")
                        var c = makeCtx("const k: i64 = 42")
                        var b = makeBinder(file: 0)
                        c.consts.append(ConstSymbol { name: "k", type: prim(.i64), state: 2, value: Constant { kind: .integer, integer: bigFromI64(42) } })
                        let e = constantRef(ref b, through: ref c, at: TextSpan { start: 0, length: 14 }, for: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section constantRefArray
                    {
                        print("== constantRefArray")
                        var c = makeCtx("const arr: i32[2] = [1, 2]")
                        var b = makeBinder(file: 0)
                        let arrType = internArray(ref c, by: prim(.i32), at: 2)
                        c.consts.append(ConstSymbol { name: "arr", type: arrType, state: 2, isStaticArray: true })
                        let e = constantRef(ref b, through: ref c, at: TextSpan { start: 15, length: 26 }, for: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section lookupMember
                    {
                        print("== lookupMember")
                        var c = makeCtx("")
                        c.fields.append(FieldSymbol { name: "x", type: prim(.i32) })
                        c.structs.append(StructSymbol { name: "Point", fields: [0] })
                        let m = lookupMember(ref c, for: makeType(.structType, at: 0), by: "x")
                        if m.kind == .field {
                            print(c.fields[m.id].name)
                        } else {
                            print("none")
                        }
                    }

                    // Section lookupMemberMissing
                    {
                        print("== lookupMemberMissing")
                        var c = makeCtx("")
                        c.structs.append(StructSymbol { name: "Point" })
                        let m = lookupMember(ref c, for: makeType(.structType, at: 0), by: "nonexistent")
                        if m.kind == .none {
                            print("null")
                        } else {
                            print("found")
                        }
                    }

                    // Section implicitThisWithoutThis
                    {
                        print("== implicitThisWithoutThis")
                        var c = makeCtx("          x")
                        c.fields.append(FieldSymbol { name: "x", type: prim(.i32) })
                        c.structs.append(StructSymbol { name: "Point", fields: [0] })
                        c.functions.append(FunctionSymbol { name: "s", isStatic: true, kind: .method })
                        var b = makeBinder(file: 0, fn: 0, owner: 0, thisType: makeType(.structType, at: 0))
                        let n = NameExpr { span: TextSpan { start: 10, length: 1 }, name: TextSpan { start: 10, length: 1 }, typeArgs: List<TypeSyntax>() }
                        let e = bindImplicitThisMember(ref b, through: ref c, for: in n, by: MemberRef { kind: .field, id: 0 })
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section enumStatic
                    {
                        print("== enumStatic")
                        var c = makeCtx("Color.red")
                        c.enumMembers.append(EnumMemberSymbol { name: "red", owner: 0 })
                        c.enums.append(EnumSymbol { name: "Color", members: [0] })
                        var b = makeBinder(file: 0)
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 5 }, name: TextSpan { start: 0, length: 5 }, typeArgs: List<TypeSyntax>() }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 9 }, target: move target, member: TextSpan { start: 6, length: 3 }, nullConditional: false, typeArgs: List<TypeSyntax>() }
                        let e = t18BindEnumStatic(ref b, through: ref c, for: m, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section enumCount
                    {
                        print("== enumCount")
                        var c = makeCtx("Color.count")
                        c.enumMembers.append(EnumMemberSymbol { name: "red", owner: 0 })
                        c.enumMembers.append(EnumMemberSymbol { name: "green", owner: 0 })
                        c.enumMembers.append(EnumMemberSymbol { name: "blue", owner: 0 })
                        c.enums.append(EnumSymbol { name: "Color", members: [0, 1, 2] })
                        var b = makeBinder(file: 0)
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 5 }, name: TextSpan { start: 0, length: 5 }, typeArgs: List<TypeSyntax>() }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 11 }, target: move target, member: TextSpan { start: 6, length: 5 }, nullConditional: false, typeArgs: List<TypeSyntax>() }
                        let e = t18BindEnumStatic(ref b, through: ref c, for: m, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section enumValues
                    {
                        print("== enumValues")
                        var c = makeCtx("Color.values")
                        c.enumMembers.append(EnumMemberSymbol { name: "red", owner: 0 })
                        c.enums.append(EnumSymbol { name: "Color", members: [0] })
                        var b = makeBinder(file: 0)
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 5 }, name: TextSpan { start: 0, length: 5 }, typeArgs: List<TypeSyntax>() }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 12 }, target: move target, member: TextSpan { start: 6, length: 6 }, nullConditional: false, typeArgs: List<TypeSyntax>() }
                        let e = t18BindEnumStatic(ref b, through: ref c, for: m, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section enumUnknown
                    {
                        print("== enumUnknown")
                        var c = makeCtx("Color.reed")
                        c.enumMembers.append(EnumMemberSymbol { name: "red", owner: 0 })
                        c.enums.append(EnumSymbol { name: "Color", members: [0] })
                        var b = makeBinder(file: 0)
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 5 }, name: TextSpan { start: 0, length: 5 }, typeArgs: List<TypeSyntax>() }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 10 }, target: move target, member: TextSpan { start: 6, length: 4 }, nullConditional: false, typeArgs: List<TypeSyntax>() }
                        let e = t18BindEnumStatic(ref b, through: ref c, for: m, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section unionVariantPoint
                    {
                        print("== unionVariantPoint")
                        var c = makeCtx("shape.point")
                        c.unionVariants.append(UnionVariantSymbol { name: "point", owner: 0, fields: [] })
                        c.unions.append(UnionSymbol { name: "Shape", variants: [0] })
                        var b = makeBinder(file: 0)
                        let e = t18BindVariant(ref b, through: ref c, at: TextSpan { start: 0, length: 11 }, by: TextSpan { start: 6, length: 5 }, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section unionVariantCircle
                    {
                        print("== unionVariantCircle")
                        var c = makeCtx("shape.circle")
                        c.locals.append(LocalSymbol { name: "radius", type: prim(.f64) })
                        c.unionVariants.append(UnionVariantSymbol { name: "circle", owner: 0, fields: [0] })
                        c.unions.append(UnionSymbol { name: "Shape", variants: [0] })
                        var b = makeBinder(file: 0)
                        let e = t18BindVariant(ref b, through: ref c, at: TextSpan { start: 0, length: 12 }, by: TextSpan { start: 6, length: 6 }, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section unionVariantUnknown
                    {
                        print("== unionVariantUnknown")
                        var c = makeCtx("shape.circ")
                        c.unionVariants.append(UnionVariantSymbol { name: "point", owner: 0, fields: [] })
                        c.unions.append(UnionSymbol { name: "Shape", variants: [0] })
                        var b = makeBinder(file: 0)
                        let e = t18BindVariant(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, by: TextSpan { start: 6, length: 4 }, to: 0)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section memberSuggestionSize
                    {
                        print("== memberSuggestionSize")
                        var c = makeCtx("")
                        c.structs.append(StructSymbol { name: "Point" })
                        let sug = memberSuggestion(ref c, for: makeType(.structType, at: 0), by: "size")
                        print(sug ?? "null")
                    }

                    // Section memberSuggestionTypo
                    {
                        print("== memberSuggestionTypo")
                        var c = makeCtx("")
                        c.properties.append(PropertySymbol { name: "twice" })
                        c.structs.append(StructSymbol { name: "Point", properties: [0] })
                        let sug = memberSuggestion(ref c, for: makeType(.structType, at: 0), by: "twce")
                        print(sug ?? "null")
                    }

                    // Section reportRenamedToString
                    {
                        print("== reportRenamedToString")
                        var c = makeCtx("toString")
                        var b = makeBinder(file: 0)
                        let res = reportRenamed(ref b, through: ref c, for: prim(.i64), at: TextSpan { start: 0, length: 8 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section reportRenamedAdd
                    {
                        print("== reportRenamedAdd")
                        var c = makeCtx("add")
                        var b = makeBinder(file: 0)
                        let listType = internList(ref c, by: prim(.i32))
                        let res = reportRenamed(ref b, through: ref c, for: listType, at: TextSpan { start: 0, length: 3 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section stringIndexer
                    {
                        print("== stringIndexer")
                        var c = makeCtx("const k: i64 = 42")
                        var b = makeBinder(file: 0)
                        let target = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 5 }, kind: .string, text: "\"abc\"", suffix: null }))
                        let index = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 6, length: 1 }, kind: .integer, text: "0", suffix: null }))
                        let idx = IndexExpr { span: TextSpan { start: 0, length: 8 }, target: move target, index: move index }
                        let e = bindIndex(ref b, through: ref c, for: in idx)
                        tcDump(ref c, for: e)
                        tcDiags(ref c)
                    }

                    // Section interpolationSource
                    {
                        print("== interpolationSource")
                        var c = makeCtx("const k: i64 = 42")
                        var b = makeBinder(file: 0)
                        let lit = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 7 }, kind: .string, text: "\"hello\"", suffix: null }))
                        let s = interpolationSource(ref b, through: ref c, for: in lit)
                        print(s)
                    }

                    // Section reportRenamedOffset
                    {
                        print("== reportRenamedOffset")
                        var c = makeCtx("offset")
                        var b = makeBinder(file: 0)
                        let res = reportRenamed(ref b, through: ref c, for: prim(.string), at: TextSpan { start: 0, length: 6 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section reportRenamedReplaceRange
                    {
                        print("== reportRenamedReplaceRange")
                        var c = makeCtx("replaceRange")
                        var b = makeBinder(file: 0)
                        let listType = internList(ref c, by: prim(.i32))
                        let res = reportRenamed(ref b, through: ref c, for: listType, at: TextSpan { start: 0, length: 12 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section reportRenamedGetOr
                    {
                        print("== reportRenamedGetOr")
                        var c = makeCtx("getOr")
                        var b = makeBinder(file: 0)
                        let mapType = internMap(ref c, by: prim(.string), to: prim(.i32))
                        let res = reportRenamed(ref b, through: ref c, for: mapType, at: TextSpan { start: 0, length: 5 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section reportRenamedToList
                    {
                        print("== reportRenamedToList")
                        var c = makeCtx("toList")
                        var b = makeBinder(file: 0)
                        let listType = internList(ref c, by: prim(.i32))
                        let res = reportRenamed(ref b, through: ref c, for: listType, at: TextSpan { start: 0, length: 6 })
                        print(res ? "True" : "False")
                        tcDiags(ref c)
                    }

                    // Section targetMemberAmbiguousVariant
                    {
                        print("== targetMemberAmbiguousVariant")
                        var c = makeCtx(".badInput")
                        var b = makeBinder(file: 0)
                        c.errorVariants.append(ErrorVariantSymbol { name: "badInput", set: 0, fields: [] })
                        c.errorSets.append(ErrorSetSymbol { name: "MyError", variants: [0] })
                        c.errorVariants.append(ErrorVariantSymbol { name: "badInput", set: 1, fields: [] })
                        c.errorSets.append(ErrorSetSymbol { name: "OtherError", variants: [1] })
                        var sets = List<ErrorSetId>()
                        sets.append(0)
                        sets.append(1)
                        var ex = List<VariantId>()
                        let errUnion = errorUnionOf(ref c, for: in sets, as: in ex)
                        let tm = TargetMemberExpr { span: TextSpan { start: 0, length: 9 }, member: TextSpan { start: 1, length: 8 } }
                        let node = bindTargetMember(ref b, through: ref c, for: in tm, to: errUnion)
                        tcDump(ref c, for: node)
                        tcDiags(ref c)
                    }

                    // Section memberIoStdout
                    {
                        print("== memberIoStdout")
                        var c = makeCtx("Io.stdout")
                        var b = makeBinder(file: 0)
                        let fnId = c.functions.length
                        c.functions.append(FunctionSymbol { name: "stdout", builtin: .ioStdout, returnType: prim(.void), isStatic: true })
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 2 }, name: TextSpan { start: 0, length: 2 }, typeArgs: [] }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 9 }, target: move target, member: TextSpan { start: 3, length: 6 }, nullConditional: false }
                        let node = bindMember(ref b, through: ref c, for: in m)
                        tcDump(ref c, for: node)
                        tcDiags(ref c)
                    }

                    // Section memberDynInterfaceMethod
                    {
                        print("== memberDynInterfaceMethod")
                        var c = makeCtx("item.area")
                        var b = makeBinder(file: 0)
                        let ifaceId = c.interfaces.length
                        let areaFn = c.functions.length
                        c.functions.append(FunctionSymbol { name: "area", returnType: prim(.f64), isStatic: false })
                        c.interfaces.append(InterfaceSymbol { name: "IHasArea", methods: [areaFn], properties: [] })
                        let dynType = internDynRef(ref c, by: ifaceId, readOnly: false)
                        let locId = c.locals.length
                        c.locals.append(LocalSymbol { name: "item", type: dynType })
                        let bLoc = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 4 }, local: locId, type: dynType })
                        let node = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 9 }, for: bLoc, by: TextSpan { start: 5, length: 4 })
                        tcDump(ref c, for: node)
                        tcDiags(ref c)
                    }

                    // Section memberModuleFunction
                    {
                        print("== memberModuleFunction")
                        var c = makeCtx("test.runGeneric")
                        var b = makeBinder(file: 0)
                        let fnId = c.functions.length
                        c.functions.append(FunctionSymbol { name: "runGeneric", returnType: prim(.void), access: .publicKeyword })
                        c.modules[0].functions["runGeneric"] = [fnId]
                        let target = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 4 }, name: TextSpan { start: 0, length: 4 }, typeArgs: [] }))
                        let m = MemberExpr { span: TextSpan { start: 0, length: 15 }, target: move target, member: TextSpan { start: 5, length: 10 }, nullConditional: false }
                        let node = bindMember(ref b, through: ref c, for: in m)
                        tcDump(ref c, for: node)
                        tcDiags(ref c)
                    }

                    // Section memberUnionNarrowedPayload
                    {
                        print("== memberUnionNarrowedPayload")
                        var c = makeCtx("s.radius")
                        var b = makeBinder(file: 0)
                        let fId = c.locals.length
                        c.locals.append(LocalSymbol { name: "radius", type: prim(.f64) })
                        let vId = c.unionVariants.length
                        c.unionVariants.append(UnionVariantSymbol { name: "circle", owner: 0, fields: [fId] })
                        c.unions.append(UnionSymbol { name: "Shape", variants: [vId] })
                        let uType = makeType(.unionType, at: 0)
                        let locId = c.locals.length
                        c.locals.append(LocalSymbol { name: "s", type: uType })
                        b.narrowed[locId] = vId
                        let bLoc = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 1 }, local: locId, type: uType })
                        let node = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 8 }, for: bLoc, by: TextSpan { start: 2, length: 6 })
                        tcDump(ref c, for: node)
                        tcDiags(ref c)
                    }
                }
            }
            """;
    }
}
