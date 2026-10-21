using System.Globalization;
using System.Text.RegularExpressions;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>The §2 colour roles in <c>docs/bayan-terminal-style.md</c> meet WCAG AA and match <see cref="Palette.Hex"/>.</summary>
public sealed partial class ContrastTests
{
    private const string LightBackground = "#FFFFFF";
    private const string DarkBackground = "#14202E";

    [GeneratedRegex(@"^\|\s*(Error|Warning|Help|Success|Note)\b[^|]*\|\s*`(#[0-9A-Fa-f]{6})`[^|]*\|\s*`(#[0-9A-Fa-f]{6})`")]
    private static partial Regex RowPattern();

    private static List<(string Role, string Light, string Dark)> GuideTable()
    {
        var path = Path.Combine(Golden.RepoRoot, "docs", "bayan-terminal-style.md");
        return File.ReadLines(path)
            .Select(l => RowPattern().Match(l))
            .Where(m => m.Success)
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value.ToUpperInvariant()))
            .ToList();
    }

    private static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void GuideTableHasFiveRoles() =>
        Assert.Equal(["Error", "Warning", "Help", "Success", "Note"], GuideTable().Select(r => r.Role));

    [Fact]
    public void EveryGuideColourMeetsAa()
    {
        var failures = new List<string>();
        foreach (var (role, light, dark) in GuideTable())
        {
            var l = Contrast(light, LightBackground);
            var d = Contrast(dark, DarkBackground);
            if (l < 4.5) failures.Add($"{role} light {light} on {LightBackground}: {l:0.0}:1 < 4.5:1");
            if (d < 4.5) failures.Add($"{role} dark {dark} on {DarkBackground}: {d:0.0}:1 < 4.5:1");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact]
    public void PaletteHexMatchesTheGuide() => Assert.Equal(GuideTable(), Palette.Hex);

    [Fact]
    public void PaintWithoutColoursIsIdentity() =>
        Assert.Equal("boom", Style.Plain.Paint(Role.Error, "boom"));

    [Fact]
    public void PaintWithColoursWrapsInPrefixAndReset()
    {
        var style = new Style(Palette.Ansi16, Symbols.Ascii, 80);
        Assert.Equal("\e[31mboom\e[0m", style.Paint(Role.Error, "boom"));
        Assert.Equal("\e[2mx\e[0m", style.Paint(Role.Note, "x"));
    }

    [Fact]
    public void TruecolorPrefixesComeFromHex()
    {
        Assert.Equal("\e[38;2;180;35;24m", Palette.TruecolorLight.Error);
        Assert.Equal("\e[38;2;255;138;128m", Palette.TruecolorDark.Error);
    }
}
