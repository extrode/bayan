using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Syntax.Ast;

namespace Bayan.Tests;

/// <summary>
/// 011 T21 (Q849, Q850): the syntax node each selfhost bound expression records (<c>exprSyntax</c>) against C#'s
/// <c>BoundNode.Syntax</c>, compared in-process. For every bound expression reachable from an <c>assert</c> condition
/// (<c>BoundTree.Descendants</c>), one line: the node's span, then <c>Syntax</c>'s record type and span when it is an
/// <c>Expr</c>, else <c>-</c>; and for the walked children (<c>Inner</c>, <c>Member</c>, <c>Callee</c>, <c>Left</c>,
/// <c>Value</c>, <c>Target</c>, <c>Condition</c>, <c>Operand</c>, unary <c>Op</c>) the same, so <c>Lowerer.Anchor</c>
/// and <c>IsLiteral</c> see the C# tree.
/// </summary>
public sealed class SelfhostEmitterT21Tests
{
    private const string Main = """
        i32 main(ReadOnlySpan<string> args) {
            for i in 0..args.length {
                let all = args[i].startsWith("+")
                let path = all ? $"{args[i][1..]}" : args[i]
                var c = fromPath(path, by: in CompilationOptions { includeTests: true }, wholeDirectory: false)
                Io.stdout.write(all ? t21PrintAll(in c, at: args[i]) : t21Print(in c, at: args[i]))
            }
            return 0
        }

        string t21Print(in Ctx c, at string path) uses alloc {
            var sb = StrBuf()
            sb.append($"== {path}\n")
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].body == -1 { continue }
                    for s in descendants(in c, for: c.functions[fn].body) {
                        if boundKind(s) != .assertStmt { continue }
                        let a = nodeSpan(in c, for: s)
                        sb.append($"assert {a.start}..{a.end}\n")
                        for e in descendants(in c, for: c.bAsserts[s & 0xFFFFFFFF].condition) {
                            let sp = nodeSpan(in c, for: e)
                            sb.append($"{sp.start}..{sp.end} {t21Top(in c, for: e)}\n")
                        }
                    }
                }
            }
            return $"{sb.finish()}"
        }

        string t21PrintAll(in Ctx c, at string path) uses alloc {
            var sb = StrBuf()
            sb.append($"== {path}\n")
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].body == -1 { continue }
                    for e in descendants(in c, for: c.functions[fn].body) {
                        if !isExprKind(boundKind(e)) { continue }
                        sb.append($"{t21Top(in c, for: e)}\n")
                    }
                }
            }
            return $"{sb.finish()}"
        }

        string t21Top(in Ctx c, for BNode e) uses alloc {
            if (exprSyntax(in c, for: e) is { } n) { return t21Syntax(in c, for: n) }
            return "-"
        }

        string t21Node(in Ctx c, at i64 h) uses alloc => h == -1 ? "-" : t21Syntax(in c, for: exprSyntaxNode(in c, at: h))

        string t21Syntax(in Ctx c, for ExprSyntax n) uses alloc {
            var sb = StrBuf()
            sb.append($"{n.kind} {n.span.start}..{n.span.end}")
            if n.inner != -1 { sb.append($" inner({t21Node(in c, at: n.inner)})") }
            if n.kind == .memberExpr { sb.append($" member({n.member.start}..{n.member.end})") }
            if n.callee != -1 { sb.append($" callee({t21Node(in c, at: n.callee)})") }
            if n.left != -1 { sb.append($" left({t21Node(in c, at: n.left)})") }
            if n.value != -1 { sb.append($" value({t21Node(in c, at: n.value)})") }
            if n.target != -1 { sb.append($" target({t21Node(in c, at: n.target)})") }
            if n.condition != -1 { sb.append($" condition({t21Node(in c, at: n.condition)})") }
            if n.operand != -1 { sb.append($" operand({n.op} {t21Node(in c, at: n.operand)})") }
            return $"{sb.finish()}"
        }
        """;

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Span(Node n) => $"{n.Span.Start}..{n.Span.End}";

    private static string NodeText(Node n)
    {
        if (n is not Expr) return "-";
        var s = new StringBuilder($"{Lower(n.GetType().Name)} {Span(n)}");
        switch (n)
        {
            case ParenExpr p: s.Append($" inner({NodeText(p.Inner)})"); break;
            case MemberExpr m: s.Append($" member({Span(m.Member)})"); break;
            case CallExpr c: s.Append($" callee({NodeText(c.Callee)})"); break;
            case BinaryExpr b: s.Append($" left({NodeText(b.Left)})"); break;
            case IsExpr i: s.Append($" value({NodeText(i.Value)})"); break;
            case IndexExpr i: s.Append($" target({NodeText(i.Target)})"); break;
            case ConditionalExpr c: s.Append($" condition({NodeText(c.Condition)})"); break;
            case UnaryExpr u: s.Append($" operand({(u.Op == UnaryOp.Not ? "notKeyword" : Lower(u.Op.ToString()))} {NodeText(u.Operand)})"); break;
        }
        return s.ToString();
    }

    /// <summary>The C# lines for <paramref name="path"/>, as the generated main prints them; a leading <c>+</c> asks for
    /// every bound expression of every function body instead of those under an <c>assert</c> condition.</summary>
    private static List<string> Expected(string path)
    {
        var all = path.StartsWith('+');
        var c = Compilation.FromPath(all ? path[1..] : path, new CompilationOptions(IncludeTests: true), wholeDirectory: false);
        var lines = new List<string>();
        if (all)
        {
            foreach (var fn in c.Modules.SelectMany(m => m.AllFunctions).Where(f => f.Body is not null))
                foreach (var e in BoundTree.Descendants(fn.Body!).OfType<BoundExpr>())
                    lines.Add(NodeText(e.Syntax));
            return lines;
        }
        foreach (var fn in c.Modules.SelectMany(m => m.AllFunctions).Where(f => f.Body is not null))
            foreach (var a in BoundTree.Descendants(fn.Body!).OfType<BoundAssert>())
            {
                lines.Add($"assert {Span(a.Syntax)}");
                foreach (var e in BoundTree.Descendants(a.Condition))
                    lines.Add($"{Span(e.Syntax)} {NodeText(e.Syntax)}");
            }
        return lines;
    }

    private static void AssertSameAsCSharp(IReadOnlyList<string> paths)
    {
        var sections = SelfhostSemanticsHarness.RunSections(Main, paths.ToArray());
        var compared = 0;
        foreach (var path in paths)
        {
            var expected = Expected(path);
            compared += expected.Count;
            Assert.True(sections.TryGetValue(path, out var actual), $"{path}: not printed");
            SelfhostEmitterHarness.AssertSame(path, string.Join("\n", expected), string.Join("\n", actual!));
        }
        Assert.True(compared > 0, "the oracle produced no lines");
    }

    /// <summary>Every <c>run/</c> and <c>diag/</c> case with an <c>assert</c>: a file, or a directory case's directory.</summary>
    private static List<string> CorpusWithAsserts()
    {
        var paths = new List<string>();
        foreach (var dir in new[] { "run", "diag" })
        {
            var root = Path.Combine(Golden.CasesRoot, dir);
            foreach (var file in Directory.EnumerateFiles(root, "*.byn", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (!File.ReadAllText(file).Contains("assert", StringComparison.Ordinal)) continue;
                var parent = Path.GetDirectoryName(file)!;
                var path = parent == root ? file : Path.Combine(root, Path.GetRelativePath(root, parent).Split(Path.DirectorySeparatorChar)[0]);
                if (!paths.Contains(path)) paths.Add(path);
            }
        }
        return paths;
    }

    [Fact]
    public void CorpusAssertConditionsRecordTheCSharpSyntax() => AssertSameAsCSharp(CorpusWithAsserts());

    private const string ValidFixture = """
        struct Point {
            i64 x
            i64 y
        }

        enum Color {
            red
            green
        }

        union error E {
            bad
        }

        i64 twice(i64 n) => n * 2

        i64 apply(i64 v, by i64 -> i64 f) => f(v)

        i64 parsed(i64 n) fails E {
            fail .bad if n == 0
            return n
        }

        let p = Point { x: 1, y: 2 }
        let xs = [1, 2, 3]
        let o: i64? = null
        assert -3 < 0 && !(p.x == 2) && ~1 != 0
        assert (o ?? 4) == 4
        assert p.x in 0..5
        assert (p.x > 0 ? 1 : 2) == 1
        assert i32(p.y) == 2
        assert xs[0] + xs.length == 4
        assert $"{p.x}-{twice(2)}" == "1-4"
        assert (p with { x: 5 }).x == 5
        assert Color.red != Color.green
        assert apply(3, by: x => x + 1) == 4
        assert xs[1..].length == 2
        assert (p.x switch { 1 => 10, _ => 20 }) == 10
        assert (parsed(7) catch 0) == 7
        assert ((1 + 2) * 3) == 9
        assert "ab".length == 2 && true
        assert (line: 1, col: 2).line == 1 && o is { }
        """;

    private const string ErrorFixture = """
        struct Point {
            i64 x
        }

        union Shape {
            circle(f64 r)
            dot
        }

        let p = Point { x: 1 }
        let s: Shape = .circle(r: 2.0)
        let m: Map<string, i64> = ["a": 1]
        assert p.nope == 1
        assert undefinedName
        assert 1 + "a" == 2
        assert i8(300) == 0
        assert 9223372036854775807 + 1 > 0
        assert s.r > 1.0
        assert m["a"] == 1
        assert [1, 2].length + p.x == 3
        assert -(-5) == 5 && !true == false
        """;

    private const string MoreFixture = """
        union error A {
            bad
        }

        union error B {
            bad
        }

        struct Order {
            i64 id
            states {
                draft
                paid(i64 payment)
            }
            void pay(ref this is .draft, i64 amount) becomes .paid {
                become .paid(amount)
            }
        }

        struct Box {
            i64 v
        }

        enum Planet(f64 mass) {
            small(1.0)
            big
        }

        struct Ship {
            i64 id
            states {
                a(i64 p)
                b(i64 q, i64 p)
            }
            i64 get(this is .b) => this.p
        }

        struct Hold {
            i64 id
            states {
                a(i64 p)
                c
            }
        }

        void payloads(Ship s, by Hold h) {
            let pp = Order.paid { id: 1, payment: 2 }
            assert pp.payment == 2
            let sp = s.p
            let hp = h.p
            let sn = s.nope
        }

        struct Early {
            Late l = Late()
        }

        struct Late {
            i64 x = 1
        }

        struct Pair<T> {
            T a
        }

        interface IShower {
            string show<T>(T x)
        }

        void viaParam<S>(S s) where S : IShower {
            let r = s.show(1)
        }

        void trusted(Untrusted<i64> v) {
            let n = 5
            let a = trust(move v, reason: "checked")
            let b = trust(move n, reason: "checked")
            let la = List()
            let pa = Pair()
            let e = Early()
            let op = Order.paid { id: 1 }
        }

        void r() => 0..3

        void l() => [1]

        void s() => stackalloc i64[4]

        i64 both(i64 n) fails A, B {
            return .bad
        }

        void inferred(i64 n) fails {
            return .nope
        }

        void body() {
            for i in 0..3 {
                print($"{i}")
            }
            let m: Map<string, i64> = ["a": 1, "b": 2]
            let e: Map<string, i64> = []
            let xs = [1, 2]
            let t = i64.tryFrom(nope)
            let p = i64.parse()
            let q = f64.tryParse()
            let w = Owned()
            let u = Untrusted()
            let ys: List<string> = [..xs]
            let bx = Box()
            let call = nope(1)
            var o = Order.draft { id: 7 }
            o.pay(1)
            o = Order.draft { id: 8 }
            let bm: Map<string, i64> = [1]
            let g = List<Nope>()
            let ls: List<List<i64>> = [[1]]
            let ms: List<List<i64>> = [..ls]
        }
        """;

    /// <summary>Folding, coalesce, range, conditional, cast, index/slice, interpolation, <c>with</c>, literals, lambda,
    /// switch, catch, tuple, map and <c>is</c>; then the error paths (unknown member/name, mismatched operands, constant
    /// overflow, payload field, map index); then expression bodies, failure returns, builtin calls without arguments,
    /// spreads and map targets in function bodies.</summary>
    [Fact]
    public void FixturesRecordTheCSharpSyntax()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t21-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new List<string>();
            foreach (var (name, source) in new[] { ("valid", ValidFixture), ("errors", ErrorFixture), ("more", MoreFixture) })
            {
                var path = Path.Combine(dir, name, "main.byn");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, source);
                paths.Add(path);
            }
            // Every bound expression of the fixtures' bodies, and of the `for try` loop whose `next()` member C# names at
            // the iterable (Q739); the lines carry the recorded syntax only, in `Descendants` order.
            paths.Add(Path.Combine(Golden.CasesRoot, "run", "for-try-var.byn"));
            AssertSameAsCSharp(paths.Concat(paths.Select(p => "+" + p)).ToList());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
