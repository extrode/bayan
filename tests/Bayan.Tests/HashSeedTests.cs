using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Map hashing gets a fixed seed only under <c>--test</c> of a program that has tests.</summary>
public sealed class HashSeedTests
{
    private static string Emit(string text)
    {
        var dir = Path.Combine(Golden.CasesRoot, "run");
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
        Assert.False(c.Diagnostics.HasErrors);
        return CliApp.EmitC(c);
    }

    [Fact]
    public void ProgramWithoutTestsAlwaysGetsThePerRunSeed()
    {
        var code = Emit("var m = Map<i64, i64>();\nm.set(1, 10);\nprint($\"{m.count}\");\n");
        Assert.Contains("byn_hash_seed_init(false);", code);
        Assert.DoesNotContain("--test", code);
    }
}
