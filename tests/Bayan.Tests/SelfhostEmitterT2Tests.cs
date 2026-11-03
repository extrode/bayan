using System.Collections.Concurrent;
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
/// 011 T2: <c>selfhost/emitter/lower.byn</c> (Lowerer.cs 11-351) and <c>cprint/common.byn</c> (CEmitter.cs 397-476,
/// 588-599, 1237-1253) against the C# as an in-process oracle. The emitter's top-level files but <c>main.byn</c> are
/// built with a generated <c>main.byn</c> that prints what the ported functions return, by mode (Q763).
/// </summary>
public sealed class SelfhostEmitterT2Tests
{
    // args: mode, the C file name and `#line` path of the C# `FunctionEmitter` ("-" for none), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CFunc = cprint.CFunc

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 3..args.length { rest.append(args[i]) }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            let mode = args[0]
            if mode == "lower" { Io.stdout.write(t2PrintLower(ref c, through: ref l)) }
            if mode == "helper" { Io.stdout.write(t2PrintHelper()) }
            if mode == "keyfail" { Io.stdout.write(t2KeyFail(ref c)) }
            if mode == "corpus" {
                Io.stdout.write(t2PrintCTypes(ref c))
                Io.stdout.write(t2PrintPredicates(ref c, through: ref l))
                Io.stdout.write(t2PrintLines(ref c, at: args[1], for: args[2]))
            }
            return 0
        }

        /// `Lowerer.Lower`'s filter: the functions it lowers, in order.
        List<FuncId> t2Functions(ref Ctx c) uses alloc {
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

        string t2PrintLower(ref Ctx c, through ref LCtx l) uses alloc {
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            sb.append("== lower\n")
            for f in p.functions { sb.append(t2PrintFunction(in c, at: $"fn {c.functions[f.symbol].name}", for: in f)) }
            sb.append(p.main == -1 ? "main -\n" : $"main {c.functions[p.main].name}\n")
            for i in 0..p.tests.length { sb.append(t2PrintFunction(in c, at: $"test {c.tests[p.tests[i]].title}", for: in p.testFunctions[i])) }
            return $"{sb.finish()}"
        }

        string t2PrintFunction(in Ctx c, at string head, for in LoweredFunction f) uses alloc {
            return $"{head} locals={f.locals.length} ctx={f.contextSlots} panic={f.panicSlots} stmts={c.bBlocks[f.body & 0xFFFFFFFF].statements.length}\n"
        }

        string t2PrintHelper() uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== helper\n")
            sb.append($"{cprint.helper(ref cc, for: "a", from: "pa", as: "ba")}\n")
            sb.append($"{cprint.helper(ref cc, for: "a", from: "px", as: "bx")}\n")
            sb.append($"{cprint.helper(ref cc, for: "b", from: "pb", as: "bb")}\n")
            for n in cc.helperOrder { sb.append($"{n} {cc.helpers[n].proto} | {cc.helpers[n].body}\n") }
            return $"{sb.finish()}"
        }

        /// `Key` of a generic function's first parameter type, which has no C type.
        string t2KeyFail(ref Ctx c) uses alloc {
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if semantics.functionIsGeneric(ref c, by: fn) {
                        let t = c.locals[c.functions[fn].parameters[0]].type
                        return cprint.key(ref c, for: t)
                    }
                }
            }
            return ""
        }

        string t2PrintCTypes(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== ctype\n")
            for fn in t2Functions(ref c) {
                var types = List<Type>()
                for p in c.functions[fn].parameters { types.append(c.locals[p].type) }
                types.append(c.functions[fn].returnType)
                for t in types { sb.append($"{cprint.cType(ref cc, from: ref c, for: t)}\n") }
            }
            sb.append($"error {cprint.errorCType(ref cc, from: ref c)}\n")
            for n in cc.typeOrder { sb.append($"order {n}\n") }
            return $"{sb.finish()}"
        }

        string t2PrintPredicates(ref Ctx c, through ref LCtx l) uses alloc {
            var sb = StrBuf()
            sb.append("== pred\n")
            for fn in t2Functions(ref c) {
                l.fn = fn
                l.outs = List<List<BStmt>>()
                l.outs.append(List<BStmt>())
                l.locals = List<LocalId>()
                l.seen = Map<LocalId, bool>()
                let body = c.functions[fn].body
                var decls = List<i64>()
                for s in c.bBlocks[body & 0xFFFFFFFF].statements {
                    if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl { decls.append(s & 0xFFFFFFFF) }
                }
                var declared = List<LocalId>()
                for d in decls {
                    let init = c.bLocalDecls[d].initializer
                    let local = c.bLocalDecls[d].local
                    if init != -1 {
                        sb.append($"{isTrivial(in c, through: in l, for: init)} {isRepeatable(in c, through: in l, for: init)} {isLvalue(in c, through: in l, for: init)} {hasEffects(in c, through: in l, for: init)}")
                        for x in declared { sb.append($" {moves(in c, through: in l, for: init, by: x)}") }
                        let n0 = l.locals.length
                        captureBegin(ref l)
                        let sp = spill(ref c, through: ref l, for: init)
                        let spilled = captureEnd(ref l)
                        captureBegin(ref l)
                        emitEffect(ref c, through: ref l, for: init)
                        let effects = captureEnd(ref l)
                        sb.append($" spill={spilled.length} temps={l.locals.length - n0} effect={effects.length}\n")
                    }
                    declared.append(local)
                }
            }
            return $"{sb.finish()}"
        }

        string t2PrintLines(ref Ctx c, at string file, for string linePath) uses alloc {
            var sb = StrBuf()
            sb.append("== lines\n")
            for fn in t2Functions(ref c) {
                let span = semantics.nodeSpan(in c, for: c.functions[fn].body)
                var withPath = CFunc { fn: fn, file: file, linePath: linePath }
                if linePath == "-" { withPath.linePath = null }
                sb.append(t2LineCase(in c, through: ref withPath, at: span))
                var without = CFunc { fn: fn, file: file, linePath: null }
                sb.append(t2LineCase(in c, through: ref without, at: span))
            }
            return $"{sb.finish()}"
        }

        string t2LineCase(in Ctx c, through ref CFunc f, at semantics.TextSpan span) uses alloc {
            var buf = StrBuf()
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            cprint.line(ref buf, by: in f, for: "a;")
            cprint.mapLine(ref buf, from: in c, through: ref f, at: span)
            f.indent = 2
            cprint.line(ref buf, by: in f, for: "b;")
            return $"line {cprint.lineOf(in c, by: in f, at: span)} loc {cprint.loc(in c, by: in f, at: span)}\n{buf.finish()}"
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T2 main did not build:\n{error}");
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

    /// <summary>Writes <paramref name="source"/> to a temporary file, then runs <paramref name="act"/> on its path.</summary>
    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t2-" + Guid.NewGuid().ToString("N"));
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

    private static (int Exit, string Out, string Err) Run(string mode, string path, string file = "-", string linePath = "-") =>
        SelfhostParserTests.Run(Build(), [mode, file, linePath, path]);

    private static Dictionary<string, List<string>> RunMode(string mode, string path, string file = "-", string linePath = "-")
    {
        var (exit, stdout, stderr) = Run(mode, path, file, linePath);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout);
    }

    private static readonly Type FunctionLowerer = typeof(Lowerer).Assembly.GetType("Bayan.Lowering.FunctionLowerer", throwOnError: true)!;

    private static object? Call(Type type, object? target, string name, params object?[] args) =>
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    /// <summary><c>Lowerer.Lower</c>'s filter: the functions it lowers, in order.</summary>
    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric).ToList();

    private static string Flag(object? b) => (bool)b! ? "true" : "false";

    // ── lower (Lowerer.cs:18-93) ─────────────────────────────────────────────────

    private const string LowerFixture = """
        struct P {
            i64 x = 1
            i64 y
            P(i64 a) { }
        }

        void f() { }

        T id<T>(move T v) => move v

        test "nothing" { }

        void main() { }
        """;

    private static List<string> LowerOracle(Compilation c)
    {
        var p = Lowerer.Lower(c);
        var lines = new List<string>();
        foreach (var f in p.Functions) lines.Add($"fn {f.Symbol.Name} {Shape(f)}");
        lines.Add(p.Main is null ? "main -" : $"main {p.Main.Name}");
        foreach (var (t, f) in p.Tests) lines.Add($"test {t.Name} {Shape(f)}");
        return lines;
    }

    private static string Shape(LoweredFunction f) =>
        $"locals={f.Locals.Count} ctx={f.ContextSlots} panic={f.PanicSlots} stmts={f.Body.Statements.Length}";

    [Fact]
    public void Lower_ProgramShapeMatchesCSharp() => WithFixture(LowerFixture, path =>
        SelfhostSemanticsHarness.AssertSection(RunMode("lower", path), "lower", LowerOracle(Compile(path))));

    [Fact]
    public void Lower_WithErrorsFailsLikeCSharp() => WithFixture("void main() { let x: i64 = \"s\" }\n", path =>
    {
        var e = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(Compile(path)));
        var (exit, _, err) = Run("lower", path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });

    // ── helper (CEmitter.cs:591) ──────────────────────────────────────────────────

    [Fact]
    public void Helper_RegistersOnceLikeCSharp() => WithFixture("void main() { }\n", path =>
    {
        var c = Compile(path);
        var emitter = NewEmitter(Lowerer.Lower(c));
        var expected = new List<string>
        {
            emitter.Helper("a", "pa", () => "ba"),
            emitter.Helper("a", "px", () => "bx"),
            emitter.Helper("b", "pb", () => "bb"),
        };
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(emitter, "_helpers")!;
        foreach (var n in (List<string>)Field(emitter, "_helperOrder")!) expected.Add($"{n} {helpers[n].Proto} | {helpers[n].Body}");
        SelfhostSemanticsHarness.AssertSection(RunMode("helper", path), "helper", expected);
    });

    // ── Key's failure (CEmitter.cs:468) ───────────────────────────────────────────

    [Fact]
    public void Key_OfATypeParameterFailsLikeCSharp() => WithFixture("T id<T>(move T v) => move v\n\nvoid main() { }\n", path =>
    {
        var c = Compile(path);
        var emitter = NewEmitter(Lowerer.Lower(c));
        var fn = c.EntryModule.AllFunctions.First(f => f.IsGeneric);
        var e = Assert.Throws<InvalidOperationException>(() => emitter.Key(fn.Parameters[0].Type));
        var (exit, _, err) = Run("keyfail", path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });

    // ── The run/ corpus: CType/ErrorCType/Named order, the lowerer's predicates, Spill, EmitEffect, the line helpers ─

    private static (string File, string LinePath, Dictionary<string, List<string>> Sections) CorpusOracle(Compilation c)
    {
        var p = Lowerer.Lower(c);
        var emitter = NewEmitter(p);
        var functions = Functions(c);
        var ctype = new List<string>();
        foreach (var fn in functions)
        {
            foreach (var param in fn.Parameters) ctype.Add(emitter.CType(param.Type));
            ctype.Add(emitter.CType(fn.ReturnType));
        }
        ctype.Add($"error {emitter.ErrorCType}");
        foreach (var n in (List<string>)Field(emitter, "_typeOrder")!) ctype.Add($"order {n}");

        var pred = new List<string>();
        foreach (var fn in functions)
        {
            var fl = Activator.CreateInstance(FunctionLowerer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [fn, c.Builtins, null, null], null)!;
            var output = (List<BoundStmt>)Field(fl, "_out")!;
            var locals = (List<LocalSymbol>)Field(fl, "_locals")!;
            var declared = new List<LocalSymbol>();
            foreach (var d in fn.Body!.Statements.OfType<BoundLocalDecl>())
            {
                if (d.Initializer is { } init)
                {
                    var sb = new StringBuilder();
                    sb.Append($"{Flag(Call(FunctionLowerer, null, "IsTrivial", init))} {Flag(Call(FunctionLowerer, null, "IsRepeatable", init))} ");
                    sb.Append($"{Flag(Call(FunctionLowerer, null, "IsLvalue", init))} {Flag(Call(FunctionLowerer, null, "HasEffects", init))}");
                    foreach (var x in declared) sb.Append($" {Flag(Call(FunctionLowerer, null, "Moves", init, x))}");
                    var n0 = locals.Count;
                    var o0 = output.Count;
                    Call(FunctionLowerer, fl, "Spill", init);
                    var spilled = output.Count - o0;
                    var temps = locals.Count - n0;
                    o0 = output.Count;
                    Call(FunctionLowerer, fl, "EmitEffect", init);
                    sb.Append($" spill={spilled} temps={temps} effect={output.Count - o0}");
                    pred.Add(sb.ToString());
                }
                declared.Add(d.Local);
            }
        }

        var lines = new List<string>();
        string file = "-", linePath = "-";
        foreach (var f in p.Functions)
        {
            var syntax = f.Symbol.Body!.Syntax;
            foreach (var withPath in new[] { true, false })
            {
                var fe = new FunctionEmitter(emitter, f);
                if (file == "-")
                {
                    file = (string)Field(fe, "_file")!;
                    // A program built without debug info has no `#line` path, so the C# is given the one the selfhost is.
                    linePath = (string?)Field(fe, "_linePath") ?? "\"a.byn\"";
                }
                typeof(FunctionEmitter).GetField("_linePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fe, withPath ? linePath : null);
                var t = typeof(FunctionEmitter);
                Call(t, fe, "MapLine", syntax);
                Call(t, fe, "MapLine", syntax);
                Call(t, fe, "Line", "a;");
                Call(t, fe, "MapLine", syntax);
                t.GetField("_indent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fe, 2);
                Call(t, fe, "Line", "b;");
                lines.Add($"line {Call(t, fe, "LineOf", syntax)} loc {Call(t, fe, "Loc", syntax)}");
                lines.AddRange(((StringBuilder)Field(fe, "_sb")!).ToString().Split('\n').Where(s => s.Length > 0));
            }
        }
        return (file, linePath, new() { ["ctype"] = ctype, ["pred"] = pred, ["lines"] = lines });
    }

    // The selfhost binder builds a `requires` function's body with one more local than the C# (Q766), so only the
    // `pred` section of these files is skipped.
    private static readonly HashSet<string> PredSkipped = ["positional-requires.byn"];

    [Fact]
    public void Corpus_MatchesCSharp()
    {
        var files = Directory.GetFiles(Path.Combine(Golden.RepoRoot, "tests", "cases", "run"), "*.byn").Order(StringComparer.Ordinal).ToList();
        var failures = new ConcurrentBag<string>();
        var compared = 0;
        Build();
        // Each file alone in a directory: bayan-check reads a file's directory as its package.
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, corpus => WithFixture(File.ReadAllText(corpus), path =>
        {
            var c = Compile(path);
            if (c.Diagnostics.HasErrors) return;
            var (file, linePath, expected) = CorpusOracle(c);
            // bayan-check's warnings go to stderr, so only the exit is checked.
            var (exit, stdout, stderr) = Run("corpus", path, file, linePath);
            if (exit != 0)
            {
                failures.Add($"{Path.GetFileName(corpus)}: exited {exit}: {stderr}");
                return;
            }
            var actual = SelfhostSemanticsHarness.Sections(stdout);
            foreach (var (section, lines) in expected)
            {
                if (section == "pred" && PredSkipped.Contains(Path.GetFileName(corpus))) continue;
                var got = actual.GetValueOrDefault(section) ?? [];
                for (var i = 0; i < Math.Max(got.Count, lines.Count); i++)
                {
                    var e = i < lines.Count ? lines[i] : "<end>";
                    var a = i < got.Count ? got[i] : "<end>";
                    if (e == a) continue;
                    failures.Add($"{Path.GetFileName(corpus)} {section} line {i + 1}\n expected: {e}\n   actual: {a}");
                    break;
                }
            }
            Interlocked.Increment(ref compared);
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(4))}");
        Assert.True(failures.IsEmpty, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(8))}");
    }
}
