using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L015 (docs/laws/015-interning-is-identity-on-text.md): over generated texts, two symbols are equal exactly when
/// their texts are, a symbol's text is the text it was interned from, a literal equals its runtime twin, symbol
/// <c>&lt;</c> agrees with <c>string</c> <c>&lt;</c>, and the output is the same under two hash seeds.
/// Seeds 1..32; <c>BAYAN_L015_SEEDS</c> raises the count.
/// </summary>
[Trait("Law", "L015")]
public sealed class SymbolLawTests(ITestOutputHelper output)
{
    private static readonly string[] Pool = ["", "a", "ab", "abc", "b", "B", "total", "totals", "x_1", "é", "aé", "€", "😀", "a😀", "｡", "名前", "q\"\\", "{z}"];
    private static readonly string[] Runes = ["a", "b", "z", "A", "Z", "0", "_", "\"", "\\", "é", "ß", "Ω", "€", "中", "｡", "😀", "\U0010FFFF"];
    private static readonly string[] Seeds = ["0", "9e3779b97f4a7c15"];

    private sealed record Generated(string Source, string Expected, HashSet<string> Choices);

    [Fact]
    public void InterningIsIdentityOnText()
    {
        var compiler = Toolchain.FindCompiler();
        if (compiler is null)
        {
            output.WriteLine("skipped: no C compiler found");
            return;
        }
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("BAYAN_L015_SEEDS"), out var n) && n > 0 ? n : 32;
        var failures = new ConcurrentBag<(int Seed, string Message)>();
        var choices = new ConcurrentBag<string>();
        Parallel.For(1, seeds + 1, seed =>
        {
            var g = Generate(seed);
            foreach (var c in g.Choices) choices.Add(c);
            if (Failure(compiler, g.Source, g.Expected, $"gen-{seed}.byn") is { } failure)
                failures.Add((seed, $"seed {seed}: {failure}\nprogram:\n{g.Source}"));
        });
        output.WriteLine($"seeds {seeds}, failed {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join("\n\n", failures.OrderBy(f => f.Seed).Select(f => f.Message)));
        // Every generator choice is drawn by some seed.
        foreach (var c in new[] { "pool", "random", "prefix", "extension", "duplicate", "empty", "ascii", "multibyte", "bytes", "strbuf", "string", "literal", "grow" })
            Assert.True(choices.Contains(c), $"no program draws `{c}`");
    }

    /// <summary>The proof can fail: output that disagrees with the model is reported.</summary>
    [Fact]
    public void DisagreeingOutputFails()
    {
        var compiler = Toolchain.FindCompiler();
        if (compiler is null) return;
        var g = Generate(1);
        var expected = g.Expected.StartsWith("true", StringComparison.Ordinal) ? "false" + g.Expected[4..] : "true" + g.Expected[5..];
        Assert.StartsWith("Debug output differs from the model under seed 0", Failure(compiler, g.Source, expected, "gen-1.byn"));
    }

    /// <summary>
    /// A program over 5 to 12 texts: each from <see cref="Pool"/>, random runes, a prefix or an extension of an
    /// earlier text, or a repeat of one. Each is built at run time from UTF-8 bytes, from runes appended to a
    /// <c>StrBuf</c>, or as a string, and interned in shuffled order, some seeds after a loop of other interns that
    /// grows the table; about half the distinct texts are also symbol literals. It prints, for every symbol,
    /// whether its <c>.text</c> equals its string, and for every ordered pair <c>a == b</c>, <c>a &lt; b</c> and the
    /// strings' <c>&lt;</c>. The expected output is computed from the texts' UTF-8 bytes.
    /// </summary>
    private static Generated Generate(int seed)
    {
        var rng = new Random(seed);
        var choices = new HashSet<string>();
        var texts = new List<string>();
        var count = rng.Next(5, 13);
        for (var i = 0; i < count; i++)
        {
            string t;
            switch (i == 0 ? 0 : rng.Next(5))
            {
                case 0:
                    t = Pool[rng.Next(Pool.Length)];
                    choices.Add("pool");
                    break;
                case 1:
                    t = RandomText(rng, rng.Next(5));
                    choices.Add("random");
                    break;
                case 2:
                    var runes = RunesOf(texts[rng.Next(texts.Count)]);
                    t = string.Concat(runes.Take(rng.Next(runes.Count + 1)));
                    choices.Add("prefix");
                    break;
                case 3:
                    t = texts[rng.Next(texts.Count)] + RandomText(rng, rng.Next(1, 3));
                    choices.Add("extension");
                    break;
                default:
                    t = texts[rng.Next(texts.Count)];
                    choices.Add("duplicate");
                    break;
            }
            choices.Add(t.Length == 0 ? "empty" : Encoding.UTF8.GetByteCount(t) == t.Length ? "ascii" : "multibyte");
            texts.Add(t);
        }

        var sb = new StringBuilder($"// Generated by SymbolLawTests (L015), seed {seed}.\nvoid main() fails Utf8Error {{\n");
        // (symbol, string, text): runtime symbols, then literals paired with their first runtime twin's string.
        var symbols = new List<(string Sym, string Str, string Text)>();
        var literals = new List<string>();
        foreach (var t in texts.Distinct(StringComparer.Ordinal))
        {
            if (rng.Next(2) == 0) continue;
            sb.Append($"    let l{literals.Count}: Symbol = {Literal(t)}\n");
            choices.Add("literal");
            literals.Add(t);
        }
        for (var i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            switch (rng.Next(3))
            {
                case 0:
                    var bytes = Encoding.UTF8.GetBytes(t);
                    sb.Append(bytes.Length == 0 ? $"    let b{i} = List<u8>()\n" : $"    var b{i} = List<u8>()\n");
                    foreach (var b in bytes) sb.Append($"    b{i}.append(0x{b:X2})\n");
                    sb.Append($"    let s{i} = try Utf8.decode(b{i}.span)\n");
                    choices.Add("bytes");
                    break;
                case 1:
                    var runes = RunesOf(t);
                    sb.Append(runes.Count == 0 ? $"    let buf{i} = StrBuf(capacity: 1)\n" : $"    var buf{i} = StrBuf(capacity: 1)\n");
                    foreach (var r in runes) sb.Append($"    buf{i}.append({Literal(r)})\n");
                    sb.Append($"    let s{i} = buf{i}.view\n");
                    choices.Add("strbuf");
                    break;
                default:
                    sb.Append($"    let s{i} = {Literal(t)}\n");
                    choices.Add("string");
                    break;
            }
        }
        // Recursion, not a loop: a `for` emits an unused `continue_N` label, which `-Wall -Werror` rejects.
        var grow = rng.Next(2) == 0 ? rng.Next(70, 150) : 0;
        if (grow > 0)
        {
            sb.Insert(sb.ToString().IndexOf("void main", StringComparison.Ordinal), "i64 grow(i64 n) uses alloc {\n    if n < 1 { return 0 }\n    return Symbol.intern($\"g{n}\").text.length + grow(n - 1)\n}\n\n");
            sb.Append($"    let grown = grow({grow})\n");
            choices.Add("grow");
        }
        foreach (var i in Enumerable.Range(0, texts.Count).OrderBy(_ => rng.Next()))
            sb.Append($"    let r{i} = Symbol.intern(s{i})\n");
        for (var i = 0; i < texts.Count; i++) symbols.Add(($"r{i}", $"s{i}", texts[i]));
        for (var k = 0; k < literals.Count; k++) symbols.Add(($"l{k}", $"s{texts.IndexOf(literals[k])}", literals[k]));

        var expected = new StringBuilder();
        foreach (var (sym, str, _) in symbols)
        {
            sb.Append($"    print($\"{{{sym}.text == {str}}}\")\n");
            expected.Append("true\n");
        }
        foreach (var a in symbols)
            foreach (var b in symbols)
            {
                // `a == a` is `-Wtautological-compare`; a repeated text or a literal's twin compares two names.
                if (a.Sym == b.Sym) continue;
                sb.Append($"    print($\"{{{a.Sym} == {b.Sym}}} {{{a.Sym} < {b.Sym}}} {{{a.Str} < {b.Str}}}\")\n");
                var order = Encoding.UTF8.GetBytes(a.Text).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b.Text));
                var lt = order < 0 ? "true" : "false";
                expected.Append($"{(order == 0 ? "true" : "false")} {lt} {lt}\n");
            }
        if (grow > 0)
        {
            sb.Append("    print($\"{grown}\")\n");
            expected.Append($"{Enumerable.Range(1, grow).Sum(i => 1 + i.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)}\n");
        }
        sb.Append("}\n");
        return new Generated(sb.ToString(), expected.ToString(), choices);
    }

    private static string RandomText(Random rng, int runes)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < runes; i++) sb.Append(Runes[rng.Next(Runes.Length)]);
        return sb.ToString();
    }

    private static List<string> RunesOf(string t) => t.EnumerateRunes().Select(r => r.ToString()).ToList();

    private static string Literal(string t) => "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string? Failure(string[] compiler, string source, string expected, string path)
    {
        var c = Compilation.Create([SourceText.FromString(path, source)], ".");
        if (c.Diagnostics.HasErrors) return "rejected:\n" + DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true);
        foreach (var mode in new[] { BuildMode.Debug, BuildMode.Safe })
        {
            var cSource = CliApp.EmitC(c, mode);
            const string seedInit = "byn_hash_seed_init(false);";
            if (!cSource.Contains(seedInit, StringComparison.Ordinal)) return $"{mode} C has no `{seedInit}`";
            // The seed comes from the environment, so one build runs under each seed.
            cSource = cSource.Replace(seedInit, "byn_hash_seed = strtoull(getenv(\"BYN_L015_SEED\"), NULL, 16);");
            var (runs, stderr) = BuildStandalone(compiler, cSource, mode);
            if (runs is null) return $"{mode} C does not build under -Wall -Werror:\n{stderr}";
            for (var i = 0; i < Seeds.Length; i++)
            {
                var (code, stdout, err) = runs[i];
                if (code != 0) return $"{mode} exit {code} under seed {Seeds[i]}\nstderr:\n{err}";
                stdout = stdout.Replace("\r\n", "\n");
                if (stdout != expected) return $"{mode} output differs from the model under seed {Seeds[i]}:\n{FirstDifference(expected, stdout)}";
            }
            if (runs[0].Stdout != runs[1].Stdout) return $"{mode} output differs between seeds";
        }
        return null;
    }

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
            if (i >= e.Length || i >= a.Length || e[i] != a[i])
                return $"line {i + 1}: expected `{(i < e.Length ? e[i] : "")}`, got `{(i < a.Length ? a[i] : "")}`";
        return "no difference";
    }

    /// <summary>
    /// Builds <paramref name="cSource"/> as <c>out.c</c> with <c>-Wall -Werror</c> (the toolchain build passes
    /// <c>-w</c>) and the runtime without them, then runs it once under each of <see cref="Seeds"/>. Null runs
    /// mean the build failed.
    /// </summary>
    private static (List<(int Code, string Stdout, string Stderr)>? Runs, string Stderr) BuildStandalone(string[] compiler, string cSource, BuildMode mode)
    {
        var dir = Directory.CreateTempSubdirectory("bayan-l015-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.c"), cSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), Toolchain.RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), Toolchain.RuntimeHeader);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "out.exe" : "out");
            string[] define = mode == BuildMode.Debug ? ["-DBYN_DEBUG"] : ["-DBYN_NDEBUG"];
            foreach (var args in new[]
            {
                ["-std=c11", "-fwrapv", "-Wall", "-Werror", .. define, "-I.", "-c", "out.c", "-o", "out.o"],
                ["-std=c11", "-fwrapv", "-w", .. define, "-I.", "-c", "bayan-rt.c", "-o", "rt.o"],
                new[] { "out.o", "rt.o", "-o", exe, OperatingSystem.IsWindows() ? "-lshell32" : "-lm" },
            })
            {
                var cc = new ProcessStartInfo(compiler[0]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in compiler[1..]) cc.ArgumentList.Add(a);
                foreach (var a in args) cc.ArgumentList.Add(a);
                var (ccCode, ccOut, ccErr) = Exec(cc);
                if (ccCode != 0) return (null, ccOut + ccErr);
            }
            var runs = new List<(int, string, string)>();
            foreach (var seed in Seeds)
            {
                var run = new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                run.Environment["BYN_L015_SEED"] = seed;
                runs.Add(Exec(run));
            }
            return (runs, "");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (int Code, string Stdout, string Stderr) Exec(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.Result;
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }
}
