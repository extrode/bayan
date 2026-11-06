using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 014 T5: the selfhost emitter's C for <c>Path.directoryName</c>, <c>Path.combine</c> (both forms) and <c>Path.fullPath</c>
/// against <c>CliApp.EmitC</c>, in <c>fast</c> and <c>debug</c>. Every expected text is computed by the C#.
/// </summary>
public sealed class SelfhostPathT5Tests
{
    private static readonly (string Name, string Text)[] Programs =
    [
        ("directory-name", """
            void main() uses alloc {
                let d: string? = Path.directoryName("/a/b")
                print(d ?? "")
            }
            """),
        ("combine", """
            void main() uses alloc {
                print(Path.combine("/a", "b"))
            }
            """),
        ("combine-span", """
            void main() uses alloc {
                let parts: ReadOnlySpan<string> = ["/a", "b", "c"]
                print(Path.combine(parts))
            }
            """),
        ("full-path", """
            void main() uses alloc {
                print(Path.fullPath("x"))
            }
            """),
        ("all", """
            void main() uses alloc {
                let d: string? = Path.directoryName("/a/b")
                let parts: ReadOnlySpan<string> = ["/a", "b"]
                print(Path.combine("/a", "b"))
                print(Path.combine(parts))
                print(Path.fullPath("x"))
                print(Path.relative("/a", "/a/b"))
                print(d ?? "")
            }
            """),
    ];

    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (name, _) in Programs)
                foreach (var mode in new[] { "fast", "debug" }) data.Add(name, mode);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmitsTheSameC(string name, string mode)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t5-path", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, Programs.First(p => p.Name == name).Text.ReplaceLineEndings("\n") + "\n");
        var c = Compilation.Create([SourceText.FromFile(file)], dir);
        Assert.False(c.Diagnostics.HasErrors, $"{name}: the C# rejects the program");
        Assert.True(Toolchain.TryParseMode(mode, out var buildMode));
        var (exit, output, error) = SelfhostEmitterHarness.Run("--mode", mode, "--package", dir, file);
        Assert.True(exit == 0, $"{name} {mode}: exit {exit}\n{error}");
        var expected = CliApp.EmitC(c, buildMode);
        Assert.Contains("byn_path_", expected);
        SelfhostEmitterHarness.AssertSame($"{name} {mode}", expected, output);
    }
}
