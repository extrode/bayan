using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

public sealed class DiagnosticCodesTests
{
    [Fact]
    public void CodesAreUniqueAndWellFormed()
    {
        var codes = DiagnosticCodes.All.Select(d => d.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^BYN[0-9]{4}$", c));
    }

    [Fact]
    public void EveryCodeHasAnEmbeddedExplanation()
    {
        foreach (var d in DiagnosticCodes.All)
        {
            var text = CliApp.ExplainText(d.Code);
            Assert.True(text is not null, $"{d.Code} has no docs/diagnostics/{d.Code}.md");
            Assert.StartsWith($"# {d.Code}:", text);
        }
    }

    [Fact]
    public void EveryDocBelongsToACode()
    {
        var known = DiagnosticCodes.All.Select(d => d.Code).ToHashSet();
        foreach (var file in Directory.GetFiles(Path.Combine(Golden.RepoRoot, "docs", "diagnostics"), "*.md"))
            Assert.Contains(Path.GetFileNameWithoutExtension(file), known);
    }

    [Fact]
    public void EveryCodeAppearsInAGoldenCase()
    {
        var expected = Directory.EnumerateFiles(Golden.CasesRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".byn", StringComparison.Ordinal) && !f.EndsWith(".md", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
        // A retired code keeps its descriptor and docs page but can no longer appear in output; a reserved one cannot yet.
        var missing = DiagnosticCodes.All
            .Where(d => !CliApp.ExplainText(d.Code)!.Contains("No longer reported", StringComparison.Ordinal))
            .Where(d => !CliApp.ExplainText(d.Code)!.Contains("Not reported yet", StringComparison.Ordinal))
            .Where(d => !expected.Any(text => text.Contains($" {d.Code}:", StringComparison.Ordinal)))
            .Select(d => d.Code)
            .ToList();
        Assert.True(missing.Count == 0, "no golden case for " + string.Join(", ", missing));
    }

    [Fact]
    public void NoLiveCodeReusesAReservedNumber()
    {
        var lines = File.ReadAllLines(Path.Combine(Golden.RepoRoot, "src", "Bayan.Syntax", "Diagnostics", "DiagnosticCodes.cs"))
            .Select(l => l.Trim()).ToList();
        var start = lines.FindIndex(l => l.StartsWith("// Reserved: round 4", StringComparison.Ordinal));
        var end = lines.FindIndex(l => l == "// End reserved: round 4.");
        Assert.True(start >= 0 && end > start, "no `// Reserved: round 4` block");
        var entries = lines.Skip(start + 1).Take(end - start - 1).Where(l => l.StartsWith("// BYN", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, l => Assert.Matches("^// BYN[0-9]{4} .*// target: \\S", l));
        var reserved = entries.Select(l => l[3..10]).ToList();
        Assert.Equal(reserved.Count, reserved.Distinct().Count());
        Assert.Empty(DiagnosticCodes.All.Select(d => d.Code).Intersect(reserved));
    }

    // Docs whose example cannot be compiled standalone, each with the reason.
    private static readonly Dictionary<string, string> UncompilableExamples = new()
    {
        ["BYN0001"] = "needs a literal bidi control character, which the doc spells as <U+202E>",
        ["BYN0005"] = "needs a decomposed character that a doc code block does not preserve",
        ["BYN0015"] = "needs invalid UTF-8 bytes; a doc code block is text",
        ["BYN0016"] = "needs 17 nested interpolated strings; the doc has no failing example",
        ["BYN1030"] = "retired: the C# primitive spellings are accepted now",
        ["BYN1032"] = "retired: `if !x` is accepted",
        ["BYN1049"] = "retired: merged into BYN1007",
        ["BYN2005"] = "multi-file: needs a `money` module",
        ["BYN2006"] = "stale: the example now reports BYN5004 and BYN5006",
        ["BYN2010"] = "multi-file: two modules importing each other",
        ["BYN2011"] = "circular alias example reports no diagnostic standalone",
        ["BYN2015"] = "multi-file: the module's file is imported elsewhere",
        ["BYN2026"] = "retired: the example now reports BYN1017 and BYN1018",
        ["BYN2033"] = "multi-file: needs a std module, and no shipped one imports a package module",
        ["BYN2113"] = "superseded by BYN2761",
        ["BYN2116"] = "not reported yet: operator declarations are spec-only",
        ["BYN2224"] = "no longer reported since M14",
        ["BYN2307"] = "retired code",
        ["BYN2322"] = "retired: `requires` is checked at construction",
        ["BYN2707"] = "no longer reported since M21: now BYN2761",
        ["BYN2762"] = "retired: replaced by BYN2780",
        ["BYN2787"] = "the `trust(x, reason:)` form is now BYN1062",
        ["BYN3100"] = "project-level: the example is a package.byn manifest",
        ["BYN3101"] = "project-level: the example is a package.byn manifest",
        ["BYN3102"] = "project-level: the example is a package.byn manifest",
        ["BYN3103"] = "project-level: the example is a package.byn manifest",
        ["BYN3104"] = "project-level: the example is a package.byn manifest",
        ["BYN3201"] = "a stray `-2` line is BYN1012 now; BYN3201 needs an unused call value",
        ["BYN3213"] = "not reported yet: reserved code with no example",
        ["BYN3215"] = "not reported yet: reserved code with no example",
        ["BYN3217"] = "not reported yet: reserved code with no example",
        ["BYN4001"] = "backend error from the C compiler, not reachable from source",
        ["BYN6004"] = "project-level: needs a package.byn and a second file",
    };

    // Skipped docs whose example is not compilable text, or that have no example block (the rest must still fail to report their own code).
    private static readonly HashSet<string> StructuralExamples =
    [
        "BYN0001", "BYN0005", "BYN0015", "BYN0016", "BYN2005", "BYN2010", "BYN2015", "BYN2033",
        "BYN3100", "BYN3101", "BYN3102", "BYN3103", "BYN3104", "BYN3213", "BYN3215", "BYN3217", "BYN4001", "BYN6004",
    ];

    // Error codes an example may report besides its own ("doc/extra"), each with the reason.
    private static readonly Dictionary<string, string> AllowedExtras = new()
    {
        ["BYN1066/BYN1016"] = "no `extern` form parses yet (BYN1016; logged in work/todo.md), so the pointer-spelling example cannot be valid otherwise",
        ["BYN5004/BYN5006"] = "BYN5004 is reported with BYN5006 for each parameter besides the object",
        ["BYN5006/BYN5004"] = "BYN5006 is reported with BYN5004 for the same function",
    };

    // A doc example is a library snippet; a missing `main` is not what the doc is about.
    private const string NoEntryPoint = "BYN2014";

    // Docs whose Fix block is a complete program. The other docs' fix blocks are fragments (76 of 116, over 25%),
    // so only the docs fixed in the review of the example rewrites are checked.
    private static readonly string[] CompleteFixBlocks = ["BYN2027", "BYN2235", "BYN2236", "BYN2320", "BYN2321", "BYN2612"];

    private static readonly string DocsDir = Path.Combine(Golden.RepoRoot, "docs", "diagnostics");

    // The fenced blocks of the paragraph starting with `heading`, up to the next `**` paragraph.
    private static List<string> BlocksAfter(string file, string heading)
    {
        var lines = File.ReadAllLines(file);
        var blocks = new List<string>();
        var at = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        if (at < 0) return blocks;
        for (var i = at + 1; i < lines.Length && !lines[i].StartsWith("**", StringComparison.Ordinal); i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal)) continue;
            var close = Array.FindIndex(lines, i + 1, l => l.StartsWith("```", StringComparison.Ordinal));
            if (close < 0) break;
            blocks.Add(string.Join("\n", lines[(i + 1)..close]) + "\n");
            i = close;
        }
        return blocks;
    }

    // The first fenced block of the paragraph starting with `heading`, or null.
    private static string? BlockAfter(string file, string heading) => BlocksAfter(file, heading).FirstOrDefault();

    private static List<Bayan.Syntax.Diagnostics.Diagnostic> Compile(string code, string source) =>
        Compilation.Create([new Bayan.Syntax.Text.SourceText(code + ".byn", System.Text.Encoding.UTF8.GetBytes(source))], Golden.RepoRoot, new CompilationOptions())
            .Diagnostics.Items.ToList();

    [Fact]
    public void SkipListNamesExistingDocsAndStillNeedsIt()
    {
        var failures = new List<string>();
        foreach (var code in UncompilableExamples.Keys.Concat(StructuralExamples))
            if (!File.Exists(Path.Combine(DocsDir, code + ".md"))) failures.Add($"{code}: skip entry names no doc");
        foreach (var code in StructuralExamples)
            if (!UncompilableExamples.ContainsKey(code)) failures.Add($"{code}: structural entry is not in the skip list");
        foreach (var code in UncompilableExamples.Keys)
        {
            if (StructuralExamples.Contains(code) || !File.Exists(Path.Combine(DocsDir, code + ".md"))) continue;
            var source = BlockAfter(Path.Combine(DocsDir, code + ".md"), "**Example");
            if (source is null) failures.Add($"{code}: the doc has no Bayan example block; add it to StructuralExamples");
            else if (Compile(code, source).Any(d => d.Code == code))
                failures.Add($"{code}: the example now reports its own code; remove the skip entry");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void AllowedExtrasNameLiveDocsAndStillNeedThem()
    {
        var failures = new List<string>();
        foreach (var key in AllowedExtras.Keys)
        {
            var (code, extra) = (key[..7], key[8..]);
            var file = Path.Combine(DocsDir, code + ".md");
            if (!File.Exists(file)) { failures.Add($"{key}: names no doc; remove the entry"); continue; }
            if (UncompilableExamples.ContainsKey(code)) { failures.Add($"{key}: the doc is in the skip list; remove the entry"); continue; }
            var source = BlockAfter(file, "**Example");
            if (source is null || !Compile(code, source).Any(d => d.Code == extra))
                failures.Add($"{key}: the example no longer reports {extra}; remove the entry");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryDocExampleReportsItsOwnCode()
    {
        var docs = Directory.GetFiles(DocsDir, "*.md");
        Assert.True(UncompilableExamples.Count * 4 < docs.Length, "the skip list is over 25% of the docs");
        var failures = new List<string>();
        foreach (var file in docs)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            if (UncompilableExamples.ContainsKey(code)) continue;
            var sources = BlocksAfter(file, "**Example");
            if (sources.Count == 0) { failures.Add($"{code}: no example block"); continue; }
            for (var b = 0; b < sources.Count; b++)
            {
                var at = sources.Count == 1 ? code : $"{code} block {b + 1}";
                var diagnostics = Compile(code, sources[b]);
                var reported = diagnostics.Select(d => d.Code).Distinct().ToList();
                var extra = diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct()
                    .Where(c => c != code && c != NoEntryPoint && !AllowedExtras.ContainsKey($"{code}/{c}")).ToList();
                if (!reported.Contains(code)) failures.Add($"{at}: reported [{string.Join(", ", reported)}]");
                else if (extra.Count > 0) failures.Add($"{at}: also reports [{string.Join(", ", extra)}]");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void CompleteFixBlocksCompileClean()
    {
        var failures = new List<string>();
        foreach (var code in CompleteFixBlocks)
        {
            var sources = BlocksAfter(Path.Combine(DocsDir, code + ".md"), "**Fix");
            if (sources.Count == 0) { failures.Add($"{code}: no fix block"); continue; }
            for (var b = 0; b < sources.Count; b++)
            {
                var errors = Compile(code, sources[b]).Where(d => d.IsError && d.Code != NoEntryPoint).Select(d => d.Code).Distinct().ToList();
                if (errors.Count > 0) failures.Add($"{code}: fix block {b + 1} reports [{string.Join(", ", errors)}]");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void PreconditionDocsNameRequiresAndExpectsAndExample612AgreesWithItsFix()
    {
        foreach (var code in new[] { "BYN2320", "BYN2321" })
        {
            var text = File.ReadAllText(Path.Combine(DocsDir, code + ".md"));
            Assert.Contains("requires", text);
            Assert.Contains("expects", text);
        }
        var example = BlockAfter(Path.Combine(DocsDir, "BYN2612.md"), "**Example")!;
        var fix = BlockAfter(Path.Combine(DocsDir, "BYN2612.md"), "**Fix")!;
        Assert.Contains("db.name", fix);
        Assert.Contains("string name", example);
    }

    [Fact]
    public void NoDocBayanBlockEndsALineWithASemicolon()
    {
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(DocsDir, "*.md"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var inBlock = false;
            var bayan = false;
            var skipping = false;
            var seenExample = false;
            var number = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                number++;
                if (!inBlock && line.StartsWith("**Example", StringComparison.Ordinal)) seenExample = true;
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inBlock = !inBlock;
                    var fence = line.TrimEnd();
                    bayan = inBlock && fence is "```" or "```bayan" or "```byn";
                    // BYN1040 shows `;` on purpose in its first Example block; its fix blocks are checked.
                    if (inBlock) skipping = code == "BYN1040" && seenExample;
                    else if (skipping) { skipping = false; seenExample = false; }
                }
                else if (inBlock && bayan && !skipping && System.Text.RegularExpressions.Regex.IsMatch(line, @";\s*(\}|$)"))
                    found.Add($"{code}:{number}");
            }
        }
        Assert.True(found.Count == 0, "`;` ends a statement in a Bayan block: " + string.Join(", ", found));
    }
}
