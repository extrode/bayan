using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;

namespace Bayan.Tests;

/// <summary><c>tests/cases/diag/*.byn</c> → <c>*.diag</c>: every diagnostic the front end reports, rendered.</summary>
public sealed class DiagTests
{
    public static TheoryData<string> Cases => Golden.Cases("diag");

    [Theory]
    [MemberData(nameof(Cases))]
    public void DiagGolden(string name) =>
        Golden.Check(name, ".diag", path => DiagnosticRenderer.RenderAll(SyntaxTree.Parse(Golden.Load(path)).Diagnostics));
}
