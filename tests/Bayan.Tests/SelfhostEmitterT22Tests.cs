using System.Collections.Concurrent;
using System.Reflection;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T22: the selfhost lowering's <c>LoweredFunction.locals</c> (<c>selfhost/emitter/lower*.byn</c>, the
/// <c>note</c>/<c>newTemp</c> calls) against the C# <c>LoweredFunction.Locals</c> (Lowerer.cs:99-110 <c>Note</c>,
/// <c>NewTemp</c>), run in-process on the same compilation. Each function and test of <c>Lowerer.Lower</c> prints its
/// locals in order as <c>kind:name</c>, the kind <c>LocalKind</c>'s ordinal.
/// </summary>
public sealed class SelfhostEmitterT22Tests
{
    private const string Main = """
        import semantics
        import emitter.ltree

        i32 main(ReadOnlySpan<string> args) {
            let shapes = args[0] == "shapes"
            var rest = List<string>()
            for i in (shapes ? 1 : 0)..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            // The `shapes` fixture's `shut` state gets tag -1 by hand on both sides: a `states` block declares no tags.
            if shapes {
                for v in 0..c.unionVariants.length {
                    if c.unionVariants[v].name == "shut" { c.unionVariants[v].tag = -1 }
                }
            }
            var l = LCtx {}
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            if shapes {
                for i in 0..p.functions.length { t22Shapes(ref sb, from: ref c, through: in l.tree, for: in p.functions[i]) }
                Io.stdout.write(sb.finish())
                return 0
            }
            for i in 0..p.functions.length { t22Locals(ref sb, from: in c, for: in p.functions[i]) }
            for i in 0..p.testFunctions.length { t22Locals(ref sb, from: in c, for: in p.testFunctions[i]) }
            Io.stdout.write(sb.finish())
            return 0
        }

        void t22Locals(ref StrBuf sb, from in Ctx c, for in LoweredFunction lf) uses alloc {
            sb.append($"{c.functions[lf.symbol].name}:")
            for x in lf.locals {
                sb.append($" {c.locals[x].kind.ordinal}:{c.locals[x].name}")
                if c.locals[x].kind != .temp { sb.append($"@{c.locals[x].span.start}") }
            }
            sb.append("\n")
        }

        /// The lowered body's error variants (their type) and union tag stores (the stored constant's two words).
        void t22Shapes(ref StrBuf sb, from ref Ctx c, through in ltree.LTree t, for in LoweredFunction lf) uses alloc {
            let name = c.functions[lf.symbol].name
            for n in t22Lowered(in c, through: in t, for: lf.body) {
                if ltree.isLowered(n) { continue }
                let i = i64(n & 0xFFFFFFFF)
                if semantics.boundKind(n) == .errorVariant { sb.append($"{name} ev {semantics.typeDisplay(ref c, by: c.bErrorVariants[i].type)}\n") }
                if semantics.boundKind(n) == .assign && ltree.isLowered(c.bAssigns[i].target) && ltree.lKindOf(c.bAssigns[i].target) == .unionTag {
                    let k = semantics.exprConstant(in c, for: c.bAssigns[i].value) ?? return
                    sb.append($"{name} tag {k.integer.lo} {k.integer.hi}\n")
                }
            }
        }

        /// `BoundTree.Descendants`, also into an `LLoop`'s and an `LPushPanicHandler`'s body (T11's `t11Lowered`).
        List<semantics.BNode> t22Lowered(in Ctx c, through in ltree.LTree t, for semantics.BNode n) uses alloc {
            var result = List<semantics.BNode>()
            var stack = List<semantics.BNode>()
            stack.append(n)
            while stack.length > 0 {
                let m = stack.removeLast()
                result.append(m)
                if ltree.isLowered(m) {
                    let i = ltree.lIndexOf(m)
                    if ltree.lKindOf(m) == .loop { stack.append(t.loops[i].body) }
                    if ltree.lKindOf(m) == .pushPanicHandler { stack.append(t.pushPanicHandlers[i].body) }
                    continue
                }
                let ch = semantics.children(in c, for: m)
                var j = ch.length - 1
                while j >= 0 {
                    stack.append(ch[j])
                    j -= 1
                }
            }
            return result
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T22 main did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var dir = SelfhostEmitterHarness.EmitterDir;
        var sources = Directory.GetFiles(dir, "*.byn")
            .Where(p => Path.GetFileName(p) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(dir, "main.byn"), Main));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static T WithFixture<T>(string source, Func<string, T> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t22-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            return act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    /// <summary>The C# <c>LoweredFunction.Locals</c> of every function, then every test, as the generated main prints them.</summary>
    private static List<string> CSharp(Compilation c)
    {
        var p = Lowerer.Lower(c);
        return p.Functions.Concat(p.Tests.Select(t => t.Function))
            .Select(lf => lf.Symbol.Name + ":" + string.Concat(lf.Locals.Select(Local))).ToList();
    }

    /// <summary>A local as <c>kind:name</c>, and for a non-temp <c>@</c> its declaration's start (0 with no location).</summary>
    private static string Local(LocalSymbol l) =>
        $" {(int)l.Kind}:{l.Name}" + (l.Kind == LocalKind.Temp ? "" : $"@{l.Location?.Span.Start ?? 0}");

    /// <summary>Compares one input; null when every function matches, else the first differing function and the count of differing ones.</summary>
    private static (string First, int Count)? Compare(string path)
    {
        var expected = CSharp(Compile(path));
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), [path]);
        if (exit != 0) return ($"exited {exit}: {stderr.Split('\n').FirstOrDefault(l => l.Contains("panic", StringComparison.Ordinal)) ?? stderr}", expected.Count);
        var actual = stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        string? first = null;
        var count = 0;
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            var e = i < expected.Count ? expected[i] : "<end>";
            var a = i < actual.Count ? actual[i] : "<end>";
            if (e == a) continue;
            count++;
            first ??= $"function {i}\n expected: {e}\n   actual: {a}";
        }
        return first is null ? null : (first, count);
    }

    private static readonly HashSet<string> Skipped = ["positional-requires.byn"];

    [Fact]
    public void Corpus_LocalsMatchCSharp()
    {
        var files = Directory.GetFiles(Path.Combine(Golden.RepoRoot, "tests", "cases", "run"), "*.byn").Order(StringComparer.Ordinal).ToList();
        var failures = new ConcurrentBag<string>();
        var compared = 0;
        var differing = 0;
        Build();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, corpus => WithFixture(File.ReadAllText(corpus), path =>
        {
            if (Skipped.Contains(Path.GetFileName(corpus))) return 0;
            if (Compile(path).Diagnostics.HasErrors) return 0;
            Interlocked.Increment(ref compared);
            if (Compare(path) is not { } d) return 0;
            Interlocked.Add(ref differing, d.Count);
            failures.Add($"{Path.GetFileName(corpus)} {d.First}");
            return 0;
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared");
        Assert.True(failures.IsEmpty, $"{differing} functions differ in {failures.Count} inputs:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(12))}");
    }

    // `safe`'s `catch (.gone)` builds an error variant (Lowerer.cs:1789); `close`'s `become .shut` stores a negative tag
    // (Lowerer.cs:221), set by hand on both sides since a `states` block declares none.
    private const string Shapes = """
        union error E {
            bad(i64 code)
            gone
        }

        i64 risky(i64 n) fails E {
            fail .gone if n < 0
            return n
        }

        i64 safe(i64 n) fails E {
            return risky(n) catch (.gone) 7
        }

        struct Door {
            i64 id
            states {
                shut
                open(i64 width)
            }
            void close(ref this is .open) becomes .shut {
                become .shut
            }
            void widen(ref this is .open, i64 w) becomes .open {
                become .open(w)
            }
        }

        void main() { }
        """;

    /// <summary><c>BoundTree.Descendants</c>, also into an <c>LLoop</c>'s and an <c>LPushPanicHandler</c>'s body (T11's).</summary>
    private static IEnumerable<BoundNode> LoweredDescendants(BoundNode node)
    {
        var stack = new Stack<BoundNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            IEnumerable<BoundNode> children = n switch
            {
                LLoop l => [l.Body],
                LPushPanicHandler h => [h.Body],
                _ => BoundTree.Children(n),
            };
            foreach (var c in children.Reverse()) stack.Push(c);
        }
    }

    [Fact]
    public void Shapes_MatchCSharp() => WithFixture(Shapes, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var shut = c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null)
            .SelectMany(fn => BoundTree.Descendants(fn.Body!)).OfType<BoundBecome>().Single(b => b.State.Name == "shut").State;
        typeof(UnionVariantSymbol).GetField("<Tag>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shut, -1);
        var lines = new List<string>();
        foreach (var lf in Lowerer.Lower(c).Functions)
            foreach (var n in LoweredDescendants(lf.Body))
            {
                if (n is BoundErrorVariant v) lines.Add($"{lf.Symbol.Name} ev {v.Type.DisplayName}");
                if (n is BoundAssign { Target: LUnionTag, Value.Constant: { } k })
                    lines.Add($"{lf.Symbol.Name} tag {(ulong)(k.Integer & ulong.MaxValue)} {(ulong)(k.Integer >> 64)}");
            }
        Assert.Contains(lines, x => x.StartsWith("safe ev", StringComparison.Ordinal));
        Assert.Contains(lines, x => x.StartsWith("close tag", StringComparison.Ordinal) && x.EndsWith(" 18446744073709551615", StringComparison.Ordinal));
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), ["shapes", path]);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        SelfhostEmitterHarness.AssertSame("shapes", string.Join('\n', lines) + "\n", stdout);
        return 0;
    });

    // A tuple `for` in a field default's handler block binds with no function (Compilation.cs:1421 `new Binder(this, s.File,
    // null, s)`, Binder.cs:145 `_fn?.Locals.Add`); the lowered `main` notes it where the default is inlined.
    private const string FieldDefault = """
        union error E {
            gone
        }

        i64 risky(i64 n) fails E {
            fail .gone if n < 0
            return n
        }

        struct S {
            i64 x = risky(1) catch {
                for (a, b) in [(1, 2)] { let _ = a + b }
                panic("x")
            }
        }

        i32 main() {
            let s = S {}
            return i32(s.x)
        }
        """;

    [Fact]
    public void FieldDefault_LocalsMatchCSharp() => WithFixture(FieldDefault, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        Assert.Null(Compare(path));
        return 0;
    });

    // `for try (a, b) in …` binds the group as `_` through `BindPresent` (Binder.cs:1119, Parser.cs:2722).
    private const string ForTryTuple = """
        union error ReadError { broken }

        type Pair = (i64 x, i64 y)

        struct Pairs {
            i64 at
            i64 limit

            Pair? next(ref this) fails ReadError {
                if at >= limit { return null }
                at += 1
                return (x: at, y: at * 10)
            }
        }

        i64 total(through i64 limit) fails ReadError {
            var sum = 0
            for try (a, b) in (Pairs { at: 0, limit: limit }) {
                sum += a + b
            }
            return sum
        }

        print($"total {try total(through: 3)}")
        """;

    [Fact]
    public void ForTryTuple_LocalsMatchCSharp() => WithFixture(ForTryTuple, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        Assert.Contains(CSharp(c), x => x.StartsWith("total:", StringComparison.Ordinal) && x.Contains(" 5:_@", StringComparison.Ordinal));
        Assert.Null(Compare(path));
        return 0;
    });

    /// <summary>Every fixture of the T2-T5 and T11 test classes that compiles alone.</summary>
    [Fact]
    public void Fixtures_LocalsMatchCSharp()
    {
        var fixtures = new[] { typeof(SelfhostEmitterT2Tests), typeof(SelfhostEmitterT3Tests), typeof(SelfhostEmitterT4Tests), typeof(SelfhostEmitterT5Tests), typeof(SelfhostEmitterT11Tests) }
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(f => (f.IsLiteral || f.IsInitOnly) && f.FieldType == typeof(string) && f.Name != "Main")
                .Select(f => (Name: $"{t.Name}.{f.Name}", Source: (string)(f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null))!)))
            .ToList();
        var failures = new List<string>();
        var compared = new List<string>();
        foreach (var (name, source) in fixtures)
            WithFixture(source, path =>
            {
                if (Compile(path).Diagnostics.HasErrors) return 0;
                compared.Add(name);
                if (Compare(path) is { } d) failures.Add($"{name} ({d.Count}) {d.First}");
                return 0;
            });
        Assert.True(compared.Count >= 10, $"only {compared.Count} fixtures compared");
        Assert.Contains("SelfhostEmitterT5Tests.Fixture", compared);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }
}
