using System.Diagnostics;
using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Region analysis computes each <c>ref this</c> method's changed fields once, not once per call path.</summary>
public sealed class RegionAnalysisPerfTests
{
    [Fact]
    public void TwoHundredMethodsCallingEachOtherCheckQuickly()
    {
        const int count = 200;
        var src = new StringBuilder("struct P {\n    List<i64> xs;\n    i64 n;\n\n");
        for (var i = 0; i < count; i++)
        {
            src.Append($"    void m{i}(ref this) {{\n        n += 1;\n");
            for (var j = 1; j <= 10 && i + j < count; j++)
                src.Append($"        if n > {j} {{ m{i + j}(); }}\n");
            src.Append("    }\n");
        }
        src.Append("}\n\nvoid main() {\n    var p = P { xs: List<i64>(), n: 0 };\n    p.m0();\n}\n");
        var watch = Stopwatch.StartNew();
        var c = Compilation.Create([new SourceText("perf.byn", Encoding.UTF8.GetBytes(src.ToString()))], ".");
        watch.Stop();
        Assert.DoesNotContain(c.Diagnostics.Items, d => d.IsError);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }
}
