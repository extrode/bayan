using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>Spec 016 T1: <c>import std.…</c> against an injected std table.</summary>
public sealed class StdImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bayan-std-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Probe = """
        public string greet() {
            return "probe"
        }

        string hidden() {
            return "hidden"
        }

        public void save(string text) uses fs {
            Io.writeFile("probe.txt", text) catch { return }
        }
        """;

    private static StdLibrary Std(params (string Path, string Text)[] files) =>
        new(files.Select(f => (f.Path, Encoding.UTF8.GetBytes(f.Text))));

    private static readonly StdLibrary ProbeStd = Std(("probe/probe.byn", Probe), ("probe/probe.test.byn", "test \"t\" {}"));

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static void AssertProbeLoaded(Compilation c)
    {
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var probe = Assert.Single(c.Modules, m => m.Name == "std.probe");
        Assert.Equal(["<std>/probe/probe.byn"], probe.Files.Select(f => f.Source.Path));
        Assert.True(probe.Functions.ContainsKey("greet"));
    }

    [Fact]
    public void ResolvesFromAScript()
    {
        var path = Write("a.byn", "#!/usr/bin/env bayan\nimport std.probe\nprint(probe.greet())\n");
        Write("other.byn", "this does not parse {");
        AssertProbeLoaded(Compilation.FromPath(path, new CompilationOptions(Std: ProbeStd), wholeDirectory: false));
    }

    [Fact]
    public void ResolvesFromAFileAndADirectory()
    {
        var path = Write("main.byn", "import std.probe\nprint(probe.greet())\n");
        AssertProbeLoaded(Compilation.FromPath(path, new CompilationOptions(Std: ProbeStd)));
        AssertProbeLoaded(Compilation.FromPath(_dir, new CompilationOptions(Std: ProbeStd)));
    }

    [Fact]
    public void ResolvesFromAProject()
    {
        Write("src/app/main.byn", "import std.probe\nprint(probe.greet())\n");
        AssertProbeLoaded(Compilation.FromProject(_dir, "app", new CompilationOptions(Std: ProbeStd)));
    }

    [Fact]
    public void ResolvesANestedModule()
    {
        Write("main.byn", "import std.a.b\nprint(b.name())\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std(("a/a.byn", "public i64 one() {\n    return 1\n}\n"),
            ("a/b/b.byn", "public string name() {\n    return \"b\"\n}\n"))));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        Assert.Equal(["<std>/a/b/b.byn"], Assert.Single(c.Modules, m => m.Name == "std.a.b").Files.Select(f => f.Source.Path));
        Assert.DoesNotContain(c.Modules, m => m.Name == "std.a");
    }

    [Fact]
    public void APackagesOwnStdDirectoryIsNotRead()
    {
        // The package's `std/probe/` neither parses nor declares `greet`: reading it would fail.
        Write("main.byn", "import std.probe\nprint(probe.greet())\n");
        Write("std/probe/probe.byn", "public string other( {");
        AssertProbeLoaded(Compilation.FromPath(_dir, new CompilationOptions(Std: ProbeStd)));
        Write("src/app/main.byn", "import std.probe\nprint(probe.greet())\n");
        Write("src/std/probe/probe.byn", "public string other( {");
        AssertProbeLoaded(Compilation.FromProject(_dir, "app", new CompilationOptions(Std: ProbeStd)));
    }

    [Fact]
    public void APackageStdDirectoryDoesNotSupplyAMissingStdModule()
    {
        Write("main.byn", "import std.probe\nprint(probe.greet())\n");
        Write("std/probe/probe.byn", Probe);
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std()));
        var d = Assert.Single(c.Diagnostics.Items);
        Assert.Equal("BYN2032", d.Code);
        Assert.Equal("no std module `std.probe`", d.Message);
        Assert.Equal("remove the import: no std modules ship with this build", d.Help);
    }

    [Fact]
    public void AnUnknownStdModuleListsTheShippedOnesSorted()
    {
        Write("main.byn", "import std.nope\nimport std\nprint(\"hi\")\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std(("zeta/z.byn", ""), ("probe/p.byn", ""), ("a/b/c.byn", ""))));
        Assert.Equal(["no std module `std.nope`", "no std module `std`"], c.Diagnostics.Items.Select(d => d.Message));
        Assert.All(c.Diagnostics.Items, d => Assert.Equal("BYN2032", d.Code));
        Assert.All(c.Diagnostics.Items, d => Assert.Equal("import one of `std.a.b`, `std.probe`, `std.zeta`", d.Help));
    }

    [Fact]
    public void AStdModuleImportsAnother()
    {
        Write("main.byn", "import std.a\nprint($\"{a.twice()}\")\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std(
            ("a/a.byn", "import std.b\n\npublic i64 twice() {\n    return b.once() + b.once()\n}\n"),
            ("b/b.byn", "public i64 once() {\n    return 1\n}\n"))));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        Assert.Equal(["std.b", "std.a", "main"], c.Modules.Select(m => m.Name));
    }

    [Fact]
    public void AStdCycleIsAnImportCycle()
    {
        Write("main.byn", "import std.a\nprint(\"hi\")\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std(("a/a.byn", "import std.b\n"), ("b/b.byn", "import std.a\n"))));
        var d = Assert.Single(c.Diagnostics.Items);
        Assert.Equal(DiagnosticCodes.ImportCycle.Code, d.Code);
        Assert.Equal("import cycle: std.a → std.b → std.a", d.Message);
        Assert.Equal("<std>/b/b.byn", d.Source!.Path);
    }

    [Fact]
    public void AStdModuleCannotImportAPackageModule()
    {
        // The package's `util/` would resolve if the import were read against PackageRoot.
        Write("main.byn", "import std.a\nprint(\"hi\")\n");
        Write("util/util.byn", "public i64 one() {\n    return 1\n}\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: Std(("a/a.byn", "import util\n"))));
        var d = Assert.Single(c.Diagnostics.Items);
        Assert.Equal("BYN2033", d.Code);
        Assert.Equal("std module `std.a` imports `util`, which is not a std module", d.Message);
        Assert.Equal("<std>/a/a.byn", d.Source!.Path);
        Assert.DoesNotContain(c.Modules, m => m.Name == "util");
    }

    [Fact]
    public void ANonPublicStdMemberIsNotVisible()
    {
        Write("main.byn", "import std.probe\nprint(probe.hidden())\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: ProbeStd));
        Assert.Contains(c.Diagnostics.Items, d => d.Code == DiagnosticCodes.NotVisible.Code && d.Source!.Path.EndsWith("main.byn", StringComparison.Ordinal));
    }

    [Fact]
    public void AStdFunctionsEffectReachesTheImporter()
    {
        // A public function without `uses` is pure, so calling the std `save` (uses fs) must be declared.
        Write("main.byn", "import std.probe\n\npublic void store() {\n    probe.save(\"x\")\n}\n\nvoid main() {\n    store()\n}\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Std: ProbeStd));
        var d = Assert.Single(c.Diagnostics.Items, d => d.IsError);
        Assert.Equal(DiagnosticCodes.EffectNotDeclared.Code, d.Code);
        Assert.Equal("`store` uses `fs` but does not declare it", d.Message);
    }

    [Fact]
    public void DiagnosticsInStdSourceUseTheStdDisplayPath()
    {
        // The std function's own `fs` counts against the importing package's capabilities.
        Write("main.byn", "import std.probe\n\nvoid main() {\n    probe.save(\"x\")\n}\n");
        var c = Compilation.FromPath(_dir, new CompilationOptions(Capabilities: ["net"], PackageName: "local/p", Std: ProbeStd));
        var d = Assert.Single(c.Diagnostics.Items, d => d.Code == DiagnosticCodes.CapabilityExceeded.Code && d.Message.StartsWith("`save`", StringComparison.Ordinal));
        Assert.Contains("--> <std>/probe/probe.byn:9:", DiagnosticRenderer.RenderAll([d], includePath: true));
    }
}
