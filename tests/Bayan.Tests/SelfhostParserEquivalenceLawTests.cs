using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
    public async Task BayanParseEqualsTheCParserOnEveryInput()
    {
        await CheckCorpus("parse", Cases(SelfhostParserTests.ParseCases));
        await CheckCorpus("diag", Cases(SelfhostParserTests.DiagCases));
        await CheckCorpus("valid", Generated(SelfhostParserGenerator.GenerateValid(SelfhostParserGenerator.Seeds)));
        await CheckCorpus("corrupt", Generated(SelfhostParserGenerator.GenerateCorrupt(SelfhostParserGenerator.Seeds)));

        var (randomA, crashedA) = RandomCorpus(RandomSeedA);
        var (randomB, crashedB) = RandomCorpus(RandomSeedB);
        output.WriteLine($"random corpus: {crashedA.Length + crashedB.Length} candidates throw in the C# reference (Q506) and were replaced by empty inputs");
        output.WriteLine($"  seed {RandomSeedA:X16}: {string.Join(" ", crashedA)}");
        output.WriteLine($"  seed {RandomSeedB:X16}: {string.Join(" ", crashedB)}");
        Assert.True(randomA.Zip(randomB).Any(p => !p.First.AsSpan().SequenceEqual(p.Second)),
            "the two seeds produced the same corpus, so the second seed proves nothing");

        var (wrapA, checkedA) = await CheckCorpus("random-a", randomA.Select((b, i) => ($"random-{RandomSeedA:X16}-{i:D5}", b)).ToArray());
        var (wrapB, checkedB) = await CheckCorpus("random-b", randomB.Select((b, i) => ($"random-{RandomSeedB:X16}-{i:D5}", b)).ToArray());
        Assert.True(checkedA - crashedA.Length >= RandomCorpusSize * 9 / 10,
            $"only {checkedA - crashedA.Length} of {RandomCorpusSize} random seed-A inputs were compared ({wrapA} Q505 wrapping-only)");
        Assert.True(checkedB - crashedB.Length >= RandomCorpusSize * 9 / 10,
            $"only {checkedB - crashedB.Length} of {RandomCorpusSize} random seed-B inputs were compared ({wrapB} Q505 wrapping-only)");
    }

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
    /// Writes <paramref name="inputs"/> to <c>tmp/l017/&lt;corpus&gt;/</c> as <c>00000.byn</c>, …, runs one
    /// <c>bayan-parse --many</c> over the directory for the dumps and one single-file <c>bayan-parse</c> per
    /// input for the exit code. Each dump (which ends at the first <c>span out of range:</c> line, L018's
    /// Bayan-only check) must equal <see cref="AstDump.Dump"/>, and each process exit must equal the C# exit,
    /// independently of the dump comparison. A dump that differs only in line wrapping where the C# diagnostic
    /// holds a space inside backticks is Q505's renderer bug; it is counted, not compared, and its exit still
    /// is. Inputs are numbered so the <c>Io.listDir</c> byte order <c>--many</c> prints is the write order.
    /// Returns the count that differed only by Q505 wrapping and the count that was compared.
    /// </summary>
    private async Task<(int WrappingOnly, int Checked)> CheckCorpus(string corpus, IReadOnlyList<(string Label, byte[] Bytes)> inputs)
    {
        output.WriteLine($"{corpus}: {inputs.Count} inputs");
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "l017", corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string Name(int i) => $"{i:D5}.byn";
        for (var i = 0; i < inputs.Count; i++) File.WriteAllBytes(Path.Combine(dir, Name(i)), inputs[i].Bytes);

        var exits = await ExitCodes(dir, inputs.Count);

        var (exit, stdout, stderr) = SelfhostParserTests.Run(Built, "--many", dir);
        Assert.True(exit == 0, $"bayan-parse --many {corpus} exited {exit}:\n{stderr}");
        Assert.False(stderr.Contains("panic:", StringComparison.Ordinal), $"bayan-parse panicked on {corpus}:\n{stderr}");

        var wrappingOnly = new List<string>();
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

            var actual = SelfhostParserTests.BeforeSpanLines(segment);

            var tree = SyntaxTree.Parse(new SourceText(inputs[i].Label, inputs[i].Bytes));
            var expected = AstDump.Dump(tree);
            if (actual != expected && !IsWrappingOnly(actual, expected, tree))
            {
                var expectedPath = Path.Combine(dir, Path.ChangeExtension(Name(i), ".ast"));
                File.WriteAllText(expectedPath, expected);
                var failure = Golden.Compare(expectedPath, actual, update: false).Failure ?? "outputs differ in bytes only (CRLF or invalid UTF-8)";
                Assert.Fail($"{inputs[i].Label} ({Path.Combine(dir, Name(i))}): bayan-parse and the C# parser disagree\n{failure}");
            }
            if (actual != expected) wrappingOnly.Add(inputs[i].Label);

            var expectedExit = tree.Diagnostics.Any(d => d.IsError) ? 1 : 0;
            Assert.True(exits[i] == expectedExit,
                $"{inputs[i].Label} ({Path.Combine(dir, Name(i))}): exit code mismatch (C# {expectedExit}, bayan-parse {exits[i]})");
        }

        var checkedCount = inputs.Count - wrappingOnly.Count;
        output.WriteLine($"{corpus}: {checkedCount} compared, {wrappingOnly.Count} differ only by Q505 wrapping" +
            (wrappingOnly.Count == 0 ? "" : $" (e.g. {string.Join(" ", wrappingOnly.Take(5))})"));
        return (wrappingOnly.Count, checkedCount);
    }

    /// <summary>
    /// One single-file <c>bayan-parse</c> process per input, returning its exit code in directory order.
    /// Unix batches the spawns in a POSIX shell, because a .NET <see cref="Process.Start"/> costs tens of
    /// milliseconds here and hundreds of inputs would dominate the proof; Windows falls back to the same
    /// process loop in managed code.
    /// </summary>
    private static async Task<int[]> ExitCodes(string dir, int count)
    {
        if (OperatingSystem.IsWindows()) return await ManagedExitCodes(dir, count);

        var script = Path.Combine(Golden.RepoRoot, "tmp", "l017", "exits.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, """
            #!/bin/sh
            LC_ALL=C
            program=$1
            dir=$2
            n=0
            for f in "$dir"/*.byn; do
                ( "$program" "$f" >/dev/null 2>&1; printf '%s %s\n' "${f##*/}" "$?" ) &
                n=$((n+1))
                if [ "$n" -ge 8 ]; then wait; n=0; fi
            done
            wait
            exit 0
            """);
        var (_, stdout, stderr) = SelfhostParserTests.Run("/bin/sh", script, Built, dir);
        var exits = new int[count];
        var seen = new bool[count];
        foreach (var line in stdout.Split('\n'))
        {
            if (line.Length == 0) continue;
            var space = line.LastIndexOf(' ');
            var code = 0;
            Assert.True(space > 0 && int.TryParse(line[(space + 1)..], out code),
                $"bad exit line from the shell on {dir}: {line}\n{stderr}");
            var name = line[..space];
            var dot = name.IndexOf('.');
            var index = dot > 0 && int.TryParse(name[..dot], out var i) ? i : -1;
            Assert.True(index >= 0 && index < count && !seen[index], $"unexpected shell exit line on {dir}: {line}\n{stderr}");
            seen[index] = true;
            exits[index] = code;
        }
        Assert.True(seen.All(x => x), $"the shell did not report every exit code under {dir}\n{stderr}");
        return exits;
    }

    /// <summary>The Windows fallback: one single-file <c>bayan-parse</c> process per input, eight at a time.</summary>
    private static async Task<int[]> ManagedExitCodes(string dir, int count)
    {
        using var gate = new SemaphoreSlim(8);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { return await ExitCode(Path.Combine(dir, $"{i:D5}.byn")).ConfigureAwait(false); }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>The single-file <c>bayan-parse</c> exit code, with its dump drained.</summary>
    private static async Task<int> ExitCode(string path)
    {
        var psi = new ProcessStartInfo(Built) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        return p.ExitCode;
    }

    /// <summary>
    /// True when <paramref name="actual"/> and <paramref name="expected"/> have the same tree and the same
    /// diagnostic prose except for where lines break, and the C# diagnostic holds a space inside backticks
    /// (Q505: <c>wrap</c> splits there, C#'s <c>Words</c> does not). Any other difference is a real mismatch.
    /// </summary>
    private static bool IsWrappingOnly(string actual, string expected, SyntaxTree tree)
    {
        if (!tree.Diagnostics.Any(HasMultiWordBacktick)) return false;
        if (TreePart(actual) != TreePart(expected)) return false;
        return Unwrap(actual) == Unwrap(expected);
    }

    /// <summary>The dump before its diagnostics section (the first blank line).</summary>
    private static string TreePart(string dump)
    {
        var i = dump.IndexOf("\n\n", StringComparison.Ordinal);
        return i < 0 ? dump : dump[..i];
    }

    /// <summary>Every line break and its indentation folded back to the single space it replaced.</summary>
    private static string Unwrap(string text) => Regex.Replace(text.Replace("\r\n", "\n"), "\n[ \t]*", " ");

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
    /// reference dump, so it is recorded and replaced by an empty input, as T18 replaces the C# crashes.
    /// Candidates whose diagnostics merely rewrap (Q505) stay in the corpus and are compared.
    /// </summary>
    private static (byte[][] Corpus, string[] Crashed) RandomCorpus(ulong seed)
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
            corpus[i] = bytes.ToArray();
        }
        return (corpus, crashed.ToArray());
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
        catch (Exception)
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
