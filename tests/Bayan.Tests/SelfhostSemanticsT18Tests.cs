using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T18: <c>selfhost/semantics/express2.byn</c> against the C# <c>BinderExpressions</c> (BinderExpressions.cs ~600-1155).
/// One source, <see cref="Source"/>, is compiled by both sides: the C# side via <c>Compilation.Create</c> plus
/// reflection on the C# <c>Binder</c>, the Bayan side via <c>loadModule</c> + <c>declare</c> in the generated main run
/// by <c>SelfhostSemanticsHarness.RunSections</c>. Each case binds one <c>let</c> initializer of that source on both
/// sides; every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT18Tests
{
    private const string Dir = "tmp/t18-cases";

    /// <summary>A second module on disk under <see cref="Dir"/>, imported by <see cref="Source"/>: the cross-module access checks.</summary>
    private const string Other = """
        const oc: i32 = 1

        void hiddenFn() {
        }

        private void privFn() {
        }
        """;

    private const string Source = """
        import other

        const k: i64 = 42
        const arr: i32[2] = [1, 2]
        const sym: Symbol = "b"
        const bad: i64 = nope

        enum Color {
            red
            green
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
            notFound
            other
        }

        union error Both = MyError, OtherError

        union error NoBad = MyError, OtherError except .badInput

        union Shape {
            circle(f64 radius)
            point
        }

        union Blob {
            empty
            full(i32 size)
        }

        struct Point {
            i32 x = 10
            i32 y = 20
            private i32 hidden = 0
            i32 twice => 20
            static Point origin() => Point { x: 0, y: 0 }
            void clear() {
            }
            void m() {
                let a = x
                let b = twice
                let c = clear
                let d = origin
            }
            static void s() {
                let e = x
            }
        }

        interface IHasArea {
            f64 area()
        }

        struct Box<T> where T : IHasArea {
            T v
            T first => v
            void g() {
                let g1 = this.v.nope
                let l1 = nope
                let r1 = first
            }
        }

        struct Bag<U> {
            U w
            void h() {
                let h1 = this.w.nope
            }
        }

        struct Fd {
            i32 a = 1
            i32 b = a
        }

        void probes(Point p, Shape s, string str, List<i32> list, i32[2] fixed, Map<string, i32> map, Point? maybe, Status st, dyn IHasArea d, i64 n, (i32 a, i32 b) tup, (Shape a, i32 b) ts, dyn ref IHasArea dr, Owned<Point> op, StrBuf sb, Span<i32> sp, u64 un, Blob bl, MyError me, Untrusted<i32> ui) {
            let m1 = MyError.notFound
            let m2 = MyError.badVariant
            let m3 = Color.red
            let m4 = Shape.point
            let m5 = Point.origin
            let m6 = Point.nope
            let m7 = Io.stdout
            let m8 = string.from
            let m9 = i32.nope
            let m10 = p?.x
            let m11 = nope.x
            let m12 = Color.blue
            let m13 = Hex.encode
            let m14 = Symbol.intern
            let m15 = Utf8.decode
            let m16 = string.tryFrom
            let m17 = string.nope
            let m18 = Color.count
            let m19 = Color.values
            let m20 = Color.fromOrdinal
            let m21 = Color.parse
            let m22 = Io.nope
            let m23 = Hex.nope
            let m24 = Symbol.nope
            let m25 = Utf8.nope
            let m26 = string.invalidUtf8At
            let m27 = string.invalidUtf8
            let m28 = Shape.nope
            let m29 = Shape.circle
            let m30 = Point.x
            let m31 = Point.twice
            let m32 = Point.clear
            let m33 = Io.readFile
            let m34 = Io.stderr
            let m35 = Io.stdin
            let m36 = Hex.decode
            let x1 = other.oc
            let x2 = other.hiddenFn
            let x3 = other.privFn
            let u1 = main.k
            let u2 = main.probes
            let u3 = main.Point
            let u4 = main.nope
            let u5 = main.sym
            let u6 = main.bad
            let v1 = p.x
            let v2 = p.twice
            let v3 = p.clear
            let v4 = p.hidden
            let v5 = s.radius
            let v6 = st.code
            let v7 = maybe.x
            let v9 = str.matches
            let v10 = d.area
            let v11 = p.nope
            let v12 = list.add
            let v14 = n.toString
            let v15 = str.offset
            let v16 = fixed.toList
            let v17 = tup.a
            let v19 = n.toHex
            let v20 = n.toBinary
            let v21 = ts.a.radius
            let v22 = dr.nope
            let v23 = op.x
            let v25 = p.take
            let v26 = sb.asString
            let v27 = list.toList
            let v28 = sp.toList
            let v29 = Shape.point.radius
            let v30 = bl.size
            let v31 = list.removeRange
            let v32 = list.replaceRange
            let v33 = map.getOr
            let v34 = fixed.asSpan
            let v35 = str.toUtf16
            let v36 = n.toOctal
            let v37 = me.x
            let v38 = MyError.notFound.x
            let v39 = p.size
            let v40 = st.cod
            let v41 = p.length
            let v42 = ui.length
            let t1: Color = .red
            let t2: MyError = .notFound
            let t3: MyError = .nope
            let t4: Shape = .point
            let t5: Point = .origin
            let t6 = .x
            let t7: Color? = .green
            let t8: Color = .blue
            let t9: Both = .notFound
            let t10: Both = .zzz
            let t11: Both = .other
            let t12: () -> void = .x
            let t13: Point = .clear
            let t14: NoBad = .badInput
            let i1 = str[0]
            let i2 = list[0]
            let i3 = fixed[^1]
            let i4 = fixed[5]
            let i5 = map["a"]
            let i6 = n[0]
            let i7 = list[str]
            let i8 = str[1..2]
            let i9 = nope[0]
            let i10 = str[^2..]
            let i11 = list[1..^1]
            let i12 = fixed[..1]
            let i13 = str[1..=2]
            let i14 = list[un]
            let s1 = $"a{n}b{{c}}"
            let s2 = "plain"
            let s3 = "a{b}"
            let s4 = n
        }
        """;

    /// <summary>(op, declaration, member or "-", local): bind the initializer of <c>let local</c> in that body; member "=" binds the default of field <c>local</c>.</summary>
    public static readonly (string Op, string Decl, string Member, string Name)[] Cases =
    [
        ("this", "Point", "m", "a"), ("this", "Point", "m", "b"), ("this", "Point", "m", "c"), ("this", "Point", "m", "d"),
        ("this", "Point", "s", "e"), ("this", "Fd", "=", "b"),
        ("member", "Box", "g", "g1"), ("member", "Bag", "h", "h1"), ("lookup", "Box", "g", "l1"), ("rtt", "Box", "g", "r1"),
        ("member", "probes", "-", "m1"), ("member", "probes", "-", "m2"), ("member", "probes", "-", "m3"),
        ("member", "probes", "-", "m4"), ("member", "probes", "-", "m5"), ("member", "probes", "-", "m6"),
        ("member", "probes", "-", "m7"), ("member", "probes", "-", "m8"), ("member", "probes", "-", "m9"),
        ("member", "probes", "-", "m10"), ("member", "probes", "-", "m11"), ("member", "probes", "-", "m12"),
        ("member", "probes", "-", "m13"), ("member", "probes", "-", "m14"), ("member", "probes", "-", "m15"),
        ("member", "probes", "-", "m16"), ("member", "probes", "-", "m17"),
        ("member", "probes", "-", "m18"), ("member", "probes", "-", "m19"), ("member", "probes", "-", "m20"), ("member", "probes", "-", "m21"), ("member", "probes", "-", "m22"), ("member", "probes", "-", "m23"), ("member", "probes", "-", "m24"), ("member", "probes", "-", "m25"), ("member", "probes", "-", "m26"), ("member", "probes", "-", "m27"), ("member", "probes", "-", "m28"), ("member", "probes", "-", "m29"), ("member", "probes", "-", "m30"), ("member", "probes", "-", "m31"), ("member", "probes", "-", "m32"), ("member", "probes", "-", "m33"), ("member", "probes", "-", "m34"), ("member", "probes", "-", "m35"), ("member", "probes", "-", "m36"), ("member", "probes", "-", "x1"), ("member", "probes", "-", "x2"), ("member", "probes", "-", "x3"),
        ("module", "probes", "-", "u1"), ("module", "probes", "-", "u2"), ("module", "probes", "-", "u3"),
        ("module", "probes", "-", "u4"), ("module", "probes", "-", "u5"), ("module", "probes", "-", "u6"),
        ("member", "probes", "-", "v1"), ("member", "probes", "-", "v2"), ("member", "probes", "-", "v3"),
        ("member", "probes", "-", "v4"), ("member", "probes", "-", "v5"), ("member", "probes", "-", "v6"),
        ("member", "probes", "-", "v7"), ("member", "probes", "-", "v9"),
        ("member", "probes", "-", "v10"), ("member", "probes", "-", "v11"), ("member", "probes", "-", "v12"),
        ("member", "probes", "-", "v14"), ("member", "probes", "-", "v15"), ("member", "probes", "-", "v16"),
        ("member", "probes", "-", "v17"), ("member", "probes", "-", "v19"),
        ("member", "probes", "-", "v20"), ("member", "probes", "-", "v21"),
        ("member", "probes", "-", "v22"),
        ("member", "probes", "-", "v23"), ("member", "probes", "-", "v25"), ("member", "probes", "-", "v26"), ("member", "probes", "-", "v27"), ("member", "probes", "-", "v28"), ("member", "probes", "-", "v29"), ("member", "probes", "-", "v30"), ("member", "probes", "-", "v31"), ("member", "probes", "-", "v32"), ("member", "probes", "-", "v33"), ("member", "probes", "-", "v34"), ("member", "probes", "-", "v35"), ("member", "probes", "-", "v36"), ("member", "probes", "-", "v37"), ("member", "probes", "-", "v38"), ("member", "probes", "-", "v39"), ("member", "probes", "-", "v40"), ("member", "probes", "-", "v41"), ("member", "probes", "-", "v42"),
        ("target", "probes", "-", "t1"), ("target", "probes", "-", "t2"), ("target", "probes", "-", "t3"),
        ("target", "probes", "-", "t4"), ("target", "probes", "-", "t5"), ("target", "probes", "-", "t6"),
        ("target", "probes", "-", "t7"), ("target", "probes", "-", "t8"),
        ("target", "probes", "-", "t9"), ("target", "probes", "-", "t10"), ("target", "probes", "-", "t11"), ("target", "probes", "-", "t12"), ("target", "probes", "-", "t13"), ("target", "probes", "-", "t14"),
        ("index", "probes", "-", "i1"), ("index", "probes", "-", "i2"), ("index", "probes", "-", "i3"),
        ("index", "probes", "-", "i4"), ("index", "probes", "-", "i5"), ("index", "probes", "-", "i6"),
        ("index", "probes", "-", "i7"), ("index", "probes", "-", "i8"), ("index", "probes", "-", "i9"),
        ("index", "probes", "-", "i10"), ("index", "probes", "-", "i11"), ("index", "probes", "-", "i12"), ("index", "probes", "-", "i13"), ("index", "probes", "-", "i14"),
        ("interp", "probes", "-", "s1"), ("interp", "probes", "-", "s2"), ("interp", "probes", "-", "s3"), ("interp", "probes", "-", "s4"),
    ];

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

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
        BoundCall => "call", BoundBuiltinCall => "builtinCall", BoundIndex => "index", BoundSlice => "slice",
        BoundErrorVariant => "errorVariant", BoundEnumMember => "enumMember", BoundUnionNew => "unionNew",
        BoundEnumValues => "enumValues", BoundStaticArray => "staticArray", BoundErrorExpr => "errorExpr",
        BoundTypeRef => "typeRef", BoundModuleRef => "moduleRef",
        _ => char.ToLowerInvariant(node.GetType().Name[5]) + node.GetType().Name[6..],
    };

    private static string Recv(BoundExpr? r) => r switch
    {
        null => "-1", BoundThis => "this", BoundLocal l => "local:" + l.Local.Name, _ => NodeKindName(r),
    };

    private static object? Prop(BoundNode node, string name) => node.GetType().GetProperty(name)?.GetValue(node);

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>A child expression: kind:type, a literal's value, a conversion's kind and operand.</summary>
    private static string Sub(BoundExpr? e) => e switch
    {
        null => "-",
        BoundLiteral l => $"literal:{l.Type.DisplayName}={l.Value}",
        BoundConversion v => $"conversion:{v.Type.DisplayName}({Camel(v.Kind.ToString())} {Sub(v.Operand)})",
        _ => $"{NodeKindName(e)}:{e.Type.DisplayName}",
    };

    /// <summary>kind|type|receiver|callee|variant|index|fromEnd|span|sub: every field a port bug could change.</summary>
    private static string DumpNode(BoundNode? node)
    {
        if (node is null) return "null";
        var type = node is BoundExpr e ? e.Type.DisplayName : "-";
        var span = $"{node.Syntax.Span.Start}:{node.Syntax.Span.Length}";
        var recv = "-"; var callee = "-"; var variant = "-"; var idx = "-"; var fromEnd = "-"; var sub = "-";
        switch (node)
        {
            case BoundField f: recv = Recv(f.Receiver); idx = f.Field.Name; break;
            case BoundTupleElement t: recv = Recv(t.Receiver); idx = t.Index.ToString(); break;
            case BoundCall c: callee = c.Function.Name; recv = Recv(c.Receiver); break;
            case BoundBuiltinCall b: callee = char.ToLowerInvariant(b.Member.ToString()[0]) + b.Member.ToString()[1..]; recv = Recv(b.Receiver); sub = string.Join(",", b.Arguments.Select(Sub)); break;
            case BoundLocal l: callee = l.Local.Name; break;
            case BoundErrorVariant v: variant = v.Variant.Name; break;
            case BoundEnumMember m: callee = m.Member.Name; break;
            case BoundUnionNew u: variant = u.Variant.Name; break;
            case BoundEnumValues ev: callee = ev.Enum.Name; break;
            case BoundStaticArray sa: callee = sa.Const.Name; break;
            case BoundUnionField uf: variant = uf.Variant.Name; idx = uf.Index.ToString(); recv = Recv(uf.Receiver); break;
            case BoundErrorField ef: variant = ef.Variant.Name; idx = ef.Index.ToString(); recv = Recv(ef.Receiver); break;
            case BoundIndex i: recv = Recv(i.Target); fromEnd = i.FromEnd ? "t" : "f"; sub = Sub(i.Index); break;
            case BoundSlice s: recv = Recv(s.Target); fromEnd = $"{(s.StartFromEnd ? "true" : "false")}:{(s.EndFromEnd ? "true" : "false")}"; sub = $"{Sub(s.Start)};{Sub(s.End)};{(s.Inclusive ? "true" : "false")}"; break;
            case BoundTypeRef t: callee = t.Referenced.DisplayName; break;
            case BoundModuleRef m: callee = m.Module.Name; break;
            case BoundLiteral l: sub = l.Value.ToString(); break;
            case BoundSymbolLiteral sl: sub = sl.Text; break;
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
        return $"{NodeKindName(node)}|{type}|{recv}|{callee}|{variant}|{idx}|{fromEnd}|{span}|{sub}";
    }

    /// <summary>The C# lines for one case: the node dump (or the string) then the diagnostics it reported.</summary>
    private static List<string> Expected(string op, string decl, string member, string name)
    {
        WriteOther();
        var path = Path.Combine(Dir, "main.byn");
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        var mark = c.Diagnostics.Items.Count;
        var unit = file.Tree.Root;
        FunctionDecl fd;
        object b;
        StructSymbol? owner = null;
        Expr init;
        TypeSyntax? ldType = null;
        if (member == "-")
        {
            fd = unit.Members.OfType<FunctionDecl>().First(f => f.Name.Text == decl);
            b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [c, file, null, null, null, null], null)!;
            foreach (var p in fd.Params) Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), LocalKind.Parameter);
        }
        else if (member == "=")
        {
            // The field-default binder: `new Binder(this, s.File, null, s)` (Compilation.cs:1383).
            var sd = unit.Members.OfType<StructDecl>().First(s => s.Name.Text == decl);
            owner = file.Module.Structs.First(s => s.Name == decl);
            b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [c, file, null, owner, null, null], null)!;
            init = sd.Members.OfType<FieldDecl>().First(f => f.Name.Text == name).Default!;
            goto bind;
        }
        else
        {
            var sd = unit.Members.OfType<StructDecl>().First(s => s.Name.Text == decl);
            fd = sd.Members.OfType<FunctionDecl>().First(f => f.Name.Text == member);
            owner = file.Module.Structs.First(s => s.Name == decl);
            var fn = owner.Methods.First(m => m.Name == member);
            b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [c, file, fn, owner, null, null], null)!;
        }
        var ld = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name);
        init = ld.Init!;
        ldType = ld.Type;
    bind:
        var lines = new List<string>();
        switch (op)
        {
            case "this":
                var n = (NameExpr)init;
                lines.Add(DumpNode((BoundNode?)Call(b, "BindImplicitThisMember", n, Call(b, "LookupMember", owner, n.Name.Text))));
                break;
            case "member": lines.Add(DumpNode((BoundNode?)Call(b, "BindMember", init))); break;
            case "module": lines.Add(DumpNode((BoundNode?)Call(b, "BindModuleMember", init, file.Module))); break;
            case "target":
                var expected = ldType is null ? null : Call(b, "ResolveType", ldType);
                lines.Add(DumpNode((BoundNode?)Call(b, "BindTargetMember", init, expected)));
                break;
            case "index": lines.Add(DumpNode((BoundNode?)Call(b, "BindIndex", init))); break;
            case "interp": lines.Add((string)Call(b, "InterpolationSource", init)!); break;
            case "lookup":
                var sym = (Symbol?)Call(b, "LookupMember", owner!.FindField("v")!.Type, ((NameExpr)init).Name.Text);
                lines.Add(sym switch { null => "null", FieldSymbol f => "field:" + f.Name, PropertySymbol p => "propertyKeyword:" + p.Name, _ => "method:" + sym.Name });
                break;
            case "rtt":
                var getter = owner!.FindProperty(((NameExpr)init).Name.Text)!.Getter;
                lines.Add(((TypeSymbol)Call(b, "ReturnTypeThrough", owner.FindField("v")!.Type, getter)!).DisplayName);
                break;
        }
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
    }

    private static void WriteOther()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "other"));
        File.WriteAllText(Path.Combine(Dir, "other", "other.byn"), Other + "\n");
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        WriteOther();
        return SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases.SelectMany(k => new[] { k.Op, k.Decl, k.Member, k.Name })]);
    });

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add($"{k.Op}.{k.Decl}.{k.Member}.{k.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string section)
    {
        var parts = section.Split('.');
        SelfhostSemanticsHarness.AssertSection(Sections.Value, section, Expected(parts[0], parts[1], parts[2], parts[3]));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        string t18Recv(ref Ctx c, for BExpr r) {
            if r == -1 { return "-1" }
            let k = boundKind(r)
            if k == .thisRef { return "this" }
            if k == .local { return $"local:{c.locals[c.bLocals[boundIndex(r)].local].name}" }
            return $"{k}"
        }

        string tcSub(ref Ctx c, for BExpr e) {
            if e == -1 { return "-" }
            let k = boundKind(e)
            let typeName = typeDisplay(ref c, by: exprType(in c, for: e))
            if k == .literal { return $"literal:{typeName}={constantText(in c.bLiterals[boundIndex(e)].value)}" }
            if k == .conversion { return $"conversion:{typeName}({c.bConversions[boundIndex(e)].kind} {tcSub(ref c, for: c.bConversions[boundIndex(e)].operand)})" }
            return $"{k}:{typeName}"
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
            var sub = "-"
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
                    var joined = ""
                    var first = true
                    for a in copyIds(in c.bBuiltinCalls[boundIndex(e)].arguments) {
                        joined = first ? tcSub(ref c, for: a) : $"{joined},{tcSub(ref c, for: a)}"
                        first = false
                    }
                    sub = joined
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
                    sub = tcSub(ref c, for: c.bIndexes[boundIndex(e)].index)
                case .slice:
                    recv = t18Recv(ref c, for: c.bSlices[boundIndex(e)].target)
                    fromEnd = $"{c.bSlices[boundIndex(e)].startFromEnd}:{c.bSlices[boundIndex(e)].endFromEnd}"
                    sub = $"{tcSub(ref c, for: c.bSlices[boundIndex(e)].start)};{tcSub(ref c, for: c.bSlices[boundIndex(e)].end)};{c.bSlices[boundIndex(e)].inclusive}"
                case .typeRef:
                    callee = typeDisplay(ref c, by: c.bTypeRefs[boundIndex(e)].referenced)
                case .moduleRef:
                    callee = c.modules[c.bModuleRefs[boundIndex(e)].module].name
                case .literal:
                    sub = constantText(in c.bLiterals[boundIndex(e)].value)
                case .symbolLiteral:
                    sub = c.bSymbolLiterals[boundIndex(e)].text
                default: {}
            }
            let sp = nodeSpan(in c, for: e)
            print($"{k}|{typeName}|{recv}|{callee}|{variant}|{idx}|{fromEnd}|{sp.start}:{sp.length}|{sub}")
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        string t18MemberRef(ref Ctx c, for MemberRef r) {
            switch r.kind {
                case .none: return "null"
                case .field: return $"field:{c.fields[r.id].name}"
                case .propertyKeyword: return $"propertyKeyword:{c.properties[r.id].name}"
                default: return $"method:{c.functions[r.id].name}"
            }
        }

        void runInit(ref Binder b, through ref Ctx c, for in parser.Expr init, from in parser.TypeSyntax declType, by string op) {
            if op == "interp" {
                print(interpolationSource(ref b, through: ref c, for: in init))
                return
            }
            switch init {
                case .nameExpr(let n):
                    let nText = t18Text(in c, from: b.file, at: n.value.name)
                    if op == "this" {
                        let member = lookupMember(ref c, for: b.thisType, by: nText)
                        tcDump(ref c, for: bindImplicitThisMember(ref b, through: ref c, for: in n.value, by: member))
                    }
                    if op == "lookup" {
                        let vType = c.fields[findField(ref c, by: b.owner, for: "v")].type
                        print(t18MemberRef(ref c, for: lookupMember(ref c, for: vType, by: nText)))
                    }
                    if op == "rtt" {
                        let vType = c.fields[findField(ref c, by: b.owner, for: "v")].type
                        let getter = c.properties[findProperty(ref c, by: b.owner, for: nText)].getter
                        print(typeDisplay(ref c, by: returnTypeThrough(ref b, through: ref c, for: vType, by: getter)))
                    }
                case .memberExpr(let m):
                    if op == "member" { tcDump(ref c, for: bindMember(ref b, through: ref c, for: in m.value)) }
                    if op == "module" { tcDump(ref c, for: t18BindModuleMember(ref b, through: ref c, for: m.value, to: c.files[b.file].module)) }
                case .targetMemberExpr(let t):
                    var expected: Type = -1
                    switch declType {
                        case .none: {}
                        default: expected = resolveType(ref b, through: ref c, for: in declType)
                    }
                    tcDump(ref c, for: bindTargetMember(ref b, through: ref c, for: in t.value, to: expected))
                case .indexExpr(let i):
                    tcDump(ref c, for: bindIndex(ref b, through: ref c, for: in i.value))
                default:
                    print("unexpected initializer")
            }
        }

        void runBody(ref Binder b, through ref Ctx c, for in parser.Stmt body, by string op, at string name) {
            switch body {
                case .blockStmt(let blk):
                    for s in blk.value.statements {
                        switch s {
                            case .localDecl(let ld):
                                if t18Text(in c, from: b.file, at: ld.value.name) == name { runInit(ref b, through: ref c, for: in ld.value.init, from: in ld.value.type, by: op) }
                            default: {}
                        }
                    }
                default: {}
            }
        }

        void runCase(ref Ctx c, for string op, from string decl, at string member, by string name) {
            let file = c.modules[c.entryModule].files[0]
            var b = Binder {
                file: file,
                fn: -1,
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
            pushScope(ref b)
            // The C# `Compilation.Create` has bound every module constant before the case runs.
            var consts = List<ConstId>()
            for (_, k) in c.modules[c.entryModule].constants { consts.append(k) }
            for k in consts { bindConstant(ref c, for: k) }
            let start = c.diagnostics.length
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if member == "-" && t18Text(in c, from: file, at: fd.value.name) == decl {
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: .parameter)
                            }
                            runBody(ref b, through: ref c, for: in fd.value.body, by: op, at: name)
                        }
                    case .structDecl(let sd):
                        if member != "-" && t18Text(in c, from: file, at: sd.value.name) == decl {
                            let owner = typeIndex(c.modules[c.entryModule].types[decl])
                            for d in sd.value.members {
                                switch d {
                                    case .fieldDecl(let fdl):
                                        // The field-default binder: no function, `this` is the struct (Compilation.cs:1383).
                                        if member == "=" && t18Text(in c, from: file, at: fdl.value.name) == name {
                                            b.owner = owner
                                            b.thisType = c.modules[c.entryModule].types[decl]
                                            runInit(ref b, through: ref c, for: in fdl.value.defaultKeyword, from: in fdl.value.type, by: op)
                                        }
                                    case .functionDecl(let fd):
                                        if t18Text(in c, from: file, at: fd.value.name) == member {
                                            for f in c.structs[owner].methods {
                                                if c.functions[f].name == member { b.fn = f }
                                            }
                                            b.owner = owner
                                            b.thisType = c.modules[c.entryModule].types[decl]
                                            runBody(ref b, through: ref c, for: in fd.value.body, by: op, at: name)
                                        }
                                    default: {}
                                }
                            }
                        }
                    default: {}
                }
            }
            tcDiags(ref c, from: start)
        }

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 3 < args.length {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t18-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t18-cases",
                    displayRoot: "tmp/t18-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t18-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {args[i]}.{args[i + 1]}.{args[i + 2]}.{args[i + 3]}")
                runCase(ref c, for: args[i], from: args[i + 1], at: args[i + 2], by: args[i + 3])
                i += 4
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source));
}
