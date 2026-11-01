using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T19: <c>selfhost/semantics/express3.byn</c> against the C# <c>BinderExpressions</c> (BinderExpressions.cs ~1156-1640).
/// One source, <see cref="Source"/>, is compiled by both sides: the C# side via <c>Compilation.Create</c> plus
/// reflection on the C# <c>Binder</c>, the Bayan side via <c>loadModule</c> + <c>declare</c> in the generated main run
/// by <c>SelfhostSemanticsHarness.RunSections</c>. Each case runs one T19 function on one <c>let</c> of that source on
/// both sides; every expected line is computed from the C#. Not compared: the value a struct literal or a <c>with</c>
/// that passes every check returns (ops <c>structd</c>, <c>withd</c> compare only the diagnostics), because the C#
/// <c>Checked</c> is the T21 stub <c>checkedValue</c> (<c>selfhost/semantics/calls2.byn</c>:39, <c>=&gt; -1</c>).
/// </summary>
public sealed class SelfhostSemanticsT19Tests
{
    private const string Dir = "tmp/t19-cases";

    private const string Source = """"
        union error MyError {
            notFound
            badInput(i32 code)
        }

        union error OtherError {
            other
        }

        union error Both = MyError, OtherError

        const kc: i32 = 5

        union error Bad {
            plain
            withList(List<i32> xs)
        }

        union error BadBoth = Bad, OtherError

        union error Rec {
            leaf
            nest(Owned<Rec> inner)
        }

        enum Color {
            red
            green
        }

        union Shape {
            circle(f64 radius)
            point
        }

        struct Holder {
            List<i32> xs
        }

        union Bag {
            full(List<i32> xs)
            empty
        }

        union Tree {
            leaf
            node(Owned<Tree> next)
        }

        struct Point {
            i32 x = 10
            i32 y = 20
        }

        struct Req {
            required i32 a
            i32 b
            i32 c = 3
        }

        struct One {
            required i32 a
        }

        struct Ratio {
            i64 num
            i64 den

            // No parameters: T38's `declareSignature` is a stub, so a Bayan constructor has none (Q612).
            public Ratio() {
                this.num = 1
                this.den = 1
            }
        }

        struct Node {
            i32 v
            Owned<Node>? next
        }

        void gen<T>(T x, T? ox, i32 k) {
            let g1 = -x
            let g2 = x + x
            let g3 = ox < ox
            let g4 = x == x
            let g5 = k + x
            let e1: T = x
        }

        void probes(i32 a, i32 b, u32 u, i64 n, f64 f, bool flag, string str, rune ch, i32? oi, Point p, Point? op, Untrusted<i32> ui, MyError me, u8 small, Symbol sym, Bad bad, BadBoth bb, i64? on) {
            let u1 = -5
            let u2: i8 = -200
            let u3 = ^1
            let u4 = !flag
            let u5 = !a
            let u6 = -u
            let u7 = ~a
            let u8v = +f
            let u9 = -(a)
            let u10 = -nope
            let u11 = ~5
            let u12: i8 = -(-128)
            let u13 = -1.5
            let u14 = !true
            let u15 = +str
            let u16: i64 = -5
            let u17 = -flag
            let b1 = a + b
            let b2 = 1 + 2
            let b3 = flag && flag
            let b4 = true && false
            let b5 = oi == null
            let b6 = null != oi
            let b7 = a == null
            let b8 = a << 2
            let b9 = f << 1
            let b10 = "a" + "b"
            let b11 = str + str
            let b12 = a + u
            let b13 = ch + a
            let b14 = 1 / 0
            let b15 = 2147483647 + 1
            let b16 = a < b
            let b17 = flag < flag
            let b18 = p == p
            let b19 = oi ?? 0
            let b20 = a ?? 0
            let b21 = nope ?? 1
            let b22 = oi ?? oi
            let b23 = ui < 3
            let b24 = 1 + a
            let b25 = "x" == str
            let b26 = MyError.notFound == me
            let b27 = nope + 1
            let b28 = 7 % 0
            let b29: i64 = 3 * 4
            let b30 = n + u
            let b31 = u + n
            let b32 = flag || true
            let b33 = 1 << 2
            let b34 = a < str
            let b35: i64? = oi ?? 1
            let b36 = flag & flag
            let b37 = small +% small
            let b38 = oi != null && oi > 0
            let b39 = oi == null || oi > 0
            let b40 = nope + nada
            let b41 = 1 + f
            let b42 = 1 + small
            let b43 = 300 + small
            let b44 = small + 300
            let b45 = "a" + str
            let b46 = Bad.plain == bad
            let b47 = bad == bad
            let b48 = Bad.plain < bad
            let b49 = "x" == sym
            let b50 = ("x") == sym
            let b51 = (str) == sym
            let b52 = """x""" == sym
            let b53 = """a""" + "b"
            let b54 = ("x") == str
            let b55 = kc + small
            let b56 = small + kc
            let b57 = (flag ? 1 : 2) == small
            let b58 = bb == Bad.plain
            let b60: i64 = null ?? 5
            let b61 = oi ?? on
            let r1 = 1..10
            let r2 = a..=b
            let r3 = 1..n
            let r4 = f..f
            let r5 = nope..1
            let r6 = ..5
            let r7 = 'a'..'z'
            let r8 = a..n
            let r9 = 1..
            let r10 = (flag ? 1 : 2)..n
            let r11: i64 = 1..2
            let i1 = a in 1..=5
            let i2 = a in str
            let i3 = f in 1..2
            let i4 = ch in 'a'..='z'
            let i5 = nope in 1..2
            let i6 = n in a..b
            let i7 = a in nope
            let i8 = a in str..str
            let i9 = small in 1..300
            let c1 = flag ? 1 : 2
            let c2 = true ? 1 : 2
            let c3 = flag ? 1 : str
            let c4 = flag ? null : null
            let c5: i64 = flag ? a : 2
            let c6 = flag ? oi : null
            let c7 = false ? a : b
            let c8 = flag ? small : 1
            let c9: i64? = flag ? a : 2
            let c10: string = flag ? 1 : 2
            let k1 = (u8)300
            let k2 = (u8)a
            let k3 = (string)a
            let k4 = (i32)a
            let k5 = (f64)5
            let k6 = (Nope)a
            let k7 = (u8)-1
            let k8 = (i32?)null
            let k9 = (i64)a
            let k10 = (u8)nope
            let k11 = (u8)n
            let v1: u8 = u8(a)
            let v2: u8 = u8(300)
            let v3: string = string(a)
            let s1 = Point { x: 1 }
            let s2 = Point { x: 1, x: 2 }
            let s3 = Point { z: 1 }
            let s4 = Req { c: 1 }
            let s5 = { x: 1 }
            let s6 = Color { x: 1 }
            let s7 = Ratio { num: 1, den: 0 }
            let s8 = Nope { x: 1 }
            let s9: Point? = { x: 1 }
            let s10 = One { }
            let s11 = Req { a: 1 }
            let s12: Point = { y: 2 }
            let s13 = Point { x: nope }
            let s14 = Req { b: 1, q: 2 }
            let w1 = p with { x: 1 }
            let w2 = a with { x: 1 }
            let w3 = p with { nope: 1 }
            let w4 = p with { x: 1, x: 2 }
            let w5 = nope with { x: 1 }
            let w6 = p with { x: str }
            let f1 = ui
            let f2 = a
            let f3 = nope
            let e1: Point = p
            let e2: List<i32> = p
            let e3: MyError = p
            let e4: Shape = p
            let e5: (i32, string) = p
            let e6: i32[2] = p
            let e7: Untrusted<i32> = p
            let e8: Owned<Point> = p
            let e9: Both = p
            let e10: Color = p
            let e11: Symbol = p
            let e12: f64? = p
            let e13: Node = p
            let e14: Map<string, i32> = p
            let e15: (Point, List<i32>) = p
            let e16: ReadOnlySpan<i32> = p
            let e17: never = p
            let e18: void = p
            let e19: Result<i32, Bad> = p
            let e20: Rec = p
            let e21: BadBoth = p
            let e22: Holder = p
            let e23: Bag = p
            let e24: Tree = p
            let o1: string = a < b
            let o2: bool = a & b
            let o3: rune = a < b
            let o4: Symbol = a < b
            let o5: f64 = a +% b
            let o6: Point = a == b
            let o7: i32 = a ?? b
            let o8: f64 = a - b
            let o9: bool = a ^ b
            let o10: u8 = a *| b
            let o11: List<i32> = a != b
            let t1 = a + b
            let t2 = a - b
            let t3 = a * b
            let t4 = a / b
            let t5 = a % b
            let t6 = a +% b
            let t7 = a -% b
            let t8 = a *% b
            let t9 = a +| b
            let t10 = a -| b
            let t11 = a *| b
            let t12 = a << b
            let t13 = a >> b
            let t14 = a < b
            let t15 = a > b
            let t16 = a <= b
            let t17 = a >= b
            let t18 = a == b
            let t19 = a != b
            let t20 = a & b
            let t21 = a | b
            let t22 = a ^ b
            let t23 = a && b
            let t24 = a || b
            let t25 = a ?? b
            let d1 = 7 / 0
            let d2 = a + b
            let d3 = 3 * 4
            let d4 = 2147483647 * 2
            let d5 = 7 % 0
            let d6 = 7 / 2
            let d7 = 5 - a
            let d8 = 1.5 + 0
        }

        void pend1() {
            var v = 1
            let w: i64 = v + 1
        }

        void pend2() {
            var v = 1
            let w: i64 = 1 + v
        }
        """";

    private static readonly string[] ProbeNames =
    [
        "u1", "u2", "u3", "u4", "u5", "u6", "u7", "u8v", "u9", "u10", "u11", "u12", "u13", "u14", "u15", "u16", "u17",
        "b1", "b2", "b3", "b4", "b5", "b6", "b7", "b8", "b9", "b10", "b11", "b12", "b13", "b14", "b15", "b16", "b17", "b18",
        "b19", "b20", "b21", "b22", "b23", "b24", "b25", "b26", "b27", "b28", "b29", "b30", "b31", "b32", "b33", "b34", "b35",
        "b36", "b37", "b38", "b39", "b40", "b41", "b42", "b43", "b44", "b45", "b46", "b47", "b48", "b49", "b50",
        "b51", "b52", "b53", "b54", "b55", "b56", "b57", "b58", "b60", "b61",
        "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11",
        "i1", "i2", "i3", "i4", "i5", "i6", "i7", "i8", "i9",
        "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c8", "c9", "c10",
        "k1", "k2", "k3", "k4", "k5", "k6", "k7", "k8", "k9", "k10", "k11",
        "s4", "s5", "s6", "s7", "s8", "s10", "s11", "s14",
        "w2", "w5",
    ];

    /// <summary>(op, function, local): run op on the initializer (or declared type) of <c>let local</c> in that function.</summary>
    public static readonly (string Op, string Fn, string Name)[] Cases =
    [
        .. ProbeNames.Select(n => ("bind", "probes", n)),
        ("structd", "probes", "s1"), ("structd", "probes", "s2"), ("structd", "probes", "s3"), ("structd", "probes", "s9"),
        ("structd", "probes", "s12"), ("structd", "probes", "s13"),
        ("withd", "probes", "w1"), ("withd", "probes", "w3"), ("withd", "probes", "w4"), ("withd", "probes", "w6"),
        ("conv", "probes", "v1"), ("conv", "probes", "v2"), ("conv", "probes", "v3"),
        ("fact", "probes", "f1"), ("fact", "probes", "f2"), ("fact", "probes", "f3"),
        .. Enumerable.Range(1, 24).Select(i => ("eq", "probes", $"e{i}")),
        .. Enumerable.Range(1, 11).Select(i => ("applies", "probes", $"o{i}")),
        .. Enumerable.Range(1, 25).Select(i => ("text", "probes", $"t{i}")),
        .. Enumerable.Range(1, 8).Select(i => ("fold", "probes", $"d{i}")),
        ("bind", "gen", "g1"), ("bind", "gen", "g2"), ("bind", "gen", "g3"), ("bind", "gen", "g4"), ("bind", "gen", "g5"), ("eq", "gen", "e1"),
        ("seq", "pend1", "none"), ("seq", "pend2", "none"),
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

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static string KindName(BoundNode node) => node switch
    {
        BoundLiteral => "literal", BoundDefault => "defaultValue", BoundLocal => "local", BoundErrorExpr => "errorExpr",
        BoundStructLiteral => "structLiteral", BoundWith => "withExpr", BoundErrorVariant => "errorVariant",
        BoundRangePattern => "rangePattern",
        _ => Camel(node.GetType().Name[5..]),
    };

    /// <summary>The Bayan syntax flags of a node, from its C# syntax: 1 a literal, 2 a unary, 4 <c>IsUntypedLiteral</c>.</summary>
    private static int Flags(BoundNode? node) => node?.Syntax is not Expr x ? 0
        : (x is LiteralExpr ? 1 : 0) | (x is UnaryExpr ? 2 : 0) | ((bool)Call(null, "IsUntypedLiteral", x)! ? 4 : 0);

    private static string Const(BoundExpr e) => e.Constant is { } k ? k.ToString() : "-";

    /// <summary>A node, recursively: kind:type[span]=constant(children).</summary>
    private static string Dump(BoundNode? node)
    {
        if (node is null) return "null";
        var head = $"{KindName(node)}:{(node is BoundExpr e ? e.Type.DisplayName : "-")}[{node.Syntax.Span.Start}:{node.Syntax.Span.Length}]={(node is BoundExpr x ? Const(x) : "-")}";
        var kids = node switch
        {
            BoundUnary u => $"{Camel(u.Op.ToString())} {Dump(u.Operand)}",
            BoundBinary b => $"{Camel(b.Op.ToString())} {Dump(b.Left)} {Dump(b.Right)}",
            BoundConversion v => $"{Camel(v.Kind.ToString())} {Dump(v.Operand)}",
            BoundConditional c => $"{Dump(c.Condition)} {Dump(c.WhenTrue)} {Dump(c.WhenFalse)}",
            BoundCoalesce c => $"{Dump(c.Left)} {Dump(c.Right)}",
            BoundRange r => $"{(r.Inclusive ? "true" : "false")} {Dump(r.Start)} {Dump(r.End)}",
            BoundIs i => $"{Dump(i.Value)} {Dump(i.Pattern)}",
            BoundRangePattern p => $"{(p.Inclusive ? "true" : "false")} {Dump(p.Start)} {Dump(p.End)}",
            BoundLocal l => l.Local.Name,
            BoundLiteral l => l.Value.ToString(),
            _ => "",
        };
        return $"{head}({kids})";
    }

    /// <summary>The C# lines for one case: the result then the diagnostics it reported.</summary>
    private static List<string> Expected(string op, string fnName, string name)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "main.byn");
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        // The `(T)x` input forms are syntax errors, so `Create` stops before declaring; declare as the Bayan main does.
        if (c.Diagnostics.HasErrors) typeof(Compilation).GetMethod("Declare", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c, null);
        var mark = c.Diagnostics.Items.Count;
        var fd = file.Tree.Root.Members.OfType<FunctionDecl>().First(f => f.Name.Text == fnName);
        var fn = file.Module.Functions[fnName][0];
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, fn, null, null, null], null)!;
        foreach (var p in fd.Params) Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), LocalKind.Parameter);
        if (op == "seq")
        {
            // A held (first-pass) binding, where a `var` can be pending; every statement in order, then the
            // pending-`var` constraints by declaration start (Q614). Its diagnostics are held, so none are compared.
            Call(b, "Hold");
            var seq = new List<string>();
            foreach (var s in fd.Body!.Statements) Call(b, "BindStatement", s, null);
            var cons = (System.Collections.IDictionary)BinderType.GetField("_constraints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(b)!;
            foreach (System.Collections.DictionaryEntry kv in cons) seq.Add($"{((LocalDecl)kv.Key).Span.Start}|cons {((LocalDecl)kv.Key).Span.Start} {((TypeSymbol)kv.Value!).DisplayName}");
            var sorted = seq.OrderBy(x => int.Parse(x[..x.IndexOf('|')])).Select(x => x[(x.IndexOf('|') + 1)..]).ToList();
            sorted.Add("seq");
            for (var i = mark; i < c.Diagnostics.Items.Count; i++) sorted.AddRange(Diag(c.Diagnostics.Items[i]).Split('\n'));
            return sorted;
        }
        var ld =fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name);
        var init = ld.Init!;
        var expected = ld.Type is null ? null : (TypeSymbol)Call(b, "ResolveType", ld.Type)!;
        var lines = new List<string>();
        switch (op)
        {
            case "bind":
                var bound = init switch
                {
                    UnaryExpr u => Call(b, "BindUnary", u, expected),
                    BinaryExpr bin => Call(b, "BindBinary", bin, expected),
                    RangeExpr r => Call(b, "BindRange", r, expected),
                    InExpr i => Call(b, "BindIn", i),
                    ConditionalExpr ce => Call(b, "BindConditional", ce, expected),
                    CastExpr ca => Call(b, "BindCast", ca),
                    StructLiteralExpr s => Call(b, "BindStructLiteral", s, expected),
                    WithExpr w => Call(b, "BindWith", w),
                    _ => throw new InvalidOperationException(name),
                };
                lines.Add($"flags {Flags((BoundNode?)bound)}");
                lines.Add(Dump((BoundNode?)bound));
                break;
            case "structd": Call(b, "BindStructLiteral", (StructLiteralExpr)init, expected); lines.Add("bound"); break;
            case "withd": Call(b, "BindWith", (WithExpr)init); lines.Add("bound"); break;
            case "conv":
                var call = (CallExpr)init;
                lines.Add(Dump((BoundNode?)Call(b, "BindConversion", call, expected, call.Args[0].Value)));
                break;
            case "fact": lines.Add(Dump((BoundNode?)Call(b, "Fact", (BoundExpr)Call(b, "BindExpr", init, null)!))); break;
            case "eq": lines.Add((bool)Call(b, "IsEquatable", expected)! ? "true" : "false"); break;
            case "applies": lines.Add((bool)Call(b, "OperatorApplies", ((BinaryExpr)init).Op, expected)! ? "true" : "false"); break;
            case "text": lines.Add((string)Call(b, "BinaryText", ((BinaryExpr)init).Op)!); break;
            case "fold":
                var fb = (BinaryExpr)init;
                var l = (BoundExpr)Call(b, "BindExpr", fb.Left, null)!;
                var r2 = (BoundExpr)Call(b, "BindExpr", fb.Right, null)!;
                var node = new BoundBinary(fb, fb.Op, l, r2, l.Type);
                lines.Add(Dump((BoundNode?)Call(b, "Fold", node, l, r2, l.Type)));
                break;
        }
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases.SelectMany(k => new[] { k.Op, k.Fn, k.Name })]);
    });

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add($"{k.Op}.{k.Fn}.{k.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string section)
    {
        var parts = section.Split('.');
        SelfhostSemanticsHarness.AssertSection(Sections.Value, section, Expected(parts[0], parts[1], parts[2]));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        string tcConst(in Ctx c, for BExpr e) {
            if (exprConstant(in c, for: e) is { } k) { return constantText(in k) }
            return "-"
        }

        string tcDump(ref Ctx c, for BNode e) {
            if e == -1 { return "null" }
            let k = boundKind(e)
            let i = boundIndex(e)
            if k == .rangePattern {
                let p = c.bRangePatterns[i]
                return $"rangePattern:-[{p.span.start}:{p.span.length}]=-({p.inclusive} {tcDump(ref c, for: p.start)} {tcDump(ref c, for: p.end)})"
            }
            let sp = nodeSpan(in c, for: e)
            let kindName = k == .isExpr ? "is" : $"{k}"
            let head = $"{kindName}:{typeDisplay(ref c, by: exprType(in c, for: e))}[{sp.start}:{sp.length}]={tcConst(in c, for: e)}"
            var kids = ""
            switch k {
                case .unary: kids = $"{c.bUnaries[i].op == .notKeyword ? "not" : $"{c.bUnaries[i].op}"} {tcDump(ref c, for: c.bUnaries[i].operand)}"
                case .binary: kids = $"{c.bBinaries[i].op} {tcDump(ref c, for: c.bBinaries[i].left)} {tcDump(ref c, for: c.bBinaries[i].right)}"
                case .conversion: kids = $"{c.bConversions[i].kind} {tcDump(ref c, for: c.bConversions[i].operand)}"
                case .conditional: kids = $"{tcDump(ref c, for: c.bConditionals[i].condition)} {tcDump(ref c, for: c.bConditionals[i].whenTrue)} {tcDump(ref c, for: c.bConditionals[i].whenFalse)}"
                case .coalesce: kids = $"{tcDump(ref c, for: c.bCoalesces[i].left)} {tcDump(ref c, for: c.bCoalesces[i].right)}"
                case .range: kids = $"{c.bRanges[i].inclusive} {tcDump(ref c, for: c.bRanges[i].start)} {tcDump(ref c, for: c.bRanges[i].end)}"
                case .isExpr: kids = $"{tcDump(ref c, for: c.bIses[i].value)} {tcDump(ref c, for: c.bIses[i].pattern)}"
                case .local: kids = c.locals[c.bLocals[i].local].name
                case .literal: kids = constantText(in c.bLiterals[i].value)
                default: {}
            }
            return $"{head}({kids})"
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        BExpr runBind(ref Binder b, through ref Ctx c, for in parser.Expr init, to Type expected) {
            switch init {
                case .unaryExpr(let u): return bindUnary(ref b, through: ref c, for: in u.value, to: expected)
                case .binaryExpr(let x): return bindBinary(ref b, through: ref c, for: in x.value, to: expected)
                case .rangeExpr(let r): return bindRange(ref b, through: ref c, for: in r.value, to: expected)
                case .inExpr(let x): return bindIn(ref b, through: ref c, for: in x.value)
                case .conditionalExpr(let x): return bindConditional(ref b, through: ref c, for: in x.value, to: expected)
                case .castExpr(let x): return bindCast(ref b, through: ref c, for: in x.value, from: init)
                case .structLiteralExpr(let s): return bindStructLiteral(ref b, through: ref c, for: in s.value, from: init, to: expected)
                case .withExpr(let w): return bindWith(ref b, through: ref c, for: in w.value, from: init)
                default: panic("unexpected initializer")
            }
        }

        void runInit(ref Binder b, through ref Ctx c, for in parser.Expr init, from in parser.TypeSyntax declType, by string op) {
            var expected: Type = -1
            switch declType {
                case .none: {}
                default: expected = resolveType(ref b, through: ref c, for: in declType)
            }
            if op == "bind" {
                let bound = runBind(ref b, through: ref c, for: in init, to: expected)
                print($"flags {bound == -1 ? 0 : syntaxFlags(in c, for: bound) & 7}")
                print(tcDump(ref c, for: bound))
            }
            if op == "structd" || op == "withd" {
                let _ = runBind(ref b, through: ref c, for: in init, to: expected)
                print("bound")
            }
            if op == "fact" {
                let bound = bindExpr(ref b, through: ref c, for: in init)
                print(tcDump(ref c, for: fact(ref c, for: bound)))
            }
            if op == "eq" { print(isEquatable(ref c, for: expected) ? "true" : "false") }
            switch init {
                case .binaryExpr(let x):
                    if op == "applies" { print(operatorApplies(ref c, for: x.value.op, as: expected) ? "true" : "false") }
                    if op == "text" { print(binaryText(x.value.op)) }
                    if op == "fold" {
                        let l = bindExpr(ref b, through: ref c, for: in x.value.left)
                        let r = bindExpr(ref b, through: ref c, for: in x.value.right)
                        let lt = exprType(in c, for: l)
                        let node = addBinary(ref c, from: BBinary { span: x.value.span, op: x.value.op, left: l, right: r, type: lt })
                        print(tcDump(ref c, for: fold(ref b, through: ref c, for: node, from: l, by: r, as: lt)))
                    }
                case .callExpr(let call):
                    if op == "conv" { print(tcDump(ref c, for: bindConversion(ref b, through: ref c, for: in init, to: expected, from: in call.value.args[0].value))) }
                default: {}
            }
        }

        void runCase(ref Ctx c, for string op, from string fnName, by string name) {
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
            let start = c.diagnostics.length
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if t18Text(in c, from: file, at: fd.value.name) == fnName {
                            b.fn = c.modules[c.entryModule].functions[fnName][0]
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: .parameter)
                            }
                            switch fd.value.body {
                                case .blockStmt(let blk):
                                    if op == "seq" {
                                        let _ = hold(ref b, through: ref c)
                                        for s in blk.value.statements { let _ = bindStatement(ref b, through: ref c, for: in s) }
                                        for s in blk.value.statements {
                                            switch s {
                                                case .localDecl(let ld):
                                                    let at = ld.value.span.start
                                                    if b.constraints.contains(at) { print($"cons {at} {typeDisplay(ref c, by: b.constraints[at])}") }
                                                default: {}
                                            }
                                        }
                                        print("seq")
                                    }
                                    for s in blk.value.statements {
                                        switch s {
                                            case .localDecl(let ld):
                                                if t18Text(in c, from: file, at: ld.value.name) == name { runInit(ref b, through: ref c, for: in ld.value.init, from: in ld.value.type, by: op) }
                                            default: {}
                                        }
                                    }
                                default: {}
                            }
                        }
                    default: {}
                }
            }
            tcDiags(ref c, from: start)
        }

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 2 < args.length {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t19-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t19-cases",
                    displayRoot: "tmp/t19-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t19-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {args[i]}.{args[i + 1]}.{args[i + 2]}")
                runCase(ref c, for: args[i], from: args[i + 1], by: args[i + 2])
                i += 3
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source));
}
