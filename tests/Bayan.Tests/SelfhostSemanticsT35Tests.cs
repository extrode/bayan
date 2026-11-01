using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T35: <c>selfhost/semantics/express4.byn</c> against the C# <c>BinderExpressions</c> (BinderExpressions.cs 1641-2145:
/// collections, spreads, map literals, tuples, lambdas and captures, <c>stackalloc</c>, interpolation and format specs,
/// <c>WhyNotFormattable</c>, <c>PropertyType</c>). One source, <see cref="Source"/>, is compiled by both sides as in the
/// T19 tests; each case runs one op on one <c>let</c> of that source on both sides, and every expected line is computed
/// from the C#.
/// </summary>
public sealed class SelfhostSemanticsT35Tests
{
    private const string Dir = "tmp/t35-cases";

    private const string Source = """"
        struct Point {
            i32 x = 1
            i32 y = 2
        }

        struct Fmt {
            i32 v

            public void formatInto(ref StrBuf sb) {
            }
        }

        struct FmtSpec {
            i32 v

            public void formatInto(ref StrBuf sb, as FormatSpec spec) {
            }
        }

        struct Holder {
            List<Point> xs
        }

        struct Plain {
            i32 a
            string s
        }

        union Bag {
            full(List<Point> xs)
            empty
        }

        union Tree {
            leaf
            node(Owned<Tree> next)
        }

        enum Color {
            red
            green
        }

        void nothing() {
        }

        i64 inc(i64 x) => x + 1

        void probes(i64 n, i32 a, f64 f, string str, rune ch, bool flag, List<i64> xs, i64[3] arr, Span<i64> sp, Point p, Fmt fm, FmtSpec fs, Holder h, Plain pl, Bag bag, List<Point> lp, Owned<Point> op, ref i64 rn, Map<string, i64> m) {
            var vv: i64 = 1
            let k: i64 = 7
            let c1 = [1, 2, 3]
            let c2: List<i64> = [1, 2]
            let c3: List<i64> = []
            let c4 = []
            let c5: i64[3] = [1, 2]
            let c6: i64[3] = [1, 2, 3]
            let c7: List<i64> = [..xs, 4]
            let c8: i64[4] = [..arr, 4]
            let c9: i64[4] = [..xs, 4]
            let c10: Span<i64> = [..sp]
            let c11: List<i64>? = [1]
            let c12: string = [1]
            let c13: Nope = [1]
            let c14 = [null, 1]
            let c15 = [nothing(), 1]
            let c16 = [nope, 1]
            let c17 = [inc, inc]
            let c18 = [..arr, 4]
            let c19 = [..xs]
            let c20: List<i64> = [..n]
            let c21: List<i64> = [..lp]
            let c22: List<Point> = [..lp]
            let c23: List<i64> = [..nope]
            let c24: List<i64> = ["a"]
            let c25: Map<string, i64> = []
            let c26 = [1, "a"]
            let c27: List<string> = [..xs]
            let c28: Span<i64> = [1, 2]
            let c29 = [..n]
            let c30: List<i64> = [1, ..xs, 2]
            let m1 = ["a": 1, "b": 2]
            let m2: Map<string, i64> = ["a": 1]
            let m3: Map<string, i64>? = ["a": 1]
            let m4 = ["a": 1, 2]
            let m5 = [1, "a": 2]
            let m6: List<i64> = ["a": 1]
            let m7 = [nope: 1, "b": 2]
            let m8 = [f: 1]
            let m9 = [p: 1]
            let m10 = ["a": null]
            let m11 = ["a": inc]
            let m12: Map<string, i64> = ["a": "b"]
            let m13 = ["a": 1, "b": "c"]
            let m14: Nope = ["a": 1]
            let m15 = [..xs, "a": 1]
            let m16 = ["a": nothing()]
            let t1 = (1, "a")
            let t2: (i64, string) = (1, "a")
            let t3: (i64 x, i64 y) = (x: 1, z: 2)
            let t4: (i64, i64)? = (1, 2)
            let t5: (i64, i64) = (1, 2, 3)
            let t6 = (null, 1)
            let t7 = (nope, 1)
            let t8: Nope = (1, 2)
            let t9 = (inc, inc)
            let t10 = (a: 1, b: str)
            let t11: (i64, string) = (1, 2)
            let t12 = (nothing(), 1)
            let l1: i64 -> i64 = x => x + 1
            let l2 = x => x
            let l3: i64 -> i64 = (x, y) => x
            let l4: (i64, i64) -> i64 = x => x
            let l5: (i64, i64, i64) -> i64 = x => x
            let l6: i64 -> i64 = x => x + k
            let l7: i64 -> i64 = x => x + vv
            let l8: i64 -> i64 = x => x + rn
            let l9: i64 -> i64 = x => x + n + k + n
            let l10: i64 -> (i64 -> i64) = x => y => x + y + k
            let l11: i64 -> i64 = x => { return x }
            let l12: i64? = x => x
            let l13: (i64 -> i64)? = x => x
            let l14: () -> i64 = () => k
            let l15: i64 -> i64 = n => n
            let l16: i64 -> i64 = x => a
            let s1 = stackalloc i64[4]
            let s2 = stackalloc i64[0]
            let s3 = stackalloc i64[2000000]
            let s4 = stackalloc i64[n]
            let s5 = stackalloc Nope[4]
            let s6 = stackalloc i64[nope]
            let s7 = stackalloc u8[1048576]
            let s8 = stackalloc i64[-1]
            let i1 = $"a{n}b"
            let i2 = $"{p}"
            let i3 = $"{p, as: .debug}"
            let i4 = $"{xs}"
            let i5 = $"{lp}"
            let i6 = $"{n, as: .hex}"
            let i7 = $"{n, as: .binary}"
            let i8 = $"{n, as: .octal}"
            let i9 = $"{n, as: .decimal}"
            let i10 = $"{f, as: .hex}"
            let i11 = $"{n, width: 8}"
            let i12 = $"{n, width: 2000}"
            let i13 = $"{n, width: n}"
            let i14 = $"{f, digits: 3}"
            let i15 = $"{n, digits: 3}"
            let i16 = $"{f, digits: 20}"
            let i17 = $"{n, fill: '0'}"
            let i18 = $"{n, fill: ch}"
            let i19 = $"{n, align: .start}"
            let i20 = $"{n, align: .end}"
            let i21 = $"{n, align: .center}"
            let i22 = $"{n, align: .left}"
            let i23 = $"{n, align: 3}"
            let i24 = $"{n, width: 1, width: 2}"
            let i25 = $"{n, bogus: 1}"
            let i26 = $"{n, as: .nope}"
            let i27 = $"{fm}"
            let i28 = $"{fs, as: .hex}"
            let i29 = $"{fm, as: .hex}"
            let i30 = $"{h, as: .debug}"
            let i31 = $"{pl, as: .debug}"
            let i32v = $"{bag}"
            let i33 = $"{nope}"
            let i34 = $"{nope, width: 3}"
            let i35 = $"{op}"
            let i36 = $"{nothing()}"
            let i37 = $"{fs, digits: 2}"
            let i38 = $"{n, width: "a"}"
            let i39 = $"{n, fill: 1}"
            let i40 = $"{fs, as: .debug}"
            let i41 = $"{n, width: -1}"
            let i42 = $"{f, digits: f}"
            let i43 = $"{m}"
            let i44 = $"x{n, width: 4, fill: '*', align: .end}y{str}"
            let w1: i64 = 0
            let w2: void = 0
            let w3: Point = 0
            let w4: Fmt = 0
            let w5: List<i64> = 0
            let w6: List<Point> = 0
            let w7: Map<string, Point> = 0
            let w8: Map<Point, i64> = 0
            let w9: Map<string, i64> = 0
            let w10: (i64, Point) = 0
            let w11: (i64, string) = 0
            let w12: Point? = 0
            let w13: Owned<Point> = 0
            let w14: Bag = 0
            let w15: Tree = 0
            let w16: Color = 0
            let w17: Symbol = 0
            let w18: StrBuf = 0
            let w19: i64[2] = 0
            let w20: Span<Point> = 0
            let w21: Result<Point, Color> = 0
            let w22: i64 -> i64 = 0
            let w23: never = 0
            let w24: ReadOnlySpan<i64> = 0
            let w25: string? = 0
            let p1: i64 = 0
            let p2: i64 = 0
            let p3: i64 = 0
            let n1: i64 -> i64 = x => x
        }
        """";

    private static readonly string[] ExprNames =
    [
        .. Enumerable.Range(1, 30).Select(i => $"c{i}"),
        .. Enumerable.Range(1, 16).Select(i => $"m{i}"),
        .. Enumerable.Range(1, 12).Select(i => $"t{i}"),
        .. Enumerable.Range(1, 16).Select(i => $"l{i}"),
        .. Enumerable.Range(1, 8).Select(i => $"s{i}"),
        .. Enumerable.Range(1, 44).Select(i => i == 32 ? "i32v" : $"i{i}"),
    ];

    /// <summary>(op, function, local): run op on the initializer (or declared type) of <c>let local</c> in that function.</summary>
    public static readonly (string Op, string Fn, string Name)[] Cases =
    [
        .. ExprNames.Select(n => ("expr", "probes", n)),
        .. Enumerable.Range(1, 25).Select(i => ("fmt", "probes", $"w{i}")),
        ("prop", "probes", "p1"), ("prop", "probes", "p2"), ("prop", "probes", "p3"),
        ("inl", "probes", "n1"),
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
        BoundLiteral => "literal", BoundLocal => "local", BoundErrorExpr => "errorExpr",
        _ => Camel(node.GetType().Name[5..]),
    };

    private static string Const(BoundExpr e) => e.Constant is { } k ? k.ToString() : "-";

    private static string B(bool v) => v ? "true" : "false";

    private static string Part(BoundInterpolationPart p) => p.Text is { } t ? $"t:{t}"
        : $"v:{Dump(p.Value)}{(p.Spec is { } s ? $"/{s.Radix},{B(s.Debug)},{s.Width},{s.Fill},{s.Align},{s.Digits}" : "")}";

    /// <summary>A node, recursively: kind:type[span]=constant(children).</summary>
    private static string Dump(BoundNode? node)
    {
        if (node is null) return "null";
        var head = $"{KindName(node)}:{(node is BoundExpr e ? e.Type.DisplayName : "-")}[{node.Syntax.Span.Start}:{node.Syntax.Span.Length}]={(node is BoundExpr x ? Const(x) : "-")}";
        var kids = node switch
        {
            BoundConversion v => $"{Camel(v.Kind.ToString())} {Dump(v.Operand)}",
            BoundBinary b => $"{Camel(b.Op.ToString())} {Dump(b.Left)} {Dump(b.Right)}",
            BoundCollection c => string.Join(" ", c.Elements.Select(Dump)),
            BoundSpread s => Dump(s.Operand),
            BoundTuple t => string.Join(" ", t.Elements.Select(Dump)),
            BoundStackalloc s => $"{s.Element.DisplayName} {s.Count}",
            BoundInterpolated i => string.Join(" ", i.Parts.Select(Part)),
            BoundLambda l => $"{l.Lambda.Name} {l.Lambda.Kind} ret {l.Lambda.ReturnType.DisplayName} fails {B(l.Lambda.Fails)} encl {l.Lambda.Enclosing?.Name}"
                + $" params[{string.Join(",", l.Lambda.Parameters.Select(q => $"{q.Name}:{q.Type.DisplayName}"))}]"
                + $" capsyms[{string.Join(",", l.Lambda.Captures.Select(q => q.Name))}]"
                + $" caps[{string.Join(",", l.Captures.Select(Dump))}] all {l.Lambda.Module.AllFunctions.Contains(l.Lambda)}",
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
        if (c.Diagnostics.HasErrors) typeof(Compilation).GetMethod("Declare", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c, null);
        var mark = c.Diagnostics.Items.Count;
        var fd = file.Tree.Root.Members.OfType<FunctionDecl>().First(f => f.Name.Text == fnName);
        var fn = file.Module.Functions[fnName][0];
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, fn, null, null, null], null)!;
        foreach (var p in fd.Params) Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), LocalKind.Parameter);
        var lines = new List<string>();
        foreach (var s in fd.Body!.Statements.OfType<LocalDecl>())
        {
            if (s.Name.Text == name) break;
            // The outer `var vv` and `let k` that lambdas capture are declared as the statement binder would.
            if (s.Name.Text is "vv" or "k") Call(b, "BindStatement", s, null);
        }
        var ld = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name);
        var init = ld.Init!;
        var expected = ld.Type is null ? null : (TypeSymbol)Call(b, "ResolveType", ld.Type)!;
        switch (op)
        {
            case "expr": lines.Add(Dump((BoundNode?)Call(b, "BindExpr", init, expected))); break;
            case "fmt": lines.Add((string?)Call(null, "WhyNotFormattable", expected, new HashSet<TypeSymbol>()) is { } why ? $"[{why}]" : "null"); break;
            case "prop":
                var member = name switch { "p1" => BuiltinMember.FormatSpecRadix, "p2" => BuiltinMember.FormatSpecAlign, _ => BuiltinMember.FormatSpecWidth };
                lines.Add(((TypeSymbol)Call(b, "PropertyType", member, expected)!).DisplayName);
                break;
            case "inl":
                var lam = (BoundLambda)Call(b, "BindExpr", init, expected)!;
                lines.Add(B((bool)Call(b, "InLambda", init, "this", "a lambda does not capture `this`")!));
                BinderType.GetField("_fn", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(b, lam.Lambda);
                lines.Add(B((bool)Call(b, "InLambda", init, "this", "a lambda does not capture `this`")!));
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
        if (Environment.GetEnvironmentVariable("T35DUMP") is { } dump) File.AppendAllText(dump, $"== {section}\n{string.Join("\n", Expected(parts[0], parts[1], parts[2]))}\n"); // T35DUMP
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

        string tcJoin(ref Ctx c, for List<BExpr> xs) {
            var sb = StrBuf()
            for i in 0..xs.length {
                if i > 0 { sb.append(" ") }
                sb.append(tcDump(ref c, for: xs[i]))
            }
            return $"{sb.finish()}"
        }

        string tcPart(ref Ctx c, for BInterpolationPart p) {
            if (p.text is { } t) { return $"t:{t}" }
            var spec = ""
            if (p.spec is { } s) { spec = $"/{s.radix},{s.debug},{s.width},{s.fill},{s.align},{s.digits}" }
            return $"v:{tcDump(ref c, for: p.value)}{spec}"
        }

        string tcLambda(ref Ctx c, for i64 i) {
            let f = c.bLambdas[i].lambda
            var sb = StrBuf()
            let encl = c.functions[f].enclosing == -1 ? "" : c.functions[c.functions[f].enclosing].name
            sb.append($"{c.functions[f].name} Lambda ret {typeDisplay(ref c, by: c.functions[f].returnType)} fails {c.functions[f].hasFails} encl {encl} params[")
            for k in 0..c.functions[f].parameters.length {
                let q = c.functions[f].parameters[k]
                if k > 0 { sb.append(",") }
                sb.append($"{c.locals[q].name}:{typeDisplay(ref c, by: c.locals[q].type)}")
            }
            sb.append("] capsyms[")
            for k in 0..c.functions[f].captures.length {
                if k > 0 { sb.append(",") }
                sb.append(c.locals[c.functions[f].captures[k]].name)
            }
            sb.append("] caps[")
            for k in 0..c.bLambdas[i].captures.length {
                if k > 0 { sb.append(",") }
                sb.append(tcDump(ref c, for: c.bLambdas[i].captures[k]))
            }
            let all = listContains(in c.modules[c.functions[f].module].allFunctions, by: f) ? "True" : "False"
            sb.append($"] all {all}")
            return $"{sb.finish()}"
        }

        string tcDump(ref Ctx c, for BNode e) {
            if e == -1 { return "null" }
            let k = boundKind(e)
            let i = boundIndex(e)
            let sp = nodeSpan(in c, for: e)
            let head = $"{k}:{typeDisplay(ref c, by: exprType(in c, for: e))}[{sp.start}:{sp.length}]={tcConst(in c, for: e)}"
            var kids = ""
            switch k {
                case .conversion: kids = $"{c.bConversions[i].kind} {tcDump(ref c, for: c.bConversions[i].operand)}"
                case .binary: kids = $"{c.bBinaries[i].op} {tcDump(ref c, for: c.bBinaries[i].left)} {tcDump(ref c, for: c.bBinaries[i].right)}"
                case .collection: kids = tcJoin(ref c, for: copyIds(in c.bCollections[i].elements))
                case .spread: kids = tcDump(ref c, for: c.bSpreads[i].operand)
                case .tuple: kids = tcJoin(ref c, for: copyIds(in c.bTuples[i].elements))
                case .stackalloc: kids = $"{typeDisplay(ref c, by: c.bStackallocs[i].element)} {c.bStackallocs[i].count}"
                case .interpolated:
                    var sb = StrBuf()
                    for j in 0..c.bInterpolateds[i].parts.length {
                        if j > 0 { sb.append(" ") }
                        let part = c.bInterpolateds[i].parts[j]
                        sb.append(tcPart(ref c, for: part))
                    }
                    kids = $"{sb.finish()}"
                case .lambda: kids = tcLambda(ref c, for: i)
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

        void runInit(ref Binder b, through ref Ctx c, for in parser.Expr init, from in parser.TypeSyntax declType, by string op, at string name) {
            var expected: Type = -1
            switch declType {
                case .none: {}
                default: expected = resolveType(ref b, through: ref c, for: in declType)
            }
            if op == "expr" { print(tcDump(ref c, for: bindExpr(ref b, through: ref c, for: in init, to: expected))) }
            if op == "fmt" {
                var seen = List<Type>()
                let why = whyNotFormattable(ref c, for: expected, by: ref seen)
                print(why is null ? "null" : $"[{why ?? ""}]")
            }
            if op == "prop" {
                let member: BuiltinMember = name == "p1" ? .formatSpecRadix : name == "p2" ? .formatSpecAlign : .formatSpecWidth
                print(typeDisplay(ref c, by: propertyType(ref b, through: ref c, for: member, as: expected)))
            }
            if op == "inl" {
                let lam = bindExpr(ref b, through: ref c, for: in init, to: expected)
                let span = parser.exprSpan(in init)
                print($"{inLambda(ref b, through: ref c, at: span, for: "this", by: "a lambda does not capture `this`")}")
                b.fn = c.bLambdas[boundIndex(lam)].lambda
                print($"{inLambda(ref b, through: ref c, at: span, for: "this", by: "a lambda does not capture `this`")}")
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
                                    var done = false
                                    for s in blk.value.statements {
                                        switch s {
                                            case .localDecl(let ld):
                                                let ln = t18Text(in c, from: file, at: ld.value.name)
                                                if !done && ln == name {
                                                    runInit(ref b, through: ref c, for: in ld.value.init, from: in ld.value.type, by: op, at: name)
                                                    done = true
                                                }
                                                if !done && (ln == "vv" || ln == "k") { let _ = bindStatement(ref b, through: ref c, for: in s) }
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
                sources.append(lexer.SourceText.create("tmp/t35-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t35-cases",
                    displayRoot: "tmp/t35-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t35-cases", from: move sources, by: -1)
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
