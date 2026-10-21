using Bayan.Cli;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

public sealed class ProgressTests
{
    private static string Run(Style style, bool isTty = true, bool json = false, string? ci = null, string phase = "compiling")
    {
        var writer = new StringWriter();
        using (var progress = Progress.Start(writer, style, isTty, json, name => name == "CI" ? ci : null, phase, TimeSpan.Zero)) progress.Done();
        return writer.ToString();
    }

    [Fact]
    public void TheLineIsCutToTheTerminalWidth()
    {
        var phase = "testing " + new string('w', 60);
        Assert.Equal("\r◆◇◇ testing " + new string('w', 26) + "…\r\e[2K", Run(Style.Plain with { Width = 40 }, phase: phase));
        Assert.Equal("\r... testing " + new string('w', 24) + "...\n", Run(Style.Plain with { Symbols = Symbols.Ascii, Width = 40 }, phase: phase));
        Assert.Equal("\r◆◇◇ testing www\r\e[2K", Run(Style.Plain with { Width = 40 }, phase: "testing www"));
    }

    [Fact]
    public void WideScalarsCountTwoColumnsAndMarksNone()
    {
        // width 40: room 40 - 1 - 3 - 1 = 35, keep 34 before the ellipsis
        Assert.Equal("\r◆◇◇ " + new string('漢', 17) + "…\r\e[2K", Run(Style.Plain with { Width = 40 }, phase: new string('漢', 30)));
        Assert.Equal("\r◆◇◇ " + new string('漢', 17) + "…\r\e[2K", Run(Style.Plain with { Width = 40 }, phase: new string('漢', 17) + "xy"));
        Assert.Equal("\r◆◇◇ " + new string('e', 35) + "\u0301" + "\r\e[2K", Run(Style.Plain with { Width = 40 }, phase: new string('e', 35) + "\u0301"));
    }

    [Fact]
    public void ControlCharactersInThePhaseAreShownEscaped() =>
        Assert.Equal("\r◆◇◇ testing y\\u{1b}]0;PWNED\\u{7}\\u{1b}[2J\r\e[2K", Run(Style.Plain, phase: "testing y\u001b]0;PWNED\u0007\u001b[2J"));

    [Fact]
    public void UnicodeRewritesInPlaceAndClears() =>
        Assert.Equal("\r◆◇◇ compiling\r\e[2K", Run(Style.Plain));

    [Fact]
    public void AsciiPrintsOnceWithoutRewriting() =>
        Assert.Equal("\r... compiling\n", Run(Style.Plain with { Symbols = Symbols.Ascii }));

    [Fact]
    public void CiPrintsNothing() => Assert.Equal("", Run(Style.Plain, ci: "1"));

    [Fact]
    public void EmptyCiDoesNotSuppress() => Assert.NotEqual("", Run(Style.Plain, ci: ""));

    [Fact]
    public void NonTerminalPrintsNothing() => Assert.Equal("", Run(Style.Plain, isTty: false));

    [Fact]
    public void JsonModePrintsNothing() => Assert.Equal("", Run(Style.Plain, json: true));

    [Fact]
    public void BelowThresholdPrintsNothing()
    {
        var writer = new StringWriter();
        using (var progress = Progress.Start(writer, Style.Plain, true, false, _ => null, "compiling", TimeSpan.FromHours(1))) progress.Done();
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void TimerCyclesFramesThenClears()
    {
        var writer = new StringWriter();
        using (var progress = Progress.Start(writer, Style.Plain, true, false, _ => null, "compiling", TimeSpan.FromMilliseconds(1)))
        {
            SpinWait.SpinUntil(() => writer.ToString().Length > 0, TimeSpan.FromSeconds(5));
            progress.Done();
        }
        var text = writer.ToString();
        Assert.StartsWith("\r◆◇◇ compiling", text);
        Assert.EndsWith("\r\e[2K", text);
    }
}
