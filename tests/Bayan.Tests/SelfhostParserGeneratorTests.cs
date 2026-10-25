using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Tests for the generator (spec 009 R7). The build and process helpers mirror <see cref="SelfhostParserTests"/>
/// (T13) so this class can run before the wave merges; <see cref="GeneratedSeedsAgree"/> is the end-to-end
/// comparison, which only T15 can make pass.
/// </summary>
public sealed class SelfhostParserGeneratorTests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>The built <c>bayan-parse</c>, or the compiler's complaint (package root <c>selfhost/</c>, Q430).</summary>
    private static readonly Lazy<(string? Path, string Error)> BayanParse = new(() =>
        BuildParser(Directory.GetFiles(ParserDir, "*.byn").Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f)).ToList()));

    private static (string? Path, string Error) BuildParser(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static string Built
    {
        get
        {
            var (path, error) = BayanParse.Value;
            if (path is null) Assert.Fail($"selfhost/parser did not build:\n{error}");
            return path!;
        }
    }

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>R7: the valid corpus, parsed by the C# parser, instantiates every node record type.</summary>
    [Fact]
    public void EveryNodeRecordTypeIsInstantiatedByTheValidCorpus()
    {
        var found = new Dictionary<Type, string>();
        foreach (var (name, source) in SelfhostParserGenerator.GenerateValid(SelfhostParserGenerator.Seeds))
        {
            var tree = SyntaxTree.Parse(SourceText.FromString(name, source));
            foreach (var type in SelfhostParserGenerator.InstantiatedTypes(tree))
                if (!found.ContainsKey(type)) found[type] = name;
        }
        var missing = SelfhostParserGenerator.RecordTypes.Where(t => !found.ContainsKey(t)).ToArray();
        Assert.True(missing.Length == 0, $"no valid program instantiates: {string.Join(", ", missing.Select(t => t.Name))}");
    }

    /// <summary>R7: the corpora are identical across two runs of the same seeds.</summary>
    [Fact]
    public void GeneratedCorporaAreIdenticalAcrossRuns()
    {
        var seeds = SelfhostParserGenerator.Seeds;
        var a = Corpus(seeds);
        var b = Corpus(seeds);
        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.Equal(a[i].Source, b[i].Source);
        }
    }

    private static (string Name, string Source)[] Corpus(ulong[] seeds) =>
        SelfhostParserGenerator.GenerateValid(seeds).Concat(SelfhostParserGenerator.GenerateCorrupt(seeds)).ToArray();

    /// <summary>Corruption drops, duplicates or swaps a token of the source using the C# lexer's spans.</summary>
    [Fact]
    public void CorruptionDropsDuplicatesOrSwapsTokens()
    {
        const string source = "void f() { return a + b }\n";
        var dropped = false;
        var duplicated = false;
        var swapped = false;
        for (ulong seed = 1; seed < 400; seed++)
        {
            var corrupt = SelfhostParserGenerator.Corrupt(source, seed);
            if (corrupt.Length < source.Length) dropped = true;
            else if (corrupt.Length > source.Length) duplicated = true;
            else if (corrupt != source) swapped = true;
        }
        Assert.True(dropped && duplicated && swapped, $"dropped {dropped}, duplicated {duplicated}, swapped {swapped}");
        Assert.Equal(SelfhostParserGenerator.Corrupt(source, 42), SelfhostParserGenerator.Corrupt(source, 42));
    }

    /// <summary>R8 input: <see cref="SelfhostParserGenerator.OneMegabyte"/> is about 1 MB of valid code.</summary>
    [Fact]
    public void OneMegabyteIsValidSource()
    {
        var source = SelfhostParserGenerator.OneMegabyte();
        Assert.Equal(1024 * 1024, source.Length);
        Assert.Empty(SyntaxTree.Parse(source).Diagnostics);
    }

    /// <summary>
    /// R7 mutation sensitivity: pins the C# reference dump of the whole generated corpus (valid + corrupted) so a
    /// deliberate change to <c>AstDump</c> (a dropped node field) or the parser (a moved recovery point) fails this
    /// test on at least one seed. This is the in-process reference <see cref="GeneratedSeedsAgree"/> compares the
    /// self-hosted parser against once it lands (T15).
    /// </summary>
    [Fact]
    public void GeneratedCorpusReferenceIsPinned()
    {
        var programs = Corpus(SelfhostParserGenerator.Seeds);
        var sb = new StringBuilder();
        foreach (var (name, source) in programs)
            sb.Append(name).Append('\n').Append(AstDump.Dump(SyntaxTree.Parse(SourceText.FromString(name, source)))).Append('\0');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        Assert.Equal(PinnedCorpusReference, hash);
    }

    private const string PinnedCorpusReference = "59685440FCC5EA11E638AEB4C7E974B025D2F1524CCDAA11AAD736B330D54951";

    /// <summary>
    /// R7: the valid and corrupted corpora under <c>tmp/parser-corpus/</c>, run with <c>bayan-parse --many</c>,
    /// compared file by file with the in-process C# reference. Skipped until T13 lands <c>--many</c>; the full
    /// parity is T15's.
    /// </summary>
    [Fact]
    public void GeneratedSeedsAgree()
    {
        var probeDir = Path.Combine(Golden.RepoRoot, "tmp", "parser-corpus-probe");
        Directory.CreateDirectory(probeDir);
        File.WriteAllBytes(Path.Combine(probeDir, "empty.byn"), []);
        var (probeExit, _, probeErr) = Run(Built, "--many", probeDir);
        if (probeExit == 2)
        {
            output.WriteLine($"skipped: bayan-parse --many is not implemented yet (T13/T15 pending); probe exit {probeExit}\n{probeErr}");
            return;
        }

        var dir = Path.Combine(Golden.RepoRoot, "tmp", "parser-corpus");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        // `--many` prints in `Io.listDir` byte order (Q53), so iterate in that order, not the generator's.
        var programs = Corpus(SelfhostParserGenerator.Seeds).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        foreach (var (name, source) in programs) File.WriteAllText(Path.Combine(dir, name), source);

        var (exit, stdout, stderr) = Run(Built, "--many", dir);
        var cursor = 0;
        for (var i = 0; i < programs.Length; i++)
        {
            var header = $"== {programs[i].Name}\n";
            Assert.True(stdout.AsSpan(cursor).StartsWith(header),
                $"bayan-parse --many stopped before {programs[i].Name} (exit {exit}):\n{stderr}");
            cursor += header.Length;
            var next = i + 1 < programs.Length ? stdout.IndexOf($"== {programs[i + 1].Name}\n", cursor, StringComparison.Ordinal) : -1;
            var end = next < 0 ? stdout.Length : next;
            var actual = stdout[cursor..end];
            cursor = end;
            // The dump ends where the span-check lines begin (bayan L018 has no C# counterpart),
            // so byte parity compares only the dump against AstDump.
            var spanIdx = actual.IndexOf("span out of range: ", StringComparison.Ordinal);
            var actualDump = spanIdx < 0 ? actual : actual[..spanIdx];
            var expected = AstDump.Dump(SyntaxTree.Parse(SourceText.FromFile(Path.Combine(dir, programs[i].Name))));
            if (actualDump == expected) continue;
            var expectedPath = Path.Combine(dir, Path.ChangeExtension(programs[i].Name, ".ast"));
            File.WriteAllText(expectedPath, expected);
            var failure = Golden.Compare(expectedPath, actualDump, update: false).Failure ?? "outputs differ";
            Assert.Fail($"{Path.Combine(dir, programs[i].Name)}: bayan-parse and the C# parser disagree\n{failure}");
        }
        Assert.True(exit == 0, $"exit {exit}\n{stderr}");
    }
}
