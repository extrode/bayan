using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Backend.C;
using Bayan.Syntax.Packages;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan dist</c> writes immutable release builds to <c>dist/&lt;version&gt;/&lt;target&gt;/</c>
/// with <c>SHA256SUMS</c> and <c>BUILDINFO.json</c>, then refreshes <c>dist/latest/</c> as a copy
/// of the highest version. <c>bayan clean</c> removes <c>out/</c>, and on request <c>dist/</c> and
/// the build cache. Git use is read-only.
/// </summary>
internal static class Dist
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? path = null, targetArg = null, modeArg = null;
        bool force = false, allowDirty = false, json = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target" when i + 1 < args.Length: targetArg = args[++i]; break;
                case "--mode" when i + 1 < args.Length: modeArg = args[++i]; break;
                case "--force": force = true; break;
                case "--allow-dirty": allowDirty = true; break;
                case "--diagnostics" when i + 1 < args.Length && args[i + 1] is "json" or "text":
                    json = args[++i] == "json";
                    break;
                case var a when a.StartsWith('-') || path is not null:
                    stderr.WriteLine(Messages.UnexpectedArgument.Text(a));
                    stderr.WriteLine(Messages.CommandUsage.Text("bayan dist [<dir>] [--target <t>|all] [--mode debug|safe|fast] [--force] [--allow-dirty]"));
                    return CliApp.ExitUsage;
                default: path = args[i]; break;
            }
        }
        path ??= ".";
        if (!File.Exists(Path.Combine(path, "package.byn")))
        {
            stderr.WriteLine(Messages.DistNotProject.Text(path, "package.byn"));
            return CliApp.ExitUsage;
        }
        var buildMode = BuildMode.Safe;
        if (modeArg is not null && !Toolchain.TryParseMode(modeArg, out buildMode))
        {
            stderr.WriteLine(Messages.UnknownMode.Text(modeArg));
            return CliApp.ExitUsage;
        }

        var o = new CliApp.BuildOptions(path, buildMode, null, false, json, []);
        if (CliApp.Compile(o, stderr) is not { } compiled) return CliApp.ExitFailure;
        var manifest = compiled.Manifest!;
        var root = compiled.ProjectRoot!;
        if (manifest.Kind == PackageKind.Lib)
        {
            stderr.WriteLine(Messages.DistLibrary.Text(manifest.Name));
            return CliApp.ExitUsage;
        }
        if (modeArg is null) Toolchain.TryParseMode(manifest.DistMode, out buildMode);
        if (ResolveTargets(targetArg, manifest, stderr) is not { } targets) return CliApp.ExitUsage;

        var source = ReadSource(root);
        if (source is { Dirty: true } && !allowDirty)
        {
            stderr.WriteLine(Messages.DistDirty.Text());
            stderr.WriteLine(Messages.DistDirtyHelp.Text("--allow-dirty", "BUILDINFO.json"));
            return CliApp.ExitUsage;
        }

        var version = manifest.Version;
        var distDir = Path.Combine(root, "dist");
        var versionDir = Path.Combine(distDir, version);
        if (Directory.Exists(versionDir) && !force)
        {
            stderr.WriteLine(Messages.DistExists.Text($"dist/{version}/"));
            stderr.WriteLine(Messages.DistExistsHelp.Text("version", "package.byn", "--force"));
            return CliApp.ExitUsage;
        }

        var cSource = CliApp.EmitC(compiled.Compilation, buildMode);
        var staging = Path.Combine(distDir, $".{version}.tmp");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        var sums = new List<(string Name, string Hash)>();
        foreach (var target in targets)
        {
            var exe = $"{target}/{Targets.ExecutableName(manifest.ShortName, target)}";
            var output = Path.Combine(staging, exe);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (CliApp.BuildNative(cSource, buildMode, stderr, output, target, json) is null)
            {
                Directory.Delete(staging, recursive: true);
                return CliApp.ExitToolchain;
            }
            sums.Add((exe, Sha256(File.ReadAllBytes(output))));
        }
        File.WriteAllText(Path.Combine(staging, "SHA256SUMS"), string.Concat(sums.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => $"{s.Hash}  {s.Name}\n")));
        File.WriteAllText(Path.Combine(staging, "BUILDINFO.json"), BuildInfo(manifest, CliApp.ModeName(buildMode), targets, source, InputsHash(root)));
        if (Directory.Exists(versionDir)) Directory.Delete(versionDir, recursive: true);
        Directory.Move(staging, versionDir);
        var latest = RefreshLatest(distDir);
        stdout.WriteLine(Messages.DistBuilt.Text(manifest.Name, version, CliApp.ModeName(buildMode), string.Join(", ", targets), $"dist/{version}/"));
        if (latest is not null) stdout.WriteLine(Messages.DistLatest.Text("dist/latest/", latest));
        return CliApp.ExitOk;
    }

    /// <summary><c>--target</c> (a name, <c>host</c> or <c>all</c>), else <c>dist.targets</c>, else the host.</summary>
    private static List<string>? ResolveTargets(string? arg, Manifest manifest, TextWriter stderr)
    {
        IEnumerable<string> names = arg switch
        {
            "all" => Targets.All,
            null => manifest.DistTargets.Count > 0 ? manifest.DistTargets : ["host"],
            _ => [arg],
        };
        var targets = new List<string>();
        foreach (var name in names)
        {
            var target = name == "host" ? CliApp.HostTarget : name;
            if (!Targets.IsKnown(target))
            {
                stderr.WriteLine(Messages.UnknownDistTarget.Text(name, string.Join(", ", Targets.All)));
                return null;
            }
            if (!targets.Contains(target)) targets.Add(target);
        }
        return targets;
    }

    private sealed record SourceState(string? Commit, bool Dirty);

    /// <summary>The commit and dirtiness of the git work tree holding <paramref name="root"/>; null outside one or when it ignores the project.</summary>
    private static SourceState? ReadSource(string root)
    {
        if (Toolchain.FindOnPath("git") is not { } git) return null;
        if (Scaffold.Git(git, root, "rev-parse", "--is-inside-work-tree") is not (0, "true")) return null;
        // A project the enclosing repository ignores isn't in any of its commits.
        if (Scaffold.Git(git, root, "check-ignore", "-q", "package.byn") is (0, _)) return null;
        var commit = Scaffold.Git(git, root, "rev-parse", "HEAD") is (0, var sha) ? sha : null;
        var dirty = Scaffold.Git(git, root, "status", "--porcelain", "--", ".") is not (0, "");
        return new SourceState(commit, dirty);
    }

    /// <summary>SHA-256 over <c>package.byn</c> and every <c>.byn</c> under <c>src/</c>, by relative path.</summary>
    internal static string InputsHash(string root)
    {
        var files = new List<string> { Path.Combine(root, "package.byn") };
        var src = Path.Combine(root, "src");
        if (Directory.Exists(src)) files.AddRange(Directory.EnumerateFiles(src, "*.byn", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }));
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relative, file) in files.Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), f)).OrderBy(f => f.Item1, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            sha.AppendData(File.ReadAllBytes(file));
            sha.AppendData([0]);
        }
        return "sha256:" + Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string BuildInfo(Manifest manifest, string mode, List<string> targets, SourceState? source, string inputs)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("package", manifest.Name);
            w.WriteString("version", manifest.Version);
            w.WriteString("compiler", "bayan " + CliApp.Version);
            w.WriteString("mode", mode);
            w.WriteStartArray("targets");
            foreach (var t in targets) w.WriteStringValue(t);
            w.WriteEndArray();
            if (source is null) w.WriteNull("source");
            else
            {
                w.WriteStartObject("source");
                if (source.Commit is null) w.WriteNull("commit");
                else w.WriteString("commit", source.Commit);
                w.WriteBoolean("dirty", source.Dirty);
                w.WriteEndObject();
            }
            w.WriteString("inputs", inputs);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>
    /// Rebuilds <c>dist/latest/</c> as a copy of the highest version present (by semantic
    /// version, not build time), staged in <c>dist/.latest.tmp/</c> and renamed into place.
    /// </summary>
    internal static string? RefreshLatest(string distDir)
    {
        var newest = Directory.EnumerateDirectories(distDir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && Manifest.IsSemanticVersion(n))
            .Select(n => n!)
            .Aggregate((string?)null, (best, n) => best is null || Manifest.CompareVersions(n, best) > 0 ? n : best);
        if (newest is null) return null;
        var staging = Path.Combine(distDir, ".latest.tmp");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        CopyDirectory(Path.Combine(distDir, newest), staging);
        File.WriteAllText(Path.Combine(staging, "VERSION"), newest + "\n");
        var latest = Path.Combine(distDir, "latest");
        if (Directory.Exists(latest)) Directory.Delete(latest, recursive: true);
        Directory.Move(staging, latest);
        return newest;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            File.Copy(file, dest);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
        }
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    public static int Clean(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? path = null;
        bool dist = false, cache = false;
        foreach (var a in args)
        {
            switch (a)
            {
                case "--dist": dist = true; break;
                case "--cache": cache = true; break;
                case var x when x.StartsWith('-') || path is not null:
                    stderr.WriteLine(Messages.UnexpectedArgument.Text(x));
                    stderr.WriteLine(Messages.CommandUsage.Text("bayan clean [<dir>] [--dist] [--cache]"));
                    return CliApp.ExitUsage;
                default: path = a; break;
            }
        }
        var project = File.Exists(Path.Combine(path ?? ".", "package.byn"));
        if (!project && (path is not null || dist || !cache))
        {
            stderr.WriteLine(Messages.CleanNotProject.Text(path ?? ".", "package.byn", "out/"));
            return CliApp.ExitUsage;
        }
        if (project)
        {
            var root = Path.GetFullPath(path ?? ".");
            Remove(Path.Combine(root, "out"), "out/", stdout);
            if (dist) Remove(Path.Combine(root, "dist"), "dist/", stdout);
        }
        if (cache) Remove(Toolchain.CacheData, Toolchain.CacheData, stdout);
        return CliApp.ExitOk;
    }

    private static void Remove(string dir, string name, TextWriter stdout)
    {
        if (!Directory.Exists(dir)) return;
        Directory.Delete(dir, recursive: true);
        stdout.WriteLine(Messages.CleanRemoved.Text(name));
    }
}
