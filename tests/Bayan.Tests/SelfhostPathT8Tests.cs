using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;

namespace Bayan.Tests;

/// <summary>
/// 014 T8: <c>t26PathOf</c> (<c>typed.byn</c>) and <c>programName</c> (<c>json.byn</c>, with <c>t26FileName</c> and
/// <c>t26TrimEndSlash</c>) against the C# <c>TypedDump.PathOf</c> (<c>TypedDump.cs:89</c>) and <c>CliApp.ProgramName</c>
/// (<c>CliApp.cs:535</c>), both private and called by reflection. Inputs are built from <c>tmp/t8-path/&lt;name&gt;</c>
/// with the OS separator, so the class runs on macOS and Windows.
/// </summary>
public sealed class SelfhostPathT8Tests
{
    private static string Base => Path.Combine(Golden.RepoRoot, "tmp", "t8-path", "work");

    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    private const string MainSource = """
        i32 main(ReadOnlySpan<string> args) {
            print("== all")
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
                displayRoot: args[0],
            }
            var i: i64 = 1
            while i + 1 < args.length {
                if args[i] == "pathof" {
                    print($"pathof {args[i + 1]} {t26PathOf(ref c, for: args[i + 1])}")
                } else {
                    print($"name {args[i + 1]} {programName(ref c, for: args[i + 1])}")
                }
                i += 2
            }
            return 0
        }
        """;

    private static void Prepare()
    {
        if (Directory.Exists(Base)) Directory.Delete(Base, recursive: true);
        Directory.CreateDirectory(Path.Combine(Base, "proj"));
        Directory.CreateDirectory(Path.Combine(Base, "app.d"));
        File.WriteAllText(Path.Combine(Base, "proj", "main.byn"), "");
        File.WriteAllText(Path.Combine(Base, "proj", "tool.v2.byn"), "");
    }

    /// <summary>Creates the scratch tree once, before any test asks the C# about it.</summary>
    private static readonly Lazy<bool> Ready = new(() => { Prepare(); return true; });

    private static string PathOf(string root, string path)
    {
        _ = Ready.Value;
        var c = Compilation.Create([], root, null, root);
        var m = typeof(TypedDump).GetMethod("PathOf", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)m.Invoke(null, [c, path])!;
    }

    private static string ProgramName(string path)
    {
        _ = Ready.Value;
        var compiled = Activator.CreateInstance(typeof(Bayan.Cli.CliApp).GetNestedType("Compiled", BindingFlags.NonPublic | BindingFlags.Public)!, null, null, null)!;
        var m = typeof(Bayan.Cli.CliApp).GetMethod("ProgramName", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)m.Invoke(null, [compiled, path])!;
    }

    private static List<string> Inputs() =>
    [
        Path.Combine(Base, "proj"),
        Path.Combine(Base, "proj", "tool.v2.byn"),
        Path.Combine(Base, "proj") + Sep,
        Path.Combine(Base, "proj") + Sep + Sep,
        Path.Combine(Base, "proj", "main.byn"),
        Path.Combine(Base, "app.d"),
        Path.Combine(Base, "missing", "x.byn"),
    ];

    /// <summary>Path-only inputs: a relative path, the root itself, and a relative path with a backslash (the <c>Replace</c>).</summary>
    private static List<string> Extra() => ["rel" + Sep + "x.byn", Base, "rel\\x.byn", "a\\b\\c.byn"];

    private static readonly Lazy<List<string>> Run = new(() =>
    {
        _ = Ready.Value;
        var args = new List<string> { Base };
        foreach (var p in Inputs()) args.AddRange(["pathof", p, "name", p]);
        foreach (var p in Extra()) args.AddRange(["pathof", p]);
        return SelfhostSemanticsHarness.RunSections(MainSource, [.. args])["all"];
    });

    private static List<string> Lines(string kind) => Run.Value.Where(l => l.StartsWith(kind + " ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void PathOfMatchesCSharp()
    {
        var all = Inputs().Concat(Extra()).ToList();
        var expected = all.Select(p => $"pathof {p} {PathOf(Base, p)}").ToList();
        SelfhostSemanticsHarness.AssertSection(new() { ["pathof"] = Lines("pathof") }, "pathof", expected);
    }

    [Fact]
    public void ProgramNameMatchesCSharp()
    {
        var expected = Inputs().Select(p => $"name {p} {ProgramName(p)}").ToList();
        SelfhostSemanticsHarness.AssertSection(new() { ["name"] = Lines("name") }, "name", expected);
    }

    [Fact]
    public void ProgramNameCoversDirectoryFileTrailingSeparatorAndMain()
    {
        // guards the inputs: a directory, a dotted file, a trailing separator and main.byn all name differently
        Assert.Equal(["proj", "tool.v2", "proj", "proj", "proj", "app.d", "x"], Inputs().Select(ProgramName));
    }
}
