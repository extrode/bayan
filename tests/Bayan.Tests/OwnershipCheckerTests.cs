using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The debug-build ownership checker (spec 003): safe and fast builds carry none of it, and
/// <see cref="CompilationOptions.SkipOwnershipCheck"/> lets a test compile a program the static check rejects.
/// </summary>
public sealed class OwnershipCheckerTests
{
    public static TheoryData<string> RunCases => Golden.Cases("run");

    [Theory]
    [MemberData(nameof(RunCases))]
    public void SafeAndFastCHaveNoChecker(string name)
    {
        var path = Path.Combine(Golden.CasesRoot, name);
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors);
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Safe));
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Fast));
    }

    private const string UseAfterMove = """
        void main() {
            var a = List<i64>();
            var b = move a;
            a.add(1);
            print($"{b.length}");
        }

        """;

    [Fact]
    public void SkipOwnershipCheckSilencesTheStaticCheck()
    {
        Assert.Empty(OwnershipErrors(skip: true));
        Assert.Contains("BYN2781", OwnershipErrors(skip: false));
    }

    private static List<string> OwnershipErrors(bool skip)
    {
        var source = new SourceText("c.byn", System.Text.Encoding.UTF8.GetBytes(UseAfterMove));
        var c = Compilation.Create([source], ".", new CompilationOptions(SkipOwnershipCheck: skip));
        return c.Diagnostics.Items.Where(d => d.IsError && d.Code.StartsWith("BYN278", StringComparison.Ordinal)).Select(d => d.Code).ToList();
    }
}
