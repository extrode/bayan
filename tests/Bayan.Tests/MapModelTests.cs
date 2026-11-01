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

    /// <summary>Seeds for the generated key types; every fourth is a plain <c>Symbol</c> key.</summary>
    public static TheoryData<int> KeyTypeSeeds
    {
        get
        {
            var data = new TheoryData<int>();
            for (var seed = 1; seed <= 16; seed++) data.Add(seed);
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
        var source = new StringBuilder("void bump(ref i64 n) {\n    n += 100\n}\n");
        source.Append(stringKeys ? "var m = Map<string, i64>()\n" : "var m = Map<i64, i64>()\n");
        var expected = new StringBuilder();
        for (var step = 0; step < OpsPerSequence; step++)
        {
            var k = Next(KeySpace);
            var v = Next(1000);
            switch (Next(10))
            {
                case < 4:
                    source.Append($"m[{Key(k)}] = {v}\n");
                    model.Set(k, v);
                    break;
                case < 6:
                    source.Append($"let _ = m.remove({Key(k)})\n");
                    model.Remove(k);
                    break;
                case 6:
                    source.Append($"m.slot({Key(k)}) += {v}\n");
                    model.Set(k, model.Values.GetValueOrDefault(k) + v);
                    break;
                case 7:
                    source.Append($"bump(ref m.slot({Key(k)}))\n");
                    model.Set(k, model.Values.GetValueOrDefault(k) + 100);
                    break;
                case 8:
                    source.Append($"m.slot({Key(k)}) = {v}\n");
                    model.Set(k, v);
                    break;
                default:
                    if (Next(4) == 0)
                    {
                        source.Append("m.clear()\n");
                        model.Clear();
                    }
                    else
                    {
                        source.Append($"m[{Key(k)}] = {v}\n");
                        model.Set(k, v);
                    }
                    break;
            }
            if ((step + 1) % OpsPerPhase != 0) continue;
            source.Append($"print(\"phase {step + 1}\")\nprint($\"length {{m.length}}\")\n");
            source.Append("for e in m { print($\"{e.key} {e.value}\") }\n");
            expected.Append($"phase {step + 1}\nlength {model.Order.Count}\n");
            foreach (var key in model.Order) expected.Append($"{KeyText(key)} {model.Values[key]}\n");
            for (long key = 0; key < KeySpace; key++)
            {
                source.Append($"print($\"get {KeyText(key)} {{m.tryGet(at: {Key(key)}) ?? -1}}\")\n");
                expected.Append($"get {KeyText(key)} {model.Values.GetValueOrDefault(key, -1)}\n");
            }
        }
        var outcome = Run($"l003-{seed}.byn", source.ToString());
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(expected.ToString(), outcome.Stdout.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// L003 over generated <c>Symbol</c> and composite keys (spec 008, L016): the same operations as
    /// <see cref="L003GeneratedSequenceMatchesModel"/> on eight keys of a <see cref="KeyGen"/> type, each written
    /// either as a literal or built separately in another function, so the model is keyed by value, not by spelling.
    /// </summary>
    [Trait("Law", "L003")]
    [Theory]
    [MemberData(nameof(KeyTypeSeeds))]
    public void L003GeneratedKeyTypeMatchesModel(int seed)
    {
        var state = 0xA0761D6478BD642FUL ^ ((ulong)seed * 0xD1B54A32D192ED03UL);
        long Next(int bound)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return (long)((state >> 11) % (ulong)bound);
        }
        var gen = seed % 4 == 0 ? KeyGen.SymbolKey() : KeyGen.Generate(Next);
        var keys = gen.Pool(KeySpace, Next);
        string Key(int i) => Next(2) == 0 ? gen.Literal(keys[i]) : $"make({keys[i]})";

        var model = new Model();
        var source = new StringBuilder(gen.Declarations()).Append("void bump(ref i64 n) {\n    n += 100\n}\n");
        source.Append($"var m = Map<{gen.TypeName}, i64>()\n");
        var expected = new StringBuilder();
        for (var step = 0; step < OpsPerSequence; step++)
        {
            var i = (int)Next(KeySpace);
            var v = (step + 1) * 1000 + Next(1000);
            switch (Next(10))
            {
                case < 4:
                    source.Append($"m[{Key(i)}] = {v}\n");
                    model.Set(i, v);
                    break;
                case < 6:
                    source.Append($"let _ = m.remove({Key(i)})\n");
                    model.Remove(i);
                    break;
                case 6:
                    source.Append($"m.slot({Key(i)}) += {v}\n");
                    model.Set(i, model.Values.GetValueOrDefault(i) + v);
                    break;
                case 7:
                    source.Append($"bump(ref m.slot({Key(i)}))\n");
                    model.Set(i, model.Values.GetValueOrDefault(i) + 100);
                    break;
                default:
                    if (Next(4) == 0)
                    {
                        source.Append("m.clear()\n");
                        model.Clear();
                    }
                    else
                    {
                        source.Append($"m[{Key(i)}] = {v}\n");
                        model.Set(i, v);
                    }
                    break;
            }
            if ((step + 1) % OpsPerPhase != 0) continue;
            source.Append($"print(\"phase {step + 1}\")\nprint($\"length {{m.length}}\")\n");
            source.Append("for e in m { print($\"{e.value}\") }\n");
            expected.Append($"phase {step + 1}\nlength {model.Order.Count}\n");
            foreach (var key in model.Order) expected.Append($"{model.Values[key]}\n");
            for (var key = 0; key < KeySpace; key++)
            {
                source.Append($"print($\"get {key} {{m.tryGet(at: {Key(key)}) ?? -1}}\")\n");
                expected.Append($"get {key} {model.Values.GetValueOrDefault(key, -1)}\n");
            }
        }
        var outcome = Run($"l003-key-{seed}.byn", source.ToString());
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(expected.ToString(), outcome.Stdout.ReplaceLineEndings("\n"));
    }

    [Trait("Law", "L003")]
    [Theory]
    [InlineData("m[3] = 30", "m[3]", true)]
    [InlineData("m[3] = 30\nm[3] = 31", "m[3]", true)]
    [InlineData("m[3] = 30\nm.slot(4) += 1", "m[4]", true)]
    [InlineData("m[3] = 30", "m[4]", false)]
    [InlineData("m[3] = 30\nlet _ = m.remove(3)", "m[3]", false)]
    [InlineData("m[3] = 30\nm.clear()", "m[3]", false)]
    [InlineData("m[3] = 30\nlet _ = m.remove(3)\nm[3] = 5", "m[3]", true)]
    [InlineData("let _ = m.tryGet(at: 3) ?? 0\nlet _ = m.contains(3)", "m[3]", false)]
    public void L003IndexPanicsExactlyForNonLiveKeys(string setup, string read, bool live)
    {
        var outcome = Run("l003-index.byn", $"var m = Map<i64, i64>()\n{setup}\nprint($\"got {{{read}}}\")\n");
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
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

    /// <summary>
    /// A generated map key type (L003, L016): a struct or tuple mixing <c>bool</c>, <c>i8</c>, <c>i64</c>,
    /// <c>rune</c>, <c>string</c>, <c>Symbol</c>, a plain enum and one level of nested structs and tuples
    /// (a struct nests only structs: a tuple-typed field does not parse), each composite holding a small part
    /// before a larger one so its layout has padding. A key value is a code whose
    /// mixed-radix digits pick each leaf's value; <see cref="Literal"/> spells it in place and
    /// <see cref="Declarations"/> declares <c>make(i64 code)</c>, which builds the same value at run time in
    /// another frame, with its <c>string</c> and <c>Symbol</c> parts interpolated rather than literal.
    /// </summary>
    internal sealed class KeyGen
    {
        private enum Leaf { Bool, I8, I64, Rune, Str, Sym, Tint }

        private sealed record Part(Leaf? Leaf, bool IsStruct, string Name, List<Part> Parts);

        private static readonly Leaf[] Small = [Leaf.Bool, Leaf.I8, Leaf.Rune, Leaf.Tint, Leaf.Sym];
        private static readonly Leaf[] Large = [Leaf.I64, Leaf.Str];
        private static readonly string[] Tints = ["red", "green", "blue"];

        private readonly Part _root;
        private readonly List<Part> _structs = [];
        private readonly List<(Leaf Leaf, long Stride)> _leaves = [];

        private KeyGen(Part root)
        {
            _root = root;
            Collect(root);
        }

        public static KeyGen SymbolKey() => new(new Part(Leaf.Sym, false, "", []));

        public static KeyGen Generate(Func<int, long> next) => new(Composite(next, next(2) == 0, 0, [0]));

        private static Part Composite(Func<int, long> next, bool isStruct, int depth, int[] count)
        {
            var parts = new List<Part>
            {
                LeafPart(Small[next(Small.Length)]),
                LeafPart(Large[next(Large.Length)]),
            };
            for (var i = next(3); i > 0; i--)
                parts.Insert((int)next(parts.Count + 1), depth == 0 && next(3) == 0
                    ? Composite(next, isStruct || next(2) == 0, depth + 1, count)
                    : LeafPart((Leaf)next(7)));
            return new Part(null, isStruct, $"K{count[0]++}", parts);
        }

        private static Part LeafPart(Leaf leaf) => new(leaf, false, "", []);

        private void Collect(Part p)
        {
            if (p.Leaf is { } leaf)
            {
                _leaves.Add((leaf, _leaves.Count == 0 ? 1 : _leaves[^1].Stride * Radix(_leaves[^1].Leaf)));
                return;
            }
            foreach (var part in p.Parts) Collect(part);
            if (p.IsStruct) _structs.Add(p);
        }

        private static int Radix(Leaf leaf) => leaf switch { Leaf.Bool => 2, Leaf.I8 or Leaf.Tint => 3, _ => 8 };

        /// <summary>The number of distinct key values.</summary>
        public long Count => _leaves[^1].Stride * Radix(_leaves[^1].Leaf);

        public string TypeName => TypeOf(_root);

        /// <summary><paramref name="n"/> distinct codes: a random one, then codes that differ from it in exactly one leaf, then random ones.</summary>
        public List<long> Pool(int n, Func<int, long> next)
        {
            long Random() => _leaves.Sum(l => next(Radix(l.Leaf)) * l.Stride);
            var pool = new List<long> { Random() };
            foreach (var (leaf, stride) in _leaves)
            {
                var digit = pool[0] / stride % Radix(leaf);
                var other = pool[0] + ((digit + 1) % Radix(leaf) - digit) * stride;
                if (pool.Count < n && !pool.Contains(other)) pool.Add(other);
            }
            while (pool.Count < Math.Min(n, Count))
                if (Random() is var code && !pool.Contains(code)) pool.Add(code);
            return pool;
        }

        /// <summary>The enum, the struct declarations and <c>make</c>.</summary>
        public string Declarations()
        {
            var sb = new StringBuilder("enum Tint {\n    red\n    green\n    blue\n}\n\n");
            sb.Append("Tint tint(i64 c) {\n    if c < 1 {\n        return .red\n    }\n    if c < 2 {\n        return .green\n    }\n    return .blue\n}\n\n");
            foreach (var s in _structs)
            {
                sb.Append($"struct {s.Name} {{\n");
                for (var i = 0; i < s.Parts.Count; i++) sb.Append($"    {TypeOf(s.Parts[i])} f{i}\n");
                sb.Append("}\n\n");
            }
            var leaf = 0;
            var uses = _leaves.Any(l => l.Leaf is Leaf.Str or Leaf.Sym) ? " uses alloc" : "";
            return sb.Append($"{TypeName} make(i64 code){uses} {{\n    return {Build(_root, null, ref leaf)}\n}}\n\n").ToString();
        }

        /// <summary>The key with <paramref name="code"/>, spelled in place with literals.</summary>
        public string Literal(long code)
        {
            var leaf = 0;
            return Build(_root, code, ref leaf);
        }

        private static string TypeOf(Part p) => p.Leaf switch
        {
            Leaf.Bool => "bool",
            Leaf.I8 => "i8",
            Leaf.I64 => "i64",
            Leaf.Rune => "rune",
            Leaf.Str => "string",
            Leaf.Sym => "Symbol",
            Leaf.Tint => "Tint",
            _ => p.IsStruct ? p.Name : "(" + string.Join(", ", p.Parts.Select(TypeOf)) + ")",
        };

        /// <summary>A literal for <paramref name="code"/>, or with a null code the run-time expression over <c>code</c>.</summary>
        private string Build(Part p, long? code, ref int leaf)
        {
            if (p.Leaf is { } kind)
            {
                var (_, stride) = _leaves[leaf++];
                var radix = Radix(kind);
                if (code is { } c)
                {
                    var d = c / stride % radix;
                    return kind switch
                    {
                        Leaf.Bool => d == 1 ? "true" : "false",
                        Leaf.I8 => $"i8({d * 50 - 60})",
                        Leaf.I64 => $"i64({d * 1000003 + 7})",
                        Leaf.Rune => $"'{(char)('a' + d)}'",
                        Leaf.Str => $"\"s{d}\"",
                        Leaf.Sym => $"\"y{d}\"",
                        _ => $"Tint.{Tints[d]}",
                    };
                }
                var digit = $"code / {stride} % {radix}";
                return kind switch
                {
                    Leaf.Bool => $"{digit} == 1",
                    Leaf.I8 => $"i8(({digit}) * 50 - 60)",
                    Leaf.I64 => $"({digit}) * 1000003 + 7",
                    Leaf.Rune => $"rune(u32(97 + {digit}))",
                    Leaf.Str => $"$\"s{{{digit}}}\"",
                    Leaf.Sym => $"Symbol.intern($\"y{{{digit}}}\")",
                    _ => $"tint({digit})",
                };
            }
            var parts = new List<string>();
            foreach (var part in p.Parts) parts.Add(Build(part, code, ref leaf));
            return p.IsStruct
                ? $"{p.Name} {{ " + string.Join(", ", parts.Select((e, i) => $"f{i}: {e}")) + " }"
                : "(" + string.Join(", ", parts) + ")";
        }
    }
}
