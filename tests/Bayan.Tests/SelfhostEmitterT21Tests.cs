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
                var c = fromPath(args[i], by: in CompilationOptions { includeTests: true }, wholeDirectory: false)
                Io.stdout.write(t21Print(in c, at: args[i]))
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
                            let h = c.exprSyntaxOf.tryGet(at: e) ?? -1
                            sb.append($"{sp.start}..{sp.end} {t21Node(in c, at: h)}\n")
                        }
                    }
                }
            }
            return $"{sb.finish()}"
        }

        string t21Node(in Ctx c, at i64 h) uses alloc {
            if h == -1 { return "-" }
            let n = exprSyntaxNode(in c, at: h)
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

    /// <summary>The C# lines for <paramref name="path"/>, as the generated main prints them.</summary>
    private static List<string> Expected(string path)
    {
        var c = Compilation.FromPath(path, new CompilationOptions(IncludeTests: true), wholeDirectory: false);
        var lines = new List<string>();
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
}
