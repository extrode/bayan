using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;

namespace Bayan.Tests;

/// <summary>
/// <c>tests/cases/diag/*.byn</c> → <c>*.diag</c>: every diagnostic the front end reports, rendered.
/// A directory with a <c>main.byn</c> is a package whose imports resolve inside it, bounded by its <c>package.byn</c> when it has one, its <c>std.…</c> imports read from its <c>stdlib/</c> when it has one. A <c>*.test.byn</c> case compiles its tests.
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
            // A package case may have a `package.byn`; its capabilities bound the effects (BYN6004).
            var manifest = File.Exists(Path.Combine(dir, "package.byn")) ? Manifest.Read(Path.Combine(dir, "package.byn")) : null;
            // A package case may have a `stdlib/`: its std modules replace the shipped ones.
            var stdlib = Path.Combine(dir, "stdlib");
            var std = Directory.Exists(stdlib)
                ? new StdLibrary(Directory.EnumerateFiles(stdlib, "*", SearchOption.AllDirectories)
                    .Select(f => (Path.GetRelativePath(stdlib, f).Replace('\\', '/'), File.ReadAllBytes(f))))
                : null;
            var c = Compilation.Create([Golden.Load(path)], dir, new CompilationOptions(IncludeTests: path.EndsWith(".test.byn", StringComparison.Ordinal),
                Capabilities: manifest?.Capabilities, PackageName: manifest?.Name, Std: std));
            var package = Path.GetFileName(dir) != "diag";
            return DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: package);
        });
}
