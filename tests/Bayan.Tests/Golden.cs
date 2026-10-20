namespace Bayan.Tests;

/// <summary>
/// Golden-file harness. A case is one input under <c>tests/cases/&lt;kind&gt;/</c> with its expected
/// output beside it. <c>BAYAN_UPDATE=1 dotnet test</c> rewrites expected files instead of comparing.
/// </summary>
public static class Golden
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string CasesRoot => Path.Combine(RepoRoot, "tests", "cases");

    public static bool UpdateMode =>
        Environment.GetEnvironmentVariable("BAYAN_UPDATE") is "1" or "true";

    /// <summary>Input files for <paramref name="kind"/>, relative to <see cref="CasesRoot"/>, sorted.</summary>
    public static TheoryData<string> Cases(string kind, string inputExtension = ".byn")
    {
        var data = new TheoryData<string>();
        var dir = Path.Combine(CasesRoot, kind);
        if (!Directory.Exists(dir)) return data;
        foreach (var file in Directory.EnumerateFiles(dir, "*" + inputExtension, SearchOption.AllDirectories)
                                      .Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(CasesRoot, file).Replace('\\', '/'));
        }
        return data;
    }

    /// <summary>Compares <paramref name="actual"/> with the file at <paramref name="expectedPath"/>.</summary>
    public static GoldenResult Compare(string expectedPath, string actual, bool update)
    {
        actual = Normalize(actual);
        if (update)
        {
            var existing = File.Exists(expectedPath) ? Normalize(File.ReadAllText(expectedPath)) : null;
            if (existing == actual) return GoldenResult.Match;
            File.WriteAllText(expectedPath, actual);
            return GoldenResult.Updated;
        }
        if (!File.Exists(expectedPath))
            return GoldenResult.Fail($"missing expected file {expectedPath} (run with BAYAN_UPDATE=1 to create it)");
        var expected = Normalize(File.ReadAllText(expectedPath));
        return expected == actual ? GoldenResult.Match : GoldenResult.Fail(Describe(expectedPath, expected, actual));
    }

    /// <summary>Runs one case: <paramref name="produce"/> maps the input path to the actual output.</summary>
    public static void Check(string caseName, string expectedExtension, Func<string, string> produce)
    {
        var input = Path.Combine(CasesRoot, caseName);
        var expectedPath = Path.ChangeExtension(input, expectedExtension);
        var result = Compare(expectedPath, produce(input), UpdateMode);
        if (result.Failure is { } failure) Assert.Fail(failure);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string Describe(string path, string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var line = 0;
        while (line < e.Length && line < a.Length && e[line] == a[line]) line++;
        return $"""
            golden mismatch: {path}
            first difference at line {line + 1}
              expected: {(line < e.Length ? e[line] : "<end of file>")}
              actual:   {(line < a.Length ? a[line] : "<end of file>")}
            --- actual output ---
            {actual}
            """;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Bayan.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Bayan.slnx not found above " + AppContext.BaseDirectory);
    }
}

public sealed record GoldenResult(string? Failure, bool WasUpdated)
{
    public static GoldenResult Match { get; } = new(null, false);
    public static GoldenResult Updated { get; } = new(null, true);
    public static GoldenResult Fail(string message) => new(message, false);
}
