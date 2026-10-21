using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// L011: removing the SGR escapes from a coloured rendering gives exactly the <c>NO_COLOR</c> one, for
/// every output shape, with Unicode symbols (the <c>style/</c> goldens) and with <c>--ascii</c> (in memory).
/// </summary>
[Trait("Law", "L011")]
[Collection(nameof(ToolchainEnvironment))]
public sealed partial class L011ColourAddsOnlyEscapesTests
{
    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex Sgr();

    private static string Strip(string text) => Sgr().Replace(text, "");

    [Fact]
    public void EveryColourGoldenStripsToItsPlainGolden()
    {
        var dir = Path.Combine(Golden.CasesRoot, "style");
        var goldens = Directory.GetFiles(dir, "*.color").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(StyleTests.Shapes.Length, goldens.Length);
        foreach (var color in goldens)
        {
            var text = File.ReadAllText(color).ReplaceLineEndings("\n");
            Assert.Contains("\u001b[", text, StringComparison.Ordinal);
            Agree(Path.GetFileNameWithoutExtension(color) + " (goldens)", Strip(text), File.ReadAllText(Path.ChangeExtension(color, ".plain")).ReplaceLineEndings("\n"));
        }
    }

    [Theory]
    [MemberData(nameof(StyleTests.AllShapes), MemberType = typeof(StyleTests))]
    public void EveryAsciiRenderingStripsToItsNoColorRendering(string shape)
    {
        var color = StyleTests.Render(shape, StyleTests.Rendering.Ascii);
        Assert.Contains("\u001b[", color, StringComparison.Ordinal);
        Agree(shape + " (--ascii)", Strip(color), StyleTests.Render(shape, StyleTests.Rendering.AsciiPlain));
    }

    private static void Agree(string shape, string stripped, string plain)
    {
        if (stripped == plain) return;
        var s = stripped.Split('\n');
        var p = plain.Split('\n');
        var line = 0;
        while (line < s.Length && line < p.Length && s[line] == p[line]) line++;
        Assert.Fail($"""
            L011 counterexample: {shape}, first difference at line {line + 1}
              colour, stripped: {(line < s.Length ? s[line] : "<end>")}
              NO_COLOR:         {(line < p.Length ? p[line] : "<end>")}
            """);
    }
}
