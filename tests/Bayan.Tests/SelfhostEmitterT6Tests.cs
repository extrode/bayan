using System.Numerics;
using System.Reflection;
using System.Text;
using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 011 T6: <c>selfhost/emitter/cprint/program.byn</c> (CEmitter.cs 61-178, 288-396) and <c>cprint/data.byn</c>
/// (CEmitter.cs 1047-1147) against the C# as an in-process oracle. A generated <c>main.byn</c> prints what the ported
/// functions return, by mode, as <see cref="SelfhostEmitterT2Tests"/> does (Q783).
/// </summary>
public sealed class SelfhostEmitterT6Tests
{
    // args: mode, the source path, then a mode-specific flag ("" for none).
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CompilationOptions = semantics.CompilationOptions

        i32 main(ReadOnlySpan<string> args) {
            let mode = args[0]
            if mode == "corpus" {
                Io.stdout.write(t6Corpus(args))
                return 0
            }
            let flag = args[2]
            var c = semantics.fromPath(args[1], by: in CompilationOptions { includeTests: true })
            var l = LCtx {}
            var cc = CCtx { packageRoot: c.packageRoot, hashes: flag == "hashes", symbols: flag == "symbols", ownershipChecks: flag == "debug" }
            if flag == "noerrors" { c.errorVariants = List<semantics.ErrorVariantSymbol>() }
            if flag.startsWith("root:") { cc.packageRoot = flag[5..flag.length] }
            if mode == "data" { Io.stdout.write(t6Data(ref cc, from: ref c)) }
            if mode == "consts" { Io.stdout.write(t6Consts(ref cc, from: ref c)) }
            if mode == "strings" { Io.stdout.write(t6Strings()) }
            if mode == "symbols" { Io.stdout.write(t6Symbols(ref cc)) }
            if mode == "names" || mode == "main" || mode == "namefail" {
                let program = lower(ref c, to: ref l)
                for i in 0..program.testFunctions.length { cc.testIndex[program.testFunctions[i].symbol] = i }
                if flag == "nofile" || flag == "nomodule" || flag == "relpath" { t6Mutate(ref c, through: in program, by: flag) }
                if mode == "names" { Io.stdout.write(t6Names(ref cc, from: ref c, through: in program)) }
                if mode == "main" { Io.stdout.write($"== main\n{cprint.t6EmitMain(ref cc, from: ref c, through: in program)}") }
                if mode == "namefail" {
                    var empty = CCtx {}
                    Io.stdout.write(cprint.functionName(ref empty, from: ref c, for: program.testFunctions[0].symbol))
                }
            }
            if mode == "run" || mode == "emit" {
                let program = LoweredProgram { functions: List<LoweredFunction>(), main: -1, tests: List<semantics.TestId>(), testFunctions: List<LoweredFunction>() }
                if mode == "emit" { Io.stdout.write(cprint.emit(ref c, from: in l.tree, through: in program, by: flag == "debug")) }
                if mode == "run" {
                    t6Prepare(ref cc)
                    Io.stdout.write(cprint.t6Run(ref cc, from: ref c, through: in l.tree, for: in program))
                }
            }
            if mode == "lowered" {
                let program = lower(ref c, to: ref l)
                Io.stdout.write(cprint.emit(ref c, from: in l.tree, through: in program, by: flag == "debug"))
            }
            return 0
        }

        string t6Data(ref CCtx cc, from ref Ctx c) uses alloc {
            var sb = StrBuf()
            sb.append("== data\n")
            let m = c.entryModule
            for i in 0..c.modules[m].enums.length {
                let e = c.modules[m].enums[i]
                sb.append($"{cprint.enumNames(ref cc, from: ref c, for: e)}\n")
                sb.append($"{cprint.enumNames(ref cc, from: ref c, for: e)}\n")
                sb.append($"{cprint.enumValues(ref cc, from: ref c, for: e)}\n")
                sb.append($"{cprint.enumValues(ref cc, from: ref c, for: e)}\n")
                for j in 0..c.enums[e].fields.length {
                    let f = c.enums[e].fields[j]
                    sb.append($"{cprint.enumField(ref cc, from: ref c, for: f)}\n")
                    sb.append($"{cprint.enumField(ref cc, from: ref c, for: f)}\n")
                }
            }
            for i in 0..c.modules[m].allConstants.length {
                let k = c.modules[m].allConstants[i]
                if c.consts[k].isStaticArray {
                    sb.append($"{cprint.staticArray(ref cc, from: ref c, for: k)}\n")
                    sb.append($"{cprint.staticArray(ref cc, from: ref c, for: k)}\n")
                } else {
                    let v = c.consts[k].value ?? panic("a constant has no value")
                    sb.append($"{c.consts[k].name} {cprint.constant(ref cc, from: ref c, for: v, as: c.consts[k].type)}\n")
                }
            }
            for n in cc.staticOrder { sb.append($"static {n}\n{cc.statics[n]}") }
            return $"{sb.finish()}"
        }

        semantics.Constant t6Int(u64 lo, by u64 hi) => Constant { kind: .integer, integer: Big { lo: lo, hi: hi } }

        string t6Consts(ref CCtx cc, from ref Ctx c) uses alloc {
            var sb = StrBuf()
            sb.append("== consts\n")
            var types = List<semantics.Type>()
            types.append(semantics.prim(.f32))
            types.append(semantics.prim(.f64))
            types.append(semantics.prim(.i64))
            types.append(semantics.prim(.isize))
            types.append(semantics.prim(.u64))
            types.append(semantics.prim(.usize))
            types.append(semantics.prim(.u8))
            types.append(semantics.prim(.i32))
            types.append(semantics.enumTypeOf(c.modules[c.entryModule].enums[0]))
            var values = List<Constant>()
            values.append(t6Int(5, by: 0))
            values.append(t6Int(0xFFFFFFFFFFFFFFFB, by: 0xFFFFFFFFFFFFFFFF))
            values.append(t6Int(0x8000000000000000, by: 0xFFFFFFFFFFFFFFFF))
            values.append(t6Int(0xFFFFFFFFFFFFFFFF, by: 0))
            values.append(t6Int(0, by: 1))
            values.append(t6Int(0x8000000000000000, by: 0))
            values.append(t6Int(0, by: 0x100000000))
            values.append(t6Int(12345, by: 1))
            values.append(t6Int(0xFFFFFFFFFFFFCFC7, by: 0xFFFFFFFFFFFFFFFE))
            values.append(t6Int(0xFFFFFFFFFFFFFFFF, by: 0x7FFFFFFFFFFFFFFF))
            values.append(t6Int(1, by: 0x8000000000000000))
            values.append(t6Int(0x800, by: 1))
            values.append(t6Int(0x1800, by: 1))
            values.append(t6Int(0x801, by: 1))
            values.append(t6Int(0xFFFFFFFFFFFFE800, by: 0xFFFFFFFFFFFFFFFE))
            values.append(Constant { kind: .flag, flag: true })
            values.append(Constant { kind: .flag, flag: false })
            values.append(Constant { kind: .text, text: "a\"b\\c?\n" })
            values.append(Constant { kind: .rune, rune: 233 })
            let zero = 0.0
            for r in [0.0, 0.001, 0.5, 5.0, 1.0e300, 1.0e-7, 123456789012345678.0, 0.1, -2.5, 100.0, 1.5e16] { values.append(Constant { kind: .real, real: r }) }
            values.append(Constant { kind: .real, real: zero / zero })
            values.append(Constant { kind: .real, real: 1.0 / zero })
            values.append(Constant { kind: .real, real: -1.0 / zero })
            for v in values {
                for t in types { sb.append($"{cprint.constant(ref cc, from: ref c, for: v, as: t)}\n") }
            }
            return $"{sb.finish()}"
        }

        /// The float and string constants of every input in `args[1..]`, each compiled alone, as the port prints them.
        string t6Corpus(ReadOnlySpan<string> args) uses alloc, clock, fs {
            var sb = StrBuf()
            var cc = CCtx {}
            var seen = Map<string, bool>()
            for n in 1..args.length {
                var c = semantics.fromPath(args[n], by: in CompilationOptions { includeTests: true }, wholeDirectory: false)
                var found = List<Constant>()
                for (handle, k) in c.constants { found.append(k) }
                for k in found {
                    var line = ""
                    if k.kind == .real {
                        line = $"r {k.real} {cprint.constant(ref cc, from: ref c, for: k, as: semantics.prim(.f64))} {cprint.constant(ref cc, from: ref c, for: k, as: semantics.prim(.f32))}"
                    }
                    if k.kind == .text {
                        var bytes = StrBuf()
                        for b in k.text.bytes { bytes.append($"{b},") }
                        line = $"s {bytes.finish()} {cprint.constant(ref cc, from: ref c, for: k, as: semantics.prim(.i32))}"
                    }
                    if line.length > 0 && !seen.contains(line) {
                        seen[line] = true
                        sb.append($"{line}\n")
                    }
                }
            }
            return $"{sb.finish()}"
        }

        string t6Strings() uses alloc {
            var sb = StrBuf()
            sb.append("== strings\n")
            var xs = List<string>()
            for s in ["", "plain", "a\"b", "back\\slash", "what?", "tab\there\nnl\r", "héllo ✓"] { xs.append(s) }
            for s in xs {
                sb.append($"{cprint.escape(s)}|{cprint.cString(s)}|{cprint.stringLiteral(s)}\n")
            }
            for w in ["int", "main", "name", "typeof", "Int", "class"] { sb.append($"{cprint.fieldName(w)}\n") }
            return $"{sb.finish()}"
        }

        string t6Symbols(ref CCtx cc) uses alloc {
            var sb = StrBuf()
            sb.append("== symbols\n")
            for s in ["b", "a", "b", "", "é"] { sb.append($"{cprint.symbolLiteral(ref cc, for: s)}\n") }
            sb.append($"{cprint.symbolText(ref cc, for: "x")}\n{cprint.symbolIntern(ref cc, for: "y")}\n")
            sb.append($"{cc.symbols}\n")
            for s in cc.symbolTexts { sb.append($"text {s}\n") }
            return $"{sb.finish()}"
        }

        /// Functions the corpus has none of: one with no file, one its module does not list, and files named relatively.
        void t6Mutate(ref Ctx c, through in LoweredProgram program, by string flag) uses alloc {
            for f in program.functions {
                let fn = f.symbol
                if flag == "nofile" && c.functions[fn].name == "apply" { c.functions[fn].file = -1 }
                if flag == "nomodule" && c.functions[fn].name == "twice" { let _ = c.modules[c.functions[fn].module].functions.remove(c.functions[fn].name) }
                if flag == "relpath" && c.functions[fn].file != -1 { c.files[c.functions[fn].file].path = "lib/rel.byn" }
            }
        }

        string t6Names(ref CCtx cc, from ref Ctx c, through in LoweredProgram program) uses alloc {
            var sb = StrBuf()
            sb.append("== names\n")
            var fns = List<semantics.FuncId>()
            for f in program.functions { fns.append(f.symbol) }
            for f in program.testFunctions { fns.append(f.symbol) }
            if program.main != -1 { fns.append(program.main) }
            for fn in fns {
                sb.append($"{cprint.functionName(ref cc, from: ref c, for: fn)} {cprint.fileName(ref cc, from: ref c, for: fn)} {cprint.filePath(ref cc, from: ref c, for: fn)}\n")
            }
            for (path, name) in cc.files { sb.append($"file {name} {path}\n") }
            return $"{sb.finish()}"
        }

        /// What T7 (types, vtables) and T8 (files, helpers) would have registered before `Run`.
        void t6Prepare(ref CCtx cc) uses alloc {
            cc.files["lib/a.byn"] = "byn_file_0"
            cc.files["bé.byn"] = "byn_file_1"
            cc.statics["st_a"] = $"/* {cprint.symbolLiteral(ref cc, for: "zed")} */\n"
            cc.staticOrder.append("st_a")
            cc.statics["st_b"] = $"/* {cprint.symbolLiteral(ref cc, for: "abc")} {cprint.symbolLiteral(ref cc, for: "zed")} */\n"
            cc.staticOrder.append("st_b")
            let h = cprint.helper(ref cc, for: "h1", from: "void h1(void)", as: "void h1(void) {\n}\n#line generated\nint x;\n")
            cc.symbolTexts.append("zed")
            let wide = cprint.symbolLiteral(ref cc, for: "～")
            let smile = cprint.symbolLiteral(ref cc, for: "😀")
            let accent = cprint.symbolLiteral(ref cc, for: "é")
            if !cc.ownershipChecks { let blank = cprint.symbolLiteral(ref cc, for: "") }
            cc.statics["st_c"] = $"{rune(1)}x {rune(1)}7x {rune(1)}12\n"
            cc.staticOrder.append("st_c")
            cc.vtables["v1"] = "static const int v1 = 1;\n"
            cc.vtableOrder.append("v1")
        }
        """;

    // ── Harness (as SelfhostEmitterT2Tests) ──────────────────────────────────────

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T6 main did not build:\n{error}");
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
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path)
    {
        var c = Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!, new CompilationOptions(IncludeTests: true));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        return c;
    }

    private static (int Exit, string Out, string Err) Run(string mode, string path, string flag = "") =>
        SelfhostParserTests.Run(Build(), [mode, path, flag]);

    private static Dictionary<string, List<string>> RunMode(string mode, string path, string flag = "")
    {
        var (exit, stdout, stderr) = Run(mode, path, flag);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    private static object? Call(object target, string name, params object?[] args) =>
        typeof(CEmitter).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object Field(object target, string name) =>
        typeof(CEmitter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void SetField(object target, string name, object? value) =>
        typeof(CEmitter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static CEmitter NewEmitter(LoweredProgram p, bool ownership = false) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, ownership], null)!;

    private static CEmitter WithFlag(LoweredProgram p, string flag)
    {
        var e = NewEmitter(p, flag == "debug");
        if (flag == "hashes") SetField(e, "_hashes", true);
        if (flag == "symbols") SetField(e, "_symbols", true);
        if (flag.StartsWith("root:", StringComparison.Ordinal)) SetField(e, "_packageRoot", flag[5..]);
        return e;
    }

    /// <summary>The lines of <paramref name="text"/>, empty ones included, so blank-line placement is compared.</summary>
    private static List<string> Lines(string text) => text.Split('\n').ToList();

    /// <summary>The lines <see cref="SelfhostSemanticsHarness.Sections"/> keeps of <paramref name="text"/>.</summary>
    private static List<string> SectionLines(string text) => Lines(text).Where(l => l.Length > 0).ToList();

    /// <summary>The generated main's whole stdout, split as <see cref="Lines"/> splits the oracle (nothing dropped).</summary>
    private static Dictionary<string, List<string>> RawMode(string mode, string path, string flag = "")
    {
        var (exit, stdout, stderr) = Run(mode, path, flag);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return new() { [""] = Lines(stdout.Replace("\r\n", "\n")) };
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    private const string Full = """
        union error E {
            bad
            worse(string why)
        }

        const gravity: f64 = 6.673e-11
        const half: f32 = 0.5
        const lowest: i64 = -9223372036854775807 - 1
        const top: u64 = 18446744073709551615
        const small: u8 = 200
        const yes: bool = true
        const letter: rune = 'x'
        const label: string = "a\"b\\c?\t é✓"
        const primes: u32[3] = [2, 3, 5]
        const names: string[2] = ["x\ty", "é"]
        const ratios: f64[2] = [0.5, 3.0]
        const many: u32[10] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]

        enum Kind {
            word
            symbol
        }

        enum Planet(f64 mass, f32 gee, string label, Kind kind) {
            mercury(3.303e23, 3.7, "swift", .word)
            earth(5.976e24, 9.8, "home", .symbol)
        }

        struct Counter {
            i64 n
            Counter(i64 start) { n = start }
            i64 plus(by i64 d) => n + d
            i64 plus(from i64 a, to i64 b) => n - a + b
            i64 value => n
        }

        i64 twice(i64 x) => x * 2
        i64 twice(i64 x, by i64 k) => x * k

        T id<T>(move T v) => move v

        i64 apply(i64 x) {
            let add: i64 -> i64 = y => y + 1
            return add(x)
        }

        test "adds" {
            let a = id(1)
            let b = id("s")
            let c = Counter(1)
            assert twice(a) == 2 && twice(a, by: 3) == 3 && c.value == 1 && apply(1) == 2
        }

        test "plus" {
            assert Counter(3).plus(by: 1) == 4
        }

        void main() fails E {
            fail .bad if apply(0) == 7
        }
        """;

    private const string ArgsMain = "i32 main(ReadOnlySpan<string> args) {\n    return i32(args.length)\n}\n";

    private const string VoidMain = "void main() {\n}\n";

    private const string Script = "let x = 1\n";

    private const string Library = "i64 f(i64 x) => x\n\nbool g(bool b) => b\n";

    private const string Failing = "union error E {\n    bad\n}\n\ni32 main(ReadOnlySpan<string> args) fails E {\n    fail .bad if args.length > 5\n    return 0\n}\n";

    private static void Agree(string source, string mode, string flag, Func<Compilation, LoweredProgram, CEmitter, string> oracle) =>
        WithFixture(source, path =>
        {
            var c = Compile(path);
            var p = Lowerer.Lower(c);
            var e = WithFlag(p, flag);
            if (mode == "main") SelfhostSemanticsHarness.AssertSection(RawMode(mode, path, flag), "", Lines("== main\n" + oracle(c, p, e)));
            else SelfhostSemanticsHarness.AssertSection(RunMode(mode, path, flag), mode, SectionLines(oracle(c, p, e)));
        });

    // ── Static data and literals (CEmitter.cs 1047-1147) ─────────────────────────

    private static string DataOracle(Compilation c, LoweredProgram p, CEmitter e)
    {
        var sb = new StringBuilder();
        foreach (var en in c.EntryModule.Enums)
        {
            sb.Append(e.EnumNames(en)).Append('\n').Append(e.EnumNames(en)).Append('\n').Append(e.EnumValues(en)).Append('\n').Append(e.EnumValues(en)).Append('\n');
            foreach (var f in en.Fields) sb.Append(e.EnumField(f)).Append('\n').Append(e.EnumField(f)).Append('\n');
        }
        foreach (var k in c.EntryModule.AllConstants)
        {
            if (k.IsStaticArray) sb.Append(e.StaticArray(k)).Append('\n').Append(e.StaticArray(k)).Append('\n');
            else sb.Append($"{k.Name} {e.Constant(k.Value!, k.Type)}\n");
        }
        foreach (var n in (List<string>)Field(e, "_staticOrder")) sb.Append($"static {n}\n{((Dictionary<string, string>)Field(e, "_statics"))[n]}");
        return sb.ToString();
    }

    [Fact]
    public void Data_EnumsConstantsAndArraysMatchCSharp() => Agree(Full, "data", "", DataOracle);

    [Fact]
    public void Constants_EveryKindAndTypeMatchCSharp() => WithFixture(Full, path =>
    {
        var c = Compile(path);
        var e = NewEmitter(Lowerer.Lower(c));
        var types = new TypeSymbol[]
        {
            PrimitiveType.F32, PrimitiveType.F64, PrimitiveType.I64, PrimitiveType.Isize, PrimitiveType.U64, PrimitiveType.Usize,
            PrimitiveType.U8, PrimitiveType.I32, c.EntryModule.Enums[0],
        };
        var values = new List<object>
        {
            (Int128)5, (Int128)(-5), (Int128)long.MinValue, (Int128)ulong.MaxValue, (Int128)1 << 64, (Int128)long.MaxValue + 1, (Int128)1 << 96,
            ((Int128)1 << 64) + 12345, -(((Int128)1 << 64) + 12345), Int128.MaxValue, -Int128.MaxValue,
            ((Int128)1 << 64) + 0x800, ((Int128)1 << 64) + 0x1800, ((Int128)1 << 64) + 0x801, -(((Int128)1 << 64) + 0x1800),
            true, false, "a\"b\\c?\n", new System.Text.Rune(233),
        };
        foreach (var d in new[] { 0.0, 0.001, 0.5, 5.0, 1e300, 1e-7, 123456789012345678.0, 0.1, -2.5, 100.0, 1.5e16, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            values.Add(d);
        var lines = new List<string>();
        foreach (var v in values)
            foreach (var t in types) lines.Add(e.Constant(new ConstantValue(v), t));
        SelfhostSemanticsHarness.AssertSection(RunMode("consts", path), "consts", lines);
    });

    /// <summary>
    /// Every float and string constant of every <c>run/</c> input (the corpus <see cref="Golden.Cases"/> lists), as Bayan's binder
    /// finds it, printed by the port and by <c>CEmitter.Constant</c> (f64 and f32 for a float).
    /// </summary>
    [Fact]
    public void Constants_OfTheRunCorpusMatchCSharp()
    {
        var paths = ((IEnumerable<object[]>)Golden.Cases("run")).Select(d => Path.Combine(Golden.CasesRoot, (string)d[0])).ToList();
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, Library);
            var e = NewEmitter(Lowerer.Lower(Compile(path)));
            var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), ["corpus", .. paths]);
            Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
            var lines = stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Count(l => l[0] == 'r') > 20 && lines.Count(l => l[0] == 's') > 50, "the corpus has too few constants to mean anything");
            foreach (var line in lines)
            {
                var parts = line.Split(' ', 3);
                string expected;
                if (parts[0] == "r")
                {
                    var rest = parts[2].Split(' ', 2);
                    var d = parts[1] switch { "inf" => double.PositiveInfinity, "-inf" => double.NegativeInfinity, "nan" or "NaN" => double.NaN, var t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture) };
                    expected = $"{e.Constant(new ConstantValue(d), PrimitiveType.F64)} {e.Constant(new ConstantValue(d), PrimitiveType.F32)}";
                    Assert.Equal($"r {parts[1]} {expected}", line);
                }
                else
                {
                    var bytes = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(byte.Parse).ToArray();
                    expected = e.Constant(new ConstantValue(Encoding.UTF8.GetString(bytes)), PrimitiveType.I32);
                    Assert.Equal($"s {parts[1]} {expected}", line);
                }
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Strings_EscapeAndFieldNamesMatchCSharp() => WithFixture(Library, path =>
    {
        var e = NewEmitter(Lowerer.Lower(Compile(path)));
        var lines = new List<string>();
        foreach (var s in new[] { "", "plain", "a\"b", "back\\slash", "what?", "tab\there\nnl\r", "héllo ✓" })
            lines.Add($"{Call(e, "Escape", (object)Encoding.UTF8.GetBytes(s))}|{Call(e, "CString", s)}|{Call(e, "StringLiteral", s)}");
        foreach (var w in new[] { "int", "main", "name", "typeof", "Int", "class" }) lines.Add(CEmitter.FieldName(w));
        SelfhostSemanticsHarness.AssertSection(RunMode("strings", path), "strings", lines);
    });

    // ── Symbols and names (CEmitter.cs 137-178, 348-396) ─────────────────────────

    [Fact]
    public void Symbols_NumberOnFirstSightLikeCSharp() => Agree(Library, "symbols", "", (c, p, e) =>
    {
        var sb = new StringBuilder();
        foreach (var s in new[] { "b", "a", "b", "", "é" }) sb.Append(e.SymbolLiteral(s)).Append('\n');
        sb.Append(e.SymbolText("x")).Append('\n').Append(e.SymbolIntern("y")).Append('\n');
        sb.Append((bool)Field(e, "_symbols") ? "true" : "false").Append('\n');
        foreach (var t in (List<string>)Field(e, "_symbolTexts")) sb.Append($"text {t}\n");
        return sb.ToString();
    });

    private static string NamesOracle(Compilation c, LoweredProgram p, CEmitter e)
    {
        var fns = p.Functions.Select(f => f.Symbol).Concat(p.Tests.Select(t => t.Function.Symbol)).ToList();
        if (p.Main is not null) fns.Add(p.Main);
        var sb = new StringBuilder();
        foreach (var fn in fns) sb.Append($"{e.FunctionName(fn)} {e.FileName(fn)} {e.FilePath(fn)}\n");
        foreach (var (path, name) in (Dictionary<string, string>)Field(e, "_files")) sb.Append($"file {name} {path}\n");
        return sb.ToString();
    }

    [Fact]
    public void Names_FunctionsFilesAndPathsMatchCSharp() => Agree(Full, "names", "", NamesOracle);

    [Theory]
    [InlineData("nofile")]
    [InlineData("nomodule")]
    [InlineData("relpath")]
    public void Names_OfFunctionsTheCorpusLacksMatchCSharp(string flag) => Agree(Full, "names", flag, (c, p, e) =>
    {
        foreach (var f in p.Functions.Select(f => f.Symbol))
        {
            if (flag == "nofile" && f.Name == "apply") f.File = null;
            if (flag == "nomodule" && f.Name == "twice") f.Module.Functions.Remove(f.Name);
            if (flag == "relpath" && f.File is { } file)
                typeof(SourceText).GetField("<Path>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file.Source, "lib/rel.byn");
        }
        return NamesOracle(c, p, e);
    });

    [Fact]
    public void Names_OfAnImplicitMainMatchCSharp() => Agree(Script, "names", "", NamesOracle);

    // The root is a subdirectory of the file's, so the relative path starts with `..` and the file name stands (CEmitter.cs:393).
    [Fact]
    public void Names_PathOutsideTheRootUsesTheFileNameLikeCSharp() => WithFixture(Full, path =>
    {
        var sub = Path.Combine(Path.GetDirectoryName(path)!, "sub");
        var c = Compile(path);
        var p = Lowerer.Lower(c);
        var e = NewEmitter(p);
        SetField(e, "_packageRoot", sub);
        var expected = SectionLines(NamesOracle(c, p, e));
        Assert.Contains(expected, l => l.EndsWith(" a.byn", StringComparison.Ordinal));
        SelfhostSemanticsHarness.AssertSection(RunMode("names", path, "root:" + sub), "names", expected);
    });

    // A test function missing from `_testIndex`: C# throws KeyNotFoundException (CEmitter.cs:350), Bayan panics.
    [Fact]
    public void FunctionName_OfAnUnnumberedTestFailsLikeCSharp() => WithFixture(Full, path =>
    {
        var c = Compile(path);
        var p = Lowerer.Lower(c);
        var e = NewEmitter(p);
        ((Dictionary<FunctionSymbol, int>)Field(e, "_testIndex")).Clear();
        Assert.Throws<KeyNotFoundException>(() => e.FunctionName(p.Tests[0].Function.Symbol));
        var (exit, _, err) = Run("namefail", path);
        Assert.NotEqual(0, exit);
        Assert.Contains("panic:", err);
    });

    // ── EmitMain (CEmitter.cs 290-346) ───────────────────────────────────────────

    [Theory]
    [InlineData("full", "")]
    [InlineData("full", "hashes")]
    [InlineData("full", "symbols")]
    [InlineData("args", "")]
    [InlineData("args", "symbols")]
    [InlineData("failing", "")]
    [InlineData("library", "hashes")]
    [InlineData("void", "")]
    [InlineData("script", "")]
    public void Main_MatchesCSharp(string which, string flag)
    {
        var source = which switch { "full" => Full, "args" => ArgsMain, "failing" => Failing, "void" => VoidMain, "script" => Script, _ => Library };
        Agree(source, "main", flag, (c, p, e) => (string)Call(e, "EmitMain")!);
    }

    // ── Run and Emit (CEmitter.cs 61-135) ────────────────────────────────────────

    private static void Prepare(CEmitter e)
    {
        var files = (Dictionary<string, string>)Field(e, "_files");
        files["lib/a.byn"] = "byn_file_0";
        files["bé.byn"] = "byn_file_1";
        var statics = (Dictionary<string, string>)Field(e, "_statics");
        var order = (List<string>)Field(e, "_staticOrder");
        statics["st_a"] = $"/* {e.SymbolLiteral("zed")} */\n";
        order.Add("st_a");
        statics["st_b"] = $"/* {e.SymbolLiteral("abc")} {e.SymbolLiteral("zed")} */\n";
        order.Add("st_b");
        e.Helper("h1", "void h1(void)", () => "void h1(void) {\n}\n#line generated\nint x;\n");
        ((List<string>)Field(e, "_symbolTexts")).Add("zed");
        // U+FF5E (UTF-8 EF BD 9E) sorts after U+1F600 (F0 9F 98 80) by code unit and before it by bytes.
        e.SymbolLiteral("\uFF5E");
        e.SymbolLiteral("\U0001F600");
        e.SymbolLiteral("é");
        if (!e.OwnershipChecks) e.SymbolLiteral("");
        statics["st_c"] = "\u0001x \u00017x \u000112\n";
        order.Add("st_c");
        ((Dictionary<string, string>)Field(e, "_vtables"))["v1"] = "static const int v1 = 1;\n";
        ((List<string>)Field(e, "_vtableOrder")).Add("v1");
    }

    /// <summary><c>Run</c> of an empty program; <c>EmitTypes</c> is real since T7 landed, so nothing is cut.</summary>
    private static string RunOf(Compilation c, string flag, bool prepare)
    {
        if (flag == "noerrors") c.ErrorVariants.Clear();
        var e = WithFlag(new LoweredProgram(c, [], null, []), flag);
        if (prepare) Prepare(e);
        return (string)Call(e, "Run")!;
    }

    [Theory]
    [InlineData("full", "")]
    [InlineData("full", "debug")]
    [InlineData("full", "hashes")]
    [InlineData("full", "symbols")]
    [InlineData("library", "")]
    [InlineData("library", "noerrors")]
    [InlineData("library", "symbols")]
    public void Run_SectionsAndSubstitutionMatchCSharp(string which, string flag) => WithFixture(which == "full" ? Full : Library, path =>
    {
        var c = Compile(path);
        SelfhostSemanticsHarness.AssertSection(RawMode("run", path, flag), "", Lines(RunOf(c, flag, prepare: true)));
    });

    [Theory]
    [InlineData("full", "")]
    [InlineData("library", "debug")]
    public void Emit_OfAnEmptyProgramMatchesCSharp(string which, string flag) => WithFixture(which == "full" ? Full : Library, path =>
    {
        var c = Compile(path);
        SelfhostSemanticsHarness.AssertSection(RawMode("emit", path, flag), "", Lines(RunOf(c, flag, prepare: false)));
    });

    private const string Lowered = "void f() {\n}\n\ntest \"t\" {\n}\n";

    // T9: `Emit` of a lowered function and test, so `Run` emits their prototypes and bodies.
    [Theory]
    [InlineData("")]
    [InlineData("debug")]
    public void Emit_OfLoweredFunctionsMatchesCSharp(string flag) => WithFixture(Lowered, path =>
    {
        var expected = CEmitter.Emit(Lowerer.Lower(Compile(path)), flag == "debug");
        SelfhostSemanticsHarness.AssertSection(RawMode("lowered", path, flag), "", Lines(expected));
    });
}
