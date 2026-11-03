using System.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 D9: <c>selfhost/emitter/cprint/</c> is the only module with C text in its string literals. Every other
/// <c>.byn</c> under <c>selfhost/emitter/</c> is scanned for a literal containing one of <see cref="Forbidden"/>.
/// </summary>
public sealed class SelfhostEmitterPrinterBoundaryTests
{
    private static readonly string[] Forbidden =
    [
        "#include", "#line", "#define", "typedef", "int64_t", "uint8_t", "byn_", "BYN_", "bynt_", "(void)", "->", "goto ",
        "static ", ";\n", "{\n",
    ];

    [Fact]
    public void OnlyThePrinterWritesCText()
    {
        var cprint = Path.Combine(SelfhostEmitterHarness.EmitterDir, "cprint") + Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(SelfhostEmitterHarness.EmitterDir, "*.byn", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(cprint, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var literal in Literals(lines[i]))
                    foreach (var word in Forbidden)
                        if (Unescape(literal).Contains(word, StringComparison.Ordinal))
                            hits.Add($"{Path.GetRelativePath(SelfhostEmitterHarness.EmitterDir, file)}:{i + 1}: \"{literal}\" has `{word.Replace("\n", "\\n")}`");
        }
        Assert.True(hits.Count == 0, $"C text outside selfhost/emitter/cprint:\n{string.Join('\n', hits)}");
    }

    /// <summary>The scan itself catches a literal on one line, so the real test goes red on a one-line break.</summary>
    [Theory]
    [InlineData("let s = \"#include <stdio.h>\\n\"")]
    [InlineData("sb.append($\"int64_t x{n};\\n\")")]
    [InlineData("return \"goto \" + name")]
    [InlineData("x = \"a->b\"")]
    public void ScanFlagsCText(string line) =>
        Assert.Contains(Literals(line), literal => Forbidden.Any(w => Unescape(literal).Contains(w, StringComparison.Ordinal)));

    [Theory]
    [InlineData("let s = \"L\" + n // \"#include\"")]
    [InlineData("/// `goto ` and \"static \" in a comment")]
    [InlineData("return \"unmatched switch\"")]
    public void ScanIgnoresCommentsAndPlainText(string line) =>
        Assert.DoesNotContain(Literals(line), literal => Forbidden.Any(w => Unescape(literal).Contains(w, StringComparison.Ordinal)));

    private static string Unescape(string literal) => literal.Replace("\\n", "\n");

    /// <summary>The contents of the string literals on <paramref name="line"/>, outside a trailing <c>//</c> comment.</summary>
    private static List<string> Literals(string line)
    {
        var result = new List<string>();
        StringBuilder? current = null;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (current is null)
            {
                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (ch == '"') current = new StringBuilder();
            }
            else if (ch == '\\' && i + 1 < line.Length) current.Append(ch).Append(line[++i]);
            else if (ch == '"') { result.Add(current.ToString()); current = null; }
            else current.Append(ch);
        }
        return result;
    }
}
