using Bayan.Semantics;
using Bayan.Semantics.Symbols;

namespace Bayan.Tests;

public sealed class ErrorUnionTypeTests
{
    [Fact]
    public void SetsThatDisplayAlikeStayDistinct()
    {
        var path = Path.Combine(Golden.CasesRoot, "diag", "validation-error-same-name-unhandled", "main.byn");
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        var sets = c.EntryModule.Functions["both"][0].ErrorSets;
        Assert.Equal(sets[0].DisplayName, sets[1].DisplayName);
        Assert.NotEqual(ErrorUnionType.Of([sets[0], sets[1]]), ErrorUnionType.Of([sets[1], sets[0]]));
        Assert.Equal(ErrorUnionType.Of([sets[0], sets[1]]), ErrorUnionType.Of([sets[0], sets[1]]));
    }
}
