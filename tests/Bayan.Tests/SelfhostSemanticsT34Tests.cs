using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T34: <c>selfhost/semantics/calls3.byn</c> against the C# <c>BinderCalls</c> (BinderCalls.cs 1007-1577: <c>BindTry</c>
/// through <c>ResolveStep</c>). One source, <see cref="Source"/>, is compiled by both sides; each case binds one <c>let</c>
/// initializer of <c>probes</c> with <c>BindExpr</c> and prints the bound tree (try, catch, switch arms and every pattern
/// node, its locals with their borrow and guard marks), then the diagnostics the binding reported. Every expected line is
/// computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT34Tests
{
    private const string Dir = "tmp/t34-cases";

    private const string Source = """
        union error NetError {
            timeout
            closed(i64 code)
        }

        union error DiskError {
            full
        }

        union error NoError {
        }

        i64 fetch(i64 n) fails NetError {
            if n > 0 {
                fail .timeout
            }
            return n
        }

        void act(i64 n) fails NetError {
            if n > 0 {
                fail .timeout
            }
        }

        i64 plain(i64 n) => n

        struct Ledger {
            i64 total
            List<i64> items
            List<i64> more
            i64 twice => total * 2
        }

        union Shape {
            circle(i64 r)
            square(i64 side)
            empty
        }

        union Box {
            full(Ledger ledger)
            empty
        }

        struct Order {
            i64 id
            states {
                draft
                paid(i64 payment)
            }
        }

        void probes(i64 n, Shape sh, Box bx, Ledger lg, (i64 a, i64 b) tp, i64? on, Order od, Order.paid op, rune ch, string s, NetError ne, ReadOnlySpan<u8> bytes, Box mb, Ledger ml, NoError none, Ledger? ol) {
            let t1 = try fetch(n)
            let t2 = try n
            let t3 = try (fetch(n))
            let t4 = try plain(n)
            let t5 = try fetch(fetch(n))
            let t6 = try string(bytes)
            let c1 = fetch(n) catch 0
            let c2 = fetch(n) catch (.timeout) 0
            let c3 = fetch(n) catch (.nope) 0
            let c4 = act(n) catch { n = 1 }
            let c5 = fetch(n) catch "x"
            let c6 = fetch(n) catch switch { .timeout => 1, .closed(let code) => code }
            let c7 = fetch(n) catch switch { NetError e => 1 }
            let c8 = n catch 0
            let c9 = fetch(n) catch (DiskError.full) 0
            let c10 = act(n) catch 0
            let c11 = (fetch(n)) catch 0
            let s1 = sh switch { .circle(let r) => r, .square(let side) => side, .empty => 0 }
            let s2 = sh switch { .circle(let r) when r > 0 => r, _ => 0 }
            let s3 = sh switch { .nope(let r) => r, _ => 0 }
            let s4 = sh switch { .circle(let a, let b) => a, _ => 0 }
            let s5 = sh switch { .empty(let a) => a, _ => 0 }
            let s6 = sh switch { .circle c => c.r, _ => 0 }
            let s7 = sh switch { Box.full(let l) => 1, _ => 0 }
            let s8 = n switch { 1..5 => 1, 7 => 2, > 9 => 3, not 8 => 4, _ => 0 }
            let s9 = n switch { 1 or 2 => 1, var x when x > 3 => x, _ => 0 }
            let s10 = lg switch { { total: let t } => t }
            let s11 = lg switch { { nope: let t } => 0 }
            let s12 = tp switch { { b: let y } => y }
            let s13 = on switch { null => 0, { } v => v }
            let s14 = ol switch { { total: let t } => t, _ => 0 }
            let s15 = n switch { "x" => 1, _ => 0 }
            let s16 = n switch { s => 1, _ => 0 }
            let s17 = od switch { .draft => 1, .paid(let p) => p }
            let s18 = od switch { .nope => 1, _ => 0 }
            let s19 = od switch { .paid p => p.payment, _ => 0 }
            let s20 = ne switch { .timeout => 1, _ => 0 }
            let s21 = ne switch { NetError => 1 }
            let s22 = ne switch { DiskError d => 1, _ => 0 }
            let s23 = sh switch { .empty => 0, _ => 1 }
            let s24 = (move mb) switch { .full(let l) and .full(let m) => 1, _ => 0 }
            let s25 = (move ml) switch { { items: let a, items: let b } => 1 }
            let s26 = (move ml) switch { Ledger { items: let a } w => 1 }
            let s27 = (move mb) switch { .full(let l) when l.total > 0 => 1, _ => 0 }
            let s28 = n switch { 1 => "a", _ => 2 }
            let s29 = n switch { _ => null }
            let s30 = n switch { }
            let s31 = op switch { { payment: let p } => p }
            let s32 = od switch { Order.paid(let p) => p, _ => 0 }
            let s33 = od switch { Order.nope(let p) => p, _ => 0 }
            let s34 = ch switch { 'a'..'z' => 1, _ => 0 }
            let s35 = s switch { 1..2 => 1, _ => 0 }
            let s36 = n switch { 1..n => 1, _ => 0 }
            let s37 = none switch { NoError e => 1 }
            let s38 = ne switch { DiskError.full => 1, _ => 0 }
            let s39 = n switch { null => 1, _ => 0 }
            let s40 = lg switch { Box { total: let t } => t }
            let s41 = lg switch { Ledger { total: let t } w => t }
            let s42 = sh switch { .circle(let r) or .square(let r) => 1, _ => 0 }
            let s43 = (move ml) switch { { items: let a, more: let b } => 1 }
            let s44 = (move mb) switch { .full w => 1, _ => 0 }
            let s45 = sh switch { .circle => 1, _ => 0 }
            let s46 = od switch { .paid(let p, let q) => 1, _ => 0 }
            let s47 = n switch { .circle(let r) => 1, _ => 0 }
            let s48 = n switch { [1, 2] => 1, _ => 0 }
            let s49 = n switch { [1, ..] => 1, _ => 0 }
            let s50 = tp switch { (let x, let y) => x }
            let s51 = tp switch { (let x) => x, _ => 0 }
            let s52 = ne switch { NetError.closed(let k) => k, _ => 0 }
            let s53 = sh switch { Shape x => 1 }
            let s54 = n switch { not var x => 1, _ => 0 }
            let s55 = sh switch { Box.empty => 1, _ => 0 }
            let s56 = n switch { Shape => 1, _ => 0 }
            let s57 = lg switch { { twice: let w } => w }
            let s58 = nope switch { { a: let x } => 1, _ => 0 }
            let s59 = (move mb) switch { .full({ items: let a, items: let b }) => 1, _ => 0 }
            let s60 = (move ml) switch { not { items: let a, items: let b } => 1, _ => 0 }
            let s61 = (move ml) switch { { items: let a } or { more: let a } => 1 }
            let s62 = n switch { 1 and var x => x, _ => 0 }
            let s63 = sh switch { .circle(_) => 1, _ => 0 }
            let s64 = n switch { < 3 => 1, _ => 0 }
            let s65 = lg switch { { total: { } t } => 1 }
            let s66 = ne switch { NetError.nope(let k) => k, _ => 0 }
            let s67 = (move mb) switch { .full(let l) => 1, .empty => 0 }
            let c12 = act(n) catch (.timeout) { }
            let c13 = fetch(n) catch switch { DiskError.full => 1, _ => 0 }
            let c14 = fetch(n) catch switch { NetError => 1 }
            let c15 = fetch(n) catch switch { DiskError e => 1, _ => 0 }
            let c16 = fetch(n) catch switch { NetError.closed(let k) => k, _ => 0 }
            let c17 = fetch(n) catch (.closed) 0
            let c18 = fetch(n) catch { }
        }
        """;

    private static readonly string[] Names =
    [
        "t1", "t2", "t3", "t4", "t5", "t6",
        .. Enumerable.Range(1, 18).Select(i => $"c{i}"),
        .. Enumerable.Range(1, 67).Select(i => $"s{i}"),
        "dt1", "dt2",
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

    /// <summary>The Bayan <c>BoundKind</c> name of a C# bound node.</summary>
    private static string Kind(BoundNode node) => node switch
    {
        BoundDefault => "defaultValue", BoundTry => "tryExpr", BoundCatch => "catchExpr", BoundMove => "moveExpr", BoundThis => "thisRef",
        _ => Camel(node.GetType().Name[5..]),
    };

    private static string Span(BoundNode n) => $"{n.Syntax.Span.Start}:{n.Syntax.Span.Length}";

    /// <summary>A child expression: kind:type, a literal's value, a conversion's kind and operand, an error variant's name.</summary>
    private static string Sub(BoundExpr? e) => e switch
    {
        null => "-",
        BoundLiteral l => $"literal:{l.Type.DisplayName}={l.Value}",
        BoundConversion v => $"conversion:{v.Type.DisplayName}({Camel(v.Kind.ToString())} {Sub(v.Operand)})",
        BoundLocal l => $"local:{l.Type.DisplayName}={l.Local.Name}",
        BoundErrorVariant v => $"errorVariant:{v.Type.DisplayName}={v.Variant.Name}",
        BoundUnionNew u => $"unionNew:{u.Type.DisplayName}={u.Variant.Name}",
        _ => $"{Kind(e)}:{e.Type.DisplayName}",
    };

    private static string Local(LocalSymbol? l) => l is null ? "-"
        : $"{l.Name}:{l.Type.DisplayName}:{(l.BorrowsSubject ? "b" : "o")}:{(l.GuardSpan is { } g ? $"{g.Start}:{g.Length}" : "-")}";

    private static string Step(BoundMemberStep s) =>
        (s.Field is { } f ? f.Name : s.Property is { } p ? "p." + p.Name : s.TupleIndex >= 0 ? $"#{s.TupleIndex}" : s.Builtin is { } m ? "b." + Camel(m.ToString()) : "?")
        + ":" + s.Type.DisplayName;

    private static string Pattern(BoundPattern p)
    {
        var head = $"{Kind(p)}[{Span(p)}]:{p.InputType.DisplayName}{(p.Syntax is DiscardPattern ? "_" : "")}";
        return head + p switch
        {
            BoundConstantPattern k => $"({Sub(k.Value)})",
            BoundRelationalPattern r => $"({Camel(r.Op.ToString())} {Sub(r.Value)})",
            BoundRangePattern r => $"({Sub(r.Start)},{Sub(r.End)},{(r.Inclusive ? "true" : "false")})",
            BoundBindPattern b => $"({Local(b.Local)})",
            BoundNotPattern n => $"({Pattern(n.Operand)})",
            BoundBinaryPattern b => $"({(b.IsAnd ? "and" : "or")} {Pattern(b.Left)},{Pattern(b.Right)})",
            BoundPropertyPattern pp => $"({pp.ValueType.DisplayName};{string.Join(";", pp.Subpatterns.Select(s => string.Join(".", s.Path.Select(Step)) + "=" + Pattern(s.Pattern)))}|{Local(pp.Designation)})",
            BoundUnionPattern u => $"({u.Variant.Name};{string.Join(";", u.Subpatterns.Select(Pattern))}|{Local(u.Designation)})",
            _ => "",
        };
    }

    /// <summary>The bound expression and, for a try, catch or switch, its parts.</summary>
    private static List<string> Dump(BoundExpr e)
    {
        var lines = new List<string> { $"{Kind(e)}|{e.Type.DisplayName}|{Span(e)}" };
        switch (e)
        {
            case BoundTry t:
                lines.Add($"operand {Sub(t.Operand)}");
                break;
            case BoundCatch k:
                lines.Add($"operand {Sub(k.Operand)} binding {Local(k.Binding)} variant {k.Variant?.Name ?? "-"} handler {Sub(k.Handler)}");
                if (((CatchExpr)k.Syntax).Variant is { } vs) lines.Add($"variantSpan {vs.Span.Start}:{vs.Span.Length}");
                if (k.Handler is BoundSwitchExpr hs) lines.AddRange(Dump(hs));
                break;
            case BoundSwitchExpr s:
                lines.Add($"value {Sub(s.Value)}");
                foreach (var a in s.Arms) lines.Add($"arm {a.Syntax.Span.Start}:{a.Syntax.Span.Length} {Pattern(a.Pattern)} guard {Sub(a.Guard)} value {Sub(a.Value)}");
                break;
        }
        return lines;
    }

    private static readonly HashSet<string> Lets = ["mb", "ml"];

    /// <summary>
    /// Codes only T39's stubs report: BYN2712 and BYN2410 from <c>CheckExhaustive</c> (BinderCalls.cs:1601, :1626; the
    /// calls4.byn stub returns true), BYN2104 from <c>BindAssign</c>'s <c>CheckWritable</c> (BinderCalls.cs:1962).
    /// </summary>
    private static readonly HashSet<string> StubCodes = ["BYN2712", "BYN2410", "BYN2104"];

    /// <summary>The C# lines for one case: the bound tree, then the diagnostics its binding reported.</summary>
    private static List<string> Expected(string name)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "main.byn");
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        var mark = c.Diagnostics.Items.Count;
        var fd = file.Tree.Root.Members.OfType<FunctionDecl>().First(f => f.Name.Text == "probes");
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, null, null, null, null], null)!;
        foreach (var p in fd.Params)
            Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), Lets.Contains(p.Name.Text) ? LocalKind.Let : LocalKind.Parameter);
        // `dt1` binds `t1` inside a `defer` (the C# `_deferDepth`).
        if (name.StartsWith('d')) BinderType.GetField("_deferDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(b, 1);
        var let = name.StartsWith('d') ? name[1..] : name;
        var init = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == let).Init!;
        var lines = Dump((BoundExpr)Call(b, "BindExpr", init, null)!);
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++)
            if (!StubCodes.Contains(items[i].Code)) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), Names));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var n in Names) data.Add(n);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string name) => SelfhostSemanticsHarness.AssertSection(Sections.Value, name, Expected(name));

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        string tcSpan(in Ctx c, for BNode n) {
            let sp = nodeSpan(in c, for: n)
            return $"{sp.start}:{sp.length}"
        }

        string tcSub(ref Ctx c, for BExpr e) {
            if e == -1 { return "-" }
            let k = boundKind(e)
            let typeName = typeDisplay(ref c, by: exprType(in c, for: e))
            let i = boundIndex(e)
            if k == .literal { return $"literal:{typeName}={constantText(in c.bLiterals[i].value)}" }
            if k == .conversion { return $"conversion:{typeName}({c.bConversions[i].kind} {tcSub(ref c, for: c.bConversions[i].operand)})" }
            if k == .local { return $"local:{typeName}={c.locals[c.bLocals[i].local].name}" }
            if k == .errorVariant { return $"errorVariant:{typeName}={c.errorVariants[c.bErrorVariants[i].variant].name}" }
            if k == .unionNew { return $"unionNew:{typeName}={c.unionVariants[c.bUnionNews[i].variant].name}" }
            return $"{k}:{typeName}"
        }

        string tcLocal(ref Ctx c, for LocalId l) {
            if l == -1 { return "-" }
            let borrows = c.locals[l].borrowsSubject ? "b" : "o"
            var guard = "-"
            if (c.locals[l].guardSpan is { } g) { guard = $"{g.start}:{g.length}" }
            return $"{c.locals[l].name}:{typeDisplay(ref c, by: c.locals[l].type)}:{borrows}:{guard}"
        }

        string tcStep(ref Ctx c, for in BMemberStep s) {
            var head = "?"
            if s.field != -1 {
                head = c.fields[s.field].name
            } else if s.propertyId != -1 {
                head = $"p.{c.properties[s.propertyId].name}"
            } else if s.tupleIndex >= 0 {
                head = $"#{s.tupleIndex}"
            } else if (s.builtin is { } m) {
                head = $"b.{m}"
            }
            return $"{head}:{typeDisplay(ref c, by: s.type)}"
        }

        string tcPattern(ref Ctx c, for BPattern p) {
            let k = boundKind(p)
            let i = boundIndex(p)
            let discard = hasSyntax(in c, for: p, by: syntaxDiscard) ? "_" : ""
            var input: Type = -1
            var tail = ""
            switch k {
                case .constantPattern:
                    input = c.bConstantPatterns[i].inputType
                    tail = $"({tcSub(ref c, for: c.bConstantPatterns[i].value)})"
                case .relationalPattern:
                    input = c.bRelationalPatterns[i].inputType
                    tail = $"({c.bRelationalPatterns[i].op} {tcSub(ref c, for: c.bRelationalPatterns[i].value)})"
                case .rangePattern:
                    input = c.bRangePatterns[i].inputType
                    tail = $"({tcSub(ref c, for: c.bRangePatterns[i].start)},{tcSub(ref c, for: c.bRangePatterns[i].end)},{c.bRangePatterns[i].inclusive})"
                case .bindPattern:
                    input = c.bBindPatterns[i].inputType
                    tail = $"({tcLocal(ref c, for: c.bBindPatterns[i].local)})"
                case .notPattern:
                    input = c.bNotPatterns[i].inputType
                    tail = $"({tcPattern(ref c, for: c.bNotPatterns[i].operand)})"
                case .binaryPattern:
                    input = c.bBinaryPatterns[i].inputType
                    let op = c.bBinaryPatterns[i].isAnd ? "and" : "or"
                    tail = $"({op} {tcPattern(ref c, for: c.bBinaryPatterns[i].left)},{tcPattern(ref c, for: c.bBinaryPatterns[i].right)})"
                case .propertyPattern:
                    input = c.bPropertyPatterns[i].inputType
                    var parts = StrBuf()
                    for n in 0..c.bPropertyPatterns[i].subpatterns.length {
                        if n > 0 { parts.append(";") }
                        for s in 0..c.bPropertyPatterns[i].subpatterns[n].path.length {
                            if s > 0 { parts.append(".") }
                            let step = c.bPropertyPatterns[i].subpatterns[n].path[s]
                            parts.append(tcStep(ref c, for: in step))
                        }
                        parts.append("=")
                        parts.append(tcPattern(ref c, for: c.bPropertyPatterns[i].subpatterns[n].pattern))
                    }
                    tail = $"({typeDisplay(ref c, by: c.bPropertyPatterns[i].valueType)};{parts.finish()}|{tcLocal(ref c, for: c.bPropertyPatterns[i].designation)})"
                case .unionPattern:
                    input = c.bUnionPatterns[i].inputType
                    var parts = StrBuf()
                    for n in 0..c.bUnionPatterns[i].subpatterns.length {
                        if n > 0 { parts.append(";") }
                        parts.append(tcPattern(ref c, for: c.bUnionPatterns[i].subpatterns[n]))
                    }
                    tail = $"({c.unionVariants[c.bUnionPatterns[i].variant].name};{parts.finish()}|{tcLocal(ref c, for: c.bUnionPatterns[i].designation)})"
                case .errorPattern:
                    input = c.bErrorPatterns[i].inputType
                default: {}
            }
            let shown = input == -1 ? "?" : typeDisplay(ref c, by: input)
            return $"{k}[{tcSpan(in c, for: p)}]:{shown}{discard}{tail}"
        }

        void tcDump(ref Ctx c, for BExpr e) {
            let k = boundKind(e)
            let i = boundIndex(e)
            print($"{k}|{typeDisplay(ref c, by: exprType(in c, for: e))}|{tcSpan(in c, for: e)}")
            switch k {
                case .tryExpr:
                    print($"operand {tcSub(ref c, for: c.bTries[i].operand)}")
                case .catchExpr:
                    let variant = c.bCatches[i].variant == -1 ? "-" : c.errorVariants[c.bCatches[i].variant].name
                    print($"operand {tcSub(ref c, for: c.bCatches[i].operand)} binding {tcLocal(ref c, for: c.bCatches[i].binding)} variant {variant} handler {tcSub(ref c, for: c.bCatches[i].handler)}")
                    if (c.catchVariantSpans.tryGet(at: e) is { } vs) { print($"variantSpan {vs.start}:{vs.length}") }
                    let handler = c.bCatches[i].handler
                    if boundKind(handler) == .switchExpr { tcDump(ref c, for: handler) }
                case .switchExpr:
                    print($"value {tcSub(ref c, for: c.bSwitchExprs[i].value)}")
                    for n in 0..c.bSwitchExprs[i].arms.length {
                        let arm = c.bSwitchExprs[i].arms[n]
                        print($"arm {arm.span.start}:{arm.span.length} {tcPattern(ref c, for: arm.pattern)} guard {tcSub(ref c, for: arm.guard)} value {tcSub(ref c, for: arm.value)}")
                    }
                default: {}
            }
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        void runCase(ref Ctx c, by string name) {
            let file = c.modules[c.entryModule].files[0]
            var b = t13NewBinder(file)
            pushScope(ref b)
            var target = name
            if name.startsWith("d") {
                b.deferDepth = 1
                target = name[1..name.length]
            }
            let start = c.diagnostics.length
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if t15Text(in c, from: file, at: fd.value.name) == "probes" {
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let pn = t15Text(in c, from: file, at: p.name)
                                let kind: LocalKind = pn == "mb" || pn == "ml" ? .letKeyword : .parameter
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: kind)
                            }
                            switch fd.value.body {
                                case .blockStmt(let blk):
                                    for s in blk.value.statements {
                                        switch s {
                                            case .localDecl(let ld):
                                                if t15Text(in c, from: file, at: ld.value.name) == target { tcDump(ref c, for: bindExpr(ref b, through: ref c, for: in ld.value.init)) }
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
            for a in args {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t34-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t34-cases",
                    displayRoot: "tmp/t34-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t34-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {a}")
                runCase(ref c, by: a)
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source));
}
