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
/// <c>// mode: fast</c> (or <c>debug</c>) picks the build mode; the default is <c>safe</c>. An optional
/// <c>*.args</c> holds one command-line argument per line, and an optional <c>*.stdin</c> the bytes fed to standard input (otherwise it is empty). A line <c>// os: unix</c> among the first two skips the case on Windows, and <c>// os: windows</c> skips it everywhere else. Each case runs in its own empty directory
/// under <c>tmp/run/</c>, so it may write files. A <c>*.test.byn</c> case is run by <c>bayan test -v</c> instead, with seconds masked as <c>Ts</c>.
/// A <c>*.pending</c> file marks a case expected to fail until a feature lands (it names the step): the output must still
/// differ, so the case fails loudly once it matches, and <c>BAYAN_UPDATE=1</c> leaves its expected files alone.
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

    /// <summary>Starts building every case in the background (<see cref="WarmUp"/>), so the sequential theory mostly waits on finished work.</summary>
    private static readonly Lazy<bool> Warm = new(() =>
    {
        WarmUp.Start(((IEnumerable<object[]>)Cases).Select(row => (string)row[0]), name => Get(name));
        return true;
    });

    private static Outcome Get(string name) =>
        Outcomes.GetOrAdd(name, n => new Lazy<Outcome>(() => Execute(n), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    [Theory]
    [MemberData(nameof(Cases))]
    public void RunGolden(string name)
    {
        var head = File.ReadLines(Path.Combine(Golden.CasesRoot, name)).Take(2).ToList();
        if (OperatingSystem.IsWindows() ? head.Contains("// os: unix") : head.Contains("// os: windows")) return;
        _ = Warm.Value;
        var outcome = Get(name);
        if (outcome.BuildError is { } error) Assert.Fail($"{name} did not build:\n{error}");
        var input = Path.Combine(Golden.CasesRoot, name);
        var pending = File.Exists(Path.ChangeExtension(input, ".pending"));
        var failures = new List<string>();
        void Check(string extension, string actual, bool optional)
        {
            var expected = Path.ChangeExtension(input, extension);
            if (optional && actual.Length == 0 && !File.Exists(expected)) return;
            if (Golden.Compare(expected, actual, Golden.UpdateMode && !pending).Failure is { } f) failures.Add(f);
        }
        Check(".stdout", outcome.Stdout, optional: false);
        Check(".stderr", outcome.Stderr, optional: true);
        Check(".exitcode", outcome.ExitCode == 0 ? "" : outcome.ExitCode + "\n", optional: true);
        if (pending)
        {
            if (failures.Count == 0) Assert.Fail($"{name} now matches its expected output; delete its .pending file");
            return;
        }
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
        if (name.EndsWith(".test.byn", StringComparison.Ordinal)) return ExecuteTests(name, input);
        var c = Compilation.Create([source], dir);
        if (c.Diagnostics.HasErrors) return new Outcome("", "", 0, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, mode), mode);
        if (!build.Succeeded) return new Outcome("", "", 0, build.Errors);
        var work = Path.Combine(Golden.RepoRoot, "tmp", "run", Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_'));
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = work,
        };
        var argsFile = Path.ChangeExtension(input, ".args");
        if (File.Exists(argsFile))
            foreach (var arg in File.ReadAllLines(argsFile)) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var stdinFile = Path.ChangeExtension(input, ".stdin");
        if (File.Exists(stdinFile))
        {
            var bytes = File.ReadAllBytes(stdinFile);
            p.StandardInput.BaseStream.Write(bytes);
        }
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        // Generous: after a runtime change every case cold-compiles (Warm, ProcessorCount at a time), and a trivial run can wait 20 s+ for CPU.
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            return new Outcome("", "", 0, "timed out after 120 s");
        }
        var code = p.ExitCode;
        code = PanicExit.Portable(code);
        return new Outcome(stdout.Result, stderr.Result, code, null);
    }

    /// <summary>The test file alone in its work directory, so <c>bayan test</c> sees no other case.</summary>
    private static Outcome ExecuteTests(string name, string input)
    {
        var work = Path.Combine(Golden.RepoRoot, "tmp", "run", Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_'));
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.Copy(input, Path.Combine(work, Path.GetFileName(input)));
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliApp.Run(["test", "-v", work], stdout, stderr);
        var masked = System.Text.RegularExpressions.Regex.Replace(stdout.ToString().ReplaceLineEndings("\n"), @"\d+\.\d\ds$", "Ts", System.Text.RegularExpressions.RegexOptions.Multiline);
        return new Outcome(masked, stderr.ToString(), code, null);
    }
}
