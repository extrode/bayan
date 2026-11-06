using System.Collections;
using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>Law L018: over the corrupted seed corpora and a random-byte corpus, <c>bayan-parse --many</c>
/// never panics and its span walker agrees with the C# tree. The parser and process helpers are reused
/// from <see cref="SelfhostParserTests"/>; the corpus generator reuses <see cref="SelfhostParserGenerator"/>.</summary>
public sealed class SelfhostParserNoPanicLawTests(ITestOutputHelper output)
{
    private const int FullRandomCorpusSize = 4_000;

    /// <summary>The quick tier's random inputs (Q748): the first 400 of the full corpus, which the seeded generator
    /// writes in the same order whatever its size. <c>BAYAN_LAW_TIER=full</c> runs all of <see cref="FullRandomCorpusSize"/>.</summary>
    private const int QuickRandomCorpusSize = 400;

    private static int RandomCorpusSize =>
        Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full" ? FullRandomCorpusSize : QuickRandomCorpusSize;

    /// <summary>Law L018: on every corpus input <c>bayan-parse</c> exits 0 or 1, no node span leaves the
    /// input, and every child-outside-parent it reports is one the C# tree itself has (the port matches C#).</summary>
    [Fact]
    [Trait("Law", "L018")]
    public void BayanParseNeverPanicsAndEverySpanMatchesTheCSharpTree()
    {
        CheckCorpus("corrupt", SelfhostParserGenerator.GenerateCorrupt(SelfhostParserGenerator.Seeds).Select(p => (p.Name, Encoding.UTF8.GetBytes(p.Source))));
        var (random, crashed) = GenerateRandomCorpus();
        output.WriteLine($"random corpus: {crashed.Count} candidates crash the C# parser and were replaced by an empty input: {string.Join(" ", crashed)}");
        CheckCorpus("random", random.Select((b, i) => ($"{i:D5}.byn", b)));
    }

    /// <summary>Writes <paramref name="corpus"/> under <c>tmp/parser-nopanic/&lt;kind&gt;/</c> and runs one
    /// <c>bayan-parse --many</c> over it: the exit is 0 or 1, no <c>panic:</c> is printed, no node span lies
    /// outside the input, and every child-outside-parent line matches the C# tree's own span violations.</summary>
    private void CheckCorpus(string kind, IEnumerable<(string Name, byte[] Bytes)> corpus)
    {
        var programs = corpus.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "parser-nopanic", kind);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (name, bytes) in programs) File.WriteAllBytes(Path.Combine(dir, name), bytes);

        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--many", dir);
        output.WriteLine(stderr);
        Assert.True(exit is 0 or 1, $"{kind}: exit {exit}\n{stderr}");
        Assert.False(stderr.Contains("panic:", StringComparison.Ordinal), $"{kind}: panic on stderr\n{stderr}");

        var names = programs.Select(p => p.Name).ToArray();
        var sections = Sections(stdout, names);
        var csharpChildOutside = new List<string>();
        for (var i = 0; i < programs.Length; i++)
        {
            var name = programs[i].Name;
            var spanLines = sections[name].Split('\n').Where(l => l.StartsWith("span out of range:", StringComparison.Ordinal)).ToArray();
            var outsideInput = spanLines.Any(l => !l.Contains(" outside ", StringComparison.Ordinal));
            var childOutside = spanLines.Any(l => l.Contains(" outside ", StringComparison.Ordinal));

            var csharp = CSharpSpanViolations(programs[i].Bytes);
            Assert.False(outsideInput,
                $"{Path.Combine(dir, name)}: a node span lies outside the input\n{string.Join("\n", spanLines)}");

            // The child-within-parent half of L018 is false of C# itself (Q473, Q509): the port must
            // report exactly the inputs where C# has one, no more and no fewer.
            if (childOutside) csharpChildOutside.Add(name);
            if (childOutside && !csharp.ChildOutside)
                Assert.Fail($"{Path.Combine(dir, name)}: the port reports a child outside its parent that C# does not have\n{string.Join("\n", spanLines)}");
            if (!childOutside && csharp.ChildOutside)
                Assert.Fail($"{Path.Combine(dir, name)}: C# has a child outside its parent that the port's checkSpans does not report");
        }
        output.WriteLine($"{kind}: {programs.Length} inputs, {csharpChildOutside.Count} with a C# child outside its parent: {string.Join(", ", csharpChildOutside)}");
    }

    /// <summary>Splits <c>--many</c> stdout into one section per input, in <c>Io.listDir</c> byte order.</summary>
    private static Dictionary<string, string> Sections(string stdout, string[] names)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var cursor = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var header = $"== {names[i]}\n";
            Assert.True(stdout.AsSpan(cursor).StartsWith(header), $"bayan-parse --many stopped before {names[i]}:\n{stdout}");
            cursor += header.Length;
            var next = i + 1 < names.Length ? stdout.IndexOf($"== {names[i + 1]}\n", cursor, StringComparison.Ordinal) : -1;
            var end = next < 0 ? stdout.Length : next;
            sections[names[i]] = stdout[cursor..end];
            cursor = end;
        }
        return sections;
    }

    /// <summary>The C# tree's own span violations, walked like <c>spans.byn</c>: node span within the input,
    /// child span (a <c>TextSpan</c> field or a child <c>Node</c>) within its parent.</summary>
    private static (bool OutsideInput, bool ChildOutside) CSharpSpanViolations(byte[] bytes)
    {
        var tree = SyntaxTree.Parse(new SourceText("corpus.byn", bytes));
        var outsideInput = false;
        var childOutside = false;
        CheckNode(tree.Root, default, haveParent: false, bytes.Length, ref outsideInput, ref childOutside);
        return (outsideInput, childOutside);
    }

    private static void CheckNode(Node node, TextSpan parent, bool haveParent, int length, ref bool outsideInput, ref bool childOutside)
    {
        var span = node.Span;
        if (span.Start < 0 || span.Length < 0 || span.End > length) outsideInput = true;
        if (haveParent && (span.Start < parent.Start || span.End > parent.End)) childOutside = true;
        foreach (var prop in node.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead || prop.Name == "Span") continue;
            CheckValue(prop.GetValue(node), span, length, ref outsideInput, ref childOutside);
        }
    }

    private static void CheckValue(object? value, TextSpan parent, int length, ref bool outsideInput, ref bool childOutside)
    {
        switch (value)
        {
            case null:
                break;
            case Node n:
                CheckNode(n, parent, haveParent: true, length, ref outsideInput, ref childOutside);
                break;
            case TextSpan s:
                if (s.Start < parent.Start || s.End > parent.End) childOutside = true;
                break;
            case IEnumerable seq when value is not string:
                foreach (var item in seq) CheckValue(item, parent, length, ref outsideInput, ref childOutside);
                break;
        }
    }

    private static readonly byte[][] InvalidUtf8 =
    [
        [0xFF], [0x80], [0xC0, 0x80], [0xC3], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xF0, 0x9F, 0x98], [0xF4, 0x90, 0x80, 0x80], [0xF8, 0x88, 0x80, 0x80, 0x80],
    ];

    /// <summary>Parser-shaped fragments: recovery points, unclosed constructs and the token pairs C# spans end before.</summary>
    private static readonly string[] Specials =
    [
        "move ", "fail ", "break ", "continue ", "except ", "?.", "?.x", "if true { ", "unless false { ",
        "while true { break }", "for x in xs { use(x) }", "switch n { case 1: use(n) }", "$\"{", "}\"",
        "\"", "'", "/*", "*/", "//\n", "\\", "let x = ", "x = ", "return ", "..", "..=", "->", "=>",
        "==", "!=", "[", "]", "{", "}", "(", ")", ";", ",", ":", ".", "@", "#", "\0", "\r\n", "\r", "\t", "é",
    ];

    /// <summary>The <c>parse/</c> inputs mixed, mutated and broken into <see cref="RandomCorpusSize"/> files,
    /// with nesting capped at <see cref="SelfhostParserGenerator.NestingCap"/> (both parsers recurse unboundedly).
    /// A candidate that crashes the C# parser itself (the <c>ReportBraceless</c> out-of-range span, Q509) is
    /// replaced by an empty input: the port matches C# on it, so it is not part of the no-panic corpus.</summary>
    private static (byte[][] Corpus, List<string> Crashed) GenerateRandomCorpus()
    {
        var cases = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "parse"), "*.byn").Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray();
        var lines = cases.SelectMany(SplitLines).ToArray();
        var rng = new XorShift(0x9E3779B97F4A7C15);
        var corpus = new byte[RandomCorpusSize][];
        var crashed = new List<string>();
        for (var i = 0; i < RandomCorpusSize; i++)
        {
            if (i % 250 == 0)
            {
                corpus[i] = [];
                continue;
            }
            var bytes = new List<byte>();
            for (var pieces = 1 + rng.Below(8); pieces > 0; pieces--)
            {
                switch (rng.Below(16))
                {
                    case < 7: bytes.AddRange(rng.Pick(lines)); break;
                    case < 9:
                        var text = rng.Pick(cases);
                        var start = rng.Below(text.Length);
                        bytes.AddRange(text.AsSpan(start, Math.Min(text.Length - start, 1 + rng.Below(64))).ToArray());
                        break;
                    case < 11: bytes.AddRange(rng.Pick(InvalidUtf8)); break;
                    case < 15: bytes.AddRange(Encoding.UTF8.GetBytes(rng.Pick(Specials))); break;
                    default:
                        var depth = 1 + rng.Below(SelfhostParserGenerator.NestingCap);
                        bytes.AddRange(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("if true { ", depth)) + "x"));
                        bytes.AddRange(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(" }", depth))));
                        break;
                }
                bytes.AddRange(rng.Below(3) switch { 0 => [], 1 => " "u8.ToArray(), _ => "\n"u8.ToArray() });
            }
            for (var mutations = rng.Below(4); mutations > 0 && bytes.Count > 0; mutations--)
            {
                var at = rng.Below(bytes.Count);
                switch (rng.Below(3))
                {
                    case 0: bytes.Insert(at, (byte)rng.Next()); break;
                    case 1: bytes.RemoveAt(at); break;
                    default: bytes[at] = (byte)rng.Next(); break;
                }
            }
            var candidate = bytes.ToArray();
            if (candidate.Length > 0 && CSharpCrashes(candidate))
            {
                crashed.Add(Convert.ToHexString(candidate));
                candidate = [];
            }
            corpus[i] = candidate;
        }
        return (corpus, crashed);
    }

    private static bool CSharpCrashes(byte[] bytes)
    {
        try { SyntaxTree.Parse(new SourceText("corpus.byn", bytes)); return false; }
        catch { return true; }
    }

    private static IEnumerable<byte[]> SplitLines(byte[] text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            yield return text[start..(i + 1)];
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
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
