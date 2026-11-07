using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T25: <c>selfhost/semantics/region.byn</c> against the C# <c>RegionAnalysis</c> (RegionAnalysis.cs 1-416). The C# class and
/// its records are private, so the oracle drives them by reflection: <c>Borrow.Union</c>/<c>Lent</c> on borrows both sides build
/// from one spec; <c>Run</c> on whole programs, compared on the state T25 code alone writes (<c>_depthOf</c>, <c>_declaredAt</c>,
/// <c>_restTails</c> and the rest parameters' <c>_locals</c>); <c>Compute</c> on probe expressions; and <c>Analyze</c> against
/// <c>analyzeRegions</c> on every region diag golden, compared on the diagnostics it reports (Q641).
/// </summary>
public sealed class SelfhostSemanticsT25Tests
{
    private const string Dir = "tmp/t25-cases";

    private static readonly Assembly Sem = typeof(BoundNode).Assembly;
    private static readonly System.Type Analysis = Sem.GetType("Bayan.Semantics.Binding.RegionAnalysis")!;
    private static readonly System.Type RegionT = Analysis.GetNestedType("Region", BindingFlags.NonPublic)!;
    private static readonly System.Type BorrowT = Analysis.GetNestedType("Borrow", BindingFlags.NonPublic)!;
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    // ── Borrow specs: `d:name,…|o,…|o=at,…` with `-` for no lends; owners are locals `l0`…`l3` ──────────

    /// <summary>(name, operation, a, b): operation `union a b` or `lent a owner at`.</summary>
    private static readonly (string Name, string Op, string A, string B)[] Pure =
    [
        ("empty-left", "union", "||-", "1:x,0:y|1,2|-"),
        ("empty-right", "union", "1:x|0|1=4", "||-"),
        ("both-empty", "union", "||-", "||-"),
        ("overlap", "union", "1:x,2:z|0,1|-", "2:z,0:y|1,3|-"),
        ("merge-min", "union", "1:x|1|1=5", "0:y|2|1=3,2=9"),
        ("merge-left-null", "union", "1:x|1|-", "0:y|2|2=9"),
        ("merge-right-null", "union", "1:x|1|1=5,3=2", "0:y|2|-"),
        ("merge-keep-smaller", "union", "1:x|1|1=2", "0:y|1|1=8"),
        ("lent-fresh", "lent", "1:x|0|-", "2 7"),
        ("lent-again", "lent", "1:x|2|2=3", "2 7"),
        ("lent-later", "lent", "|2|2=9", "2 4"),
        ("lent-empty", "lent", "||-", "0 0"),
    ];

    private static LocalSymbol[] Owners { get; } = [.. Enumerable.Range(0, 4).Select(i => new LocalSymbol($"l{i}", null, PrimitiveType.I64, LocalKind.Let))];

    private static object CsBorrow(string spec)
    {
        var parts = spec.Split('|');
        var regions = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => Activator.CreateInstance(RegionT, int.Parse(r.Split(':')[0]), r.Split(':')[1], int.MinValue)!).ToList();
        var typedRegions = typeof(ImmutableHashSet).GetMethods().First(m => m.Name == "CreateRange" && m.GetParameters().Length == 1)
            .MakeGenericMethod(RegionT).Invoke(null, [CastTo(RegionT, regions)])!;
        var owners = ImmutableHashSet.CreateRange(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(o => Owners[int.Parse(o)]));
        ImmutableDictionary<LocalSymbol, int>? lends = parts[2] == "-" ? null
            : ImmutableDictionary.CreateRange(parts[2].Split(',').Select(l => KeyValuePair.Create(Owners[int.Parse(l.Split('=')[0])], int.Parse(l.Split('=')[1]))));
        return Activator.CreateInstance(BorrowT, typedRegions, owners, lends)!;
    }

    private static object CastTo(System.Type t, IEnumerable xs)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!;
        foreach (var x in xs) list.Add(x);
        return list;
    }

    /// <summary>A borrow as one line: regions, owners and lends each sorted, so set order does not matter.</summary>
    private static string Show(object borrow, Func<LocalSymbol, string> name)
    {
        var regions = ((IEnumerable)BorrowT.GetProperty("Regions")!.GetValue(borrow)!).Cast<object>()
            .Select(r => $"{RegionT.GetProperty("Depth")!.GetValue(r)}:{RegionT.GetProperty("Name")!.GetValue(r)}").Order(StringComparer.Ordinal);
        var owners = ((IEnumerable<LocalSymbol>)BorrowT.GetProperty("Owners")!.GetValue(borrow)!).Select(name).Order(StringComparer.Ordinal);
        var lends = (ImmutableDictionary<LocalSymbol, int>?)BorrowT.GetProperty("Lends")!.GetValue(borrow);
        var lendText = lends is null ? "-" : string.Join(",", lends.Select(kv => $"{name(kv.Key)}={kv.Value}").Order(StringComparer.Ordinal));
        return $"[{string.Join(",", regions)}] [{string.Join(",", owners)}] {lendText}";
    }

    private static List<string> ExpectedPure()
    {
        var lines = new List<string>();
        foreach (var (name, op, a, b) in Pure)
        {
            var left = CsBorrow(a);
            var result = op == "union"
                ? BorrowT.GetMethod("Union")!.Invoke(left, [CsBorrow(b)])!
                : BorrowT.GetMethod("Lent")!.Invoke(left, [Owners[int.Parse(b.Split(' ')[0])], int.Parse(b.Split(' ')[1])])!;
            lines.Add($"{name} {Show(result, l => l.Name)}");
        }
        return lines;
    }

    private static string BayanBorrow(string spec)
    {
        var parts = spec.Split('|');
        var sb = new StringBuilder("tcBorrow([");
        sb.Append(string.Join(", ", parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => $"Region {{ depth: {r.Split(':')[0]}, name: \"{r.Split(':')[1]}\" }}")));
        sb.Append("], by: [").Append(parts[1].Replace(",", ", ")).Append("], from: ");
        if (parts[2] == "-") sb.Append("[], hasLends: false)");
        else sb.Append('[').Append(string.Join(", ", parts[2].Split(',').Select(l => l.Replace("=", ": ")))).Append("], hasLends: true)");
        return sb.ToString();
    }

    private static string PureMain()
    {
        var sb = new StringBuilder();
        sb.AppendLine("void tcPure() {");
        sb.AppendLine("    print(\"== pure\")");
        foreach (var (name, op, a, b) in Pure)
        {
            sb.AppendLine("    {");
            sb.AppendLine($"        let a = {BayanBorrow(a)}");
            if (op == "union")
            {
                sb.AppendLine($"        let b = {BayanBorrow(b)}");
                sb.AppendLine($"        print($\"{name} {{tcShowIds(in unionBorrow(in a, by: in b))}}\")");
            }
            else
            {
                sb.AppendLine($"        print($\"{name} {{tcShowIds(in lent(in a, for: {b.Split(' ')[0]}, at: {b.Split(' ')[1]}))}}\")");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }


    // ── runRegions: whole programs, the state T25 alone writes ─────────────────────

    /// <summary>
    /// Programs for <c>Run</c>. Each function has at most one capturing lambda; environments are compared on depth only.
    /// </summary>
    private static readonly Dictionary<string, string> Programs = new()
    {
        // Run: a value, a `ref`, an `Arena` and a `rest` parameter. Stmt: every statement kind but `switch` and `context`
        // (in `r-b`), Iterate in `while`/`do`/`for`, Declare in errdefer/panic defer.
        ["r-a"] = """
            union error E {
                bad
            }

            union Shape {
                circle(i64 radius)
                square(i64 side)
            }

            i64 work(i64 n, to ref i64 total, by Arena arena, rest ReadOnlySpan<i64> more) fails E {
                var a = 1
                assert n > 0
                a = n + 2
                if n > 3 {
                    let c = 4
                    a = c
                } else {
                    let d = 5
                    a = d
                }
                while a < 10 {
                    let w = a
                    a += w
                }
                do {
                    let dw = 1
                    a -= dw
                } while a > 100
                for i in 0..3 {
                    let inner = i
                }
                defer {
                    let df = 1
                }
                defer failing (e) {
                    let ef = 2
                }
                defer panicking (p) {
                    let pf = 3
                }
                if n == 7 { fail .bad }
                return a
            }

            i64 area(Shape s) {
                switch s {
                    case .circle(let r) when r > 1:
                        let rr = r * r
                        return rr
                    case .square(let side):
                        return side
                    default:
                        return 0
                }
            }

            i64 pick(Shape s, by i64? maybe) {
                let v = s switch {
                    .circle(let r) => r,
                    .square(let q) => q,
                }
                if (maybe is { } m) {
                    return m + v
                }
                if s is .circle(let rad) { return rad }
                return v
            }

            i64 risky(i64 n) fails E {
                if n > 1 { fail .bad }
                return n
            }

            void main() {
                let arena = Arena()
                provide allocator: arena {
                    let inside = 1
                }
                let got = risky(2) catch switch {
                    E err => -1,
                }
                let k = 3
                for i in 0..2 {
                    let f: i64 -> i64 = x => x + k
                }
            }

            i64 loopy(i64 n) {
                var total = 0
                while total < n {
                    let step = 1
                    let g: i64 -> i64 = x => x + step
                    total += step
                }
                return total
            }

            i64 walk(List<i64> xs) {
                var sum = 0
                for x in xs {
                    let y = x
                    sum += y
                }
                return sum
            }
            """,
        // Stmt's switch (pattern, guard, default), Compute's switch expression, `is`, catch binding, context, lambda in `for`.
        ["r-b"] = """
            union error E {
                bad
            }

            union Shape {
                circle(i64 radius)
                square(i64 side)
            }

            i64 area(Shape s) {
                switch s {
                    case .circle(let r) when r > 1:
                        let rr = r * r
                        return rr
                    case .square(let side):
                        return side
                    default:
                        return 0
                }
            }

            i64 pick(Shape s, by i64? maybe) {
                let v = s switch {
                    .circle(let r) => r,
                    .square(let q) => q,
                }
                if (maybe is { } m) {
                    return m + v
                }
                if s is .circle(let rad) { return rad }
                return v
            }

            i64 risky(i64 n) fails E {
                if n > 1 { fail .bad }
                return n
            }

            void main() {
                let arena = Arena()
                provide allocator: arena {
                    let inside = 1
                }
                let got = risky(2) catch switch {
                    E err => -1,
                }
                let k = 3
                for i in 0..2 {
                    let f: i64 -> i64 = x => x + k
                }
            }
            """,
    };

    private static Compilation Compile(string name, string source)
    {
        Directory.CreateDirectory(Dir);
        return Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), source + "\n")], packageRoot: Dir);
    }

    private static List<string> Functions(Compilation c) =>
        [.. c.EntryModule.Functions.Where(kv => kv.Value.Count == 1 && kv.Value[0].Body is not null)
            .Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    private static object NewAnalysis(Compilation c, FunctionSymbol fn) =>
        Analysis.GetConstructors(Any).Single(k => !k.IsStatic).Invoke([c, fn, false]);

    private static T Field<T>(object analysis, string name) => (T)Analysis.GetField(name, Any)!.GetValue(analysis)!;

    private static List<string> ExpectedRun(string name)
    {
        var c = Compile(name, Programs[name]);
        var lines = new List<string>();
        foreach (var f in Functions(c))
        {
            var fn = c.EntryModule.Functions[f][0];
            var a = NewAnalysis(c, fn);
            Analysis.GetMethod("Run", Any)!.Invoke(a, []);
            var declaredAt = Field<Dictionary<LocalSymbol, int>>(a, "_declaredAt");
            foreach (var (l, d) in Field<Dictionary<LocalSymbol, int>>(a, "_depthOf")) lines.Add($"{f} depth {l.Name} {d} {declaredAt[l]}");
            foreach (DictionaryEntry e in (IDictionary)Analysis.GetField("_restTails", Any)!.GetValue(a)!)
                lines.Add($"{f} rest {RegionT.GetProperty("Depth")!.GetValue(e.Key)}:{RegionT.GetProperty("Name")!.GetValue(e.Key)} {e.Value}");
            // Only a rest parameter's entry is T25's alone: every other entry comes from T37's `MayBorrow` or `Store`.
            foreach (DictionaryEntry e in (IDictionary)Analysis.GetField("_locals", Any)!.GetValue(a)!)
                if (e.Key is ParameterSymbol { Syntax.Rest: true }) lines.Add($"{f} local {((LocalSymbol)e.Key).Name} {Show(e.Value!, l => l.Name)}");
            foreach (var r in (IEnumerable)Analysis.GetField("_environments", Any)!.GetValue(a)!) lines.Add($"{f} env {RegionT.GetProperty("Depth")!.GetValue(r)}");
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    [Theory]
    [InlineData("r-a")]
    [InlineData("r-b")]
    public void RunMatches(string name)
    {
        var sections = SelfhostSemanticsHarness.RunSections(Main.Value, name);
        var actual = sections[name].Select(l => l.Contains(" local ") ? CanonLocal(l) : l).ToList();
        actual.Sort(StringComparer.Ordinal);
        sections[name] = actual;
        SelfhostSemanticsHarness.AssertSection(sections, name, ExpectedRun(name));
    }

    /// <summary>`f local name [..] [..] lends` with the lists sorted.</summary>
    private static string CanonLocal(string line)
    {
        var at = line.IndexOf(" local ") + 7;
        var head = line[..at];
        var rest = line[at..];
        return head + Canon(rest);
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static string RunFunctions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("string tcSource(string name) {");
        foreach (var (k, v) in Programs) sb.AppendLine($"    if name == \"{k}\" {{ return \"{Escape(v + "\n")}\" }}");
        foreach (var k in DiagCases) sb.AppendLine($"    if name == \"{k}\" {{ return \"{Escape(DiagSource(k) + "\n")}\" }}");
        sb.AppendLine($"    if name == \"compute\" {{ return \"{Escape(ComputeProgram + "\n")}\" }}");
        sb.AppendLine("    return \"\"");
        sb.AppendLine("}");
        sb.AppendLine("List<string> tcFunctions(string name) {");
        sb.AppendLine("    var names = List<string>()");
        foreach (var (k, v) in Programs)
            foreach (var f in Functions(Compile(k, v))) sb.AppendLine($"    if name == \"{k}\" {{ names.append(\"{f}\") }}");
        sb.AppendLine("    return names");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── computeBorrow: expressions whose result needs no T37 function ─────────────

    private const string ComputeProgram = """
        void main() {
            let a = 1
            let s = $"n {a}"
            let xs: List<i64> = [1, 2]
            let n = a
            let lit = 5
            let low = s.lowercased()
            let cp = s.copy()
            let t = string(a)
            let m: Map<i64, i64> = [1: 2]
            let has = m.contains(1)
        }
        """;

    /// <summary>(name, statement of `main` whose initializer is computed, `a` given a borrow, a context pushed).</summary>
    private static readonly (string Name, int Stmt, bool SetLocal, bool PushContext)[] ComputeCases =
    [
        ("interp-ctx", 1, false, true), ("interp-none", 1, false, false), ("list-ctx", 2, false, true), ("list-none", 2, false, false),
        ("local-set", 3, true, false), ("local-unset", 3, false, false), ("literal", 4, true, true),
        // builtinBorrow: the fresh-text arm, `copy()` with no argument falling to it, and a `None` arm.
        ("lower-ctx", 5, false, true), ("lower-none", 5, false, false), ("copy-ctx", 6, false, true),
        ("to-string-ctx", 7, false, true), ("contains-ctx", 9, false, true),
    ];

    private static List<string> ExpectedCompute()
    {
        var c = Compile("compute", ComputeProgram);
        var fn = c.EntryModule.Functions["main"][0];
        var stmts = ((BoundBlock)fn.Body!).Statements;
        var a = ((BoundLocalDecl)stmts[0]).Local;
        var lines = new List<string>();
        foreach (var (name, stmt, setLocal, push) in ComputeCases)
        {
            var analysis = NewAnalysis(c, fn);
            if (push)
                Analysis.GetField("_contexts", Any)!.FieldType.GetMethod("Push")!.Invoke(Analysis.GetField("_contexts", Any)!.GetValue(analysis), [CsBorrowOf("2:ctx", [a], null)]);
            if (setLocal)
                ((IDictionary)Analysis.GetField("_locals", Any)!.GetValue(analysis)!)[a] = CsBorrowOf("1:a-data", [a], ImmutableDictionary<LocalSymbol, int>.Empty.Add(a, 3));
            var result = Analysis.GetMethod("Compute", Any)!.Invoke(analysis, [((BoundLocalDecl)stmts[stmt]).Initializer])!;
            lines.Add($"{name} {Show(result, l => l.Name)}");
        }
        return lines;
    }

    private static object CsBorrowOf(string region, LocalSymbol[] owners, ImmutableDictionary<LocalSymbol, int>? lends)
    {
        var r = Activator.CreateInstance(RegionT, int.Parse(region.Split(':')[0]), region.Split(':')[1], int.MinValue)!;
        var regions = typeof(ImmutableHashSet).GetMethods().First(m => m.Name == "CreateRange" && m.GetParameters().Length == 1)
            .MakeGenericMethod(RegionT).Invoke(null, [CastTo(RegionT, new[] { r })])!;
        return Activator.CreateInstance(BorrowT, regions, ImmutableHashSet.CreateRange(owners), lends)!;
    }

    private static string ComputeFunction()
    {
        var sb = new StringBuilder();
        sb.AppendLine("void tcCompute() {");
        sb.AppendLine("    var c = tcLoad(\"compute\")");
        sb.AppendLine("    print(\"== compute\")");
        sb.AppendLine("    let f = tcNth(in c, by: \"main\", at: 0)");
        sb.AppendLine("    let block = boundIndex(c.functions[f].body)");
        sb.AppendLine("    let a = c.bLocalDecls[boundIndex(c.bBlocks[block].statements[0])].local");
        foreach (var (name, stmt, setLocal, push) in ComputeCases)
        {
            sb.AppendLine("    {");
            sb.AppendLine("        var r = Regions { fn: f, source: c.functions[f].file }");
            if (push) sb.AppendLine("        r.contexts.append(Borrow { regions: [Region { depth: 2, name: \"ctx\" }], owners: [a] })");
            if (setLocal)
            {
                sb.AppendLine("        var lends: Map<LocalId, i64> = []");
                sb.AppendLine("        lends[a] = 3");
                sb.AppendLine("        r.locals[a] = Borrow { regions: [Region { depth: 1, name: \"a-data\" }], owners: [a], lends: move lends }");
            }
            sb.AppendLine($"        let init = c.bLocalDecls[boundIndex(c.bBlocks[block].statements[{stmt}])].initializer");
            sb.AppendLine("        let b = computeBorrow(ref r, through: ref c, for: init)");
            sb.AppendLine($"        print($\"{name} {{tcShowNames(ref c, by: in b)}}\")");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    [Fact]
    public void ComputeMatches()
    {
        var sections = SelfhostSemanticsHarness.RunSections(Main.Value, "compute");
        sections["compute"] = [.. sections["compute"].Select(Canon)];
        SelfhostSemanticsHarness.AssertSection(sections, "compute", ExpectedCompute());
    }

    // ── analyzeRegions on the region diag goldens: every case whose .diag has a code RegionAnalysis reports ──────

    private static readonly string[] RegionCodes = ["BYN3002", "BYN3003", "BYN3004", "BYN3005", "BYN3006", "BYN3214"];

    private static string DiagDir => Path.Combine(Golden.RepoRoot, "tests", "cases", "diag");

    private static readonly string[] DiagCases =
        [.. Directory.GetFiles(DiagDir, "*.diag").Where(p => RegionCodes.Any(File.ReadAllText(p).Contains))
            .Select(p => Path.GetFileNameWithoutExtension(p)!).Order(StringComparer.Ordinal)];

    private static string DiagSource(string name) => File.ReadAllText(Path.Combine(DiagDir, name + ".byn")).TrimEnd('\n');

    public static TheoryData<string> DiagCaseNames() => [.. DiagCases];

    /// <summary>Compilation.cs:1405-1413 runs `RegionAnalysis.Analyze` on every bodied template; only what it reports is kept.</summary>
    private static bool Analyzed(FunctionSymbol fn) => fn.Body is not null && !fn.IsGeneric && !fn.IsInstance;

    private static string Diag(Bayan.Syntax.Diagnostics.Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static List<string> ExpectedDiag(string name)
    {
        var c = Compile(name, DiagSource(name));
        var mark = c.Diagnostics.Items.Count;
        foreach (var fn in c.EntryModule.AllFunctions.Where(Analyzed))
            Analysis.GetMethod("Analyze", Any)!.Invoke(null, [c, fn]);
        var lines = new List<string>();
        for (var i = mark; i < c.Diagnostics.Items.Count; i++) lines.AddRange(Diag(c.Diagnostics.Items[i]).Split('\n'));
        return lines;
    }

    private static string DiagFunction() => """
        void tcDiag(string name) {
            var c = tcLoad(name)
            print($"== {name}")
            let mark = c.diagnostics.length
            let fns = copyIds(in c.modules[c.entryModule].allFunctions)
            for fn in fns {
                if c.functions[fn].body != -1 && !functionIsGeneric(ref c, by: fn) && !functionIsInstance(ref c, by: fn) { analyzeRegions(ref c, for: fn) }
            }
            for i in mark..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> DiagSections = new(RunDiag);

    private static Dictionary<string, List<string>> RunDiag() => SelfhostSemanticsHarness.RunSections(Main.Value, ["diag", .. DiagCases]);

    [Theory]
    [MemberData(nameof(DiagCaseNames))]
    public void DiagMatches(string name) => SelfhostSemanticsHarness.AssertSection(DiagSections.Value, name, ExpectedDiag(name));

    // ── The generated main ──────────────────────────────────────────────────────
    private static readonly Lazy<string> Main = new(MainSource);

    private static string MainSource() => """
        import lexer
        import parser

        Borrow tcBorrow(move List<Region> regions, by move List<LocalId> owners, from move Map<LocalId, i64> lends, bool hasLends) {
            if hasLends { return Borrow { regions: move regions, owners: move owners, lends: move lends } }
            return Borrow { regions: move regions, owners: move owners }
        }

        /// The borrow as one line; the C# test sorts the three lists, so this prints them in any order.
        string tcShowIds(in Borrow b) {
            var sb = StrBuf()
            sb.append("[")
            for k in 0..b.regions.length {
                if k > 0 { sb.append(",") }
                sb.append($"{b.regions[k].depth}:{b.regions[k].name}")
            }
            sb.append("] [")
            for k in 0..b.owners.length {
                if k > 0 { sb.append(",") }
                sb.append($"l{b.owners[k]}")
            }
            sb.append("] ")
            if (b.lends is { } l) {
                var first = true
                for (o, at) in l {
                    if !first { sb.append(",") }
                    first = false
                    sb.append($"l{o}={at}")
                }
            } else {
                sb.append("-")
            }
            return $"{sb.finish()}"
        }

        /// The `k`th function named `name` of the entry module, in declaration order.
        FuncId tcNth(in Ctx c, by string name, at i64 k) {
            var seen: i64 = 0
            for f in 0..c.functions.length {
                if c.functions[f].name == name && c.functions[f].module == c.entryModule {
                    if seen == k { return f }
                    seen += 1
                }
            }
            return -1
        }

        /// The borrow as one line, owners and lends by local name.
        string tcShowNames(ref Ctx c, by in Borrow b) {
            var sb = StrBuf()
            sb.append("[")
            for k in 0..b.regions.length {
                if k > 0 { sb.append(",") }
                sb.append($"{b.regions[k].depth}:{b.regions[k].name}")
            }
            sb.append("] [")
            for k in 0..b.owners.length {
                if k > 0 { sb.append(",") }
                sb.append(c.locals[b.owners[k]].name)
            }
            sb.append("] ")
            if (b.lends is { } l) {
                var first = true
                for (o, at) in l {
                    if !first { sb.append(",") }
                    first = false
                    sb.append($"{c.locals[o].name}={at}")
                }
            } else {
                sb.append("-")
            }
            return $"{sb.finish()}"
        }

        Ctx tcLoad(string name) {
            var bytes = List<u8>()
            for x in tcSource(name).bytes { bytes.append(x) }
            var sources = List<lexer.SourceText>()
            sources.append(lexer.SourceText.create("tmp/t25-cases/main.byn", from: move bytes))
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
                packageRoot: "tmp/t25-cases",
                displayRoot: "tmp/t25-cases",
            }
            c.entryModule = loadModule(ref c, for: "main", at: "tmp/t25-cases", from: move sources, by: -1)
            buildLexicon(ref c)
            declare(ref c)
            // Compilation.cs BindAll checks conformances first, then binds every body.
            checkConformances(ref c)
            let fns = copyIds(in c.modules[c.entryModule].allFunctions)
            for fn in fns {
                if !functionIsInstance(ref c, by: fn) { bindFunction(ref c, for: fn) }
            }
            summarizeRegions(ref c)
            return move c
        }

        void tcRun(string name) {
            var c = tcLoad(name)
            print($"== {name}")
            for fname in tcFunctions(name) {
                let f = tcNth(in c, by: fname, at: 0)
                var r = Regions { fn: f, source: c.functions[f].file }
                runRegions(ref r, through: ref c)
                for (l, d) in r.depthOf { print($"{fname} depth {c.locals[l].name} {d} {r.declaredAt[l]}") }
                for t in r.restTails { print($"{fname} rest {t.region.depth}:{t.region.name} {t.tail}") }
                // Only a rest parameter's entry, as on the C# side: every other entry comes from T37's `mayBorrow` or `storeBorrow`.
                let ps = copyIds(in c.functions[f].parameters)
                let syntax = t12Params(in c, for: f)
                for i in 0..ps.length {
                    let l = ps[i]
                    if (syntax[i].syntax is { } x) && x.isRest && r.locals.contains(l) {
                        print($"{fname} local {c.locals[l].name} {tcShowNames(ref c, by: in r.locals[l])}")
                    }
                }
                for env in r.environments { print($"{fname} env {env.depth}") }
            }
        }

        RUN

        PURE

        i32 main(ReadOnlySpan<string> args) {
            if args.length > 0 && args[0] == "diag" {
                for k in 1..args.length { tcDiag(args[k]) }
                return 0
            }
            for name in args {
                if name == "pure" { tcPure() } else if name == "compute" { tcCompute() } else { tcRun(name) }
            }
            return 0
        }
        """.Replace("PURE", PureMain()).Replace("RUN", RunFunctions() + ComputeFunction() + DiagFunction());

    /// <summary>The Bayan line with each bracketed list and the lends sorted, as <see cref="Show"/> sorts the C# one.</summary>
    private static string Canon(string line)
    {
        var name = line[..line.IndexOf(' ')];
        var rest = line[(name.Length + 1)..];
        var regions = rest[1..rest.IndexOf(']')];
        rest = rest[(rest.IndexOf(']') + 2)..];
        var owners = rest[1..rest.IndexOf(']')];
        var lends = rest[(rest.IndexOf(']') + 2)..];
        static string Sort(string s) => string.Join(",", s.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        return $"{name} [{Sort(regions)}] [{Sort(owners)}] {(lends == "-" ? "-" : Sort(lends))}";
    }

    [Fact]
    public void UnionAndLentMatch()
    {
        var sections = SelfhostSemanticsHarness.RunSections(Main.Value, "pure");
        sections["pure"] = [.. sections["pure"].Select(Canon)];
        SelfhostSemanticsHarness.AssertSection(sections, "pure", ExpectedPure());
    }
}
