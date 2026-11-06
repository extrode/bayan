using System.Collections.Concurrent;
using System.Reflection;
using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T11: <c>selfhost/emitter/cprint/builtin.byn</c> (CEmitter.cs 1946-2454: <c>Builtin</c>, <c>Unary</c>,
/// <c>Binary</c>, <c>Conversion</c>, <c>Index</c>, <c>Slice</c>) against the C# <c>FunctionEmitter</c> run in-process.
/// Every builtin call, unary, binary, conversion, index and slice of the unlowered bodies is numbered in
/// <c>BoundTree.Descendants</c> order. Each case is emitted by both sides with the locals named as the C#
/// <c>FunctionEmitter</c> names them (the generated main's <c>t11Func</c>), and the full text, the helpers and the type
/// order are compared (Q876).
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
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            if args[0] == "cases" || args[0] == "errors" { Io.stdout.write(t11Cases(ref c, at: args[1], for: args[2], by: args[0] == "errors")) }
            return 0
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

        /// `BoundTree.Descendants`, also into an `LLoop`'s and an `LPushPanicHandler`'s body.
        List<semantics.BNode> t11Lowered(in Ctx c, through in ltree.LTree t, for semantics.BNode n) uses alloc {
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

        /// Whether `fn`'s body has a `for` over a text view or a map, the loops whose builtins only the lowerer builds; in
        /// `errors` mode, whether it has a `catch` instead, whose failure test only the lowerer builds.
        bool t11Loops(ref Ctx c, through in ltree.LTree t, for FuncId fn, by bool errors) uses alloc {
            for e in semantics.descendants(in c, for: c.functions[fn].body) {
                if errors {
                    if semantics.boundKind(e) == .catchExpr { return true }
                    continue
                }
                if semantics.boundKind(e) != .forStmt { continue }
                let k = semantics.kindOf(ltree.typeOf(in c, through: in t, for: c.bFors[i64(e & 0xFFFFFFFF)].iterable))
                if k == .textView || k == .map { return true }
            }
            return false
        }

        /// A node only the lowerer builds: the text-view and map `for` loops' builtin calls; in `errors` mode, a `catch`'s
        /// failure test, a comparison of an error code (`bareCase` over a lowered operand).
        bool t11LowererMember(in Ctx c, for semantics.BNode e, by bool errors) {
            if errors {
                if semantics.boundKind(e) != .binary { return false }
                let left = c.bBinaries[i64(e & 0xFFFFFFFF)].left
                return ltree.isLowered(left) && ltree.lKindOf(left) == .errorCode
            }
            if semantics.boundKind(e) != .builtinCall { return false }
            let m = c.bBuiltinCalls[i64(e & 0xFFFFFFFF)].member
            return m == .textViewMore || m == .textViewNext || m == .mapSlotCount || m == .mapSlotLive || m == .mapSlotEntry
        }

        /// The C# `FunctionEmitter` constructor's `_names` (CEmitter.cs:1179-1190): `p_` parameters, the captures read
        /// through the environment, then the lowered locals numbered in order. The environment's type is named on `scratch`,
        /// as the oracle compares only the types named after its emitters are built.
        CFunc t11Func(ref CCtx scratch, from ref Ctx c, for in LoweredFunction lf, at string file) uses alloc {
            let fn = lf.symbol
            var f = CFunc { fn: fn, file: file, lineStarts: cprint.lineStartsOf(in c, for: c.functions[fn].file) }
            for p in c.functions[fn].parameters { f.names[p] = $"p_{c.locals[p].name}" }
            if c.functions[fn].captures.length > 0 {
                var fields = List<TupleField>()
                for x in c.functions[fn].captures { fields.append(TupleField { name: null, type: c.locals[x].type }) }
                let env = cprint.cType(ref scratch, from: ref c, for: semantics.internTuple(ref c, for: in fields))
                for i in 0..c.functions[fn].captures.length { f.names[c.functions[fn].captures[i]] = $"(({env}*)env)->item{i + 1}" }
            }
            var n: i64 = 0
            for x in lf.locals {
                f.names[x] = c.locals[x].kind == .temp ? $"t{n}" : $"l{n}_{c.locals[x].name}"
                n += 1
            }
            return f
        }

        string t11Cases(ref Ctx c, at string file, for string flags, by bool errors) uses alloc {
            var cc = CCtx {}
            var scratch = CCtx {}
            // `Lowerer.Lower` first, as the oracle's emitters are built over its functions (its filter is `Functions`').
            var l = LCtx {}
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            sb.append("== cases\n")
            var n: i64 = 0
            for i in 0..p.functions.length {
                if errors { break }
                let fn = p.functions[i].symbol
                var f = t11Func(ref scratch, from: ref c, for: in p.functions[i], at: file)
                for e in semantics.descendants(in c, for: c.functions[fn].body) {
                    let k = semantics.boundKind(e)
                    if !(k == .builtinCall || k == .unary || k == .binary || k == .conversion || k == .index || k == .slice) { continue }
                    let flag = n < flags.length ? flags[n..n + 1] : "0"
                    if flag == "1" { sb.append($"{n} {t11Case(ref cc, for: ref f, from: ref c, through: l.tree, at: e)}\n") }
                    if flag == "2" { sb.append($"{n} {t11Case(ref scratch, for: ref f, from: ref c, through: l.tree, at: e)}\n") }
                    n += 1
                }
            }
            // Then the builtin calls only the lowerer builds (Lowerer.cs:668-693), in the lowered bodies of the functions
            // with such a loop.
            for i in 0..p.functions.length {
                let fn = p.functions[i].symbol
                unless t11Loops(ref c, through: in l.tree, for: fn, by: errors) { continue }
                var f = t11Func(ref scratch, from: ref c, for: in p.functions[i], at: file)
                for e in t11Lowered(in c, through: in l.tree, for: p.functions[i].body) {
                    unless t11LowererMember(in c, for: e, by: errors) { continue }
                    let flag = n < flags.length ? flags[n..n + 1] : "0"
                    if flag == "1" { sb.append($"{n} {t11Case(ref cc, for: ref f, from: ref c, through: l.tree, at: e)}\n") }
                    if flag == "2" { sb.append($"{n} {t11Case(ref scratch, for: ref f, from: ref c, through: l.tree, at: e)}\n") }
                    n += 1
                }
            }
            sb.append($"count {n}\n")
            sb.append("== helpers\n")
            for h in cc.helperOrder { sb.append($"{h} {cc.helpers[h].proto} | {cc.helpers[h].body}\n") }
            sb.append("== types\n")
            for x in cc.typeOrder { sb.append($"{x}\n") }
            sb.append("== locals\n")
            for i in 0..p.functions.length {
                sb.append($"{c.functions[p.functions[i].symbol].name}:")
                for j in 0..p.functions[i].locals.length {
                    let x = p.functions[i].locals[j]
                    sb.append(" ")
                    sb.append(c.locals[x].kind == .temp ? "t" : c.locals[x].name)
                }
                sb.append("\n")
            }
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

    private static (int Exit, string Out, string Err) Run(string path, string file, string flags, bool errors = false) =>
        SelfhostParserTests.Run(Build(), [errors ? "errors" : "cases", file, flags.Length == 0 ? "-" : flags, path]);

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

    /// <summary>The builtins only <c>Lowerer.cs</c> builds (the text-view and map <c>for</c> loops, Lowerer.cs:668-693).</summary>
    private static readonly HashSet<BuiltinMember> LowererMembers =
    [
        BuiltinMember.TextViewMore, BuiltinMember.TextViewNext, BuiltinMember.MapSlotCount, BuiltinMember.MapSlotLive, BuiltinMember.MapSlotEntry,
    ];

    /// <summary><c>BoundTree.Descendants</c>, also into an <c>LLoop</c>'s and an <c>LPushPanicHandler</c>'s body; the generated
    /// main's <c>t11Lowered</c>.</summary>
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

    private sealed record Oracle(string File, string Flags, int Count, Dictionary<int, string> Cases, List<int> Run, List<string> Helpers, List<string> Types,
        HashSet<BuiltinMember> Emitted, HashSet<BuiltinMember> Compared, List<string> Locals);

    /// <summary>Numbers the cases as the generated main does; a case is compared when the C# does not throw.
    /// <paramref name="force"/> marks one more case to run, for the failure path.</summary>
    private static Oracle CSharp(Compilation c, Func<BoundNode, bool>? force = null, bool errors = false)
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
        var emitted = new HashSet<BuiltinMember>();
        var compared = new HashSet<BuiltinMember>();
        string file = "-";
        void Case(FunctionSymbol fn, BoundNode n)
        {
            if (Method(n) is not { } method) return;
            var index = flags.Count;
            flags.Add('0');
            if (force?.Invoke(n) == true)
            {
                flags[index] = '2';
                return;
            }
            var m = typeof(FunctionEmitter).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
            try { m.Invoke(probes[fn], [n]); }
            catch (TargetInvocationException) { return; }
            if (n is BoundBuiltinCall emittedCall) emitted.Add(emittedCall.Member);
            flags[index] = '1';
            cases[index] = (string)m.Invoke(emitters[fn], [n])!;
            if (n is BoundBuiltinCall comparedCall) compared.Add(comparedCall.Member);
        }
        foreach (var fn in functions)
        {
            if (errors) break;
            file = (string)Field(emitters[fn], "_file")!;
            foreach (var n in BoundTree.Descendants(fn.Body!)) Case(fn, n);
        }
        // Then the builtin calls only the lowerer builds (Lowerer.cs:668-693), in the lowered bodies of the functions with
        // such a loop.
        // In `errors` mode, instead, the failure tests of the functions with a `catch` (a lowered error code compared).
        foreach (var lf in p.Functions.Where(lf => errors
                ? BoundTree.Descendants(lf.Symbol.Body!).OfType<BoundCatch>().Any()
                : BoundTree.Descendants(lf.Symbol.Body!).OfType<BoundFor>().Any(f => f.Iterable.Type is TextViewType or MapType)))
            foreach (var n in LoweredDescendants(lf.Body))
                if (errors ? n is BoundBinary { Left: LErrorCode } : n is BoundBuiltinCall { Member: var member } && LowererMembers.Contains(member))
                {
                    file = (string)Field(emitters[lf.Symbol], "_file")!;
                    Case(lf.Symbol, n);
                }
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(emitter, "_helpers")!;
        var helperLines = ((List<string>)Field(emitter, "_helperOrder")!).Select(h => $"{h} {helpers[h].Proto} | {helpers[h].Body}")
            .SelectMany(h => h.Split('\n')).Where(l => l.Length > 0).ToList();
        return new(file, new string(flags.ToArray()), flags.Count, cases, run, helperLines, typeOrder.Skip(typeBase).ToList(), emitted, compared,
            p.Functions.Select(lf => lf.Symbol.Name + ":" + string.Concat(lf.Locals.Select(l => " " + (l.Kind == LocalKind.Temp ? "t" : l.Name)))).ToList());
    }

    /// <summary>The differences between the selfhost run and <paramref name="o"/>, one line each.</summary>
    private static List<string> Compare(Oracle o, string stdout)
    {
        var sections = SelfhostSemanticsHarness.Sections(stdout);
        var failures = new List<string>();
        var locals = sections.GetValueOrDefault("locals") ?? [];
        if (locals.Count != o.Locals.Count) failures.Add($"locals: expected {o.Locals.Count} functions, actual {locals.Count}");
        for (var i = 0; i < Math.Min(locals.Count, o.Locals.Count); i++)
            if (locals[i] != o.Locals[i]) failures.Add($"locals of function {i}\n expected: {o.Locals[i]}\n   actual: {locals[i]}");
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

    private static Oracle RunAndCompare(string path, bool errors = false)
    {
        var o = CSharp(Compile(path), errors: errors);
        var (exit, stdout, stderr) = Run(path, o.File, o.Flags, errors);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        var failures = Compare(o, stdout);
        Assert.True(failures.Count == 0, string.Join('\n', failures.Take(8)));
        return o;
    }

    // ── A fixture of plain locals: every case kind on the types the port switches on ────────────────────────────

    private const string Fixture = """
        enum Color {
            red
            green
        }

        void strings() {
            var s = "ab"
            var t = "cd"
            var n = 0
            var m = 1
            var ch = 'x'
            var w = StrBuf()
            var ss = List<string>()
            let a = s.length
            let e = s.isEmpty
            let rc = s.runeCount
            let ascii = s.isAscii
            let by = s.bytes
            var bs = by.copy()
            var bsp = bs.span
            let st = string(bsp)
            let it = string(n)
            let p = i32.tryParse(s)
            let q = u8.tryParse(s)
            let pf = f64.tryParse(s)
            let pf32 = f32.tryParse(s)
            let j = ss.joined(by: s)
            let lt = s < t
            let ge = s >= t
            let sl = s[n..m]
            let sl2 = s[n..=m]
            let sl3 = s[..m]
            let cv = ch.value
            let wl = w.length
            let ws = w.finish()
            let sw = s.startsWith(t)
            let ew = s.endsWith(t)
            let co = s.contains(t)
            let tr = s.trimmed()
            let lo = s.lowercased()
            let up = s.uppercased()
        }

        void lists() {
            var xs = List<i64>()
            var ys = List<string>()
            var i = 0
            var k = 1
            var zs = xs.copy()
            let s1 = xs.span
            var sp = xs.span
            let n = xs.length
            let e = xs.isEmpty
            let g = xs.tryGet(i)
            let v = xs[i]
            let w = sp[i]
            let s2 = sp[i..k]
            let s4 = xs[i..k]
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
        }

        void numbers() {
            var a = 1
            var b = 2
            var x = 1.5
            var y = 2.5
            var z = f32(1.5)
            var zz = f32(2.5)
            var p = true
            var q = false
            var h = i32(3)
            var g = u32(4)
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
            let r17 = rune(g)
            let r18 = rune(h)
        }

        void options() {
            var a = 1
            var xs = List<i64>()
            var col = Color.red
            let p: i64? = a
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

    // More builtins, operators and conversions on plain locals: text views, ranges, maps, arrays, error cases.
    private const string Fixture2 = """
        union error E {
            bad(i64 code)
            gone
        }

        i64 risky(i64 n) fails E {
            fail .gone if n < 0
            return n
        }

        void strings2() uses console, alloc {
            var s = "a,b"
            var t = ","
            var n = 0
            var k = 2
            var ch = 'x'
            var eq = s.equals(t)
            let po = s.position(t)
            let po2 = s.position(t, from: n)
            let lp = s.lastPosition(t)
            let cn = s.count(t)
            let tr = s.trimmed()
            var rl = List<rune>()
            var rsp = rl.span
            let tb = s.trimmed(by: rsp)
            let rp = s.replaced(t, by: s)
            let rr = s.repeated(k)
            let pd = s.padded(to: k, by: ch)
            var ps = s.parts(by: t)
            var ps2 = s.parts(by: t, limit: k)
            var pr = s.parts(by: ch)
            var pr2 = s.parts(by: ch, limit: k)
            var rs = s.runes
            let rt = rs.take(k)
            let rk = rs.skip(k)
            let rl = rs.takeLast(k)
            var us = s.utf16
            let uc = us.copy()
            var ls = s.lines
            let sc = s.copy()
            var so = Io.stdout
            so.write(s)
        }

        void lists2() {
            var xs = List<i64>()
            var ys = List<i64>()
            var i = 0
            var j: i64 = 1
            var r = i..j
            var sp = ys.span
            xs.replace(at: r, by: sp)
            var ms = xs.mutableSpan()
            xs.remove(at: r)
            var w = StrBuf()
            w.clear()
            var ch = 'x'
            var k = 2
            var s = "ab"
            w.appendRepeated(ch, by: k)
            w.insert(s, at: i)
            var m = Map<i64, i64>()
            let ml = m.length
            m.clear()
            var arr = [1, 2, 3]
            let al = arr.length
            let asp = arr.span
            var amp = arr.mutableSpan()
            let av = arr[i]
            let asl = arr[i..i]
            var u = 3
            let xu = sp[u]
        }

        void numbers2() {
            var a = 1
            var b = 2
            var u = u8(1)
            var v = u8(2)
            var x = 1.5
            let s1 = a + b
            let s2 = x - x
            let s3 = x * x
            let s4 = x / x
            let s5 = x > x
            let s6 = x <= x
            let s7 = x >= x
            let s8 = x == x
            let s9 = u & v
            let s10 = i64(u)
            let s11 = u8(a)
            let s12 = i64(x)
            let st = string(u)
        }

        void errors2() {
            var e = E.gone
            let same = e == .gone
            let bare = e == .bad
            var n = 1
            let full = e == .bad(n)
            let left = E.bad == e
            let ne = e != .bad
            var p = true
            var q = false
            let ba = p & q
            let bo = p | q
            let bx = p ^ q
            let none: i64? = null
        }

        void main() { }
        """;

    [Fact]
    public void Fixture2_MatchesCSharp() => WithFixture(Fixture2, path =>
    {
        var o = RunAndCompare(path);
        Assert.True(o.Cases.Count >= 20, $"only {o.Cases.Count} cases compared");
    });

    // The member accessors the corpus reaches only through non-plain receivers, on plain locals: results, owned values,
    // copies, panics, process results, format specs, input, map methods, and the text-view and map loops.
    private const string Fixture3 = """
        union error E {
            bad(i64 code)
            gone
        }

        struct P {
            i64 x
        }

        i64 results(Result<i64, E> r) {
            let ok = r.isOk
            let v = r.value
            let e = r.err
            return 0
        }

        i64 owned(Owned<P> o) {
            let v = o.value
            return 0
        }

        void copies(Arena arena) uses alloc {
            var m = Map<i64, i64>()
            var w = StrBuf()
            var a = arena
            var mc = m.copy(to: a)
            var wc = w.copy(to: a)
            a.reset()
        }

        void onCrash(Panic p) uses console {
            let m = p.message
        }

        i32 process(ProcessResult r) {
            let o = r.stdout
            let e = r.stderr
            return r.exitCode
        }

        struct B {
            i64 n
            public void formatInto(ref StrBuf output, as FormatSpec spec) {
                let r = spec.radix
                let d = spec.debug
                let w = spec.width
                let f = spec.fill
                let al = spec.align
                let dg = spec.digits
            }
        }

        void input() uses console, alloc {
            var i = Io.stdin
            let all = i.readAll()
        }

        void loops() uses console, alloc {
            var s = "a,b"
            var t = ","
            var n = 0
            for r in s.runes { n += 1 }
            for u in s.utf16 { n += 1 }
            for ln in s.lines { n += 1 }
            for p in s.parts(by: t) { n += 1 }
            var m = Map<i64, i64>()
            for e in m { n += 1 }
        }

        void maps() uses alloc {
            var m = Map<i64, i64>()
            var k = 1
            var v = 2
            m[k] = v
            let x = m[k]
            let g = m.tryGet(k)
            let h = m.contains(k)
            let d = m.remove(k)
        }

        void radix(FormatSpec spec) uses alloc {
            var r = spec.radix
            var s = "ff"
            var n = 5
            let p = i32.tryParse(s, as: r)
            let t = string(n, as: r)
        }

        void main() { }
        """;

    [Fact]
    public void Fixture3_MatchesCSharp() => WithFixture(Fixture3, path =>
    {
        var o = RunAndCompare(path);
        Assert.True(o.Cases.Count >= 15, $"only {o.Cases.Count} cases compared");
        var want = LowererMembers.Concat([BuiltinMember.MapIndex, BuiltinMember.MapIndexSet, BuiltinMember.MapTryGet, BuiltinMember.MapContains, BuiltinMember.MapRemove]);
        var missing = want.Where(m => !o.Compared.Contains(m)).ToList();
        Assert.True(missing.Count == 0, $"not compared: {string.Join(", ", missing)}");
    });

    // A `catch` lowers to a test of the error code (Lowerer.cs:1753, 1771), so `bareCase` meets a lowered operand
    // (CEmitter.cs:2352).
    private const string CatchFixture = """
        union error E {
            bad(i64 code)
            gone
        }

        i64 risky(i64 n) fails E {
            fail .gone if n < 0
            return n
        }

        i64 safe(i64 n) {
            return risky(n) catch 0
        }

        void main() { }
        """;

    [Fact]
    public void CatchFailureTest_BareCaseOverLoweredOperand_MatchesCSharp() => WithFixture(CatchFixture, path =>
    {
        var o = RunAndCompare(path, errors: true);
        Assert.True(o.Cases.Count > 0, "no lowered error-code comparison was compared");
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
        Assert.Contains($"panic: {e.InnerException!.Message}", err, StringComparison.Ordinal);
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
        var emitted = new HashSet<BuiltinMember>();
        var comparedMembers = new HashSet<BuiltinMember>();
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
            var differences = Compare(o, stdout);
            foreach (var f in differences) failures.Add($"{Path.GetFileName(corpus)} {f}");
            lock (emitted)
            {
                emitted.UnionWith(o.Emitted);
                comparedMembers.UnionWith(o.Compared);
            }
            Interlocked.Increment(ref compared);
            Interlocked.Add(ref cases, o.Cases.Count);
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(4))}");
        Assert.True(failures.IsEmpty, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(8))}");
        Assert.True(cases > 100, $"only {cases} cases compared");
        // The fixtures' own tests compare their text; here they add to the two sets.
        foreach (var fixture in new[] { Fixture, Fixture2, Fixture3 })
            WithFixture(fixture, path =>
            {
                var o = CSharp(Compile(path));
                emitted.UnionWith(o.Emitted);
                comparedMembers.UnionWith(o.Compared);
            });
        var missing = emitted.Where(b => !comparedMembers.Contains(b) && !Uncompared.ContainsKey(b)).Order().ToList();
        Assert.True(missing.Count == 0, $"emitted but never compared: {string.Join(", ", missing)}");
        var stale = Uncompared.Keys.Where(comparedMembers.Contains).Order().ToList();
        Assert.True(stale.Count == 0, $"excluded but compared: {string.Join(", ", stale)}");
    }

    /// <summary>The builtins the C# emits over the corpus and fixtures whose text is not compared, with why (Q876).</summary>
    private static readonly Dictionary<BuiltinMember, string> Uncompared = new()
    {
        // Empty since T9 landed: the four builtins that call its `claimed` are compared.
    };
}
