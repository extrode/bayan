using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>The failed-<c>assert</c> diagram (HANDOFF §16, plan J5): its layout, and how <c>bayan test</c> redraws it.</summary>
public sealed class PowerAssertTests
{
    [Fact]
    public void BarsSitUnderEachColumn()
    {
        var (bars, _) = PowerAssert.Layout([2, 6, 9]);
        Assert.Equal("  │   │  │", bars);
        Assert.Equal("", PowerAssert.Layout([]).Bars);
    }

    [Fact]
    public void ValuesHangRightToLeftWithTheBarsToTheirLeft()
    {
        var (_, prefixes) = PowerAssert.Layout([2, 6, 9]);
        Assert.Equal(["  │   │  ", "  │   ", "  "], prefixes);
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
