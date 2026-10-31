using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T20: <c>selfhost/semantics/calls.byn</c> against the C# <c>BinderCalls</c> (BinderCalls.cs 1-595).
/// One source, <see cref="Source"/>, is compiled by both sides: the C# side via <c>Compilation.Create</c> plus
/// reflection on the C# <c>Binder</c>, the Bayan side via <c>loadModule</c> + <c>declare</c> in the generated main run
/// by <c>SelfhostSemanticsHarness.RunSections</c>. Each case runs one operation on one <c>let</c> initializer of
/// <c>probes</c> on both sides; every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT20Tests
{
    private const string Dir = "tmp/t20-cases";

    private const string Source = """
        enum Color {
            red
            green
        }

        union Shape {
            circle(f64 radius)
            dot
        }

        union Opt<T> {
            some(T value)
            none
        }

        struct Plain {
            i64 v
        }


        struct Box<T> {
            T item
        }

        void put(at i64 x) {
        }

        void put(by i64 x) {
        }

        void take(i64 a, bool verbose, to i64 b = 3, string label = "x") {
        }

        void bump(ref i64 x) {
        }

        void mix(at i64 a) {
        }

        void mix(i64 a, rest ReadOnlySpan<i64> xs) {
        }

        void fit(at i64 a) {
        }

        void fit(i64 a, i64 b = 2) {
        }

        void spread(i64 a, rest ReadOnlySpan<i64> xs) {
        }

        T pick<T>(T a, T b) => a

        T first<T>(List<T> xs) => xs[0]

        T zero<T>() => zero()

        T orElse<T>(T? x, T y) => y

        K keyOf<K, V>(Map<K, V> m) => keyOf(m)

        T firstOf<T>(ReadOnlySpan<T> xs) => xs[0]

        T via<T>((T) -> T f) => via(f)

        i64 twice(i64 x) => x

        i64 add2(i64 a, i64 b) => a

        T unwrap<T>(Opt<T> o) => unwrap(o)

        T unbox<T>(Box<T> x) => unbox(x)

        T keyed<T>(Map<T, Plain> m) => keyed(m)

        T pairB<T>(T a, Box<T> x) => a

        T pairO<T>(T a, Opt<T> x) => a

        T fst<T>((T, i64) p) => fst(p)

        void guarded(i64 n, i64 k) expects n > k {
        }

        void doomed(i64 n) expects 1 > 2 {
        }

        void probes(i64 n, string s, bool verbose, List<i64> ns, ReadOnlySpan<u8> bytes, Map<string, i64> m, Opt<i64> o, Box<i64> bx, Untrusted<string> un, Map<string, Plain> mp, (i64, i64) tp) {
            let c1 = u8.tryFrom(n)
            let c2 = u8.tryFrom(s)
            let c3 = u8.tryFrom(n, n)
            let c4 = i64.tryParse(s)
            let c5 = i32.tryParse(s, as: .hex)
            let c6 = i64.tryParse(n)
            let c7 = Log.debug("x")
            let c8 = Shape.circle(1.0)
            let c9 = Shape.dot()
            let c10 = nope(1)
            let c11 = n(1)
            let c12 = frobUnchecked()
            let c13 = u8.tryFrom()
            let c14 = Shape.circle(radius: 2.0)
            let c15 = Shape.circle()
            let c16 = Utf8.decode(bytes, invalid: .replace)
            let c17 = take<i32>(1, true)
            let c18 = take(1, true, invalid: 2)
            let c19 = StrBuf.over(bytes)
            let c20 = StrBuf.under(bytes)
            let c21 = nope(x => 1)
            let c22 = twice(n)
            let c23 = Foo.debug("x")
            let c24 = twice(un)
            let c25 = pick<i32, i64>(zz, 2)
            let c26 = i64.frob(s)
            let r1 = guarded(0, 1)
            let r2 = guarded(2, 1)
            let r3 = doomed(n)
            let a1 = take(1, true)
            let a2 = take(1, verbose)
            let a3 = take(1, verbose: true, to: 2, label: "y")
            let a4 = take(verbose: true, 1)
            let a5 = take(1, verbose: true, labl: "z")
            let a6 = take(1, verbose: true, verbose: false)
            let a7 = take()
            let a8 = take(1, false, 3)
            let a9 = bump(ref verbose)
            let a10 = spread(1, 2, 3)
            let a11 = spread(1)
            let a12 = spread(1, ..ns)
            let a13 = take(1, 2, 3, 4, 5)
            let a14 = take(1, in verbose)
            let a15 = take(1, ref verbose)
            let a16 = take(1, verbos: true)
            let i1 = pick(1, 2)
            let i2 = pick<i32>(1, 2)
            let i3 = pick<i32, i64>(1, 2)
            let i4 = pick(1, "x")
            let i5 = first(ns)
            let i6 = zero()
            let i7 = take<i32>(1)
            let i8 = pick(b: n, a: 1)
            let i9 = pick(null, n)
            let i10 = pick(nope, n)
            let i11 = orElse(n, 2)
            let i12 = orElse(null, n)
            let i13 = keyOf(m)
            let i14 = firstOf(ns)
            let i15 = firstOf(bytes)
            let i16 = via(twice)
            let i17 = pick(null, nope)
            let i18 = via(add2)
            let i19 = pick(a: 1, zz: s)
            let i20 = via(x => x)
            let i21 = unwrap(o)
            let i22 = unbox(bx)
            let i23 = unwrap(bx)
            let i24 = keyed(mp)
            let i25 = pairB(s, bx)
            let i26 = pairO(s, o)
            let i27 = fst(tp)
            let o1 = put(at: 1)
            let o2 = put(by: 2)
            let o3 = put(3)
            let o4 = mix(1, 2, 3)
            let o5 = fit(1)
            let m1 = it + 1
            let m2 = n + 1
            let m3 = s.f(n, [it])
            let m4 = (x => it)
            let s1 = ((n))
            let s2 = n
        }
        """;

    private static readonly (string Op, string Name)[] Cases =
    [
        ("call", "c1"), ("call", "c2"), ("call", "c3"), ("call", "c4"), ("call", "c5"), ("call", "c6"), ("call", "c7"),
        ("call", "c8"), ("call", "c9"), ("call", "c10"), ("call", "c11"), ("call", "c12"), ("call", "c13"), ("call", "c14"),
        ("call", "c15"), ("call", "c16"), ("call", "c17"), ("call", "c18"), ("call", "c19"), ("call", "c20"), ("call", "c21"), ("call", "c22"), ("call", "c23"), ("call", "c24"), ("call", "c25"), ("call", "c26"),
        ("req", "r1"), ("req", "r2"), ("req", "r3"),
        ("args", "a1"), ("args", "a2"), ("args", "a3"), ("args", "a4"), ("args", "a5"), ("args", "a6"), ("args", "a7"),
        ("args", "a8"), ("args", "a9"), ("args", "a10"), ("args", "a11"), ("args", "a12"), ("args", "a13"), ("args", "a14"),
        ("args", "a15"), ("args", "a16"),
        ("inst", "i1"), ("inst", "i2"), ("inst", "i3"), ("inst", "i4"), ("inst", "i5"), ("inst", "i6"), ("inst", "i7"),
        ("inst", "i8"), ("inst", "i9"), ("inst", "i10"), ("inst", "i11"), ("inst", "i12"),
        ("inst", "i13"), ("inst", "i14"), ("inst", "i15"), ("inst", "i16"), ("inst", "i17"), ("inst", "i18"), ("inst", "i19"),
        ("inst", "i20"), ("inst", "i21"), ("inst", "i22"), ("inst", "i23"), ("inst", "i24"), ("inst", "i25"), ("inst", "i26"), ("inst", "i27"),
        ("overload", "o1"), ("overload", "o2"), ("overload", "o3"), ("overload", "o4"), ("overload", "o5"),
        ("mention", "m1"), ("mention", "m2"), ("mention", "m3"), ("mention", "m4"),
        ("strip", "s1"), ("strip", "s2"),
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

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>A child expression: kind:type, a literal's value, a conversion's kind and operand.</summary>
    private static string Sub(BoundExpr? e) => e switch
    {
        null => "-",
        BoundLiteral l => $"literal:{l.Type.DisplayName}={l.Value}",
        BoundConversion v => $"conversion:{v.Type.DisplayName}({Camel(v.Kind.ToString())} {Sub(v.Operand)})",
        BoundEnumMember m => $"enumMember:{m.Type.DisplayName}={m.Member.Name}",
        _ => $"{NodeKindName(e)}:{e.Type.DisplayName}",
    };

    /// <summary>kind|type|receiver|callee|variant|span|sub: every field a port bug could change.</summary>
    private static string DumpNode(BoundNode? node)
    {
        if (node is null) return "null";
        var type = node is BoundExpr e ? e.Type.DisplayName : "-";
        var span = $"{node.Syntax.Span.Start}:{node.Syntax.Span.Length}";
        var recv = "-"; var callee = "-"; var variant = "-"; var sub = "-";
        switch (node)
        {
            case BoundCall c: callee = c.Function.Name; recv = Recv(c.Receiver); sub = string.Join(",", c.Arguments.Select(Sub)); break;
            case BoundBuiltinCall b: callee = Camel(b.Member.ToString()); recv = Sub(b.Receiver); sub = string.Join(",", b.Arguments.Select(Sub)); break;
            case BoundTryFrom t: callee = t.Target.DisplayName; sub = Sub(t.Operand); break;
            case BoundLocal l: callee = l.Local.Name; break;
            case BoundEnumMember m: callee = m.Member.Name; break;
            case BoundUnionNew u: variant = u.Variant.Name; sub = string.Join(",", u.Payload.Select(Sub)); break;
            case BoundCollection col: sub = string.Join(",", col.Elements.Select(Sub)); break;
            case BoundConversion v: sub = Sub(v); break;
            case BoundLiteral l: sub = l.Value.ToString()!; break;
        }
        return $"{NodeKindName(node)}|{type}|{recv}|{callee}|{variant}|{span}|{sub}";
    }

    private static FunctionSymbol Fn(Compilation c, CallExpr call) =>
        c.EntryModule.Functions[((NameExpr)call.Callee).Name.Text][0];

    /// <summary>The C# lines for one case: what the operation gives, then the diagnostics it reported.</summary>
    private static List<string> Expected(string op, string name)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "main.byn");
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        var mark = c.Diagnostics.Items.Count;
        var fd = file.Tree.Root.Members.OfType<FunctionDecl>().First(f => f.Name.Text == "probes");
        var b = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, null, null, null, null], null)!;
        foreach (var p in fd.Params) Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), LocalKind.Parameter);
        var ld = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name);
        var init = ld.Init!;
        var lines = new List<string>();
        switch (op)
        {
            case "call":
            case "req":
                // `req` checks only this call's contract: the compilation's own checks are dropped first.
                if (op == "req") ((System.Collections.IList)typeof(Compilation).GetProperty("RequiresChecks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!).Clear();
                var expected = ld.Type is null ? null : Call(b, "ResolveType", ld.Type);
                lines.Add(DumpNode((BoundNode?)Call(b, "BindCall", init, expected)));
                if (op == "req") typeof(Compilation).GetMethod("CheckRequires", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c, null);
                break;
            case "args":
                var fn = Fn(c, (CallExpr)init);
                var args = (BoundExpr[])Call(b, "BindArguments", init, fn.Parameters, fn, fn.Kind is not (FunctionKind.Builtin or FunctionKind.Invoke), null)!;
                foreach (var a in args) lines.Add(DumpNode(a));
                break;
            case "inst":
                var call = (CallExpr)init;
                var instance = (FunctionSymbol?)Call(b, "Instantiate", call, Fn(c, call), ((NameExpr)call.Callee).TypeArgs);
                lines.Add(instance is null ? "null" : $"{instance.Name}<{string.Join(",", instance.TypeArguments.Select(t => t.DisplayName))}>");
                break;
            case "overload":
                var chosen = (FunctionSymbol)Call(null, "Overload", Fn(c, (CallExpr)init), init)!;
                lines.Add($"{chosen.Name}({string.Join(",", chosen.Parameters.Select(p => (string)Call(null, "Label", p)!))})");
                break;
            case "mention":
                var it = (Name?)Call(null, "MentionedIt", init);
                lines.Add(it is null ? "null" : $"it@{it.Span.Start}:{it.Span.Length}");
                break;
            case "strip":
                var inner = (Expr)Call(null, "StripParens", init)!;
                lines.Add($"{inner.Span.Start}:{inner.Span.Length}");
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

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        string t20Recv(ref Ctx c, for BExpr r) {
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
            var variant = "-"
            var sub = "-"
            switch k {
                case .call:
                    callee = c.functions[c.bCalls[i].function].name
                    recv = t20Recv(ref c, for: c.bCalls[i].receiver)
                    let args = copyIds(in c.bCalls[i].arguments)
                    sub = tcSubs(ref c, for: in args)
                case .builtinCall:
                    callee = $"{c.bBuiltinCalls[i].member}"
                    recv = tcSub(ref c, for: c.bBuiltinCalls[i].receiver)
                    let args = copyIds(in c.bBuiltinCalls[i].arguments)
                    sub = tcSubs(ref c, for: in args)
                case .tryFrom:
                    callee = typeDisplay(ref c, by: c.bTryFroms[i].target)
                    sub = tcSub(ref c, for: c.bTryFroms[i].operand)
                case .local:
                    callee = c.locals[c.bLocals[i].local].name
                case .enumMember:
                    callee = c.enumMembers[c.bEnumMembers[i].member].name
                case .unionNew:
                    variant = c.unionVariants[c.bUnionNews[i].variant].name
                    let args = copyIds(in c.bUnionNews[i].payload)
                    sub = tcSubs(ref c, for: in args)
                case .collection:
                    let args = copyIds(in c.bCollections[i].elements)
                    sub = tcSubs(ref c, for: in args)
                case .conversion:
                    sub = tcSub(ref c, for: e)
                case .literal:
                    sub = constantText(in c.bLiterals[i].value)
                default: {}
            }
            let sp = nodeSpan(in c, for: e)
            print($"{k}|{typeName}|{recv}|{callee}|{variant}|{sp.start}:{sp.length}|{sub}")
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        FuncId testFn(ref Ctx c, for in parser.Expr callee, from FileId file) {
            switch callee {
                case .nameExpr(let n): return c.modules[c.entryModule].functions[t15Text(in c, from: file, at: n.value.name)][0]
                default: return -1
            }
        }

        void runCall(ref Binder b, through ref Ctx c, for in parser.CallExpr call, at parser.Expr syntax, from in parser.TypeSyntax declType, by string op) {
            if op == "call" || op == "req" {
                var expected: Type = -1
                switch declType {
                    case .none: {}
                    default: expected = resolveType(ref b, through: ref c, for: in declType)
                }
                tcDump(ref c, for: bindCall(ref b, through: ref c, for: in call, from: syntax, to: expected))
                if op == "req" { checkRequires(ref c) }
                return
            }
            let fn = testFn(ref c, for: in call.callee, from: b.file)
            if op == "args" {
                let ps = copyIds(in c.functions[fn].parameters)
                let k = c.functions[fn].kind
                let args = bindArguments(ref b, through: ref c, for: in call, by: in ps, as: fn, enforceNames: k != .builtin && k != .invoke)
                for a in copyIds(in args) { tcDump(ref c, for: a) }
            }
            if op == "inst" {
                switch call.callee {
                    case .nameExpr(let n):
                        let r = t20Instantiate(ref b, through: ref c, for: in call, by: fn, from: in n.value.typeArgs)
                        if r == -1 {
                            print("null")
                        } else {
                            var names = List<string>()
                            for t in copyIds(in c.functions[r].typeArgs) { names.append(typeDisplay(ref c, by: t)) }
                            print($"{c.functions[r].name}<{names.joined(by: ",")}>")
                        }
                    default: {}
                }
            }
            if op == "overload" {
                let o = t20Overload(ref c, for: fn, from: b.file, by: in call)
                var labels = List<string>()
                for p in copyIds(in c.functions[o].parameters) { labels.append(label(ref c, for: p)) }
                print($"{c.functions[o].name}({labels.joined(by: ",")})")
            }
        }

        void runInit(ref Binder b, through ref Ctx c, for in parser.Expr init, from in parser.TypeSyntax declType, by string op) {
            if op == "mention" {
                let m = t20MentionedIt(in c, from: b.file, for: in init)
                if (m is { } s) { print($"it@{s.start}:{s.length}") } else { print("null") }
                return
            }
            if op == "strip" {
                let s = stripParens(in init)
                print($"{s.start}:{s.length}")
                return
            }
            switch init {
                case .callExpr(let call): runCall(ref b, through: ref c, for: in call.value, at: init, from: in declType, by: op)
                default: print("unexpected initializer")
            }
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
            // A callee's `requirements` are bound with its body; the C# compilation has bound them before the case runs.
            if op == "req" {
                tcBindBody(ref c, for: file, by: "guarded")
                tcBindBody(ref c, for: file, by: "doomed")
            }
            let start = c.diagnostics.length
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if t15Text(in c, from: file, at: fd.value.name) == "probes" {
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: .parameter)
                            }
                            switch fd.value.body {
                                case .blockStmt(let blk):
                                    for s in blk.value.statements {
                                        switch s {
                                            case .localDecl(let ld):
                                                if t15Text(in c, from: file, at: ld.value.name) == name { runInit(ref b, through: ref c, for: in ld.value.init, from: in ld.value.type, by: op) }
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

        void tcBindBody(ref Ctx c, for FileId file, by string name) {
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if t15Text(in c, from: file, at: fd.value.name) == name {
                            var fb = t13NewBinder(file, fn: tcNth(in c, by: name, at: 0))
                            bindFunctionBody(ref fb, through: ref c, for: in fd.value)
                        }
                    default: {}
                }
            }
        }

        void tcParam(ref Ctx c,for FuncId f, by string name, at TextSpan span, as Type type, to parser.ParamModifier modifier) {
            let ordinal = c.functions[f].parameters.length
            let p = newLocal(ref c, by: LocalSymbol { name: name, nameSpan: span, span: span, type: type, kind: .parameter, isParameter: true, modifier: modifier, ordinal: ordinal })
            c.functions[f].parameters.append(p)
        }

        /// The `k`th function named `name` of the entry module, in declaration order.
        FuncId tcNth(in Ctx c, by string name, at i64 k) {
            var seen: i64 = 0
            for f in 0..c.functions.length {
                if c.functions[f].name == name && c.functions[f].module == c.entryModule {
                    if seen == k { return f }
                    seen += 1
                }
            }
            return -1
        }

        void t20Signatures(ref Ctx c) {
        SIGNATURES
        }

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 1 < args.length {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t20-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t20-cases",
                    displayRoot: "tmp/t20-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t20-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                t20Signatures(ref c)
                print($"== {args[i]}.{args[i + 1]}")
                runCase(ref c, for: args[i], by: args[i + 1])
                i += 2
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source)).Replace("SIGNATURES", Signatures());

    private static string Lower(bool b) => b ? "true" : "false";

    /// <summary>The Bayan expression for type <paramref name="t"/>; statements it needs first go to <paramref name="sb"/>.</summary>
    private static string BayanType(System.Text.StringBuilder sb, TypeSymbol t, Dictionary<string, string> tps, ref int n)
    {
        switch (t)
        {
            case TypeParameterType p: return $"typeParamTypeOf({tps[p.Name]})";
            case PrimitiveType p: return $"prim(.{p.DisplayName})";
            case ListType l: return $"internList(ref c, by: {BayanType(sb, l.Element, tps, ref n)})";
            case SpanType sp: return $"internSpan(ref c, by: {BayanType(sb, sp.Element, tps, ref n)}, readOnly: {Lower(sp.ReadOnly)})";
            case Bayan.Semantics.Symbols.OptionalType o: return $"internOptional(ref c, by: {BayanType(sb, o.Inner, tps, ref n)})";
            case MapType m: return $"internMap(ref c, by: {BayanType(sb, m.KeyType, tps, ref n)}, to: {BayanType(sb, m.ValueType, tps, ref n)})";
            case FunctionType f when f.Fails.Count == 0:
            {
                var i = n++;
                var ps = new List<string>();
                foreach (var p in f.Parameters) ps.Add(BayanType(sb, p, tps, ref n));
                var ret = BayanType(sb, f.Return, tps, ref n);
                sb.AppendLine($"    var fp{i} = List<Type>()");
                foreach (var p in ps) sb.AppendLine($"    fp{i}.append({p})");
                sb.AppendLine($"    let fe{i} = List<ErrorSetId>()");
                sb.AppendLine($"    let fu{i} = List<string>()");
                return $"internFunction(ref c, for: in fp{i}, to: {ret}, by: in fe{i}, as: in fu{i})";
            }
            case Bayan.Semantics.Symbols.TupleType tt:
            {
                var i = n++;
                var fs = new List<string>();
                foreach (var f in tt.Fields) fs.Add(BayanType(sb, f.Type, tps, ref n));
                sb.AppendLine($"    var tf{i} = List<TupleField>()");
                for (var k = 0; k < fs.Count; k++)
                    sb.AppendLine(tt.Fields[k].Name is { } fn ? $"    tf{i}.append(TupleField {{ name: \"{fn}\", type: {fs[k]} }})" : $"    tf{i}.append(TupleField {{ type: {fs[k]} }})");
                return $"internTuple(ref c, for: in tf{i})";
            }
            case UnionSymbol { Template: { } u } us: return Instance(sb, u.Name, us.TypeArguments, tps, ref n);
            case StructSymbol { Template: { } s } ss: return Instance(sb, s.Name, ss.TypeArguments, tps, ref n);
            case StructSymbol s: return $"c.modules[c.entryModule].types[\"{s.Name}\"]";
            default: throw new InvalidOperationException(t.DisplayName);
        }
    }

    /// <summary>The entry module's generic struct or union <paramref name="name"/> at <paramref name="args"/>.</summary>
    private static string Instance(System.Text.StringBuilder sb, string name, IReadOnlyList<TypeSymbol> args, Dictionary<string, string> tps, ref int n)
    {
        var i = n++;
        var ts = new List<string>();
        foreach (var a in args) ts.Add(BayanType(sb, a, tps, ref n));
        sb.AppendLine($"    var ta{i} = List<Type>()");
        foreach (var a in ts) sb.AppendLine($"    ta{i}.append({a})");
        return $"instantiateType(ref c, for: c.modules[c.entryModule].types[\"{name}\"], by: in ta{i}, from: c.modules[c.entryModule].files[0], at: TextSpan {{ start: 0, length: 0 }})";
    }

    /// <summary>
    /// What T11's stubbed <c>declareSignature</c>/<c>declareParameters</c> (declare2.byn:15, :18) would set on each free
    /// function but <c>probes</c>, generated from the C# symbols as SelfhostSemanticsT17Tests.Signature does (Q618).
    /// </summary>
    private static string Signatures()
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "main.byn");
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var (name, fns) in c.EntryModule.Functions)
        {
            if (name == "probes") continue;
            for (var i = 0; i < fns.Count; i++)
            {
                var fn = fns[i];
                var v = $"f{n++}";
                sb.AppendLine($"    let {v} = tcNth(in c, by: \"{name}\", at: {i})");
                var tps = new Dictionary<string, string>();
                for (var k = 0; k < fn.TypeParameters.Count; k++)
                {
                    // declare already gives a generic its type parameters; only their uses in the signature are filled.
                    var tv = $"tp{n++}";
                    tps[fn.TypeParameters[k].Name] = tv;
                    sb.AppendLine($"    let {tv} = c.functions[{v}].typeParams[{k}]");
                }
                var ret = BayanType(sb, fn.ReturnType, tps, ref n);
                sb.AppendLine($"    c.functions[{v}].returnType = {ret}");
                sb.AppendLine($"    c.functions[{v}].hasFails = {Lower(fn.Fails)}");
                foreach (var p in fn.Parameters)
                {
                    var span = p.Location!.Value.Span;
                    var modifier = p.Modifier switch
                    {
                        ParamModifier.None => "none", ParamModifier.Ref => "refKeyword", ParamModifier.Move => "moveKeyword",
                        ParamModifier.Out => "outKeyword", _ => "inKeyword",
                    };
                    var type = BayanType(sb, p.Type, tps, ref n);
                    sb.AppendLine($"    tcParam(ref c, for: {v}, by: \"{p.Name}\", at: TextSpan {{ start: {span.Start}, length: {span.Length} }}, as: {type}, to: .{modifier})");
                }
            }
            if (fns.Count < 2) continue;
            // T12's checkOverloads ran on the empty signatures and dropped the later overloads as duplicates; with the
            // signatures above it keeps them, as the C# does.
            sb.AppendLine($"    var ov{n} = List<FuncId>()");
            for (var i = 0; i < fns.Count; i++) sb.AppendLine($"    ov{n}.append(tcNth(in c, by: \"{name}\", at: {i}))");
            sb.AppendLine($"    c.modules[c.entryModule].functions[\"{name}\"] = move ov{n++}");
        }
        return sb.ToString();
    }
}
