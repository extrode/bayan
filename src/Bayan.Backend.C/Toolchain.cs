using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Bayan.Backend.C;

/// <summary>
/// <c>debug</c>: no optimization, debug info, checked arithmetic. <c>safe</c> (default): optimized
/// and checked. <c>fast</c>: optimized; <c>+ - *</c> and narrowing casts wrap instead of trapping.
/// </summary>
public enum BuildMode { Debug, Safe, Fast }

/// <summary>The executable, or the C compiler's complaint; <see cref="CacheHit"/> when nothing was compiled.</summary>
public sealed record NativeBuild(string? ExecutablePath, string Errors, bool CacheHit = false, string? Key = null)
{
    public bool Succeeded => ExecutablePath is not null;
}

/// <summary>What <c>bayan cache stats</c> reports.</summary>
public sealed record CacheStats(string Directory, int Programs, long Bytes, int Scripts);

/// <summary>
/// Compiles generated C with the runtime into an executable, cached by content in a directory
/// shared by every project, worktree and process on the machine (§21.4). An entry is
/// <c>v1/programs/&lt;key&gt;/</c> holding the executable and its SHA-256; it is built in a private
/// temporary directory and renamed into place, so readers never see a partial entry and no lock
/// is taken. An entry whose executable no longer matches its hash is rebuilt.
/// </summary>
public static class Toolchain
{
    /// <summary>The Bayan compiler's version; part of every cache key.</summary>
    public const string CompilerVersion = "0.1.0-dev";

    public static string RuntimeHeader { get; } = Resource("runtime/bayan-rt.h");

    public static string RuntimeSource { get; } = Resource("runtime/bayan-rt.c");

    /// <summary>
    /// <c>$BAYAN_CACHE</c>; else <c>%LOCALAPPDATA%\bayan\cache</c> on Windows,
    /// <c>~/Library/Caches/bayan</c> on macOS, <c>$XDG_CACHE_HOME/bayan</c> or
    /// <c>~/.cache/bayan</c> elsewhere.
    /// </summary>
    public static string CacheDirectory
    {
        get
        {
            if (Environment.GetEnvironmentVariable("BAYAN_CACHE") is { Length: > 0 } dir) return dir;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bayan", "cache");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Caches", "bayan");
            if (Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg))
                return Path.Combine(xdg, "bayan");
            return Path.Combine(home, ".cache", "bayan");
        }
    }

    /// <summary>Everything this version of the cache layout writes: <c>&lt;cache&gt;/v1</c>.</summary>
    public static string CacheData => Path.Combine(CacheDirectory, "v1");

    private static string Programs => Path.Combine(CacheData, "programs");

    private static string Scripts => Path.Combine(CacheData, "scripts");

    private static string Staging => Path.Combine(CacheData, "tmp");

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

    /// <summary>
    /// The C compiler for <c>sanitize</c> builds: <c>/usr/bin/clang</c> if present, else <c>clang</c>
    /// from <c>PATH</c>; zig cc ships no AddressSanitizer runtime.
    /// </summary>
    public static string? SanitizerCompiler { get; } =
        File.Exists("/usr/bin/clang") ? "/usr/bin/clang" : FindOnPath("clang");

    /// <summary>Whether <c>sanitize</c> builds get AddressSanitizer on this machine.</summary>
    public static bool AsanAvailable => SanitizerCompiler is not null;

    public static string? FindOnPath(string name)
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
    /// Compiles <paramref name="cSource"/> to an executable in the cache (reused when the compiler
    /// version, source, runtime, mode, target and C compiler are unchanged) and copies it to
    /// <paramref name="output"/> if given. A <paramref name="target"/> other than the host needs zig.
    /// <paramref name="sanitize"/> builds for the host with AddressSanitizer when <see cref="AsanAvailable"/>,
    /// and is ignored otherwise.
    /// </summary>
    public static NativeBuild Build(string cSource, BuildMode mode, string? output = null, string? target = null, bool sanitize = false)
    {
        try
        {
            return BuildCached(cSource, mode, output, target ?? Targets.Host, sanitize);
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

    private static NativeBuild BuildCached(string cSource, BuildMode mode, string? output, string? target, bool sanitize)
    {
        var cross = target != Targets.Host;
        var asan = sanitize && !cross && SanitizerCompiler is not null;
        string[]? compiler = asan ? [SanitizerCompiler!] : FindCompiler();
        if (compiler is null)
            return new NativeBuild(null, "no C compiler found: install zig (https://ziglang.org) or set BAYAN_CC");
        if (cross && (target is null || Targets.Triple(target) is null))
            return new NativeBuild(null, $"unknown target `{target}`; targets are {string.Join(", ", Targets.All)}");
        if (cross && !(compiler.Length > 1 && compiler[1] == "cc" && Path.GetFileNameWithoutExtension(compiler[0]) == "zig"))
            return new NativeBuild(null, $"building for {target} on this machine needs zig (https://ziglang.org); the C compiler is `{string.Join(" ", compiler)}`");
        var targetOs = target ?? (OperatingSystem.IsWindows() ? "windows" : "");
        var exeName = Targets.ExecutableName("program", targetOs);
        var flags = new List<string>();
        if (cross) flags.AddRange(["-target", Targets.Triple(target!)!]);
        flags.AddRange(["-std=c11", "-fwrapv", "-ffp-contract=off", "-w"]);
        flags.AddRange(mode switch
        {
            BuildMode.Debug => ["-O0", "-g"],
            BuildMode.Safe => ["-O2"],
            _ => ["-O2", "-DBYN_FAST"],
        });
        if (asan) flags.AddRange(["-fsanitize=address", "-fno-omit-frame-pointer", "-g"]);
        var key = Hash(string.Join("\0", CompilerVersion, mode, target, string.Join(" ", compiler), string.Join(" ", flags), RuntimeHeader, RuntimeSource, cSource));
        var hit = Lookup(key, exeName) is not null;
        if (!hit)
        {
            var failure = Compile(compiler, flags, cSource, key, exeName, windows: targetOs.StartsWith("windows", StringComparison.Ordinal));
            if (failure is not null) return new NativeBuild(null, failure);
        }
        var exe = Path.Combine(Programs, key, exeName);
        if (output is null) return new NativeBuild(exe, "", hit, key);
        ReplaceFile(output, temp => File.Copy(exe, temp));
        return new NativeBuild(output, "", hit, key);
    }

    /// <summary>
    /// Writes <paramref name="path"/> through a temporary file beside it and a rename, so
    /// concurrent builds into one <c>out/</c> never interleave and readers never see a partial file.
    /// </summary>
    public static void ReplaceFile(string path, Action<string> write)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            write(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>The cached executable for <paramref name="key"/> if it is intact; a damaged entry is removed.</summary>
    public static string? Lookup(string key, string exeName)
    {
        var dir = Path.Combine(Programs, key);
        var exe = Path.Combine(dir, exeName);
        var sum = Path.Combine(dir, "program.sha256");
        if (!Directory.Exists(dir)) return null;
        try
        {
            if (File.Exists(exe) && File.Exists(sum) && File.ReadAllText(sum).Trim() == HashFile(exe)) return exe;
        }
        catch (IOException)
        {
            // Unreadable counts as damaged.
        }
        Discard(dir);
        return null;
    }

    /// <summary>Builds into a private directory, then renames it to <c>programs/&lt;key&gt;</c>; null on success.</summary>
    private static string? Compile(string[] compiler, List<string> flags, string cSource, string key, string exeName, bool windows)
    {
        var work = Path.Combine(Staging, key[..16] + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            File.WriteAllText(Path.Combine(work, "bayan-rt.h"), RuntimeHeader);
            File.WriteAllText(Path.Combine(work, "bayan-rt.c"), RuntimeSource);
            File.WriteAllText(Path.Combine(work, "main.c"), cSource);
            var args = new List<string>(compiler[1..]);
            args.AddRange(flags);
            // Debug info names `main.c`, not this temporary directory.
            args.AddRange(["-ffile-prefix-map=" + work + "=.", "-I", ".", "main.c", "bayan-rt.c", "-o", exeName]);
            // `CommandLineToArgvW` (program arguments) is in shell32.
            args.Add(windows ? "-lshell32" : "-lm");
            var (code, stdout, stderr) = Exec(compiler[0], args, work);
            var exe = Path.Combine(work, exeName);
            if (code != 0 || !File.Exists(exe))
                return $"C compiler failed (exit {code}):\n{stdout}{stderr}";
            foreach (var file in Directory.EnumerateFiles(work))
                if (Path.GetFileName(file) != exeName) File.Delete(file);
            File.WriteAllText(Path.Combine(work, "program.sha256"), HashFile(exe) + "\n");
            Directory.CreateDirectory(Programs);
            try
            {
                Directory.Move(work, Path.Combine(Programs, key));
            }
            catch (IOException) when (Lookup(key, exeName) is not null)
            {
                // Another build of the same program won the race; its entry is as good as ours.
            }
            return null;
        }
        finally
        {
            if (Directory.Exists(work)) Discard(work);
        }
    }

    /// <summary>Renames <paramref name="dir"/> out of the way first, so a half-deleted entry is never visible.</summary>
    private static void Discard(string dir)
    {
        var trash = Path.Combine(Staging, "trash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Staging);
            Directory.Move(dir, trash);
            Directory.Delete(trash, recursive: true);
        }
        catch (IOException)
        {
            // Someone else moved or removed it.
        }
    }

    /// <summary>
    /// The program a script last built to, for <c>bayan run script.byn</c> to start without
    /// compiling; <paramref name="scriptKey"/> covers the script's bytes, mode and C compiler.
    /// </summary>
    public static string? LookupScript(string scriptKey, string exeName)
    {
        var pointer = Path.Combine(Scripts, scriptKey);
        try
        {
            return File.Exists(pointer) ? Lookup(File.ReadAllText(pointer).Trim(), exeName) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void RememberScript(string scriptKey, string programKey)
    {
        try
        {
            Directory.CreateDirectory(Scripts);
            var temp = Path.Combine(Scripts, scriptKey + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, programKey + "\n");
            File.Move(temp, Path.Combine(Scripts, scriptKey), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A script that isn't remembered just compiles again next time.
        }
    }

    /// <summary>
    /// The key under which a script's program is remembered: compiler version and build, mode, host
    /// and the script's bytes (its <c>#:</c> directives included). The C compiler isn't part of it,
    /// so a remembered script starts even when no C compiler is installed.
    /// </summary>
    public static string ScriptKey(byte[] script, BuildMode mode, string? build = null) =>
        Hash(string.Join("\0", "script", CompilerVersion, build ?? CompilerBuild, mode, Targets.Host, Convert.ToHexStringLower(SHA256.HashData(script))));

    /// <summary>
    /// Size and last-write time of the running compiler and its <c>Bayan*.dll</c> assemblies (any
    /// case; none under NativeAOT), so a rebuilt dev compiler doesn't start scripts the old one
    /// remembered.
    /// </summary>
    public static string CompilerBuild { get; } = ComputeCompilerBuild();

    private static string ComputeCompilerBuild()
    {
        var files = new List<string>();
        if (Environment.ProcessPath is { } exe) files.Add(exe);
        if (Directory.Exists(AppContext.BaseDirectory))
            files.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "Bayan*.dll", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Order(StringComparer.Ordinal));
        var parts = files.Select(f => new FileInfo(f)).Where(f => f.Exists).Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}");
        return string.Join(";", parts.Prepend(CompilerVersion));
    }

    public static CacheStats Stats()
    {
        int programs = 0, scripts = 0;
        long bytes = 0;
        if (Directory.Exists(Programs))
            foreach (var entry in Directory.EnumerateDirectories(Programs))
            {
                programs++;
                foreach (var file in Directory.EnumerateFiles(entry)) bytes += new FileInfo(file).Length;
            }
        if (Directory.Exists(Scripts)) scripts = Directory.EnumerateFiles(Scripts).Count(f => !f.EndsWith(".tmp", StringComparison.Ordinal));
        return new CacheStats(CacheDirectory, programs, bytes, scripts);
    }

    private static (int Code, string Stdout, string Stderr) Exec(string file, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string HashFile(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    private static string Resource(string name)
    {
        using var stream = typeof(Toolchain).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
