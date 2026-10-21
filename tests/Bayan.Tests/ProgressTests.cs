using Bayan.Cli;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

public sealed class ProgressTests
{
    private static string Run(Style style, bool isTty = true, bool json = false, string? ci = null)
    {
        var writer = new StringWriter();
        using (var progress = Progress.Start(writer, style, isTty, json, name => name == "CI" ? ci : null, "compiling", TimeSpan.Zero)) progress.Done();
        return writer.ToString();
    }

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
