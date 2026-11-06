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

    /// <summary>Spec 008 R12: <c>bayan types</c> on the symbol-table program binds cleanly and is byte-identical across two runs.</summary>
    [Fact]
    public void SelfHostSymbolTablesTypesAreByteIdentical()
    {
        var path = Path.Combine(Golden.CasesRoot, "run", "selfhost-symbol-tables.byn");
        var c = Compilation.FromPath(path, wholeDirectory: false);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var first = TypedDump.ToJson(c);
        Assert.Contains("Symbol", first);
        Assert.Equal(first, TypedDump.ToJson(Compilation.FromPath(path, wholeDirectory: false)));
    }

    public static TheoryData<string> L009Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var kind in new[] { "typed", "run" })
                foreach (var name in Golden.Cases(kind)) data.Add(name);
            return data;
        }
    }

    /// <summary>
    /// L009: the typed dump is byte-identical across two runs and across a run on a copy of the
    /// case in another directory under the same relative path. Each dump is a fresh compilation of
    /// the file by absolute path, as <c>bayan types file.byn</c> does; a multi-module case is
    /// copied with its directory. A case with errors is compared all the same.
    /// </summary>
    [Theory]
    [Trait("Law", "L009")]
    [MemberData(nameof(L009Cases))]
    public void DumpIsDeterministic(string name)
    {
        var path = Path.Combine(Golden.CasesRoot, name);
        var isModule = name.Count(ch => ch == '/') > 1;
        static string Dump(string file, bool isModule) =>
            TypedDump.ToJson(Compilation.FromPath(file, wholeDirectory: isModule));

        var root = Path.Combine(Golden.RepoRoot, "tmp", "l009", Guid.NewGuid().ToString("N"));
        var copy = Path.Combine(root, Path.GetRelativePath(Golden.RepoRoot, path));
        try
        {
            var dir = Path.GetDirectoryName(copy)!;
            Directory.CreateDirectory(dir);
            if (isModule)
            {
                var src = Path.GetDirectoryName(path)!;
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(dir, Path.GetRelativePath(src, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to);
                }
            }
            else
                File.Copy(path, copy);
            var first = Dump(path, isModule);
            Assert.Equal(first, Dump(path, isModule));
            Assert.Equal(first, Dump(copy, isModule));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
