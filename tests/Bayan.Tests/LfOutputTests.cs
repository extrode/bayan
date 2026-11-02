using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Q742: stdout and stderr of a built Bayan program and of <c>bayan</c> itself carry no <c>\r</c>. Child output is read as raw
/// bytes, so nothing here hides a <c>\r\n</c>. The test runs on every platform but can only fail on Windows, where text-mode
/// stdio would turn each <c>\n</c> into <c>\r\n</c>.
/// </summary>
public sealed class LfOutputTests
{
    private static string Work(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "lf-output", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Runs the child to exit and returns its stdout and stderr bytes, unconverted.</summary>
    private static (int Exit, byte[] Stdout, byte[] Stderr) RunRaw(ProcessStartInfo psi)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = Drain(p.StandardOutput.BaseStream);
        var stderr = Drain(p.StandardError.BaseStream);
        Assert.True(p.WaitForExit(120_000), "child timed out after 120 s");
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private static async Task<byte[]> Drain(Stream s)
    {
        var buffer = new MemoryStream();
        await s.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static ProcessStartInfo Bayan(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet");
        psi.ArgumentList.Add(typeof(CliApp).Assembly.Location);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    [Fact]
    public void BuiltProgramWritesLf()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Work("program");
        var source = "void main() {\n    print(\"one\")\n    print(\"two\")\n    Io.stderr.write(\"err\\n\")\n}\n";
        var c = Compilation.Create([new SourceText("lf.byn", System.Text.Encoding.UTF8.GetBytes(source))], dir);
        Assert.False(c.Diagnostics.HasErrors);
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        var (exit, stdout, stderr) = RunRaw(new ProcessStartInfo(build.ExecutablePath!) { WorkingDirectory = dir });
        Assert.Equal(0, exit);
        Assert.Equal("one\ntwo\n", System.Text.Encoding.UTF8.GetString(stdout));
        Assert.Equal("err\n", System.Text.Encoding.UTF8.GetString(stderr));
        Assert.DoesNotContain((byte)'\r', stdout);
        Assert.DoesNotContain((byte)'\r', stderr);
    }

    [Fact]
    public void CliWritesLf()
    {
        var dir = Work("cli");
        Directory.CreateDirectory(Path.Combine(dir, "bad"));
        Directory.CreateDirectory(Path.Combine(dir, "good"));
        var bad = Path.Combine(dir, "bad", "bad.byn");
        File.WriteAllText(bad, "void main() {\n    let x: i64 = \"s\"\n}\n");
        var good = Path.Combine(dir, "good", "good.byn");
        File.WriteAllText(good, "void main() {\n    print(\"ok\")\n}\n");

        var (checkExit, checkOut, checkErr) = RunRaw(Bayan("check", bad));
        Assert.NotEqual(0, checkExit);
        Assert.True(checkOut.Length + checkErr.Length > 0, "check printed no diagnostic");
        Assert.DoesNotContain((byte)'\r', checkOut);
        Assert.DoesNotContain((byte)'\r', checkErr);
        Assert.Contains((byte)'\n', checkOut.Concat(checkErr).ToArray());

        var (typesExit, typesOut, typesErr) = RunRaw(Bayan("types", good));
        Assert.Equal(0, typesExit);
        Assert.Contains((byte)'\n', typesOut);
        Assert.DoesNotContain((byte)'\r', typesOut);
        Assert.DoesNotContain((byte)'\r', typesErr);
    }
}
