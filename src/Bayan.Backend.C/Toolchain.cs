using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Bayan.Backend.C;

/// <summary>
/// <c>debug</c>: no optimization, debug info, checked arithmetic. <c>safe</c> (default): optimized
/// and checked. <c>fast</c>: optimized; <c>+ - *</c> and narrowing casts wrap instead of trapping.
/// </summary>
public enum BuildMode { Debug, Safe, Fast }

/// <summary>The executable, or the C compiler's complaint; <see cref="CacheHit"/> when nothing was compiled.
/// <see cref="Rejected"/>: the C compiler ran and refused the generated C, a Bayan compiler bug (BYN4001).</summary>
public sealed record NativeBuild(string? ExecutablePath, string Errors, bool CacheHit = false, string? Key = null, bool Rejected = false)
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
    public const string CompilerVersion = "0.2.0-dev";

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

    /// <summary>The CACHEDIR.TAG signature (bford.info/cachedir): backup tools and worktree removal treat a tagged
    /// directory as a disposable cache, so the program cache under a checkout never blocks or bloats.</summary>
    internal const string CacheDirTag = "Signature: 8a477f597d28d172789f06886806bc55\n# This file is a cache directory tag created by bayan.\n# For information about cache directory tags see https://bford.info/cachedir/\n";

    /// <summary>Writes <c>CACHEDIR.TAG</c> in <see cref="CacheData"/> once: only the directory bayan owns, never the
    /// <c>$BAYAN_CACHE</c> root a user may share, so `bayan clean --cache` removes the tag with the cache. Written to a temporary
    /// name and moved into place, so no reader sees a partial tag; a cache that cannot be tagged still works.</summary>
    internal static void TagCacheDirectory()
    {
        var tag = Path.Combine(CacheData, "CACHEDIR.TAG");
        if (File.Exists(tag)) return;
        var temp = tag + "." + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(CacheData);
            File.WriteAllText(temp, CacheDirTag);
            File.Move(temp, tag, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
        }
    }

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
    /// The C compiler for <c>sanitize</c> builds, probed once: <c>$BAYAN_CC</c> if it can build and run an
    /// AddressSanitizer program, else <c>/usr/bin/clang</c>, else <c>clang</c> from <c>PATH</c>; zig cc
    /// ships no AddressSanitizer runtime. Null when none works.
    /// </summary>
    public static string[]? SanitizerCompiler => sanitizer.Value;

    private static readonly Lazy<string[]?> sanitizer = new(() => ProbeSanitizer(SanitizerCandidates()));

    /// <summary>Test seam: replaces the probed sanitizer compiler; null restores the probe.</summary>
    internal static string[]? SanitizerOverride { get; set; }

    /// <summary>Whether <c>sanitize</c> builds get AddressSanitizer on this machine.</summary>
    public static bool AsanAvailable => (SanitizerOverride ?? SanitizerCompiler) is not null;

    private static IEnumerable<string[]> SanitizerCandidates()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_CC") is { Length: > 0 } cc)
            yield return cc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (File.Exists("/usr/bin/clang")) yield return ["/usr/bin/clang"];
        if (FindOnPath("clang") is { } clang) yield return [clang];
    }

    /// <summary>How long one C compiler run may take: 10 minutes, or <c>$BAYAN_CC_TIMEOUT_S</c> seconds.</summary>
    private static TimeSpan CompileTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("BAYAN_CC_TIMEOUT_S"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromMinutes(10);

    /// <summary>Test seams: how long the probe waits for a candidate to compile, and for its program to run.</summary>
    internal static TimeSpan ProbeCompileTimeout { get; set; } = TimeSpan.FromSeconds(120);

    internal static TimeSpan ProbeRunTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The first of <paramref name="candidates"/> that compiles, links and runs an AddressSanitizer program;
    /// one that takes too long is killed and rejected.</summary>
    internal static string[]? ProbeSanitizer(IEnumerable<string[]> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0) continue;
            var work = Path.Combine(Path.GetTempPath(), "bayan-asan-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                File.WriteAllText(Path.Combine(work, "probe.c"), "int main(void) { return 0; }\n");
                var exe = Path.Combine(work, OperatingSystem.IsWindows() ? "probe.exe" : "probe");
                var args = new List<string>(candidate[1..]) { "-fsanitize=address", "probe.c", "-o", exe };
                if (Exec(candidate[0], args, work, ProbeCompileTimeout).Code == 0 && File.Exists(exe) && Exec(exe, [], work, ProbeRunTimeout).Code == 0)
                    return candidate;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                // This candidate cannot do it; try the next.
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

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
    }

    private static NativeBuild BuildCached(string cSource, BuildMode mode, string? output, string? target, bool sanitize)
    {
        SweepOnce();
        var cross = target != Targets.Host;
        var sanitizerCompiler = sanitize && !cross ? SanitizerOverride ?? SanitizerCompiler : null;
        var asan = sanitizerCompiler is not null;
        var compiler = sanitizerCompiler ?? FindCompiler();
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
            BuildMode.Debug => ["-O0", "-g", "-DBYN_DEBUG"],
            BuildMode.Safe => ["-O2", "-DBYN_NDEBUG"],
            _ => ["-O2", "-DBYN_NDEBUG"],
        });
        if (asan) flags.AddRange(["-fsanitize=address", "-fno-omit-frame-pointer", "-g"]);
        var key = Hash(string.Join("\0", CompilerVersion, mode, target, string.Join(" ", compiler), string.Join(" ", flags), RuntimeHeader, RuntimeSource, cSource));
        var hit = Lookup(key, exeName) is not null;
        if (!hit)
        {
            try
            {
                var failure = Compile(compiler, flags, cSource, key, exeName, windows: targetOs.StartsWith("windows", StringComparison.Ordinal));
                if (failure is not null) return new NativeBuild(null, failure, Rejected: true);
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                return new NativeBuild(null, $"cannot run the C compiler `{string.Join(" ", compiler)}`: {e.Message}");
            }
        }
        if (hit) TagCacheDirectory();
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
            const int maxTries = 50;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    break;
                }
                catch (Exception ex) when (attempt < maxTries && (ex is UnauthorizedAccessException || ex is IOException))
                {
                    Thread.Sleep(Math.Min(attempt * 2, 100));
                }
            }
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
            if (File.Exists(exe) && File.Exists(sum) && File.ReadAllText(sum).Trim() == HashFile(exe))
            {
                Touch(dir);
                return exe;
            }
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
        TagCacheDirectory();
        var work = Path.Combine(Staging, key[..16] + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            File.WriteAllText(Path.Combine(work, "bayan-rt.h"), RuntimeHeader);
            File.WriteAllText(Path.Combine(work, "bayan-rt.c"), RuntimeSource);
            File.WriteAllText(Path.Combine(work, CEmitter.GeneratedFile), cSource);
            var args = new List<string>(compiler[1..]);
            args.AddRange(flags);
            // Debug info names `main.c`, not this temporary directory.
            args.AddRange(["-ffile-prefix-map=" + work + "=.", "-I", ".", CEmitter.GeneratedFile, "bayan-rt.c", "-o", exeName]);
            // `CommandLineToArgvW` (program arguments) is in shell32.
            args.Add(windows ? "-lshell32" : "-lm");
            var limit = CompileTimeout;
            var (code, stdout, stderr) = Exec(compiler[0], args, work, limit);
            if (code == -1)
                return $"C compiler timed out after {limit.TotalSeconds:0} s and was killed (set BAYAN_CC_TIMEOUT_S to allow longer)";
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
                Evict(key);
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

    private const long DefaultCacheMaxMb = 2048;
    private const int DefaultCacheMaxEntries = 4000;
    private static readonly TimeSpan InUseWindow = TimeSpan.FromMinutes(10);

    /// <summary>Marks a cache entry as just used; failure is harmless.</summary>
    private static void Touch(string dir)
    {
        try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Trims the program cache to <c>$BAYAN_CACHE_MAX_MB</c> (2 GiB; 0 for no cap) and 4000 entries, least recently
    /// used first, then drops script pointers whose program is gone. <paramref name="keep"/> and any entry used
    /// within <paramref name="minAge"/> (a concurrent build may be running it) are never removed.
    /// </summary>
    private static void Evict(string keep)
    {
        var mb = long.TryParse(Environment.GetEnvironmentVariable("BAYAN_CACHE_MAX_MB"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : DefaultCacheMaxMb;
        if (mb == 0) return;
        Evict(keep, mb * 1024 * 1024, DefaultCacheMaxEntries, InUseWindow);
    }

    internal static void Evict(string keep, long maxBytes, int maxEntries, TimeSpan minAge)
    {
        try
        {
            var entries = Directory.EnumerateDirectories(Programs).Select(d => new DirectoryInfo(d))
                .Select(d => (Dir: d, Used: d.LastWriteTimeUtc, Size: d.EnumerateFiles().Sum(f => f.Length)))
                .OrderBy(e => e.Used).ToList();
            var count = entries.Count;
            var bytes = entries.Sum(e => e.Size);
            var cutoff = DateTime.UtcNow - minAge;
            foreach (var e in entries)
            {
                if (count <= maxEntries && bytes <= maxBytes) break;
                if (e.Dir.Name == keep || e.Used > cutoff) continue;
                Discard(e.Dir.FullName);
                count--;
                bytes -= e.Size;
            }
            if (!Directory.Exists(Scripts)) return;
            foreach (var pointer in Directory.EnumerateFiles(Scripts).Where(f => !f.EndsWith(".tmp", StringComparison.Ordinal)))
                if (!Directory.Exists(Path.Combine(Programs, File.ReadAllText(pointer).Trim()))) File.Delete(pointer);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static int s_swept;

    /// <summary>Sweeps stale temporary directories once per process, before the first build.</summary>
    private static void SweepOnce()
    {
        if (Interlocked.Exchange(ref s_swept, 1) == 0) SweepStale(Path.GetTempPath(), TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Removes what an interrupted run left behind and nothing cleans up later: <c>bayan-coverage-*</c>,
    /// <c>bayan-mutate-*</c> and <c>bayan-asan-*</c> under <paramref name="tempRoot"/>, and everything in the cache's
    /// staging directory, when last modified more than <paramref name="age"/> ago (staging: more than max(<paramref name="age"/>,
    /// compile timeout + 10 minutes), so a slow compile in progress is kept). Errors are ignored.
    /// </summary>
    internal static void SweepStale(string tempRoot, TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        var stagingCutoff = DateTime.UtcNow - TimeSpan.FromTicks(Math.Max(age.Ticks, (CompileTimeout + TimeSpan.FromMinutes(10)).Ticks));
        var stale = new List<(string Path, DateTime Cutoff)>();
        try
        {
            foreach (var pattern in new[] { "bayan-coverage-*", "bayan-mutate-*", "bayan-asan-*" })
                if (Directory.Exists(tempRoot)) stale.AddRange(Directory.EnumerateDirectories(tempRoot, pattern).Select(d => (d, cutoff)));
            if (Directory.Exists(Staging)) stale.AddRange(Directory.EnumerateFileSystemEntries(Staging).Select(d => (d, stagingCutoff)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var (path, limit) in stale)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(path) >= limit) continue;
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
            TagCacheDirectory();
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

    /// <summary>Runs <paramref name="file"/>; past <paramref name="timeout"/> it is killed and the code is -1.</summary>
    private static (int Code, string Stdout, string Stderr) Exec(string file, IEnumerable<string> args, string workingDirectory, TimeSpan? timeout = null)
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
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout ?? Timeout.InfiniteTimeSpan))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            p.WaitForExit();
            return (-1, "", "");
        }
        return (p.ExitCode, stdout.Result, stderr.Result);
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
