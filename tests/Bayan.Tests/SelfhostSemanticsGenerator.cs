using System.Text;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Deterministic program generator for the self-hosted checker (spec 010 R6, R7 input). The valid corpus is one
/// program per checker feature (<see cref="Features"/>), one assembled program per seed that combines features and
/// nests their uses up to <see cref="NestingCap"/> blocks deep, and the lambda and interface generators' programs
/// for the same seeds. The faulty corpus injects one semantic fault of each <see cref="FaultKind"/> into an
/// assembled program at an RNG-chosen statement, so every fault kind appears for every seed.
/// </summary>
public static class SelfhostSemanticsGenerator
{
    /// <summary>Maximum nesting depth of the <c>if</c> blocks in an assembled <c>main</c>.</summary>
    public const int NestingCap = 8;

    /// <summary>The fixed seeds every run of the corpora is built from (R6).</summary>
    public static readonly ulong[] Seeds =
    [
        0x9E3779B97F4A7C15, 0x0123456789ABCDEF, 0xFEDCBA9876543210, 0xDEADBEEFCAFEF00D,
        0x3141592653589793, 0x2718281828459045, 0x0BADF00D0BADF00D, 0x5EED5EED5EED5EED,
    ];

    /// <summary>The five injected faults of R6.</summary>
    public enum FaultKind { UnknownName, WrongType, UseAfterMove, MissingFails, UnassignedRead }

    /// <summary>One checker feature: top-level declarations and a <c>main</c> statement that uses them (or none).</summary>
    public sealed record Feature(string Name, string Decls, string? Use);

    /// <summary>A generated program; a faulty one names its fault and the one-based lines the fault occupies.</summary>
    public sealed record GeneratedProgram(string Name, string Source, FaultKind? Fault = null, int FirstLine = 0, int LastLine = 0);

    /// <summary>One template per checker feature; every use adds to <c>total</c>, an <c>i64</c> local of <c>main</c>.</summary>
    public static IReadOnlyList<Feature> Features { get; } =
    [
        new("structs", "struct Point {\n    i64 x\n    i64 y\n    i64 sum() => x + y\n}\n",
            "total += Point { x: total, y: 2 }.sum()"),
        new("enums", "enum Color {\n    red\n    green\n}\n\ni64 colorCode(Color c) => c switch {\n    .red => 1,\n    .green => 2,\n}\n",
            "total += colorCode(Color.green)"),
        new("unions", "union Shape {\n    circle(i64 r)\n    square(i64 side)\n    none\n}\n\ni64 area(Shape s) => s switch {\n    .circle(let r) => r * r * 3,\n    .square(let side) => side * side,\n    .none => 0,\n}\n",
            "total += area(.square(total))"),
        new("interfaces", "interface IArea {\n    i64 area()\n}\n\nstruct Box : IArea {\n    i64 w\n    i64 h\n    i64 area() => w * h\n}\n\ni64 biggerArea<T>(T a, to T b) where T : IArea => a.area() > b.area() ? a.area() : b.area()\n",
            "total += biggerArea(Box { w: 1, h: 2 }, to: Box { w: 3, h: total })"),
        new("lambdas", "i64 apply(i64 n, by i64 -> i64 f) => f(n)\n",
            "total += apply(total, by: x => x * 2)"),
        new("errors", "union error ParseError {\n    empty\n    badDigit(i64 at)\n}\n\ni64 parseDigit(i64 n) fails ParseError {\n    if n < 0 { fail .badDigit(n) }\n    return n\n}\n\ni64 viaTry(i64 n) fails ParseError {\n    let d = try parseDigit(n)\n    return d + 1\n}\n\ni64 safe(i64 n) => viaTry(n) catch 0\n",
            "total += safe(total)"),
        new("loops", "i64 sumAll(List<i64> xs) {\n    var sum: i64 = 0\n    for x in xs { sum += x }\n    var i = 0\n    while i < xs.length { i += 1 }\n    return sum + i\n}\n",
            "total += sumAll([1, 2, 3])"),
        new("ownership", "i64 owned(i64 n) {\n    var a = List<i64>()\n    a.append(n)\n    let b = move a\n    return b.length\n}\n",
            "total += owned(total)"),
        new("optionals", "i64 orZero(i64? n) => n ?? 0\n",
            "total += orZero(null)"),
        new("strings", "string greet(string name) uses alloc => $\"hello {name}\"\n",
            "print(greet($\"{total}\"))"),
        new("maps", "i64 stock(Map<string, i64> m) => m.length\n",
            "total += stock([\"a\": 1, \"b\": total])"),
        new("constants", "const limit: i64 = 100\n",
            "total += limit"),
        new("conversions", "i64 widen(i32 n) => i64(n)\n",
            "total += widen(i32(3))"),
        new("tuples", "(i64 lo, i64 hi) bounds(i64 n) => (lo: 0, hi: n)\n",
            "total += bounds(total).hi"),
        new("tests", "i64 twice(i64 n) => n * 2\n\ntest \"twice doubles\" {\n    assert twice(2) == 4\n}\n",
            "total += twice(total)"),
    ];

    /// <summary>The valid corpus: one program per feature, then per seed an assembled, a lambda and an interface program.</summary>
    public static IReadOnlyList<GeneratedProgram> GenerateValid(IReadOnlyList<ulong> seeds)
    {
        var programs = new List<GeneratedProgram>(Features.Count + 3 * seeds.Count);
        foreach (var feature in Features)
            programs.Add(new($"valid-{feature.Name}.byn", feature.Decls + "\n" + MainOf([feature.Use ?? ""])));
        for (var i = 0; i < seeds.Count; i++)
        {
            programs.Add(new($"valid-seed-{i:D3}.byn", Assemble(new XorShift(seeds[i]), forced: null).Source));
            programs.Add(new($"valid-lambda-{i:D3}.byn", LambdaGenerator.Generate(seeds[i])));
            programs.Add(new($"valid-interface-{i:D3}.byn", InterfaceGenerator.Generate(seeds[i])));
        }
        return programs;
    }

    /// <summary>The faulty corpus: per seed, one program per <see cref="FaultKind"/>, each with exactly one injected fault.</summary>
    public static IReadOnlyList<GeneratedProgram> GenerateFaulty(IReadOnlyList<ulong> seeds)
    {
        var programs = new List<GeneratedProgram>(seeds.Count * Enum.GetValues<FaultKind>().Length);
        for (var i = 0; i < seeds.Count; i++)
        {
            var rng = new XorShift(seeds[i]);
            foreach (var kind in Enum.GetValues<FaultKind>())
                programs.Add(Inject(Assemble(rng, forced: "errors"), kind, i, rng) with { Name = $"fault-{kind}-{i:D3}.byn" });
        }
        return programs;
    }

    /// <summary>
    /// About 1 MB of ordinary, well-typed Bayan code (R7): a declaration unit repeated with a numbered name, a
    /// <c>main</c> that calls the first, padded with spaces to exactly 1 MiB.
    /// </summary>
    public static SourceText OneMegabyte()
    {
        const int size = 1024 * 1024;
        const string main = "void main() {\n    print($\"{total0(List<Line0>())}\")\n}\n";
        var sb = new StringBuilder(size);
        for (var n = 0; ; n++)
        {
            var unit = $$"""
                /// A ledger line.
                struct Line{{n}} {
                    i64 price
                    string name
                }

                i64 total{{n}}(List<Line{{n}}> lines) {
                    var sum: i64 = 0
                    for line in lines { sum += line.price * 2 + 0xFF }
                    return sum + lines.length
                }


                """;
            if (Encoding.UTF8.GetByteCount(unit) + sb.Length + main.Length > size) break;
            sb.Append(unit);
        }
        sb.Append(main);
        sb.Append(' ', size - sb.Length);
        return new SourceText("big.byn", Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>A <c>main</c> whose body is <paramref name="body"/> between the <c>total</c> local and its print.</summary>
    private static string MainOf(IEnumerable<string> body)
    {
        var sb = new StringBuilder("void main() {\n    var total: i64 = 0\n");
        foreach (var line in body)
            if (line.Length > 0) sb.Append(line.StartsWith(' ') ? line : "    " + line).Append('\n');
        sb.Append("    print($\"{total}\")\n}\n");
        return sb.ToString();
    }

    /// <summary>
    /// A valid program: each feature with probability 1/2 (<paramref name="forced"/> always), then a <c>main</c>
    /// nesting <c>if total &gt;= 0</c> blocks up to <see cref="NestingCap"/> deep, one included feature's use per
    /// level and one innermost. Returns the source and the zero-based index of <c>main</c>'s first line.
    /// </summary>
    private static (string Source, int MainLine) Assemble(XorShift rng, string? forced)
    {
        var sb = new StringBuilder();
        var included = new List<Feature>();
        foreach (var feature in Features)
            if (rng.Below(2) == 0 || feature.Name == forced)
            {
                included.Add(feature);
                sb.Append(feature.Decls).Append('\n');
            }
        var uses = included.Where(f => f.Use is not null).Select(f => f.Use!).ToArray();
        var depth = 1 + rng.Below(NestingCap);
        var body = new List<string>();
        for (var d = 0; d < depth; d++)
        {
            body.Add(new string(' ', 4 * (d + 1)) + "if total >= 0 {");
            if (uses.Length > 0) body.Add(new string(' ', 4 * (d + 2)) + rng.Pick(uses));
        }
        body.Add(new string(' ', 4 * (depth + 1)) + "total += 1");
        for (var d = depth - 1; d >= 0; d--)
            body.Add(new string(' ', 4 * (d + 1)) + "}");
        var mainLine = sb.ToString().Count(c => c == '\n');
        sb.Append(MainOf(body));
        return (sb.ToString(), mainLine);
    }

    /// <summary>The statements that inject one fault of <paramref name="kind"/>; <paramref name="k"/> keeps names unique.</summary>
    public static string[] FaultLines(FaultKind kind, int k) => kind switch
    {
        FaultKind.UnknownName => [$"total = missingName{k} + 1"],
        FaultKind.WrongType => [$"total = \"text{k}\""],
        FaultKind.UseAfterMove => [$"var owned{k} = List<i64>()", $"owned{k}.append(total)", $"let taken{k} = move owned{k}", $"total = owned{k}.length + taken{k}.length"],
        FaultKind.MissingFails => ["total = parseDigit(total)"],
        FaultKind.UnassignedRead => [$"var unset{k}: i64", $"total = unset{k}"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Inserts <see cref="FaultLines"/> before an RNG-chosen line of <c>main</c>'s body (after the <c>total</c>
    /// local, at or before its print), indented like that line.
    /// </summary>
    private static GeneratedProgram Inject((string Source, int MainLine) assembled, FaultKind kind, int k, XorShift rng)
    {
        var lines = assembled.Source.Split('\n').ToList();
        var firstBody = assembled.MainLine + 2;
        var print = lines.FindLastIndex(l => l == "    print($\"{total}\")");
        var at = firstBody + rng.Below(print - firstBody + 1);
        var indent = new string(' ', lines[at].Length - lines[at].TrimStart().Length);
        var fault = FaultLines(kind, k);
        lines.InsertRange(at, fault.Select(l => indent + l));
        return new GeneratedProgram("", string.Join('\n', lines), kind, at + 1, at + fault.Length);
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
