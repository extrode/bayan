using Bayan.Backend.C;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan doctor</c>: the compiler version, host target, C compiler, zig, git and the build
/// cache, each a <c>◆</c> line, or a <c>✗</c> line with a <c>help:</c> fix under it. Exits 4 when
/// something fails, since every check is about the toolchain.
/// </summary>
internal static class Doctor
{
    internal enum Level { Ok, Note, Fail }

    private const string ZigUrl = "https://ziglang.org";

    internal sealed record Check(Level Level, string Name, string Detail, string? Fix = null);

    public static int Run(string[] args, TextWriter stdout, Style style)
    {
        var checks = new List<Check>
        {
            new(Level.Ok, Messages.DoctorBayan.Text(), Messages.DoctorVersion.Text(CliApp.Version, BuildInfo.Commit)),
            Targets.Host is { } host
                ? new(Level.Ok, Messages.DoctorHost.Text(), host)
                : new(Level.Fail, Messages.DoctorHost.Text(), Messages.DoctorNoHost.Text(), Messages.DoctorNoHostFix.Text()),
            CCompiler(),
            Zig(),
            Git(),
            Cache(),
        };
        Render(checks, style, stdout);
        return checks.Any(c => c.Level == Level.Fail) ? CliApp.ExitToolchain : CliApp.ExitOk;
    }

    /// <summary>Writes one line per check, a failure's fix under it; a note is a <c>◆</c> line that may carry a fix.</summary>
    internal static void Render(IReadOnlyList<Check> checks, Style style, TextWriter writer)
    {
        var width = checks.Max(c => c.Name.Length);
        foreach (var c in checks)
        {
            if (c.Level == Level.Fail)
                writer.WriteLine(style.Paint(Role.Error, Messages.DoctorFailLine.Text(style.Symbols.Cross, c.Name, c.Detail)));
            else
                writer.WriteLine(Messages.DoctorLine.Text(style.Paint(c.Level == Level.Ok ? Role.Success : Role.Note, style.Symbols.Dot), c.Name.PadRight(width), c.Detail));
            if (c.Fix is not null) writer.WriteLine(style.Paint(Role.Help, Messages.DoctorHelp.Text(c.Fix)));
        }
    }

    private static Check CCompiler()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BAYAN_CC") is { Length: > 0 };
        if (Toolchain.FindCompiler() is not { } compiler)
            return new(Level.Fail, Messages.DoctorCCompiler.Text(), Messages.DoctorNoCCompiler.Text(), Messages.DoctorNoCCompilerFix.Text(ZigUrl, "BAYAN_CC"));
        var command = string.Join(" ", compiler);
        if (Version(compiler[0], [.. compiler[1..], "--version"]) is not { } version)
            return fromEnv
                ? new(Level.Fail, Messages.DoctorCCompiler.Text(), Messages.DoctorCCompilerEnvBroken.Text("BAYAN_CC", command), Messages.DoctorCCompilerEnvBrokenFix.Text("BAYAN_CC"))
                : new(Level.Fail, Messages.DoctorCCompiler.Text(), Messages.DoctorCCompilerBroken.Text(command), Messages.DoctorCCompilerBrokenFix.Text("BAYAN_CC"));
        return new(Level.Ok, Messages.DoctorCCompiler.Text(), Messages.DoctorTool.Text(command, version));
    }

    private static Check Zig()
    {
        if (Toolchain.FindOnPath("zig") is not { } zig)
            return new(Level.Note, Messages.DoctorZig.Text(), Messages.DoctorNoZig.Text(), Messages.DoctorInstallZig.Text(ZigUrl));
        return Version(zig, ["version"]) is { } version
            ? new(Level.Ok, Messages.DoctorZig.Text(), Messages.DoctorTool.Text(zig, version))
            : new(Level.Fail, Messages.DoctorZig.Text(), Messages.DoctorToolBroken.Text(zig), Messages.DoctorReinstallZig.Text(ZigUrl));
    }

    private static Check Git()
    {
        if (Toolchain.FindOnPath("git") is not { } git)
            return new(Level.Note, Messages.DoctorGit.Text(), Messages.DoctorNoGit.Text("bayan new", "git init", "bayan dist", "bayan test --changed"), Messages.DoctorInstallGit.Text());
        return Version(git, ["--version"]) is { } version
            ? new(Level.Ok, Messages.DoctorGit.Text(), Messages.DoctorTool.Text(git, version))
            : new(Level.Note, Messages.DoctorGit.Text(), Messages.DoctorToolBroken.Text(git), Messages.DoctorReinstallGit.Text());
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
            return new(Level.Ok, Messages.DoctorCache.Text(), Messages.DoctorCacheOk.Text(dir, stats.Programs, stats.Bytes / 1024.0 / 1024.0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(Level.Fail, Messages.DoctorCache.Text(), Messages.DoctorCacheBroken.Text(dir, e.Message), Messages.DoctorCacheBrokenFix.Text("BAYAN_CACHE"));
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
