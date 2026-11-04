using System.Collections.Concurrent;
using System.Reflection;
using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T11: <c>selfhost/emitter/cprint/builtin.byn</c> (CEmitter.cs 1946-2454: <c>Builtin</c>, <c>Unary</c>,
/// <c>Binary</c>, <c>Conversion</c>, <c>Index</c>, <c>Slice</c>) against the C# <c>FunctionEmitter</c> run in-process.
/// Every builtin call, unary, binary, conversion, index and slice of the unlowered bodies is numbered in
/// <c>BoundTree.Descendants</c> order. A case whose children are all plain locals is emitted by both sides, with every
/// local's C# name set to "" (the selfhost <c>emitExpr</c> is T10's stub, which returns ""), and the text, the helpers
/// and the type order are compared. A case that reaches another task's stub runs but is not compared (Q811).
/// </summary>
public sealed class SelfhostEmitterT11Tests
{
    // args: mode, the C file name of the C# `FunctionEmitter`, the case flags, then bayan-check's. Flag `1` emits and
    // compares a case, `2` emits it into a scratch context (not compared), anything else skips it.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CFunc = cprint.CFunc

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 3..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            if args[0] == "cases" { Io.stdout.write(t11Cases(ref c, at: args[1], for: args[2])) }
            return 0
        }

        /// `Lowerer.Lower`'s filter: the functions it lowers, in order.
        List<FuncId> t11Functions(ref Ctx c) uses alloc {
            var r = List<FuncId>()
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].body == -1 || c.functions[fn].kind == .testKeyword || semantics.functionIsGeneric(ref c, by: fn) { continue }
                    r.append(fn)
                }
            }
            return r
        }

        string t11Case(ref CCtx cc, for ref CFunc f, from ref Ctx c, through ltree.LTree t, at BExpr e) uses alloc {
            switch semantics.boundKind(e) {
                case .builtinCall: return cprint.emitBuiltin(ref cc, for: ref f, from: ref c, through: t, at: e)
                case .unary: return cprint.unary(ref cc, for: ref f, from: ref c, through: t, at: e)
                case .binary: return cprint.binary(ref cc, for: ref f, from: ref c, through: t, at: e)
                case .conversion: return cprint.conversion(ref cc, for: ref f, from: ref c, through: t, at: e)
                case .index: return cprint.index(ref cc, for: ref f, from: ref c, through: t, at: e)
                default: return cprint.slice(ref cc, for: ref f, from: ref c, through: t, at: e)
            }
        }

        string t11Cases(ref Ctx c, at string file, for string flags) uses alloc {
            var cc = CCtx {}
            var scratch = CCtx {}
            let t = ltree.LTree {}
            var sb = StrBuf()
            sb.append("== cases\n")
            var n: i64 = 0
            for fn in t11Functions(ref c) {
                var f = CFunc { fn: fn, file: file }
                for e in semantics.descendants(in c, for: c.functions[fn].body) {
                    let k = semantics.boundKind(e)
                    unless k == .builtinCall || k == .unary || k == .binary || k == .conversion || k == .index || k == .slice { continue }
                    let flag = n < flags.length ? flags[n..n + 1] : "0"
                    if flag == "1" { sb.append($"{n} {t11Case(ref cc, for: ref f, from: ref c, through: t, at: e)}\n") }
                    if flag == "2" { sb.append($"{n} {t11Case(ref scratch, for: ref f, from: ref c, through: t, at: e)}\n") }
                    n += 1
                }
            }
            sb.append($"count {n}\n")
            sb.append("== helpers\n")
            for h in cc.helperOrder { sb.append($"{h} {cc.helpers[h].proto} | {cc.helpers[h].body}\n") }
            sb.append("== types\n")
            for x in cc.typeOrder { sb.append($"{x}\n") }
            return $"{sb.finish()}"
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T11 main did not build:\n{error}");
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

    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    private static (int Exit, string Out, string Err) Run(string path, string file, string flags) =>
        SelfhostParserTests.Run(Build(), ["cases", file, flags.Length == 0 ? "-" : flags, path]);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric).ToList();

    private static string? Method(BoundNode n) => n switch
    {
        BoundBuiltinCall => "Builtin",
        BoundUnary => "Unary",
        BoundBinary => "Binary",
        BoundConversion => "Conversion",
        BoundIndex => "Index",
        BoundSlice => "Slice",
        _ => null,
    };

    /// <summary>Whether every child is a local the C# names "" (no constant, no by-ref parameter).</summary>
    private static bool PlainChildren(BoundNode n)
    {
        if (n is BoundBuiltinCall { Receiver: null }) return false;
        return BoundTree.Children(n).All(ch => ch is BoundLocal { Constant: null } l && l.Local is not ParameterSymbol { IsByRef: true });
    }

    private static readonly HashSet<BuiltinMember> StubMembers =
    [
        BuiltinMember.ListAdd, BuiltinMember.StrBufAppend, BuiltinMember.StrBufAppendRepeated, BuiltinMember.StrBufInsert,
        BuiltinMember.SymbolText, BuiltinMember.EnumName, BuiltinMember.EnumField, BuiltinMember.MapIndex,
        BuiltinMember.MapIndexSet, BuiltinMember.MapSlot, BuiltinMember.MapTryGet, BuiltinMember.MapContains, BuiltinMember.MapRemove,
    ];

    private static readonly HashSet<BinaryOp> SuffixOps =
    [
        BinaryOp.Add, BinaryOp.Sub, BinaryOp.Mul, BinaryOp.Div, BinaryOp.Rem, BinaryOp.AddWrap, BinaryOp.SubWrap, BinaryOp.MulWrap,
        BinaryOp.AddSat, BinaryOp.SubSat, BinaryOp.MulSat, BinaryOp.Shl, BinaryOp.Shr,
    ];

    private static bool Ordering(BinaryOp op) => op is BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le or BinaryOp.Ge;

    private static bool BareCase(BoundExpr e) => e switch
    {
        BoundErrorVariant { Payload.Length: 0, Variant.Fields.Count: > 0 } => true,
        BoundConversion c => BareCase(c.Operand),
        _ => false,
    };

    /// <summary>Whether the case calls a sibling stub (<c>IntSuffix</c>, <c>Claimed</c>, <c>MapMethod</c>, <c>SymbolText</c>,
    /// <c>EnumNames</c>, <c>EnumField</c>, <c>Equal</c>, <c>Vtable</c>), so its text cannot match yet (Q811).</summary>
    private static bool ReachesStub(BoundNode n) => n switch
    {
        BoundBuiltinCall b => StubMembers.Contains(b.Member),
        BoundUnary u => u.Op == UnaryOp.Negate && u.Type.IsInteger,
        BoundBinary b => (b.Left.Type.IsInteger && SuffixOps.Contains(b.Op))
            || (b.Left.Type == BuiltinType.Symbol && Ordering(b.Op))
            || (b.Op is BinaryOp.Eq or BinaryOp.Ne && !BareCase(b.Left) && !BareCase(b.Right)),
        BoundConversion c => c.Kind == ConversionKind.ToDynRef
            || (c.Kind is ConversionKind.NumericCast or ConversionKind.CharToInteger && c.Type.IsInteger),
        _ => false,
    };

    private sealed record Oracle(string File, string Flags, int Count, Dictionary<int, string> Cases, List<int> Run, List<string> Helpers, List<string> Types);

    /// <summary>Numbers the cases as the generated main does; a case is compared when its children are plain locals, it
    /// reaches no stub and the C# does not throw; it is run only when it reaches a stub. <paramref name="force"/> marks one
    /// more case to run, for the failure path.</summary>
    private static Oracle CSharp(Compilation c, Func<BoundNode, bool>? force = null)
    {
        var p = Lowerer.Lower(c);
        var emitter = NewEmitter(p);
        var probe = NewEmitter(p);
        var lowered = p.Functions.ToDictionary(f => f.Symbol);
        var functions = Functions(c);
        var emitters = functions.ToDictionary(fn => fn, fn => new FunctionEmitter(emitter, lowered[fn]));
        var probes = functions.ToDictionary(fn => fn, fn => new FunctionEmitter(probe, lowered[fn]));
        var typeOrder = (List<string>)Field(emitter, "_typeOrder")!;
        var typeBase = typeOrder.Count;
        var flags = new List<char>();
        var cases = new Dictionary<int, string>();
        var run = new List<int>();
        string file = "-";
        foreach (var fn in functions)
        {
            file = (string)Field(emitters[fn], "_file")!;
            foreach (var n in BoundTree.Descendants(fn.Body!))
            {
                if (Method(n) is not { } method) continue;
                var index = flags.Count;
                flags.Add('0');
                if (force?.Invoke(n) == true)
                {
                    flags[index] = '2';
                    continue;
                }
                if (!PlainChildren(n)) continue;
                foreach (var fe in new[] { emitters[fn], probes[fn] })
                {
                    var names = (Dictionary<LocalSymbol, string>)Field(fe, "_names")!;
                    foreach (var ch in BoundTree.Children(n)) names[((BoundLocal)ch).Local] = "";
                }
                var m = typeof(FunctionEmitter).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
                try { m.Invoke(probes[fn], [n]); }
                catch (TargetInvocationException) { continue; }
                if (ReachesStub(n))
                {
                    flags[index] = '2';
                    run.Add(index);
                    continue;
                }
                flags[index] = '1';
                cases[index] = (string)m.Invoke(emitters[fn], [n])!;
            }
        }
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(emitter, "_helpers")!;
        var helperLines = ((List<string>)Field(emitter, "_helperOrder")!).Select(h => $"{h} {helpers[h].Proto} | {helpers[h].Body}").ToList();
        return new(file, new string(flags.ToArray()), flags.Count, cases, run, helperLines, typeOrder.Skip(typeBase).ToList());
    }

    /// <summary>The differences between the selfhost run and <paramref name="o"/>, one line each.</summary>
    private static List<string> Compare(Oracle o, string stdout)
    {
        var sections = SelfhostSemanticsHarness.Sections(stdout);
        var failures = new List<string>();
        var got = new Dictionary<int, string>();
        var count = -1;
        foreach (var line in sections.GetValueOrDefault("cases") ?? [])
        {
            if (line.StartsWith("count ", StringComparison.Ordinal)) { count = int.Parse(line[6..]); continue; }
            var space = line.IndexOf(' ');
            got[int.Parse(space < 0 ? line : line[..space])] = space < 0 ? "" : line[(space + 1)..];
        }
        if (count != o.Count) failures.Add($"count: expected {o.Count}, actual {count}");
        foreach (var (i, text) in o.Cases)
        {
            if (!got.TryGetValue(i, out var a)) failures.Add($"case {i}: expected {text}, actual <missing>");
            else if (a != text) failures.Add($"case {i}\n expected: {text}\n   actual: {a}");
        }
        foreach (var i in o.Run)
            if (!got.ContainsKey(i)) failures.Add($"case {i}: run-only case missing");
        foreach (var (section, lines) in new[] { ("helpers", o.Helpers), ("types", o.Types) })
        {
            var actual = sections.GetValueOrDefault(section) ?? [];
            for (var i = 0; i < Math.Max(actual.Count, lines.Count); i++)
            {
                var e = i < lines.Count ? lines[i] : "<end>";
                var a = i < actual.Count ? actual[i] : "<end>";
                if (e == a) continue;
                failures.Add($"{section} line {i + 1}\n expected: {e}\n   actual: {a}");
                break;
            }
        }
        return failures;
    }

    private static Oracle RunAndCompare(string path)
    {
        var o = CSharp(Compile(path));
        var (exit, stdout, stderr) = Run(path, o.File, o.Flags);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        var failures = Compare(o, stdout);
        Assert.True(failures.Count == 0, string.Join('\n', failures.Take(8)));
        return o;
    }

    // ── A fixture of plain locals: every case kind on the types the port switches on ────────────────────────────

    private const string Fixture = """
        enum Color { red, green }

        struct P {
            i64 x
        }

        error E {
            bad(i64 code)
        }

        interface Shape {
            i64 area()
        }

        struct Sq: Shape {
            i64 side
            i64 area() => side * side
        }

        void strings(string s, string t, i64 n, i64 m, char ch, bool b, StrBuf w, Span<u8> bs, Span<string> ss, Radix r) {
            let a = s.length
            let e = s.isEmpty
            let rc = s.runeCount
            let ascii = s.isAscii
            let by = s.bytes
            let st = bs.toString()
            let p = s.parse(i32, radix: r)
            let q = s.parse(u8, radix: r)
            let pf = s.parse(f64)
            let pf32 = s.parse(f32)
            let j = ss.joined(by: s)
            let lt = s < t
            let ge = s >= t
            let sl = s[n..m]
            let sl2 = s[n..=m]
            let sl3 = s[..m]
            let cv = ch.value
            let ts = n.toString(radix: r)
            let wl = w.length
            let ws = w.asString()
        }

        void lists(List<i64> xs, List<string> ys, Span<i64> sp, i64 i, i64 k, bool b, Arena ar, i64[4] arr) {
            var zs = xs.copy()
            let s1 = xs.asSpan()
            let n = xs.length
            let e = xs.isEmpty
            let g = xs.tryGet(i)
            let v = xs[i]
            let w = sp[i]
            let u = arr[i]
            let s2 = sp[i..k]
            let s3 = arr[i..k]
            let s4 = xs[i..k]
            let al = arr.length
            let asp = arr.asSpan()
            let sc = sp.copy()
            let sn = sp.length
            zs.sort()
            var ws = ys.copy()
            ws.sort()
            zs.clear()
            let last = zs.removeLast()
            zs.remove(from: i, until: k)
            zs.remove(from: i, through: k)
            zs.remove(at: i)
            ar.reset()
        }

        void numbers(i64 a, i64 b, f64 x, f64 y, f32 z, f32 zz, u8 c, bool p, bool q, i32 h, u32 g, char ch) {
            let r1 = a & b
            let r2 = a | b
            let r3 = a ^ b
            let r4 = x % y
            let r5 = z % zz
            let r6 = x + y
            let r7 = x < y
            let r8 = p && q
            let r9 = p || q
            let r10 = !p
            let r11 = ~a
            let r12 = -x
            let r13 = +a
            let r14 = f64(a)
            let r15 = i64(h)
            let r16 = f32(x)
            let r17 = char(g)
            let r18 = char(h)
        }

        void options(i64 a, i64? o, List<i64> xs, Owned<i64> ow, Color col) {
            let p: i64? = a
            let s: Span<i64> = xs
            let v = ow.value
            let ord = col.ordinal
        }

        void main() { }
        """;

    [Fact]
    public void Fixture_MatchesCSharp() => WithFixture(Fixture, path =>
    {
        var o = RunAndCompare(path);
        Assert.True(o.Cases.Count >= 20, $"only {o.Cases.Count} cases compared");
    });

    // ── The failure path: a builtin the emitter has no C for (CEmitter.cs:2292) ──────────────────────────────────

    [Fact]
    public void Take_FailsLikeCSharp() => WithFixture("void main() {\n    var xs = List<i64>()\n    let ys = xs.take()\n}\n", path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var take = Functions(c).SelectMany(fn => BoundTree.Descendants(fn.Body!)).OfType<BoundBuiltinCall>().Single(b => b.Member == BuiltinMember.ListTake);
        var p = Lowerer.Lower(c);
        var main = p.Functions.Single(f => f.Symbol.Name == "main");
        var fe = new FunctionEmitter(NewEmitter(p), main);
        var e = Assert.Throws<TargetInvocationException>(() =>
            typeof(FunctionEmitter).GetMethod("Builtin", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fe, [take]));
        var o = CSharp(c, n => n == take);
        var (exit, _, err) = Run(path, o.File, o.Flags);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.InnerException!.Message}", err, StringComparison.OrdinalIgnoreCase);
    });

    // ── The run/ corpus ──────────────────────────────────────────────────────────────────────────────────────────

    // The selfhost binder builds a `requires` function's body with one more local than the C# (Q766).
    private static readonly HashSet<string> Skipped = ["positional-requires.byn"];

    [Fact]
    public void Corpus_MatchesCSharp()
    {
        var files = Directory.GetFiles(Path.Combine(Golden.RepoRoot, "tests", "cases", "run"), "*.byn").Order(StringComparer.Ordinal).ToList();
        var failures = new ConcurrentBag<string>();
        var compared = 0;
        var cases = 0;
        Build();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, corpus => WithFixture(File.ReadAllText(corpus), path =>
        {
            if (Skipped.Contains(Path.GetFileName(corpus))) return;
            var c = Compile(path);
            if (c.Diagnostics.HasErrors) return;
            var o = CSharp(c);
            var (exit, stdout, stderr) = Run(path, o.File, o.Flags);
            if (exit != 0)
            {
                failures.Add($"{Path.GetFileName(corpus)}: exited {exit}: {stderr}");
                return;
            }
            foreach (var f in Compare(o, stdout)) failures.Add($"{Path.GetFileName(corpus)} {f}");
            Interlocked.Increment(ref compared);
            Interlocked.Add(ref cases, o.Cases.Count);
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(4))}");
        Assert.True(failures.IsEmpty, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(8))}");
        Assert.True(cases > 100, $"only {cases} cases compared");
    }
}
