using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Emitted C for lambdas and function values (spec 007 T7): a non-capturing call site allocates nothing.</summary>
public sealed class LambdaEmitTests
{
    private static string EmitGolden(string name)
    {
        var c = Compilation.Create([Golden.Load(Path.Combine(Golden.CasesRoot, "run", name))], Path.Combine(Golden.CasesRoot, "run"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return CliApp.EmitC(c, BuildMode.Safe);
    }

    [Fact]
    public void NonCapturingCallSitesDoNotAllocate()
    {
        var code = EmitGolden("lambda-non-capturing.byn");
        Assert.Contains("__main__lambda1(", code);
        Assert.DoesNotContain("malloc", code);
        Assert.DoesNotContain("byn_alloc", code);
        Assert.DoesNotContain("byn_context", code);
    }

    [Fact]
    public void CapturingCallSiteDoesNotAllocate()
    {
        var code = EmitGolden("lambda-capture-no-alloc.byn");
        Assert.Contains("__main__lambda1(", code);
        Assert.DoesNotContain("malloc", code);
        Assert.DoesNotContain("byn_alloc", code);
    }
}
