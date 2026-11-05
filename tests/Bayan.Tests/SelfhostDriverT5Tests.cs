using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// 012 T5: <c>build</c>, <c>build --emit-c</c> and <c>run</c> of the driver against the C# <c>bayan</c> (R2, R6, R7). The C# side
/// is <see cref="CliApp.Run"/>: in-process where the paths are absolute and no child process prints, else <c>dotnet Bayan.Cli.dll</c>
/// (its <c>Program.cs</c> calls <c>CliApp.Run</c>) in the same working directory as the driver, because <c>run</c>'s program
/// inherits the C# process's streams (CliApp.cs:573) and default output names are relative to the working directory.
/// Masked: the elapsed seconds, and the C#'s ` · from cache` (the driver has no cache, R6).
/// </summary>
public sealed class SelfhostDriverT5Tests
{
    static SelfhostDriverT5Tests() =>
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));

    private static string Mask(string text) =>
        Regex.Replace(Regex.Replace(Regex.Replace(text, @"\d+\.\d\ds|from cache", "masked"), @" · masked", ""), @"^(bayan \S+) \(.*\)$", "$1 (masked)", RegexOptions.Multiline);

    private static (int Exit, string Out, string Err) InProcess(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static (int Exit, byte[] Out, string Err) Process(string program, string[]? prefix, string cwd, string[] args, string? path = null)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd };
        if (path is not null)
        {
            psi.Environment["PATH"] = path;
            psi.Environment.Remove("BAYAN_CC");
        }
        foreach (var a in prefix ?? []) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result.ReplaceLineEndings("\n"));
    }

    private static (int Exit, byte[] Out, string Err) Cs(string cwd, params string[] args) =>
        Process("dotnet", [typeof(CliApp).Assembly.Location], cwd, args);

    private static (int Exit, byte[] Out, string Err) Self(string cwd, params string[] args) =>
        Process(SelfhostDriverHarness.Build(), null, cwd, args);

    private static string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    /// <summary>A difference as `label: what`, or null when exit code and both streams are equal after <see cref="Mask"/>.</summary>
    private static string? Differs(string label, (int Exit, byte[] Out, string Err) expected, (int Exit, byte[] Out, string Err) actual)
    {
        if (expected.Exit != actual.Exit) return $"{label}: exit {expected.Exit} != {actual.Exit}\n{actual.Err}";
        if (Mask(Text(expected.Out)) != Mask(Text(actual.Out))) return $"{label}: stdout differs\n--- C#\n{Text(expected.Out)}\n--- driver\n{Text(actual.Out)}";
        if (Mask(expected.Err) != Mask(actual.Err)) return $"{label}: stderr differs\n--- C#\n{expected.Err}\n--- driver\n{actual.Err}";
        return null;
    }

    /// <summary>The `run/` inputs RunTests runs as programs (no `.test.byn`, no `.stdin`, not skipped on this OS).</summary>
    private static IEnumerable<string> RunInputs()
    {
        foreach (string name in Golden.Cases("run"))
        {
            var input = Path.Combine(Golden.CasesRoot, name);
            if (name.EndsWith(".test.byn", StringComparison.Ordinal) || File.Exists(Path.ChangeExtension(input, ".stdin"))) continue;
            var head = File.ReadLines(input).Take(2).ToList();
            if (head.Contains("// os: unix") && OperatingSystem.IsWindows()) continue;
            if (head.Contains("// os: windows") && !OperatingSystem.IsWindows()) continue;
            yield return name;
        }
    }

    /// <summary>`--mode m` from a `// mode: m` first line (RunTests.cs:78), else nothing.</summary>
    private static string[] ModeArgs(string input)
    {
        var first = File.ReadLines(input).FirstOrDefault() ?? "";
        return first.StartsWith("// mode: ", StringComparison.Ordinal) ? ["--mode", first["// mode: ".Length..].Trim()] : [];
    }

    /// <summary>
    /// `name` copied alone into a fresh `tmp/t5/<side>/<case>/`: a path compiles its whole directory (CliApp.cs:286). Each side
    /// runs on a fresh copy at the same path, so the paths in diagnostics and `#line` agree.
    /// </summary>
    private static string Isolated(string name, string side)
    {
        var source = Path.Combine(Golden.CasesRoot, name);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", side, Path.ChangeExtension(name, null).Replace('/', '_'));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, Path.GetFileName(source));
        File.Copy(source, copy);
        return copy;
    }

    /// <summary>A fresh empty working directory for a `run` case (as RunTests.cs runs each case), apart from its source: the
    /// driver's `run` stages its executable and scratch C in a directory beside the source (Q1042), which a program listing
    /// its source's directory would see while it runs.</summary>
    private static string Empty(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", "cwd", Path.ChangeExtension(name, null).Replace('/', '_'));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void AllSame(Func<string, string?> compare)
    {
        var failures = new ConcurrentBag<string>();
        Parallel.ForEach(RunInputs(), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, name =>
        {
            if (compare(name) is { } f) failures.Add(f);
        });
        Assert.True(failures.IsEmpty, string.Join("\n\n", failures.Order(StringComparer.Ordinal).Take(5)) + $"\n({failures.Count} cases differ)");
    }

    [Fact]
    public void EmitCAsCliAppOnEveryRunInput()
    {
        SelfhostDriverHarness.Build();
        AllSame(name =>
        {
            var copy = Isolated(name, "emit");
            var mode = ModeArgs(copy);
            var expected = Cs(Path.GetDirectoryName(copy)!, ["build", "--emit-c", .. mode, Path.GetFileName(copy)]);
            copy = Isolated(name, "emit");
            var actual = Self(Path.GetDirectoryName(copy)!, ["build", "--emit-c", .. mode, Path.GetFileName(copy)]);
            if (!expected.Out.AsSpan().SequenceEqual(actual.Out)) return $"{name}: emitted C differs";
            return Differs(name, expected, actual);
        });
    }

    [Fact]
    public void RunAsCliAppOnEveryRunInput()
    {
        SelfhostDriverHarness.Build();
        AllSame(name =>
        {
            var copy = Isolated(name, "run");
            var argsFile = Path.ChangeExtension(Path.Combine(Golden.CasesRoot, name), ".args");
            string[] programArgs = File.Exists(argsFile) ? ["--", .. File.ReadAllLines(argsFile)] : [];
            var mode = ModeArgs(copy);
            var expected = Cs(Empty(name), ["run", .. mode, copy, .. programArgs]);
            copy = Isolated(name, "run");
            var actual = Self(Empty(name), ["run", .. mode, copy, .. programArgs]);
            return Differs(name, expected, actual);
        });
    }

    /// <summary>A fresh `tmp/t5/<side>/<name>/` holding `arithmetic.byn`, its path relative to the directory.</summary>
    private static string Fresh(string side, string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", side, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(Golden.CasesRoot, "run", "arithmetic.byn"), Path.Combine(dir, "arithmetic.byn"));
        return dir;
    }

    private static void SameIn(string name, params string[] args)
    {
        var cs = Fresh("cs", name);
        var self = Fresh("self", name);
        var expected = Cs(cs, args);
        var actual = Self(self, args);
        Assert.Null(Differs(string.Join(' ', args), expected, actual));
        Assert.Equal(Entries(cs), Entries(self));
    }

    /// <summary>The names in `dir`, files and directories, in ordinal order.</summary>
    private static List<string> Entries(string dir) =>
        [.. Directory.EnumerateFileSystemEntries(dir).Select(e => Path.GetFileName(e)!).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The build's scratch work runs in a staging directory beside the output (Toolchain.cs `Compile`, Q1042 (a)), removed
    /// afterwards (plan D4): a user's `main.c`, `bayan-rt.h` and `bayan-rt.c` beside the output keep their bytes, and after
    /// `build` and `run` the directory holds what the C#'s does.
    /// </summary>
    [Fact]
    public void LeavesTheOutputDirectoryAsCliApp()
    {
        foreach (var args in new[] { new[] { "build", "arithmetic.byn" }, ["build", "-o", "prog", "arithmetic.byn"], ["run", "arithmetic.byn"] })
        {
            var label = string.Join(' ', args);
            var cs = Fresh("cs", "user-files");
            var self = Fresh("self", "user-files");
            foreach (var dir in new[] { cs, self })
                foreach (var name in new[] { "main.c", "bayan-rt.h", "bayan-rt.c" })
                    File.WriteAllText(Path.Combine(dir, name), $"// the user's {name}\n");
            Assert.Null(Differs(label, Cs(cs, args), Self(self, args)));
            foreach (var name in new[] { "main.c", "bayan-rt.h", "bayan-rt.c" })
                Assert.True(File.ReadAllBytes(Path.Combine(cs, name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(self, name))), $"`{label}`: the driver changed the user's {name}");
            Assert.Equal(Entries(cs), Entries(self));
        }
    }

    /// <summary>`run` on a source with no extension: the C# links in its cache (CliApp.cs:561-564), the driver in its staging
    /// directory, so the source keeps its bytes.</summary>
    [Fact]
    public void RunsAnExtensionlessSourceAsCliApp()
    {
        var sides = new List<string>();
        foreach (var side in new[] { "cs", "self" })
        {
            var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", side, "extensionless");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(Golden.CasesRoot, "run", "arithmetic.byn"), Path.Combine(dir, "tool"));
            sides.Add(dir);
        }
        var before = File.ReadAllBytes(Path.Combine(sides[1], "tool"));
        Assert.Null(Differs("run tool", Cs(sides[0], "run", "tool"), Self(sides[1], "run", "tool")));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(sides[1], "tool"))), "`run tool` wrote over the source");
        Assert.Equal(Entries(sides[0]), Entries(sides[1]));
    }

    [Fact]
    public void OutputNamesAsCliApp()
    {
        SameIn("default", "build", "arithmetic.byn");
        SameIn("dot", "build", ".");
        SameIn("named", "build", "-o", "prog", "arithmetic.byn");
        SameIn("verbose", "build", "-v", "arithmetic.byn");
        SameIn("json", "build", "--diagnostics", "json", "arithmetic.byn");
        SameIn("debug", "build", "--mode", "debug", "--target", "host", "arithmetic.byn");
        SameIn("run-package", "run", "--package", ".", "arithmetic.byn");
        SameIn("run", "run", "arithmetic.byn");
        SameIn("run-dir", "run", ".");
        SameIn("safe", "build", "--mode", "safe", "--verbose", "arithmetic.byn");
    }

    private static void SameAsInProcess(params string[] args)
    {
        var expected = InProcess(args);
        var actual = SelfhostDriverHarness.Run(args);
        var label = string.Join(' ', args);
        Assert.Equal(expected.Exit, actual.Exit);
        SelfhostEmitterHarness.AssertSame($"stdout of `{label}`", Mask(expected.Out), Mask(actual.Out));
        SelfhostEmitterHarness.AssertSame($"stderr of `{label}`", Mask(expected.Err), Mask(actual.Err));
    }

    [Fact]
    public void FailuresAsCliApp()
    {
        var file = Path.Combine(Fresh("self", "failures"), "arithmetic.byn");
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", "self", "broken");
        Directory.CreateDirectory(dir);
        var broken = Path.Combine(dir, "broken.byn");
        File.WriteAllText(broken, "void main() {\n    let x: i32 = \"no\"\n    print(y)\n}\n");
        foreach (var command in new[] { "build", "run" })
        {
            SameAsInProcess(command);
            SameAsInProcess(command, "--mode", "bogus", file);
            SameAsInProcess(command, "--diagnostics", "yaml", file);
            SameAsInProcess(command, file, file);
            SameAsInProcess(command, "--bogus", file);
            SameAsInProcess(command, Path.Combine(dir, "missing.byn"));
            SameAsInProcess(command, broken);
            SameAsInProcess(command, "--diagnostics", "json", broken);
        }
        SameAsInProcess("build", "--target", "bogus", file);
        SameAsInProcess("build", "--emit-c", "--target", "bogus", file);
        SameAsInProcess("run", "-o", "x", file);
        SameAsInProcess("run", "-v", file);
        SameAsInProcess("build", "--emit-c", "--package", Path.Combine(Golden.RepoRoot, "selfhost"), broken);
        foreach (var dangling in new[] { "--mode", "--diagnostics", "-o", "--target", "--package" })
            SameAsInProcess("build", file, dangling);
        SameAsInProcess("run", "--emit-c", file);
        SameAsInProcess("run", "--target", "linux-x64", file);
        SameAsInProcess("build", "--emit-c", "--all", "--diagnostics", "text", "--mode", "debug", file);
    }

    /// <summary>`Target.os`/`Target.arch` fold to the `--target`'s strings on the path and `--package` branches, and stay the host macros without it.</summary>
    [Fact]
    public void FoldsTheTargetAsCliApp()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", "target", "app");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), "void main() {\n    print(Target.os)\n    print(Target.arch)\n}\n");
        var root = Path.GetDirectoryName(dir)!;
        SameAsInProcess("build", "--emit-c", "--target", "linux-x64", dir);
        SameAsInProcess("build", "--emit-c", "--target", "windows-x64", "--package", root, dir);
        SameAsInProcess("build", "--emit-c", "--target", "host", dir);
        Assert.Contains("BYN_STR(\"linux\", 5)", InProcess("build", "--emit-c", "--target", "linux-x64", dir).Out);
        // The `package.byn` branch passes the target too (CliApp.cs:283-284).
        var project = Project("self", "target", lib: false);
        File.WriteAllText(Path.Combine(project, "src", "app", "main.byn"), "void main() {\n    print(Target.os)\n    print(Target.arch)\n}\n");
        SameAsInProcess("build", "--emit-c", "--target", "windows-x64", project);
        Assert.Contains("BYN_STR(\"windows\", 7)", InProcess("build", "--emit-c", "--target", "windows-x64", project).Out);
    }

    /// <summary>
    /// `build --emit-c --mode debug --package selfhost selfhost/<dir>` from the repo root, for `lexer` and the driver itself:
    /// same exit and stderr, byte-identical C, and every `#line` name in the C#'s C relative to `Path.fullPath("selfhost")`
    /// (CEmitter.cs `FilePath`, 388-395) and naming a file of the package, `<dir>/` among them; `main.c` is the generated-code reset.
    /// </summary>
    [Fact]
    public void EmitsAPackageAsCliApp()
    {
        var root = Path.GetFullPath(Path.Combine(Golden.RepoRoot, "selfhost"));
        foreach (var dir in new[] { "lexer", "driver" })
        {
            string[] args = ["build", "--emit-c", "--mode", "debug", "--package", "selfhost", $"selfhost/{dir}"];
            var expected = Cs(Golden.RepoRoot, args);
            var actual = Self(Golden.RepoRoot, args);
            Assert.True(expected.Exit == actual.Exit, $"{dir}: exit {expected.Exit} != {actual.Exit}\n{actual.Err}");
            Assert.Equal(expected.Err, actual.Err);
            Assert.True(expected.Out.AsSpan().SequenceEqual(actual.Out), $"the driver's C of selfhost/{dir} differs from the C#'s");
            var names = Regex.Matches(Text(expected.Out), "^#line \\d+ \"([^\"]+)\"$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Where(n => n != CEmitter.GeneratedFile).Distinct().ToList();
            Assert.Contains(names, n => n.StartsWith($"{dir}/", StringComparison.Ordinal));
            foreach (var name in names)
            {
                Assert.False(Path.IsPathRooted(name), $"{dir}: rooted #line name {name}");
                Assert.True(File.Exists(Path.Combine(root, name)), $"{dir}: #line name {name} is not relative to {root}");
            }
        }
    }

    /// <summary>The C# `bayan build --package selfhost selfhost/driver` builds a driver that answers as the harness's driver.</summary>
    [Fact]
    public void TheCsBuildsTheDriver()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", "built");
        Directory.CreateDirectory(dir);
        var built = Path.Combine(dir, "bayan-driver");
        var (exit, _, err) = InProcess("build", "--mode", "fast", "--package", Path.Combine(Golden.RepoRoot, "selfhost"), "-o", built, SelfhostDriverHarness.DriverDir);
        Assert.True(exit == 0, err);
        var arithmetic = Path.Combine(Golden.CasesRoot, "run", "arithmetic.byn");
        foreach (var args in new[] { new[] { "version" }, ["help"], ["build", "--emit-c", arithmetic], ["check", arithmetic], ["build", "--mode", "bogus", arithmetic] })
            Assert.Null(Differs(string.Join(' ', args), Process(SelfhostDriverHarness.Build(), null, dir, args), Process(built, null, dir, args)));
    }

    /// <summary>
    /// `--package` and path builds read a directory's module files in ordinal order (CliApp.cs:290), skipping `.test.byn`, other
    /// extensions and directories; `a.byn` sorts before `a.byn.byn`, its extension. A `#!` file is a script, compiled alone
    /// (CliApp.cs:362); an empty file is not.
    /// </summary>
    [Fact]
    public void ReadsModuleFilesAndScriptsAsCliApp()
    {
        var root = Path.Combine(Golden.RepoRoot, "tmp", "t5", "modules");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        var dir = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(dir, "d.byn"));
        File.WriteAllText(Path.Combine(dir, "b.byn"), "i32 bee() => 2\n");
        File.WriteAllText(Path.Combine(dir, "a.byn.byn"), "i32 ayy() => 1\n");
        File.WriteAllText(Path.Combine(dir, "ab.byn"), "i32 abb() => 3\n");
        File.WriteAllText(Path.Combine(dir, "a.byn"), "void main() {\n    print($\"{ayy() + bee() + abb()}\")\n}\n");
        File.WriteAllText(Path.Combine(dir, "c.test.byn"), "test \"never\" {\n}\n");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not code\n");
        SameAsInProcess("build", "--emit-c", "--package", root, dir);
        SameAsInProcess("build", "--emit-c", dir);
        var scripts = Path.Combine(root, "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "s.byn"), "#!/usr/bin/env bayan\nprint(\"hi\")\n");
        File.WriteAllText(Path.Combine(scripts, "other.byn"), "this does not parse (\n");
        SameAsInProcess("build", "--emit-c", Path.Combine(scripts, "s.byn"));
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "e.byn"), "");
        File.WriteAllText(Path.Combine(empty, "f.byn"), "#");
        SameAsInProcess("build", "--emit-c", Path.Combine(empty, "e.byn"));
        SameAsInProcess("build", "--emit-c", Path.Combine(empty, "f.byn"));
        var hash = Path.Combine(root, "hash");
        Directory.CreateDirectory(hash);
        File.WriteAllText(Path.Combine(hash, "h.byn"), "#x\nvoid main() {\n}\n");
        SameAsInProcess("build", "--emit-c", Path.Combine(hash, "h.byn"));
    }

    /// <summary>A project made by the C# `bayan new` under `tmp/t5/<side>/project/`, its directory.</summary>
    private static string Project(string side, string name, bool lib)
    {
        var parent = Path.Combine(Golden.RepoRoot, "tmp", "t5", side, "project");
        Directory.CreateDirectory(parent);
        var dir = Path.Combine(parent, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var made = InProcess(lib ? ["new", dir, "--lib", "--vcs", "none"] : ["new", dir, "--vcs", "none"]);
        Assert.True(made.Exit == 0, made.Err);
        return dir;
    }

    /// <summary>`build` and `run` inside a project (`package.byn` in the working directory, no path): `out/<mode>/<target>/`
    /// (CliApp.cs `PrepareOut`), the `-v` output path relative to the root, a second build, and a library's refusal.</summary>
    [Fact]
    public void ProjectsAsCliApp()
    {
        var cs = Project("cs", "demo", lib: false);
        var self = Project("self", "demo", lib: false);
        // The driver creates no directory (spec Q1, Q1016): the `out/` tree the C# makes is made for it beforehand.
        Assert.Equal(0, Cs(cs, "build").Exit);
        foreach (var d in Directory.EnumerateDirectories(Path.Combine(cs, "out"), "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(self, Path.GetRelativePath(cs, d)));
        foreach (var args in new[] { new[] { "build" }, ["build"], ["build", "-v"], ["build", "-v", "-o", "prog"], ["run"], ["build", "--package", "..", "."] })
        {
            Assert.Null(Differs(string.Join(' ', args), Cs(cs, args), Self(self, args)));
            foreach (var file in Directory.EnumerateFiles(cs, "*", SearchOption.AllDirectories))
                Assert.True(File.Exists(Path.Combine(self, Path.GetRelativePath(cs, file))), $"`{string.Join(' ', args)}`: the driver did not write {Path.GetRelativePath(cs, file)}");
        }
        // A rebuild after a source edit: `out/.../c/main.c` exists and differs, so `WriteIfChanged` rewrites it.
        foreach (var side in new[] { cs, self })
            File.WriteAllText(Path.Combine(side, "src", "app", "main.byn"), "void main() {\n    print(\"edited\")\n}\n");
        Assert.Null(Differs("build after edit", Cs(cs, "build"), Self(self, "build")));
        Assert.Equal(File.ReadAllText(Path.Combine(cs, "out", "safe", CliApp.HostTarget, "c", "main.c")), File.ReadAllText(Path.Combine(self, "out", "safe", CliApp.HostTarget, "c", "main.c")));
        Assert.Null(Differs("run after edit", Cs(cs, "run"), Self(self, "run")));
        var csLib = Project("cs", "kit", lib: true);
        var selfLib = Project("self", "kit", lib: true);
        foreach (var args in new[] { new[] { "build" }, ["run"] })
            Assert.Null(Differs(string.Join(' ', args), Cs(csLib, args), Self(selfLib, args)));
    }

    /// <summary>No C compiler on an empty `PATH` (and no `BAYAN_CC`): `build` and `run` report it as the C# and exit 4, as plain text under `--diagnostics json` too (CliApp.cs:405-409).</summary>
    [Fact]
    public void NoCompilerAsCliApp()
    {
        foreach (var args in new[] { new[] { "build", "arithmetic.byn" }, ["run", "arithmetic.byn"], ["build", "--diagnostics", "json", "arithmetic.byn"] })
        {
            var cs = Fresh("cs", "no-cc");
            var self = Fresh("self", "no-cc");
            var empty = Path.Combine(Golden.RepoRoot, "tmp", "t5", "empty-path");
            Directory.CreateDirectory(empty);
            var expected = Process("dotnet", [typeof(CliApp).Assembly.Location], cs, args, empty);
            Assert.Equal(CliApp.ExitToolchain, expected.Exit);
            Assert.Null(Differs(string.Join(' ', args), expected, Process(SelfhostDriverHarness.Build(), null, self, args, empty)));
        }
    }

    /// <summary>A C compiler that rejects the C (a `cc` on `PATH` that fails): BYN4001 as the C# `ReportToolchain` (CliApp.cs:403-419),
    /// in text and as `--diagnostics json` (:411-415), for `build` and `run`; nothing is left beside the source.</summary>
    [Fact]
    public void RejectedCAsCliApp()
    {
        if (OperatingSystem.IsWindows()) return; // a `/bin/sh` script stands for the compiler
        var bin = Path.Combine(Golden.RepoRoot, "tmp", "t5", "rejecting-bin");
        Directory.CreateDirectory(bin);
        var fake = Path.Combine(bin, "cc");
        File.WriteAllText(fake, "#!/bin/sh\necho \"main.c:2:5: error: injected\" >&2\nexit 1\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var args in new[] { new[] { "build", "--diagnostics", "json", "arithmetic.byn" }, ["run", "--diagnostics", "json", "arithmetic.byn"], ["build", "arithmetic.byn"] })
        {
            var cs = Fresh("cs", "rejected");
            var self = Fresh("self", "rejected");
            var expected = Process("dotnet", [typeof(CliApp).Assembly.Location], cs, args, bin);
            Assert.Equal(CliApp.ExitToolchain, expected.Exit);
            Assert.Contains("BYN4001", expected.Err);
            Assert.Null(Differs(string.Join(' ', args), expected, Process(SelfhostDriverHarness.Build(), null, self, args, bin)));
            // A failed link leaves no staging directory either (plan D4).
            Assert.Equal(Entries(cs), Entries(self));
        }
    }

    /// <summary>The dispatch of `run` (main.byn) the T5 commands sit in, under this class's filter: global options, usage,
    /// the T2 commands, a `#!` script with its arguments, an unknown command and `fmt`.</summary>
    [Fact]
    public void DispatchAsCliApp()
    {
        var file = Path.Combine(Fresh("self", "dispatch"), "arithmetic.byn");
        foreach (var args in new[] { [], ["--color", "bad", "help"], ["version"], ["help"], ["lex", file], ["parse", "--ast", file], ["types", file], ["check", file], ["frobnicate"], ["fmt"], new[] { "missing-script.byn" } })
            SameAsInProcess(args);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5", "script");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "hello.byn");
        File.WriteAllText(script, "#!/usr/bin/env bayan\nprint(\"hello\")\n");
        Assert.Null(Differs("script", Cs(dir, script, "a", "b"), Self(dir, script, "a", "b")));
        var (exit, stdout, stderr) = SelfhostDriverHarness.Run("doctor");
        Assert.Equal(CliApp.ExitUsage, exit); // no-oracle: Q917 the C# runs `doctor`; the refusal text is R3's (Q4)
        Assert.Equal("", stdout);
        Assert.StartsWith("error: `", stderr);
    }
}
