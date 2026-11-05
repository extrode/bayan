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
/// symlinks (dangling and looping included), odd separators and a NUL, and needs <c>uses fs</c>. A symlink row is skipped where
/// creating the link needs a privilege the test lacks (Windows without developer mode).
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

    private static bool TryLink(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is UnauthorizedAccessException or IOException)
        {
            return false;
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
            if (TryLink(Path.Combine(dir, "link-file"), "file.txt", directory: false)) names.AddRange(["link-file", "link-file/"]);
            if (TryLink(Path.Combine(dir, "link-dir"), "sub", directory: true)) names.AddRange(["link-dir", "link-dir/inner/deep.txt", "link-dir/missing"]);
            if (TryLink(Path.Combine(dir, "link-dangling"), "nowhere", directory: false)) names.Add("link-dangling");
            if (TryLink(Path.Combine(dir, "link-loop"), "link-loop", directory: false)) names.Add("link-loop");
            if (TryLink(Path.Combine(dir, "link-abs"), Path.Combine(dir, "file.txt"), directory: false)) names.Add("link-abs");

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
