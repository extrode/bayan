using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Bayan.Backend.C;

/// <summary>
/// <c>debug</c>: no optimization, debug info, checked arithmetic. <c>safe</c> (default): optimized
/// and checked. <c>fast</c>: optimized; <c>+ - *</c> and narrowing casts wrap instead of trapping.
/// </summary>
public enum BuildMode { Debug, Safe, Fast }

/// <summary>The executable, or the C compiler's complaint.</summary>
public sealed record NativeBuild(string? ExecutablePath, string Errors)
{
    public bool Succeeded => ExecutablePath is not null;
}

/// <summary>Compiles generated C with the runtime into an executable, caching by content.</summary>
public static class Toolchain
{
    public static string RuntimeHeader { get; } = Resource("runtime/bayan-rt.h");

    public static string RuntimeSource { get; } = Resource("runtime/bayan-rt.c");

    /// <summary><c>$BAYAN_CACHE</c>, or <c>bayan-cache</c> in the OS temp directory.</summary>
    public static string CacheDirectory =>
        Environment.GetEnvironmentVariable("BAYAN_CACHE") is { Length: > 0 } dir ? dir : Path.Combine(Path.GetTempPath(), "bayan-cache");

    public static bool TryParseMode(string text, out BuildMode mode)
    {
        switch (text)
        {
            case "debug": mode = BuildMode.Debug; return true;
            case "safe": mode = BuildMode.Safe; return true;
            case "fast": mode = BuildMode.Fast; return true;
            default: mode = BuildMode.Safe; return false;
        }
    }

    /// <summary>
    /// The C compiler command: <c>$BAYAN_CC</c> (split on spaces), else <c>zig cc</c>, else <c>cc</c>
    /// from <c>PATH</c>; null when none is found.
    /// </summary>
    public static string[]? FindCompiler()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_CC") is { Length: > 0 } cc)
            return cc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (FindOnPath("zig") is { } zig) return [zig, "cc"];
        foreach (var name in new[] { "cc", "clang", "gcc" })
            if (FindOnPath(name) is { } path) return [path];
        return null;
    }

    private static string? FindOnPath(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    /// <summary>
    /// Compiles <paramref name="cSource"/> to an executable in the cache (reused when the source,
    /// runtime, mode and compiler are unchanged) and copies it to <paramref name="output"/> if given.
    /// </summary>
    public static NativeBuild Build(string cSource, BuildMode mode, string? output = null)
    {
        try
        {
            return BuildCached(cSource, mode, output);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new NativeBuild(null, $"cannot write the build output or cache ({CacheDirectory}): {e.Message}");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return new NativeBuild(null, $"cannot run the C compiler `{string.Join(" ", FindCompiler() ?? [])}`: {e.Message}");
        }
    }

    private static NativeBuild BuildCached(string cSource, BuildMode mode, string? output)
    {
        var compiler = FindCompiler();
        if (compiler is null)
            return new NativeBuild(null, "no C compiler found: install zig (https://ziglang.org) or set BAYAN_CC");
        var key = Hash(cSource + "\0" + RuntimeHeader + "\0" + RuntimeSource + "\0" + mode + "\0" + string.Join(" ", compiler));
        var dir = Path.Combine(CacheDirectory, key[..16]);
        var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "program.exe" : "program");
        if (!File.Exists(exe))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), RuntimeHeader);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "main.c"), cSource);
            var temp = exe + "." + Environment.ProcessId + ".tmp" + (OperatingSystem.IsWindows() ? ".exe" : "");
            var args = new List<string>(compiler[1..])
            {
                "-std=c11", "-fwrapv", "-ffp-contract=off", "-w",
                "-I", dir, Path.Combine(dir, "main.c"), Path.Combine(dir, "bayan-rt.c"), "-o", temp,
            };
            args.AddRange(mode switch
            {
                BuildMode.Debug => ["-O0", "-g"],
                BuildMode.Safe => ["-O2"],
                _ => ["-O2", "-DBYN_FAST"],
            });
            if (!OperatingSystem.IsWindows()) args.Add("-lm");
            var (code, stdout, stderr) = Exec(compiler[0], args);
            if (code != 0 || !File.Exists(temp))
                return new NativeBuild(null, $"C compiler failed (exit {code}):\n{stdout}{stderr}");
            try
            {
                File.Move(temp, exe, overwrite: true);
            }
            catch (IOException) when (File.Exists(exe))
            {
                // Another build of the same program won the race.
            }
            if (File.Exists(temp)) File.Delete(temp);
            foreach (var extra in Directory.EnumerateFiles(dir, "program*.pdb")) File.Delete(extra);
        }
        if (output is null) return new NativeBuild(exe, "");
        File.Copy(exe, output, overwrite: true);
        return new NativeBuild(output, "");
    }

    private static (int Code, string Stdout, string Stderr) Exec(string file, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Resource(string name)
    {
        using var stream = typeof(Toolchain).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
