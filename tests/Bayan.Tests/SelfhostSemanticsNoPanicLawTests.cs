using System.Diagnostics;
using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L023: over every golden, the checker generator's seeds and random byte and token mutations of them,
/// <c>bayan-check --many</c> never panics and its <c>--spans</c> walker reports exactly the inputs on which the
/// C# checker itself has a diagnostic span outside the file. The binary is built as
/// <see cref="SelfhostSemanticsCorpusParityTests"/> builds it, processes run through <see cref="SelfhostParserTests.Run"/>,
/// and the seeds come from <see cref="SelfhostSemanticsGeneratorTests.Corpus"/>; the corpus shape is L018's.
/// </summary>
public sealed class SelfhostSemanticsNoPanicLawTests(ITestOutputHelper output)
{
    private const int FullRandomCorpusSize = 10_000;

    /// <summary>The quick tier's random inputs (Q748): the first 1,000 of the full corpus, which the seeded generator
    /// writes in the same order whatever its size. <c>BAYAN_LAW_TIER=full</c> runs all of <see cref="FullRandomCorpusSize"/>.</summary>
    private const int QuickRandomCorpusSize = 1_000;

    private static int RandomCorpusSize =>
        Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full" ? FullRandomCorpusSize : QuickRandomCorpusSize;

    private const ulong RandomSeed = 0x9E3779B97F4A7C15;

    private static string Root => Path.Combine(Golden.RepoRoot, "tmp", "semantics-nopanic");

    private static string BayanCheck =>
        SelfhostSemanticsHarness.Build(File.ReadAllText(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, "main.byn")));

    /// <summary>Every golden kind with <c>*.byn</c> inputs.</summary>
    private static readonly string[] GoldenKinds = ["diag", "typed", "run", "parse", "lex"];

    /// <summary>
    /// Law L023: on every corpus input <c>bayan-check --many</c> exits 0 or 1 without <c>panic:</c>, and prints a
    /// <c>span out of range</c> line exactly on the inputs where the C# checker has a diagnostic span outside the file.
    /// </summary>
    [Fact]
    [Trait("Law", "L023")]
    public void BayanCheckNeverPanicsAndEveryDiagnosticSpanLiesInItsFile()
    {
        var clock = Stopwatch.StartNew();
        var flat = FlatGoldens();
        output.WriteLine("golden inputs: " + string.Join(", ", GoldenKinds.Select(k => $"{k} {flat.Count(p => p.Name.StartsWith(k + "-", StringComparison.Ordinal))}")));
        var seeds = SelfhostSemanticsGeneratorTests.Corpus(SelfhostSemanticsGenerator.Seeds)
            .Select(p => (Name: p.Name, Bytes: Encoding.UTF8.GetBytes(p.Source))).ToArray();
        var totals = new Totals();
        CheckCorpus("golden", Path.Combine(Root, "golden"), flat, totals);
        foreach (var dir in GoldenKinds.SelectMany(k => Directory.GetDirectories(Path.Combine(Golden.CasesRoot, k))).Order(StringComparer.Ordinal))
            CheckCorpus($"golden {Path.GetRelativePath(Golden.CasesRoot, dir)}", dir, null, totals);
        CheckCorpus("seeds", Path.Combine(Root, "seeds"), seeds, totals);

        var bases = flat.Concat(seeds).Select(p => p.Bytes).ToArray();
        var random = GenerateRandomCorpus(bases, RandomSeed);
        Assert.False(random.SequenceEqual(GenerateRandomCorpus(bases, RandomSeed + 1), ByteArrayComparer.Instance),
            "two random seeds wrote the same corpus");
        Assert.True(random.SequenceEqual(GenerateRandomCorpus(bases, RandomSeed), ByteArrayComparer.Instance),
            "one random seed wrote two corpora");
        CheckCorpus("random", Path.Combine(Root, "random"), random.Select((b, i) => ($"{i:D5}.byn", b)).ToArray(), totals);

        output.WriteLine($"total: {totals.Inputs} inputs, {totals.Compared} compared, {totals.Checked} without a lexer or parser diagnostic, "
                         + $"{totals.Panicked.Count} panicked, "
                         + $"{totals.Replaced.Count} replaced (C# throws), {totals.SameRendering} rendered as C# does, "
                         + $"{clock.ElapsedMilliseconds} ms");
        Assert.True(totals.Panicked.Count == 0, $"bayan-check panicked:\n{string.Join("\n", totals.Panicked)}");
        Assert.Equal(totals.Inputs, totals.Compared);
        Assert.Equal(KnownCSharpThrows.Where(t => !t.StartsWith("random/", StringComparison.Ordinal) || int.Parse(t[7..12]) < RandomCorpusSize), totals.Replaced);

        clock.Restart();
        var checkedPaths = CheckCli(flat);
        output.WriteLine($"cli: {checkedPaths} golden inputs through `bayan-check --spans <path>` in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// C# counterexamples (Q662): the corpus inputs on which the C# checker itself throws, as
    /// <c>&lt;corpus&gt;/&lt;input&gt;</c>. Each is replaced by an empty input; the test asserts the list exactly, so a
    /// new C# throw fails it. The indices follow <see cref="FlatGoldens"/>'s ordinal file order, so they are the same on every file system.
    /// Empty since 010 T45: the four Q662 inputs were the <c>Parser.ReportBraceless</c> throw, fixed in C# and in the port.
    /// </summary>
    private static readonly string[] KnownCSharpThrows = [];

    private sealed class Totals
    {
        public int Inputs, Compared, SameRendering, Checked;
        public readonly List<string> Panicked = [];
        public readonly List<string> Replaced = [];
    }

    /// <summary>
    /// The default CLI path over the goldens: each flat golden alone in its own directory under
    /// <c>tmp/semantics-nopanic/cli/</c> (<c>bayan check</c> on a file reads its whole directory, CliApp.cs:274),
    /// and each directory case's <c>main.byn</c> in a copy of the case, run as <c>bayan-check --spans &lt;path&gt;</c>:
    /// the exit is 0 or 1, no <c>panic:</c>, no <c>span out of range</c> line. Returns the number of paths run.
    /// </summary>
    private int CheckCli((string Name, byte[] Bytes)[] flat)
    {
        var root = Path.Combine(Root, "cli");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        var paths = new List<string>();
        foreach (var (name, bytes) in flat)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name[(name.IndexOf('-') + 1)..]);
            File.WriteAllBytes(path, bytes);
            paths.Add(path);
        }
        foreach (var caseDir in GoldenKinds.SelectMany(k => Directory.GetDirectories(Path.Combine(Golden.CasesRoot, k))).Order(StringComparer.Ordinal))
        {
            var dir = Path.Combine(root, Path.GetRelativePath(Golden.CasesRoot, caseDir).Replace('/', '-'));
            foreach (var file in Directory.EnumerateFiles(caseDir, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dir, Path.GetRelativePath(caseDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            paths.Add(Path.Combine(dir, "main.byn"));
        }
        foreach (var path in paths)
        {
            var (exit, stdout, stderr) = SelfhostParserTests.Run(BayanCheck, "--spans", path);
            Assert.True(exit is 0 or 1 && !stderr.Contains("panic:", StringComparison.Ordinal), $"{path}: bayan-check exit {exit}\n{stderr}");
            var spanLines = stdout.Split('\n').Where(l => l.StartsWith("span out of range:", StringComparison.Ordinal)).ToArray();
            Assert.True(spanLines.Length == 0, $"{path}: a diagnostic span lies outside its file\n{string.Join("\n", spanLines)}");
        }
        return paths.Count;
    }

    /// <summary>
    /// Q661's reproducer: a bare <c>return</c> in a test's <c>catch</c> block. The test function's return type is
    /// <c>void</c> (Symbols.cs:207); before the fix the port's was -1 and <c>bayan-check</c> panicked. Compiled with
    /// tests, as <c>bayan test</c> does (CliApp.cs:598), and compared with the C# rendering.
    /// </summary>
    [Fact]
    [Trait("Law", "L023")]
    public void BareReturnInTestCatchMatchesCSharp()
    {
        const string source = "public union error E {\n    bad\n}\n\ni64 f() fails E => 1\n\ntest \"t\" {\n    let _ = f() catch { return }\n}\n";
        var dir = Path.Combine(Root, "q661");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "a.test.byn");
        File.WriteAllText(path, source);
        var c = Compile(dir, "a.test.byn", File.ReadAllBytes(path), null);
        var (exit, stdout, stderr) = SelfhostParserTests.Run(BayanCheck, "--golden", "--tests", "--spans", "--package", dir, path);
        Assert.True(exit == 0 && stderr.Length == 0, $"{path}: exit {exit}\n{stderr}");
        SelfhostSemanticsTests.AssertSameLines(path, "golden", DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: false), stdout);
    }

    /// <summary>
    /// Every top-level <c>*.byn</c> of <see cref="GoldenKinds"/>, named <c>&lt;kind&gt;-&lt;file&gt;</c> so the
    /// <c>.test.byn</c> suffix (and with it <c>--tests</c>) survives the copy.
    /// </summary>
    private static (string Name, byte[] Bytes)[] FlatGoldens() =>
        GoldenKinds.SelectMany(k => Directory.GetFiles(Path.Combine(Golden.CasesRoot, k), "*.byn").Order(StringComparer.Ordinal)
                .Select(f => (Name: $"{k}-{Path.GetFileName(f)}", Bytes: File.ReadAllBytes(f))))
            .ToArray();

    /// <summary>
    /// Writes <paramref name="corpus"/> to <paramref name="dir"/> (or, when null, reads a golden directory case in
    /// place) and runs one <c>bayan-check --many</c> over it. Each input is compiled by C# as <c>--golden --package
    /// &lt;dir&gt;</c> compiles it (<see cref="SelfhostSemanticsTests.GoldenSame"/>); an input on which C# throws is
    /// replaced by an empty file and counted.
    /// </summary>
    private void CheckCorpus(string kind, string dir, (string Name, byte[] Bytes)[]? corpus, Totals totals)
    {
        var replaced = new List<string>();
        var work = corpus is null ? Path.Combine(Root, "dirs", kind.Replace(' ', '-').Replace('/', '-')) : dir;
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        if (corpus is null)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(work, Path.GetRelativePath(dir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        else
        {
            foreach (var (name, bytes) in corpus)
            {
                var written = bytes;
                if (CSharpThrows(work, name, bytes) is { } thrown)
                {
                    replaced.Add($"{kind}/{name} {thrown} {Convert.ToHexString(bytes)}");
                    written = [];
                }
                File.WriteAllBytes(Path.Combine(work, name), written);
            }
        }
        var names = Directory.GetFiles(work, "*.byn").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        var manifest = File.Exists(Path.Combine(work, "package.byn")) ? Manifest.Read(Path.Combine(work, "package.byn")) : null;
        var inputs = names.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(work, n)), StringComparer.Ordinal);

        // One `--many` per corpus. A panic aborts the process after the panicking input's `== name` header: the
        // input is recorded, every input up to it is renamed out of `*.byn`, and `--many` resumes on the rest.
        var clock = Stopwatch.StartNew();
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var panicked = new List<string>();
        var pending = names;
        while (pending.Length > 0)
        {
            var (exit, stdout, stderr) = SelfhostParserTests.Run(BayanCheck, "--many", work);
            var panic = stderr.Contains("panic:", StringComparison.Ordinal);
            var last = stdout.Split('\n').LastOrDefault(l => l.StartsWith("== ", StringComparison.Ordinal))?[3..];
            Assert.True(exit is 0 or 1 || (exit == PanicExit.Code && panic && last is not null), $"{kind}: exit {exit} after `{last}` in {work}\n{stderr}");
            if (!panic)
            {
                foreach (var (name, text) in Sections(stdout, pending)) sections[name] = text;
                break;
            }
            var done = pending.TakeWhile(n => StringComparer.Ordinal.Compare(n, last) < 0).ToArray();
            var cut = stdout.IndexOf($"== {last}\n", StringComparison.Ordinal);
            foreach (var (name, text) in Sections(stdout[..cut], done)) sections[name] = text;
            panicked.Add($"{kind}/{last} {stderr.Split('\n')[0]} {stderr.Split('\n').ElementAtOrDefault(1)?.Trim()}");
            foreach (var name in done.Append(last!))
                if (name != "package.byn") File.Move(Path.Combine(work, name), Path.Combine(work, name + ".done"));
            pending = pending.Where(n => StringComparer.Ordinal.Compare(n, last) > 0).ToArray();
        }
        var ms = clock.ElapsedMilliseconds;

        var same = 0;
        var reached = 0;
        foreach (var name in names.Where(sections.ContainsKey))
        {
            var c = Compile(work, name, inputs[name], manifest);
            if (!c.Diagnostics.Items.Any(d => d.Code.StartsWith("BYN0", StringComparison.Ordinal) || d.Code.StartsWith("BYN1", StringComparison.Ordinal))) reached++;
            var lines = sections[name].Split('\n');
            var spanLines = lines.Where(l => l.StartsWith("span out of range:", StringComparison.Ordinal)).ToArray();
            Assert.False(CSharpSpansOutside(c.Diagnostics.Items), $"{Path.Combine(work, name)}: a C# diagnostic span lies outside the file");
            Assert.True(spanLines.Length == 0, $"{Path.Combine(work, name)}: a diagnostic span lies outside the file\n{string.Join("\n", spanLines)}");
            // `--many` prints `exit <n>` after an input whose own exit is not 0 (main.byn `many`).
            var exits = lines.Where(l => l.StartsWith("exit ", StringComparison.Ordinal)).ToArray();
            Assert.True(exits.All(l => l == "exit 1"), $"{Path.Combine(work, name)}: {string.Join(", ", exits)}");
            var rendered = string.Join("\n", lines.Where(l => !l.StartsWith("span out of range:", StringComparison.Ordinal) && !l.StartsWith("exit ", StringComparison.Ordinal)));
            if (rendered == DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: false)) same++;
        }
        totals.Inputs += names.Length;
        totals.Compared += sections.Count;
        totals.Replaced.AddRange(replaced.Select(r => r.Split(' ')[0]));
        totals.Panicked.AddRange(panicked);
        totals.SameRendering += same;
        totals.Checked += reached;
        output.WriteLine($"{kind}: {names.Length} inputs in {ms} ms, {reached} without a lexer or parser diagnostic, {same} rendered as C# does, "
                         + $"{panicked.Count} panicked: {string.Join("; ", panicked)}; "
                         + $"{replaced.Count} replaced (C# throws): {string.Join(", ", replaced)}");
    }

    /// <summary><c>golden</c> (selfhost/semantics/main.byn) and <see cref="SelfhostSemanticsTests.GoldenSame"/>: the file alone,
    /// <paramref name="dir"/> as root, tests for <c>*.test.byn</c>, the manifest's capabilities and name.</summary>
    private static Compilation Compile(string dir, string name, byte[] bytes, Manifest? manifest) =>
        Compilation.Create([new SourceText(name, bytes)], dir, new CompilationOptions(
            IncludeTests: name.EndsWith(".test.byn", StringComparison.Ordinal),
            Capabilities: manifest?.Capabilities, PackageName: manifest?.Name));

    /// <summary>The exception and its first two Bayan frames when the C# checker or renderer throws on the input, else null.</summary>
    private static string? CSharpThrows(string dir, string name, byte[] bytes)
    {
        try
        {
            var c = Compile(dir, name, bytes, null);
            DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: false);
            return null;
        }
        catch (Exception e)
        {
            var frames = (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at Bayan.", StringComparison.Ordinal))
                .Take(2).Select(l => l.Split(" in ")[0].Split('(')[0][3..]);
            return $"{e.GetType().Name} at {string.Join(" < ", frames)}";
        }
    }

    /// <summary>
    /// The C# diagnostics' spans walked as <c>t26CheckSpans</c> walks the port's (selfhost/semantics/main.byn):
    /// for every diagnostic with a file, its span, each related span and its fix span lie within the file.
    /// </summary>
    private static bool CSharpSpansOutside(IReadOnlyList<Diagnostic> diagnostics)
    {
        static bool Outside(TextSpan s, int size) => s.Start < 0 || s.Length < 0 || s.End > size;
        foreach (var d in diagnostics)
        {
            if (d.Source is null) continue;
            var size = d.Source.Length;
            if (Outside(d.Span, size)) return true;
            if (d.Related is not null && d.Related.Any(r => Outside(r.Span, size))) return true;
            if (d.Fix is not null && Outside(d.Fix.Span, size)) return true;
        }
        return false;
    }

    /// <summary>Splits <c>--many</c> stdout into one section per input, in <c>Io.listDir</c> byte order.</summary>
    private static Dictionary<string, string> Sections(string stdout, string[] names)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var cursor = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var header = $"== {names[i]}\n";
            Assert.True(stdout.AsSpan(cursor).StartsWith(header), $"bayan-check --many stopped before {names[i]}:\n{stdout[cursor..]}");
            cursor += header.Length;
            var next = i + 1 < names.Length ? stdout.IndexOf($"== {names[i + 1]}\n", cursor, StringComparison.Ordinal) : -1;
            var end = next < 0 ? stdout.Length : next;
            sections[names[i]] = stdout[cursor..end];
            cursor = end;
        }
        return sections;
    }

    private static readonly byte[][] InvalidUtf8 = [[0xFF], [0x80], [0xC3], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xF4, 0x90, 0x80, 0x80]];

    /// <summary>Checker-shaped tokens: declarations, ownership, failure, types and the punctuation recovery stops at.</summary>
    private static readonly string[] Tokens =
    [
        "let", "var", "const", "move", "ref", "in", "fail", "fails", "try", "catch", "return", "struct", "union", "enum",
        "interface", "import", "switch", "case", "default", "if", "else", "for", "while", "break", "continue", "null",
        "true", "false", "this", "i32", "i64", "u8", "f64", "string", "bool", "void", "List<i32>", "Map<string, i32>",
        "Owned<x>", "?", "??", "!", ".", "..", "->", "=>", "=", "==", "+", "-", "*", "/", "%", "<", ">", "(", ")", "{",
        "}", "[", "]", ",", ":", "@", "$\"{", "\"", "'a'", "0", "1", "-1", "9223372036854775808", "0.5", "x", "main",
    ];

    /// <summary>
    /// <see cref="RandomCorpusSize"/> inputs, each a golden or seed with 1 to 4 byte or token mutations: a byte
    /// inserted, deleted or replaced, invalid UTF-8 inserted, a token deleted, duplicated, swapped with its
    /// neighbour, replaced by one of <see cref="Tokens"/> or (most often, so that the input still parses and reaches
    /// the checker) by another token of the same input, or a line wrapped in <c>if true { … }</c> nested up to
    /// <see cref="SelfhostSemanticsGenerator.NestingCap"/> deep (both checkers recurse without a limit, as L018 notes).
    /// </summary>
    private static byte[][] GenerateRandomCorpus(byte[][] bases, ulong seed)
    {
        var rng = new XorShift(seed);
        var corpus = new byte[RandomCorpusSize][];
        for (var i = 0; i < RandomCorpusSize; i++)
        {
            var bytes = rng.Pick(bases).ToList();
            for (var mutations = 1 + rng.Below(4); mutations > 0; mutations--)
            {
                var at = bytes.Count == 0 ? 0 : rng.Below(bytes.Count);
                var token = TokenAt(bytes, at);
                switch (rng.Below(12))
                {
                    case 0: bytes.Insert(at, (byte)rng.Next()); break;
                    case 1 when bytes.Count > 0: bytes.RemoveAt(at); break;
                    case 2 when bytes.Count > 0: bytes[at] = (byte)rng.Next(); break;
                    case 3: bytes.InsertRange(at, rng.Pick(InvalidUtf8)); break;
                    case 4: bytes.RemoveRange(token.Start, token.Length); break;
                    case 5: bytes.InsertRange(token.Start, bytes.GetRange(token.Start, token.Length)); break;
                    case 6:
                        var next = TokenAt(bytes, Math.Min(bytes.Count, token.Start + token.Length + 1));
                        if (next.Start <= token.Start) break;
                        var second = bytes.GetRange(next.Start, next.Length);
                        var first = bytes.GetRange(token.Start, token.Length);
                        bytes.RemoveRange(next.Start, next.Length);
                        bytes.InsertRange(next.Start, first);
                        bytes.RemoveRange(token.Start, token.Length);
                        bytes.InsertRange(token.Start, second);
                        break;
                    case 7:
                        bytes.RemoveRange(token.Start, token.Length);
                        bytes.InsertRange(token.Start, Encoding.UTF8.GetBytes(rng.Pick(Tokens)));
                        break;
                    case < 11:
                        var word = TokenAt(bytes, bytes.Count == 0 ? 0 : rng.Below(bytes.Count));
                        if (token.Length == 0 || word.Length == 0) break;
                        var replacement = bytes.GetRange(word.Start, word.Length);
                        bytes.RemoveRange(token.Start, token.Length);
                        bytes.InsertRange(token.Start, replacement);
                        break;
                    default:
                        if (bytes.Count == 0) break;
                        var lineStart = bytes.FindLastIndex(Math.Max(0, at - 1), Math.Max(0, at), b => b == '\n') + 1;
                        var lineEnd = bytes.FindIndex(at, b => b == '\n');
                        if (lineEnd < 0) lineEnd = bytes.Count;
                        var depth = 1 + rng.Below(SelfhostSemanticsGenerator.NestingCap);
                        bytes.Insert(lineEnd, (byte)'\n');
                        bytes.InsertRange(lineEnd, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(" }", depth))));
                        bytes.InsertRange(lineStart, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("if true { ", depth)) + "\n"));
                        break;
                }
            }
            corpus[i] = bytes.ToArray();
        }
        return corpus;
    }

    /// <summary>The token around <paramref name="at"/>: a run of identifier bytes (ASCII letters, digits, <c>_</c> and
    /// non-ASCII), or one other non-space byte; empty at a space or the end.</summary>
    private static (int Start, int Length) TokenAt(List<byte> bytes, int at)
    {
        static bool Word(byte b) => b == '_' || b >= 0x80 || char.IsAsciiLetterOrDigit((char)b);
        if (at >= bytes.Count || bytes[at] is (byte)' ' or (byte)'\n' or (byte)'\t' or (byte)'\r') return (at, 0);
        if (!Word(bytes[at])) return (at, 1);
        var start = at;
        while (start > 0 && Word(bytes[start - 1])) start--;
        var end = at;
        while (end < bytes.Count && Word(bytes[end])) end++;
        return (start, end - start);
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }

    /// <summary>xorshift64 with a fixed seed, so every run writes the same corpus.</summary>
    private sealed class XorShift(ulong state)
    {
        public ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public int Below(int n) => (int)(Next() % (ulong)n);

        public T Pick<T>(T[] items) => items[Below(items.Length)];
    }
}
