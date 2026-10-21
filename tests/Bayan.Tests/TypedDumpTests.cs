using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary><c>tests/cases/typed/*.byn</c> → <c>*.types</c>: the <c>bayan types</c> JSON.</summary>
public sealed class TypedDumpTests
{
    public static TheoryData<string> Cases => Golden.Cases("typed");

    [Theory]
    [MemberData(nameof(Cases))]
    public void TypedGolden(string name) =>
        Golden.Check(name, ".types", path =>
        {
            var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
            Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
            return TypedDump.ToJson(c);
        });

    [Theory]
    [MemberData(nameof(Cases))]
    public void TwoRunsAreByteIdentical(string name)
    {
        var path = Path.Combine(Golden.CasesRoot, name);
        string Dump() => TypedDump.ToJson(Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!));
        Assert.Equal(Dump(), Dump());
    }
}
