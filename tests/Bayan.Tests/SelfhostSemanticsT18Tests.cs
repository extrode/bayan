using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using TextSpan = Bayan.Syntax.Text.TextSpan;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;

namespace Bayan.Tests;

/// <summary>
/// T18: <c>selfhost/semantics/express2.byn</c> against the C# <c>BinderExpressions</c> (BinderExpressions.cs ~600-1155).
/// One world source is compiled by both sides: the C# side via <c>Compilation.Create</c> plus reflection on the C#
/// <c>Binder</c>, the Bayan side via <c>SelfhostSemanticsHarness.RunSections</c>. Every expected line is computed
/// from the C# implementation; none is typed.
/// </summary>
public sealed class SelfhostSemanticsT18Tests
{
    private const string Dir = "tmp/t18-cases";

    private const string Source = """
        const k: i64 = 42
        const arr: i32[2] = [1, 2]
        const msg: string = "hello"

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

        // names: Io Hex Symbol Utf8 string test badVariant nope stdout encode intern decode from value length take matches toString add offset
        """;

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private static TextSpan S(string needle, int skip = 0)
    {
        var i = -1;
        for (var k = 0; k <= skip; k++) i = Source.IndexOf(needle, i + 1, StringComparison.Ordinal);
        return new TextSpan(i, needle.Length);
    }

    private sealed class Oracle
    {
        public Compilation C = null!;
        public FileContext File = null!;
        public int Mark;

        public static Oracle Of()
        {
            var path = PathOf("main");
            var c = Compilation.Create([SourceText.FromString(path, Source)], packageRoot: Dir, options: new CompilationOptions(Library: true));
            if (c.Diagnostics.HasErrors)
            {
                var errs = string.Join("\n", c.Diagnostics.Items.Select(d => $"{d.Code} {d.Span.Start}: {d.Message}"));
                throw new InvalidOperationException($"Compilation failed:\n{errs}");
            }
            var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
            return new Oracle { C = c, File = file, Mark = c.Diagnostics.Items.Count };
        }

        public ModuleSymbol Module => File.Module;

        public object Binder(FunctionSymbol? fn = null, StructSymbol? container = null) =>
            Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [C, File, fn, container, null, null], null)!;

        public IEnumerable<string> NewDiagnostics()
        {
            var items = C.Diagnostics.Items;
            var lines = new List<string>();
            for (var i = Mark; i < items.Count; i++)
                foreach (var line in Diag(items[i]).Split('\n')) lines.Add(line);
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
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            var match = true;
            for (var i = 0; i < ps.Length; i++)
                if (args[i] is not null && !ps[i].ParameterType.IsInstanceOfType(args[i])) { match = false; break; }
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
        BoundLiteral => "literal", BoundSymbolLiteral => "symbolLiteral", BoundDefault => "defaultValue",
        BoundLocal => "local", BoundThis => "thisRef", BoundField => "field", BoundTupleElement => "tupleElement",
        BoundCall => "call", BoundBuiltinCall => "builtinCall", BoundUnary => "unary", BoundBinary => "binary",
        BoundConversion => "conversion", BoundTryFrom => "tryFrom", BoundConditional => "conditional",
        BoundCoalesce => "coalesce", BoundStructLiteral => "structLiteral", BoundWith => "withExpr",
        BoundTuple => "tuple", BoundCollection => "collection", BoundSpread => "spread", BoundIndex => "index",
        BoundSlice => "slice", BoundRange => "range", BoundIs => "isExpr", BoundSwitchExpr => "switchExpr",
        BoundInterpolated => "interpolated", BoundMove => "moveExpr", BoundTry => "tryExpr", BoundCatch => "catchExpr",
        BoundJump => "jump", BoundBlockExpr => "blockExpr", BoundErrorVariant => "errorVariant",
        BoundEnumMember => "enumMember", BoundUnionNew => "unionNew", BoundEnumValues => "enumValues",
        BoundStaticArray => "staticArray", BoundStackalloc => "stackalloc", BoundErrorExpr => "errorExpr",
        BoundTypeRef => "typeRef", BoundModuleRef => "moduleRef", BoundLambda => "lambda",
        _ => node.GetType().Name.StartsWith("Bound") ? char.ToLowerInvariant(node.GetType().Name[5]) + node.GetType().Name[6..] : node.GetType().Name,
    };

    private static string Recv(BoundExpr? r) => r switch
    {
        null => "-1", BoundThis => "this", BoundLocal l => "local:" + l.Local.Name, _ => NodeKindName(r),
    };

    private static object? Prop(BoundNode node, string name) => node.GetType().GetProperty(name)?.GetValue(node);

    private static void DumpNode(List<string> lines, BoundNode? node)
    {
        if (node is null) { lines.Add("null"); return; }
        var kind = NodeKindName(node);
        var type = node is BoundExpr e ? e.Type.DisplayName : "-";
        var span = $"{node.Syntax.Span.Start}:{node.Syntax.Span.Length}";
        var recv = "-"; var callee = "-"; var variant = "-"; var idx = "-"; var fromEnd = "-";
        switch (node)
        {
            case BoundField f: recv = Recv(f.Receiver); idx = f.Field.Name; break;
            case BoundTupleElement t: recv = Recv(t.Receiver); idx = t.Index.ToString(); break;
            case BoundCall c: callee = c.Function.Name; recv = Recv(c.Receiver); break;
            case BoundBuiltinCall b: callee = b.Member.ToString(); recv = Recv(b.Receiver); break;
            case BoundLocal l: callee = l.Local.Name; break;
            case BoundErrorVariant v: variant = v.Variant.Name; break;
            case BoundEnumMember m: callee = m.Member.Name; break;
            case BoundUnionNew u: variant = u.Variant.Name; break;
            case BoundEnumValues ev: callee = ev.Enum.Name; break;
            case BoundStaticArray sa: callee = sa.Const.Name; break;
            case BoundUnionField uf: variant = uf.Variant.Name; idx = uf.Index.ToString(); recv = Recv(uf.Receiver); break;
            case BoundErrorField ef: variant = ef.Variant.Name; idx = ef.Index.ToString(); recv = Recv(ef.Receiver); break;
            case BoundIndex i: recv = Recv(i.Target); fromEnd = i.FromEnd ? "t" : "f"; break;
            case BoundSlice s: recv = Recv(s.Target); fromEnd = $"{s.StartFromEnd}:{s.EndFromEnd}"; break;
            case BoundTypeRef t: callee = t.Referenced.DisplayName; break;
            case BoundModuleRef m: callee = m.Module.Name; break;
        }
        switch (node.GetType().Name)
        {
            case "BoundMethodGroup":
                callee = ((FunctionSymbol)Prop(node, "Function")!).Name;
                recv = Recv((BoundExpr?)Prop(node, "Receiver"));
                break;
            case "BoundVariantGroup":
                variant = ((UnionVariantSymbol)Prop(node, "Variant")!).Name;
                break;
            case "BoundBuiltinGroup":
                callee = (string)Prop(node, "Name")!;
                recv = Recv((BoundExpr?)Prop(node, "Receiver"));
                break;
        }
        lines.Add($"{kind}|{type}|{recv}|{callee}|{variant}|{idx}|{fromEnd}|{span}");
    }

    private static Name N(string text) => new(S(text), text);
    private static NameExpr Nx(string text) => new(new TextSpan(0, 10), N(text), []);
    private static MemberExpr Mx(string full)
    {
        var dot = full.IndexOf('.');
        var target = full[..dot];
        var member = full[(dot + 1)..];
        return new MemberExpr(new TextSpan(0, 10), new NameExpr(S(target), new Name(S(target), target), []),
            new Name(S(member), member), false);
    }
    private static TargetMemberExpr Tm(string member) => new(new TextSpan(0, 10), new Name(S(member), member));
    private static LiteralExpr Str(string text) => new(S(text), LiteralKind.String, text, null);

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    private static List<string> E(Oracle o, BoundNode? node)
    {
        var expected = new List<string>();
        DumpNode(expected, node);
        expected.AddRange(o.NewDiagnostics());
        return expected;
    }
    private static void AssertMatches(string section, List<string> expected) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, section, expected);

    private static FieldInfo CalleeOf => BinderType.GetField("_calleeOf", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static FieldInfo Narrowed => BinderType.GetField("_narrowed", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact] public void ConstantRefMatches() { var o = Oracle.Of(); var b = o.Binder(); var k = o.Module.Constants["k"]; AssertMatches("constantRef", E(o, (BoundNode)Call(b, "ConstantRef", k.Decl, k)!)); }
    [Fact] public void ConstantRefArrayMatches() { var o = Oracle.Of(); var b = o.Binder(); var arr = o.Module.Constants["arr"]; AssertMatches("constantRefArray", E(o, (BoundNode)Call(b, "ConstantRef", arr.Decl, arr)!)); }

    [Fact]
    public void ConstantLiteralSymbolMatches()
    {
        var o = Oracle.Of();
        var node = (BoundNode)Call(o.Binder(), "ConstantLiteral", N("k"), BuiltinType.Symbol, new ConstantValue("sym"))!;
        AssertMatches("constantLiteralSymbol", E(o, node));
    }

    [Fact]
    public void LookupMemberMatches()
    {
        var o = Oracle.Of();
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        foreach (var (name, section) in new[] { ("x", "lookupField"), ("twice", "lookupProperty"), ("clear", "lookupMethod"), ("nonexistent", "lookupMissing") })
        {
            var res = (Symbol?)Call(o.Binder(), "LookupMember", pt, name);
            AssertMatches(section, [res is null ? "null" : res.Name]);
        }
        var iface = o.Module.Interfaces.First(i => i.Name == "IHasArea");
        var res2 = (Symbol?)Call(o.Binder(), "LookupMember", iface.Self, "area");
        AssertMatches("lookupTypeParam", [res2 is null ? "null" : res2.Name]);
    }

    [Fact]
    public void ImplicitThisMatches()
    {
        var o = Oracle.Of();
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        foreach (var (name, fn, section) in new[] { ("x", "m", "implicitThisField"), ("twice", "m", "implicitThisProperty"), ("clear", "m", "implicitThisMethod"), ("origin", "m", "implicitThisStaticMethod"), ("x", "s", "implicitThisStaticHost") })
        {
            var fnSym = pt.Methods.First(m => m.Name == fn);
            var b = o.Binder(fnSym, pt);
            var member = (Symbol?)Call(b, "LookupMember", pt, name);
            AssertMatches(section, E(o, (BoundNode)Call(b, "BindImplicitThisMember", Nx(name), member!)!));
        }
    }

    [Fact]
    public void CheckAccessMatches()
    {
        var o = Oracle.Of();
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var b = o.Binder();
        Call(b, "CheckAccess", Accessibility.Module, null, new ModuleSymbol("other", "."), N("k"), true);
        AssertMatches("checkAccessPublicRequired", o.NewDiagnostics().ToList());
        Call(b, "CheckAccess", Accessibility.Private, pt, new ModuleSymbol("other", "."), N("x"));
        AssertMatches("checkAccessPrivate", o.NewDiagnostics().ToList());
        Call(b, "CheckAccess", Accessibility.Module, null, new ModuleSymbol("other", "."), N("x"));
        AssertMatches("checkAccessModule", o.NewDiagnostics().ToList());
        Call(b, "CheckAccess", Accessibility.Public, null, o.Module, N("x"));
        AssertMatches("checkAccessOk", o.NewDiagnostics().ToList());
    }

    [Fact]
    public void MemberDispatchMatches()
    {
        var o = Oracle.Of();
        foreach (var (full, section) in new[] {
            ("MyError.notFound", "memberErrorSetFound"), ("MyError.badVariant", "memberErrorSetUnknown"),
            ("Color.red", "memberEnumStatic"), ("Shape.point", "memberUnionVariant"),
            ("Point.origin", "memberStructStatic"), ("Point.nope", "memberStructUnknown"),
            ("Io.stdout", "memberIoStdout"), ("Hex.encode", "memberHexEncode"),
            ("Symbol.intern", "memberSymbolIntern"), ("Utf8.decode", "memberUtf8Decode"),
            ("string.from", "memberStringFrom"), ("i32.nope", "memberOtherTypeUnknown"),
        })
        {
            var b = o.Binder();
            AssertMatches(section, E(o, (BoundNode)Call(b, "BindMember", Mx(full))!));
        }
    }

    [Fact]
    public void ModuleMemberMatches()
    {
        var o = Oracle.Of();
        foreach (var (member, section) in new[] { ("k", "moduleConstant"), ("runGeneric", "moduleFunction"), ("Point", "moduleType"), ("nope", "moduleUnknown") })
        {
            var b = o.Binder();
            AssertMatches(section, E(o, (BoundNode)Call(b, "BindModuleMember", Mx("test." + member), o.Module)!));
        }
    }

    [Fact]
    public void ValueMemberStructMatches()
    {
        var o = Oracle.Of();
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        var b = o.Binder();
        var target = new BoundLocal(Nx("p"), new LocalSymbol("p", null, pt, LocalKind.Let));
        foreach (var (name, section) in new[] { ("x", "valueStructField"), ("twice", "valueStructProperty"), ("clear", "valueStructMethod") })
            AssertMatches(section, E(o, (BoundNode)Call(b, "BindValueMember", Nx(name), target, N(name))!));
    }

    [Fact]
    public void ValueMemberUnionMatches()
    {
        var o = Oracle.Of();
        var sh = o.Module.Unions.First(u => u.Name == "Shape");
        var b = o.Binder();
        var target = new BoundLocal(Nx("s"), new LocalSymbol("s", null, sh, LocalKind.Let));
        AssertMatches("valueUnionPayloadNeeds", E(o, (BoundNode)Call(b, "BindValueMember", Nx("radius"), target, N("radius"))!));
        var circle = sh.Variants.First(v => v.Name == "circle");
        var b2 = o.Binder();
        var target2 = new BoundLocal(Nx("s"), new LocalSymbol("s", null, sh, LocalKind.Let));
        ((System.Collections.IDictionary)Narrowed.GetValue(b2)!)![((BoundLocal)target2).Local] = circle;
        AssertMatches("valueUnionNarrowed", E(o, (BoundNode)Call(b2, "BindValueMember", Nx("radius"), target2, N("radius"))!));
    }

    [Fact]
    public void ValueMemberEnumOptionalBuiltinMatches()
    {
        var o = Oracle.Of();
        var b = o.Binder();
        var status = o.Module.Enums.First(e => e.Name == "Status");
        AssertMatches("valueEnumField", E(o, (BoundNode)Call(b, "BindValueMember", Nx("code"), new BoundLocal(Nx("st"), new LocalSymbol("st", null, status, LocalKind.Let)), N("code"))!));
        var opt = new OptionalType(PrimitiveType.I32);
        AssertMatches("valueOptional", E(o, (BoundNode)Call(b, "BindValueMember", Nx("value"), new BoundLocal(Nx("maybe"), new LocalSymbol("maybe", null, opt, LocalKind.Let)), N("value"))!));
        var str = PrimitiveType.String;
        AssertMatches("valueBuiltinProperty", E(o, (BoundNode)Call(b, "BindValueMember", Nx("length"), new BoundLocal(Nx("s"), new LocalSymbol("s", null, str, LocalKind.Let)), N("length"))!));
        AssertMatches("valueTake", E(o, (BoundNode)Call(b, "BindValueMember", Nx("take"), new BoundLocal(Nx("s"), new LocalSymbol("s", null, str, LocalKind.Let)), N("take"))!));
        AssertMatches("valueUnknownMatches", E(o, (BoundNode)Call(b, "BindValueMember", Nx("matches"), new BoundLocal(Nx("s"), new LocalSymbol("s", null, str, LocalKind.Let)), N("matches"))!));
    }

    [Fact]
    public void ValueMemberDynMatches()
    {
        var o = Oracle.Of();
        var iface = o.Module.Interfaces.First(i => i.Name == "IHasArea");
        var b = o.Binder();
        var dyn = new DynRefType(iface, readOnly: false);
        AssertMatches("valueDynMethod", E(o, (BoundNode)Call(b, "BindValueMember", Nx("area"), new BoundLocal(Nx("item"), new LocalSymbol("item", null, dyn, LocalKind.Let)), N("area"))!));
    }

    [Fact]
    public void ReportRenamedMatches()
    {
        var o = Oracle.Of();
        var b = o.Binder();
        AssertMatches("renamedToString", [((bool)Call(b, "ReportRenamed", PrimitiveType.I64, N("toString"))!).ToString(), .. o.NewDiagnostics()]);
        AssertMatches("renamedAdd", [((bool)Call(b, "ReportRenamed", new ListType(PrimitiveType.I32), N("add"))!).ToString(), .. o.NewDiagnostics()]);
        AssertMatches("renamedOffset", [((bool)Call(b, "ReportRenamed", PrimitiveType.String, N("offset"))!).ToString(), .. o.NewDiagnostics()]);
    }

    [Fact]
    public void MemberSuggestionMatches()
    {
        var o = Oracle.Of();
        var b = o.Binder();
        var color = o.Module.Enums.First(e => e.Name == "Color");
        AssertMatches("memberSuggestionEnum", [(string?)Call(b, "MemberSuggestion", color, "reed") ?? "null"]);
        AssertMatches("memberSuggestionBuiltin", [(string?)Call(b, "MemberSuggestion", PrimitiveType.String, "lengt") ?? "null"]);
    }

    [Fact]
    public void TargetMemberMatches()
    {
        var o = Oracle.Of();
        var b = o.Binder();
        var myErr = o.Module.ErrorSets.First(e => e.Name == "MyError");
        AssertMatches("targetErrorSetFound", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("notFound"), myErr)!));
        AssertMatches("targetErrorSetUnknown", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("nope"), myErr)!));
        var color = o.Module.Enums.First(e => e.Name == "Color");
        AssertMatches("targetEnum", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("red"), color)!));
        var sh = o.Module.Unions.First(u => u.Name == "Shape");
        AssertMatches("targetUnion", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("point"), sh)!));
        var pt = o.Module.Structs.First(s => s.Name == "Point");
        AssertMatches("targetStructStatic", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("origin"), pt)!));
        AssertMatches("targetNoTargetType", E(o, (BoundNode)Call(b, "BindTargetMember", Tm("x"), null)!));
    }

    [Fact]
    public void InterpolationMatches()
    {
        var o = Oracle.Of();
        var b = o.Binder();
        AssertMatches("interpolationLiteral", [(string)Call(b, "InterpolationSource", Str("\"hello\""))!]);
    }

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        type Expr = parser.Expr
        type MemberExpr = parser.MemberExpr
        type TargetMemberExpr = parser.TargetMemberExpr
        type NameExpr = parser.NameExpr
        type LiteralExpr = parser.LiteralExpr
        type TypeSyntax = parser.TypeSyntax

        const world: string = "const k: i64 = 42\nconst arr: i32[2] = [1, 2]\nconst msg: string = \"hello\"\n\nenum Color {\n    red\n    green\n    blue\n}\n\nenum Status(i32 code) {\n    ok(200)\n    err(500)\n}\n\nunion error MyError {\n    notFound\n    badInput\n}\n\nunion error OtherError {\n    timeout\n    badInput\n}\n\nunion Shape {\n    circle(f64 radius)\n    point\n}\n\nstruct Point {\n    i32 x = 10\n    i32 y = 20\n    i32 twice => 20\n    static Point origin() => Point { x: 0, y: 0 }\n    void clear() {\n    }\n    void m() {\n        let a = x\n        let b = twice\n        clear()\n    }\n    static void s() {\n        let p = 0\n    }\n}\n\ninterface IHasArea {\n    f64 area()\n}\n\nvoid runGeneric<T>(T item) where T : IHasArea {\n    let a = item.area()\n}\n\n// names: Io Hex Symbol Utf8 string test badVariant nope stdout encode intern decode from value length take matches toString add offset\n"

        TextSpan spanOf(string needle) {
            let p = world.position(needle) ?? 0
            return TextSpan { start: p, length: needle.length }
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

        NameExpr tNameExpr(string name) => NameExpr { span: TextSpan { start: 0, length: 10 }, name: spanOf(name), typeArgs: List<TypeSyntax>() }

        MemberExpr tMemberExpr(string target, by string member) {
            return MemberExpr { span: TextSpan { start: 0, length: 10 }, target: Expr.nameExpr(Owned(NameExpr { span: spanOf(target), name: spanOf(target), typeArgs: List<TypeSyntax>() })), member: spanOf(member), nullConditional: false, typeArgs: List<TypeSyntax>() }
        }

        TargetMemberExpr tTargetMember(string member) => TargetMemberExpr { span: TextSpan { start: 0, length: 10 }, member: spanOf(member) }

        void buildWorld(ref Ctx c) {
            let i32t = prim(.i32)
            let f64t = prim(.f64)
            let kId = newConst(ref c, by: ConstSymbol { name: "k", type: prim(.i64), state: 2, value: Constant { kind: .integer, integer: bigFromI64(42) } })
            c.modules[0].constants["k"] = kId
            c.modules[0].allConstants.append(kId)
            let arrT = internArray(ref c, by: i32t, at: 2)
            let arrId = newConst(ref c, by: ConstSymbol { name: "arr", type: arrT, state: 2, isStaticArray: true })
            c.modules[0].constants["arr"] = arrId
            c.modules[0].allConstants.append(arrId)
            let point = c.structs.length
            let xId = newField(ref c, by: FieldSymbol { name: "x", type: i32t, owner: point })
            let yId = newField(ref c, by: FieldSymbol { name: "y", type: i32t, owner: point })
            let twiceGet = newFunction(ref c, by: FunctionSymbol { name: "twice", kind: .getter, owner: point, returnType: i32t })
            let twiceProp = newProperty(ref c, by: PropertySymbol { name: "twice", type: i32t, getter: twiceGet, owner: point })
            let originFn = newFunction(ref c, by: FunctionSymbol { name: "origin", kind: .method, owner: point, isStatic: true, returnType: structTypeOf(point) })
            let clearFn = newFunction(ref c, by: FunctionSymbol { name: "clear", kind: .method, owner: point, returnType: prim(.void) })
            let mFn = newFunction(ref c, by: FunctionSymbol { name: "m", kind: .method, owner: point, returnType: prim(.void) })
            let sFn = newFunction(ref c, by: FunctionSymbol { name: "s", kind: .method, owner: point, isStatic: true, returnType: prim(.void) })
            var methods = List<FuncId>()
            methods.append(originFn)
            methods.append(clearFn)
            methods.append(mFn)
            methods.append(sFn)
            let pointId = newStruct(ref c, by: StructSymbol { name: "Point", module: 0, fields: [xId, yId], properties: [twiceProp], methods: move methods })
            c.modules[0].structs.append(pointId)
            c.modules[0].types["Point"] = structTypeOf(pointId)
            let color = c.enums.length
            let red = newEnumMember(ref c, by: EnumMemberSymbol { name: "red", owner: color, ordinal: 0 })
            let green = newEnumMember(ref c, by: EnumMemberSymbol { name: "green", owner: color, ordinal: 1 })
            let blue = newEnumMember(ref c, by: EnumMemberSymbol { name: "blue", owner: color, ordinal: 2 })
            let colorId = newEnum(ref c, by: EnumSymbol { name: "Color", members: [red, green, blue] })
            c.modules[0].enums.append(colorId)
            c.modules[0].types["Color"] = enumTypeOf(colorId)
            let status = c.enums.length
            let codeField = newEnumField(ref c, by: EnumFieldSymbol { name: "code", type: i32t, index: 0, owner: status })
            let ok = newEnumMember(ref c, by: EnumMemberSymbol { name: "ok", owner: status, ordinal: 0 })
            let err = newEnumMember(ref c, by: EnumMemberSymbol { name: "err", owner: status, ordinal: 1 })
            let statusId = newEnum(ref c, by: EnumSymbol { name: "Status", members: [ok, err], fields: [codeField] })
            c.modules[0].enums.append(statusId)
            c.modules[0].types["Status"] = enumTypeOf(statusId)
            let myErr = c.errorSets.length
            let nf = newErrorVariant(ref c, by: ErrorVariantSymbol { name: "notFound", set: myErr })
            let bi = newErrorVariant(ref c, by: ErrorVariantSymbol { name: "badInput", set: myErr })
            let myErrId = newErrorSet(ref c, by: ErrorSetSymbol { name: "MyError", variants: [nf, bi] })
            c.modules[0].errorSets.append(myErrId)
            c.modules[0].types["MyError"] = errorSetTypeOf(myErrId)
            let otherErr = c.errorSets.length
            let to = newErrorVariant(ref c, by: ErrorVariantSymbol { name: "timeout", set: otherErr })
            let bi2 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: "badInput", set: otherErr })
            let otherErrId = newErrorSet(ref c, by: ErrorSetSymbol { name: "OtherError", variants: [to, bi2] })
            c.modules[0].errorSets.append(otherErrId)
            c.modules[0].types["OtherError"] = errorSetTypeOf(otherErrId)
            let shape = c.unions.length
            let radius = newLocal(ref c, by: LocalSymbol { name: "radius", type: f64t, kind: .letKeyword })
            let circle = newUnionVariant(ref c, by: UnionVariantSymbol { name: "circle", owner: shape, fields: [radius] })
            let pointV = newUnionVariant(ref c, by: UnionVariantSymbol { name: "point", owner: shape })
            let shapeId = newUnion(ref c, by: UnionSymbol { name: "Shape", variants: [circle, pointV] })
            c.modules[0].unions.append(shapeId)
            c.modules[0].types["Shape"] = unionTypeOf(shapeId)
            let iface = c.interfaces.length
            let areaFn = newFunction(ref c, by: FunctionSymbol { name: "area", kind: .method, returnType: f64t, iface: iface })
            let ifaceId = newInterface(ref c, by: InterfaceSymbol { name: "IHasArea", methods: [areaFn] })
            c.modules[0].interfaces.append(ifaceId)
            c.modules[0].types["IHasArea"] = interfaceTypeOf(ifaceId)
            var constrs = List<InterfaceId>()
            constrs.append(ifaceId)
            let selfTp = newTypeParam(ref c, by: TypeParam { name: "Self", constraints: move constrs })
            c.interfaces[ifaceId].self = selfTp
            var rgc = List<InterfaceId>()
            rgc.append(ifaceId)
            let tParam = newTypeParam(ref c, by: TypeParam { name: "T", constraints: move rgc })
            let rg = newFunction(ref c, by: FunctionSymbol { name: "runGeneric", kind: .function, returnType: prim(.void), typeParams: [tParam], access: .publicKeyword })
            var rgList = List<FuncId>()
            rgList.append(rg)
            c.modules[0].functions["runGeneric"] = move rgList
            c.modules[0].allFunctions.append(rg)
        }

        string t18Recv(ref Ctx c, for BExpr r) {
            if r == -1 { return "-1" }
            let k = boundKind(r)
            if k == .thisRef { return "this" }
            if k == .local { return $"local:{c.locals[c.bLocals[boundIndex(r)].local].name}" }
            return $"{k}"
        }

        void tcDump(ref Ctx c, for BExpr e) {
            if e == -1 {
                print("null")
                return
            }
            let k = boundKind(e)
            let t = exprType(in c, for: e)
            let typeName = typeDisplay(ref c, by: t)
            var recv = "-"
            var callee = "-"
            var variant = "-"
            var idx = "-"
            var fromEnd = "-"
            switch k {
                case .field:
                    recv = t18Recv(ref c, for: c.bFields[boundIndex(e)].receiver)
                    idx = c.fields[c.bFields[boundIndex(e)].field].name
                case .tupleElement:
                    recv = t18Recv(ref c, for: c.bTupleElements[boundIndex(e)].receiver)
                    idx = $"{c.bTupleElements[boundIndex(e)].index}"
                case .call:
                    callee = c.functions[c.bCalls[boundIndex(e)].function].name
                    recv = t18Recv(ref c, for: c.bCalls[boundIndex(e)].receiver)
                case .builtinCall:
                    callee = $"{c.bBuiltinCalls[boundIndex(e)].member}"
                    recv = t18Recv(ref c, for: c.bBuiltinCalls[boundIndex(e)].receiver)
                case .methodGroup:
                    callee = c.functions[c.bMethodGroups[boundIndex(e)].function].name
                    recv = t18Recv(ref c, for: c.bMethodGroups[boundIndex(e)].receiver)
                case .builtinGroup:
                    callee = c.bBuiltinGroups[boundIndex(e)].name
                    recv = t18Recv(ref c, for: c.bBuiltinGroups[boundIndex(e)].receiver)
                case .local:
                    callee = c.locals[c.bLocals[boundIndex(e)].local].name
                case .errorVariant:
                    variant = c.errorVariants[c.bErrorVariants[boundIndex(e)].variant].name
                case .enumMember:
                    callee = c.enumMembers[c.bEnumMembers[boundIndex(e)].member].name
                case .unionNew:
                    variant = c.unionVariants[c.bUnionNews[boundIndex(e)].variant].name
                case .variantGroup:
                    variant = c.unionVariants[c.bVariantGroups[boundIndex(e)].variant].name
                case .enumValues:
                    callee = c.enums[c.bEnumValues[boundIndex(e)].enumId].name
                case .staticArray:
                    callee = c.consts[c.bStaticArrays[boundIndex(e)].constId].name
                case .unionField:
                    variant = c.unionVariants[c.bUnionFields[boundIndex(e)].variant].name
                    idx = $"{c.bUnionFields[boundIndex(e)].index}"
                    recv = t18Recv(ref c, for: c.bUnionFields[boundIndex(e)].receiver)
                case .errorField:
                    variant = c.errorVariants[c.bErrorFields[boundIndex(e)].variant].name
                    idx = $"{c.bErrorFields[boundIndex(e)].index}"
                    recv = t18Recv(ref c, for: c.bErrorFields[boundIndex(e)].receiver)
                case .index:
                    recv = t18Recv(ref c, for: c.bIndexes[boundIndex(e)].target)
                    fromEnd = c.bIndexes[boundIndex(e)].fromEnd ? "t" : "f"
                case .slice:
                    recv = t18Recv(ref c, for: c.bSlices[boundIndex(e)].target)
                    fromEnd = $"{c.bSlices[boundIndex(e)].startFromEnd}:{c.bSlices[boundIndex(e)].endFromEnd}"
                case .typeRef:
                    callee = typeDisplay(ref c, by: c.bTypeRefs[boundIndex(e)].referenced)
                case .moduleRef:
                    callee = c.modules[c.bModuleRefs[boundIndex(e)].module].name
                default: {}
            }
            let sp = nodeSpan(in c, for: e)
            print($"{k}|{typeName}|{recv}|{callee}|{variant}|{idx}|{fromEnd}|{sp.start}:{sp.length}")
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        string memberRefName(ref Ctx c, for MemberRef m) {
            switch m.kind {
                case .none: return "null"
                case .field: return c.fields[m.id].name
                case .propertyKeyword: return c.properties[m.id].name
                case .method: return c.functions[m.id].name
            }
        }

        i32 main() {
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
            }
            var bytes = List<u8>()
            for b in world.bytes { bytes.append(b) }
            c.files.append(SourceFile { path: "tmp/t18-cases/main.byn", displayPath: "tmp/t18-cases/main.byn", text: move bytes, unit: CompilationUnit { span: TextSpan { start: 0, length: 0 }, members: List<parser.Decl>() }, module: 0, isEntry: true, isTestFile: false, imports: Map<string, ModId>() })
            var modFiles = List<FileId>()
            modFiles.append(0)
            c.modules.append(Mod { name: "main", files: move modFiles, directory: ".", cName: "main" })
            var otherFiles = List<FileId>()
            c.modules.append(Mod { name: "other", files: move otherFiles, directory: ".", cName: "other" })
            buildWorld(ref c)
            let pointT = structTypeOf(0)
            let i32t = prim(.i32)
            let f64t = prim(.f64)
            let strT = prim(.string)

            // Section constantRef
            {
                print("== constantRef")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = constantRef(ref b, through: ref c, at: spanOf("const k: i64 = 42"), for: 0)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section constantRefArray
            {
                print("== constantRefArray")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = constantRef(ref b, through: ref c, at: spanOf("const arr: i32[2] = [1, 2]"), for: 1)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section constantLiteralSymbol
            {
                print("== constantLiteralSymbol")
                let ds = c.diagnostics.length
                let e = constantLiteral(ref c, at: spanOf("k"), as: builtinType(.symbol), by: Constant { kind: .text, text: "sym" })
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section lookupField/Property/Method/Missing/TypeParam
            {
                print("== lookupField")
                let m = lookupMember(ref c, for: pointT, by: "x")
                print(memberRefName(ref c, for: m))
            }
            {
                print("== lookupProperty")
                let m = lookupMember(ref c, for: pointT, by: "twice")
                print(memberRefName(ref c, for: m))
            }
            {
                print("== lookupMethod")
                let m = lookupMember(ref c, for: pointT, by: "clear")
                print(memberRefName(ref c, for: m))
            }
            {
                print("== lookupMissing")
                let m = lookupMember(ref c, for: pointT, by: "nonexistent")
                print(memberRefName(ref c, for: m))
            }
            {
                print("== lookupTypeParam")
                let m = lookupMember(ref c, for: typeParamTypeOf(c.interfaces[0].self), by: "area")
                print(memberRefName(ref c, for: m))
            }

            // Section implicitThis*
            {
                print("== implicitThisField")
                var b = makeBinder(file: 0, fn: 3, owner: 0, thisType: pointT)
                let ds = c.diagnostics.length
                let m = lookupMember(ref c, for: pointT, by: "x")
                let e = bindImplicitThisMember(ref b, through: ref c, for: in tNameExpr("x"), by: m)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== implicitThisProperty")
                var b = makeBinder(file: 0, fn: 3, owner: 0, thisType: pointT)
                let ds = c.diagnostics.length
                let m = lookupMember(ref c, for: pointT, by: "twice")
                let e = bindImplicitThisMember(ref b, through: ref c, for: in tNameExpr("twice"), by: m)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== implicitThisMethod")
                var b = makeBinder(file: 0, fn: 3, owner: 0, thisType: pointT)
                let ds = c.diagnostics.length
                let m = lookupMember(ref c, for: pointT, by: "clear")
                let e = bindImplicitThisMember(ref b, through: ref c, for: in tNameExpr("clear"), by: m)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== implicitThisStaticMethod")
                var b = makeBinder(file: 0, fn: 3, owner: 0, thisType: pointT)
                let ds = c.diagnostics.length
                let m = lookupMember(ref c, for: pointT, by: "origin")
                let e = bindImplicitThisMember(ref b, through: ref c, for: in tNameExpr("origin"), by: m)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== implicitThisStaticHost")
                var b = makeBinder(file: 0, fn: 4, owner: 0, thisType: pointT)
                let ds = c.diagnostics.length
                let m = lookupMember(ref c, for: pointT, by: "x")
                let e = bindImplicitThisMember(ref b, through: ref c, for: in tNameExpr("x"), by: m)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section checkAccess*
            {
                print("== checkAccessPublicRequired")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                checkAccess(ref b, through: ref c, for: .module, from: -1, by: 1, at: spanOf("k"), publicRequired: true)
                tcDiags(ref c, from: ds)
            }
            {
                print("== checkAccessPrivate")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                checkAccess(ref b, through: ref c, for: .privateKeyword, from: 0, by: 1, at: spanOf("x"))
                tcDiags(ref c, from: ds)
            }
            {
                print("== checkAccessModule")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                checkAccess(ref b, through: ref c, for: .module, from: -1, by: 1, at: spanOf("x"))
                tcDiags(ref c, from: ds)
            }
            {
                print("== checkAccessOk")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                checkAccess(ref b, through: ref c, for: .publicKeyword, from: -1, by: 0, at: spanOf("x"))
                tcDiags(ref c, from: ds)
            }

            // Section memberDispatch*
            {
                print("== memberErrorSetFound")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("MyError", by: "notFound"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberErrorSetUnknown")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("MyError", by: "badVariant"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberEnumStatic")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Color", by: "red"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberUnionVariant")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Shape", by: "point"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberStructStatic")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Point", by: "origin"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberStructUnknown")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Point", by: "nope"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberIoStdout")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Io", by: "stdout"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberHexEncode")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Hex", by: "encode"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberSymbolIntern")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Symbol", by: "intern"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberUtf8Decode")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("Utf8", by: "decode"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberStringFrom")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("string", by: "from"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== memberOtherTypeUnknown")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindMember(ref b, through: ref c, for: in tMemberExpr("i32", by: "nope"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section moduleMember*
            {
                print("== moduleConstant")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = t18BindModuleMember(ref b, through: ref c, for: tMemberExpr("test", by: "k"), to: 0)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== moduleFunction")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = t18BindModuleMember(ref b, through: ref c, for: tMemberExpr("test", by: "runGeneric"), to: 0)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== moduleType")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = t18BindModuleMember(ref b, through: ref c, for: tMemberExpr("test", by: "Point"), to: 0)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== moduleUnknown")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = t18BindModuleMember(ref b, through: ref c, for: tMemberExpr("test", by: "nope"), to: 0)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section valueStruct*
            {
                print("== valueStructField")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "p", type: pointT, kind: .letKeyword }), type: pointT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("x"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueStructProperty")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "p", type: pointT, kind: .letKeyword }), type: pointT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("twice"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueStructMethod")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "p", type: pointT, kind: .letKeyword }), type: pointT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("clear"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueUnionPayloadNeeds")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "s", type: unionTypeOf(0), kind: .letKeyword }), type: unionTypeOf(0) })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("radius"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueUnionNarrowed")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let locId = newLocal(ref c, by: LocalSymbol { name: "s", type: unionTypeOf(0), kind: .letKeyword })
                b.narrowed[locId] = 0
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: locId, type: unionTypeOf(0) })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("radius"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueEnumField")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "st", type: enumTypeOf(1), kind: .letKeyword }), type: enumTypeOf(1) })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("code"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueOptional")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let optT = internOptional(ref c, by: i32t)
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "maybe", type: optT, kind: .letKeyword }), type: optT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("value"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueBuiltinProperty")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "s", type: strT, kind: .letKeyword }), type: strT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("length"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueTake")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "s", type: strT, kind: .letKeyword }), type: strT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("take"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueUnknownMatches")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "s", type: strT, kind: .letKeyword }), type: strT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("matches"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== valueDynMethod")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let dynT = internDynRef(ref c, by: 0, readOnly: false)
                let tgt = addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 5 }, local: newLocal(ref c, by: LocalSymbol { name: "item", type: dynT, kind: .letKeyword }), type: dynT })
                let e = bindValueMember(ref b, through: ref c, at: TextSpan { start: 0, length: 10 }, for: tgt, by: spanOf("area"))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section reportRenamed*
            {
                print("== renamedToString")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let r = reportRenamed(ref b, through: ref c, for: prim(.i64), at: spanOf("toString"))
                print(r ? "True" : "False")
                tcDiags(ref c, from: ds)
            }
            {
                print("== renamedAdd")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let r = reportRenamed(ref b, through: ref c, for: internList(ref c, by: i32t), at: spanOf("add"))
                print(r ? "True" : "False")
                tcDiags(ref c, from: ds)
            }
            {
                print("== renamedOffset")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let r = reportRenamed(ref b, through: ref c, for: strT, at: spanOf("offset"))
                print(r ? "True" : "False")
                tcDiags(ref c, from: ds)
            }

            // Section memberSuggestion*
            {
                print("== memberSuggestionEnum")
                let sug = memberSuggestion(ref c, for: enumTypeOf(0), by: "reed")
                print(sug ?? "null")
            }
            {
                print("== memberSuggestionBuiltin")
                let sug = memberSuggestion(ref c, for: strT, by: "lengt")
                print(sug ?? "null")
            }

            // Section targetMember*
            {
                print("== targetErrorSetFound")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("notFound"), to: errorSetTypeOf(0))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== targetErrorSetUnknown")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("nope"), to: errorSetTypeOf(0))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== targetEnum")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("red"), to: enumTypeOf(0))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== targetUnion")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("point"), to: unionTypeOf(0))
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== targetStructStatic")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("origin"), to: pointT)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }
            {
                print("== targetNoTargetType")
                var b = makeBinder(file: 0)
                let ds = c.diagnostics.length
                let e = bindTargetMember(ref b, through: ref c, for: in tTargetMember("x"), to: -1)
                tcDump(ref c, for: e)
                tcDiags(ref c, from: ds)
            }

            // Section interpolationLiteral
            {
                print("== interpolationLiteral")
                var b = makeBinder(file: 0)
                let lit = Expr.literalExpr(Owned(LiteralExpr { span: spanOf("\"hello\""), kind: .string, text: "\"hello\"", suffix: null }))
                let s = interpolationSource(ref b, through: ref c, for: in lit)
                print(s)
            }

            return 0
        }
        """;
}
