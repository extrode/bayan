using System.Text;
using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// K2 (spec 011 R4): every compiler extension the emitted C relies on is a row of <c>011-c-inventory.md</c>, and every row has a hit.
/// The runtime is scanned as C; <c>CEmitter.cs</c> and <c>cprint/*.byn</c> only over their string literals, holes removed.
/// </summary>
public sealed partial class CInventoryTests
{
    private const string InventoryPath = "docs/specs/011-self-hosted-emitter/011-c-inventory.md";

    private static readonly string[] CFiles = ["src/runtime/bayan-rt.h", "src/runtime/bayan-rt.c"];

    private static readonly string[] LiteralFiles = ["src/Bayan.Backend.C/CEmitter.cs"];

    [GeneratedRegex(@"__builtin_\w+|__attribute__\s*\(\(([^()]|\([^()]*\))*\)\)|__declspec|\balloca\b|__typeof__|\btypeof\b|__auto_type|__thread\b|__int128|__extension__|__restrict\w*|#pragma \w+|_Pragma|__asm__|\basm\b|__has_builtin|__PRETTY_FUNCTION__|__COUNTER__|\(\{")]
    private static partial Regex Extensions();

    [GeneratedRegex(@"^\| (?<site>[^ |]+:\d+) \| `(?<token>[^`]+)` \| (?<why>.+) \|$")]
    private static partial Regex Row();

    [Fact]
    public void EveryHitIsARowAndEveryRowHasAHit()
    {
        var hits = Hits().Order(StringComparer.Ordinal).ToList();
        var rows = Rows().Order(StringComparer.Ordinal).ToList();
        var noRow = hits.Except(rows).ToList();
        var noHit = rows.Except(hits).ToList();
        Assert.True(noRow.Count == 0 && noHit.Count == 0,
            $"hits without a row:\n{string.Join("\n", noRow)}\nrows without a hit:\n{string.Join("\n", noHit)}");
        Assert.Equal(hits, rows);
    }

    private static IEnumerable<string> Hits()
    {
        var files = CFiles.Select(f => (File: f, Text: Read(f)))
            .Concat(LiteralFiles.Select(f => (File: f, Text: Literals(Read(f)))));
        var cprint = Path.Combine(Golden.RepoRoot, "selfhost", "emitter", "cprint");
        if (Directory.Exists(cprint))
        {
            files = files.Concat(Directory.GetFiles(cprint, "*.byn").Order(StringComparer.Ordinal)
                .Select(p => (File: Path.GetRelativePath(Golden.RepoRoot, p).Replace('\\', '/'), Text: Literals(File.ReadAllText(p)))));
        }
        foreach (var (file, text) in files)
        {
            foreach (Match m in Extensions().Matches(text))
            {
                var line = text.AsSpan(0, m.Index).Count('\n') + 1;
                foreach (var token in Split(m.Value)) yield return $"{file}:{line} {token}";
            }
        }
    }

    private static IEnumerable<string> Rows()
    {
        foreach (var line in Read(InventoryPath).ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| Site ", StringComparison.Ordinal)) continue;
            var m = Row().Match(line);
            Assert.True(m.Success, $"inventory table line is not a row: {line}");
            yield return $"{m.Groups["site"].Value} {m.Groups["token"].Value}";
        }
    }

    private static string Read(string repoPath) => File.ReadAllText(Path.Combine(Golden.RepoRoot, repoPath));

    /// <summary>One token per match; an <c>__attribute__((a, b))</c> list gives <c>__attribute__((a))</c> and <c>__attribute__((b))</c>.</summary>
    public static IEnumerable<string> Split(string match)
    {
        if (!match.StartsWith("__attribute__", StringComparison.Ordinal))
        {
            yield return match;
            yield break;
        }
        var open = match.IndexOf("((", StringComparison.Ordinal);
        var list = match[(open + 2)..^2];
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= list.Length; i++)
        {
            if (i < list.Length && list[i] == '(') depth++;
            else if (i < list.Length && list[i] == ')') depth--;
            else if (i == list.Length || (list[i] == ',' && depth == 0))
            {
                yield return $"__attribute__(({list[start..i].Trim()}))";
                start = i + 1;
            }
        }
    }

    /// <summary>
    /// The text of <paramref name="source"/> with everything but string-literal content blanked, same length and line breaks.
    /// Interpolation holes are blanked, <c>{{</c> and <c>}}</c> in a <c>$"…"</c> read as one brace; comments and char literals are skipped.
    /// Handles plain, verbatim (<c>@"</c>), interpolated (<c>$</c>, <c>$$</c>) and raw (<c>"""</c>) literals.
    /// </summary>
    public static string Literals(string source)
    {
        var s = source;
        var o = new StringBuilder(new string(' ', s.Length));
        for (var i = 0; i < s.Length; i++) if (s[i] == '\n') o[i] = '\n';

        var p = 0;
        while (p < s.Length)
        {
            if (s[p] == '/' && p + 1 < s.Length && s[p + 1] == '/')
            {
                while (p < s.Length && s[p] != '\n') p++;
            }
            else if (s[p] == '/' && p + 1 < s.Length && s[p + 1] == '*')
            {
                var end = s.IndexOf("*/", p + 2, StringComparison.Ordinal);
                p = end < 0 ? s.Length : end + 2;
            }
            else if (s[p] == '\'')
            {
                p++;
                while (p < s.Length && s[p] != '\'' && s[p] != '\n') p += s[p] == '\\' ? 2 : 1;
                p++;
            }
            else if (s[p] is '$' or '@' or '"')
            {
                var dollars = 0;
                var verbatim = false;
                while (p < s.Length && s[p] is '$' or '@')
                {
                    if (s[p] == '$') dollars++;
                    else verbatim = true;
                    p++;
                }
                if (p >= s.Length || s[p] != '"') continue;
                var quotes = 0;
                while (p < s.Length && s[p] == '"') { quotes++; p++; }
                if (quotes == 2) continue; // the empty string
                p = quotes >= 3 ? Raw(s, o, p, quotes, dollars) : Quoted(s, o, p, dollars > 0, verbatim);
            }
            else p++;
        }
        return o.ToString();
    }

    /// <summary>Reads a plain or verbatim literal body starting after the opening quote; returns the index after the closing quote.</summary>
    private static int Quoted(string s, StringBuilder o, int p, bool interpolated, bool verbatim)
    {
        while (p < s.Length)
        {
            var c = s[p];
            if (c == '"')
            {
                if (verbatim && p + 1 < s.Length && s[p + 1] == '"') { o[p] = '"'; o[p + 1] = '"'; p += 2; continue; }
                return p + 1;
            }
            if (c == '\\' && !verbatim)
            {
                o[p] = c;
                if (p + 1 < s.Length) o[p + 1] = s[p + 1] == '\n' ? '\n' : s[p + 1];
                p += 2;
            }
            else if (interpolated && c == '{' && p + 1 < s.Length && s[p + 1] == '{') { o[p] = '{'; p += 2; }
            else if (interpolated && c == '}' && p + 1 < s.Length && s[p + 1] == '}') { o[p + 1] = '}'; p += 2; }
            else if (interpolated && c == '{') p = Hole(s, p + 1, 1);
            else { if (c != '\n') o[p] = c; p++; }
        }
        return p;
    }

    /// <summary>Reads a raw literal body; a hole opens at <paramref name="dollars"/> braces. Returns the index after the closing quotes.</summary>
    private static int Raw(string s, StringBuilder o, int p, int quotes, int dollars)
    {
        var close = new string('"', quotes);
        while (p < s.Length)
        {
            if (string.CompareOrdinal(s, p, close, 0, quotes) == 0) return p + quotes;
            if (dollars > 0 && s[p] == '{' && Run(s, p, '{') >= dollars)
            {
                var extra = Run(s, p, '{') - dollars;
                for (var k = 0; k < extra; k++) o[p + k] = '{';
                p = Hole(s, p + extra + dollars, dollars);
            }
            else { if (s[p] != '\n') o[p] = s[p]; p++; }
        }
        return p;
    }

    private static int Run(string s, int p, char c)
    {
        var n = 0;
        while (p + n < s.Length && s[p + n] == c) n++;
        return n;
    }

    /// <summary>Skips an interpolation hole whose opening braces are already consumed; returns the index after its closing braces.</summary>
    private static int Hole(string s, int p, int braces)
    {
        var depth = 1;
        while (p < s.Length && depth > 0)
        {
            if (s[p] == '{') depth++;
            else if (s[p] == '}') depth--;
            else if (s[p] == '"')
            {
                p++;
                while (p < s.Length && s[p] != '"') p += s[p] == '\\' ? 2 : 1;
            }
            p++;
        }
        return Math.Min(p + braces - 1, s.Length);
    }

    [Fact]
    public void PlainLiteralKeepsItsText()
    {
        Assert.Contains("__builtin_x", Literals("f(\"__builtin_x\"); // \"__builtin_y\""));
        Assert.DoesNotContain("__builtin_y", Literals("f(\"__builtin_x\"); // \"__builtin_y\""));
    }

    [Fact]
    public void CodeOutsideLiteralsIsBlanked()
    {
        Assert.DoesNotContain("typeof", Literals("var t = typeof(int); /* \"asm\" */ var c = '\"'; // asm"));
        Assert.DoesNotContain("asm", Literals("var t = typeof(int); /* \"asm\" */ var c = '\"'; // asm"));
    }

    [Fact]
    public void VerbatimLiteralEndsAtASingleQuote()
    {
        var r = Literals("@\"a \"\"b\"\" __asm__\" asm");
        Assert.Contains("__asm__", r);
        Assert.DoesNotContain("asm\"", r.TrimEnd());
        Assert.Equal(' ', r[^1]);
    }

    [Fact]
    public void InterpolationHolesAreRemoved()
    {
        var r = Literals("$\"({f(\"typeof\", x)}) __asm__\"");
        Assert.DoesNotContain("typeof", r);
        Assert.Contains("__asm__", r);
        Assert.DoesNotContain("({", r);
    }

    [Fact]
    public void DoubledBracesAreOneBrace()
    {
        var r = Literals("$\"({T}){{a}} ({{ b\"");
        Assert.Single(Extensions().Matches(r));
    }

    private static string Collapsed(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    [Fact]
    public void StatementExpressionInAnInterpolatedLiteralIsFound()
    {
        var r = Literals("$\"({{ int t = {x}; t; }})\"");
        Assert.Equal("({ int t = ; t; })", Collapsed(r));
        Assert.Single(Extensions().Matches(r));
    }

    [Fact]
    public void OnlyRealHolesAreRemoved()
    {
        Assert.Equal("a b c", Collapsed(Literals("$\"a {x:X2} b {f(new { A = 1 }, \"}\")} c\"")));
        Assert.Equal("{ a }", Collapsed(Literals("$\"{{ a }}\"")));
    }

    [Fact]
    public void PlainLiteralWithAnEscapedQuoteStaysOneLiteral()
    {
        var r = Literals("f(\"a \\\" __asm__ \"); asm");
        Assert.Contains("__asm__", r);
        Assert.DoesNotContain("); asm", r);
        Assert.Single(Extensions().Matches(r));
    }

    [Fact]
    public void RawLiteralWithDollarsUsesDoubleBraceHoles()
    {
        var r = Literals("$$\"\"\"\n  a({ {{x.ToString(\"asm\")}} typeof\n  \"\"\" asm");
        Assert.Contains("a({ ", r);
        Assert.Contains("typeof", r);
        Assert.DoesNotContain("asm", r);
        Assert.Equal(2, r.Count(c => c == '\n'));
    }

    [Fact]
    public void LineBreaksArePreserved()
    {
        var src = "a\n\"b\"\n$\"c{x}\n\"";
        Assert.Equal(src.Count(c => c == '\n'), Literals(src).Count(c => c == '\n'));
        Assert.Equal(src.Length, Literals(src).Length);
    }

    [Fact]
    public void AttributeListSplitsOnCommas()
    {
        Assert.Equal(["__attribute__((noreturn))", "__attribute__((cold))"], Split("__attribute__((noreturn, cold))"));
        Assert.Equal(["__attribute__((aligned(8)))", "__attribute__((packed))"], Split("__attribute__ ((aligned(8), packed))"));
        Assert.Equal(["__builtin_expect"], Split("__builtin_expect"));
    }
}
