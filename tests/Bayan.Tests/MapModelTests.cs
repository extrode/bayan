using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Model check for <c>Map</c> (L3): <c>run/map-model.byn</c> applies 5,000 xorshift-chosen sets and removes
/// over 64 keys; <see cref="RunTests"/> pins its output to <c>map-model.stdout</c>, and this test pins that
/// golden to an insertion-ordered reference model running the same sequence.
/// </summary>
public sealed class MapModelTests
{
    [Fact]
    public void GoldenMatchesReferenceModel()
    {
        var keys = new List<long>();
        var values = new Dictionary<long, long>();
        var state = 0x9E3779B97F4A7C15UL;
        for (var step = 0L; step < 5000; step++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            var key = (long)(state % 64);
            if ((state >> 8) % 3 == 2)
            {
                if (values.Remove(key)) keys.Remove(key);
            }
            else
            {
                if (!values.ContainsKey(key)) keys.Add(key);
                values[key] = step;
            }
        }
        var expected = $"count {keys.Count}\n" + string.Concat(keys.Select(k => $"{k} {values[k]}\n"));
        var golden = File.ReadAllText(Path.Combine(Golden.CasesRoot, "run", "map-model.stdout")).ReplaceLineEndings("\n");
        Assert.Equal(expected, golden);
    }

    // ---- L003 (docs/laws/003-map-insertion-order.md): generated programs checked against a reference model ----

    private const int KeySpace = 8;
    private const int OpsPerSequence = 200;
    private const int OpsPerPhase = 40;

    private sealed record Outcome(string Stdout, string Stderr, int ExitCode);

    /// <summary>The law's model: live keys in first-insertion order since their last removal.</summary>
    private sealed class Model
    {
        public readonly List<long> Order = [];
        public readonly Dictionary<long, long> Values = [];

        public void Set(long k, long v)
        {
            if (Values.TryAdd(k, v)) Order.Add(k);
            else Values[k] = v;
        }

        public void Remove(long k)
        {
            if (Values.Remove(k)) Order.Remove(k);
        }

        public void Clear()
        {
            Order.Clear();
            Values.Clear();
        }
    }

    public static TheoryData<int, bool> Sequences
    {
        get
        {
            var data = new TheoryData<int, bool>();
            for (var seed = 1; seed <= 24; seed++) data.Add(seed, seed % 2 == 0);
            return data;
        }
    }

    [Trait("Law", "L003")]
    [Theory]
    [MemberData(nameof(Sequences))]
    public void L003GeneratedSequenceMatchesModel(int seed, bool stringKeys)
    {
        var state = 0x9E3779B97F4A7C15UL ^ ((ulong)seed * 0xD1B54A32D192ED03UL);
        long Next(int bound)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return (long)((state >> 11) % (ulong)bound);
        }
        string Key(long k) => stringKeys ? $"\"k{k}\"" : k.ToString();
        string KeyText(long k) => stringKeys ? $"k{k}" : k.ToString();

        var model = new Model();
        var source = new StringBuilder("void bump(ref i64 n) {\n    n += 100;\n}\n");
        source.Append(stringKeys ? "var m = Map<string, i64>();\n" : "var m = Map<i64, i64>();\n");
        var expected = new StringBuilder();
        for (var step = 0; step < OpsPerSequence; step++)
        {
            var k = Next(KeySpace);
            var v = Next(1000);
            switch (Next(10))
            {
                case < 4:
                    source.Append($"m[{Key(k)}] = {v};\n");
                    model.Set(k, v);
                    break;
                case < 6:
                    source.Append($"m.remove({Key(k)});\n");
                    model.Remove(k);
                    break;
                case 6:
                    source.Append($"m.slot({Key(k)}) += {v};\n");
                    model.Set(k, model.Values.GetValueOrDefault(k) + v);
                    break;
                case 7:
                    source.Append($"bump(ref m.slot({Key(k)}));\n");
                    model.Set(k, model.Values.GetValueOrDefault(k) + 100);
                    break;
                case 8:
                    source.Append($"m.slot({Key(k)}) = {v};\n");
                    model.Set(k, v);
                    break;
                default:
                    if (Next(4) == 0)
                    {
                        source.Append("m.clear();\n");
                        model.Clear();
                    }
                    else
                    {
                        source.Append($"m[{Key(k)}] = {v};\n");
                        model.Set(k, v);
                    }
                    break;
            }
            if ((step + 1) % OpsPerPhase != 0) continue;
            source.Append($"print(\"phase {step + 1}\");\nprint($\"length {{m.length}}\");\n");
            source.Append("for (let e in m) print($\"{e.key} {e.value}\");\n");
            expected.Append($"phase {step + 1}\nlength {model.Order.Count}\n");
            foreach (var key in model.Order) expected.Append($"{KeyText(key)} {model.Values[key]}\n");
            for (long key = 0; key < KeySpace; key++)
            {
                source.Append($"print($\"get {KeyText(key)} {{m.getOr({Key(key)}, -1)}}\");\n");
                expected.Append($"get {KeyText(key)} {model.Values.GetValueOrDefault(key, -1)}\n");
            }
        }
        var outcome = Run($"l003-{seed}.byn", source.ToString());
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(expected.ToString(), outcome.Stdout.ReplaceLineEndings("\n"));
    }

    [Trait("Law", "L003")]
    [Theory]
    [InlineData("m[3] = 30;", "m[3]", true)]
    [InlineData("m[3] = 30; m[3] = 31;", "m[3]", true)]
    [InlineData("m[3] = 30; m.slot(4) += 1;", "m[4]", true)]
    [InlineData("m[3] = 30;", "m[4]", false)]
    [InlineData("m[3] = 30; m.remove(3);", "m[3]", false)]
    [InlineData("m[3] = 30; m.clear();", "m[3]", false)]
    [InlineData("m[3] = 30; m.remove(3); m[3] = 5;", "m[3]", true)]
    [InlineData("m.getOr(3, 0); m.contains(3);", "m[3]", false)]
    public void L003IndexPanicsExactlyForNonLiveKeys(string setup, string read, bool live)
    {
        var outcome = Run("l003-index.byn", $"var m = Map<i64, i64>();\n{setup}\nprint($\"got {{{read}}}\");\n");
        if (live)
        {
            Assert.Equal(0, outcome.ExitCode);
            Assert.StartsWith("got ", outcome.Stdout);
        }
        else
        {
            Assert.NotEqual(0, outcome.ExitCode);
            Assert.StartsWith("panic: key not found in map", outcome.Stderr);
            Assert.Equal("", outcome.Stdout);
        }
    }

    private static Outcome Run(string name, string source)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create([new SourceText(name, Encoding.UTF8.GetBytes(source))], ".");
        if (c.Diagnostics.HasErrors) throw new Xunit.Sdk.XunitException(DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true) + "\n" + source);
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        if (!build.Succeeded) throw new Xunit.Sdk.XunitException(build.Errors + "\n" + source);
        var work = Path.Combine(Golden.RepoRoot, "tmp", "run", "l003_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = work,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            throw new Xunit.Sdk.XunitException("timed out after 120 s");
        }
        return new Outcome(stdout.Result, stderr.Result, p.ExitCode);
    }
}
