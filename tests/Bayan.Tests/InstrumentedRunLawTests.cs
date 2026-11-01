using System.Collections.Concurrent;
using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Testing;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L019 (docs/laws/019-instrumented-run-equals-plain.md): over every <c>run/</c> program, the <c>--coverage</c> build
/// (counters written or not) and the <c>--mutate</c> build (<c>BAYAN_MUTANT</c> unset or <c>0</c>) give the plain build's
/// stdout, stderr and exit code.
/// </summary>
[Trait("Law", "L019")]
public sealed class InstrumentedRunLawTests(ITestOutputHelper output)
{
    private sealed record Outcome(string Stdout, string Stderr, int ExitCode);

    [Fact]
    public void InstrumentedRunEqualsPlainRun()
    {
        if (Toolchain.FindCompiler() is null)
        {
            output.WriteLine("skipped: no C compiler found");
            return;
        }
        var failures = new ConcurrentBag<string>();
        var cases = ((IEnumerable<object[]>)MutantSchemataTests.ProgramCases()).Select(r => (string)r[0]).ToList();
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, name =>
        {
            if (Failure(name) is { } failure) failures.Add(failure);
        });
        output.WriteLine($"cases {cases.Count}, failed {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join("\n", failures.Order(StringComparer.Ordinal).Take(10)));
    }

    private static string? Failure(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        if (OperatingSystem.IsWindows() && File.ReadLines(input).Take(2).Contains("// os: unix")) return null;
        var mode = MutantSchemataTests.ModeOf(input);
        var c = MutantSchemataTests.CompileCase(name);
        var plain = Toolchain.Build(CliApp.EmitC(c, mode), mode);
        var map = CoverageInstrumenter.Instrument(c);
        var coverage = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, coverage: map.Probes), mode == BuildMode.Debug), mode);
        var mutate = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, Schemata.Build(c).Switches), mode == BuildMode.Debug), mode);
        foreach (var (label, build) in new[] { ("plain", plain), ("--coverage", coverage), ("--mutate", mutate) })
            if (!build.Succeeded) return $"{name}: {label} build failed:\n{build.Errors}";
        var scratch = Path.Combine(Golden.RepoRoot, "tmp", "l019", Path.ChangeExtension(name, null).Replace('/', '_'));
        var expected = Run(plain.ExecutablePath!, input, Path.Combine(scratch, "plain"), []);
        var runs = new (string Label, string Exe, Dictionary<string, string?> Env)[]
        {
            ("--coverage, BAYAN_COVERAGE unset", coverage.ExecutablePath!, new() { ["BAYAN_COVERAGE"] = null }),
            ("--coverage, BAYAN_COVERAGE set", coverage.ExecutablePath!, new() { ["BAYAN_COVERAGE"] = Path.Combine(scratch, "counters", "cov") }),
            ("--mutate, BAYAN_MUTANT unset", mutate.ExecutablePath!, new() { ["BAYAN_MUTANT"] = null }),
            ("--mutate, BAYAN_MUTANT=0", mutate.ExecutablePath!, new() { ["BAYAN_MUTANT"] = "0" }),
        };
        for (var i = 0; i < runs.Length; i++)
        {
            Directory.CreateDirectory(Path.Combine(scratch, "counters"));
            var actual = Run(runs[i].Exe, input, Path.Combine(scratch, "run" + i), runs[i].Env);
            var stream = expected.Stdout != actual.Stdout ? $"stdout:\n{actual.Stdout}\nexpected:\n{expected.Stdout}"
                : expected.Stderr != actual.Stderr ? $"stderr:\n{actual.Stderr}\nexpected:\n{expected.Stderr}"
                : expected.ExitCode != actual.ExitCode ? $"exit code {actual.ExitCode}, expected {expected.ExitCode}"
                : null;
            if (stream is not null) return $"{name}, {runs[i].Label}: differs from the plain run in {stream}";
        }
        return null;
    }

    private static Outcome Run(string exe, string input, string work, Dictionary<string, string?> env)
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = work,
        };
        psi.Environment.Remove("BAYAN_COVERAGE");
        psi.Environment.Remove("BAYAN_MUTANT");
        foreach (var (k, v) in env)
            if (v is not null) psi.Environment[k] = v;
        if (File.Exists(Path.ChangeExtension(input, ".args")))
            foreach (var arg in File.ReadAllLines(Path.ChangeExtension(input, ".args"))) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        if (File.Exists(Path.ChangeExtension(input, ".stdin")))
            p.StandardInput.BaseStream.Write(File.ReadAllBytes(Path.ChangeExtension(input, ".stdin")));
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            return new Outcome("", "", -1);
        }
        return new Outcome(stdout.Result.Replace("\r\n", "\n"), stderr.Result.Replace("\r\n", "\n"), p.ExitCode);
    }
}
