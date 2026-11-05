using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T9: coverage instrumentation (R9, R10, R11).</summary>
public sealed class CoverageTests
{
    /// <summary>The C of a <c>--coverage</c> build.</summary>
    private static string EmitCoverage(Compilation c, BuildMode mode, out CoverageMap map)
    {
        map = CoverageInstrumenter.Instrument(c);
        return CEmitter.Emit(Lowerer.Lower(c, coverage: map.Probes), mode == BuildMode.Debug);
    }

    private sealed record Outcome(string Stdout, string Stderr, int ExitCode, int Pid);

    /// <summary>Runs <paramref name="exe"/> with <c>BAYAN_COVERAGE</c> set to <paramref name="prefix"/>, or unset when it is null.</summary>
    private static Outcome Run(string exe, string? prefix, string? input = null, string? work = null, string[]? args = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        if (work is not null) psi.WorkingDirectory = work;
        if (prefix is null) psi.Environment.Remove("BAYAN_COVERAGE");
        else psi.Environment["BAYAN_COVERAGE"] = prefix;
        foreach (var arg in args ?? []) psi.ArgumentList.Add(arg);
        if (input is not null && File.Exists(Path.ChangeExtension(input, ".args")))
            foreach (var arg in File.ReadAllLines(Path.ChangeExtension(input, ".args"))) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        if (input is not null && File.Exists(Path.ChangeExtension(input, ".stdin")))
            p.StandardInput.BaseStream.Write(File.ReadAllBytes(Path.ChangeExtension(input, ".stdin")));
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            Assert.Fail($"{exe} timed out after 120 s");
        }
        var code = p.ExitCode;
        code = PanicExit.Portable(code);
        return new Outcome(stdout.Result, stderr.Result, code, p.Id);
    }

    private static readonly ConcurrentDictionary<string, Lazy<string>> Builds = new();

    /// <summary>Starts every coverage build in the background (<see cref="WarmUp"/>), so the sequential theory mostly waits on finished work.</summary>
    private static readonly Lazy<bool> Warm = new(() =>
    {
        WarmUp.Start(((IEnumerable<object[]>)MutantSchemataTests.ProgramCases()).Select(row => (string)row[0]), name => CoverageBuild(name));
        return true;
    });

    private static string CoverageBuild(string name) => Builds.GetOrAdd(name, n => new Lazy<string>(() =>
    {
        var mode = MutantSchemataTests.ModeOf(Path.Combine(Golden.CasesRoot, n));
        var build = Toolchain.Build(EmitCoverage(MutantSchemataTests.CompileCase(n), mode, out _), mode);
        return build.Succeeded ? build.ExecutablePath! : throw new InvalidOperationException($"{n} did not build:\n{build.Errors}");
    }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static string Scratch(string kind, string name) =>
        Path.Combine(Golden.RepoRoot, "tmp", kind, Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_'));

    /// <summary>R10 law (L019): a <c>run/</c> program built with coverage gives the plain stdout, stderr and exit code, with counters written or not.</summary>
    [Theory]
    [MemberData(nameof(MutantSchemataTests.ProgramCases), MemberType = typeof(MutantSchemataTests))]
    public void CoverageBuildIsThePlainProgram(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var head = File.ReadLines(input).Take(2).ToList();
        if (OperatingSystem.IsWindows() ? head.Contains("// os: unix") : head.Contains("// os: windows")) return;
        _ = Warm.Value;
        var exe = CoverageBuild(name);
        var work = Scratch("run-coverage", name);
        var prefix = Path.Combine(Scratch("run-coverage-files", name), "cov");
        foreach (var counters in new[] { null, prefix })
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
            CoverageInstrumenter.DeleteStale(prefix);
            var outcome = Run(exe, counters, input, work);
            string Expected(string extension) => File.Exists(Path.ChangeExtension(input, extension))
                ? File.ReadAllText(Path.ChangeExtension(input, extension)).Replace("\r\n", "\n") : "";
            var label = $"{name}, BAYAN_COVERAGE={counters ?? "unset"}";
            Assert.True(Expected(".stdout") == outcome.Stdout.Replace("\r\n", "\n"), $"{label}: stdout differs:\n{outcome.Stdout}");
            Assert.True(Expected(".stderr") == outcome.Stderr.Replace("\r\n", "\n"), $"{label}: stderr differs:\n{outcome.Stderr}");
            Assert.True(Expected(".exitcode").Trim() == (outcome.ExitCode == 0 ? "" : outcome.ExitCode.ToString(CultureInfo.InvariantCulture)), $"{label}: exit code {outcome.ExitCode}");
        }
    }

    /// <summary>R10 law for a <c>*.test.byn</c> case: every test of the coverage test binary, run as <c>bayan test</c> runs it, gives the plain test binary's output.</summary>
    [Theory]
    [MemberData(nameof(MutantSchemataTests.TestCases), MemberType = typeof(MutantSchemataTests))]
    public void CoverageBuildIsThePlainTestBinary(string name)
    {
        var mode = MutantSchemataTests.ModeOf(Path.Combine(Golden.CasesRoot, name));
        var c = MutantSchemataTests.CompileCase(name);
        var tests = Lowerer.Lower(c).Tests.Count;
        Assert.True(tests > 0, $"{name} has no tests");
        var plain = Toolchain.Build(CliApp.EmitC(c, mode), mode);
        Assert.True(plain.Succeeded, plain.Errors);
        var coverage = Toolchain.Build(EmitCoverage(c, mode, out _), mode);
        Assert.True(coverage.Succeeded, coverage.Errors);
        var prefix = Path.Combine(Scratch("test-coverage-files", name), "cov");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        for (var i = 0; i < tests; i++)
        {
            string[] args = ["--test", i.ToString(CultureInfo.InvariantCulture)];
            var expected = Run(plain.ExecutablePath!, null, args: args) with { Pid = 0 };
            foreach (var counters in new[] { null, prefix })
            {
                CoverageInstrumenter.DeleteStale(prefix);
                Assert.True(expected == Run(coverage.ExecutablePath!, counters, args: args) with { Pid = 0 },
                    $"{name}, test {i}, BAYAN_COVERAGE={counters ?? "unset"}: differs from the plain test binary");
            }
        }
    }

    private static string Dir => Path.Combine(Golden.RepoRoot, "tmp", "coverage");

    private static (CoverageMap Map, string Exe) Build(string text, bool tests = false)
    {
        var c = Compilation.Create([SourceText.FromString(Path.Combine(Dir, tests ? "main.test.byn" : "main.byn"), text)], Dir,
            tests ? new CompilationOptions(IncludeTests: true, Library: true) : null);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(EmitCoverage(c, BuildMode.Safe, out var map), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        return (map, build.ExecutablePath!);
    }

    /// <summary>A fresh counter-file prefix for one test, its stale files deleted.</summary>
    private static string Prefix(string test)
    {
        Directory.CreateDirectory(Dir);
        var prefix = Path.Combine(Dir, test);
        CoverageInstrumenter.DeleteStale(prefix);
        return prefix;
    }

    /// <summary>The single counter file a run left under <paramref name="prefix"/>, read.</summary>
    private static Dictionary<int, ulong> Counts(string prefix, string suffix)
    {
        var files = Directory.GetFiles(Path.GetDirectoryName(prefix)!, Path.GetFileName(prefix) + ".*");
        Assert.Equal([prefix + "." + suffix], files);
        return CoverageInstrumenter.Read(files[0]);
    }

    private static int LineOf(string text, string snippet)
    {
        var at = text.IndexOf(snippet, StringComparison.Ordinal);
        Assert.True(at >= 0, snippet);
        return text[..at].Count(ch => ch == '\n') + 1;
    }

    private static ulong Hits(Dictionary<int, ulong> counts, CoverageCounter counter) => counts.GetValueOrDefault(counter.Id);

    private const string Paths = """
        union error E {
            bad
            worse
        }

        i64 check(i64 n) fails E {
            fail .bad if n < 0
            return n
        }

        i64 pass(i64 n) fails E {
            return try check(n)
        }

        i64 count(i64 n) {
            var i: i64 = 0
            while i < n {
                i += 1
            }
            return i
        }

        string sign(i64 n) {
            if n > 0 {
                return "pos"
            }
            return n == 0 ? "zero" : "neg"
        }

        bool both(bool a, bool b) => a && b

        bool either(bool a, bool b) => a || b

        print(sign(1))
        print(sign(-1))
        print($"{count(3)}")
        print($"{both(a: true, b: false)} {either(a: false, b: false)}")
        let r = pass(-1) catch switch {
            E.bad => 1,
            E.worse => 2
        }
        print($"{r}")

        """;

    /// <summary>R9: statements, both outcomes of if, while, ternary, <c>&&</c> and <c>||</c>, and error paths by (function, variant) with their origin.</summary>
    [Fact]
    public void ProbesCountWhatRan()
    {
        var (map, exe) = Build(Paths);
        var prefix = Prefix("paths");
        var outcome = Run(exe, prefix);
        Assert.Equal("pos\nneg\n3\nfalse false\n1\n", outcome.Stdout);
        var counts = Counts(prefix, outcome.Pid.ToString(CultureInfo.InvariantCulture));

        CoverageCounter At(string kind, string snippet, bool? outcome = null, int nth = 0) =>
            map.Counters.Where(c => c.Kind == kind && c.Line == LineOf(Paths, snippet) && (outcome is null || c.Outcome == outcome)).ElementAt(nth);
        (ulong True, ulong False) Branch(string snippet, int nth = 0) =>
            (Hits(counts, At(CoverageInstrumenter.Branch, snippet, true, nth)), Hits(counts, At(CoverageInstrumenter.Branch, snippet, false, nth)));

        // Statements: one per statement, the main program's too.
        Assert.Equal(3UL, Hits(counts, At(CoverageInstrumenter.Line, "i += 1")));
        Assert.Equal(1UL, Hits(counts, At(CoverageInstrumenter.Line, "return \"pos\"")));
        Assert.Equal(1UL, Hits(counts, At(CoverageInstrumenter.Line, "print(sign(-1))")));
        Assert.Equal(0UL, Hits(counts, At(CoverageInstrumenter.Line, "return n\n")));
        // Branches: each outcome its own counter.
        Assert.Equal((1UL, 1UL), Branch("if n > 0"));
        Assert.Equal((3UL, 1UL), Branch("while i < n"));
        Assert.Equal((0UL, 1UL), Branch("n == 0 ?"));
        Assert.Equal((1UL, 0UL), Branch("a && b", 0));
        Assert.Equal((0UL, 1UL), Branch("a && b", 1));
        Assert.Equal((0UL, 1UL), Branch("a || b", 0));
        Assert.Equal((0UL, 1UL), Branch("a || b", 1));
        Assert.Equal((1UL, 0UL), Branch("fail .bad if n < 0"));
        // Error paths: `check` raises E.bad with `fail`, `pass` passes it on through `try`; E.worse never happens.
        var errors = map.Counters.Where(c => c.Kind == CoverageInstrumenter.ErrorPath).ToList();
        var raised = Assert.Single(errors, c => c.Function == "check");
        Assert.Equal(("E.bad", "fail", LineOf(Paths, "fail .bad")), (raised.Variant, raised.Origin, raised.Line));
        Assert.Equal(1UL, Hits(counts, raised));
        var passed = errors.Where(c => c.Function == "pass").ToList();
        Assert.Equal(["E.bad", "E.worse"], passed.Select(c => c.Variant));
        Assert.All(passed, c => Assert.Equal(("try", LineOf(Paths, "try check")), (c.Origin, c.Line)));
        Assert.Equal([1UL, 0UL], passed.Select(c => Hits(counts, c)));
    }

    /// <summary>R10: counters reach the file when the program stops through <c>exit()</c> (<c>todo</c> exits 4) and on a panic (<c>byn_cov_flush</c>).</summary>
    [Theory]
    [InlineData("todo(\"later\")", 4)]
    [InlineData("panic(\"boom\")", 134)]
    public void CountersFlushOnExitAndPanic(string stop, int exitCode)
    {
        var text = $"void step() {{\n    print(\"step\")\n}}\n\nstep()\nstep()\n{stop}\n";
        var (map, exe) = Build(text);
        var prefix = Prefix("stop");
        var outcome = Run(exe, prefix);
        Assert.Equal(("step\nstep\n", exitCode), (outcome.Stdout, outcome.ExitCode));
        var counts = Counts(prefix, outcome.Pid.ToString(CultureInfo.InvariantCulture));
        var line = Assert.Single(map.Counters, c => c.Kind == CoverageInstrumenter.Line && c.Line == 2);
        Assert.Equal(2UL, Hits(counts, line));
        Assert.Equal(1UL, Hits(counts, Assert.Single(map.Counters, c => c.Kind == CoverageInstrumenter.Line && c.Line == 7)));
    }

    /// <summary>
    /// Each process writes <c>&lt;BAYAN_COVERAGE&gt;.&lt;test index&gt;</c> under <c>--test</c> and <c>&lt;BAYAN_COVERAGE&gt;.&lt;pid&gt;</c>
    /// without it; stale files are deleted first; with <c>BAYAN_COVERAGE</c> unset nothing is written; test bodies have no counters.
    /// </summary>
    [Fact]
    public void CounterFilesAreNamedByTestIndexOrPid()
    {
        const string tests = "i64 twice(i64 n) {\n    return n * 2\n}\n\ntest \"one\" {\n    assert(twice(1) == 2)\n}\n\ntest \"two\" {\n    assert(twice(2) == 4)\n    assert(twice(3) == 6)\n}\n";
        var (map, exe) = Build(tests, tests: true);
        Assert.Equal([2], map.Counters.Select(c => c.Line));
        var prefix = Prefix("tests");
        File.WriteAllText(prefix + ".7", "stale");
        CoverageInstrumenter.DeleteStale(prefix);
        Assert.Equal(0, Run(exe, prefix, args: ["--test", "1"]).ExitCode);
        Assert.Equal(2UL, Counts(prefix, "1")[map.Counters[0].Id]);
        CoverageInstrumenter.DeleteStale(prefix);
        Assert.Equal(0, Run(exe, null, args: ["--test", "0"]).ExitCode);
        Assert.Empty(Directory.GetFiles(Dir, "tests.*"));

        var (program, programExe) = Build("print(\"hi\")\n");
        prefix = Prefix("program");
        var outcome = Run(programExe, prefix);
        Assert.Equal(1UL, Counts(prefix, outcome.Pid.ToString(CultureInfo.InvariantCulture))[Assert.Single(program.Counters).Id]);
    }
}
