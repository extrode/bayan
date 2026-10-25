using System.Collections;
using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L018: over the corrupted seed corpus and a random-byte corpus, <c>bayan-parse --many</c> never
/// panics, no node span leaves the input, and every child-outside-parent it reports is one the C# tree
/// itself has. The build and process helpers are reused from <see cref="SelfhostParserTests"/>; the
/// corrupted corpus comes from <see cref="SelfhostParserGenerator"/>.
/// </summary>
public sealed class SelfhostParserNoPanicLawTests(ITestOutputHelper output)
{
    private const string SpanCheck = "span out of range:";

    /// <summary>Law L018: every corpus input exits 0 or 1, no panic, and every span line is a C# span.</summary>
    [Fact]
    [Trait("Law", "L018")]
    public void BayanParseNeverPanicsAndEverySpanMatchesTheCSharpTree()
    {
        CheckCorpus("corrupt", CorruptedCorpus());
        CheckCorpus("random", RandomByteCorpus());
    }

    /// <summary>
    /// Writes <paramref name="corpus"/> under <c>tmp/l018-corpus/&lt;kind&gt;/</c>, runs one
    /// <c>bayan-parse --many</c> over it, and checks every input's section.
    /// </summary>
    private void CheckCorpus(string kind, (string Name, byte[] Bytes)[] corpus)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "l018-corpus", kind);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (name, bytes) in corpus) File.WriteAllBytes(Path.Combine(dir, name), bytes);

        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--many", dir);
        output.WriteLine($"{kind}: {corpus.Length} inputs, exit {exit}, {stdout.Length} bytes of output");
        Assert.True(exit is 0 or 1, $"{kind}: exit {exit}\n{stderr}");
        Assert.False(stderr.Contains("panic:", StringComparison.Ordinal), $"{kind}: panic on stderr\n{stderr}");

        var sections = Sections(stdout, corpus);
        var childOutside = new List<string>();
        var missing = new List<string>();
        for (var i = 0; i < corpus.Length; i++)
        {
            var name = corpus[i].Name;
            var spanLines = sections[i].Split('\n').Where(l => l.StartsWith(SpanCheck, StringComparison.Ordinal)).ToArray();
            Assert.False(spanLines.Any(l => !l.Contains(" outside ", StringComparison.Ordinal)),
                $"{Path.Combine(dir, name)}: a node span leaves the input\n{string.Join("\n", spanLines)}");

            var csharp = CSharpSpanViolations(corpus[i].Bytes);
            if (spanLines.Length > 0) childOutside.Add(name);
            // The child-within-parent half of L018 is false of C# itself (Q473, Q509): the port must
            // report exactly the violations the C# tree has, never one of its own.
            if (spanLines.Length > 0 && !csharp.ChildOutside)
                Assert.Fail($"{Path.Combine(dir, name)}: the port reports a child outside its parent that the C# tree does not have\n{string.Join("\n", spanLines)}");
            if (spanLines.Length == 0 && csharp.ChildOutside) { missing.Add(name); output.WriteLine($"MISSING {name}: {string.Join(" | ", Details)}"); }
            Details.Clear();
        }
        output.WriteLine($"{kind}: {childOutside.Count} inputs print a child-outside-parent line: {string.Join(", ", childOutside)}");
        output.WriteLine($"{kind}: {missing.Count} C# child-outside-parent inputs the port does not report: {string.Join(", ", missing)}");
    }

    /// <summary>Splits <c>--many</c> stdout into one section per input, in <c>Io.listDir</c> byte order.</summary>
    private static string[] Sections(string stdout, (string Name, byte[] Bytes)[] corpus)
    {
        var sections = new string[corpus.Length];
        var cursor = 0;
        for (var i = 0; i < corpus.Length; i++)
        {
            var header = $"== {corpus[i].Name}\n";
            Assert.True(stdout.AsSpan(cursor).StartsWith(header),
                $"--many stopped before {corpus[i].Name}:\n{stdout[cursor..Math.Min(stdout.Length, cursor + 200)]}");
            cursor += header.Length;
            var next = i + 1 < corpus.Length ? stdout.IndexOf($"== {corpus[i + 1].Name}\n", cursor, StringComparison.Ordinal) : -1;
            var end = next < 0 ? stdout.Length : next;
            sections[i] = stdout[cursor..end];
            cursor = end;
        }
        return sections;
    }

    /// <summary>
    /// The C# tree's own span violations, walked like <c>spans.byn</c>: each node's span against the input
    /// and its parent's, and each child node or <see cref="TextSpan"/> field against the node's span.
    /// </summary>
    private static (bool OutsideInput, bool ChildOutside) CSharpSpanViolations(byte[] bytes) =>
        CSharpSpanViolations(SyntaxTree.Parse(new SourceText("corpus.byn", bytes)), bytes.Length);

    private static (bool OutsideInput, bool ChildOutside) CSharpSpanViolations(SyntaxTree tree, int length)
    {
        var outsideInput = false;
        var childOutside = false;
        CheckNode(tree.Root, default, haveParent: false, length, "", ref outsideInput, ref childOutside);
        return (outsideInput, childOutside);
    }

    internal static readonly List<string> Details = [];

    private static void CheckNode(Node node, TextSpan parent, bool haveParent, int length, string prop, ref bool outsideInput, ref bool childOutside)
    {
        var span = node.Span;
        if (span.Start < 0 || span.Length < 0 || span.End > length) outsideInput = true;
        if (haveParent && (span.Start < parent.Start || span.End > parent.End))
        {
            childOutside = true;
            Details.Add($"{prop}:{node.GetType().Name} {span.start}+{span.Length} outside {parent.start}+{parent.Length}");
        }
        foreach (var p in node.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead || p.Name == nameof(Node.Span)) continue;
            CheckValue(p.GetValue(node), span, length, p.Name, ref outsideInput, ref childOutside);
        }
    }

    private static void CheckValue(object? value, TextSpan parent, int length, string prop, ref bool outsideInput, ref bool childOutside)
    {
        switch (value)
        {
            case null:
                break;
            case Node node:
                CheckNode(node, parent, haveParent: true, length, prop, ref outsideInput, ref childOutside);
                break;
            case TextSpan span:
                if (span.Start < parent.Start || span.End > parent.End)
                {
                    childOutside = true;
                    Details.Add($"{prop}:TextSpan {span.start}+{span.Length} outside {parent.start}+{parent.Length}");
                }
                break;
            case IEnumerable sequence when value is not string:
                foreach (var item in sequence) CheckValue(item, parent, length, prop, ref outsideInput, ref childOutside);
                break;
        }
    }

    /// <summary>The one corrupted program per valid generator program, in ordinal name order.</summary>
    private static (string Name, byte[] Bytes)[] CorruptedCorpus() =>
        SelfhostParserGenerator.GenerateCorrupt(SelfhostParserGenerator.Seeds)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => (p.Name, Encoding.UTF8.GetBytes(p.Source)))
            .ToArray();

    private const int RandomCorpusSize = 4096;

    private static readonly byte[][] InvalidUtf8 =
    [
        [0xFF], [0x80], [0xC0, 0x80], [0xC3], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xF0, 0x9F, 0x98], [0xF4, 0x90, 0x80, 0x80], [0xF8, 0x88, 0x80, 0x80, 0x80],
    ];

    /// <summary>
    /// A deterministic random-byte corpus: raw bytes, punctuation (to reach the bracket, quote and recovery
    /// paths) and invalid UTF-8, each with random insertions and deletions. No valid program text, so this
    /// covers the arbitrary-byte half of the law that the corrupted seed corpus does not.
    /// </summary>
    private static (string Name, byte[] Bytes)[] RandomByteCorpus()
    {
        const string punctuation = "{}()[]<>\"'\\$:=,.;+-*/!?&#@%^~| \t\n\r";
        var rng = new XorShift(0xC3B4A596D7E8F901);
        var corpus = new (string, byte[])[RandomCorpusSize];
        for (var i = 0; i < RandomCorpusSize; i++)
        {
            var bytes = new List<byte>();
            if (i % 512 == 0)
            {
                corpus[i] = ($"{i:D5}.byn", []);
                continue;
            }
            for (var pieces = 1 + rng.Below(10); pieces > 0; pieces--)
            {
                switch (rng.Below(20))
                {
                    case < 10:
                        for (var n = 1 + rng.Below(48); n > 0; n--) bytes.Add((byte)rng.Next());
                        break;
                    case < 17:
                        for (var n = 1 + rng.Below(24); n > 0; n--) bytes.Add((byte)punctuation[rng.Below(punctuation.Length)]);
                        break;
                    default: bytes.AddRange(rng.Pick(InvalidUtf8)); break;
                }
                if (rng.Below(3) == 0) bytes.Add((byte)'\n');
            }
            for (var mutations = rng.Below(5); mutations > 0 && bytes.Count > 0; mutations--)
            {
                var at = rng.Below(bytes.Count);
                switch (rng.Below(3))
                {
                    case 0: bytes.Insert(at, (byte)rng.Next()); break;
                    case 1: bytes.RemoveAt(at); break;
                    default: bytes[at] = (byte)rng.Next(); break;
                }
            }
            corpus[i] = ($"{i:D5}.byn", bytes.ToArray());
        }
        return corpus;
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
