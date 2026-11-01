using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L016 (docs/laws/016-equal-keys-are-one-entry.md): two <c>==</c> keys built separately are one entry and the
/// last write wins; keys that differ, even in one leaf, are two entries. Every program is built standalone with
/// <c>-Wall -Werror</c> in debug and safe mode and run.
/// </summary>
[Trait("Law", "L016")]
public sealed class MapKeyLawTests(ITestOutputHelper output)
{
    private const int Keys = 8;

    public static TheoryData<int> Seeds
    {
        get
        {
            var data = new TheoryData<int>();
            for (var seed = 1; seed <= 12; seed++) data.Add(seed);
            return data;
        }
    }

    /// <summary>
    /// For eight keys of a generated type, <c>m</c> writes each key as a literal and then built by <c>make</c>,
    /// <c>n</c> the other way round; both must hold eight entries, each with the second write.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void EqualKeysAreOneEntry(int seed)
    {
        var state = 0x2545F4914F6CDD1DUL ^ ((ulong)seed * 0x9E3779B97F4A7C15UL);
        long Next(int bound)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return (long)((state >> 11) % (ulong)bound);
        }
        var gen = MapModelTests.KeyGen.Generate(Next);
        var codes = gen.Pool(Keys, Next);
        var source = new StringBuilder(gen.Declarations());
        source.Append($"void main() {{\n    var m = Map<{gen.TypeName}, i64>()\n    var n = Map<{gen.TypeName}, i64>()\n");
        foreach (var c in codes)
            source.Append($"    m[{gen.Literal(c)}] = 1\n    m[make({c})] = {c}\n    n[make({c})] = 1\n    n[{gen.Literal(c)}] = {c}\n");
        source.Append("    print($\"{m.length} {n.length}\")\n");
        foreach (var c in codes)
            source.Append($"    print($\"{{m[make({c})]}} {{n[{gen.Literal(c)}]}} {{m.contains({gen.Literal(c)})}}\")\n");
        source.Append("}\n");
        var expected = $"{codes.Count} {codes.Count}\n" + string.Concat(codes.Select(c => $"{c} {c} true\n"));
        Check($"l016-{seed}.byn", source.ToString(), expected);
    }

    /// <summary>
    /// Fixed layouts with padding: a literal key and one built field by field in another frame, after that frame
    /// held other bytes, are one entry. The <c>string</c> rows build the text by interpolation, so its pointer differs.
    /// </summary>
    [Fact]
    public void PaddedKeysBuiltApartAreOneEntry()
    {
        const string source = """
            struct Pad {
                bool flag
                i64 id
            }

            struct Small {
                i8 tag
                i64 id
            }

            struct Tail {
                i64 id
                rune mark
            }

            struct Named {
                bool flag
                string name
                Symbol kind
            }

            struct Outer {
                bool flag
                Small inner
            }

            struct Wide {
                i64 a
                i64 b
                i64 c
            }

            i64 smear(i64 x) {
                var w = Wide { a: x, b: x, c: x }
                w.b -= 1
                return w.a + w.b + w.c
            }

            Pad pad(i64 id) {
                var p = Pad { flag: false, id: 0 }
                p.flag = id > 0
                p.id = id
                return p
            }

            Small small(i64 id) {
                var s = Small { tag: 0, id: 0 }
                s.tag = i8(id - 100)
                s.id = id
                return s
            }

            Tail tail(i64 id) {
                var t = Tail { id: 0, mark: 'a' }
                t.id = id
                t.mark = rune(u32(id + 90))
                return t
            }

            Named named(i64 id) uses alloc {
                var n = Named { flag: false, name: "", kind: "" }
                n.flag = id > 0
                n.name = $"n{id}"
                n.kind = Symbol.intern($"k{id}")
                return n
            }

            Outer outer(i64 id) {
                return Outer { flag: id > 0, inner: small(id) }
            }

            void main() {
                var a = Map<Pad, i64>()
                var b = Map<Small, i64>()
                var c = Map<Tail, i64>()
                var d = Map<Named, i64>()
                var e = Map<Outer, i64>()
                var f = Map<(bool, i64), i64>()
                var g = Map<(i8, string), i64>()
                a[Pad { flag: true, id: 7 }] = 1
                b[Small { tag: -93, id: 7 }] = 1
                c[Tail { id: 7, mark: 'a' }] = 1
                d[Named { flag: true, name: "n7", kind: "k7" }] = 1
                e[Outer { flag: true, inner: Small { tag: -93, id: 7 } }] = 1
                f[(true, i64(7))] = 1
                g[(i8(-93), "n7")] = 1
                let s = smear(-1)
                a[pad(7)] = s
                b[small(7)] = s
                c[tail(7)] = s
                d[named(7)] = s
                e[outer(7)] = s
                f[(pad(7).flag, pad(7).id)] = s
                g[(small(7).tag, named(7).name)] = s
                print($"{a.length} {b.length} {c.length} {d.length} {e.length} {f.length} {g.length}")
                print($"{a[pad(7)]} {b[small(7)]} {c[tail(7)]} {d[named(7)]} {e[outer(7)]} {f[(true, i64(7))]} {g[(i8(-93), "n7")]}")
            }
            """;
        Check("l016-padding.byn", source, "1 1 1 1 1 1 1\n-4 -4 -4 -4 -4 -4 -4\n");
    }

    private void Check(string name, string source, string expected)
    {
        var c = Compilation.Create([SourceText.FromString(name, source)], ".");
        Assert.True(c.Diagnostics.Items.Count == 0, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true) + "\n" + source);
        var compiler = Toolchain.FindCompiler();
        if (compiler is null)
        {
            output.WriteLine("skipped: no C compiler found");
            return;
        }
        foreach (var mode in new[] { BuildMode.Debug, BuildMode.Safe })
        {
            var (code, stdout, stderr) = BuildStandalone(compiler, CliApp.EmitC(c, mode), mode);
            Assert.True(code is not null, $"{mode} C does not build under -Wall -Werror:\n{stderr}\n{source}");
            Assert.True(code == 0, $"{mode} exit {code}\nstderr:\n{stderr}\n{source}");
            Assert.True(expected == stdout.ReplaceLineEndings("\n"), $"{mode}: expected\n{expected}got\n{stdout}\n{source}");
        }
    }

    /// <summary>
    /// Builds <paramref name="cSource"/> as <c>out.c</c> with <c>-Wall -Werror</c> (the toolchain build passes
    /// <c>-w</c>) and the runtime without them, then runs it. A null code means the build failed.
    /// </summary>
    private static (int? Code, string Stdout, string Stderr) BuildStandalone(string[] compiler, string cSource, BuildMode mode)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "run", "l016_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.c"), cSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), Toolchain.RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), Toolchain.RuntimeHeader);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "out.exe" : "out");
            string[] flags = mode == BuildMode.Debug ? ["-O0", "-DBYN_DEBUG"] : ["-O2", "-DBYN_NDEBUG"];
            foreach (var args in new[]
            {
                ["-std=c11", "-fwrapv", "-Wall", "-Werror", .. flags, "-I.", "-c", "out.c", "-o", "out.o"],
                ["-std=c11", "-fwrapv", "-w", .. flags, "-I.", "-c", "bayan-rt.c", "-o", "rt.o"],
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
