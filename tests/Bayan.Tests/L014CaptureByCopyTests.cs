using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L014 (docs/laws/014-capture-by-copy-never-aliases.md): each rejected capture site gets exactly its code, and the
/// accepted rows in <c>tests/cases/run/lambda-capture-by-copy.byn</c> build standalone under <c>-Wall -Werror</c> and
/// print the value each capture had when its lambda was created, after a shadow, after the source's scope ends and
/// after the <c>var</c> it was copied from changes.
/// </summary>
[Trait("Law", "L014")]
public sealed class L014CaptureByCopyTests(ITestOutputHelper output)
{
    private const string Apply = "i64 apply((i64 -> i64) f) => f(1)\n\n";

    public static TheoryData<string, string, string> Rejected => new()
    {
        { "var", "BYN2821", Apply + "void main() {\n    var n = 1\n    n = 2\n    print($\"{apply(x => x + n)}\")\n}\n" },
        { "var pattern binding", "BYN2821", Apply + """
            union Shape {
                circle(i64 r)
                square(i64 side)
            }

            void main() {
                let s = Shape.circle(3)
                switch s {
                    case .circle(var r):
                        r = r + 1
                        print($"{apply(x => x + r)}")
                    case .square(let side):
                        print($"{side}")
                }
            }

            """ },
        { "ref parameter", "BYN2821", Apply + "i64 bump(ref i64 total) => apply(x => x + total)\n\nvoid main() {\n    var t: i64 = 1\n    print($\"{bump(ref t)}\")\n}\n" },
        { "out parameter", "BYN2821", Apply + "void fill(out i64 total) {\n    total = 1\n    total = apply(x => x + total)\n}\n\nvoid main() {\n    var t: i64 = 0\n    fill(out t)\n    print($\"{t}\")\n}\n" },
        { "in parameter", "BYN2821", Apply + "i64 read(in i64 total) => apply(x => x + total)\n\nvoid main() {\n    let t: i64 = 1\n    print($\"{read(in t)}\")\n}\n" },
        { "explicit this", "BYN2821", Apply + Counter("apply(x => x * this.count)") },
        { "implicit this field", "BYN2821", Apply + Counter("apply(x => x * count)") },
        { "implicit this method", "BYN2821", Apply + Counter("apply(x => x * twice())") },
        { "unique value", "BYN2780", Apply + "void main() {\n    let xs = List<i64>()\n    print($\"{apply(x => x + xs.length)}\")\n}\n" },
        { "borrow of a modified var", "BYN3004", """
            void main() {
                var buf = StrBuf()
                buf.append("hi")
                let s = buf.view
                let f: () -> i64 = () => s.length
                buf.clear()
                print($"{f()}")
            }

            """ },
        { "lambda held past its loop iteration", "BYN3002", """
            void main() {
                var held: i64 -> i64 = x => x
                for i in 0..3 {
                    let j = (i + 1) * 10
                    if i == 0 {
                        held = x => x + j
                    }
                }
                print($"{held(1)}")
            }

            """ },
    };

    /// <summary>The value each accepted row's capture had when its lambda was created, as the program prints it.</summary>
    private static readonly (string Row, string Created)[] Accepted =
    [
        ("i64", "1"), ("i32", "1"), ("u8", "1"), ("f64", "1.5"), ("bool", "true"), ("rune", "a"), ("string", "one"),
        ("struct", "Point { x: 1, y: 1 }"), ("enum", "red"), ("union", "circle(r: 1)"), ("optional", "1"),
        ("tuple", "(1, one)"), ("function", "11"),
    ];

    private static readonly string[] Scenarios = ["shadow", "scope", "reassigned"];

    private static string Counter(string body) => $$"""
        struct Counter {
            i64 count

            i64 twice() => count * 2

            i64 scaled() => {{body}}
        }

        void main() {
            let c = Counter { count: 2 }
            print($"{c.scaled()}")
        }

        """;

    [Theory]
    [MemberData(nameof(Rejected))]
    public void RejectedCaptureGetsItsCode(string row, string code, string source)
    {
        var c = Compilation.Create([SourceText.FromString("main.byn", source)], ".");
        Assert.True(c.Diagnostics.Items.Select(d => d.Code).SequenceEqual([code]),
            $"{row}: expected exactly {code}, got:\n{DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true)}");
    }

    [Fact]
    public void AcceptedCapturesPrintTheirCreationValue()
    {
        var path = Path.Combine(Golden.CasesRoot, "run", "lambda-capture-by-copy.byn");
        var c = Compilation.Create([SourceText.FromString("lambda-capture-by-copy.byn", File.ReadAllText(path))], ".");
        Assert.True(c.Diagnostics.Items.Count == 0, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var compiler = Toolchain.FindCompiler();
        if (compiler is null)
        {
            output.WriteLine("skipped: no C compiler found");
            return;
        }
        foreach (var mode in new[] { BuildMode.Debug, BuildMode.Safe })
        {
            var (code, stdout, stderr) = BuildStandalone(compiler, CliApp.EmitC(c, mode), mode);
            Assert.True(code is not null, $"{mode} C does not build under -Wall -Werror:\n{stderr}");
            Assert.True(code == 0, $"{mode} exit {code}\nstderr:\n{stderr}");
            var failures = Failures(stdout);
            Assert.True(failures.Count == 0, $"{mode}:\n{string.Join("\n", failures)}\nstdout:\n{stdout}");
        }
    }

    /// <summary>The proof can fail: a row that prints the source's later value is reported.</summary>
    [Fact]
    public void ReadingTheSourceFails() =>
        Assert.Equal(["i64 reassigned: held 3 (created 1)"], Failures(ExpectedOutput().Replace("i64 reassigned 1 3", "i64 reassigned 3 3")));

    /// <summary>Each accepted row and scenario that is missing or does not print its creation value first.</summary>
    private static List<string> Failures(string stdout)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        var failures = new List<string>();
        foreach (var (row, created) in Accepted)
            foreach (var scenario in Scenarios)
            {
                var prefix = $"{row} {scenario} ";
                if (lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is not { } line)
                    failures.Add($"{row} {scenario}: missing");
                else if (!line[prefix.Length..].StartsWith(created + " ", StringComparison.Ordinal))
                    failures.Add($"{row} {scenario}: held {line[prefix.Length..].Split(' ')[0]} (created {created})");
            }
        if (!lines.Contains("parameter scope 7 7") || !lines.Contains("parameter scope 8 8")) failures.Add("parameter: wrong or missing");
        return failures;
    }

    private static string ExpectedOutput() => File.ReadAllText(Path.Combine(Golden.CasesRoot, "run", "lambda-capture-by-copy.stdout"));

    /// <summary>
    /// Builds <paramref name="cSource"/> as <c>out.c</c> with <c>-Wall -Werror</c> (the toolchain build passes
    /// <c>-w</c>) and the runtime without them, then runs it. A null code means the build failed.
    /// </summary>
    private static (int? Code, string Stdout, string Stderr) BuildStandalone(string[] compiler, string cSource, BuildMode mode)
    {
        var dir = Directory.CreateTempSubdirectory("bayan-l014-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.c"), cSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), Toolchain.RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), Toolchain.RuntimeHeader);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "out.exe" : "out");
            string[] define = mode == BuildMode.Debug ? ["-DBYN_DEBUG"] : ["-DBYN_NDEBUG"];
            foreach (var args in new[]
            {
                ["-std=c11", "-fwrapv", "-Wall", "-Werror", .. define, "-I.", "-c", "out.c", "-o", "out.o"],
                ["-std=c11", "-fwrapv", "-w", .. define, "-I.", "-c", "bayan-rt.c", "-o", "rt.o"],
                new[] { "out.o", "rt.o", "-o", exe, OperatingSystem.IsWindows() ? "-lshell32" : "-lm" },
            })
            {
                var cc = new ProcessStartInfo(compiler[0]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in compiler[1..]) cc.ArgumentList.Add(a);
                foreach (var a in args) cc.ArgumentList.Add(a);
                var (ccCode, ccOut, ccErr) = Exec(cc);
                if (ccCode != 0) return (null, "", ccOut + ccErr);
            }
            var (code, stdout, stderr) = Exec(new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            return (code, stdout, stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (int Code, string Stdout, string Stderr) Exec(ProcessStartInfo psi)
    {
        using var p = Utf8Child.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.Result;
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }
}
