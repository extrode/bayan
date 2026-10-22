using System.Collections.Concurrent;
using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L008 (docs/laws/008-unique-never-aliased-at-runtime.md): every <see cref="OwnershipGenerator"/> program
/// that compiles runs its debug build to exit 0 with no ownership-checker panic and, where the C compiler
/// supports it, no AddressSanitizer report. Seeds 1..24 of 16 functions; <c>BAYAN_L008_SEEDS</c> raises the count.
/// </summary>
public sealed class L008OwnershipRuntimeTests(ITestOutputHelper output) : IDisposable
{
    private const int Size = 16;

    /// <summary>Unique per test instance, so concurrent test processes never share a working directory.</summary>
    private readonly string runRoot = Path.Combine(Golden.RepoRoot, "tmp", "l008", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(runRoot)) Directory.Delete(runRoot, recursive: true);
    }

    private sealed record Run(int ExitCode, string Stderr, string? Error);

    [Fact]
    [Trait("Law", "L008")]
    public void CompiledProgramsRunCleanUnderTheChecker()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("BAYAN_L008_SEEDS"), out var n) && n > 0 ? n : 24;
        var rejected = new ConcurrentBag<string>();
        var failures = new ConcurrentBag<(int Seed, string Message)>();
        var compiled = 0;
        var switchMoves = 0;
        Parallel.For(1, seeds + 1, seed =>
        {
            var source = OwnershipGenerator.Generate((ulong)seed, Size);
            var c = Compilation.Create([SourceText.FromString($"gen-{seed}.byn", source)], ".");
            var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
            if (errors.Count > 0)
            {
                rejected.Add($"seed {seed}: {string.Join(" ", errors.Select(e => e.Code).Distinct().Order())}");
                return;
            }
            Interlocked.Increment(ref compiled);
            if (source.Contains("switch (move", StringComparison.Ordinal)) Interlocked.Increment(ref switchMoves);
            var run = BuildAndRun(c, $"seed-{seed}");
            if (Failure(run) is { } failure)
                failures.Add((seed, $"seed {seed}: {failure}\nexit {run.ExitCode}\nstderr:\n{run.Stderr}\nprogram:\n{source}"));
        });
        foreach (var line in rejected.Order()) output.WriteLine(line);
        output.WriteLine($"seeds {seeds}, compiled {compiled}, rejected {rejected.Count}, asan {Toolchain.AsanAvailable}, switch (move in {switchMoves}");
        Assert.True(failures.IsEmpty, string.Join("\n\n", failures.OrderBy(f => f.Seed).Select(f => f.Message)));
        Assert.True(rejected.Count * 2 <= seeds, $"rejected {rejected.Count} of {seeds}");
        Assert.True(switchMoves > 0, "no compiled program contains `switch (move`");
    }

    /// <summary>The proof can fail: an alias the static check would reject panics the checker at runtime.</summary>
    [Fact]
    public void CheckerCatchesAnInjectedAlias()
    {
        var source = OwnershipGenerator.Generate(1, Size).Replace("void main() {\n", """
            void main() {
                var own = fresh(1);
                var alias = own;
                alias.append(2);
                own.append(3);

            """);
        var c = Compilation.Create([SourceText.FromString("alias.byn", source)], ".", new CompilationOptions(SkipOwnershipCheck: true));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var run = BuildAndRun(c, "alias");
        Assert.Null(run.Error);
        Assert.Equal(134, run.ExitCode);
        Assert.Contains("second owner", run.Stderr);
    }

    private static string? Failure(Run run)
    {
        if (run.Error is { } error) return error;
        if (run.ExitCode != 0) return "non-zero exit";
        foreach (var text in new[] { "panic", "second owner", "use of moved", "AddressSanitizer" })
            if (run.Stderr.Contains(text, StringComparison.Ordinal)) return $"stderr contains \"{text}\"";
        return null;
    }

    /// <summary>The proof can fail: C with no checker hooks (here safe-mode C, which emits none) fails the proof.</summary>
    [Fact]
    public void CodeWithoutCheckerHooksFails()
    {
        var c = Compilation.Create([SourceText.FromString("gen-1.byn", OwnershipGenerator.Generate(1, Size))], ".");
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        Assert.Equal("debug C has no byn_own_ checker hooks", Failure(BuildAndRun(c, "no-hooks", BuildMode.Safe)));
    }

    private Run BuildAndRun(Compilation c, string name, BuildMode emit = BuildMode.Debug)
    {
        var code = CliApp.EmitC(c, emit);
        if (!code.Contains("byn_own_", StringComparison.Ordinal)) return new Run(0, "", "debug C has no byn_own_ checker hooks");
        var build = Toolchain.Build(code, BuildMode.Debug, sanitize: Toolchain.AsanAvailable);
        if (!build.Succeeded) return new Run(0, "", $"build failed:\n{build.Errors}");
        var work = Path.Combine(runRoot, name);
        Directory.CreateDirectory(work);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = work,
        };
        // Memory lives in allocators that are released at exit, not freed one by one.
        psi.Environment["ASAN_OPTIONS"] = "detect_leaks=0";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            return new Run(0, "", "timed out after 120 s");
        }
        stdout.Wait();
        var exit = p.ExitCode;
        if (OperatingSystem.IsWindows() && exit == 3) exit = 134;
        return new Run(exit, stderr.Result, null);
    }
}
