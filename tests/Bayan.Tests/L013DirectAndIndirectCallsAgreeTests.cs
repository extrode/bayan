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
/// L013 (docs/laws/013-direct-and-indirect-calls-agree.md): every <see cref="LambdaGenerator"/> program
/// compiles, its emitted C builds standalone under <c>-Wall -Werror</c>, and the direct, local, via1 and via2
/// sections of its output are the same text. Seeds 1..48; <c>BAYAN_L013_SEEDS</c> raises the count.
/// </summary>
public sealed class L013DirectAndIndirectCallsAgreeTests(ITestOutputHelper output)
{
    private static readonly string[] Sections = LambdaGenerator.Modes;

    [Fact]
    [Trait("Law", "L013")]
    public void DirectAndIndirectCallsAgree()
    {
        var compiler = Toolchain.FindCompiler();
        if (compiler is null)
        {
            output.WriteLine("skipped: no C compiler found");
            return;
        }
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("BAYAN_L013_SEEDS"), out var n) && n > 0 ? n : 48;
        var failures = new ConcurrentBag<(int Seed, string Message)>();
        var sources = new ConcurrentBag<string>();
        Parallel.For(1, seeds + 1, seed =>
        {
            var source = LambdaGenerator.Generate((ulong)seed);
            sources.Add(source);
            if (Failure(compiler, source, $"gen-{seed}.byn") is { } failure)
                failures.Add((seed, $"seed {seed}: {failure}\nprogram:\n{source}"));
        });
        output.WriteLine($"seeds {seeds}, failed {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join("\n\n", failures.OrderBy(f => f.Seed).Select(f => f.Message)));
        // Every grammar choice is drawn by some seed.
        foreach (var marker in new[] { "(() -> ", "(i64 -> ", "(bool -> ", "(string -> ", "((", "-> i64) f", "-> bool) f", "-> string) f", "uses alloc =", "= fn", "by: fn", "by: f)", "() => ", "p0 => ", "(p0, p1) => ", "=> {\n", "let k0", "(i64 q0", "(bool q0", "(string q0", ", to " })
            Assert.True(sources.Any(s => s.Contains(marker, StringComparison.Ordinal)), $"no program contains `{marker}`");
        foreach (var pattern in new[] { @"=> [^\n]*\bk\d", @"=> [^\n]*\bq\d", @"by: [^\n]*\bit\b" })
            Assert.True(sources.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, pattern)), $"no program matches `{pattern}`");
    }

    /// <summary>The proof can fail: a program whose via2 section disagrees with the direct one is reported.</summary>
    [Fact]
    public void DisagreeingSectionsFail()
    {
        var compiler = Toolchain.FindCompiler();
        if (compiler is null) return;
        var source = LambdaGenerator.Generate(1).Replace("print(\"== via2\")", "print(\"== via2\")\n    print(\"extra\")");
        Assert.StartsWith("via2 section differs from direct", Failure(compiler, source, "gen-1.byn"));
    }

    private static string? Failure(string[] compiler, string source, string path)
    {
        var c = Compilation.Create([SourceText.FromString(path, source)], ".");
        if (c.Diagnostics.HasErrors) return "rejected:\n" + DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true);
        foreach (var mode in new[] { BuildMode.Debug, BuildMode.Safe })
        {
            var (code, stdout, stderr) = BuildStandalone(compiler, CliApp.EmitC(c, mode), mode);
            if (code is null) return $"{mode} C does not build under -Wall -Werror:\n{stderr}";
            if (code != 0) return $"{mode} exit {code}\nstderr:\n{stderr}";
            var parts = stdout.Replace("\r\n", "\n").Split("== ");
            if (parts.Length != Sections.Length + 1 || !parts[1].StartsWith("direct\n", StringComparison.Ordinal)) return $"{mode} output has no {Sections.Length} sections:\n{stdout}";
            var direct = parts[1]["direct\n".Length..];
            for (var i = 1; i < Sections.Length; i++)
            {
                var body = parts[i + 1][(Sections[i].Length + 1)..];
                if (body != direct) return $"{Sections[i]} section differs from direct ({mode}):\n--- direct\n{direct}--- {Sections[i]}\n{body}";
            }
        }
        return null;
    }

    /// <summary>
    /// Builds <paramref name="cSource"/> as <c>out.c</c> with <c>-Wall -Werror</c> (the toolchain build passes
    /// <c>-w</c>) and the runtime without them, then runs it. A null code means the build failed.
    /// </summary>
    private static (int? Code, string Stdout, string Stderr) BuildStandalone(string[] compiler, string cSource, BuildMode mode)
    {
        var dir = Directory.CreateTempSubdirectory("bayan-l013-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.c"), cSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), Toolchain.RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), Toolchain.RuntimeHeader);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "out.exe" : "out");
            string[] define = mode == BuildMode.Debug ? ["-DBYN_DEBUG"] : ["-DBYN_NDEBUG"];
            foreach (var args in new[]
            {
                ["-std=c11", "-fwrapv", "-Wall", "-Werror", .. define, "-I.", "-c", "out.c", "-o", "out.o"],
                ["-std=c11", "-fwrapv", "-w", .. define, "-I.", "-c", "bayan-rt.c", "-o", "rt.o"],
                new[] { "out.o", "rt.o", "-o", exe, OperatingSystem.IsWindows() ? "-lshell32" : "-lm" },
            })
            {
                var cc = new ProcessStartInfo(compiler[0]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in compiler[1..]) cc.ArgumentList.Add(a);
                foreach (var a in args) cc.ArgumentList.Add(a);
                var (ccCode, ccOut, ccErr) = Exec(cc);
                if (ccCode != 0) return (null, "", ccOut + ccErr);
            }
            var (code, stdout, stderr) = Exec(new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            return (code, stdout, stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (int Code, string Stdout, string Stderr) Exec(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.Result;
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }
}
