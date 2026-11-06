using System.Diagnostics;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L022 (binder equivalence): for every program that parses without errors, <c>bayan-check --types</c>
/// prints what <c>bayan types</c> prints, and <c>bayan-check</c> reports what <c>bayan check</c> reports, with
/// the same exit code. The corpora are the diag cases that parse clean, the typed cases, and the generator's
/// valid and faulty seed programs, each copied alone to <c>tmp/l022/&lt;corpus&gt;/&lt;NNNNN&gt;/</c> (a check
/// reads the whole directory). The expected side is <c>CliApp.Run</c> in-process; the self-hosted side is one
/// POSIX shell per corpus that runs both modes of every input.
/// </summary>
public sealed class SelfhostSemanticsEquivalenceLawTests(ITestOutputHelper output)
{
    private static readonly string[] Modes = ["check", "types"];

    private static string BayanCheck =>
        SelfhostSemanticsHarness.Build(File.ReadAllText(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, "main.byn")));

    /// <summary>The measured domain: diag cases that parse clean, and those outside L022 (Q658).</summary>
    private const int PinnedClean = 483;

    private const int PinnedExcluded = 90;

    private static string Root => Path.Combine(Golden.RepoRoot, "tmp", "l022");

    /// <summary>Law L022: every clean-parsing program checks, dumps and exits like the C# checker.</summary>
    [Fact]
    [Trait("Law", "L022")]
    public async Task BayanCheckEqualsTheCCheckerOnEveryCleanParse()
    {
        var clock = Stopwatch.StartNew();
        var diag = Write("diag", SelfhostSemanticsParityTests.DiagCases.Cast<object[]>().Select(row => Case((string)row[0])).ToList());
        var clean = diag.Where(p => ParsesClean(p.Path)).ToList();
        var excluded = diag.Except(clean).ToList();
        output.WriteLine($"diag: {clean.Count} of {diag.Count} parse without errors; the rest are outside L022: " +
            string.Join(" ", excluded.Select(p => p.Label)));
        // Per parse error code, the number of excluded cases holding it (a case with two codes counts under both).
        output.WriteLine("diag excluded by parse error code: " + string.Join(", ", excluded
            .SelectMany(p => ParseErrors(p.Path).Distinct())
            .GroupBy(code => code).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")));
        output.WriteLine("diag excluded by code family: " + string.Join(", ", excluded
            .SelectMany(p => ParseErrors(p.Path).Select(code => code[..4] + "xxx").Distinct())
            .GroupBy(family => family).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"))
            + $"; with no BYN1xxx code {excluded.Count(p => !ParseErrors(p.Path).Any(code => code.StartsWith("BYN1", StringComparison.Ordinal)))}");
        output.WriteLine("diag excluded outside BYN1xxx: " + string.Join(", ", excluded
            .Select(p => (p.Label, Codes: ParseErrors(p.Path).Distinct().Where(code => !code.StartsWith("BYN1", StringComparison.Ordinal)).ToList()))
            .Where(p => p.Codes.Count > 0).Select(p => $"{p.Label} {string.Join("/", p.Codes)}")));
        // The domain is pinned: a parser or corpus change that moves a case in or out fails here (Q658).
        Assert.True(clean.Count == PinnedClean && excluded.Count == PinnedExcluded,
            $"{clean.Count} diag cases parse clean and {excluded.Count} do not, pinned {PinnedClean} and {PinnedExcluded} (Q658)");
        await CheckCorpus("diag", clean);

        var typed = Write("typed", SelfhostSemanticsParityTests.TypedCases.Cast<object[]>().Select(row => Case((string)row[0])).ToList());
        Assert.All(typed, p => Assert.True(ParsesClean(p.Path), $"{p.Label} does not parse clean"));
        await CheckCorpus("typed", typed);

        var programs = SelfhostSemanticsGeneratorTests.Corpus(SelfhostSemanticsGenerator.Seeds);
        Assert.Equal(programs.Length, programs.Select(p => p.Source).Distinct().Count());
        Assert.Contains(programs, p => p.Fault is null);
        Assert.All(Enum.GetValues<SelfhostSemanticsGenerator.FaultKind>(), k => Assert.Contains(programs, p => p.Fault == k));
        var generated = Write("generated", programs.Select(p => (p.Name, new[] { ("main.byn", p.Source) }, "main.byn")).ToList());
        Assert.All(generated, p => Assert.True(ParsesClean(p.Path), $"{p.Label} does not parse clean"));
        await CheckCorpus("generated", generated);
        output.WriteLine($"L022 in {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>A case relative to <c>tests/cases/</c>: the file alone, or a directory case's whole directory.</summary>
    private static (string Label, (string Path, string Text)[] Files, string Entry) Case(string name)
    {
        var source = Path.Combine(Golden.CasesRoot, name);
        var caseDir = Path.GetDirectoryName(source)!;
        var kind = Path.GetFileName(Path.GetDirectoryName(name)!);
        if (kind is "diag" or "typed") return (name, [(Path.GetFileName(source), File.ReadAllText(source))], Path.GetFileName(source));
        var files = Directory.EnumerateFiles(caseDir, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(caseDir, f), File.ReadAllText(f)))
            .ToArray();
        return (name, files, Path.GetFileName(source));
    }

    /// <summary>Writes each program to <c>tmp/l022/&lt;corpus&gt;/&lt;NNNNN&gt;/</c> and returns its label and entry path.</summary>
    private static List<(string Label, string Path)> Write(string corpus,
        IReadOnlyList<(string Label, (string Path, string Text)[] Files, string Entry)> programs)
    {
        var dir = Path.Combine(Root, corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var written = new List<(string, string)>();
        for (var i = 0; i < programs.Count; i++)
        {
            var programDir = Path.Combine(dir, $"{i:D5}");
            foreach (var (path, text) in programs[i].Files)
            {
                var target = Path.Combine(programDir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }
            written.Add((programs[i].Label, Path.Combine(programDir, programs[i].Entry)));
        }
        return written;
    }

    /// <summary>
    /// Whether the C# parser accepts the program a check of <paramref name="path"/> reads without an error: every
    /// <c>*.byn</c> under its directory and every tree <c>Compilation.FromPath</c> loads, imports included, but
    /// <c>package.byn</c>, the manifest (FromPath parses it as a module file, with errors). Parse diagnostics are
    /// <c>SyntaxTree.Diagnostics</c> (Compilation.cs:169-170), not a code range: BYN8001 is the parser's, BYN1016 the binder's.
    /// </summary>
    private static bool ParsesClean(string path) => !ParseErrors(path).Any();

    /// <summary>The code of every parse error <see cref="ParsesClean"/> looks at, in file order.</summary>
    private static IEnumerable<string> ParseErrors(string path) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.byn", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "package.byn")
            .SelectMany(f => SyntaxTree.Parse(SourceText.FromFile(f)).Diagnostics)
            .Concat(Compilation.FromPath(path).Modules.SelectMany(m => m.Files)
                .Where(f => Path.GetFileName(f.Source.Path) != "package.byn")
                .SelectMany(f => f.Tree.Diagnostics))
            .Where(d => d.IsError)
            .Select(d => d.Code);

    /// <summary>
    /// Runs <c>bayan-check</c> and <c>bayan-check --types</c> on every input (one shell per corpus) and compares
    /// each run's stderr, stdout (without the timed <c>◆ checked</c> summary, CliApp.cs:447) and exit code
    /// with <c>CliApp.Run(["check" | "types", path])</c>, naming the input, mode, stream and first differing line.
    /// </summary>
    private async Task CheckCorpus(string corpus, IReadOnlyList<(string Label, string Path)> inputs)
    {
        var clock = Stopwatch.StartNew();
        var results = Path.Combine(Root, corpus + "-out");
        if (Directory.Exists(results)) Directory.Delete(results, recursive: true);
        Directory.CreateDirectory(results);
        await RunAll(inputs, results);
        foreach (var (mode, i) in Modes.SelectMany(m => Enumerable.Range(0, inputs.Count).Select(i => (m, i))))
        {
            var (label, path) = inputs[i];
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = CliApp.Run([mode, path], stdout, stderr);
            var at = Path.Combine(results, $"{i:D5}.{mode}");
            var input = $"{label} ({path}) {mode}";
            SelfhostSemanticsTests.AssertSameLines(input, "stderr", stderr.ToString(), File.ReadAllText(at + ".err"));
            SelfhostSemanticsTests.AssertSameLines(input, "stdout", SelfhostSemanticsGeneratorTests.WithoutSummary(stdout.ToString()),
                SelfhostSemanticsGeneratorTests.WithoutSummary(File.ReadAllText(at + ".out")));
            var actualExit = int.Parse(File.ReadAllText(at + ".exit").Trim());
            Assert.True(exit == actualExit, $"{input}: bayan-check exited {actualExit}, bayan {mode} {exit}");
        }
        output.WriteLine($"{corpus}: {inputs.Count} of {inputs.Count} compared in both modes ({clock.Elapsed.TotalSeconds:F1} s)");
    }

    /// <summary>
    /// Both modes of every input into <c>&lt;results&gt;/NNNNN.&lt;mode&gt;.{out,err,exit}</c>. Unix runs them in one
    /// POSIX shell, eight at a time, because a .NET <see cref="Process.Start"/> per run would dominate the proof;
    /// Windows falls back to the same loop in managed code.
    /// </summary>
    private static async Task RunAll(IReadOnlyList<(string Label, string Path)> inputs, string results)
    {
        var program = BayanCheck;
        if (OperatingSystem.IsWindows())
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                foreach (var mode in Modes)
                {
                    var (exit, stdout, stderr) = SelfhostParserTests.Run(program, mode == "types" ? ["--types", inputs[i].Path] : [inputs[i].Path]);
                    var at = Path.Combine(results, $"{i:D5}.{mode}");
                    await File.WriteAllTextAsync(at + ".out", stdout);
                    await File.WriteAllTextAsync(at + ".err", stderr);
                    await File.WriteAllTextAsync(at + ".exit", $"{exit}\n");
                }
            }
            return;
        }

        var list = Path.Combine(results, "inputs.txt");
        await File.WriteAllLinesAsync(list, inputs.Select((p, i) => $"{i:D5} {p.Path}"));
        var script = Path.Combine(Root, "run.sh");
        await File.WriteAllTextAsync(script, """
            #!/bin/sh
            LC_ALL=C
            program=$1
            list=$2
            out=$3
            n=0
            while IFS=' ' read -r i f; do
                ( "$program" "$f" >"$out/$i.check.out" 2>"$out/$i.check.err"; echo "$?" >"$out/$i.check.exit" ) &
                ( "$program" --types "$f" >"$out/$i.types.out" 2>"$out/$i.types.err"; echo "$?" >"$out/$i.types.exit" ) &
                n=$((n+2))
                if [ "$n" -ge 8 ]; then wait; n=0; fi
            done <"$list"
            wait
            exit 0
            """);
        var (code, _, err) = SelfhostParserTests.Run("/bin/sh", script, program, list, results);
        Assert.True(code == 0, $"the L022 shell exited {code}:\n{err}");
        for (var i = 0; i < inputs.Count; i++)
        {
            foreach (var mode in Modes)
                Assert.True(File.Exists(Path.Combine(results, $"{i:D5}.{mode}.exit")), $"the shell did not run {mode} on {inputs[i].Path}\n{err}");
        }
    }
}
