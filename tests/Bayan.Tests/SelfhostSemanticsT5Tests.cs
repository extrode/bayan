using System.Globalization;
using System.Numerics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T5: the 128-bit <c>Big</c> in <c>selfhost/semantics/bigint.byn</c>, against a C# <c>Int128</c> and
/// <c>BigInteger</c> oracle. One generated <c>main</c> prints a line per case, and each test compares a
/// section of that output with the line the oracle built for it.
/// </summary>
public sealed class SelfhostSemanticsT5Tests
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string SemanticsDir => Path.Combine(SelfhostDir, "semantics");

    /// <summary>
    /// The generated entry module, the lines it must print, and where each section starts and how long it
    /// is: operations first, then literals, then float conversions.
    /// </summary>
    private sealed record Generated(
        string Source, List<string> Expected, int OpsStart, int OpsCount, int LitStart, int LitCount, int FloatStart, int FloatCount);

    private static readonly Lazy<Generated> Cases = new(Generate);

    /// <summary>The built program's output lines, or the compiler's complaint (Q522's build shape).</summary>
    private static readonly Lazy<(string[] Lines, string Error)> Executed = new(() =>
    {
        var sources = Directory.GetFiles(SemanticsDir, "*.byn")
            .Where(path => Path.GetFileName(path) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(SemanticsDir, "main.byn"), Cases.Value.Source));
        var (program, error) = Build(sources);
        if (program is null) return ([], error);
        var (exit, output, stderr) = SelfhostParserTests.Run(program);
        if (exit != 0 || stderr != "") return ([], $"exit {exit}: {stderr}");
        return (output.Split('\n', StringSplitOptions.RemoveEmptyEntries), "");
    });

    /// <summary>The whole <c>selfhost/semantics</c> module with a generated <c>main</c>, built once.</summary>
    private static (string? Path, string Error) Build(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    [Fact]
    public void OperationsMatchInt128()
    {
        var cases = Cases.Value;
        Assert.True(cases.Expected.Count >= 1500, $"only {cases.Expected.Count} printed lines");
        Compare("operations", cases.OpsStart, cases.OpsCount);
    }

    [Fact]
    public void LiteralsMatchBigInteger() => Compare("literals", Cases.Value.LitStart, Cases.Value.LitCount);

    [Fact]
    public void FloatConversionsMatchCSharp() => Compare("float conversions", Cases.Value.FloatStart, Cases.Value.FloatCount);

    /// <summary>One section of the output against the oracle's lines, naming the first difference.</summary>
    private static void Compare(string section, int start, int count)
    {
        var cases = Cases.Value;
        var (lines, error) = Executed.Value;
        Assert.True(error.Length == 0, error);
        Assert.True(lines.Length == cases.Expected.Count, $"{section}: printed {lines.Length} lines, expected {cases.Expected.Count}");
        for (var i = 0; i < count; i++)
        {
            var at = start + i;
            if (lines[at] != cases.Expected[at])
                Assert.Fail($"{section}: case {i} (output line {at + 1}):\n expected: {cases.Expected[at]}\n   actual: {lines[at]}");
        }
    }

    // ── The oracle ──────────────────────────────────────────────────────────────

    private static string B(bool value) => value ? "true" : "false";

    private static string T(Int128 value) => value.ToString(CultureInfo.InvariantCulture);

    private static Int128 RangeMin(int bits, bool signed) => signed ? -(Int128.One << (bits - 1)) : Int128.Zero;

    private static Int128 RangeMax(int bits, bool signed) => signed ? (Int128.One << (bits - 1)) - 1 : (Int128.One << bits) - 1;

    /// <summary><c>ConstantFolding.Wrap</c>: two's-complement truncation to <paramref name="bits"/>.</summary>
    private static Int128 Wrap(Int128 n, int bits, bool signed)
    {
        var mask = (Int128.One << bits) - 1;
        var value = n & mask;
        if (signed && value > RangeMax(bits, true)) value -= Int128.One << bits;
        return value;
    }

    /// <summary><c>ConstantFolding.Saturate</c>: clamp to the width's range.</summary>
    private static Int128 Saturate(Int128 n, int bits, bool signed) =>
        n < RangeMin(bits, signed) ? RangeMin(bits, signed) : n > RangeMax(bits, signed) ? RangeMax(bits, signed) : n;

    private static bool Fits(Int128 n, int bits, bool signed) => n >= RangeMin(bits, signed) && n <= RangeMax(bits, signed);

    /// <summary>C#'s <c>Int128</c> division: <c>MIN / -1</c> is the one case that throws, so it wraps.</summary>
    private static Int128 Div(Int128 a, Int128 b) =>
        a == Int128.MinValue && b == -1 ? Int128.MinValue : unchecked(a / b);

    /// <summary>C#'s <c>Int128</c> remainder, with the same <c>MIN % -1</c> exception.</summary>
    private static Int128 Rem(Int128 a, Int128 b) =>
        a == Int128.MinValue && b == -1 ? Int128.Zero : unchecked(a % b);

    /// <summary><c>BinderExpressions.ParseInteger</c>: strip <c>_</c>, then hex, binary or decimal.</summary>
    private static (bool Ok, bool Overflow, BigInteger Value) ParseLikeCSharp(string text)
    {
        var t = text.Replace("_", "");
        BigInteger value;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var digits = t[2..];
            if (digits.Length == 0 || !digits.All(Uri.IsHexDigit)) return (false, false, BigInteger.Zero);
            value = BigInteger.Parse("0" + digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
        else if (t.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            var digits = t[2..];
            if (digits.Length == 0 || digits.Any(c => c is not ('0' or '1'))) return (false, false, BigInteger.Zero);
            value = BigInteger.Zero;
            foreach (var c in digits) value = value * 2 + (c - '0');
        }
        else
        {
            if (t.Length == 0 || !t.All(char.IsAsciiDigit)) return (false, false, BigInteger.Zero);
            value = BigInteger.Parse(t, CultureInfo.InvariantCulture);
        }
        return (true, value > (BigInteger)Int128.MaxValue, value);
    }

    // ── The generated program ───────────────────────────────────────────────────

    private static Generated Generate()
    {
        var operands = new List<Int128>
        {
            0, 1, -1, 2, -2, 42,
            126, 127, 128, -127, -128, -129,
            255, 256, -256,
            32766, 32767, 32768, -32767, -32768, -32769,
            65535, 65536,
            2147483646, 2147483647, 2147483648, -2147483647, -2147483648, -2147483649,
            4294967295, 4294967296,
            long.MaxValue - 1, long.MaxValue, (Int128)long.MaxValue + 1,
            long.MinValue + 1, long.MinValue, (Int128)long.MinValue - 1,
            (Int128)ulong.MaxValue, (Int128)ulong.MaxValue + 1, -((Int128)ulong.MaxValue + 1),
            Int128.One << 63, (Int128.One << 100) + 1, -((Int128.One << 100) + 1),
            Int128.MaxValue, Int128.MaxValue - 1, Int128.MinValue, Int128.MinValue + 1,
        };

        // Fixed pairs first (the division and shift corners), then seeded random ones.
        var pairs = new List<(Int128 A, Int128 B)>();
        for (var i = 0; i < operands.Count; i++)
        {
            var b = operands[(i * 7 + 3) % operands.Count];
            pairs.Add((operands[i], b == 0 ? operands[1] : b));
        }
        var random = new Random(510);
        Int128 Next()
        {
            var high = unchecked((ulong)random.NextInt64());
            var low = unchecked((ulong)random.NextInt64());
            var v = ((Int128)high << 64) | (Int128)low;
            return v >> (int)(unchecked((ulong)random.NextInt64()) % 7); // a spread of magnitudes, still any sign
        }
        for (var i = 0; i < 24; i++)
        {
            var a = Next();
            var b = Next();
            pairs.Add((a, b == 0 ? operands[1] : b));
        }
        pairs.Add((Int128.MinValue, -1));
        pairs.Add((Int128.MinValue, 1));
        pairs.Add((Int128.MinValue, 2));
        pairs.Add((Int128.MaxValue, -1));
        pairs.Add((Int128.MaxValue, 2));
        pairs.Add((Int128.MaxValue, Int128.MaxValue));
        pairs.Add(((Int128)ulong.MaxValue, (Int128)ulong.MaxValue));
        pairs.Add(((Int128)ulong.MaxValue + 1, (Int128)ulong.MaxValue + 1));
        pairs.Add((-1, Int128.MinValue));
        pairs.Add((5, -7));
        pairs.Add((-5, 7));
        pairs.Add((7, -5));
        pairs.Add((-7, 5));

        var source = new List<string> { "i32 main() {", "    var sb = StrBuf()" };
        var expected = new List<string>();

        // A Bayan statement appending one line, and the line the oracle says it prints.
        void Op(string body, string line)
        {
            source.Add("    sb.append($\"" + body + "\\n\")");
            expected.Add(line);
        }

        var opsStart = expected.Count;
        for (var i = 0; i < operands.Count; i++)
        {
            var v = operands[i];
            source.Add($"    let o{i} = Big {{ lo: {Lo(v)}, hi: {Hi(v)} }}");
            Op($"o {i} {{bigText(o{i})}}", $"o {i} {T(v)}");
            Op($"neg {i} {{bigText(bigNeg(o{i}))}}", $"neg {i} {T(unchecked(-v))}");
            Op($"not {i} {{bigText(bigNot(o{i}))}}", $"not {i} {T(~v)}");
            Op($"fromu64 {i} {{bigText(bigFromU64(o{i}.lo))}}", $"fromu64 {i} {T((Int128)Lo(v))}");
            if (Fits(v, 64, true))
            {
                Op($"toi64 {i} {{bigToI64(o{i})}}", $"toi64 {i} {T(v)}");
                Op($"fromi64 {i} {{bigText(bigFromI64(bigToI64(o{i})))}}", $"fromi64 {i} {T(v)}");
            }
            foreach (var bits in new[] { 8, 16, 32, 64 })
            {
                foreach (var signed in new[] { false, true })
                {
                    var s = B(signed);
                    Op($"fits {i} {bits} {s} {{bigFits(o{i}, to: {bits}, signed: {s}) ? \"true\" : \"false\"}}",
                        $"fits {i} {bits} {s} {B(Fits(v, bits, signed))}");
                    Op($"wrap {i} {bits} {s} {{bigText(bigWrap(o{i}, to: {bits}, signed: {s}))}}",
                        $"wrap {i} {bits} {s} {T(Wrap(v, bits, signed))}");
                    Op($"sat {i} {bits} {s} {{bigText(bigSaturate(o{i}, to: {bits}, signed: {s}))}}",
                        $"sat {i} {bits} {s} {T(Saturate(v, bits, signed))}");
                    Op($"min {i} {bits} {s} {{bigText(bigMin({bits}, signed: {s}))}}",
                        $"min {i} {bits} {s} {T(RangeMin(bits, signed))}");
                    Op($"max {i} {bits} {s} {{bigText(bigMax({bits}, signed: {s}))}}",
                        $"max {i} {bits} {s} {T(RangeMax(bits, signed))}");
                }
            }
        }
        // Pairs can name values outside the boundary list; those get their own `oN` declaration.
        var extras = new Dictionary<Int128, int>();
        int IndexOf(Int128 value)
        {
            var at = operands.IndexOf(value);
            if (at >= 0) return at;
            if (!extras.TryGetValue(value, out at))
            {
                at = operands.Count + extras.Count;
                extras[value] = at;
                source.Add($"    let o{at} = Big {{ lo: {Lo(value)}, hi: {Hi(value)} }}");
            }
            return at;
        }

        foreach (var (a, b) in pairs)
        {
            var i = IndexOf(a);
            var j = IndexOf(b);
            Op($"p {i} {j} {{bigText(o{i})}} {{bigText(o{j})}}", $"p {i} {j} {T(a)} {T(b)}");
            Op($"add {i} {j} {{bigText(bigAdd(o{i}, by: o{j}))}}", $"add {i} {j} {T(unchecked(a + b))}");
            Op($"sub {i} {j} {{bigText(bigSub(o{i}, by: o{j}))}}", $"sub {i} {j} {T(unchecked(a - b))}");
            Op($"mul {i} {j} {{bigText(bigMul(o{i}, by: o{j}))}}", $"mul {i} {j} {T(unchecked(a * b))}");
            Op($"div {i} {j} {{bigText(bigDiv(o{i}, by: o{j}))}}", $"div {i} {j} {T(Div(a, b))}");
            Op($"rem {i} {j} {{bigText(bigRem(o{i}, by: o{j}))}}", $"rem {i} {j} {T(Rem(a, b))}");
            Op($"and {i} {j} {{bigText(bigAnd(o{i}, by: o{j}))}}", $"and {i} {j} {T(a & b)}");
            Op($"or {i} {j} {{bigText(bigOr(o{i}, by: o{j}))}}", $"or {i} {j} {T(a | b)}");
            Op($"xor {i} {j} {{bigText(bigXor(o{i}, by: o{j}))}}", $"xor {i} {j} {T(a ^ b)}");
            Op($"cmp {i} {j} {{bigCmp(o{i}, as: o{j})}}", $"cmp {i} {j} {(a < b ? -1 : a > b ? 1 : 0)}");
            Op($"eq {i} {j} {{bigEq(o{i}, as: o{j}) ? \"true\" : \"false\"}}", $"eq {i} {j} {B(a == b)}");
            foreach (var n in new[] { 0, 1, 63, 64, 127 })
            {
                Op($"shl {i} {j} {n} {{bigText(bigShl(o{i}, by: {n}))}}", $"shl {i} {j} {n} {T(unchecked(a << n))}");
                Op($"shr {i} {j} {n} {{bigText(bigShr(o{i}, by: {n}))}}", $"shr {i} {j} {n} {T(a >> n)}");
            }
        }
        var opsCount = expected.Count - opsStart;

        var litStart = expected.Count;
        var literals = new[]
        {
            "42", "0", "00", "1_000_000", "_1_2_", "0xFF", "0XfF", "0b1010", "0B1_1",
            "9223372036854775807", "170141183460469231731687303715884105727",
            "170141183460469231731687303715884105728",
            "0x7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "0x80000000000000000000000000000000",
            "9999999999999999999999999999999999999999",
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
            "", "0xZZ", "0b102", "0x",
        };
        for (var k = 0; k < literals.Length; k++)
        {
            var text = literals[k];
            var (ok, overflow, value) = ParseLikeCSharp(text);
            source.Add($"    let p{k} = bigParse(\"{text}\")");
            Op($"lit {k} {{p{k}.ok ? \"true\" : \"false\"}} {{p{k}.overflow ? \"true\" : \"false\"}} {{p{k}.ok || p{k}.overflow ? literalDecimalText(\"{text}\") : \"-\"}} {{p{k}.ok && !p{k}.overflow ? bigText(p{k}.value) : \"-\"}}",
                $"lit {k} {B(ok)} {B(overflow)} {(ok || overflow ? value.ToString(CultureInfo.InvariantCulture) : "-")} {(ok && !overflow ? T((Int128)value) : "-")}");
        }
        var litCount = expected.Count - litStart;

        var floatStart = expected.Count;
        var doubles = new[]
        {
            -3.7, 3.7, 0.5, -0.5, 1.0, -1.0, 123456789.0, 0.001,
            9223372036854775808.0, 1e30, -1e30, 18446744073709551616.0,
        };
        var floatText = new[]
        {
            "-3.7", "3.7", "0.5", "-0.5", "1.0", "-1.0", "123456789.0", "0.001",
            "9223372036854775808.0", "1000000000000000000000000000000.0", "-1000000000000000000000000000000.0",
            "18446744073709551616.0",
        };
        for (var k = 0; k < doubles.Length; k++)
        {
            var d = doubles[k];
            var integer = (Int128)d;
            source.Add($"    let f{k} = bigFromF64({floatText[k]})");
            source.Add($"    let e{k} = Big {{ lo: {Lo(integer)}, hi: {Hi(integer)} }}");
            Op($"fromf64 {k} {{bigText(f{k})}}", $"fromf64 {k} {T(integer)}");
            Op($"fromf64eq {k} {{bigEq(f{k}, as: e{k}) ? \"true\" : \"false\"}}", $"fromf64eq {k} true");
        }
        foreach (var v in new[] { (Int128)5, Int128.One << 63, (Int128)ulong.MaxValue, (Int128)ulong.MaxValue + 1, (Int128.One << 100) + 1, Int128.MinValue })
        {
            var k = expected.Count - floatStart;
            source.Add($"    let g{k} = bigFromF64(bigToF64(Big {{ lo: {Lo(v)}, hi: {Hi(v)} }}))");
            Op($"tof64 {k} {{bigText(g{k})}}", $"tof64 {k} {T((Int128)(double)v)}");
        }
        var floatCount = expected.Count - floatStart;

        source.Add("    Io.stdout.write(sb.finish())");
        source.Add("    return 0");
        source.Add("}");
        return new Generated(string.Join("\n", source) + "\n", expected, opsStart, opsCount, litStart, litCount, floatStart, floatCount);
    }

    /// <summary>The low 64 bits of <paramref name="value"/>, as a u64 literal.</summary>
    private static ulong Lo(Int128 value) => (ulong)(value & ulong.MaxValue);

    /// <summary>The high 64 bits of <paramref name="value"/>, as a u64 literal.</summary>
    private static ulong Hi(Int128 value) => (ulong)((value >> 64) & ulong.MaxValue);
}
