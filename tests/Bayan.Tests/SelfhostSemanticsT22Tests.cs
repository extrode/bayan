using System.Collections;
using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T22: <c>selfhost/semantics/states.byn</c> and <c>errorcases.byn</c> against the C# <c>BinderStates</c> and
/// <c>BinderErrors</c>. One source, <see cref="Source"/>, is compiled by both sides: the C# side via
/// <c>Compilation.Create</c> plus reflection on the C# <c>Binder</c>, the Bayan side via <c>loadModule</c> + <c>declare</c>
/// in the generated main. Each case anchors on one <c>let</c> of that source; every expected line is computed from the C#.
/// State types are built over the plain union <c>OrderStates</c> on both sides, since the C# <c>StatesOf</c> has no
/// Bayan port yet (T38).
/// </summary>
public sealed class SelfhostSemanticsT22Tests
{
    private const string Dir = "tmp/t22-cases";

    private const string Source = """
        union error NetError {
            timeout(i32 after)
            closed
            refused(i32 code, string why)
        }

        union error OtherError {
            gone
        }

        union error Both = NetError, OtherError

        union error NoClosed = NetError, OtherError except .closed

        union OrderStates {
            open
            paid(i32 amount)
            shipped(i32 amount, string tracking)
            mixed(string tracking, i32 amount)
        }

        struct Order {
            i64 id
            states {
                open
                paid(i32 amount)
            }
            void pay(ref this) becomes .paid {
            }
            void go(ref this) {
                let c1 = pay()
            }
            void same(ref this) becomes .paid {
                let c2 = pay()
            }
            static void st() {
                let c3 = pay()
            }
        }

        void probes(NetError ne, Both bo, NoClosed nc, OtherError ot, Order o, i32 x) {
            let e1: NetError = .timeout(5)
            let e2 = NetError.refused(1, "x")
            let e3: NetError? = .timeout(1)
            let e4: NetError = .closed(1)
            let e5: NetError = .nope(1)
            let e6: i32 = .timeout(1)
            let e7 = x.timeout(1)
            let e8: Both = .timeout(2)
            let e9 = Order.open(1)
            let e10 = NetError.timeout("a", 2)
            let g1 = NetError.timeout
            let g2 = NetError.closed
            let g3 = NetError.refused
            let q1 = ne is .timeout(let a)
            let q2 = ne is .timeout(let a, let b)
            let q3 = ne is .closed(let z)
            let q4 = ne is .refused r
            let q5 = ne is .gone(let a)
            let q6 = bo is .timeout(let a)
            let q7 = ot is NetError.timeout(let a)
            let q8 = ne is NetError.refused(let c, let w)
            let q9 = ne is NetError.refusd(let c, let w)
            let q10 = ne is NetError.zzzz(let c)
            let q11 = nc is NetError.closed(let z)
            let q12 = bo is NetError.timeout(let a)
            let q13 = ne is .refused(let c)
            let f1 = o.amount
            let f2 = o.nope
            let f3 = o.amount
            let f4 = o.tracking
            let f5 = o.tracking
            let w1 = o
            let w2 = o
            let w3 = x
            let w4 = o
            let s1 = o
            let s2 = o
            let s3 = x
            let k1 = o
            let k2 = o
            let k3 = o
            let k4 = x
        }
        """;

    /// <summary>op, decl, member, let name, and two type specs: <c>Order</c>, <c>i32</c>, or <c>,state,...,</c> over OrderStates.</summary>
    public static readonly (string Op, string Decl, string Member, string Name, string A, string B)[] Cases =
    [
        ("new", "probes", "-", "e1", "-", "-"), ("new", "probes", "-", "e2", "-", "-"), ("new", "probes", "-", "e3", "-", "-"),
        ("new", "probes", "-", "e4", "-", "-"), ("new", "probes", "-", "e5", "-", "-"), ("new", "probes", "-", "e6", "-", "-"),
        ("new", "probes", "-", "e7", "-", "-"), ("new", "probes", "-", "e8", "-", "-"), ("new", "probes", "-", "e9", "-", "-"),
        ("new", "probes", "-", "e10", "-", "-"),
        ("given", "probes", "-", "g1", "-", "-"), ("given", "probes", "-", "g2", "-", "-"), ("given", "probes", "-", "g3", "-", "-"),
        ("pat", "probes", "-", "q1", "-", "-"), ("pat", "probes", "-", "q2", "-", "-"), ("pat", "probes", "-", "q3", "-", "-"),
        ("pat", "probes", "-", "q4", "-", "-"), ("pat", "probes", "-", "q5", "-", "-"), ("pat", "probes", "-", "q6", "-", "-"),
        ("pat", "probes", "-", "q7", "-", "-"), ("pat", "probes", "-", "q8", "-", "-"), ("pat", "probes", "-", "q9", "-", "-"),
        ("pat", "probes", "-", "q10", "-", "-"), ("pat", "probes", "-", "q11", "-", "-"), ("pat", "probes", "-", "q12", "-", "-"),
        ("pat", "probes", "-", "q13", "-", "-"),
        ("field", "probes", "-", "f1", "Order", "-"), ("field", "probes", "-", "f2", "Order", "-"),
        ("field", "probes", "-", "f3", ",paid,shipped,", "-"), ("field", "probes", "-", "f4", ",shipped,mixed,", "-"),
        ("field", "probes", "-", "f5", ",paid,shipped,", "-"),
        ("wrong", "probes", "-", "w1", "Order", ",paid,"), ("wrong", "probes", "-", "w2", ",paid,", ",paid,shipped,"),
        ("wrong", "probes", "-", "w3", "i32", ",paid,"), ("wrong", "probes", "-", "w4", ",shipped,", ",paid,"),
        ("struct", "probes", "-", "s1", "Order", "-"), ("struct", "probes", "-", "s2", ",paid,shipped,", "-"),
        ("struct", "probes", "-", "s3", "i32", "union"),
        ("facts", "probes", "-", "k1", ",paid,", ",shipped,"), ("facts", "probes", "-", "k2", ",paid,", ",shipped,"),
        ("facts", "probes", "-", "k3", ",paid,", ",shipped,"), ("facts", "probes", "-", "k4", "-", "-"),
        ("becomes", "Order", "go", "c1", "-", "-"), ("becomes", "Order", "same", "c2", "-", "-"), ("becomes", "Order", "st", "c3", "-", "-"),
    ];

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static object? Call(object? binder, string name, params object?[] args)
    {
        var methods = BinderType.GetMethods(Any).Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
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

    private static object Field(object b, string name) => BinderType.GetField(name, Any)!.GetValue(b)!;

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static string Lower(bool v) => v ? "true" : "false";

    private static string KindName(BoundNode n) => char.ToLowerInvariant(n.GetType().Name[5]) + n.GetType().Name[6..];

    private static string DumpExpr(BoundExpr? e)
    {
        if (e is null) return "null";
        var sp = $"{e.Syntax.Span.Start}:{e.Syntax.Span.Length}";
        return e switch
        {
            BoundErrorVariant v => $"errorVariant|{v.Variant.Name}|{v.Payload.Length}|{sp}",
            BoundUnionField u => $"unionField|{u.Variant.Name}|{u.Index}|{sp}",
            _ => $"{KindName(e)}|{sp}",
        };
    }

    private static void DumpPattern(object b, BoundPattern p, List<string> lines)
    {
        switch (p)
        {
            case BoundErrorPattern ep: lines.Add($"errorPattern|{ep.Variant.Name}|{ep.Subpatterns.Length}"); break;
            case BoundBindPattern bp when bp.Local is null: lines.Add("bindPattern|-"); break;
            case BoundBindPattern bp:
                var narrowed = (IDictionary)Field(b, "_errorNarrowed");
                lines.Add($"bindPattern|{bp.Local!.Name}|{(narrowed.Contains(bp.Local) ? ((ErrorVariantSymbol)narrowed[bp.Local]!).Name : "-")}");
                break;
            case BoundBinaryPattern bin:
                lines.Add($"binaryPattern|{Lower(bin.IsAnd)}");
                DumpPattern(b, bin.Left, lines);
                DumpPattern(b, bin.Right, lines);
                break;
            default: lines.Add(KindName(p)); break;
        }
    }

    /// <summary>The C# lines for one case: what the port's function returned, then the diagnostics it reported.</summary>
    private static List<string> Expected(string op, string decl, string member, string name, string specA, string specB)
    {
        var path = Path.Combine(Dir, "main.byn");
        Directory.CreateDirectory(Dir);
        var c = Compilation.Create([SourceText.FromString(path, Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
        var mark = c.Diagnostics.Items.Count;
        var unit = file.Tree.Root;
        var order = file.Module.Structs.First(s => s.Name == "Order");
        var states = (UnionSymbol)file.Module.Types["OrderStates"];
        var orderDecl = unit.Members.OfType<StructDecl>().First(s => s.Name.Text == "Order");
        var payDecl = orderDecl.Members.OfType<FunctionDecl>().First(f => f.Name.Text == "pay");
        var pay = order.Methods.First(m => m.Name == "pay");
        var locals = new Dictionary<string, LocalSymbol>();
        FunctionDecl fd;
        object b;
        if (member == "-")
        {
            fd = unit.Members.OfType<FunctionDecl>().First(f => f.Name.Text == decl);
            b = Activator.CreateInstance(BinderType, Any, null, [c, file, null, null, null, null], null)!;
            foreach (var p in fd.Params)
                locals[p.Name.Text] = (LocalSymbol)Call(b, "DeclareLocal", p.Name, Call(b, "ResolveType", p.Type), LocalKind.Parameter)!;
        }
        else
        {
            fd = orderDecl.Members.OfType<FunctionDecl>().First(f => f.Name.Text == member);
            b = Activator.CreateInstance(BinderType, Any, null, [c, file, order.Methods.First(m => m.Name == member), order, null, null], null)!;
        }
        TypeSymbol Spec(string s) => s switch
        {
            "Order" => order,
            "i32" => locals["x"].Type,
            _ => new StateType(order, states.Variants.Where(v => s.Contains($",{v.Name},")).ToList()),
        };
        string Known(LocalSymbol l) => Call(b, "KnownState", l) is TypeSymbol t ? t.DisplayName : "null";
        int Facts() => ((ICollection)Field(b, "_stateFacts")).Count;
        var ld = fd.Body!.Statements.OfType<LocalDecl>().First(l => l.Name.Text == name);
        var init = ld.Init!;
        var lines = new List<string>();
        switch (op)
        {
            case "new":
                var expected = ld.Type is null ? null : Call(b, "ResolveType", ld.Type);
                lines.Add(DumpExpr((BoundExpr?)Call(b, "BindErrorNew", init, expected)));
                break;
            case "given":
                lines.Add(Lower((bool)Call(b, "CheckPayloadGiven", Call(b, "BindMember", init), init)!));
                break;
            case "pat":
                var ie = (IsExpr)init;
                var input = locals[((NameExpr)ie.Value).Name.Text].Type;
                BoundPattern pattern;
                if (ie.Pattern is VariantPattern vp)
                    pattern = (BoundPattern)Call(b, "BindErrorCasePattern", vp, input, Call(b, "BindTargetMember", vp.Variant, Call(b, "Unwrap", input)), vp.Elements, vp.Binding)!;
                else
                {
                    var pos = (PositionalPattern)ie.Pattern;
                    var nt = (NamedType)pos.Type!;
                    var set = (ErrorSetSymbol)file.Module.Types[nt.Path[0].Text];
                    pattern = (BoundPattern)Call(b, "BindErrorCasePattern", pos, input, Call(b, "ErrorCase", pos, set, nt.Path[1]), pos.Elements, null)!;
                }
                DumpPattern(b, pattern, lines);
                break;
            case "field":
                var me = (MemberExpr)init;
                var target = new BoundLocal(me.Target, new LocalSymbol("o", null, Spec(specA), LocalKind.Let));
                lines.Add(DumpExpr((BoundExpr?)Call(b, "BindPayloadField", init, target, states, me.Member)));
                break;
            case "wrong":
                var value = new BoundLocal(init, new LocalSymbol("v", null, Spec(specA), LocalKind.Let));
                lines.Add(Lower((bool)Call(b, "ReportWrongState", "pay", value, (StateType)Spec(specB), init.Span)!));
                break;
            case "struct":
                var st = Spec(specA);
                lines.Add(Call(b, "StateStruct", st) is StructSymbol ss ? ss.Name : "null");
                if (specB == "union") lines.Add(Call(b, "StatesUnion", st) is UnionSymbol us ? us.Name : "null");
                else lines.Add(string.Join(",", ((IEnumerable<UnionVariantSymbol>)Call(b, "PossibleStates", st, states)!).Select(v => v.Name)));
                break;
            case "facts":
                var a = new LocalSymbol("a", null, order, LocalKind.Let);
                var mv = new LocalSymbol("m", null, order, LocalKind.Var);
                switch (name)
                {
                    case "k1":
                        lines.Add(Known(a));
                        Call(b, "LearnState", a, Spec(specA));
                        lines.Add(Known(a));
                        Call(b, "PushScope");
                        Call(b, "LearnState", a, Spec(specB));
                        lines.Add(Known(a));
                        Call(b, "PopScope");
                        lines.Add(Known(a));
                        Call(b, "ForgetState", a);
                        lines.Add(Known(a));
                        break;
                    case "k2":
                        Call(b, "LearnState", a, Spec(specA));
                        Call(b, "LearnState", mv, Spec(specA));
                        Call(b, "ForgetChangingStates");
                        lines.Add(Known(a));
                        lines.Add(Known(mv));
                        break;
                    case "k3":
                        Call(b, "PushScope");
                        Call(b, "LearnState", a, Spec(specA));
                        Call(b, "PopScope");
                        Call(b, "LearnState", mv, Spec(specB));
                        lines.Add($"{Facts()}");
                        var learned = Call(b, "LearnStates", new BoundLocal(init, locals["x"]), true)!;
                        lines.Add($"{((ICollection)learned).Count}");
                        Call(b, "UnlearnStates", learned);
                        lines.Add($"{Facts()}");
                        lines.Add(Known(mv));
                        break;
                    case "k4":
                        var xb = new BoundLocal(init, locals["x"]);
                        var bind = new BoundBindPattern(init, locals["x"].Type, null);
                        Call(b, "LearnArmState", xb, bind);
                        Call(b, "LearnBecomes", xb);
                        lines.Add($"{Facts()}");
                        lines.Add(Call(b, "Narrow", locals["x"].Type, bind, true) is TypeSymbol nt2 ? nt2.DisplayName : "null");
                        lines.Add(Call(b, "ThisStates") is TypeSymbol ts ? ts.DisplayName : "null");
                        var sub = Call(b, "Subject", xb);
                        lines.Add(sub is null ? "null" : $"{(((LocalSymbol Key, TypeSymbol Type))sub).Key.Name}|{(((LocalSymbol Key, TypeSymbol Type))sub).Type.DisplayName}");
                        lines.Add(Call(b, "Subject", new BoundErrorExpr(init)) is null ? "null" : "subject");
                        break;
                }
                break;
            case "becomes":
                var caller = order.Methods.First(m => m.Name == member);
                lines.Add(Call(b, "ReceiverStates", caller) is TypeSymbol rs ? rs.DisplayName : "null");
                Call(b, "CheckBecomesCall", init, caller, pay, payDecl.Becomes);
                break;
        }
        var items = c.Diagnostics.Items;
        for (var i = mark; i < items.Count; i++) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases.SelectMany(k => new[] { k.Op, k.Decl, k.Member, k.Name, k.A, k.B })]);
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
        var k = Cases.First(k => $"{k.Op}.{k.Decl}.{k.Member}.{k.Name}" == section);
        SelfhostSemanticsHarness.AssertSection(Sections.Value, section, Expected(k.Op, k.Decl, k.Member, k.Name, k.A, k.B));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        const world: string = "WORLD\n"

        void t22mExpr(ref Ctx c, for BExpr e) {
            if e == -1 {
                print("null")
                return
            }
            let k = boundKind(e)
            let i = boundIndex(e)
            let sp = nodeSpan(in c, for: e)
            if k == .errorVariant {
                print($"errorVariant|{c.errorVariants[c.bErrorVariants[i].variant].name}|{c.bErrorVariants[i].payload.length}|{sp.start}:{sp.length}")
            } else if k == .unionField {
                print($"unionField|{c.unionVariants[c.bUnionFields[i].variant].name}|{c.bUnionFields[i].index}|{sp.start}:{sp.length}")
            } else {
                print($"{k}|{sp.start}:{sp.length}")
            }
        }

        void t22mPattern(ref Binder b, through ref Ctx c, for BPattern p) {
            let k = boundKind(p)
            let i = boundIndex(p)
            if k == .errorPattern {
                print($"errorPattern|{c.errorVariants[c.bErrorPatterns[i].variant].name}|{c.bErrorPatterns[i].subpatterns.length}")
            } else if k == .bindPattern {
                let l = c.bBindPatterns[i].local
                if l == -1 {
                    print("bindPattern|-")
                } else {
                    let v = b.errorNarrowed.tryGet(at: l) ?? -1
                    var vName = "-"
                    if v != -1 { vName = c.errorVariants[v].name }
                    print($"bindPattern|{c.locals[l].name}|{vName}")
                }
            } else if k == .binaryPattern {
                print($"binaryPattern|{c.bBinaryPatterns[i].isAnd}")
                t22mPattern(ref b, through: ref c, for: c.bBinaryPatterns[i].left)
                t22mPattern(ref b, through: ref c, for: c.bBinaryPatterns[i].right)
            } else {
                print($"{k}")
            }
        }

        void tcDiags(ref Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        Type t22mType(ref Binder b, through ref Ctx c, for string spec) {
            if spec == "Order" { return c.modules[c.entryModule].types["Order"] }
            if spec == "i32" { return c.locals[t15Lookup(ref b, for: "x")].type }
            let order = typeIndex(c.modules[c.entryModule].types["Order"])
            let states = typeIndex(c.modules[c.entryModule].types["OrderStates"])
            var vs = List<VariantId>()
            for v in copyIds(in c.unions[states].variants) {
                if spec.contains($",{c.unionVariants[v].name},") { vs.append(v) }
            }
            return internState(ref c, by: order, for: in vs)
        }

        void t22mKnown(ref Binder b, through ref Ctx c, for LocalId l) {
            let t = knownState(ref b, for: l)
            if t == -1 { print("null") } else { print(typeDisplay(ref c, by: t)) }
        }

        /// One case's arguments: the generated main's helpers take them as one value, since particles are a closed set.
        struct T22mCase {
            string op
            string name
            string specA
            string specB
            FuncId pay
            TextSpan payBecomes
        }

        void runInit(ref Binder b, through ref Ctx c, for in parser.LocalDecl ld, by in T22mCase k) {
            let op = k.op
            let name = k.name
            let specA = k.specA
            let specB = k.specB
            let pay = k.pay
            let payBecomes = k.payBecomes
            let states = typeIndex(c.modules[c.entryModule].types["OrderStates"])
            let order = c.modules[c.entryModule].types["Order"]
            let span = parser.exprSpan(in ld.init)
            if op == "new" {
                var expected: Type = -1
                switch ld.type {
                    case .none: {}
                    default: expected = resolveType(ref b, through: ref c, for: in ld.type)
                }
                switch ld.init {
                    case .callExpr(let call): t22mExpr(ref c, for: bindErrorNew(ref b, through: ref c, for: in call.value, to: expected))
                    default: print("unexpected")
                }
            }
            if op == "given" {
                switch ld.init {
                    case .memberExpr(let m):
                        let v = bindMember(ref b, through: ref c, for: in m.value)
                        print($"{checkPayloadGiven(ref b, through: ref c, for: v, from: in ld.init)}")
                    default: print("unexpected")
                }
            }
            if op == "pat" {
                switch ld.init {
                    case .isExpr(let ie):
                        var input: Type = -1
                        switch ie.value.value {
                            case .nameExpr(let n): input = c.locals[t15Lookup(ref b, for: t15Text(in c, from: b.file, at: n.value.name))].type
                            default: {}
                        }
                        switch ie.value.pattern {
                            case .variantPattern(let vp):
                                switch vp.value.variant {
                                    case .targetMemberExpr(let t):
                                        let bound = bindTargetMember(ref b, through: ref c, for: in t.value, to: unwrap(ref c, for: input))
                                        t22mPattern(ref b, through: ref c, for: bindErrorCasePattern(ref b, through: ref c, for: in ie.value.pattern, as: input, by: bound, from: in vp.value.elements, at: vp.value.binding))
                                    default: print("unexpected")
                                }
                            case .positionalPattern(let pos):
                                switch pos.value.type {
                                    case .namedType(let nt):
                                        let set = typeIndex(c.modules[c.entryModule].types[t15Text(in c, from: b.file, at: nt.value.path[0])])
                                        let ec = errorCase(ref b, through: ref c, for: in ie.value.pattern, from: set, at: nt.value.path[1])
                                        t22mPattern(ref b, through: ref c, for: bindErrorCasePattern(ref b, through: ref c, for: in ie.value.pattern, as: input, by: ec, from: in pos.value.elements, at: null))
                                    default: print("unexpected")
                                }
                            default: print("unexpected")
                        }
                    default: print("unexpected")
                }
            }
            if op == "field" {
                switch ld.init {
                    case .memberExpr(let m):
                        let local = newLocal(ref c, by: LocalSymbol { name: "o", type: t22mType(ref b, through: ref c, for: specA) })
                        let target = addLocal(ref c, from: BLocal { span: parser.exprSpan(in m.value.target), local: local })
                        t22mExpr(ref c, for: bindPayloadField(ref b, through: ref c, at: m.value.span, for: target, from: states, by: m.value.member))
                    default: print("unexpected")
                }
            }
            if op == "wrong" {
                let local = newLocal(ref c, by: LocalSymbol { name: "v", type: t22mType(ref b, through: ref c, for: specA) })
                let value = addLocal(ref c, from: BLocal { span: span, local: local })
                print($"{reportWrongState(ref b, through: ref c, for: "pay", by: value, as: t22mType(ref b, through: ref c, for: specB), at: span)}")
            }
            if op == "struct" {
                let t = t22mType(ref b, through: ref c, for: specA)
                let s = stateStruct(ref c, for: t)
                if s == -1 { print("null") } else { print(c.structs[s].name) }
                if specB == "union" {
                    let u = statesUnion(ref c, for: t)
                    if u == -1 { print("null") } else { print(c.unions[u].name) }
                } else {
                    var joined = StrBuf()
                    var first = true
                    for v in possibleStates(ref c, for: t, by: states) {
                        if !first { joined.append(",") }
                        joined.append(c.unionVariants[v].name)
                        first = false
                    }
                    print(joined.finish())
                }
            }
            if op == "facts" {
                let a = newLocal(ref c, by: LocalSymbol { name: "a", type: order, kind: .letKeyword })
                let mv = newLocal(ref c, by: LocalSymbol { name: "m", type: order, kind: .varKeyword })
                if name == "k1" {
                    t22mKnown(ref b, through: ref c, for: a)
                    learnState(ref b, for: a, as: t22mType(ref b, through: ref c, for: specA))
                    t22mKnown(ref b, through: ref c, for: a)
                    pushScope(ref b)
                    learnState(ref b, for: a, as: t22mType(ref b, through: ref c, for: specB))
                    t22mKnown(ref b, through: ref c, for: a)
                    popScope(ref b)
                    t22mKnown(ref b, through: ref c, for: a)
                    forgetState(ref b, for: a)
                    t22mKnown(ref b, through: ref c, for: a)
                }
                if name == "k2" {
                    learnState(ref b, for: a, as: t22mType(ref b, through: ref c, for: specA))
                    learnState(ref b, for: mv, as: t22mType(ref b, through: ref c, for: specA))
                    forgetChangingStates(ref b, through: ref c)
                    t22mKnown(ref b, through: ref c, for: a)
                    t22mKnown(ref b, through: ref c, for: mv)
                }
                if name == "k3" {
                    pushScope(ref b)
                    learnState(ref b, for: a, as: t22mType(ref b, through: ref c, for: specA))
                    popScope(ref b)
                    learnState(ref b, for: mv, as: t22mType(ref b, through: ref c, for: specB))
                    print($"{b.stateFacts.length}")
                    let xb = addLocal(ref c, from: BLocal { span: span, local: t15Lookup(ref b, for: "x") })
                    let learned = learnStates(ref b, through: ref c, for: xb, whenTrue: true)
                    print($"{learned.length}")
                    unlearnStates(ref b, by: in learned)
                    print($"{b.stateFacts.length}")
                    t22mKnown(ref b, through: ref c, for: mv)
                }
                if name == "k4" {
                    let x = t15Lookup(ref b, for: "x")
                    let xb = addLocal(ref c, from: BLocal { span: span, local: x })
                    let bind = addBindPattern(ref c, from: BBindPattern { span: span, inputType: c.locals[x].type })
                    learnArmState(ref b, through: ref c, for: xb, by: bind)
                    learnBecomes(ref b, through: ref c, for: xb)
                    print($"{b.stateFacts.length}")
                    let n = narrow(ref c, for: c.locals[x].type, by: bind, matched: true)
                    if n == -1 { print("null") } else { print(typeDisplay(ref c, by: n)) }
                    let ts = t22ThisStates(ref b, through: ref c)
                    if ts == -1 { print("null") } else { print(typeDisplay(ref c, by: ts)) }
                    if (subject(ref b, through: ref c, for: xb) is { } s) {
                        print($"{c.locals[s.key].name}|{typeDisplay(ref c, by: s.type)}")
                    } else {
                        print("null")
                    }
                    let err = addErrorExpr(ref c, from: BErrorExpr { span: span })
                    if subject(ref b, through: ref c, for: err) is null { print("null") } else { print("subject") }
                }
            }
            if op == "becomes" {
                let rs = receiverStates(ref b, through: ref c, for: b.fn)
                if rs == -1 { print("null") } else { print(typeDisplay(ref c, by: rs)) }
                switch ld.init {
                    case .callExpr(let call): checkBecomesCall(ref b, through: ref c, for: in call.value, from: b.fn, by: pay, to: payBecomes)
                    default: print("unexpected")
                }
            }
        }

        void runBody(ref Binder b, through ref Ctx c, for in parser.Stmt body, by in T22mCase k) {
            switch body {
                case .blockStmt(let blk):
                    for s in blk.value.statements {
                        switch s {
                            case .localDecl(let ld):
                                if t15Text(in c, from: b.file, at: ld.value.name) == k.name { runInit(ref b, through: ref c, for: in ld.value, by: in k) }
                            default: {}
                        }
                    }
                default: {}
            }
        }

        void runCase(ref Ctx c, for string op, from string decl, at string member, by string name, as in List<string> specs) {
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
            let owner = typeIndex(c.modules[c.entryModule].types["Order"])
            var pay: FuncId = -1
            for f in c.structs[owner].methods {
                if c.functions[f].name == "pay" { pay = f }
            }
            var payBecomes = TextSpan { start: 0, length: 0 }
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .structDecl(let sd):
                        for d in sd.value.members {
                            switch d {
                                case .functionDecl(let fd):
                                    if t15Text(in c, from: file, at: fd.value.name) == "pay" {
                                        if (fd.value.becomes is { } bs) { payBecomes = bs }
                                    }
                                default: {}
                            }
                        }
                    default: {}
                }
            }
            let k = T22mCase { op: op, name: name, specA: specs[0], specB: specs[1], pay: pay, payBecomes: payBecomes }
            let start = c.diagnostics.length
            for i in 0..c.files[file].unit.members.length {
                switch c.files[file].unit.members[i] {
                    case .functionDecl(let fd):
                        if member == "-" && t15Text(in c, from: file, at: fd.value.name) == decl {
                            for p in fd.value.params {
                                let t = resolveType(ref b, through: ref c, for: in p.type)
                                let _ = declareLocal(ref b, through: ref c, at: p.name, as: t, for: .parameter)
                            }
                            runBody(ref b, through: ref c, for: in fd.value.body, by: in k)
                        }
                    case .structDecl(let sd):
                        if member != "-" && t15Text(in c, from: file, at: sd.value.name) == decl {
                            for d in sd.value.members {
                                switch d {
                                    case .functionDecl(let fd):
                                        if t15Text(in c, from: file, at: fd.value.name) == member {
                                            for f in c.structs[owner].methods {
                                                if c.functions[f].name == member { b.fn = f }
                                            }
                                            b.owner = owner
                                            b.thisType = c.modules[c.entryModule].types[decl]
                                            runBody(ref b, through: ref c, for: in fd.value.body, by: in k)
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
            while i + 5 < args.length {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t22-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t22-cases",
                    displayRoot: "tmp/t22-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t22-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {args[i]}.{args[i + 1]}.{args[i + 2]}.{args[i + 3]}")
                var specs = List<string>()
                specs.append(args[i + 4])
                specs.append(args[i + 5])
                runCase(ref c, for: args[i], from: args[i + 1], at: args[i + 2], by: args[i + 3], as: in specs)
                i += 6
            }
            return 0
        }
        """.Replace("WORLD", Escape(Source));
}
