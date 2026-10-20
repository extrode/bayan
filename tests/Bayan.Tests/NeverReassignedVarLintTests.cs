using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>BYN2311: a <c>var</c> local or pattern binding that nothing ever writes should be a <c>let</c> (spec §7.1, §6.5).</summary>
public sealed class NeverReassignedVarLintTests
{
    private const string Prelude = """
        union U { A, B(i64 x), C }
        struct P { i64 x; }
        union W { Wrap(P p), Other }
        union L { Items(List<i64> items), None }
        void bump(ref i64 n) { n += 1; }
        void reset(ref P p) { p.x = 0; }

        """;

    [Theory]
    [InlineData("local", "var v = 1;\n    print($\"{v}\");", "v")]
    [InlineData("is pattern", "if (u is .B(var v)) print($\"{v}\");", "v")]
    [InlineData("switch statement arm", "switch (u) {\n        case .B(var v):\n            print($\"{v}\");\n        default:\n            print(\"d\");\n    }", "v")]
    [InlineData("switch expression arm", "let r = u switch { .B(var v) => v, _ => 0 };\n    print($\"{r}\");", "v")]
    [InlineData("irrefutable arm", "let r = u switch { .A => 0, var v => 1 };\n    print($\"{r}\");", "v")]
    [InlineData("property subpattern", "let q = P { x = 1 };\n    if (q is { x: var v }) print($\"{v}\");", "v")]
    public void ReportsUnwrittenVar(string name, string body, string variable)
    {
        var d = Assert.Single(Compile(body));
        Assert.True(d.Message == $"`{variable}` is never reassigned; declare it with `let`", name);
        Assert.NotNull(d.Fix);
        Assert.Equal("var", d.Source!.GetText(d.Fix.Span));
        Assert.Equal("let", d.Fix.Replacement);
    }

    [Theory]
    [InlineData("let local", "let v = 1;\n    print($\"{v}\");")]
    [InlineData("let pattern", "if (u is .B(let v)) print($\"{v}\");")]
    [InlineData("for variable", "for (var i in 0..3) print($\"{i}\");")]
    [InlineData("parameter", "print($\"{u is .A}\");")]
    [InlineData("local assigned", "var v = 1;\n    v = 2;\n    print($\"{v}\");")]
    [InlineData("local compound-assigned", "var v = 1;\n    v += 2;\n    print($\"{v}\");")]
    [InlineData("local incremented", "var v = 1;\n    v++;\n    print($\"{v}\");")]
    [InlineData("local passed by ref", "var i64 v = 1;\n    bump(ref v);\n    print($\"{v}\");")]
    [InlineData("local field assigned", "var q = P { x = 1 };\n    q.x = 2;\n    print($\"{q.x}\");")]
    [InlineData("local mutating call", "var xs = List<i64>();\n    xs.add(1);\n    print($\"{xs.length}\");")]
    [InlineData("pattern assigned", "if (u is .B(var v)) {\n        v = 2;\n        print($\"{v}\");\n    }")]
    [InlineData("pattern passed by ref", "if (u is .B(var v)) {\n        bump(ref v);\n        print($\"{v}\");\n    }")]
    [InlineData("pattern field assigned", "let w = W.Wrap(P { x = 1 });\n    if (w is .Wrap(var p)) {\n        p.x = 2;\n        print($\"{p.x}\");\n    }")]
    [InlineData("pattern struct by ref", "let w = W.Wrap(P { x = 1 });\n    if (w is .Wrap(var p)) {\n        reset(ref p);\n        print($\"{p.x}\");\n    }")]
    public void SilentWhenNotAVarOrWritten(string name, string body)
    {
        var warnings = Compile(body);
        Assert.True(warnings.Count == 0, $"{name}: {string.Join("; ", warnings.Select(w => w.Message))}");
    }

    [Fact]
    public void BorrowedUniqueBindingIsReportedOnceAsMutateBorrow()
    {
        var c = Create("let l = L.Items(List<i64>());\n    if (l is .Items(var items)) items.add(1);");
        Assert.Single(c.Diagnostics.Items, d => d.Code == "BYN2783");
        Assert.DoesNotContain(c.Diagnostics.Items, d => d.Code == "BYN2311");
    }

    private static List<Bayan.Syntax.Diagnostics.Diagnostic> Compile(string body)
    {
        var c = Create(body);
        var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => e.Code + " " + e.Message)));
        return c.Diagnostics.Items.Where(d => d.Code == "BYN2311").ToList();
    }

    private static Compilation Create(string body) =>
        Compilation.Create([new SourceText("lint.byn", System.Text.Encoding.UTF8.GetBytes(
            $"{Prelude}void f(U u) {{\n    {body}\n}}\nf(U.A);\n"))], ".");
}
