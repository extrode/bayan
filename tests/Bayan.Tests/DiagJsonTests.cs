using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary><c>tests/cases/diag-json/*.byn</c> → <c>*.jsonl</c>: the <c>--diagnostics json</c> lines.</summary>
public sealed class DiagJsonTests
{
    public static TheoryData<string> Cases => Golden.Cases("diag-json");

    [Theory]
    [MemberData(nameof(Cases))]
    public void DiagJsonGolden(string name) =>
        Golden.Check(name, ".jsonl", path =>
        {
            var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
            return DiagnosticJson.RenderAll(c.Diagnostics.Items);
        });

    [Fact]
    public void ADiagnosticWithoutALocationHasNullFileAndSpan()
    {
        var d = new Diagnostic(Severity.Note, "BYN0000", null, default, "m");
        Assert.Equal(
            """{"code":"BYN0000","severity":"note","file":null,"span":null,"message":"m","label":null,"help":null,"fixes":[]}""",
            DiagnosticJson.Render(d));
    }
}
