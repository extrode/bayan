using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>
/// 014 T4: the selfhost checker's rows for <c>Path.directoryName</c>, <c>Path.combine</c> (both forms) and
/// <c>Path.fullPath</c> against the C# checker. Each program is compiled by the generated <c>create</c> and by the
/// C# <c>Compilation.Create</c>, and every diagnostic is printed as <c>code line:col message</c> with its label and help.
/// Every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostPathT4Tests
{
    private static readonly (string Name, string Text)[] Programs =
    [
        ("each-function", """
            void main() uses alloc {
                let d: string? = Path.directoryName("/a/b")
                let c = Path.combine("/a", "b")
                let f = Path.fullPath("x")
                print(c)
                print(f)
                print(d ?? "")
            }
            """),
        ("combine-span", """
            void main() uses alloc {
                let parts: ReadOnlySpan<string> = ["/a", "b", "c"]
                print(Path.combine(parts))
                print(Path.combine(["a", "b"]))
            }
            """),
        ("combine-one-argument-wrong-type", """
            void main() uses alloc {
                print(Path.combine("a"))
                print(Path.combine(1))
            }
            """),
        ("combine-arity", """
            void main() uses alloc {
                print(Path.combine("a", "b", "c"))
                print(Path.combine())
            }
            """),
        ("missing-alloc", """
            public void directory(string p) {
                let d = Path.directoryName(p)
            }

            public void combine(string p) {
                let c = Path.combine(p, p)
            }

            public void combineSpan(ReadOnlySpan<string> p) {
                let c = Path.combine(p)
            }

            public void full(string p) {
                let f = Path.fullPath(p)
            }

            void main() uses alloc {
                directory("a")
                combine("a")
                combineSpan(["a"])
                full("a")
            }
            """),
        ("unknown-member", """
            void main() uses alloc {
                let a = Path.combin("a", "b")
                let b = Path.fullPat("a")
                let c = Path.directoryNam("a")
                let d = Path.nope("a")
            }
            """),
        ("wrong-argument-type", """
            void main() uses alloc {
                let a = Path.fullPath(1)
                let b = Path.directoryName(true)
                let c = Path.combine("a", 2)
            }
            """),
    ];

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), Programs.SelectMany(p => new[] { p.Name, Dir(p.Name) }).ToArray()));

    [Theory]
    [MemberData(nameof(Names))]
    public void DiagnosticsAreTheCSharps(string name)
    {
        var section = $"diags.{name}";
        var expected = Expected(Dir(name));
        var actual = new Dictionary<string, List<string>> { [section] = Sections.Value[section] };
        if (expected.Count == 0) Assert.Empty(actual[section]);
        else SelfhostSemanticsHarness.AssertSection(actual, section, expected);
    }

    public static TheoryData<string> Names() => new(Programs.Select(p => p.Name));

    /// <summary>The cases that must report something, so a pair of empty outputs cannot pass.</summary>
    [Theory]
    [InlineData("combine-arity")]
    [InlineData("missing-alloc")]
    [InlineData("unknown-member")]
    [InlineData("wrong-argument-type")]
    public void TheCSharpReportsDiagnostics(string name) => Assert.NotEmpty(Expected(Dir(name)));

    /// <summary>The program's directory under <c>tmp/t4-path/</c>, written on each call (the same text each time).</summary>
    private static string Dir(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t4-path", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), Programs.Single(p => p.Name == name).Text + "\n");
        return dir;
    }

    private static List<string> Expected(string dir)
    {
        var c = Compilation.Create([Golden.Load(Path.Combine(dir, "main.byn"))], dir, new CompilationOptions());
        var lines = new List<string>();
        foreach (var d in c.Diagnostics.Items)
        {
            string At(int offset) => d.Source is { } s ? $"{s.GetLinePosition(offset).Line}:{s.GetLinePosition(offset).Column}" : "?";
            lines.Add($"{d.Code} {At(d.Span.Start)} {d.Message}");
            if (!string.IsNullOrEmpty(d.Label)) lines.Add($"  label {d.Label}");
            if (!string.IsNullOrEmpty(d.Help)) lines.Add($"  help {d.Help}");
        }
        return lines.SelectMany(l => l.Split('\n')).Where(l => l.Length > 0).ToList();
    }

    private static string MainSource() => """
        import lexer
        import parser

        string t4At(in Ctx c, for FileId file, at i64 offset) {
            if file < 0 { return "?" }
            let p = linePosition(c.files[file].text.span, at: offset)
            return $"{p.line}:{p.column}"
        }

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 1 < args.length {
                let name = args[i]
                let dir = args[i + 1]
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("main.byn", from: t11ReadAllBytes($"{dir}/main.byn")))
                let options = CompilationOptions { includeTests: false, library: false, skipOwnershipCheck: false, capabilities: null, packageName: null }
                var c = create(from: move sources, at: dir, by: in options)
                print($"== diags.{name}")
                for d in c.diagnostics {
                    print($"{d.code} {t4At(in c, for: d.file, at: d.span.start)} {d.message}")
                    if d.label.length > 0 { print($"  label {d.label}") }
                    if d.help.length > 0 { print($"  help {d.help}") }
                }
                i += 2
            }
            return 0
        }
        """;
}
