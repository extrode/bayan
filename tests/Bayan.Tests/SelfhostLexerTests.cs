using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary><c>selfhost/lexer</c>, built by the bootstrap compiler and run against the <c>lex/</c> goldens.</summary>
public sealed class SelfhostLexerTests(ITestOutputHelper output)
{
    public static TheoryData<string> Cases => Golden.Cases("lex");

    private static string LexerDir => Path.Combine(Golden.RepoRoot, "selfhost", "lexer");

    /// <summary>The built <c>bayan-lex</c>, or the compiler's complaint.</summary>
    private static readonly Lazy<(string? Path, string Error)> BayanLex = new(() => BuildLex(SelfhostSemanticsHarness.SnapshotDir));

    /// <summary><c>selfhost/lexer</c> built in fast mode; under <paramref name="snapshot"/> a schemata build listed in its <c>manifest.json</c>.
    /// Never coverage-instrumented: the lexer group ignored <c>BAYAN_COVERAGE</c> before snapshot recording, and a guard
    /// <c>BAYAN_COVERAGE_FILES</c> naming only non-lexer files would make the build throw.</summary>
    internal static (string? Path, string Error) BuildLex(string? snapshot)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.FromPath(Path.Combine(LexerDir, "main.byn"));
        if (c.Diagnostics.HasErrors) return (null, string.Join("\n", c.Diagnostics.Items));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal), snapshot, null, null);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static string Built
    {
        get
        {
            var (path, error) = BayanLex.Value;
            if (path is null) Assert.Fail($"selfhost/lexer/main.byn did not build:\n{error}");
            return path!;
        }
    }

    /// <summary>Runs <paramref name="program"/>; under <c>BAYAN_SNAPSHOT</c> the run is recorded (spec 013 R14).</summary>
    private static (int Exit, string Out, string Err) Run(string program, params string[] args) => RunIn(SelfhostSemanticsHarness.SnapshotDir, program, args);

    internal static (int Exit, string Out, string Err) RunIn(string? snapshot, string program, string[] args)
    {
        if (snapshot is { } dir)
        {
            var (exit, o, e) = SelfhostSemanticsHarness.RunRecorded(dir, program, null, args);
            return (exit, Encoding.UTF8.GetString(ProcessText.Lf(o)), Encoding.UTF8.GetString(ProcessText.Lf(e)));
        }
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary><see cref="Run"/> with stdout kept as bytes, so invalid UTF-8 is compared as printed.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, params string[] args) => RunBytes(program, null, args);

    /// <summary><see cref="RunBytes(string, string[])"/> with <paramref name="input"/> fed to stdin.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, byte[]? input, params string[] args) => RunBytesIn(SelfhostSemanticsHarness.SnapshotDir, program, input, args);

    internal static (int Exit, byte[] Out, string Err) RunBytesIn(string? snapshot, string program, byte[]? input, string[] args)
    {
        if (snapshot is { } dir)
        {
            var (exit, o, e) = SelfhostSemanticsHarness.RunRecorded(dir, program, input, args);
            return (exit, ProcessText.Lf(o), Encoding.UTF8.GetString(ProcessText.Lf(e)));
        }
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndLfAsync();
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, ProcessText.Lf(stdout.ToArray()), stderr.Result);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LexGolden(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var (_, stdout, _) = Run(Built, input);
        if (Golden.Compare(Path.ChangeExtension(input, ".tokens"), stdout, update: false).Failure is { } failure) Assert.Fail(failure);
    }

    [Fact]
    public void LexDiffFindsNoDifferences()
    {
        var (exit, stdout, stderr) = Run(LexDiffTests.LexDiff.Value, "--left", Built, "--golden", ".tokens", Path.Combine(Golden.CasesRoot, "lex"));
        Assert.True(exit == 0, stdout + stderr);
        Assert.Matches(@"^\d+ files, 0 differ\r?\n$", stderr);
    }

    /// <summary>Spec 002 R7, R8: <c>bayan-lex --json</c> fed on stdin prints exactly <see cref="TokenDump.ToJson"/>.</summary>
    [Fact]
    public void JsonOnStdinMatchesTheReferenceLexer()
    {
        foreach (var input in Directory.GetFiles(Path.Combine(Golden.CasesRoot, "lex"), "*.byn").Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(input);
            var (exit, stdout, stderr) = RunBytes(Built, bytes, "--json");
            var result = Lexer.Lex(new SourceText(input, bytes));
            var expected = Encoding.UTF8.GetBytes(TokenDump.ToJson(result));
            Assert.True(expected.AsSpan().SequenceEqual(stdout),
                $"{input}: --json differs\nexpected: {Encoding.UTF8.GetString(expected)}\nactual:   {Encoding.UTF8.GetString(stdout)}\n{stderr}");
            Assert.Equal(result.Diagnostics.Any(d => d.IsError) ? 1 : 0, exit);
        }
    }

    [Fact]
    public void JsonOnAMissingPathExitsFour()
    {
        var (exit, stdout, stderr) = Run(Built, "--json", Path.Combine(Golden.RepoRoot, "tmp", "no-such-file.byn"));
        Assert.Equal(4, exit);
        Assert.Empty(stdout);
        Assert.NotEmpty(stderr);
    }

    /// <summary>
    /// The dump prints a Bayan <c>TokenKind</c> by upper-casing the first byte of its camelCase name,
    /// so the C# names must be exactly that form of the Bayan members, in the same order.
    /// </summary>
    [Fact]
    public void TokenKindNamesAreTheCamelCaseNamesUpperCased()
    {
        var source = File.ReadAllText(Path.Combine(LexerDir, "tokens.byn"));
        var body = Regex.Match(source, @"^(?:public )?enum TokenKind \{\n(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var bayan = Regex.Matches(body, @"^\s*([a-z]\w*),?\s*(?://.*)?$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        var csharp = Enum.GetNames<TokenKind>();
        foreach (var name in csharp)
            Assert.True(char.IsAsciiLetterUpper(name[0]), name);
        Assert.Equal(csharp, bayan.Select(n => char.ToUpperInvariant(n[0]) + n[1..]));
    }

    private const int CorpusSize = 10_000;

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

    /// <summary>The <c>lex/</c> inputs mixed, mutated and broken into <see cref="CorpusSize"/> files.</summary>
    private static byte[][] GenerateCorpus()
    {
        var cases = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "lex"), "*.byn").Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray();
        var lines = cases.SelectMany(SplitLines).ToArray();
        var rng = new XorShift(0x9E3779B97F4A7C15);
        var corpus = new byte[CorpusSize][];
        for (var i = 0; i < CorpusSize; i++)
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
                        var text = rng.Pick(cases);
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

    /// <summary>
    /// Spec 001 R10 and law L2: on every corpus input the Bayan lexer prints exactly the C# dump, keeps
    /// every span inside the input and does not panic. Inputs stay in <c>tmp/corpus/</c> for a failure.
    /// </summary>
    [Fact]
    public void CorpusMatchesTheReferenceLexer()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "corpus");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var corpus = GenerateCorpus();
        var names = new string[corpus.Length];
        for (var i = 0; i < corpus.Length; i++)
        {
            names[i] = $"{i:D5}.byn";
            File.WriteAllBytes(Path.Combine(dir, names[i]), corpus[i]);
        }

        var (exit, bytes, stderr) = RunBytes(Built, "--many", dir);

        var cursor = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var header = Encoding.UTF8.GetBytes($"== {names[i]}\n");
            Assert.True(bytes.AsSpan(cursor).StartsWith(header),
                $"bayan-lex --many stopped before {names[i]} (exit {exit}):\n{stderr}");
            cursor += header.Length;
            var next = i + 1 < names.Length ? bytes.AsSpan(cursor).IndexOf(Encoding.UTF8.GetBytes($"== {names[i + 1]}\n")) : -1;
            var end = next < 0 ? bytes.Length : cursor + next;
            var actual = bytes[cursor..end];
            cursor = end;
            var expected = Encoding.UTF8.GetBytes(TokenDump.Dump(Lexer.Lex(new SourceText(names[i], corpus[i]))));
            if (actual.AsSpan().SequenceEqual(expected)) continue;
            var expectedPath = Path.Combine(dir, Path.ChangeExtension(names[i], ".tokens"));
            File.WriteAllBytes(expectedPath, expected);
            var failure = Golden.Compare(expectedPath, Encoding.UTF8.GetString(actual), update: false).Failure ?? "outputs differ in bytes only (CRLF or invalid UTF-8)";
            Assert.Fail($"{Path.Combine(dir, names[i])}: Bayan and C# lexers disagree\n{failure}");
        }
        Assert.True(exit == 0 && !stderr.Contains("panic:"), $"exit {exit}\n{stderr}");
    }

    /// <summary>Every corpus input and <c>lex/</c> case with its <c>bayan-lex --json</c> run on stdin, made once for L001 and L002.</summary>
    private static readonly Lazy<(byte[] Input, int Exit, byte[] Out, string Err, bool TimedOut)[]> StdinRuns = new(() =>
    {
        var built = Built;
        var inputs = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "lex"), "*.byn").Order(StringComparer.Ordinal)
            .Select(File.ReadAllBytes).Concat(GenerateCorpus()).ToArray();
        var runs = new (byte[], int, byte[], string, bool)[inputs.Length];
        // sequential: concurrent Process.Start calls leak stdin pipe ends into each other's children, which then never see EOF
        Parallel.For(0, inputs.Length, new ParallelOptions { MaxDegreeOfParallelism = 1 }, i =>
        {
            var (exit, stdout, stderr, timedOut, _) = RunOnStdin(built, ["--json"], inputs[i], 30_000);
            runs[i] = (inputs[i], exit, stdout, stderr, timedOut);
        });
        return runs;
    });

    /// <summary>Runs <paramref name="program"/> with <paramref name="input"/> on stdin. A run that misses <paramref name="timeoutMs"/>
    /// is killed and tried once more: a Windows CI runner once stalled one tiny input past 30 s (CI run 41612215707) that lexes in
    /// milliseconds everywhere else, so only a second timeout counts as a hang.</summary>
    internal static (int Exit, byte[] Out, string Err, bool TimedOut, int Attempts) RunOnStdin(string program, string[] args, byte[] input, int timeoutMs)
    {
        for (var attempt = 1; ; attempt++)
        {
            var psi = new ProcessStartInfo(program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var stdout = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
            var stderr = p.StandardError.ReadToEndLfAsync();
            try
            {
                p.StandardInput.BaseStream.Write(input);
                p.StandardInput.Close();
            }
            catch (IOException)
            {
                // the program died before reading everything; its exit code says so
            }
            var done = p.WaitForExit(timeoutMs);
            if (!done) p.Kill(entireProcessTree: true);
            p.WaitForExit();
            copy.Wait();
            if (done || attempt == 2) return (done ? p.ExitCode : -1, ProcessText.Lf(stdout.ToArray()), stderr.Result, !done, attempt);
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    [Fact]
    public void RunOnStdinTriesAStalledRunOnceMoreThenReportsTheTimeout()
    {
        var (program, args) = OperatingSystem.IsWindows()
            ? ("powershell", new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" })
            : ("sleep", new[] { "30" });
        var (exit, _, _, timedOut, attempts) = RunOnStdin(program, args, [], 300);
        Assert.True(timedOut);
        Assert.Equal(2, attempts);
        Assert.Equal(-1, exit);
    }

    [Fact]
    public void RunOnStdinRunsAPromptProgramOnce()
    {
        var input = "x\n"u8.ToArray();
        var (exit, stdout, _, timedOut, attempts) = RunOnStdin(Built, ["--json"], input, 30_000);
        Assert.False(timedOut);
        Assert.Equal(1, attempts);
        Assert.Equal(0, exit);
        Assert.Equal(TokenDump.ToJson(Lexer.Lex(new SourceText("<stdin>", input))), Encoding.UTF8.GetString(stdout));
    }

    /// <summary>Law L001: on every corpus input and <c>lex/</c> case, <c>bayan-lex --json</c> on stdin prints exactly <see cref="TokenDump.ToJson"/>.</summary>
    [Fact]
    [Trait("Law", "L001")]
    public void BayanLexJsonEqualsTheReferenceLexerOnEveryInput()
    {
        var runs = StdinRuns.Value;
        for (var i = 0; i < runs.Length; i++)
        {
            var (input, _, actual, stderr, _) = runs[i];
            var expected = Encoding.UTF8.GetBytes(TokenDump.ToJson(Lexer.Lex(new SourceText("<stdin>", input))));
            if (expected.AsSpan().SequenceEqual(actual)) continue;
            Assert.Fail($"input {i} ({Hex(input)}): --json differs\nexpected: {Encoding.UTF8.GetString(expected)}\nactual:   {Encoding.UTF8.GetString(actual)}\n{stderr}");
        }
    }

    /// <summary>Law L002: on every input <c>bayan-lex</c> exits 0 or 1 in time and every span in its output lies in <c>[0, length]</c>.</summary>
    [Fact]
    [Trait("Law", "L002")]
    public void BayanLexNeverPanicsAndKeepsSpansInsideTheInput()
    {
        var runs = StdinRuns.Value;
        for (var i = 0; i < runs.Length; i++)
        {
            var (input, exit, stdout, stderr, timedOut) = runs[i];
            Assert.False(timedOut, $"input {i} ({Hex(input)}): timed out");
            Assert.True(exit is 0 or 1, $"input {i} ({Hex(input)}): exit {exit}\n{stderr}");
            foreach (Match m in Regex.Matches(Encoding.Latin1.GetString(stdout), "\"start\":(\\d+),\"end\":(\\d+)"))
            {
                var start = long.Parse(m.Groups[1].Value);
                var end = long.Parse(m.Groups[2].Value);
                Assert.True(start <= end && end <= input.Length, $"input {i} ({Hex(input)}): span {start}..{end} outside [0, {input.Length}]");
            }
        }
    }

    /// <summary>Spec 001 R10: the Bayan lexer, built in fast mode, is within 10x of the C# lexer on 1 MB.</summary>
    [Fact]
    [Trait("Snapshot", "skip")]
    public void LexesOneMegabyteWithinTenTimesTheReferenceLexer()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_SKIP_TIMING") == "1")
        {
            output.WriteLine("skipped: BAYAN_SKIP_TIMING=1");
            return;
        }
        var source = LexerTests.OneMegabyte();
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-timing", "big.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, source.Bytes.ToArray());

        var bayan = new long[5];
        for (var i = 0; i < bayan.Length; i++)
        {
            var (exit, stdout, stderr) = Run(Built, "--time", path);
            Assert.True(exit == 0, stdout + stderr);
            bayan[i] = long.Parse(stdout.Trim());
        }
        Lexer.Lex(source); // warm up statics and JIT
        var csharp = new long[5];
        for (var i = 0; i < csharp.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            Lexer.Lex(source);
            csharp[i] = (long)watch.Elapsed.TotalNanoseconds;
        }
        var bayanMedian = bayan.Order().ElementAt(2);
        var csharpMedian = csharp.Order().ElementAt(2);
        var ratio = (double)bayanMedian / csharpMedian;
        output.WriteLine($"bayan-lex {bayanMedian} ns, C# {csharpMedian} ns, ratio {ratio:F2}");
        Assert.True(ratio <= 10, $"bayan-lex median {bayanMedian} ns is {ratio:F2}x the C# median {csharpMedian} ns");
    }
}

/// <summary>Not in the <c>SelfhostLexer</c> filter: the recording helpers of <see cref="SelfhostLexerTests"/> work under a snapshot directory.</summary>
public sealed class LexerSnapshotRecordingTests
{
    /// <summary>Spec 013 R14: the lexer group records: its build is listed in <c>manifest.json</c> and both run helpers append to <c>runs.jsonl</c>.</summary>
    [Fact]
    [Trait("Snapshot", "skip")]
    public void BuildAndRunsAreRecordedUnderASnapshotDirectory()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-lexer-snap");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var (path, error) = SelfhostLexerTests.BuildLex(dir);
        Assert.True(path is not null, error);
        Assert.True(File.Exists(Path.Combine(dir, "manifest.json")), "the build wrote no manifest.json");
        var input = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "lex"), "*.byn").Order(StringComparer.Ordinal).First();
        SelfhostLexerTests.RunIn(dir, path!, [input]);
        SelfhostLexerTests.RunBytesIn(dir, path!, null, [input]);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, "runs.jsonl")).Length);
    }
}
