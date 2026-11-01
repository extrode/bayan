using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L021 (docs/laws/021-ill-typed-mutant-never-counted.md): a site that is not emitted is reported
/// <see cref="MutantStatus.NotEmitted"/> and no other site is; a mutant whose enclosing statement fails re-binding or
/// the ownership check is not emitted. Over the <c>run/</c> cases and 300 seeded programs (<c>BAYAN_L021_SEEDS</c> raises the count)
/// whose constant folds and moves make some candidates ill-typed.
/// </summary>
[Trait("Law", "L021")]
public sealed class NotEmittedLawTests(ITestOutputHelper output)
{
    private static string? Report(Schema schema)
    {
        var result = MutationRunner.Run("none", schema, [], Path.GetTempPath(), TimeSpan.FromSeconds(1));
        if (result.Mutants.Count != schema.Sites.Count) return $"{result.Mutants.Count} results for {schema.Sites.Count} sites";
        foreach (var m in result.Mutants)
        {
            var s = m.Site;
            if (!s.Emitted && m.Status != MutantStatus.NotEmitted)
                return $"{s.Site.File}:{s.Site.Line}:{s.Site.Column} `{s.Site.Original}` -> `{s.Site.Mutated}` is not emitted but reported {m.Status}";
            if (s.Emitted && m.Status == MutantStatus.NotEmitted)
                return $"{s.Site.File}:{s.Site.Line}:{s.Site.Column} `{s.Site.Original}` -> `{s.Site.Mutated}` is emitted but reported {m.Status}";
        }
        var counted = schema.Sites.Count(s => !s.Emitted);
        if (schema.NotEmitted.Values.Sum() < counted) return $"{counted} sites are not emitted, the schema counts {schema.NotEmitted.Values.Sum()}";
        return null;
    }

    [Fact]
    public void NotEmittedSitesAreReportedNotEmittedOverRunCases()
    {
        var failures = new ConcurrentBag<string>();
        var cases = ((IEnumerable<object[]>)RunTests.Cases).Select(r => (string)r[0]).ToList();
        var notEmitted = 0;
        Parallel.ForEach(cases, name =>
        {
            var schema = Schemata.Build(MutantSchemataTests.CompileCase(name));
            Interlocked.Add(ref notEmitted, schema.Sites.Count(s => !s.Emitted));
            if (Report(schema) is { } failure) failures.Add($"{name}: {failure}");
        });
        output.WriteLine($"cases {cases.Count}, sites not emitted {notEmitted}, failed {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join("\n", failures.Order(StringComparer.Ordinal).Take(10)));
        Assert.True(notEmitted > 0, "no run/ case has a site that is not emitted");
    }

    [Fact]
    public void IllTypedCandidatesAreNotEmittedOverGeneratedPrograms()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("BAYAN_L021_SEEDS"), out var n) && n > 0 ? n : 300;
        var failures = new ConcurrentBag<(int Seed, string Message)>();
        var illTyped = 0;
        var wellTyped = 0;
        Parallel.For(1, seeds + 1, seed =>
        {
            var source = Generate(seed);
            var dir = Path.Combine(Golden.RepoRoot, "tmp", "l021");
            var path = Path.Combine(dir, "main.byn");
            var c = Compilation.Create([SourceText.FromString(path, source)], dir);
            if (c.Diagnostics.HasErrors)
            {
                failures.Add((seed, $"seed {seed}: generated program does not compile:\n{source}"));
                return;
            }
            var schema = Schemata.Build(c);
            var lines = source.Split('\n');
            foreach (var s in schema.Sites)
            {
                var site = s.Site;
                var line = lines[site.Line - 1];
                var column = site.Column - 1;
                if (column + site.Original.Length > line.Length || line.Substring(column, site.Original.Length) != site.Original)
                {
                    failures.Add((seed, $"seed {seed}: site {s.Id} `{site.Original}` is not at {site.Line}:{site.Column}"));
                    continue;
                }
                lines[site.Line - 1] = string.Concat(line.AsSpan(0, column), site.Mutated, line.AsSpan(column + site.Original.Length));
                var mutated = Compilation.Create([SourceText.FromString(path, string.Join('\n', lines))], dir).Diagnostics.HasErrors;
                lines[site.Line - 1] = line;
                Interlocked.Increment(ref mutated ? ref illTyped : ref wellTyped);
                if (mutated && s.Emitted)
                    failures.Add((seed, $"seed {seed}: `{site.Original}` -> `{site.Mutated}` at {site.Line}:{site.Column} does not type-check but is emitted\n{source}"));
            }
            if (Report(schema) is { } failure) failures.Add((seed, $"seed {seed}: {failure}\n{source}"));
        });
        output.WriteLine($"seeds {seeds}, ill-typed candidates {illTyped}, well-typed {wellTyped}, failed {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join("\n\n", failures.OrderBy(f => f.Seed).Take(3).Select(f => f.Message)));
        Assert.True(illTyped > 0 && wellTyped > 0, "the generator draws no ill-typed or no well-typed candidate");
    }

    /// <summary>
    /// A program of 2 to 5 functions drawn from: a <c>u8</c> sum whose constants add up to 250 to 255 (raising one literal
    /// overflows it), an <c>i32</c> sum (every candidate type-checks), and a move whose re-assignment removal reads a moved value.
    /// </summary>
    private static string Generate(int seed)
    {
        var rng = new Random(seed);
        var sb = new StringBuilder();
        var calls = new List<string>();
        for (var i = 0; i < rng.Next(2, 6); i++)
        {
            switch (rng.Next(3))
            {
                case 0:
                    var a = rng.Next(0, 200);
                    var b = rng.Next(0, 250 - a);
                    var cc = rng.Next(250, 256) - a - b;
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"u8 f{i}() {{\n    return ({a} + {b}) + {Math.Max(cc, 0)}\n}}\n\n"));
                    calls.Add($"{{f{i}()}}");
                    break;
                case 1:
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"i32 f{i}(i32 x) {{\n    return x + {rng.Next(0, 100)}\n}}\n\n"));
                    calls.Add($"{{f{i}(3)}}");
                    break;
                default:
                    sb.Append($"i64 f{i}() {{\n    var a = List<i64>()\n    var b = move a\n    a = List<i64>()\n    a.append(1)\n    b.append(2)\n    return a.length + b.length\n}}\n\n");
                    calls.Add($"{{f{i}()}}");
                    break;
            }
        }
        sb.Append($"print($\"{string.Join(' ', calls)}\")\n");
        return sb.ToString();
    }
}
