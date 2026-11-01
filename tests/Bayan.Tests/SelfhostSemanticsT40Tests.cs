using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>
/// T40: a <c>for try</c> synthetic name (<c>&lt;sequence N&gt;</c>, <c>next</c>; binder3.byn <c>t28SyntheticName</c>)
/// reports at the iterable, as the C# <c>new Name(span, …)</c> with <c>span = f.Iterable.Span</c> does (Binder.cs:1110-1115).
/// <c>check</c>/<c>golden</c> run <c>bayan-check</c> on <c>diag/for-try-next-not-visible</c> against <c>bayan check</c> and
/// <c>DiagTests</c>; <c>spans</c> compiles a <c>for try</c> over a <c>var</c> (stepped in place) and over a call (stepped in
/// a hidden <c>&lt;sequence N&gt;</c>) and prints every recorded diagnostic's raw span, fix span and related spans, as
/// the C# <c>Compilation.Create</c> reports them; <c>map</c> is <c>addDiag</c>'s mapping of a fix and a related span.
/// </summary>
public sealed class SelfhostSemanticsT40Tests
{
    private const string Case = "diag/for-try-next-not-visible/main.byn";

    private const string Rows = """
        public union error ReadError { broken }

        public struct Rows {
            public i64 at

            i64? next(ref this) fails ReadError {
                at += 1
                return at > 2 ? null : at
            }
        }

        public Rows start() => Rows { at: 0 }
        """;

    /// <summary>Each program's <c>main.byn</c>, beside <see cref="Rows"/> as <c>rows/rows.byn</c>.</summary>
    private static readonly (string Name, string Main)[] Programs =
    [
        ("inplace", """
            import rows

            void go() fails rows.ReadError {
                var r = rows.start()
                for try x in r { print($"{x}") }
            }
            """),
        ("copy", """
            import rows

            void go() fails rows.ReadError {
                for try x in rows.start() { print($"{x}") }
                for try y in (rows.start()) { print($"{y}") }
            }
            """),
    ];

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), Programs.SelectMany(p => new[] { p.Name, Dir(p.Name) }).ToArray()));

    /// <summary><c>bayan-check</c> on the case reports BYN2005 at the iterable and exits like <c>bayan check</c>.</summary>
    [Fact]
    public void CheckReportsAtTheIterable() => SelfhostSemanticsTests.CheckSame(Case);

    /// <summary><c>bayan-check --golden</c> renders the case as <c>DiagTests</c> does.</summary>
    [Fact]
    public void GoldenRendersAtTheIterable() => SelfhostSemanticsTests.GoldenSame(Case);

    /// <summary>Every recorded span of a <c>for try</c> program is the C#'s, so none is negative.</summary>
    [Theory]
    [InlineData("inplace")]
    [InlineData("copy")]
    public void SpansAreTheCSharps(string name)
    {
        var lines = Expected(Dir(name));
        Assert.NotEmpty(lines);
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"spans.{name}", lines);
        Assert.All(Sections.Value[$"spans.{name}"], l => Assert.DoesNotContain(" -", l));
    }

    /// <summary>
    /// <c>addDiag</c> maps a synthetic fix span and related span to the recorded iterable span and leaves others.
    /// </summary>
    [Fact]
    public void AddDiagMapsFixAndRelated() =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "map", ["span 7..10 fix 7..10 related 7..10 2..3"]); // no-oracle: Q740

    /// <summary>The program's directory under <c>tmp/t40/</c>, written once.</summary>
    private static string Dir(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t40", name);
        Directory.CreateDirectory(Path.Combine(dir, "rows"));
        File.WriteAllText(Path.Combine(dir, "main.byn"), Programs.Single(p => p.Name == name).Main + "\n");
        File.WriteAllText(Path.Combine(dir, "rows", "rows.byn"), Rows + "\n");
        return dir;
    }

    /// <summary>The C# side: <c>DiagTests</c>' compilation of <c>main.byn</c> with its directory as root.</summary>
    private static List<string> Expected(string dir)
    {
        var c = Compilation.Create([Golden.Load(Path.Combine(dir, "main.byn"))], dir, new CompilationOptions());
        return c.Diagnostics.Items.Select(d =>
            $"{d.Code} {d.Span.Start}..{d.Span.End}" +
            (d.Fix is { } f ? $" fix {f.Span.Start}..{f.Span.End}" : "") +
            string.Concat((d.Related ?? []).Select(r => $" related {r.Span.Start}..{r.Span.End}"))).ToList();
    }

    private static string MainSource() => """
        import lexer
        import parser

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 1 < args.length {
                let name = args[i]
                let dir = args[i + 1]
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("main.byn", from: t11ReadAllBytes($"{dir}/main.byn")))
                let options = CompilationOptions { capabilities: null, packageName: null }
                var c = create(from: move sources, at: dir, by: in options)
                print($"== spans.{name}")
                for d in c.diagnostics {
                    var sb = StrBuf()
                    sb.append($"{d.code} {d.span.start}..{d.span.end}")
                    if d.hasFix { sb.append($" fix {d.fixSpan.start}..{d.fixSpan.end}") }
                    for r in d.related { sb.append($" related {r.span.start}..{r.span.end}") }
                    print($"{sb.finish()}")
                }
                i += 2
            }
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
            }
            let name = t28SyntheticName(ref c, for: "next", at: TextSpan { start: 7, length: 3 })
            var related = List<Related>()
            related.append(Related { span: name, label: "" })
            related.append(Related { span: TextSpan { start: 2, length: 1 }, label: "" })
            addDiag(ref c, by: Diag { code: "BYN2005", file: -1, span: name, message: "", hasFix: true, fixSpan: name, related: move related })
            print("== map")
            for d in c.diagnostics {
                print($"span {d.span.start}..{d.span.end} fix {d.fixSpan.start}..{d.fixSpan.end} related {d.related[0].span.start}..{d.related[0].span.end} {d.related[1].span.start}..{d.related[1].span.end}")
            }
            return 0
        }
        """;
}
