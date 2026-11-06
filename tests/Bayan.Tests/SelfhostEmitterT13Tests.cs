using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>011 T13: the order the emitter visits modules in, against <c>bayan build --emit-c</c> (Q818).</summary>
public sealed class SelfhostEmitterT13Tests
{
    /// <summary>
    /// A script importing a module, each with an enum and a function: the C# lowers functions (Lowerer.cs:23) and declares enums
    /// (CEmitter.cs:94) over <c>Compilation.Modules</c>, the imported module before its importer, so its file is <c>byn_file_0</c>
    /// and its enum's typedef comes first.
    /// </summary>
    [Fact]
    public void ModulesEmitInCompilationOrder()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t13-tests", "module-order");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(Path.Combine(dir, "shade"));
        File.WriteAllText(Path.Combine(dir, "shade", "shade.byn"),
            "public enum Shade {\n    light\n    dark\n}\n\npublic i64 depth(Shade s) => s == .dark ? 2 : 1\n");
        var main = Path.Combine(dir, "main.byn");
        File.WriteAllText(main, "import shade\n\nenum Mood {\n    calm\n    busy\n}\n\nlet m = Mood.busy\nprint($\"{shade.depth(.dark)} {m == .busy}\")\n");
        SelfhostEmitterHarness.SameAsCli("module-order", ["build", "--emit-c", main], [main]);
    }
}
