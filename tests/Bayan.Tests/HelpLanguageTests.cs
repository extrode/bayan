using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// R15 (guide §7): every help text starts with an imperative verb. Checked over the rendered
/// <c>help:</c> lines of the diag, manifest and lex goldens, and over the help literals in the sources.
/// </summary>
public sealed partial class HelpLanguageTests
{
    private static readonly HashSet<string> Verbs =
    [
        "add", "assign", "bind", "build", "call", "cast", "change", "check", "choose", "close", "collect",
        "combine", "convert", "copy", "create", "declare", "drop", "end", "explain", "finish", "give",
        "handle", "hand", "keep", "label", "leave", "let", "list", "loop", "make", "mark", "match", "move",
        "name", "pass", "pick", "propagate", "put", "read", "remove", "rename", "set", "store", "take",
        "split", "spread", "unwrap", "use", "wrap", "write",
    ];

    /// <summary>Helps that may start otherwise, each with its reason.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["did you mean `"] = "question",
    };

    [GeneratedRegex(@"^\s*help: (.*)$")]
    private static partial Regex HelpLine();

    [GeneratedRegex(@"(?i)\bhelp\s*[:=]\s*\$?""([a-z0-9`][^""]*)")]
    private static partial Regex HelpLiteral();

    private static bool StartsWithVerb(string help) =>
        Verbs.Contains(help.Split(' ', ',', ';', ':')[0]) || Allowed.Keys.Any(help.StartsWith);

    private static IEnumerable<(string File, string Help)> Find(string dir, string pattern, Regex regex) =>
        Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, dir), pattern, SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).SelectMany(l => regex.Matches(l))
                .Select(m => (Path.GetRelativePath(Golden.RepoRoot, f), m.Groups[1].Value)));

    [Fact]
    public void GoldenHelpsStartWithAVerb()
    {
        var helps = Find("tests/cases/diag", "*.diag", HelpLine())
            .Concat(Find("tests/cases/manifest", "*.diag", HelpLine()))
            .Concat(Find("tests/cases/lex", "*.tokens", HelpLine()))
            .ToList();
        Assert.NotEmpty(helps);
        var bad = helps.Where(h => !StartsWithVerb(h.Help)).Select(h => $"{h.File}: {h.Help}").Distinct().ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        Assert.All(Allowed.Keys, k => Assert.Contains(helps, h => h.Help.StartsWith(k)));
    }

    [Fact]
    public void SourceHelpsStartWithAVerb()
    {
        var helps = Find("src", "*.cs", HelpLiteral()).Concat(Find("selfhost", "*.byn", HelpLiteral())).ToList();
        Assert.NotEmpty(helps);
        var bad = helps.Where(h => !StartsWithVerb(h.Help)).Select(h => $"{h.File}: {h.Help}").ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
