using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>Monomorphization, checked in the emitted C rather than the program's output.</summary>
public sealed class GenericsTests
{
    [Fact]
    public void EachTypeArgumentGetsOneInstantiationAndTheTemplateNone()
    {
        var path = Path.Combine(Golden.CasesRoot, "run", "generics-functions.byn");
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors);
        var code = CliApp.EmitC(c, BuildMode.Safe);
        var instances = Regex.Matches(code, @"\bbyn_main__pick__\w+").Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["byn_main__pick__i64", "byn_main__pick__main__Point", "byn_main__pick__str"], instances);
        Assert.DoesNotMatch(@"\bbyn_main__pick\(", code);
    }

    [Fact]
    public void ARolledBackFirstPassLeavesNoInstance()
    {
        var path = Path.Combine(Golden.CasesRoot, "run", "generics-var-literal-rollback.byn");
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors);
        var code = CliApp.EmitC(c, BuildMode.Safe);
        Assert.Contains("byn_main__id__i64", code);
        Assert.DoesNotContain("id__i32", code);
    }
}
