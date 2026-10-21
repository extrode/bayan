using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// L004 (docs/laws/004-union-switch-exhaustive.md): a switch over a union reports BYN2410 exactly when some
/// variant is uncovered, and names every uncovered variant. Each row is run as a statement and as an expression.
/// </summary>
[Trait("Law", "L004")]
public sealed class L004UnionSwitchTests
{
    private const string Union = "union U { a, b(i64 x), c }\n";

    // Arms are written once; "default" and "_" stand for the default arm in either form.
    public static TheoryData<string, string[], string> Cases => new()
    {
        { "all plain", [".a", ".b(let x)", ".c"], "" },
        { "var binding", [".a", ".b(var x)", ".c"], "" },
        { "wildcard subpattern", [".a", ".b(_)", ".c"], "" },
        { "guarded only", [".a", ".b(var x) when x > 0", ".c"], ".b" },
        { "guard true is still a guard", [".a", ".b(let x) when true", ".c"], ".b" },
        { "guarded plus plain", [".a", ".b(var x) when x > 0", ".b(_)", ".c"], "" },
        { "or covers two", [".a or .c", ".b(var x)"], "" },
        { "or leaves one", [".a or .c", ".b(1)"], ".b" },
        { "refutable nested only", [".a", ".b(1)", ".c"], ".b" },
        { "refutable nested then plain", [".a", ".b(1)", ".b(let x)", ".c"], "" },
        { "default arm", [".a", "default"], "" },
        { "default only", ["default"], "" },
        { "two missing", [".b(let x)"], ".a, .c" },
        { "none covered but one", [".c"], ".a, .b" },
        { "let binding covers rest", [".a", "let s"], "" },
        { "var binding covers rest", [".a", "var s"], "" },
        { "let binding only", ["let s"], "" },
        { "guarded binding", [".a", "let s when true"], ".b, .c" },
        { "guarded wildcard", ["_ when true"], ".a, .b, .c" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SwitchStatement(string name, string[] arms, string missing)
    {
        var cases = string.Concat(arms.Select(a => a == "default"
            ? "        default:\n            print(\"d\");\n"
            : $"        case {a}:\n            print(\"a\");\n"));
        Check(name, $"{Union}void f(U u) {{\n    switch (u) {{\n{cases}    }}\n}}\nf(U.a);\n", missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SwitchExpression(string name, string[] arms, string missing)
    {
        var cases = string.Concat(arms.Select(a => $"    {(a == "default" ? "_" : a)} => 1,\n"));
        Check(name, $"{Union}i64 f(U u) => u switch {{\n{cases}}};\nprint($\"{{f(U.a)}}\");\n", missing);
    }

    private static void Check(string name, string source, string missing)
    {
        var c = Compilation.Create([new SourceText("l004.byn", System.Text.Encoding.UTF8.GetBytes(source))], ".");
        var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
        var reported = errors.Where(d => d.Code == "BYN2410").ToList();
        Assert.True(errors.Count == reported.Count, $"{name}: unexpected errors: {string.Join("; ", errors.Select(e => e.Code + " " + e.Message))}\n{source}");
        if (missing.Length == 0)
        {
            Assert.True(reported.Count == 0, $"{name}: unexpected BYN2410 ({reported.FirstOrDefault()?.Label})\n{source}");
            return;
        }
        var d = Assert.Single(reported);
        Assert.Equal("not covered: " + missing, d.Label);
    }
}
