using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Map hashing gets a fixed seed only under <c>--test</c> of a program that has tests.</summary>
public sealed class HashSeedTests
{
    private static string Emit(string text, string? tests = null)
    {
        var dir = Path.Combine(Golden.CasesRoot, "run");
        var sources = new List<SourceText> { SourceText.FromString(Path.Combine(dir, "main.byn"), text) };
        if (tests is not null) sources.Add(SourceText.FromString(Path.Combine(dir, "main.test.byn"), tests));
        var c = Compilation.Create(sources, dir, new CompilationOptions(IncludeTests: tests is not null));
        Assert.False(c.Diagnostics.HasErrors);
        return CliApp.EmitC(c, BuildMode.Safe);
    }

    [Fact]
    public void ProgramWithoutTestsAlwaysGetsThePerRunSeed()
    {
        var code = Emit("var m = Map<i64, i64>()\nm[1] = 10\nprint($\"{m.length}\")\n");
        Assert.Contains("byn_hash_seed_init(false);", code);
        Assert.DoesNotContain("--test", code);
    }

    [Fact]
    public void ProgramWithTestsSeedsOnlyUnderTestAndParsesArgsAfterDispatch()
    {
        var code = Emit("void main(ReadOnlySpan<string> args) {\n    var m = Map<i64, i64>()\n    m[1] = 10\n}\n", "test \"one\" {\n    assert(1 == 1)\n}\n");
        Assert.Contains("byn_hash_seed_init(argc == 3 && strcmp(argv[1], \"--test\") == 0);", code);
        Assert.True(code.IndexOf("byn_args(&argc", StringComparison.Ordinal) > code.IndexOf("switch (atoi(argv[2]))", StringComparison.Ordinal));
    }
}
