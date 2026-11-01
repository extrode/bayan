using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T23: <c>selfhost/semantics/flow.byn</c> and <c>effects.byn</c> against the C# <c>FlowAnalysis</c>, <c>EffectAnalysis</c>
/// and <c>ErrorFlow</c>. The statement and expression binders these walk are still stubs (T34, T36, T38, T39), so the
/// C# side compiles <see cref="Source"/>, then writes each case function's bound body and symbols out as Bayan code that
/// rebuilds them in the Bayan arenas; the generated main runs the port over that tree. Every expected line is the C#
/// analysis rerun on the same function.
/// </summary>
public sealed class SelfhostSemanticsT23Tests
{
    private const string Dir = "tmp/t23-cases";

    private const string Source = """
        union error NetError {
            timeout
            closed
        }

        union error OtherError {
            gone
        }

        union error NoClosed = NetError except .closed

        struct Order {
            i64 id
            states {
                open
                paid(i32 amount)
            }
            void pay(ref this) becomes .paid {
            }
            void payDone(ref this) becomes .paid {
                become .paid(1)
            }
            void viaThis(ref this) becomes .paid {
                pay()
            }
            void early(ref this, bool b) becomes .paid {
                if b { return }
                become .paid(2)
            }
            void unknownState(ref this) becomes .paid {
                become .nope(1)
            }
        }

        i32 failing() fails NetError {
            fail .timeout
        }

        i32 other() fails OtherError {
            fail .gone
        }

        void fill(out i32 r) {
            r = 1
        }

        never stop() {
            while true {
            }
        }

        i32 noReturn(i32 x) {
            if x > 0 {
                return 1
            }
        }

        never neverReturns(bool b) {
            if b {
                stop()
            }
        }

        void unreachable() {
            return
            let a = 1
            let b = 2
        }

        void useBefore(bool b) {
            var x: i32
            if b {
                x = 1
            }
            let y = x
            let z = x
        }

        void assignedBoth(bool b) {
            var x: i32
            if b {
                x = 1
            } else {
                x = 2
            }
            let y = x
        }

        void outMissing(out i32 r, bool b) {
            if b {
                r = 1
            }
        }

        void outEarly(out i32 r, bool b) {
            if b {
                return
            }
            r = 2
        }

        void outRead(out i32 r) {
            let y = r
            r = 2
        }

        void outCall() {
            var v: i32
            fill(out v)
            let y = v
        }

        void infallible() {
            let v = try failing()
        }

        i32 undeclared() fails OtherError {
            let v = try failing()
            return v
        }

        i32 declared() fails NetError {
            let v = try failing()
            return v
        }

        i32 caught() {
            let v = failing() catch (NetError.timeout) 0
            return v
        }

        i32 caughtWrong() {
            let v = failing() catch (OtherError.gone) 0
            return v
        }

        i32 catchFalls() {
            let v = failing() catch {
            }
            return v
        }

        i32 catchLeaves() {
            let v = failing() catch {
                return 0
            }
            return v
        }

        void loopBreak(bool b) {
            var x: i32
            while true {
                if b {
                    x = 1
                    break
                }
            }
            let y = x
        }

        void loopMaybe(bool b) {
            var x: i32
            while b {
                x = 1
            }
            let y = x
        }

        void doLoop(bool b) {
            var x: i32
            do {
                x = 1
            } while b
            let y = x
        }

        void constantIf() {
            if false {
                let a = 1
            }
            var x: i32
            if true {
                x = 1
            }
            let y = x
        }

        void afterNever() {
            stop()
            let a = 1
        }

        void logic(bool b) {
            var x: i32
            let ok = b && x > 0
        }

        void sw(i32 k) {
            var x: i32
            switch k {
                case 1: x = 1
                default: x = 2
            }
            let y = x
        }

        void swPartial(i32 k) {
            var x: i32
            switch k {
                case 1: x = 1
                case 2: x = 2
            }
            let y = x
        }

        i32 partial() fails NoClosed {
            fail .timeout
        }

        i32 caughtExcluded() {
            let v = partial() catch (NetError.closed) 0
            return v
        }

        i32 exceptCaller() fails NoClosed {
            let v = try failing()
            return v
        }

        void loopContinue(bool b) {
            var x: i32
            do {
                if b {
                    continue
                }
                x = 1
            } while b
            let y = x
        }

        void forLoop(i32 n) {
            var x: i32
            for i in 0..n {
                x = i
            }
            let y = x
        }

        void cond(bool b) {
            var x: i32
            let y = b ? x : 1
        }

        void asserted() {
            var x: i32
            assert x > 0
        }

        void deferred(bool b) {
            var x: i32
            defer {
                let y = x
            }
            x = 1
        }
        """;

    /// <summary>The case functions: <c>Type.member</c> for a method, else the top-level name.</summary>
    public static readonly string[] FlowCases =
    [
        "Order.pay", "Order.payDone", "Order.viaThis", "Order.early", "Order.unknownState",
        "noReturn", "neverReturns", "unreachable", "useBefore", "assignedBoth", "outMissing", "outEarly", "outRead", "outCall",
        "infallible", "undeclared", "declared", "caught", "caughtWrong", "catchFalls", "catchLeaves",
        "loopBreak", "loopMaybe", "doLoop", "constantIf", "afterNever", "logic", "sw", "swPartial", "deferred",
        "caughtExcluded", "exceptCaller", "loopContinue", "forLoop", "cond", "asserted",
    ];

    private static readonly System.Type FlowType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.FlowAnalysis") ?? throw new InvalidOperationException("FlowAnalysis");

    private static readonly Lazy<Compilation> Compiled = new(() =>
    {
        Directory.CreateDirectory(Dir);
        var c = Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var parse = c.Diagnostics.Items.Where(d => d.Code.StartsWith("BYN1", StringComparison.Ordinal)).ToList();
        if (parse.Count > 0) throw new InvalidOperationException(string.Join("\n", parse.Select(Diag)));
        return c;
    });

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static FunctionSymbol Function(Compilation c, string name)
    {
        var module = c.Modules.First(m => m.Files.Any(f => f.Source.Path == Path.Combine(Dir, "main.byn")));
        var dot = name.IndexOf('.');
        if (dot < 0) return module.AllFunctions.First(f => f.Name == name && f.Owner is null);
        return module.Structs.First(s => s.Name == name[..dot]).Methods.First(m => m.Name == name[(dot + 1)..]);
    }

    /// <summary>The C# lines for one flow case: the diagnostics <c>FlowAnalysis.Analyze</c> reports when rerun on it.</summary>
    private static List<string> ExpectedFlow(string name)
    {
        var c = Compiled.Value;
        lock (c)
        {
            var mark = c.Diagnostics.Items.Count;
            FlowType.GetMethod("Analyze", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [c, Function(c, name)]);
            var lines = new List<string>();
            for (var i = mark; i < c.Diagnostics.Items.Count; i++) lines.AddRange(Diag(c.Diagnostics.Items[i]).Split('\n'));
            if (lines.Count == 0) lines.Add("ok");
            return lines;
        }
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    public static TheoryData<string> FlowCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in FlowCases) data.Add(k);
        return data;
    }

    [Theory]
    [MemberData(nameof(FlowCaseNames))]
    public void FlowCaseMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"flow.{name}", ExpectedFlow(name));

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The C# bound tree written out as Bayan ───────────────────────────────────

    /// <summary>Writes one function's bound body and the symbols it reaches as Bayan statements building them.</summary>
    private sealed class Writer(StringBuilder sb)
    {
        private readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
        private int _n;

        private string Fresh(string prefix) => $"{prefix}{_n++}";

        private void Line(string s) => sb.Append("    ").Append(s).Append('\n');

        private static string Span(Node n) => $"TextSpan {{ start: {n.Span.Start}, length: {n.Span.Length} }}";

        private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s[1..];

        private static string Ty(TypeSymbol t) => t switch
        {
            PrimitiveType p when p.IsNever => "prim(.neverKind)",
            PrimitiveType p => $"prim(.{Lower(p.Kind.ToString())})",
            ErrorType => "errorType()",
            ErrorSetSymbol s => $"errorSetTypeOf(t23mSet(ref c, by: \"{s.Name}\"))",
            // Flow reads a type only for never, void, error and a missing return's display; the cases return primitives.
            _ => "prim(.i32)",
        };

        private string Set(ErrorSetSymbol s)
        {
            if (_ids.TryGetValue(s, out var id)) return id;
            id = Fresh("s");
            Line($"let {id} = t23mSet(ref c, by: \"{s.Name}\")");
            return _ids[s] = id;
        }

        private string Variant(ErrorVariantSymbol v)
        {
            if (_ids.TryGetValue(v, out var id)) return id;
            var set = Set(v.Set);
            id = Fresh("v");
            Line($"let {id} = t23mVariant(ref c, from: {set}, by: \"{v.Name}\")");
            return _ids[v] = id;
        }

        private string Local(LocalSymbol l)
        {
            if (_ids.TryGetValue(l, out var id)) return id;
            id = Fresh("l");
            var at = l.Location is { } loc ? $"TextSpan {{ start: {loc.Span.Start}, length: {loc.Span.Length} }}" : "TextSpan { start: 0, length: 0 }";
            var modifier = l is ParameterSymbol p ? p.Modifier switch
            {
                ParamModifier.None => "none",
                ParamModifier.Ref => "refKeyword",
                ParamModifier.Move => "moveKeyword",
                ParamModifier.Out => "outKeyword",
                ParamModifier.In => "inKeyword",
                _ => throw new NotSupportedException(p.Modifier.ToString()),
            } : "none";
            Line($"let {id} = newLocal(ref c, by: LocalSymbol {{ name: \"{l.Name}\", nameSpan: {at}, type: {Ty(l.Type)}, isParameter: {(l is ParameterSymbol ? "true" : "false")}, modifier: .{modifier} }})");
            return _ids[l] = id;
        }

        private string Target(JumpTarget j)
        {
            if (_ids.TryGetValue(j, out var id)) return id;
            id = Fresh("j");
            Line($"let {id} = c.jumpTargets.length");
            Line("c.jumpTargets.append(JumpTarget {})");
            return _ids[j] = id;
        }

        public string Function(FunctionSymbol f)
        {
            if (_ids.TryGetValue(f, out var id)) return id;
            var ps = Fresh("a");
            Line($"{(f.Parameters.Count > 0 ? "var" : "let")} {ps} = List<LocalId>()");
            foreach (var p in f.Parameters) Line($"{ps}.append({Local(p)})");
            var sets = Fresh("a");
            Line($"{(f.ErrorSets.Count > 0 ? "var" : "let")} {sets} = List<ErrorSetId>()");
            foreach (var s in f.ErrorSets) Line($"{sets}.append({Set(s)})");
            var excluded = Fresh("a");
            Line($"{(f.ExcludedErrors.Count > 0 ? "var" : "let")} {excluded} = List<VariantId>()");
            foreach (var v in f.ExcludedErrors) Line($"{excluded}.append({Variant(v)})");
            id = Fresh("f");
            var kind = f.Kind == FunctionKind.Test ? "testKeyword" : Lower(f.Kind.ToString());
            var becomes = f.Decl?.Becomes is { } b ? $"\"{b.Text}\"" : "null";
            var refThis = f.Decl?.RefThis is { } r ? $"TextSpan {{ start: {r.Start}, length: {r.Length} }}" : "null";
            Line($"let {id} = newFunction(ref c, by: FunctionSymbol {{ name: \"{f.Name}\", kind: .{kind}, file: file, isStatic: {(f.IsStatic ? "true" : "false")}, parameters: move {ps}, returnType: {Ty(f.ReturnType)}, errorSets: move {sets}, excludedErrors: move {excluded}, hasFails: {(f.Fails ? "true" : "false")}, infersErrors: {(f.InfersErrors ? "true" : "false")}, becomes: {becomes}, refThis: {refThis} }})");
            return _ids[f] = id;
        }

        private string List(string type, IReadOnlyList<string> items)
        {
            var id = Fresh("a");
            Line($"{(items.Count > 0 ? "var" : "let")} {id} = List<{type}>()");
            foreach (var x in items) Line($"{id}.append({x})");
            return id;
        }

        private string Opt(BoundExpr? e) => e is null ? "-1" : Expr(e);

        public string Expr(BoundExpr e)
        {
            var sp = Span(e.Syntax);
            var id = Fresh("e");
            switch (e)
            {
                case BoundLiteral lit:
                    Line($"let {id} = addLiteral(ref c, from: BLiteral {{ span: {sp}, type: {Ty(lit.Type)}, value: Constant {{ kind: .integer }} }})");
                    break;
                case BoundLocal l:
                    Line($"let {id} = addLocal(ref c, from: BLocal {{ span: {sp}, local: {Local(l.Local)}, type: {Ty(l.Type)} }})");
                    break;
                case BoundThis t:
                    Line($"let {id} = addThis(ref c, from: BThis {{ span: {sp}, type: {Ty(t.Type)} }})");
                    break;
                case BoundCall call:
                {
                    var fn = Function(call.Function);
                    var receiver = Opt(call.Receiver);
                    var args = List("BExpr", call.Arguments.Select(Expr).ToList());
                    Line($"let {id} = addCall(ref c, from: BCall {{ span: {sp}, function: {fn}, receiver: {receiver}, arguments: move {args}, type: {Ty(call.Type)} }})");
                    break;
                }
                case BoundUnary u:
                {
                    var operand = Expr(u.Operand);
                    var op = u.Op == UnaryOp.Not ? "notKeyword" : Lower(u.Op.ToString());
                    Line($"let {id} = addUnary(ref c, from: BUnary {{ span: {sp}, op: .{op}, operand: {operand}, type: {Ty(u.Type)} }})");
                    break;
                }
                case BoundBinary b:
                {
                    var left = Expr(b.Left);
                    var right = Expr(b.Right);
                    Line($"let {id} = addBinary(ref c, from: BBinary {{ span: {sp}, op: .{Lower(b.Op.ToString())}, left: {left}, right: {right}, type: {Ty(b.Type)} }})");
                    break;
                }
                case BoundConditional k:
                {
                    var cond = Expr(k.Condition);
                    var t = Expr(k.WhenTrue);
                    var f = Expr(k.WhenFalse);
                    Line($"let {id} = addConditional(ref c, from: BConditional {{ span: {sp}, condition: {cond}, whenTrue: {t}, whenFalse: {f}, type: {Ty(k.Type)} }})");
                    break;
                }
                case BoundCoalesce k:
                {
                    var left = Expr(k.Left);
                    var right = Expr(k.Right);
                    Line($"let {id} = addCoalesce(ref c, from: BCoalesce {{ span: {sp}, left: {left}, right: {right}, type: {Ty(k.Type)} }})");
                    break;
                }
                case BoundTry t:
                {
                    var operand = Expr(t.Operand);
                    Line($"let {id} = addTry(ref c, from: BTry {{ span: {sp}, operand: {operand}, type: {Ty(t.Type)} }})");
                    break;
                }
                case BoundCatch k:
                {
                    var operand = Expr(k.Operand);
                    var binding = k.Binding is null ? "-1" : Local(k.Binding);
                    var variant = k.Variant is null ? "-1" : Variant(k.Variant);
                    var handler = Expr(k.Handler);
                    Line($"let {id} = addCatch(ref c, from: BCatch {{ span: {sp}, operand: {operand}, binding: {binding}, variant: {variant}, handler: {handler}, type: {Ty(k.Type)} }})");
                    if (((CatchExpr)k.Syntax).Variant is { } vs)
                        Line($"c.catchVariantSpans[{id}] = TextSpan {{ start: {vs.Span.Start}, length: {vs.Span.Length} }}");
                    break;
                }
                case BoundJump j:
                {
                    var jump = Stmt(j.Jump);
                    Line($"let {id} = addJump(ref c, from: BJump {{ span: {sp}, jump: {jump} }})");
                    break;
                }
                case BoundBlockExpr b:
                {
                    var block = Stmt(b.Block);
                    Line($"let {id} = addBlockExpr(ref c, from: BBlockExpr {{ span: {sp}, block: {block}, type: {Ty(b.Type)} }})");
                    break;
                }
                case BoundErrorVariant v:
                {
                    var variant = Variant(v.Variant);
                    var payload = List("BExpr", v.Payload.Select(Expr).ToList());
                    Line($"let {id} = addErrorVariant(ref c, from: BErrorVariant {{ span: {sp}, variant: {variant}, payload: move {payload}, type: {Ty(v.Type)} }})");
                    break;
                }
                case BoundRange r:
                {
                    var start = Expr(r.Start);
                    var end = Expr(r.End);
                    Line($"let {id} = addRange(ref c, from: BRange {{ span: {sp}, start: {start}, end: {end}, inclusive: {(r.Inclusive ? "true" : "false")}, type: {Ty(r.Type)} }})");
                    break;
                }
                case BoundErrorExpr:
                    Line($"let {id} = addErrorExpr(ref c, from: BErrorExpr {{ span: {sp} }})");
                    break;
                default:
                    throw new NotSupportedException(e.GetType().Name);
            }
            if (e.Constant?.Value is bool flag) Line($"setConstant(ref c, for: {id}, to: Constant {{ kind: .flag, flag: {(flag ? "true" : "false")} }})");
            return id;
        }

        public string Stmt(BoundStmt s)
        {
            var sp = Span(s.Syntax);
            var id = Fresh("t");
            switch (s)
            {
                case BoundBlock b:
                {
                    var list = List("BStmt", b.Statements.Select(Stmt).ToList());
                    Line($"let {id} = addBlock(ref c, from: BBlock {{ span: {sp}, statements: move {list} }})");
                    break;
                }
                case BoundLocalDecl d:
                {
                    var init = Opt(d.Initializer);
                    Line($"let {id} = addLocalDecl(ref c, from: BLocalDecl {{ span: {sp}, local: {Local(d.Local)}, initializer: {init} }})");
                    break;
                }
                case BoundExprStmt x:
                {
                    var e = Expr(x.Expr);
                    Line($"let {id} = addExprStmt(ref c, from: BExprStmt {{ span: {sp}, expr: {e} }})");
                    break;
                }
                case BoundAssign a:
                {
                    var target = Expr(a.Target);
                    var value = Expr(a.Value);
                    var op = a.Op is { } o ? $", op: .{Lower(o.ToString())}" : "";
                    Line($"let {id} = addAssign(ref c, from: BAssign {{ span: {sp}, target: {target}, value: {value}{op} }})");
                    break;
                }
                case BoundReturn r:
                {
                    var value = Opt(r.Value);
                    Line($"let {id} = addReturn(ref c, from: BReturn {{ span: {sp}, value: {value} }})");
                    break;
                }
                case BoundFail f:
                {
                    var error = Expr(f.Error);
                    Line($"let {id} = addFail(ref c, from: BFail {{ span: {sp}, error: {error} }})");
                    break;
                }
                case BoundBecome b:
                {
                    // Flow reads only the payload; the state's variant is not walked.
                    var payload = List("BExpr", b.Payload.Select(Expr).ToList());
                    Line($"let {id} = addBecome(ref c, from: BBecome {{ span: {sp}, state: -1, payload: move {payload} }})");
                    break;
                }
                case BoundBreak b:
                    Line($"let {id} = addBreak(ref c, from: BBreak {{ span: {sp}, target: {Target(b.Target)} }})");
                    break;
                case BoundContinue k:
                    Line($"let {id} = addContinue(ref c, from: BContinue {{ span: {sp}, target: {Target(k.Target)} }})");
                    break;
                case BoundIf i:
                {
                    var cond = Expr(i.Condition);
                    var then = Stmt(i.Then);
                    var otherwise = i.Else is null ? "-1" : Stmt(i.Else);
                    Line($"let {id} = addIf(ref c, from: BIf {{ span: {sp}, condition: {cond}, then: {then}, elseBranch: {otherwise}, isNever: {(i.Never ? "true" : "false")} }})");
                    break;
                }
                case BoundWhile w:
                {
                    var target = Target(w.Target);
                    var cond = Expr(w.Condition);
                    var body = Stmt(w.Body);
                    Line($"let {id} = addWhile(ref c, from: BWhile {{ span: {sp}, condition: {cond}, body: {body}, target: {target} }})");
                    break;
                }
                case BoundDoWhile d:
                {
                    var target = Target(d.Target);
                    var body = Stmt(d.Body);
                    var cond = Expr(d.Condition);
                    Line($"let {id} = addDoWhile(ref c, from: BDoWhile {{ span: {sp}, body: {body}, condition: {cond}, target: {target} }})");
                    break;
                }
                case BoundFor f:
                {
                    var target = Target(f.Target);
                    var variable = Local(f.Variable);
                    var iterable = Expr(f.Iterable);
                    var body = Stmt(f.Body);
                    Line($"let {id} = addFor(ref c, from: BFor {{ span: {sp}, variable: {variable}, iterable: {iterable}, body: {body}, target: {target} }})");
                    break;
                }
                case BoundSwitch w:
                {
                    var target = Target(w.Target);
                    var value = Expr(w.Value);
                    var sections = Fresh("a");
                    var built = new List<string>();
                    foreach (var section in w.Sections)
                    {
                        // Flow reads only a section's guard and body.
                        var guard = Opt(section.Guard);
                        var body = List("BStmt", section.Body.Select(Stmt).ToList());
                        built.Add($"BSwitchSection {{ span: {Span(section.Syntax)}, guard: {guard}, body: move {body} }}");
                    }
                    Line($"{(built.Count > 0 ? "var" : "let")} {sections} = List<BSwitchSection>()");
                    foreach (var b in built) Line($"{sections}.append({b})");
                    Line($"let {id} = addSwitch(ref c, from: BSwitch {{ span: {sp}, value: {value}, sections: move {sections}, target: {target}, exhaustive: {(w.Exhaustive ? "true" : "false")} }})");
                    break;
                }
                case BoundDefer d:
                {
                    var body = Stmt(d.Body);
                    Line($"let {id} = addDefer(ref c, from: BDefer {{ span: {sp}, body: {body} }})");
                    break;
                }
                case BoundAssert a:
                {
                    var cond = Expr(a.Condition);
                    Line($"let {id} = addAssert(ref c, from: BAssert {{ span: {sp}, condition: {cond} }})");
                    break;
                }
                case BoundErrorStmt:
                    Line($"let {id} = addErrorStmt(ref c, from: BErrorStmt {{ span: {sp} }})");
                    break;
                default:
                    throw new NotSupportedException(s.GetType().Name);
            }
            return id;
        }
    }

    private static string FlowCase(Compilation c, int index, string name)
    {
        var sb = new StringBuilder();
        sb.Append($"void t23mFlow{index}(ref Ctx c, for FileId file) {{\n");
        var w = new Writer(sb);
        var fn = Function(c, name);
        var f = w.Function(fn);
        var body = w.Stmt(fn.Body!);
        sb.Append($"    c.functions[{f}].body = {body}\n");
        sb.Append($"    let start = c.diagnostics.length\n");
        sb.Append($"    analyzeFlow(ref c, for: {f})\n");
        sb.Append($"    print(\"== flow.{name}\")\n");
        sb.Append($"    t23mDiags(ref c, from: start)\n");
        sb.Append("}\n\n");
        return sb.ToString();
    }

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource()
    {
        var c = Compiled.Value;
        var cases = new StringBuilder();
        var calls = new StringBuilder();
        lock (c)
        {
            for (var i = 0; i < FlowCases.Length; i++)
            {
                cases.Append(FlowCase(c, i, FlowCases[i]));
                calls.Append($"    t23mFlow{i}(ref c, for: file)\n");
            }
        }
        return """
            import lexer
            import parser

            const world: string = "WORLD\n"

            void t23mDiags(ref Ctx c, from i64 start) {
                var any = false
                for i in start..c.diagnostics.length {
                    let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                    let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                    print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
                    any = true
                }
                if !any { print("ok") }
            }

            ErrorSetId t23mSet(ref Ctx c, by string name) => typeIndex(c.modules[c.entryModule].types[name])

            VariantId t23mVariant(ref Ctx c, from ErrorSetId s, by string name) {
                for v in c.errorSets[s].variants {
                    if c.errorVariants[v].name == name { return v }
                }
                return -1
            }

            CASES
            i32 main(ReadOnlySpan<string> args) {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t23-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t23-cases",
                    displayRoot: "tmp/t23-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t23-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                let file = c.modules[c.entryModule].files[0]
            CALLS    return 0
            }
            """.Replace("WORLD", Escape(Source)).Replace("CASES", cases.ToString()).Replace("CALLS", calls.ToString());
    }
}
