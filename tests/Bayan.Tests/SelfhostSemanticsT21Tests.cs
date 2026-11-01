using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T21: <c>selfhost/semantics/calls2.byn</c> against the C# <c>BinderCalls</c> (BinderCalls.cs 596-1005).
/// One source, <see cref="Source"/>, is compiled by both sides: the C# side via <c>Compilation.Create</c> plus
/// reflection on the C# <c>Binder</c>, the Bayan side via <c>loadModule</c> + <c>declare</c> in the generated main run
/// by <c>SelfhostSemanticsHarness.RunSections</c>. Each case runs one operation on one <c>let</c> initializer of
/// <c>probes</c> on both sides; every expected line is computed from the C#. T38's <c>declareCheck</c> and T15's
/// <c>bindChecks</c> are stubs, so the Bayan side gets each struct's checks generated from the C# ones (<see cref="Checks"/>).
/// </summary>
public sealed class SelfhostSemanticsT21Tests
{
    private const string Dir = "tmp/t21-cases";

    private const string Source = """
        struct Plain {
            i64 v
        }

        struct Deft {
            i64 a = 1
            string s = "x"
        }

        struct Need {
            i64 a
            i64 b
            i64 c = 3
        }

        struct Made {
            i64 v = 0

            Made() {
            }
        }

        @positional
        struct Pt {
            i64 x
            i64 y
        }

        type distinct Tag = i64

        type distinct Qty = i32 requires it > 0

        struct Pos {
            i64 v requires it > 0
        }

        struct Holder {
            List<i64> items
            i64 n
        }

        void probes(i64 n, i32 small, string s, ReadOnlySpan<u8> bytes, List<i64> ns, Map<string, i64> mp, ref StrBuf buf, Untrusted<string> un, move List<i64> mv, Holder h, ref i64 rn, char ch, Range<i64> rg, List<List<i64>> nested, Map<string, List<i64>> mnest, Map<string, Plain> mplain) {
            let k1 = Plain()
            let k2 = Plain(1)
            let k3 = Deft()
            let k4 = Need()
            let k5 = Made()
            let k6 = Pt(1, 2)
            let k7 = Tag(5)
            let k8 = List<i64>()
            let k9 = List<i64>(1)
            let k10 = Map<string, i64>()
            let k11 = Arena()
            let k12 = StrBuf()
            let k13 = StrBuf(capacity: 8)
            let k14 = StrBuf(bytes)
            let k15 = i32(n)
            let k16 = string(n)
            let k17 = string(n, as: .hex)
            let k18 = string(bytes)
            let k19 = string(s)
            let k20 = bool(1)
            let k21 = i32(1, 2)
            let k22 = Owned(n)
            let k23 = Owned<i64>(n)
            let k24 = Untrusted(s)
            let k25 = Untrusted<string>(s)
            let k26 = Owned(nope)
            let k27 = string(nope)
            let k28 = Pt(1)
            let t1 = trust(un, reason: "checked upstream")
            let t2 = trust(un)
            let t3 = trust(un, reason: s)
            let t4 = trust(n, reason: "x")
            let t5 = trust(un, reason: "")
            let t6 = un.trustUnchecked() because "ok"
            let t7 = n.trustUnchecked() because "x"
            let t8 = nope.trustUnchecked() because "x"
            let b1 = s.parts(by: ",")
            let b2 = s.parts(by: ch)
            let b3 = buf.append(n)
            let b4 = buf.append(s)
            let b5 = ns.remove(at: 0)
            let b6 = ns.remove(at: 0..2)
            let b7 = ns.remove(at: rg)
            let b8 = ns.remove(at: 0, through: 2)
            let b9 = s.trimmed()
            let b10 = s.trimmed(by: "ab")
            let b11 = s.equals(s, as: .ignoreCase)
            let b12 = mp.slot(at: "k")
            let b13 = nested.copy()
            let b14 = mnest.copy()
            let b15 = ns.copy()
            let b16 = ns.take()
            let b17 = mplain.slot(at: "k")
            let q1 = Qty(5)
            let q2 = Qty(small)
            let p1 = Pos { v: 1 }
            let p2 = Pos { v: 0 }
            let p3 = Pos { v: n }
            let w1 = n
            let w2 = move n
            let w3 = (move mv)
            let w4 = move nope
            let w5 = move h.items
            let w6 = move ns[0]
            let w7 = move 1
            let w8 = move rn
            let w9 = move Plain { v: 1 }
        }
        """;

    private static readonly (string Op, string Name)[] Cases =
    [
        ("call", "k1"), ("call", "k2"), ("call", "k3"), ("call", "k4"), ("call", "k5"), ("call", "k6"), ("call", "k7"),
        ("call", "k8"), ("call", "k9"), ("call", "k10"), ("call", "k11"), ("call", "k12"), ("call", "k13"), ("call", "k14"),
        ("call", "k15"), ("call", "k16"), ("call", "k17"), ("call", "k18"), ("call", "k19"), ("call", "k20"), ("call", "k21"),
        ("call", "k22"), ("call", "k23"), ("call", "k24"), ("call", "k25"), ("call", "k26"), ("call", "k27"), ("call", "k28"),
        ("call", "t1"), ("call", "t2"), ("call", "t3"), ("call", "t4"), ("call", "t5"), ("call", "t6"), ("call", "t7"), ("call", "t8"),
        ("call", "b1"), ("call", "b2"), ("call", "b3"), ("call", "b4"), ("call", "b5"), ("call", "b6"), ("call", "b7"),
        ("call", "b8"), ("call", "b9"), ("call", "b10"), ("call", "b11"), ("call", "b12"), ("call", "b13"), ("call", "b14"),
        ("call", "b15"), ("call", "b16"), ("call", "b17"),
        ("call", "q1"), ("call", "q2"), ("cov", "q2"), ("hand", "q2"),
        ("expr", "p1"), ("expr", "p2"), ("expr", "p3"), ("cov", "p3"), ("hand", "p3"),
        ("subj", "w1"), ("subj", "w2"), ("subj", "w3"), ("subj", "w4"), ("subj", "w5"), ("subj", "w6"), ("subj", "w7"),
        ("subj", "w8"), ("subj", "w9"),
        ("handles", "k1"), ("handles", "p1"), ("handles", "w1"),
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
        BoundLiteral => "literal", BoundLocal => "local", BoundThis => "thisRef", BoundField => "field",
        BoundTupleElement => "tupleElement", BoundCall => "call", BoundBuiltinCall => "builtinCall", BoundIndex => "index",
        BoundEnumMember => "enumMember", BoundErrorExpr => "errorExpr", BoundMove => "moveExpr",
        _ => char.ToLowerInvariant(node.GetType().Name[5]) + node.GetType().Name[6..],
    };

    private static string Recv(BoundExpr? r) => r switch
    {
        null => "-1", BoundThis => "this", BoundLocal l => "local:" + l.Local.Name, _ => NodeKindName(r),
    };

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>A child expression: kind:type, a literal's value, a conversion's kind and operand.</summary>
    private static string Sub(BoundExpr? e) => e switch
    {
        null => "-",
        BoundLiteral l => $"literal:{l.Type.DisplayName}={l.Value}",
        BoundConversion v => $"conversion:{v.Type.DisplayName}({Camel(v.Kind.ToString())} {Sub(v.Operand)})",
        BoundEnumMember m => $"enumMember:{m.Type.DisplayName}={m.Member.Name}",
        BoundLocal l => $"local:{l.Type.DisplayName}={l.Local.Name}",
        _ => $"{NodeKindName(e)}:{e.Type.DisplayName}",
    };

    /// <summary>kind|type|receiver|callee|span|sub: every field a port bug could change.</summary>
    private static string DumpNode(BoundNode? node)
    {
        if (node is null) return "null";
        var type = node is BoundExpr e ? e.Type.DisplayName : "-";
        var span = $"{node.Syntax.Span.Start}:{node.Syntax.Span.Length}";
        var recv = "-"; var callee = "-"; var sub = "-";
        switch (node)
        {
            case BoundCall c: callee = c.Function.Name; recv = Recv(c.Receiver); sub = string.Join(",", c.Arguments.Select(Sub)); break;
            case BoundBuiltinCall b: callee = Camel(b.Member.ToString()); recv = Sub(b.Receiver); sub = string.Join(",", b.Arguments.Select(Sub)); break;
            case BoundLocal l: callee = l.Local.Name; break;
            case BoundCollection col: sub = string.Join(",", col.Elements.Select(Sub)); break;
            case BoundStructLiteral sl: sub = string.Join(",", sl.Fields.Select(Sub)); break;
            case BoundConversion v: sub = Sub(v); break;
            case BoundMove m: sub = Sub(m.Operand); break;
            case BoundLiteral l: sub = l.Value.ToString()!; break;
        }
        return $"{NodeKindName(node)}|{type}|{recv}|{callee}|{span}|{sub}";
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));

    /// <summary>The C# lines for one case: what the operation gives, then the diagnostics it reported.</summary>
    private static List<string> Expected(string op, string name)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "main.byn");
        var c = Compile(path);
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        var mark = c.Diagnostics.Items.Count;
        var fd = file.Tree.Root.Members.OfType<FunctionDecl>().First(f => f.Name.Text == "probes");
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, null, null, null, null], null)!;
        // `mv` is declared a `let`, so `move mv` takes the path a movable local takes.
        foreach (var p in fd.Params) Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), p.Name.Text == "mv" ? LocalKind.Let : LocalKind.Parameter);
        var init = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name).Init!;
        var lines = new List<string>();
        switch (op)
        {
            case "call":
                lines.Add(DumpNode((BoundNode?)Call(b, "BindCall", init, null)));
                break;
            case "expr":
                lines.Add(DumpNode((BoundNode?)Call(b, "BindExpr", init, null)));
                break;
            case "cov":
            case "hand":
                // `cov` binds inside a `try` operand (the C# `_covered` set), `hand` as the value a `try` handles (`_handled`).
                var covered = new List<(Expr Call, string Name)>();
                BinderType.GetField("_covered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(b, covered);
                if (op == "hand") BinderType.GetField("_handled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(b, init);
                lines.Add(DumpNode((BoundNode?)Call(b, "BindExpr", init, null)));
                foreach (var e in covered) lines.Add($"covered {e.Call.Span.Start}:{e.Call.Span.Length} {e.Name} {(e.Call is StructLiteralExpr ? "true" : "false")}");
                break;
            case "subj":
                var subject = BinderType.GetMethod("BindSwitchSubject", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object?[] args = [init, false];
                lines.Add(DumpNode((BoundNode?)subject.Invoke(b, args)));
                lines.Add($"owns {((bool)args[1]! ? "true" : "false")}");
                break;
            case "handles":
                lines.Add((bool)Call(null, "Handles", init)! ? "true" : "false");
                break;
        }
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
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

    /// <summary>A rule condition of the C# checks as the Bayan expression that builds it.</summary>
    private static string Cond(BoundExpr e) => e switch
    {
        BoundBinary x => $"addBinary(ref c, from: BBinary {{ span: sp, op: .{Camel(x.Op.ToString())}, left: {Cond(x.Left)}, right: {Cond(x.Right)}, type: prim(.{x.Type.DisplayName}) }})",
        BoundField { Receiver: BoundThis } f => $"addField(ref c, from: BField {{ span: sp, receiver: addThis(ref c, from: BThis {{ span: sp, type: structTypeOf(s) }}), field: tcField(in c, for: s, by: \"{f.Field.Name}\"), type: prim(.{f.Type.DisplayName}) }})",
        BoundLiteral l => $"tcInt(ref c, by: {l.Value.Value}, as: prim(.{l.Type.DisplayName}))",
        _ => throw new InvalidOperationException(e.GetType().Name),
    };

    /// <summary>
    /// What T38's stubbed <c>declareCheck</c> (and T15's blocked <c>bindChecks</c>) would set: each struct's check, with
    /// its rules generated from the C# <c>Compilation.Checks</c>.
    /// </summary>
    private static string Checks()
    {
        Directory.CreateDirectory(Dir);
        var c = Compile(Path.Combine(Dir, "main.byn"));
        var all = (System.Collections.IDictionary)typeof(Compilation).GetProperty("Checks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        var sb = new System.Text.StringBuilder();
        foreach (System.Collections.DictionaryEntry entry in all)
        {
            var s = (StructSymbol)entry.Key;
            var checks = (StructChecks)entry.Value!;
            sb.AppendLine("    {");
            sb.AppendLine($"    let s = typeIndex(c.modules[c.entryModule].types[\"{s.Name}\"])");
            sb.AppendLine("    let fn = newFunction(ref c, by: FunctionSymbol { name: \"requires\", module: c.entryModule, owner: s, file: c.structs[s].file })");
            sb.AppendLine("    var rules = List<StructRule>()");
            foreach (var r in checks.Rules)
            {
                var span = r.Syntax.Condition.Span;
                sb.AppendLine($"    let sp = TextSpan {{ start: {span.Start}, length: {span.Length} }}");
                sb.AppendLine("    var reads = List<FieldId>()");
                foreach (var f in r.Reads) sb.AppendLine($"    reads.append(tcField(in c, for: s, by: \"{f.Name}\"))");
                var field = r.Field is { } own ? $"tcField(in c, for: s, by: \"{own.Name}\")" : "-1";
                sb.AppendLine($"    rules.append(StructRule {{ span: sp, field: {field}, condition: {Cond(r.Condition)}, reads: move reads }})");
            }
            sb.AppendLine("    c.checksOf[s] = c.checks.length");
            sb.AppendLine("    c.checks.append(StructChecks { check: fn, rules: move rules })");
            sb.AppendLine("    }");
        }
        return sb.ToString();
    }

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        string tcRecv(ref Ctx c, for BExpr r) {
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
            if k == .enumMember { return $"enumMember:{typeName}={c.enumMembers[c.bEnumMembers[boundIndex(e)].member].name}" }
            if k == .local { return $"local:{typeName}={c.locals[c.bLocals[boundIndex(e)].local].name}" }
            return $"{k}:{typeName}"
        }

        string tcSubs(ref Ctx c, for in List<BExpr> es) {
            var joined = ""
            var first = true
            for a in copyIds(in es) {
                joined = first ? tcSub(ref c, for: a) : $"{joined},{tcSub(ref c, for: a)}"
                first = false
            }
            return joined
        }

        void tcDump(ref Ctx c, for BExpr e) {
            if e == -1 {
                print("null")
                return
            }
            let k = boundKind(e)
            let t = exprType(in c, for: e)
            let typeName = typeDisplay(ref c, by: t)
            let i = boundIndex(e)
            var recv = "-"
            var callee = "-"
            var sub = "-"
            switch k {
                case .call:
                    callee = c.functions[c.bCalls[i].function].name
                    recv = tcRecv(ref c, for: c.bCalls[i].receiver)
                    let args = copyIds(in c.bCalls[i].arguments)
                    sub = tcSubs(ref c, for: in args)
                case .builtinCall:
                    callee = $"{c.bBuiltinCalls[i].member}"
                    recv = tcSub(ref c, for: c.bBuiltinCalls[i].receiver)
                    let args = copyIds(in c.bBuiltinCalls[i].arguments)
                    sub = tcSubs(ref c, for: in args)
                case .local:
                    callee = c.locals[c.bLocals[i].local].name
                case .collection:
                    let args = copyIds(in c.bCollections[i].elements)
                    sub = tcSubs(ref c, for: in args)
                case .structLiteral:
                    let args = copyIds(in c.bStructLiterals[i].fields)
                    sub = tcSubs(ref c, for: in args)
                case .conversion:
                    sub = tcSub(ref c, for: e)
                case .moveExpr:
                    sub = tcSub(ref c, for: c.bMoves[i].operand)
                case .literal:
                    sub = constantText(in c.bLiterals[i].value)
                default: {}
            }
            let sp = nodeSpan(in c, for: e)
            print($"{k}|{typeName}|{recv}|{callee}|{sp.start}:{sp.length}|{sub}")
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        FieldId tcField(in Ctx c, for StructId s, by string name) {
            for f in c.structs[s].fields {
                if c.fields[f].name == name { return f }
            }
            return -1
        }

        BExpr tcInt(ref Ctx c, by i64 v, as Type t) {
            let k = Constant { kind: .integer, integer: bigFromI64(v) }
            let e = addLiteral(ref c, from: BLiteral { span: TextSpan { start: 0, length: 0 }, type: t, value: k })
            setConstant(ref c, for: e, to: k)
            return e
        }

        void tcChecks(ref Ctx c) {
        CHECKS
        }

        void runInit(ref Binder b, through ref Ctx c, for in parser.Expr init, by string op) {
            if op == "call" {
                switch init {
                    case .callExpr(let call): tcDump(ref c, for: bindCall(ref b, through: ref c, for: in call.value, from: init, to: -1))
                    default: print("unexpected initializer")
                }
            }
            if op == "expr" { tcDump(ref c, for: bindExpr(ref b, through: ref c, for: in init)) }
            if op == "cov" || op == "hand" {
                b.covered = List<CoveredCall>()
                if op == "hand" { b.handled = parser.exprSpan(in init) }
                tcDump(ref c, for: bindExpr(ref b, through: ref c, for: in init))
                let cv = b.covered.take() ?? List<CoveredCall>()
                for e in cv { print($"covered {e.span.start}:{e.span.length} {e.name} {e.structLiteral}") }
            }
            if op == "subj" {
                let r = bindSwitchSubject(ref b, through: ref c, for: in init)
                tcDump(ref c, for: r.value)
                print($"owns {r.owns}")
            }
            if op == "handles" { print($"{handles(in init)}") }
        }

        void runCase(ref Ctx c, for string op, by string name) {
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
                        if t15Text(in c, from: file, at: fd.value.name) == "probes" {
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let kind: LocalKind = t15Text(in c, from: file, at: p.name) == "mv" ? .letKeyword : .parameter
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: kind)
                            }
                            switch fd.value.body {
                                case .blockStmt(let blk):
                                    for s in blk.value.statements {
                                        switch s {
                                            case .localDecl(let ld):
                                                if t15Text(in c, from: file, at: ld.value.name) == name { runInit(ref b, through: ref c, for: in ld.value.init, by: op) }
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
            while i + 1 < args.length {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t21-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t21-cases",
                    displayRoot: "tmp/t21-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t21-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {args[i]}.{args[i + 1]}")
                tcChecks(ref c)
                runCase(ref c, for: args[i], by: args[i + 1])
                i += 2
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source)).Replace("CHECKS", Checks());
}
