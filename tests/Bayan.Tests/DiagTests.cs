using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>
/// <c>tests/cases/diag/*.byn</c> → <c>*.diag</c>: every diagnostic the front end reports, rendered.
/// A directory with a <c>main.byn</c> is a package whose imports resolve inside it. A <c>*.test.byn</c> case compiles its tests.
/// </summary>
public sealed class DiagTests
{
    public static TheoryData<string> Cases => Golden.Cases("diag");

    [Theory]
    [MemberData(nameof(Cases))]
    public void DiagGolden(string name) =>
        Golden.Check(name, ".diag", path =>
        {
            var dir = Path.GetDirectoryName(path)!;
            var c = Compilation.Create([Golden.Load(path)], dir, new CompilationOptions(IncludeTests: path.EndsWith(".test.byn", StringComparison.Ordinal)));
            var package = Path.GetFileName(dir) != "diag";
            return DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: package);
        });
}
