using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>The failed-<c>assert</c> diagram (HANDOFF §16, plan J5): its layout, and how <c>bayan test</c> redraws it.</summary>
public sealed class PowerAssertTests
{
    /// <summary>The text <paramref name="pieces"/> write when the columns in <paramref name="shown"/> are shown, a value as <c>&lt;column&gt;</c>.</summary>
    private static string Render(List<PowerAssert.Piece> pieces, int[] columns, bool[] shown) =>
        string.Concat(pieces
            .Where(p => p.When.All(k => shown[k]) && p.Unless.All(k => !shown[k])
                && (p.AnyFrom is not { } from || Enumerable.Range(from, shown.Length - from).Any(k => shown[k])))
            .Select(p => p.Value < 0 ? p.Text : $"<{columns[p.Value]}>"));

    private static string Always(params int[] columns) =>
        Render(PowerAssert.Diagram(columns, columns.Select(_ => true).ToList()), columns, columns.Select(_ => true).ToArray());

    [Fact]
    public void BarsSitUnderEachColumnAndValuesHangRightToLeft()
    {
        const string i = PowerAssert.Indent;
        Assert.Equal("\n" + i + "  │   │  │" + "\n" + i + "  │   │  <9>" + "\n" + i + "  │   <6>" + "\n" + i + "  <2>", Always(2, 6, 9));
        Assert.All(PowerAssert.Diagram([2, 6, 9], [true, true, true]), p => Assert.True(p.When.Length == 0 && p.Unless.Length == 0 && p.AnyFrom is null));
    }

    [Fact]
    public void AColumnThatDidNotRunIsLeftOut()
    {
        int[] columns = [2, 6, 9, 12];
        bool[] always = [true, false, true, false];
        var pieces = PowerAssert.Diagram(columns, always);
        foreach (var six in new[] { false, true })
            foreach (var twelve in new[] { false, true })
            {
                var shown = columns.Where((c, k) => always[k] || (c == 6 ? six : twelve)).ToArray();
                Assert.Equal(Always(shown), Render(pieces, columns, [true, six, true, twelve]));
            }
        Assert.All(pieces, p => Assert.DoesNotContain(p.When.Concat(p.Unless), k => always[k]));
    }

    [Fact]
    public void NoTrailingBlankWhenTheLastColumnsDidNotRun()
    {
        int[] columns = [2, 6];
        var pieces = PowerAssert.Diagram(columns, [false, false]);
        Assert.Equal("", Render(pieces, columns, [false, false]));
        Assert.Equal(Always(2), Render(pieces, columns, [true, false]));
        Assert.Equal(Always(6), Render(pieces, columns, [false, true]));
    }

    [Fact]
    public void ColumnsCountOnTheFlattenedStatement()
    {
        Assert.Equal("assert a .b == 1", PowerAssert.Flatten("assert a   \n      .b == 1"));
        Assert.Equal(10, PowerAssert.Column("assert a\n    ."));
        Assert.Equal(10, PowerAssert.Column("assert \"é\""));
    }

    private static string Details(string value) =>
        "panic: assert total == 3\n" +
        PowerAssert.Indent + "       │     │\n" +
        PowerAssert.Indent + "       │     false\n" +
        PowerAssert.Indent + "       " + value + "\n" +
        "  at main.test.byn:4";

    [Fact]
    public void TheDiagramMovesLeftWithTheStatement() =>
        Assert.Equal(["assert total == 3", "       │     │", "       │     false", "       12", "main.test.byn:4"],
            CliApp.FailureDetail(Details("12"), verbose: false, Symbols.Unicode, 80));

    [Fact]
    public void AsciiDrawsBarsAsPipes() =>
        Assert.Equal(["assert total == 3", "       |     |", "       |     false", "       12", "main.test.byn:4"],
            CliApp.FailureDetail(Details("12"), verbose: false, Symbols.Ascii, 80));

    [Fact]
    public void LongValuesAreCutAtTheWidth()
    {
        var value = "12345678901234567890";
        Assert.Equal("       1234567…", CliApp.FailureDetail(Details(value), verbose: false, Symbols.Unicode, 15)[3]);
        Assert.Equal("       12345...", CliApp.FailureDetail(Details(value), verbose: false, Symbols.Ascii, 15)[3]);
        Assert.Equal("       │     │", CliApp.FailureDetail(Details(value), verbose: false, Symbols.Unicode, 15)[1]);
    }

    [Fact]
    public void OtherPanicsKeepTheirIndentedLines() =>
        Assert.Equal(["boom", PowerAssert.Indent + "│ not a diagram", "main.test.byn:4"],
            CliApp.FailureDetail("panic: boom\n" + PowerAssert.Indent + "│ not a diagram\n  at main.test.byn:4", verbose: false, Symbols.Ascii, 80));
}
