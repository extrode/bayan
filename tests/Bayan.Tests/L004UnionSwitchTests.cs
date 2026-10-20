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
    private const string Union = "union U { A, B(i64 x), C }\n";

    // Arms are written once; "default" and "_" stand for the default arm in either form.
    public static TheoryData<string, string[], string> Cases => new()
    {
        { "all plain", [".A", ".B(let x)", ".C"], "" },
        { "var binding", [".A", ".B(var x)", ".C"], "" },
        { "wildcard subpattern", [".A", ".B(_)", ".C"], "" },
        { "guarded only", [".A", ".B(var x) when x > 0", ".C"], ".B" },
        { "guard true is still a guard", [".A", ".B(let x) when true", ".C"], ".B" },
        { "guarded plus plain", [".A", ".B(var x) when x > 0", ".B(_)", ".C"], "" },
        { "or covers two", [".A or .C", ".B(var x)"], "" },
        { "or leaves one", [".A or .C", ".B(1)"], ".B" },
        { "refutable nested only", [".A", ".B(1)", ".C"], ".B" },
        { "refutable nested then plain", [".A", ".B(1)", ".B(let x)", ".C"], "" },
        { "default arm", [".A", "default"], "" },
        { "default only", ["default"], "" },
        { "two missing", [".B(let x)"], ".A, .C" },
        { "none covered but one", [".C"], ".A, .B" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SwitchStatement(string name, string[] arms, string missing)
    {
        var cases = string.Concat(arms.Select(a => a == "default"
            ? "        default:\n            print(\"d\");\n"
            : $"        case {a}:\n            print(\"a\");\n"));
        Check(name, $"{Union}void f(U u) {{\n    switch (u) {{\n{cases}    }}\n}}\nf(U.A);\n", missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SwitchExpression(string name, string[] arms, string missing)
    {
        var cases = string.Concat(arms.Select(a => $"    {(a == "default" ? "_" : a)} => 1,\n"));
        Check(name, $"{Union}i64 f(U u) => u switch {{\n{cases}}};\nprint($\"{{f(U.A)}}\");\n", missing);
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
