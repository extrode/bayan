using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using TextSpan = Bayan.Syntax.Text.TextSpan;

namespace Bayan.Tests;

/// <summary>
/// T41: the Q745 store and return summaries (region.byn <c>summarizeRegions</c>, region2.byn <c>callBorrow</c>'s store loop,
/// <c>t41Stores</c>, <c>t41StoreBorrow</c>, <c>t41Record</c>) against the C# <c>RegionAnalysis</c> and
/// <c>Compilation.StoreSummaries</c>/<c>ReturnSummaries</c>. Each program is compiled by the generated <c>create</c> and by
/// the C# <c>Compilation.Create</c>, and every diagnostic is printed as <c>code line:col message</c> with its label, help,
/// fix and related spans (the "`f` stores `x` into `y` here" note). The <c>diag/ref-out-escape-*</c> goldens run too.
/// Every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT41Tests
{
    /// <summary>Each program: its name and its files, <c>main.byn</c> first, relative to its directory.</summary>
    private static readonly (string Name, (string Path, string Text)[] Files)[] Custom =
    [
        ("broad", [("main.byn", """
            interface Sink {
                void put(ref this, string v)
            }

            struct Box : Sink {
                string s

                void put(ref this, string v) {
                    s = v
                }
            }

            void viaDyn(dyn ref Sink k, string v) {
                k.put(v)
            }

            void main() {
                var b = Box { s: "" }
                {
                    using let arena = Arena()
                    let name = "ann".copy(to: arena)
                    viaDyn(b, name)
                    let k: dyn ref Sink = b
                    k.put(name)
                }
                print(b.s)
            }
            """)]),
        ("arena-result", [("main.byn", """
            string make(Arena a) => "x".copy(to: a)

            string apply((string -> string) f, string s) => f(s)

            void main() {
                var kept = ""
                {
                    using let arena = Arena()
                    kept = make(arena)
                    let t = "y".copy(to: arena)
                    kept = apply(x => t, "z")
                }
                print(kept)
            }
            """)]),
        ("builtins", [("main.byn", """
            struct P {
                i64 n
            }

            const table: i64[3] = [1, 2, 3]

            void main() {
                var sb = StrBuf()
                sb.append(1)
                sb.append("a")
                sb.append(P { n: 1 })
                sb.append($"{2}")
                let f = sb.finish()
                let v = sb.view
                for p in "a,b".parts(by: ',') { print(p) }
                for p in "a,b".parts(by: ",") { print(p) }
                for p in "a,b".parts() { print(p) }
                var xs = List<i64>()
                xs.append(1)
                xs.append(2)
                xs.append(3)
                xs.remove(at: 0)
                xs.remove(at: 0..1)
                let rg = 0..0
                xs.remove(at: rg)
                xs.remove(from: 0, until: 0)
                xs.remove(from: 0, through: 0)
                let t1 = " a ".trimmed()
                let t2 = " a ".trimmed(from: .start)
                let t3 = "-a-".trimmed(by: ['-'])
                let e1 = "a".equals("A", as: .loose)
                var m = Map<string, P>()
                m.slot("a").n = 1
                var ls = List<List<i64>>()
                let lc = ls.copy()
                var mm = Map<string, List<i64>>()
                let mc = mm.copy()
                var mk = Map<i64, i64>()
                let mkc = mk.copy()
                let ys = xs.take()
                let ts = table.mutableSpan()
                let input = Io.stdin.readAll()
                print($"{f}{v}{t1}{t2}{t3}{e1}{ys.length}{ts.length}")
            }
            """)]),
    ];

    /// <summary>The single-file diag goldens the region analysis reports on (BYN30xx), the Q745 ones among them.</summary>
    private static readonly string[] Goldens = Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "diag"), "*.byn")
        .Concat(Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "run"), "*.byn"))
        .Select(f => Path.GetRelativePath(Golden.CasesRoot, f)[..^4].Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();

    private static readonly string[] All = [.. Custom.Select(p => p.Name), .. Goldens];

    public static TheoryData<string> Names() => new(All);

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), All.SelectMany(n => new[] { n, Dir(n) }).ToArray()));

    /// <summary>Every diagnostic of the program, with its notes, and every store and return summary, is the C#'s.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void DiagnosticsAreTheCSharps(string name)
    {
        var section = $"diags.{name}";
        var expected = Sorted(Expected(Dir(name)));
        var actual = new Dictionary<string, List<string>> { [section] = Sorted(Sections.Value[section]) };
        // A program with no diagnostic and no summary prints nothing on either side.
        if (expected.Count == 0) Assert.Empty(actual[section]);
        else SelfhostSemanticsHarness.AssertSection(actual, section, expected);
    }

    /// <summary>
    /// The diagnostics in order, then the summary lines sorted: the selfhost prints them by function id, the C#
    /// dictionaries hold them by insertion. A return summary is a set (the C# <c>HashSet</c>), so its indices are sorted too.
    /// </summary>
    private static List<string> Sorted(List<string> lines)
    {
        static bool IsSummary(string l) => l.StartsWith("store ", StringComparison.Ordinal) || l.StartsWith("returns ", StringComparison.Ordinal);
        static string Set(string l)
        {
            if (!l.StartsWith("returns ", StringComparison.Ordinal)) return l;
            var words = l.Split(' ');
            return string.Join(' ', words.Take(2).Concat(words.Skip(2).Select(int.Parse).Order().Select(k => k.ToString())));
        }
        return [.. lines.Where(l => !IsSummary(l)), .. lines.Where(IsSummary).Select(Set).Order(StringComparer.Ordinal)];
    }

    /// <summary>The program's directory under <c>tmp/t41/</c>, written on each call (the same text each time).</summary>
    private static string Dir(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t41", name.Replace('/', '-'));
        var files = Custom.Any(p => p.Name == name)
            ? Custom.Single(p => p.Name == name).Files
            : [("main.byn", File.ReadAllText(Path.Combine(Golden.CasesRoot, name + ".byn")))];
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text.EndsWith('\n') ? text : text + "\n");
        }
        return dir;
    }

    /// <summary>The C# side: <c>DiagTests</c>' compilation of <c>main.byn</c> with its directory as root.</summary>
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
            if (d.Fix is { } f) lines.Add($"  fix {At(f.Span.Start)}-{At(f.Span.End)} {f.Replacement}");
            foreach (var r in d.Related ?? []) lines.Add($"  related {At(r.Span.Start)}-{At(r.Span.End)} {r.Label}");
        }
        foreach (var (fn, pairs) in Summaries<Dictionary<(int Source, int Target), TextSpan>>(c, "StoreSummaries"))
            foreach (var ((source, target), site) in pairs) lines.Add($"store {fn.Name} {source} {target} {site.Start}..{site.End}");
        foreach (var (fn, returned) in Summaries<HashSet<int>>(c, "ReturnSummaries"))
            lines.Add($"returns {fn.Name}" + string.Concat(returned.Select(k => $" {k}")));
        // The selfhost prints each multi-line text line by line and drops empty lines.
        return lines.SelectMany(l => l.Split('\n')).Where(l => l.Length > 0).ToList();
    }

    /// <summary>The internal <c>Compilation.StoreSummaries</c> or <c>ReturnSummaries</c>, read by reflection.</summary>
    private static Dictionary<FunctionSymbol, T> Summaries<T>(Compilation c, string name) =>
        (Dictionary<FunctionSymbol, T>)typeof(Compilation).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c)!;

    private static string MainSource() => """
        import lexer
        import parser

        string t41At(in Ctx c, for FileId file, at i64 offset) {
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
                let options = CompilationOptions { capabilities: null, packageName: null }
                var c = create(from: move sources, at: dir, by: in options)
                print($"== diags.{name}")
                for d in c.diagnostics {
                    print($"{d.code} {t41At(in c, for: d.file, at: d.span.start)} {d.message}")
                    if d.label.length > 0 { print($"  label {d.label}") }
                    if d.help.length > 0 { print($"  help {d.help}") }
                    if d.hasFix { print($"  fix {t41At(in c, for: d.file, at: d.fixSpan.start)}-{t41At(in c, for: d.file, at: d.fixSpan.end)} {d.fixReplacement}") }
                    for r in d.related { print($"  related {t41At(in c, for: d.file, at: r.span.start)}-{t41At(in c, for: d.file, at: r.span.end)} {r.label}") }
                }
                for f in 0..c.functions.length {
                    let fn = FuncId(f)
                    if c.storeSummaries.contains(fn) {
                        for p in c.storeSummaries[fn] {
                            let site = p.site ?? TextSpan { start: -1, length: 0 }
                            print($"store {c.functions[fn].name} {p.source} {p.target} {site.start}..{site.end}")
                        }
                    }
                    if c.returnSummaries.contains(fn) {
                        var sb = StrBuf()
                        sb.append($"returns {c.functions[fn].name}")
                        for k in c.returnSummaries[fn] { sb.append($" {k}") }
                        print($"{sb.finish()}")
                    }
                }
                i += 2
            }
            return 0
        }
        """;
}
