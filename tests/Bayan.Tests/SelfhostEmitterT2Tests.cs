using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T2: <c>selfhost/emitter/lower.byn</c> (Lowerer.cs 11-351) and <c>cprint/common.byn</c> (CEmitter.cs 397-476,
/// 588-599, 1237-1253) against the C# as an in-process oracle. The emitter's top-level files but <c>main.byn</c> are
/// built with a generated <c>main.byn</c> that runs one helper per mode on a fixture (Q763).
/// </summary>
public sealed class SelfhostEmitterT2Tests
{
    private const string Main = """
        import semantics
        import emitter.cprint

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 1..args.length { rest.append(args[i]) }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            if args[0] == "lower" {
                t2PrintLower(ref c, ref l)
            }
            return 0
        }

        void t2PrintLower(ref Ctx c, ref LCtx l) uses alloc {
            let p = lower(ref c, to: ref l)
            Io.stdout.write("== lower\n")
            for f in p.functions { t2PrintFunction(in c, $"fn {c.functions[f.symbol].name}", in f) }
            Io.stdout.write(p.main == -1 ? "main -\n" : $"main {c.functions[p.main].name}\n")
            for i in 0..p.tests.length { t2PrintFunction(in c, $"test {c.tests[p.tests[i]].title}", in p.testFunctions[i]) }
        }

        void t2PrintFunction(in Ctx c, string head, in LoweredFunction f) uses alloc {
            Io.stdout.write($"{head} locals={f.locals.length} ctx={f.contextSlots} panic={f.panicSlots} stmts={c.bBlocks[f.body & 0xFFFFFFFF].statements.length}\n")
        }
        """;

    private static readonly ConcurrentDictionary<string, Lazy<(string? Path, string Error)>> Builds = new();

    private static string Build()
    {
        var (path, error) = Builds.GetOrAdd("main", _ => new(BuildNow)).Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T2 main did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var dir = SelfhostEmitterHarness.EmitterDir;
        var sources = Directory.GetFiles(dir, "*.byn")
            .Where(p => Path.GetFileName(p) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(dir, "main.byn"), Main));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Writes <paramref name="source"/> to a temporary file, then runs <paramref name="act"/> on its path.</summary>
    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    private static Dictionary<string, List<string>> RunMode(string mode, string path)
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), [mode, path]);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    // ── lower ────────────────────────────────────────────────────────────────────

    private const string LowerFixture = """
        struct P {
            i64 x = 1
            i64 y
            P(i64 a) { }
        }

        void f() { }

        T id<T>(T v) => v

        test "nothing" { }

        void main() { }
        """;

    private static List<string> LowerOracle(Compilation c)
    {
        var p = Lowerer.Lower(c);
        var lines = new List<string>();
        foreach (var f in p.Functions) lines.Add($"fn {f.Symbol.Name} {Shape(f)}");
        lines.Add(p.Main is null ? "main -" : $"main {p.Main.Name}");
        foreach (var (t, f) in p.Tests) lines.Add($"test {t.Name} {Shape(f)}");
        return lines;
    }

    private static string Shape(LoweredFunction f) =>
        $"locals={f.Locals.Count} ctx={f.ContextSlots} panic={f.PanicSlots} stmts={f.Body.Statements.Length}";

    [Fact]
    public void Lower_ProgramShapeMatchesCSharp() => WithFixture(LowerFixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("lower", path), "lower", LowerOracle(Compile(path))));
}
