using Bayan.Backend.C;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan doctor</c>: the compiler version, host target, C compiler, zig, git and the build
/// cache, each <c>ok</c>, <c>note</c> (works, with a limit) or <c>FAIL</c> with a fix. Exits 4 when
/// something fails, since every check is about the toolchain.
/// </summary>
internal static class Doctor
{
    private enum Level { Ok, Note, Fail }

    private sealed record Check(Level Level, string Name, string Detail, string? Fix = null);

    public static int Run(string[] args, TextWriter stdout)
    {
        var checks = new List<Check>
        {
            new(Level.Ok, "bayan", $"{CliApp.Version} (commit {BuildInfo.Commit})"),
            Targets.Host is { } host
                ? new(Level.Ok, "host", host)
                : new(Level.Fail, "host", "this machine has no Bayan target", "build on linux, macos or windows, x64 or arm64"),
            CCompiler(),
            Zig(),
            Git(),
            Cache(),
        };
        var width = checks.Max(c => c.Name.Length);
        foreach (var c in checks)
        {
            stdout.WriteLine($"{c.Level switch { Level.Ok => "ok  ", Level.Note => "note", _ => "FAIL" }}  {c.Name.PadRight(width)}  {c.Detail}");
            if (c.Fix is not null) stdout.WriteLine($"      {new string(' ', width)}  fix: {c.Fix}");
        }
        return checks.Any(c => c.Level == Level.Fail) ? CliApp.ExitToolchain : CliApp.ExitOk;
    }

    private static Check CCompiler()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BAYAN_CC") is { Length: > 0 };
        if (Toolchain.FindCompiler() is not { } compiler)
            return new(Level.Fail, "C compiler", "none found", "install zig (https://ziglang.org), or set BAYAN_CC to a C compiler");
        var command = string.Join(" ", compiler);
        if (Version(compiler[0], [.. compiler[1..], "--version"]) is not { } version)
            return fromEnv
                ? new(Level.Fail, "C compiler", $"BAYAN_CC is `{command}`, which can't be run", "point BAYAN_CC at a C compiler, or unset it to use zig")
                : new(Level.Fail, "C compiler", $"`{command}` can't be run", "reinstall it, or set BAYAN_CC to a working C compiler");
        return new(Level.Ok, "C compiler", $"{command} ({version})");
    }

    private static Check Zig()
    {
        if (Toolchain.FindOnPath("zig") is not { } zig)
            return new(Level.Note, "zig", "not found; building for other targets needs it", "install zig (https://ziglang.org)");
        return Version(zig, ["version"]) is { } version
            ? new(Level.Ok, "zig", $"{zig} ({version})")
            : new(Level.Fail, "zig", $"{zig} can't be run", "reinstall zig (https://ziglang.org)");
    }

    private static Check Git()
    {
        if (Toolchain.FindOnPath("git") is not { } git)
            return new(Level.Note, "git", "not found; `bayan new` skips `git init`, `bayan dist` records no commit, `bayan test --changed` is unavailable", "install git");
        return Version(git, ["--version"]) is { } version
            ? new(Level.Ok, "git", $"{git} ({version})")
            : new(Level.Note, "git", $"{git} can't be run", "reinstall git");
    }

    private static Check Cache()
    {
        var dir = Toolchain.CacheDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".doctor-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            var stats = Toolchain.Stats();
            return new(Level.Ok, "cache", $"{dir} (writable, {stats.Programs} programs, {stats.Bytes / 1024.0 / 1024.0:0.0} MB)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(Level.Fail, "cache", $"{dir} isn't writable: {e.Message}", "set BAYAN_CACHE to a writable directory");
        }
    }

    /// <summary>The first line <paramref name="file"/> prints for <paramref name="args"/>; null if it can't run or fails.</summary>
    private static string? Version(string file, string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var text = stdout.Length > 0 ? stdout : stderr.Result;
            return p.ExitCode == 0 ? text.Split('\n')[0].Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
