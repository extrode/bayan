using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L017 (parser equivalence): over every parse golden, every BYN1 diag case, the valid and corrupted
/// seed corpora and a random-byte corpus, <c>bayan-parse</c> prints the same tree dump and diagnostics as
/// the C# parser and agrees on the exit code. One <c>--many</c> process per corpus keeps the proof fast;
/// the random corpus is xorshift-seeded and run under two seeds so the result is not seed-specific.
/// <c>--many</c> prints no per-input exit code, so the exit is read back from the dump (a diagnostics
/// section means 1, which is exactly <c>bayan-parse &lt;file&gt;</c>'s <c>errorCount == 0</c> test).
/// </summary>
public sealed class SelfhostParserEquivalenceLawTests(ITestOutputHelper output)
{
    private const int RandomCorpusSize = 2048;
    private const ulong RandomSeedA = 0x9E3779B97F4A7C15;
    private const ulong RandomSeedB = 0x0123456789ABCDEF;

    private static string Built => SelfhostParserTests.Built;

    /// <summary>Law L017: every input of every corpus dumps, diagnoses and exits like the C# parser.</summary>
    [Fact]
    [Trait("Law", "L017")]
    public void BayanParseEqualsTheCParserOnEveryInput()
    {
        CheckCorpus("parse", Cases(SelfhostParserTests.ParseCases));
        CheckCorpus("diag", Cases(SelfhostParserTests.DiagCases));
        CheckCorpus("valid", Generated(SelfhostParserGenerator.GenerateValid(SelfhostParserGenerator.Seeds)));
        CheckCorpus("corrupt", Generated(SelfhostParserGenerator.GenerateCorrupt(SelfhostParserGenerator.Seeds)));

        var (randomA, crashedA, wrappedA) = RandomCorpus(RandomSeedA);
        var (randomB, crashedB, wrappedB) = RandomCorpus(RandomSeedB);
        output.WriteLine($"random corpus: {crashedA.Length + crashedB.Length} candidates throw in the C# reference (Q506) and {wrappedA.Length + wrappedB.Length} have multi-word backticked code (Q505); both were replaced by empty inputs");
        output.WriteLine($"  seed {RandomSeedA:X16}: throws {string.Join(" ", crashedA)}; wrapped {Sample(wrappedA)}");
        output.WriteLine($"  seed {RandomSeedB:X16}: throws {string.Join(" ", crashedB)}; wrapped {Sample(wrappedB)}");
        Assert.True(randomA.Zip(randomB).Any(p => !p.First.AsSpan().SequenceEqual(p.Second)),
            "the two seeds produced the same corpus, so the second seed proves nothing");

        CheckCorpus("random-a", randomA.Select((b, i) => ($"random-{RandomSeedA:X16}-{i:D5}", b)).ToArray());
        CheckCorpus("random-b", randomB.Select((b, i) => ($"random-{RandomSeedB:X16}-{i:D5}", b)).ToArray());
    }

    private static string Sample(string[] labels) =>
        labels.Length == 0 ? "none" : $"{labels.Length} ({string.Join(" ", labels.Take(8))}{(labels.Length > 8 ? " …" : "")})";

    /// <summary>Reads every row of a public theory, relative to <c>tests/cases/</c>, as bytes.</summary>
    private static IReadOnlyList<(string Label, byte[] Bytes)> Cases(IEnumerable<object[]> rows) =>
        rows.Select(row => (string)row[0])
            .Select(name => (name, File.ReadAllBytes(Path.Combine(Golden.CasesRoot, name))))
            .ToArray();

    /// <summary>The generator's valid or corrupted seed corpus, one program per entry.</summary>
    private static IReadOnlyList<(string Label, byte[] Bytes)> Generated(
        IReadOnlyList<(string Name, string Source)> programs) =>
        programs.Select(p => (p.Name, Encoding.UTF8.GetBytes(p.Source))).ToArray();

    /// <summary>
    /// Writes <paramref name="inputs"/> to <c>tmp/l017/&lt;corpus&gt;/</c> as <c>00000.byn</c>, … and runs
    /// one <c>bayan-parse --many</c> over the directory. Each dump (which ends at the first
    /// <c>span out of range:</c> line, L018's Bayan-only check) must equal <see cref="AstDump.Dump"/>, and
    /// the exit code derived from the dump must equal the C# one. Inputs are numbered so the
    /// <c>Io.listDir</c> byte order <c>--many</c> prints is the order they were written.
    /// </summary>
    private void CheckCorpus(string corpus, IReadOnlyList<(string Label, byte[] Bytes)> inputs)
    {
        output.WriteLine($"{corpus}: {inputs.Count} inputs");
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "l017", corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string Name(int i) => $"{i:D5}.byn";
        for (var i = 0; i < inputs.Count; i++) File.WriteAllBytes(Path.Combine(dir, Name(i)), inputs[i].Bytes);

        var (exit, stdout, stderr) = SelfhostParserTests.Run(Built, "--many", dir);
        Assert.True(exit == 0, $"bayan-parse --many {corpus} exited {exit}:\n{stderr}");
        Assert.False(stderr.Contains("panic:", StringComparison.Ordinal), $"bayan-parse panicked on {corpus}:\n{stderr}");

        var cursor = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var header = $"== {Name(i)}\n";
            Assert.True(stdout.AsSpan(cursor).StartsWith(header),
                $"bayan-parse --many {corpus} stopped before {Name(i)} ({inputs[i].Label}) (exit {exit}):\n{stderr}");
            cursor += header.Length;
            var next = i + 1 < inputs.Count ? stdout.IndexOf($"== {Name(i + 1)}\n", cursor, StringComparison.Ordinal) : -1;
            var end = next < 0 ? stdout.Length : next;
            var segment = stdout[cursor..end];
            cursor = end;

            var spanIdx = segment.IndexOf("span out of range: ", StringComparison.Ordinal);
            var actual = spanIdx < 0 ? segment : segment[..spanIdx];

            var tree = SyntaxTree.Parse(new SourceText(inputs[i].Label, inputs[i].Bytes));
            var expected = AstDump.Dump(tree);
            if (actual != expected)
            {
                var expectedPath = Path.Combine(dir, Path.ChangeExtension(Name(i), ".ast"));
                File.WriteAllText(expectedPath, expected);
                var failure = Golden.Compare(expectedPath, actual, update: false).Failure ?? "outputs differ in bytes only (CRLF or invalid UTF-8)";
                Assert.Fail($"{inputs[i].Label} ({Path.Combine(dir, Name(i))}): bayan-parse and the C# parser disagree\n{failure}");
            }

            var expectedExit = tree.Diagnostics.Any(d => d.IsError) ? 1 : 0;
            var actualExit = actual.Contains("\n\n", StringComparison.Ordinal) ? 1 : 0;
            Assert.True(expectedExit == actualExit,
                $"{inputs[i].Label}: exit code mismatch (C# {expectedExit}, bayan {actualExit})");
        }
    }

    private static readonly byte[][] InvalidUtf8 =
    [
        [0xFF], [0x80], [0xC0, 0x80], [0xC3], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xF0, 0x9F, 0x98], [0xF4, 0x90, 0x80, 0x80], [0xF8, 0x88, 0x80, 0x80, 0x80],
    ];

    private static readonly string[] Specials =
    [
        "$\"{", "$\"{a", "$\"x{ $\"{", "$\"{}\"", "$\"{\"", "}", "{", "\"{", "$\"a{b}c", "$\"{{\"",
        "é", "let caf" + "é" + " = 1;", "Å", "Å", "각", "ạ̇", "ạ̇", "x́́", "̈́",
        "‮", "⁦", "⁩", "‏", "﻿", "// ‮x\n", "\"a⁧b\"", "/* ‭ */", "'‮'",
        "\r\n", "\r", "\t", "\0", "///", "/*", "*/", "'", "\"", "\"\"\"", "0x", "1e", "1.", "_",
    ];

    /// <summary>
    /// The random-byte corpus, the lexer L002 shape pointed at parser sources: the <c>parse/</c> and
    /// <c>diag/</c> inputs mixed, mutated and broken into <see cref="RandomCorpusSize"/> files. A candidate
    /// that makes the C# reference throw (Q506: <c>Parser.ReportBraceless</c> reaches
    /// <c>SourceText.GetText</c> past the end, and the Bayan port panics in <c>lexer/source.byn</c>) has no
    /// reference dump, and one whose C# diagnostic holds multi-word backticked code can render differently
    /// because <c>lexer/diagnostics.byn</c>'s <c>wrap</c> breaks inside the code where C# keeps it whole
    /// (Q505). Both are recorded and replaced by an empty input, as T18 replaces the C# crashes.
    /// </summary>
    private static (byte[][] Corpus, string[] Crashed, string[] Wrapped) RandomCorpus(ulong seed)
    {
        var sources = Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "parse"), "*.byn", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "diag"), "*.byn", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllBytes)
            .Where(b => b.Length > 0)
            .ToArray();
        var lines = sources.SelectMany(SplitLines).ToArray();
        var rng = new XorShift(seed);
        var corpus = new byte[RandomCorpusSize][];
        var crashed = new List<string>();
        var wrapped = new List<string>();
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
                switch (rng.Below(20))
                {
                    case < 9: bytes.AddRange(rng.Pick(lines)); break;
                    case < 12:
                        var text = rng.Pick(sources);
                        var start = rng.Below(text.Length);
                        bytes.AddRange(text.AsSpan(start, Math.Min(text.Length - start, 1 + rng.Below(64))).ToArray());
                        break;
                    case < 14: bytes.AddRange(rng.Pick(InvalidUtf8)); break;
                    case < 19: bytes.AddRange(Encoding.UTF8.GetBytes(rng.Pick(Specials))); break;
                    default:
                        var depth = 14 + rng.Below(8);
                        bytes.AddRange(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("$\"{", depth)) + "x"));
                        bytes.AddRange(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("}\"", rng.Below(depth + 2)))));
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
            if (rng.Below(8) == 0)
                bytes = [.. Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(bytes.ToArray()).Replace("\n", "\r\n"))];
            if (!TryReference(bytes.ToArray(), out var tree))
            {
                var label = $"{seed:X16}-{i:D5}";
                crashed.Add(label);
                var skipDir = Path.Combine(Golden.RepoRoot, "tmp", "l017", "skipped");
                Directory.CreateDirectory(skipDir);
                File.WriteAllBytes(Path.Combine(skipDir, label + ".byn"), bytes.ToArray());
                corpus[i] = [];
                continue;
            }
            if (tree!.Diagnostics.Any(HasMultiWordBacktick))
            {
                wrapped.Add($"{seed:X16}-{i:D5}");
                corpus[i] = [];
                continue;
            }
            corpus[i] = bytes.ToArray();
        }
        return (corpus, crashed.ToArray(), wrapped.ToArray());
    }

    /// <summary>Whether the C# parser can parse and dump <paramref name="bytes"/> (Q506 says some cannot).</summary>
    private static bool TryReference(byte[] bytes, out SyntaxTree? tree)
    {
        try
        {
            tree = SyntaxTree.Parse(new SourceText("random.byn", bytes));
            AstDump.Dump(tree);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            tree = null;
            return false;
        }
    }

    /// <summary>A backtick pair enclosing a space: the self-hosted <c>wrap</c> breaks such code across
    /// lines where C#'s word splitter keeps it whole (Q505).</summary>
    private static bool HasMultiWordBacktick(Diagnostic d) =>
        HasBacktickedSpace(d.Message) || (d.Help is { } help && HasBacktickedSpace(help)) || (d.Label is { } label && HasBacktickedSpace(label));

    private static bool HasBacktickedSpace(string text)
    {
        var inCode = false;
        var space = false;
        foreach (var c in text)
        {
            if (c == '`')
            {
                if (inCode && space) return true;
                inCode = !inCode;
                space = false;
            }
            else if (inCode && c == ' ')
            {
                space = true;
            }
        }
        return false;
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
