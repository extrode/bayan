using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;

namespace Bayan.Tests;

/// <summary>
/// T39: <c>selfhost/semantics/calls4.byn</c> against the C# <c>BinderCalls</c> (BinderCalls.cs 1578-1963).
/// <c>diag.*</c> compiles a golden of <c>tests/cases/diag/</c> whole on both sides and compares the codes T39 reports;
/// <c>wr</c> compiles <see cref="WrSource"/> and compares each <c>w*</c> local's <c>Written</c> flag too.
/// <c>exh.*</c> takes the bound arms of one switch of <see cref="ExhSource"/> from the C# and calls <c>CheckExhaustive</c>
/// on them; the Bayan side rebuilds the same patterns, so switch statements (<c>bindSwitchStatement</c> is a stub) are covered too (Q697).
/// Every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT39Tests
{
    private const string Dir = "tmp/t39-cases";

    private const string ExhSource = """
        enum Suit {
            clubs
            diamonds
            hearts
            spades
        }

        union Shape {
            circle(f32 r)
            rect(f32 w, f32 h)
            none
        }

        union error EmailError {
            invalid
        }

        union error DbError {
            timeout
            lost
        }

        struct P {
            i64 x
        }

        void e1(Suit s) {
            switch s {
                case .hearts:
                    print("h")
            }
        }

        string e2(Suit s) => s switch {
            .hearts or .diamonds => "r",
            .clubs => "b",
        }

        string e3(Suit s) => s switch {
            .hearts => "r",
            _ => "b",
        }

        f32 e4(Shape s) => s switch {
            .circle(let r) => r,
            _ => 0,
        }

        f32 e5(Shape s) => s switch {
            .circle(let r) => r,
            .rect(let w, _) => w,
            .none => 0,
            _ => 0,
        }

        f32 e6(Shape s) => s switch {
            .circle(let r) => r,
        }

        string e7(Shape? s) => s switch {
            .none => "n",
            _ => "o",
        }

        string e8(bool b) => b switch {
            true => "t",
            false => "f",
        }

        string e9(bool b) => b switch {
            true => "t",
        }

        string e10(i64 n) => n switch {
            1 => "a",
        }

        void e11(i64 n) {
            switch n {
                case 1:
                    print("a")
            }
        }

        string e12(Suit? s) => s switch {
            null => "n",
            .clubs => "c",
        }

        string e13(i64? n) => n switch {
            null => "n",
        }

        void e14(i64? n) {
            switch n {
                case null:
                    print("n")
            }
        }

        string e15(i64? n) => n switch {
            null => "n",
            let v => "v",
        }

        string e16(P? p) => p switch {
            { x: let v } => "x",
            null => "n",
        }

        string e17(i64? n) => n switch {
            not null => "v",
        }

        string e18(Shape s) => s switch {
            .circle(let r) when true => "c",
            .rect(_, _) or .none => "o",
        }

        string e19(DbError e) => e switch {
            .timeout => "t",
        }

        string e20(DbError e) => e switch {
            DbError.timeout => "t",
        }

        void e21(Suit s) {
            switch s {
                case .hearts:
                    print("h")
                default:
                    print("d")
            }
        }

        string e22(P p) => p switch {
            { x: let v } => "x",
        }

        string e23(EmailError e) => e switch {
            .invalid => "i",
        }

        string e24(Suit? s) => s switch {
            not null => "v",
        }
        """;

    /// <summary>Cases of <see cref="ExhSource"/>; a non-null union names the error sets passed as the type instead of the subject's.</summary>
    private static readonly (string Name, string[]? Union)[] Exh =
    [
        ("e1", null), ("e2", null), ("e3", null), ("e4", null), ("e5", null), ("e6", null), ("e7", null), ("e8", null),
        ("e9", null), ("e10", null), ("e11", null), ("e12", null), ("e13", null), ("e14", null), ("e15", null), ("e16", null),
        ("e17", null), ("e18", null), ("e19", null), ("e20", null), ("e21", null), ("e22", null), ("e23", null), ("e24", null),
        ("e19u", ["EmailError", "DbError"]), ("e20u", ["EmailError", "DbError"]), ("e23u", ["EmailError", "DbError"]),
        ("e19r", ["DbError", "EmailError"]),
    ];

    private const string WrSource = """
        struct H {
            i64 n
            List<i64> items
        }

        void wr(Span<i64> sp, ReadOnlySpan<i64> ro, ref i64 rn, i64 pn, H hp) {
            var w1 = 0
            w1 = 1
            var w2 = hp
            w2.n = 1
            var w3 = List<i64>()
            w3[0] = 1
            var w4 = 0
            w4 += 1
            var w5 = 0
            w5++
            var w6 = 0
            print($"{w6}")
            let w7 = 0
            w7 = 1
            var w8 = Map<string, i64>()
            w8["a"] = 1
            var w9 = hp
            w9.items[0] = 1
            var wa = Map<string, i64>()
            wa["a"] += 1
            sp[0] = 1
            ro[0] = 1
            rn = 2
            pn = 3
            var wb = "x"
            wb += "y"
            var wc: i64? = null
            wc ??= 1
            var wd = 1
            wd <<= 2
            var we = true
            we += 1
            var wf = 1.5
            wf <<= 1
            var wg = true
            wg++
            var wh = 1
            wh ??= 2
        }
        """;

    /// <summary>The goldens whose T39 diagnostics are compared.</summary>
    private static readonly string[] DiagCases =
    [
        "readonly-field", "bad-unary-operand", "coalesce-not-optional", "checked-field-assignment", "field-write-outside",
        "field-write-states", "bad-binary-operands",
    ];

    /// <summary>
    /// Library sources standing in for goldens whose T39 lines also need an unported or divergent sibling (Q697):
    /// MutationChecks for a call of a `ref this` member, a struct literal's state type, `@sealed` attributes, a for body.
    /// </summary>
    private static readonly Dictionary<string, string> Lib = new()
    {
        ["immutable"] = """
            void bump(i64 n) {
                n = n + 1
            }

            void loop(List<i64> xs) {
                let k = 0
                k = 1
                xs = List<i64>()
            }
            """,
        ["getter"] = """
            struct G {
                i64 n

                i64 latest {
                    n = 1
                    return n
                }
            }
            """,
        ["mutates"] = """
            struct Counter {
                i64 n
                i64 total

                void bump(ref this) {
                    n += 1
                }

                void reset() {
                    total = 0
                    this.n = 0
                }

                void add(i64 by) {
                    total += by
                }

                i64 peek() => n
            }
            """,
        ["state"] = """
            struct Order {
                i64 id
                states {
                    draft
                    paid
                }
            }

            void a(ref Order.paid o, Order.draft d) {
                o = d
            }

            void s(ref Order.paid o, Order.paid p) {
                o = p
            }

            void w(ref Order o, Order.draft d) {
                o = d
            }

            void r(ref Order o, Order.paid p) {
                var copy = o
                copy = p
            }
            """,
    };

    /// <summary>The codes calls4.byn reports.</summary>
    private static readonly string[] T39Codes =
    [
        "BYN2104", "BYN2105", "BYN2106", "BYN2107", "BYN2115", "BYN2207", "BYN2208", "BYN2209", "BYN2210", "BYN2410",
        "BYN2712", "BYN2783", "BYN3208", "BYN3216", "BYN7012",
    ];

    private static readonly (string Op, string Name)[] Cases =
    [
        .. DiagCases.Select(d => ("diag", d)),
        .. Lib.Keys.Select(k => ("lib", k)),
        ("wr", "wr"),
        .. Exh.Select(e => ("exh", e.Name)),
    ];

    private static string DiagSource(string name) => Lib.TryGetValue(name, out var lib) ? lib :
        File.ReadAllText(Path.Combine(Golden.CasesRoot, "diag", name + ".byn")).Replace("\r\n", "\n");

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

        private static Compilation CompileLibrary(string text) =>
        Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), text + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));

    private static FunctionSymbol Function(Compilation c, string name) =>
        c.Modules.SelectMany(m => m.AllFunctions).First(f => f.Name == name);

    /// <summary>The switch a case function of <see cref="ExhSource"/> starts with: its subject span and type, its arms, and whether it is a statement.</summary>
    private static (TextSpanLike Span, TypeSymbol Type, List<(BoundPattern?, BoundExpr?)> Arms, bool Statement) SwitchOf(Compilation c, string name)
    {
        var first = Function(c, name).Body!.Statements[0];
        if (first is BoundSwitch sw)
            return (new(sw.Value.Syntax.Span), sw.Value.Type, [.. sw.Sections.Select(s => (s.Pattern, s.Guard))], true);
        var e = first switch
        {
            BoundReturn r => r.Value,
            BoundExprStmt x => x.Expr,
            _ => throw new InvalidOperationException(first.GetType().Name),
        };
        while (e is BoundConversion conv) e = conv.Operand;
        var se = (BoundSwitchExpr)e!;
        return (new(se.Value.Syntax.Span), se.Value.Type, [.. se.Arms.Select(a => ((BoundPattern?)a.Pattern, a.Guard))], false);
    }

    private readonly record struct TextSpanLike(Bayan.Syntax.Text.TextSpan Span);

    /// <summary>The function a case name reads: <c>e19u</c> and <c>e19r</c> are <c>e19</c> over a union.</summary>
    private static string BaseName(string name) => name.TrimEnd('u', 'r');

    private static List<string> Expected(string op, string name)
    {
        Directory.CreateDirectory(Dir);
        if (op == "diag")
        {
            var whole = Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), DiagSource(name))], packageRoot: Dir);
            return [.. whole.Diagnostics.Items.Where(d => T39Codes.Contains(d.Code)).SelectMany(d => Diag(d).Split('\n'))];
        }
        if (op == "lib")
            return [.. CompileLibrary(Lib[name]).Diagnostics.Items.Where(d => T39Codes.Contains(d.Code)).SelectMany(d => Diag(d).Split('\n'))];
        if (op == "wr")
        {
            var w = CompileLibrary(WrSource);
            var lines = new List<string>();
            foreach (var s in Function(w, "wr").Body!.Statements.OfType<BoundLocalDecl>())
                lines.Add($"{s.Local.Name} {(s.Local.Written ? "true" : "false")}");
            lines.AddRange(w.Diagnostics.Items.Where(d => T39Codes.Contains(d.Code)).SelectMany(d => Diag(d).Split('\n')));
            return lines;
        }
        var c = CompileLibrary(ExhSource);
        var file = c.Modules.SelectMany(m => m.Files).First();
        var (span, type, arms, statement) = SwitchOf(c, BaseName(name));
        if (Exh.First(e => e.Name == name).Union is { } sets)
            type = ErrorUnionType.Of([.. sets.Select(n => (ErrorSetSymbol)SwitchOf(c, n == "EmailError" ? "e23" : "e19").Type)]);
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, null, null, null, null], null)!;
        var mark = c.Diagnostics.Items.Count;
        var check = BinderType.GetMethod("CheckExhaustive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ok = (bool)check.Invoke(b, [span.Span, type, arms, statement])!;
        var result = new List<string> { $"result {(ok ? "true" : "false")}" };
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++) result.AddRange(Diag(items[i]).Split('\n'));
        return result;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases.SelectMany(k => new[] { k.Op, k.Name })]));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add($"{k.Op}.{k.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string section)
    {
        var parts = section.Split('.');
        SelfhostSemanticsHarness.AssertSection(Sections.Value, section, Expected(parts[0], parts[1]));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── Rebuilding the C# bound patterns in Bayan ────────────────────────────────

    /// <summary>The Bayan expression for a C# type of <see cref="ExhSource"/>.</summary>
    private static string Ty(TypeSymbol t) => t switch
    {
        PrimitiveType p => $"prim(.{p.DisplayName})",
        OptionalType o => $"internOptional(ref c, by: {Ty(o.Inner)})",
        ErrorUnionType { Sets.Count: 2, Excluded.Count: 0 } u => $"tcUnion2(ref c, for: \"{u.Sets[0].Name}\", by: \"{u.Sets[1].Name}\")",
        EnumSymbol or UnionSymbol or ErrorSetSymbol or StructSymbol => $"c.modules[c.entryModule].types[\"{t.DisplayName}\"]",
        _ => throw new InvalidOperationException(t.GetType().Name),
    };

    private static string Sp(Node n) => $"TextSpan {{ start: {n.Span.Start}, length: {n.Span.Length} }}";

    private sealed class Gen
    {
        private int _n;
        public readonly System.Text.StringBuilder Sb = new();

        private string Fresh(string prefix) => $"{prefix}{_n++}";

        public string Val(BoundExpr e)
        {
            var v = Fresh("v");
            var code = e switch
            {
                BoundDefault d => $"addDefault(ref c, from: BDefault {{ span: {Sp(d.Syntax)}, type: {Ty(d.Type)} }})",
                BoundErrorVariant ev => $"addErrorVariant(ref c, from: BErrorVariant {{ span: {Sp(ev.Syntax)}, variant: tcVariant(in c, for: \"{ev.Variant.Set.Name}\", by: \"{ev.Variant.Name}\") }})",
                BoundEnumMember em => $"addEnumMember(ref c, from: BEnumMember {{ span: {Sp(em.Syntax)}, member: tcMember(in c, for: \"{em.Member.Enum.Name}\", by: \"{em.Member.Name}\") }})",
                BoundLiteral { Value.Value: bool f } => $"tcBool(ref c, by: {(f ? "true" : "false")})",
                BoundLiteral l => $"tcInt(ref c, by: {l.Value.Value}, as: {Ty(l.Type)})",
                _ => throw new InvalidOperationException(e.GetType().Name),
            };
            Sb.AppendLine($"    let {v} = {code}");
            return v;
        }

        private string List(IEnumerable<BoundPattern> ps)
        {
            var parts = ps.Select(Pat).ToList();
            var l = Fresh("l");
            Sb.AppendLine($"    var {l} = List<BPattern>()");
            foreach (var p in parts) Sb.AppendLine($"    {l}.append({p})");
            return l;
        }

        public string Pat(BoundPattern p)
        {
            string code;
            var sp = Sp(p.Syntax);
            var t = Ty(p.InputType);
            switch (p)
            {
                case BoundBindPattern:
                    code = $"tcBind(ref c, at: {sp}, as: {t}, by: {(p.Syntax is DiscardPattern ? "true" : "false")})";
                    break;
                case BoundConstantPattern k:
                    code = $"addConstantPattern(ref c, from: BConstantPattern {{ span: {sp}, inputType: {t}, value: {Val(k.Value)} }})";
                    break;
                case BoundNotPattern n:
                    code = $"addNotPattern(ref c, from: BNotPattern {{ span: {sp}, inputType: {t}, operand: {Pat(n.Operand)} }})";
                    break;
                case BoundBinaryPattern bp:
                    var left = Pat(bp.Left);
                    var right = Pat(bp.Right);
                    code = $"addBinaryPattern(ref c, from: BBinaryPattern {{ span: {sp}, inputType: {t}, isAnd: {(bp.IsAnd ? "true" : "false")}, left: {left}, right: {right} }})";
                    break;
                case BoundUnionPattern up:
                    var ul = List(up.Subpatterns);
                    code = $"addUnionPattern(ref c, from: BUnionPattern {{ span: {sp}, inputType: {t}, variant: tcUVariant(in c, for: \"{up.Variant.Union.Name}\", by: \"{up.Variant.Name}\"), subpatterns: move {ul} }})";
                    break;
                case BoundErrorPattern ep:
                    var el = List(ep.Subpatterns);
                    code = $"addErrorPattern(ref c, from: BErrorPattern {{ span: {sp}, inputType: {t}, variant: tcVariant(in c, for: \"{ep.Variant.Set.Name}\", by: \"{ep.Variant.Name}\"), subpatterns: move {el} }})";
                    break;
                case BoundPropertyPattern pp:
                    var subs = pp.Subpatterns.Select(s => Pat(s.Pattern)).ToList();
                    var sl = Fresh("s");
                    Sb.AppendLine($"    var {sl} = List<BSubpattern>()");
                    foreach (var s in subs) Sb.AppendLine($"    {sl}.append(BSubpattern {{ pattern: {s} }})");
                    code = $"addPropertyPattern(ref c, from: BPropertyPattern {{ span: {sp}, inputType: {t}, valueType: {Ty(pp.ValueType)}, subpatterns: move {sl} }})";
                    break;
                default:
                    throw new InvalidOperationException(p.GetType().Name);
            }
            var v = Fresh("p");
            Sb.AppendLine($"    let {v} = {code}");
            return v;
        }
    }

    /// <summary>One Bayan function per <see cref="Exh"/> case that rebuilds its arms and calls <c>checkExhaustive</c>.</summary>
    private static string ExhFunctions()
    {
        var c = CompileLibrary(ExhSource);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("void tcExh(ref Ctx c, by string name) {");
        for (var i = 0; i < Exh.Length; i++) sb.AppendLine($"    if name == \"{Exh[i].Name}\" {{ tcExh{i}(ref c) }}");
        sb.AppendLine("}");
        for (var i = 0; i < Exh.Length; i++)
        {
            var (span, type, arms, statement) = SwitchOf(c, BaseName(Exh[i].Name));
            var g = new Gen();
            g.Sb.AppendLine("    var b = t13NewBinder(c.modules[c.entryModule].files[0])");
            var t = Exh[i].Union is { } sets ? $"tcUnion2(ref c, for: \"{sets[0]}\", by: \"{sets[1]}\")" : Ty(type);
            g.Sb.AppendLine($"    let t = {t}");
            g.Sb.AppendLine("    var arms = List<(BPattern, BExpr)>()");
            foreach (var (p, guard) in arms)
            {
                var pv = p is null ? "-1" : g.Pat(p);
                var gv = guard is null ? "-1" : "tcBool(ref c, by: true)";
                g.Sb.AppendLine($"    arms.append(({pv}, {gv}))");
            }
            g.Sb.AppendLine("    let start = c.diagnostics.length");
            g.Sb.AppendLine($"    let ok = checkExhaustive(ref b, through: ref c, at: TextSpan {{ start: {span.Span.Start}, length: {span.Span.Length} }}, as: t, by: in arms, statement: {(statement ? "true" : "false")})");
            g.Sb.AppendLine("    print($\"result {ok}\")");
            g.Sb.AppendLine("    tcDiags(ref c, from: start)");
            sb.AppendLine($"void tcExh{i}(ref Ctx c) {{");
            sb.Append(g.Sb);
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private static string Diags()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("string tcDiagSource(string name) {");
        foreach (var d in DiagCases.Concat(Lib.Keys)) sb.AppendLine($"    if name == \"{d}\" {{ return \"{Escape(DiagSource(d))}\" }}");
        sb.AppendLine("    return \"\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private static string MainSource() => """
        import lexer
        import parser

        const exhSource: string = "EXH\n"
        const wrSource: string = "WR\n"

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        /// `tcDiags` from 0, only the codes T39 reports.
        void tcT39Diags(ref Ctx c) {
            var keep = Map<string, bool>()
        KEEP
            for i in 0..c.diagnostics.length {
                if !keep.contains(c.diagnostics[i].code) { continue }
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        /// The BYN2115 half of C# `CheckMutation` (Compilation.cs:1854), whose port is T13's stub (Q694).
        void tcMutation(ref Ctx c) {
            for f in 0..c.functions.length {
                if c.thisWrites.contains(f) { reportMutatesWithoutRefThis(ref c, for: f, at: c.thisWrites[f].span) }
            }
        }

        BExpr tcInt(ref Ctx c, by i64 v, as Type t) {
            let k = Constant { kind: .integer, integer: bigFromI64(v) }
            let e = addLiteral(ref c, from: BLiteral { span: TextSpan { start: 0, length: 0 }, type: t, value: k })
            setConstant(ref c, for: e, to: k)
            return e
        }

        BExpr tcBool(ref Ctx c, by bool v) {
            let k = Constant { kind: .flag, flag: v }
            let e = addLiteral(ref c, from: BLiteral { span: TextSpan { start: 0, length: 0 }, type: prim(.bool), value: k })
            setConstant(ref c, for: e, to: k)
            return e
        }

        BPattern tcBind(ref Ctx c, at TextSpan sp, as Type t, by bool discard) {
            let p = addBindPattern(ref c, from: BBindPattern { span: sp, inputType: t })
            if discard { setSyntaxFlags(ref c, for: p, to: syntaxDiscard) }
            return p
        }

        VariantId tcVariant(in Ctx c, for string set, by string name) {
            for v in 0..c.errorVariants.length {
                if c.errorVariants[v].name == name && c.errorSets[c.errorVariants[v].set].name == set { return v }
            }
            return -1
        }

        VariantId tcUVariant(in Ctx c, for string u, by string name) {
            for v in 0..c.unionVariants.length {
                if c.unionVariants[v].name == name && c.unions[c.unionVariants[v].owner].name == u { return v }
            }
            return -1
        }

        EnumMemberId tcMember(in Ctx c, for string e, by string name) {
            for m in 0..c.enumMembers.length {
                if c.enumMembers[m].name == name && c.enums[c.enumMembers[m].owner].name == e { return m }
            }
            return -1
        }

        Type tcUnion2(ref Ctx c, for string a, by string b) {
            var sets = List<ErrorSetId>()
            sets.append(typeIndex(c.modules[c.entryModule].types[a]))
            sets.append(typeIndex(c.modules[c.entryModule].types[b]))
            let none = List<VariantId>()
            return errorUnionOf(ref c, for: in sets, as: in none)
        }

        /// Each `w*` local of `wr` and whether it was written.
        void tcWritten(ref Ctx c) {
            for l in 0..c.locals.length {
                let n = c.locals[l].name
                if n.length == 2 && n.startsWith("w") { print($"{n} {c.locals[l].written}") }
            }
        }

        DIAGS

        EXHFNS

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 1 < args.length {
                let op = args[i]
                let text = op == "diag" || op == "lib" ? tcDiagSource(args[i + 1]) : op == "wr" ? wrSource : exhSource
                var bytes = List<u8>()
                for x in text.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t39-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: op != "diag",
                    packageRoot: "tmp/t39-cases",
                    displayRoot: "tmp/t39-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t39-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {op}.{args[i + 1]}")
                if op == "diag" || op == "lib" {
                    bindAll(ref c)
                    tcMutation(ref c)
                    tcT39Diags(ref c)
                } else if op == "wr" {
                    bindAll(ref c)
                    tcMutation(ref c)
                    tcWritten(ref c)
                    tcT39Diags(ref c)
                } else {
                    tcExh(ref c, by: args[i + 1])
                }
                i += 2
            }
            return 0
        }
        """.Replace("EXHFNS", ExhFunctions()).Replace("EXH", Escape(ExhSource)).Replace("WR", Escape(WrSource))
            .Replace("KEEP", string.Concat(T39Codes.Select(k => $"    keep[\"{k}\"] = true\n"))).Replace("DIAGS", Diags());
}
