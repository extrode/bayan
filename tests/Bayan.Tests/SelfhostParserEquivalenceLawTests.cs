using System.Diagnostics;
using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L017 (parser equivalence): over every parse golden, every BYN1 diag case, the valid and
/// corrupted seed corpora and a random-byte corpus, <c>bayan-parse</c> prints the same tree dump and
/// diagnostics as the C# parser and agrees on the exit code. One <c>--many</c> process per corpus, so
/// the proof stays fast; the random corpus is xorshift-seeded and run under two seeds so the result is
/// not seed-specific. <c>--many</c> prints no per-input exit code, so the exit is read back from the
/// dump (a diagnostics section means 1, exactly <c>bayan-parse &lt;file&gt;</c>'s <c>errorCount == 0</c>).
/// </summary>
public sealed class SelfhostParserEquivalenceLawTests(ITestOutputHelper output)
{
    private static string Built => SelfhostParserTests.Built;

    private const int RandomCorpusSize = 2048;
    private const ulong RandomSeedA = 0x9E3779B97F4A7C15;
    private const ulong RandomSeedB = 0x0123456789ABCDEF;

    [Fact]
    [Trait("Law", "L017")]
    public void BayanParseEqualsTheCParserOnEveryInput()
    {
        var skipped = new List<(string Label, string Reason)>();
        skipped.AddRange(CheckCorpus("parse-goldens", ParseGoldens(), skipWrapBug: false));
        skipped.AddRange(CheckCorpus("diag-byn1", DiagCases(), skipWrapBug: false));
        skipped.AddRange(CheckCorpus("valid-seeds", Generated(SelfhostParserGenerator.GenerateValid(SelfhostParserGenerator.Seeds)), skipWrapBug: false));
        skipped.AddRange(CheckCorpus("corrupt-seeds", Generated(SelfhostParserGenerator.GenerateCorrupt(SelfhostParserGenerator.Seeds)), skipWrapBug: false));
        skipped.AddRange(CheckCorpus("random-a", RandomCorpus(RandomSeedA), skipWrapBug: true));
        skipped.AddRange(CheckCorpus("random-b", RandomCorpus(RandomSeedB), skipWrapBug: true));
        foreach (var group in skipped.GroupBy(s => s.Reason).OrderBy(g => g.Key))
            output.WriteLine($"skipped {group.Count()} input(s): {group.Key} (e.g. {group.First().Label})");
    }

    /// <summary>
    /// Feeds <paramref name="inputs"/> to one <c>bayan-parse --many</c> and compares each dump,
    /// diagnostics and exit code with the C# parser. Inputs whose C# reference throws (Q506) or, in the
    /// random corpus, whose diagnostics hold multi-word backticked code (Q505) are left out of the
    /// process and returned; everything else must match byte-for-byte.
    /// </summary>
    private List<(string Label, string Reason)> CheckCorpus(
        string corpus, IReadOnlyList<(string Label, byte[] Bytes)> inputs, bool skipWrapBug)
    {
        output.WriteLine($"{corpus}: {inputs.Count} inputs");
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "l017", corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var skipped = new List<(string, string)>();
        var comparable = new List<(string Name, string Label, byte[] Expected, SyntaxTree Tree)>();
        for (var i = 0; i < inputs.Count; i++)
        {
            SyntaxTree tree;
            try
            {
                tree = SyntaxTree.Parse(new SourceText(inputs[i].Label, inputs[i].Bytes));
            }
            catch (ArgumentOutOfRangeException)
            {
                // The C# parser itself throws on this input (a span computed past the input, e.g.
                // ReportBraceless at Parser.cs:2408; Q506). No reference tree, so L017 has nothing to match.
                skipped.Add((inputs[i].Label, "C# reference throws (Q506)"));
                continue;
            }
            string expectedText;
            try
            {
                expectedText = AstDump.Dump(tree);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The C# reference cannot render this input (a diagnostic or fix span starts past its
                // line's end; Q506). No reference dump to match, so the input is outside L017.
                skipped.Add((inputs[i].Label, "C# reference throws (Q506)"));
                continue;
            }
            if (skipWrapBug && tree.Diagnostics.Any(HasMultiWordBacktick))
            {
                // The self-hosted lexer's `wrap` breaks backticked code across lines where C# keeps it
                // whole (Q505), so a diagnostic whose prose holds `… …` can render differently.
                skipped.Add((inputs[i].Label, "wrap bug (Q505)"));
                continue;
            }
            var name = $"{comparable.Count:D5}.byn";
            File.WriteAllBytes(Path.Combine(dir, name), inputs[i].Bytes);
            comparable.Add((name, inputs[i].Label, Encoding.UTF8.GetBytes(expectedText), tree));
        }

        var (exit, outBytes, stderr) = RunBytes(Built, "--many", dir);
        Assert.True(exit == 0, $"bayan-parse --many {corpus} exited {exit}:\n{stderr}");

        var cursor = 0;
        for (var i = 0; i < comparable.Count; i++)
        {
            var header = Encoding.UTF8.GetBytes($"== {comparable[i].Name}\n");
            Assert.True(outBytes.AsSpan(cursor).StartsWith(header),
                $"bayan-parse --many {corpus} stopped before {comparable[i].Name} ({comparable[i].Label}) (exit {exit}):\n{stderr}");
            cursor += header.Length;
            var next = i + 1 < comparable.Count ? IndexOf(outBytes, Encoding.UTF8.GetBytes($"== {comparable[i + 1].Name}\n"), cursor) : -1;
            var end = next < 0 ? outBytes.Length : next;
            var segment = outBytes[cursor..end];
            cursor = end;

            // `--many` appends `span out of range:` lines (L018's checkSpans, no C# counterpart);
            // the dump ends where they begin.
            var spanIdx = segment.AsSpan().IndexOf("span out of range: "u8);
            var actualDump = spanIdx < 0 ? segment : segment[..spanIdx];

            if (!actualDump.AsSpan().SequenceEqual(comparable[i].Expected))
            {
                var expectedPath = Path.Combine(dir, Path.ChangeExtension(comparable[i].Name, ".ast"));
                File.WriteAllBytes(expectedPath, comparable[i].Expected);
                var failure = Golden.Compare(expectedPath, Encoding.UTF8.GetString(actualDump), update: false).Failure
                    ?? "outputs differ in bytes only (CRLF or invalid UTF-8)";
                Assert.Fail($"{comparable[i].Label} ({Path.Combine(dir, comparable[i].Name)}): bayan-parse and the C# parser disagree\n{failure}");
            }

            var expectedExit = comparable[i].Tree.Diagnostics.Any(d => d.IsError) ? 1 : 0;
            var actualExit = actualDump.AsSpan().IndexOf("\n\n"u8) >= 0 ? 1 : 0;
            Assert.True(expectedExit == actualExit,
                $"{comparable[i].Label}: exit code mismatch (C# {expectedExit}, bayan {actualExit})");
        }
        return skipped;
    }

    /// <summary>A backtick pair enclosing a space: the self-hosted <c>wrap</c> breaks such code across
    /// lines, where C#'s word splitter keeps it whole (R16).</summary>
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

    private static IReadOnlyList<(string Label, byte[] Bytes)> ParseGoldens()
    {
        var dir = Path.Combine(Golden.CasesRoot, "parse");
        return Directory.EnumerateFiles(dir, "*.byn", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(f => ($"parse/{Path.GetFileName(f)}", File.ReadAllBytes(f)))
            .ToArray();
    }

    /// <summary>Every <c>diag/</c> case whose <c>.diag</c> holds a parser diagnostic, each <c>.byn</c> parsed alone.</summary>
    private static IReadOnlyList<(string Label, byte[] Bytes)> DiagCases()
    {
        var list = new List<(string, byte[])>();
        var root = Path.Combine(Golden.CasesRoot, "diag");
        foreach (var diag in Directory.EnumerateFiles(root, "*.diag", SearchOption.AllDirectories)
                                     .Where(f => File.ReadAllText(f).Contains("BYN1", StringComparison.Ordinal))
                                     .Order(StringComparer.Ordinal))
        {
            var byn = Path.ChangeExtension(diag, ".byn");
            if (File.Exists(byn))
                list.Add((Path.GetRelativePath(Golden.CasesRoot, byn).Replace('\\', '/'), File.ReadAllBytes(byn)));
        }
        return list;
    }

    private static IReadOnlyList<(string Label, byte[] Bytes)> Generated(
        IReadOnlyList<(string Name, string Source)> programs) =>
        programs.Select(p => (p.Name, Encoding.UTF8.GetBytes(p.Source))).ToArray();

    private static IReadOnlyList<(string Label, byte[] Bytes)> RandomCorpus(ulong seed)
    {
        var corpus = GenerateRandomCorpus(seed);
        return corpus.Select((b, i) => ($"random-{seed:X16}-{i:D5}", b)).ToArray();
    }

    private static (int Exit, byte[] Out, string Err) RunBytes(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        var i = haystack.AsSpan(start).IndexOf(needle);
        return i < 0 ? -1 : start + i;
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
    /// <c>diag/</c> inputs mixed, mutated and broken into <see cref="RandomCorpusSize"/> files.
    /// </summary>
    private static byte[][] GenerateRandomCorpus(ulong seed)
    {
        var sources = Directory.EnumerateFiles(Path.Combine(Golden.CasesRoot, "parse"), "*.byn", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllBytes)
            .Where(b => b.Length > 0)
            .ToArray();
        var lines = sources.SelectMany(SplitLines).ToArray();
        var rng = new XorShift(seed);
        var corpus = new byte[RandomCorpusSize][];
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
            corpus[i] = bytes.ToArray();
        }
        return corpus;
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
}
