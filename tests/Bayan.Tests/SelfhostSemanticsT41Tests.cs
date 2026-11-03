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
        ("ref-this-tuple-field", [("main.byn", """
            struct Box {
                (string a, i64 n) t

                void put(ref this, string v) {
                    t.a = v
                }
            }

            void main() {
                var b = Box { t: (a: "", n: 1) }
                {
                    using let arena = Arena()
                    let name = "ann".copy(to: arena)
                    b.put(name)
                }
                print(b.t.a)
            }
            """)]),
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
        ("builtin-arity", [("main.byn", """
            struct unique U {
                i64 fd
            }

            void main() {
                var sb = StrBuf()
                sb.append()
                var m = Map<string, i64>()
                sb.append(m)
                var xs = List<string>()
                xs.remove(from: 0)
                xs.append()
                m.slot()
                let um = Map<U, i64>()
                let uc = um.copy()
                print(sb.finish())
            }
            """)]),
        ("store-pairs", [("main.byn", """
            struct Box {
                string s

                void give(to ref string t) {
                    t = s
                }

                void fill(ref this, to Span<string> xs) {
                    xs[0] = s
                }
            }

            void many(string a, by string b, to ref string x, at ref string y) {
                y = a
                x = b
                x = a
                y = b
            }

            string make(Arena a) => "x".copy(to: a)

            string make2(Arena a) => make(a)

            void main() {
                var kept = ""
                var kept2 = ""
                {
                    using let arena = Arena()
                    let n1 = "a".copy(to: arena)
                    let n2 = "b".copy(to: arena)
                    many(n1, by: n2, to: ref kept, at: ref kept2)
                    let local = Box { s: n1 }
                    local.give(to: ref kept)
                    kept = make2(arena)
                }
                print($"{kept}{kept2}")
            }
            """)]),
        ("call-arity", [("main.byn", """
            void many(string a, by string b, to ref string x, at ref string y) {
                y = a
                x = b
            }

            ReadOnlySpan<string> pick(ref List<string> xs) => xs.span

            void two(ref string a, to ref string b) {}

            void pass(string s, to ref string t) {
                t = s
            }

            void main() {
                var kept = ""
                var kept2 = ""
                var xs = List<string>()
                {
                    using let arena = Arena()
                    let n1 = "a".copy(to: arena)
                    many(n1, by: n1, to: ref kept)
                    many(n1, by: n1, to: ref kept, at: ref kept2, n1)
                    let p = pick(ref xs, n1)
                }
                two(ref kept, to: ref kept)
                pass(kept, to: ref kept)
                print($"{kept}{kept2}")
            }
            """)]),
        ("broad-ref", [("main.byn", """
            interface IKeeper {
                void keep(ref this, string v, to ref string t)
            }

            struct K : IKeeper {
                string s

                void keep(ref this, string v, to ref string t) {
                    t = v
                }
            }

            void viaDyn(dyn ref IKeeper k, by string v, to ref string t) {
                k.keep(v, to: ref t)
                k.keep(v, to: ref t, v)
            }

            void main() {
                var k = K { s: "" }
                var kept = ""
                {
                    using let arena = Arena()
                    let name = "ann".copy(to: arena)
                    viaDyn(k, by: name, to: ref kept)
                    viaDyn(5, by: name, to: ref kept)
                }
                print(kept)
            }
            """)]),
        ("context-builtins", [("main.byn", """
            string runs(ReadOnlySpan<string> none) fails IoError {
                using let arena = Arena()
                provide allocator: arena {
                    let r = try Io.run("true", none)
                    let rb = try Io.runBatch("x.bat", none)
                    print($"{r.exitCode}{rb.exitCode}")
                }
                return ""
            }

            string texts(ReadOnlySpan<u8> raw) fails Utf8Error {
                using let arena = Arena()
                provide allocator: arena {
                    let a = try Utf8.decode(raw)
                    let b = Utf8.tryDecode(raw) ?? ""
                    let d = Utf8.decode(raw, invalid: .replace)
                    let h = Hex.encode(raw)
                    var buf = StrBuf.over(stackalloc u8[8])
                    buf.append(a)
                    print($"{a}{b}{d}{h}{buf.view}")
                }
                return ""
            }

            void main() {
                let none = List<string>()
                let raw = List<u8>()
                let _ = runs(none.span) catch switch { _ => "" }
                let _ = texts(raw.span) catch switch { _ => "" }
            }
            """)]),
        ("multi-file", [("main.byn", """
            import lib

            void main() {
                var kept = ""
                {
                    using let arena = Arena()
                    let name = "ann".copy(to: arena)
                    lib.keep(name, to: ref kept)
                }
                print(kept)
            }
            """), ("lib/lib.byn", """
            public void keep(string s, to ref string t) {
                t = s
            }
            """)]),
        ("misc-shapes", [("main.byn", """
            @positional
            struct P {
                i64 x
                i64 y
            }

            void f(rest i64 x) {}

            void main() {
                let p = P(1, 2)
                f(1)
                var xs = List<i64>()
                print($"{p.x}{xs.length}")
            }
            """)]),
        ("static-ref-this", [("main.byn", """
            struct S {
                string s

                static void g(ref this) {}
            }

            void main() {
                S.g()
            }
            """)]),
        ("loop-alloc", [("main.byn", """
            interface ISink {
                void put(ref this, string v)
            }

            struct Box : ISink {
                string s

                void put(ref this, string v) {}
            }

            struct Boxes : IIndexable {
                type Item = Box
                i64 count => 1
                Box get(i64 index) => Box { s: "" }
            }

            string make(Arena a) => "x".copy(to: a)

            string make3(i64 n, by Arena a) => make(a)

            string both(bool f, by Arena a, at Arena b) => make(f ? a : b)

            void use(Arena a) {
                let s = "y".copy(to: a)
                print(s)
            }

            void consume(List<i64> xs) {}

            void main() {
                let bs = Boxes()
                for d: dyn ref ISink in bs {
                    d.put("a")
                }
                var m = Map<string, Box>()
                for d: dyn ref ISink in m {
                    d.put("a")
                }
                var sb = StrBuf()
                let fv: i64 -> i64 = x => x
                sb.append(fv)
                var xs = List<i64>()
                consume(move (xs))
                let t = make(Arena())
                print(t)
            }
            """)]),
        ("iface-default", [("main.byn", """
            interface IGrow {
                void grow(ref this, to ref string t)

                void twice(ref this, to ref string t) {
                    grow(to: ref t)
                }
            }

            struct G : IGrow {
                string s

                void grow(ref this, to ref string t) {
                    t = s
                }
            }

            struct C {
                string s
                string name => s
            }

            enum Color {
                red
                green
            }

            string make(Arena a) => "x".copy(to: a)

            string pair(Arena a, by Arena b) => make(b)

            void main() {
                var g = G { s: "" }
                var kept = ""
                g.twice(to: ref kept)
                let c = C { s: "a" }
                let k = Color.fromOrdinal(0)
                let q = Color.parse("red")
                {
                    using let a1 = Arena()
                    using let a2 = Arena()
                    kept = pair(a1, by: a2)
                }
                print($"{kept}{c.name}")
            }
            """)]),
        ("alloc-roots", [("main.byn", """
            interface IPoke {
                void grow(ref this)

                void twice(ref this) {
                    poke(ref this)
                }
            }

            struct G : IPoke {
                string s

                void grow(ref this) {}
            }

            struct H {
                Arena arena
            }

            void poke<T>(ref T x) where T : IPoke {}

            string make(Arena a) => "x".copy(to: a)

            void use2(Arena a) {
                let s = make(a)
                print(s)
            }

            string viaField(in H h) => make(h.arena)

            void main() {
                var g = G { s: "" }
                g.twice()
                using let arena = Arena()
                use2(arena)
                print(make(Arena()))
            }
            """)]),
        ("trust-span", [("main.byn", """
            void main() {
                let xs: u8[2] = [1, 2]
                let u = Untrusted(xs.span)
                let s = u.trustUnchecked() because "ok"
                print($"{s.length}")
            }
            """)]),
        ("generic-iface-default", [("main.byn", """
            interface IBox<T> {
                T get()

                T twice() {
                    return get()
                }
            }

            struct Box : IBox<string> {
                string s

                string get() => s
            }

            void main() {
                var b = Box { s: "a" }
                let d: dyn ref IBox<string> = b
                let t = d.twice()
                print(t)
            }
            """)]),
        ("tests-no-main", [("main.byn", """
            void helper() {}
            """)]),
        ("library-no-main", [("main.byn", """
            public void helper() {}
            """)]),
        ("skip-ownership", [("main.byn", """
            void main() {
                print("x")
            }
            """)]),
        ("builtin-names", [("main.byn", """
            struct Money {
                i64 cents

                public void formatInto(ref StrBuf output, as FormatSpec spec) {
                    let w: i64 = spec
                }
            }

            void main() {
                let a: i64 = Io
                let b: i64 = Hex
                let c: i64 = Utf8
                let d: i64 = Panic
                let e: i64 = Io.stdout
                let f: i64 = Io.stdin
                let g: i64 = Io.run("x", [])
                let h = Io.rea
                let i = Io.zzzzqq
                let j = Hex.encod
                let k = Hex.zzzzqq
                let l = Utf8.decod
                let m = Utf8.zzzzqq
                let n = Path.relativ
                let o = Path.zzzzqq
                let q = string.invalidUtf8At
                let r = string.invalidUtf8A
                let s = string.zzzzqq
                let t = string.from
                let u = string.fromUtf8
                let v = string.tryFrom
            }
            """)]),
        ("type-names", [("main.byn", """
            struct S {
                i64 a
                i64 p => a

                void inst() {}
                static void st() {}
            }

            void main() {
                let a = List
                let b = Map
                let c = Span
                let d = ReadOnlySpan
                let e = Arena
                let f = StrBuf
                let g = IoError
                let h = Utf8Error
                let i = ParseError
                let j = ValidationError
                let k = ProcessResult
                let l = IoError.Notfond
                let m = IoError.zzzzqq
                let n = S.inst
                let o = S.a
                let q = S.p
                let r = S.zzzzqq
                let s = S.st
                let t = Symbol.inter
                let u = Symbol.zzzzqq
                let v = Symbol.intern
                let w = Io.readFil
                let x = Arena.x
                let y = StrBuf.x
            }
            """)]),
        ("name-shapes", [("main.byn", """
            struct Box {
                i64 v

                T pick<T>(T x) => x
                i64 get<T>(T x) => v
            }

            struct D {
                i64 a = 1
                i64 b = zzz
            }

            const k: i64[2] = [1, 2]

            void main() {
                const cc = k
                let ee = cc
                const dd = 3
                let ff = dd
                let bx = Box { v: 1 }
                print($"{bx.get(1)}{bx.pick(2)}{ee}{ff}")
            }
            """)]),
        ("state-fields", [("main.byn", """
            struct Order {
                i64 id
                states {
                    draft
                    paid(i64 payment)
                }

                i64 amount(this is .paid) => payment

                i64 viaLambda(this is .paid) {
                    let f: () -> i64 = () => payment
                    return f()
                }
            }

            void main() {
                let o = Order.paid { id: 1, payment: 2 }
                print($"{o.amount()}{o.viaLambda()}")
            }
            """)]),
        ("no-files", []),
    ];

    /// <summary>The options a program compiles with on both sides, by the flag the generated main reads.</summary>
    private static readonly Dictionary<string, string> Flags = new()
    {
        ["tests-no-main"] = "tests",
        ["library-no-main"] = "library",
        ["skip-ownership"] = "skip",
        ["no-files"] = "empty",
    };

    private static string Flag(string name) => Flags.GetValueOrDefault(name, "-");

    /// <summary>Every single-file diag and run golden: the Q745 diagnostics, and the summaries of programs that compile.</summary>
    private static readonly string[] Goldens = Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "diag"), "*.byn")
        .Concat(Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "run"), "*.byn"))
        .Select(f => Path.GetRelativePath(Golden.CasesRoot, f)[..^4].Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();

    private static readonly string[] All = [.. Custom.Select(p => p.Name), .. Goldens];

    public static TheoryData<string> Names() => new(All);

    /// <summary>Argument characters of one run: each argument plus one separator.</summary>
    private static int ArgChars(string name) => name.Length + Dir(name).Length + Flag(name).Length + 3;

    /// <summary>Consecutive chunks of <see cref="All"/>; 29,000 leaves room under the 32,767 limit for the program path.</summary>
    private static List<List<string>> Chunks()
    {
        var chunks = new List<List<string>> { new() };
        var used = 0;
        foreach (var n in All)
        {
            if (used + ArgChars(n) > 29_000 && chunks[^1].Count > 0)
            {
                chunks.Add(new());
                used = 0;
            }
            chunks[^1].Add(n);
            used += ArgChars(n);
        }
        return chunks;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chunk in Chunks())
        {
            var part = SelfhostSemanticsHarness.RunSections(MainSource(), chunk.SelectMany(n => new[] { n, Dir(n), Flag(n) }).ToArray());
            foreach (var (key, lines) in part)
            {
                if (key == "" && merged.TryGetValue("", out var preamble))
                {
                    Assert.Equal(preamble, lines);
                    continue;
                }
                Assert.True(merged.TryAdd(key, lines), $"section '{key}' printed by two chunks");
            }
        }
        return merged;
    });

    /// <summary>No single run's arguments reach 30,000 characters, and the chunks hold every case once, in order.</summary>
    [Fact]
    public void RunArgumentsStayUnderTheCommandLineLimit()
    {
        var chunks = Chunks();
        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
            Assert.True(chunk.Sum(ArgChars) < 30_000, $"chunk of {chunk.Count} cases has {chunk.Sum(ArgChars)} argument characters");
        Assert.Equal(All, chunks.SelectMany(c => c).ToArray());
    }

    /// <summary>Every diagnostic of the program, with its notes, and every store and return summary, is the C#'s.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void DiagnosticsAreTheCSharps(string name)
    {
        var section = $"diags.{name}";
        var expected = Sorted(Expected(Dir(name), Flag(name)));
        var actual = new Dictionary<string, List<string>> { [section] = Sorted(Sections.Value[section]) };
        // A program with no diagnostic and no summary prints nothing on either side.
        if (expected.Count == 0) Assert.Empty(actual[section]);
        else SelfhostSemanticsHarness.AssertSection(actual, section, expected);
    }

    /// <summary>
    /// <c>checkOutlives</c> (region2.byn), T37's entry point, which T41 made a wrapper of <c>t41CheckOutlives</c>: no
    /// program reaches it, so T37's own oracle cases for it run here too, under T41's filter.
    /// </summary>
    [Theory]
    [InlineData("outlives")]
    [InlineData("outlives.none")]
    public void CheckOutlivesIsTheCSharps(string name) => new SelfhostSemanticsT37Tests().CaseMatches(name);

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
        Directory.CreateDirectory(dir);
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text.EndsWith('\n') ? text : text + "\n");
        }
        return dir;
    }

    /// <summary>
    /// The C# side: <c>DiagTests</c>' compilation of <c>main.byn</c> with its directory as root, with the options
    /// <paramref name="flag"/> names (<c>empty</c>: no entry file at all).
    /// </summary>
    private static List<string> Expected(string dir, string flag)
    {
        var options = new CompilationOptions(IncludeTests: flag == "tests", Library: flag == "library", SkipOwnershipCheck: flag == "skip");
        var c = Compilation.Create(flag == "empty" ? [] : [Golden.Load(Path.Combine(dir, "main.byn"))], dir, options);
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
            while i + 2 < args.length {
                let name = args[i]
                let dir = args[i + 1]
                let flag = args[i + 2]
                var sources = List<lexer.SourceText>()
                if flag != "empty" { sources.append(lexer.SourceText.create("main.byn", from: t11ReadAllBytes($"{dir}/main.byn"))) }
                let options = CompilationOptions { includeTests: flag == "tests", library: flag == "library", skipOwnershipCheck: flag == "skip", capabilities: null, packageName: null }
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
                i += 3
            }
            return 0
        }
        """;
}
