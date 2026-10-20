using System.Collections.Concurrent;
using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>
/// <c>tests/cases/run/*.byn</c>: compiled with the C backend and run. Expected <c>*.stdout</c>
/// always; <c>*.stderr</c> and <c>*.exitcode</c> only when non-empty / non-zero. A first line
/// <c>// mode: fast</c> (or <c>debug</c>) picks the build mode; the default is <c>safe</c>.
/// </summary>
public sealed class RunTests
{
    public static TheoryData<string> Cases => Golden.Cases("run");

    private sealed record Outcome(string Stdout, string Stderr, int ExitCode, string? BuildError);

    private static readonly ConcurrentDictionary<string, Lazy<Outcome>> Outcomes = new();

    static RunTests()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
    }

    /// <summary>Starts building every case in parallel, so the sequential theory mostly waits on finished work.</summary>
    private static readonly Lazy<bool> Warm = new(() =>
    {
        foreach (var row in (IEnumerable<object[]>)Cases)
        {
            var name = (string)row[0];
            Task.Run(() => Get(name));
        }
        return true;
    });

    private static Outcome Get(string name) =>
        Outcomes.GetOrAdd(name, n => new Lazy<Outcome>(() => Execute(n), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    [Theory]
    [MemberData(nameof(Cases))]
    public void RunGolden(string name)
    {
        _ = Warm.Value;
        var outcome = Get(name);
        if (outcome.BuildError is { } error) Assert.Fail($"{name} did not build:\n{error}");
        var input = Path.Combine(Golden.CasesRoot, name);
        var failures = new List<string>();
        void Check(string extension, string actual, bool optional)
        {
            var expected = Path.ChangeExtension(input, extension);
            if (optional && actual.Length == 0 && !File.Exists(expected)) return;
            if (Golden.Compare(expected, actual, Golden.UpdateMode).Failure is { } f) failures.Add(f);
        }
        Check(".stdout", outcome.Stdout, optional: false);
        Check(".stderr", outcome.Stderr, optional: true);
        Check(".exitcode", outcome.ExitCode == 0 ? "" : outcome.ExitCode + "\n", optional: true);
        if (failures.Count > 0) Assert.Fail(string.Join("\n", failures));
    }

    private static Outcome Execute(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var dir = Path.GetDirectoryName(input)!;
        var source = Golden.Load(input);
        var firstLine = File.ReadLines(input).FirstOrDefault() ?? "";
        var mode = BuildMode.Safe;
        if (firstLine.StartsWith("// mode: ", StringComparison.Ordinal))
            Toolchain.TryParseMode(firstLine["// mode: ".Length..].Trim(), out mode);
        var c = Compilation.Create([source], dir);
        if (c.Diagnostics.HasErrors) return new Outcome("", "", 0, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c), mode);
        if (!build.Succeeded) return new Outcome("", "", 0, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = dir,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20_000))
        {
            p.Kill();
            return new Outcome("", "", 0, "timed out after 20 s");
        }
        var code = p.ExitCode;
        if (OperatingSystem.IsWindows() && code == 3) code = 134;
        return new Outcome(stdout.Result, stderr.Result, code, null);
    }
}
