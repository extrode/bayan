using System.Diagnostics;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// <c>#line</c> debug mapping (design round 3 §30, J93): the C of each statement follows <c>#line N "file.byn"</c>
/// naming its Bayan line; generated code after a function maps back to <c>main.c</c>.
/// </summary>
public sealed partial class LineDirectiveTests
{
    private const string Program = """
        i64 twice(i64 x) {
            let y = x * 2
            return y
        }

        void main() {
            var total = 0
            for i in 0..3 {
                total += twice(i)
            }
            print($"{total}")
        }

        """;

    private static string Emit(BuildMode mode)
    {
        var c = Compilation.Create([SourceText.FromString("main.byn", Program)], ".");
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        return CliApp.EmitC(c, mode);
    }

    [GeneratedRegex("^#line ([0-9]+) \"([^\"]*)\"$")]
    private static partial Regex Directive();

    [Theory]
    [InlineData(BuildMode.Debug)]
    [InlineData(BuildMode.Safe)]
    public void EachStatementFollowsItsBynLine(BuildMode mode)
    {
        var lines = Emit(mode).Split('\n');
        var mapped = lines.Select(l => Directive().Match(l)).Where(m => m.Success && m.Groups[2].Value == "main.byn")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        // A `for` returns to its head (line 8) for the increment; a line's later statements repeat its `#line`.
        Assert.Equal([2, 3, 7, 8, 9, 8, 11], mapped.Where((n, i) => i == 0 || n != mapped.ElementAt(i - 1)));
        var y = Array.IndexOf(lines, "#line 2 \"main.byn\"");
        Assert.Contains("byn_mul_i64(", lines[y + 1]);
    }

    [Fact]
    public void EveryStatementOfAOneLineIfElseMapsToItsLine()
    {
        const string OneLine = """
            void pick(i64 x) {
                if x > 2 { print("a") } else { print("b") }
            }

            void main() {
                pick(3)
            }

            """;
        var c = Compilation.Create([SourceText.FromString("main.byn", OneLine)], ".");
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var lines = CliApp.EmitC(c, BuildMode.Debug).Split('\n');
        var body = Array.FindIndex(lines, l => l.StartsWith("void byn_main__pick(", StringComparison.Ordinal) && l.EndsWith(" {", StringComparison.Ordinal));
        // The Bayan line each C line is counted as, from the last `#line` before it.
        var at = 0;
        var statements = new List<int>();
        for (var i = body + 1; lines[i] != "}"; i++)
        {
            if (Directive().Match(lines[i]) is { Success: true } m)
            {
                at = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            if (lines[i].EndsWith(';') || lines[i].TrimStart().StartsWith("if (", StringComparison.Ordinal)) statements.Add(at);
            at++;
        }
        Assert.Equal([2, 2, 2], statements);
    }

    [Fact]
    public void GeneratedCodeMapsToTheCFile()
    {
        var lines = Emit(BuildMode.Safe).Split('\n');
        // Helpers, types and prototypes come before any `#line`.
        var first = Array.FindIndex(lines, l => l.StartsWith("#line", StringComparison.Ordinal));
        Assert.Equal(first - 2, Array.FindIndex(lines, l => l.StartsWith("int64_t byn_main__twice(", StringComparison.Ordinal) && l.EndsWith(" {", StringComparison.Ordinal)));
        var resets = 0;
        for (var i = 0; i < lines.Length; i++)
            if (Directive().Match(lines[i]) is { Success: true } m && m.Groups[2].Value == "main.c")
            {
                // `#line N` names the line after it.
                Assert.Equal(i + 2, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal("}", lines[i - 1]);
                resets++;
            }
        Assert.Equal(2, resets);
        var main = Array.FindIndex(lines, l => l.StartsWith("int main(", StringComparison.Ordinal));
        Assert.EndsWith("\"main.c\"", lines.Take(main).Last(l => l.StartsWith("#line", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(BuildMode.Debug)]
    [InlineData(BuildMode.Safe)]
    public void TheProgramStillBuildsAndRuns(BuildMode mode)
    {
        var build = Toolchain.Build(Emit(mode), mode);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true, StandardOutputEncoding = new System.Text.UTF8Encoding(false), UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.Equal("6\n", stdout);
    }

    [Fact]
    public void ACCompilerErrorNamesTheBynLine()
    {
        var lines = Emit(BuildMode.Safe).Split('\n');
        // A fault in the C of `total += twice(i)` (line 9).
        var at = Array.IndexOf(lines, "#line 9 \"main.byn\"");
        lines[at + 1] = "        no_such_c_name;";
        var build = Toolchain.Build(string.Join('\n', lines), BuildMode.Safe);
        Assert.False(build.Succeeded);
        Assert.True(build.Rejected);
        Assert.Contains("main.byn:9:", build.Errors);
    }
}
