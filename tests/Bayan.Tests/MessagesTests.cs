using System.Text;
using System.Text.RegularExpressions;
using Bayan.Cli;

namespace Bayan.Tests;

public sealed class MessagesTests
{
    /// <summary>String literals in the scanned sources that hold a letter and a space but are not shown to people.</summary>
    private static readonly HashSet<string> NotProse = new(StringComparer.Ordinal)
    {
        "bayan ", // BUILDINFO.json `compiler` value
    };

    /// <summary>Command lines passed as code-snippet arguments (`bayan test`, `git init`, usage synopses).</summary>
    private static readonly Regex CommandSnippet = new("^(bayan|git) [a-z]", RegexOptions.CultureInvariant);

    private static readonly string[] Sources = ["CliApp.cs", "Doctor.cs", "Dist.cs"];

    [Fact]
    public void IdsAreUniqueAndDotted()
    {
        var ids = Messages.All.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[a-z]+(\\.[a-z-]+)+$", id));
        Assert.Same(Messages.UnknownCommand, Messages.Get("usage.unknown-command"));
    }

    [Fact]
    public void PlaceholdersAreConsecutiveFromZero()
    {
        foreach (var m in Messages.All)
        {
            var used = Regex.Matches(m.Format.Replace("{{", "").Replace("}}", ""), "\\{(\\d+)(?:[,:][^}]*)?\\}")
                .Select(x => int.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .Order()
                .ToList();
            Assert.True(used.SequenceEqual(Enumerable.Range(0, used.Count)), $"{m.Id}: placeholders {string.Join(", ", used)} are not 0..{used.Count - 1}");
        }
    }

    [Fact]
    public void CliSourcesHaveNoProseLiterals()
    {
        var prose = new List<string>();
        foreach (var name in Sources)
        {
            var text = File.ReadAllText(Path.Combine(Golden.RepoRoot, "src", "Bayan.Cli", name));
            foreach (var literal in Literals(text))
                if (literal.Any(char.IsLetter) && literal.Contains(' ') && !NotProse.Contains(literal) && !CommandSnippet.IsMatch(literal))
                    prose.Add($"{name}: \"{literal}\"");
        }
        Assert.True(prose.Count == 0, "move to Messages:\n" + string.Join("\n", prose));
    }

    [Fact]
    public void ScannerFindsLiteralsOfEveryKind()
    {
        const string source = """"
            var a = "plain text"; // "comment text"
            var b = $"hole {x switch { 1 => "nested text", _ => "" }} after text";
            var c = @"verbatim ""text""";
            var d = '"'; var e = "after char";
            /* "block text" */
            """";
        Assert.Equal(["plain text", "nested text", "", "hole  after text", "verbatim \"text\"", "after char"], Literals(source));
    }

    /// <summary>
    /// The text of every string literal in C# <paramref name="s"/>, comments skipped. An interpolated
    /// string's holes are dropped from its text and their own literals listed before it; escapes are dropped.
    /// </summary>
    private static List<string> Literals(string s)
    {
        var found = new List<string>();
        var i = 0;
        Scan(s, ref i, found, inHole: false);
        return found;
    }

    private static void Scan(string s, ref int i, List<string> found, bool inHole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i = s.IndexOf("*/", i + 2, StringComparison.Ordinal) + 2;
            }
            else if (c == '\'')
            {
                i += s[i + 1] == '\\' ? 4 : 3;
            }
            else if (c == '"' || (c is '$' or '@' && i + 1 < s.Length && s[i + 1] is '"' or '$' or '@'))
            {
                found.Add(ReadString(s, ref i, found));
            }
            else if (inHole && c == '{')
            {
                depth++;
                i++;
            }
            else if (inHole && c == '}')
            {
                i++;
                if (depth-- == 0) return;
            }
            else
            {
                i++;
            }
        }
    }

    private static string ReadString(string s, ref int i, List<string> found)
    {
        bool interpolated = false, verbatim = false;
        for (; s[i] != '"'; i++)
        {
            if (s[i] == '$') interpolated = true;
            if (s[i] == '@') verbatim = true;
        }
        var text = new StringBuilder();
        var nested = new List<string>();
        if (s.AsSpan(i).StartsWith("\"\"\""))
        {
            var end = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
            text.Append(s, i + 3, end - i - 3);
            i = end + 3;
        }
        else
        {
            i++;
            while (true)
            {
                var c = s[i];
                if (c == '"' && verbatim && s[i + 1] == '"')
                {
                    text.Append('"');
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    break;
                }
                else if (c == '\\' && !verbatim)
                {
                    i += 2;
                }
                else if (interpolated && c is '{' or '}' && s[i + 1] == c)
                {
                    text.Append(c);
                    i += 2;
                }
                else if (interpolated && c == '{')
                {
                    i++;
                    Scan(s, ref i, nested, inHole: true);
                }
                else
                {
                    text.Append(c);
                    i++;
                }
            }
        }
        found.AddRange(nested);
        return text.ToString();
    }
}
