using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Q970 item 3: <c>Fs.fileExists</c> gives what .NET's <c>File.Exists</c> gives on the same OS, over a corpus of files, directories,
/// symlinks (dangling and looping included), a path over 260 chars, odd separators and a NUL, and needs <c>uses fs</c>. Creating a symlink
/// must succeed (Windows needs elevation or developer mode): a failure fails the test.
/// </summary>
public sealed class StdFsTests
{
    private static readonly string Root = Path.Combine(Golden.RepoRoot, "tmp", "std-fs-diag");

    private static string Literal(string s) => StdPathTests.Literal(s).Replace("\0", "\\0");

    private static string Build(string source, string dir)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([new SourceText("fs.byn", Encoding.UTF8.GetBytes(source))], dir);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        return build.ExecutablePath!;
    }

    /// <summary>Creates a symlink; a failure fails the test and names the row (a Windows host without the privilege cannot run this test).</summary>
    private static void Link(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
        }
        catch (Exception e)
        {
            Assert.Fail($"cannot create the symlink row {Path.GetFileName(link)} -> {target}: {e}");
        }
    }

    [Fact]
    public async Task FileExistsMatchesDotNet()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "std-fs-corpus");
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(Path.Combine(dir, "sub", "inner"));
            File.WriteAllText(Path.Combine(dir, "file.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "empty"), "");
            File.WriteAllText(Path.Combine(dir, "é ü 日本.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "sub", "inner", "deep.txt"), "x");
            var names = new List<string> { "file.txt", "empty", "é ü 日本.txt", "sub", "sub/inner", "sub/inner/deep.txt", "missing", "sub/missing", "FILE.TXT" };
            Link(Path.Combine(dir, "link-file"), "file.txt", directory: false);
            Link(Path.Combine(dir, "link-dir"), "sub", directory: true);
            Link(Path.Combine(dir, "link-dangling"), "nowhere", directory: false);
            Link(Path.Combine(dir, "link-loop"), "link-loop", directory: false);
            Link(Path.Combine(dir, "link-abs"), Path.Combine(dir, "file.txt"), directory: false);
            names.AddRange(["link-file", "link-file/", "link-dir", "link-dir/inner/deep.txt", "link-dir/missing", "link-dangling", "link-loop", "link-abs"]);
            // A full path of more than 260 chars (.NET prefixes it with \\?\ on Windows).
            var longDir = dir;
            while (longDir.Length < 300) longDir = Path.Combine(longDir, new string('l', 50));
            Directory.CreateDirectory(longDir);
            File.WriteAllText(Path.Combine(longDir, "long.txt"), "x");
            names.AddRange([Path.GetRelativePath(dir, Path.Combine(longDir, "long.txt")), Path.GetRelativePath(dir, Path.Combine(longDir, "missing.txt")), Path.GetRelativePath(dir, longDir)]);

            var paths = new List<string>();
            foreach (var n in names) paths.Add(Path.Combine(dir, n.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var n in new[]
            {
                "file.txt/", "file.txt/.", "file.txt/..", "file.txt/x", "sub/", "sub/.", "sub/..", "sub/../file.txt", "sub//../file.txt", "./file.txt", ".//file.txt",
                "sub/inner/../../file.txt", "sub/inner/../deep.txt", "sub/inner/../inner/deep.txt", "file.txt\0", "file.txt\0x", "\0file.txt", "sub\0", "file.txt ", " file.txt",
                "file.txt\\", "sub\\inner\\deep.txt", "sub/inner//deep.txt", "sub//", "empty/", "",
            })
                paths.Add(Path.Combine(dir, "") + n);
            paths.AddRange(["", " ", "/", "//", ".", "..", "\\", "C:", "C:\\", "x\0", "\0", Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(dir, "file.txt")),
                Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(dir, "sub")), Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(dir, "missing")),
                Path.GetTempPath(), Path.GetFullPath(Environment.ProcessPath!), Environment.ProcessPath!]);

            var source = string.Concat(paths.Select(p => $"print($\"{{Fs.fileExists({Literal(p)})}}\")\n"));
            var psi = new ProcessStartInfo(Build($"void main() uses console, fs {{\n{source}}}\n", Path.Combine(Golden.RepoRoot, "tmp", "std-fs")))
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEndAsync();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            Assert.True(proc.WaitForExit(60_000), "program timed out");
            Assert.True(proc.ExitCode == 0, await stderr);
            var actual = stdout.Split('\n');
            Assert.Equal(paths.Count + 1, actual.Length);
            for (var i = 0; i < paths.Count; i++)
            {
                var expected = File.Exists(paths[i]) ? "true" : "false";
                Assert.True(expected == actual[i], $"line {i + 1}: {Literal(paths[i])}\n.NET: {expected}\nBayan: {actual[i]}");
            }
            Assert.Contains("true", actual);
            Assert.Contains("false", actual);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Spec 012 T20: <c>Io.remove</c> succeeds and fails on the same rows as .NET <c>File.Delete</c>, and leaves the same files behind.</summary>
    [Fact]
    public async Task IoRemoveMatchesDotNet()
    {
        var work = Path.Combine(Golden.RepoRoot, "tmp", "std-io-remove");
        string[] rows =
        [
            "file.txt", "file.txt", "empty", "é ü 日本.txt", "missing", "sub/missing", "nodir/missing", "sub", "sub/", "sub/inner", "sub/inner/deep.txt", "file2.txt/", "file2.txt/.", "file2.txt/x",
            "link-file", "link-dir", "link-dangling", "link-loop", "readonly.txt", "./sub/../file2.txt", "file3.txt ", " file3.txt", "", " ", ".", "..", "x\0", "\0",
        ];
        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            string Fixture(string name)
            {
                var dir = Path.Combine(work, name);
                Directory.CreateDirectory(Path.Combine(dir, "sub", "inner"));
                foreach (var f in new[] { "file.txt", "empty", "é ü 日本.txt", "file2.txt", "file3.txt", "readonly.txt", Path.Combine("sub", "inner", "deep.txt") }) File.WriteAllText(Path.Combine(dir, f), "x");
                File.SetAttributes(Path.Combine(dir, "readonly.txt"), FileAttributes.ReadOnly);
                Link(Path.Combine(dir, "link-file"), "file2.txt", directory: false);
                Link(Path.Combine(dir, "link-dir"), "sub", directory: true);
                Link(Path.Combine(dir, "link-dangling"), "nowhere", directory: false);
                Link(Path.Combine(dir, "link-loop"), "link-loop", directory: false);
                return dir;
            }
            string Listing(string dir) => string.Join("\n", Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Select(e => Path.GetRelativePath(dir, e).Replace('\\', '/')).Order(StringComparer.Ordinal));
            string Full(string dir, string row) => row.Length == 0 ? "" : Path.Combine(dir, "") + row.Replace("\\0", "\0");

            var net = Fixture("net");
            var expected = new List<string>();
            foreach (var row in rows)
            {
                try { File.Delete(Full(net, row)); expected.Add("ok"); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { expected.Add("fail"); }
            }

            var bayan = Fixture("bayan");
            var source = string.Concat(rows.Select(r => $"print(removed({Literal(Full(bayan, r))}))\n"));
            var program = "string removed(string path) uses fs {\n    Io.remove(path) catch { return \"fail\" }\n    return \"ok\"\n}\n\n" + $"void main() uses console, fs {{\n{source}}}\n";
            var psi = new ProcessStartInfo(Build(program, Path.Combine(Golden.RepoRoot, "tmp", "std-io-remove-build"))) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEndAsync();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            Assert.True(proc.WaitForExit(60_000), "program timed out");
            Assert.True(proc.ExitCode == 0, await stderr);
            var actual = stdout.Split('\n');
            Assert.Equal(rows.Length + 1, actual.Length);
            for (var i = 0; i < rows.Length; i++) Assert.True(expected[i] == actual[i], $"row {i + 1}: {Literal(rows[i])}\n.NET: {expected[i]}\nBayan: {actual[i]}");
            Assert.Equal(Listing(net), Listing(bayan));
            Assert.Contains("ok", expected);
            Assert.Contains("fail", expected);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                foreach (var f in Directory.EnumerateFiles(work, "readonly.txt", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(work, recursive: true);
            }
        }
    }

    [Fact]
    public void IoRemoveNeedsFsEffect()
    {
        var src = "void f(string p) fails IoError uses console {\n    try Io.remove(p)\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        var rendered = Rendered(c).Where(r => r.StartsWith("BYN6002")).Select(r => r.Split(" | ")[0]).ToList();
        Assert.Equal([Expected(DiagnosticCodes.EffectNotDeclared, src, "Io.remove", "", "f", "fs")], rendered);
        var ok = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src.Replace("uses console", "uses console, fs")))], Root);
        Assert.DoesNotContain(Rendered(ok), r => r.StartsWith("BYN6002"));
    }

    /// <summary>The compiler's diagnostics as <c>code line:col message</c>, with the help line when there is one.</summary>
    private static List<string> Rendered(Compilation c) =>
        c.Diagnostics.Items.Select(d =>
        {
            var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
            return $"{d.Code} {line}:{col} {d.Message}" + (d.Help is null ? "" : $" | {d.Help}");
        }).ToList();

    private static string Expected(DiagnosticDescriptor descriptor, string source, string at, string help, params object[] args)
    {
        var offset = source.IndexOf(at, StringComparison.Ordinal);
        var line = source[..offset].Count(ch => ch == '\n') + 1;
        var col = offset - (source.LastIndexOf('\n', offset) + 1) + 1;
        return $"{descriptor.Code} {line}:{col} {descriptor.Format(args)}" + (help.Length == 0 ? "" : $" | {help}");
    }

    [Fact]
    public void FileExistsNeedsFsEffect()
    {
        var src = "void f() uses console {\n    print($\"{Fs.fileExists(\"a\")}\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        var rendered = Rendered(c).Where(r => r.StartsWith("BYN6002")).Select(r => r.Split(" | ")[0]).ToList();
        Assert.Equal([Expected(DiagnosticCodes.EffectNotDeclared, src, "Fs.fileExists", "", "f", "fs")], rendered);
        var ok = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src.Replace("uses console", "uses console, fs")))], Root);
        Assert.DoesNotContain(Rendered(ok), r => r.StartsWith("BYN6002"));
    }

    [Fact]
    public void IoUnknownMemberSuggestsRemove()
    {
        var src = "void f() uses fs {\n    let x = Io.remov(\"A\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        var rendered = Rendered(c).Where(r => r.StartsWith("BYN2003")).ToList();
        Assert.Equal([Expected(DiagnosticCodes.UnknownMember, src, "remov", "did you mean `remove`?", "remov", "Io")], rendered);
    }

    [Fact]
    public void UnknownMemberIsReported()
    {
        var src = "void f() uses fs {\n    let x = Fs.exists(\"A\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        var rendered = Rendered(c).Where(r => r.StartsWith("BYN2003")).ToList();
        var far = "void f() uses fs {\n    let x = Fs.zzzzzzzz(\"A\")\n}\n";
        var farRendered = Rendered(Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(far))], Root)).Where(r => r.StartsWith("BYN2003")).ToList();
        Assert.Equal([Expected(DiagnosticCodes.UnknownMember, src, "exists", "", "exists", "Fs")], rendered);
        Assert.Equal([Expected(DiagnosticCodes.UnknownMember, far, "zzzzzzzz", "", "zzzzzzzz", "Fs")], farRendered);
    }
}
