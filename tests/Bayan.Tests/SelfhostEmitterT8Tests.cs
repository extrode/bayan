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
/// 011 T8: <c>selfhost/emitter/cprint/helpers.byn</c> (CEmitter.cs 779-1046: <c>Equal</c>, <c>KeyHash</c>, <c>KeyEq</c>,
/// <c>MapMethod</c>, <c>Format</c>, <c>FormatInto</c>, <c>FormatIntoWithSpec</c>) against the C# as an in-process oracle. The types
/// are the parameters of the fixture's <c>eqN</c>, <c>fmtN</c> and <c>mapN</c> functions.
/// </summary>
public sealed class SelfhostEmitterT8Tests
{
    // args: mode, a function name (or "-"), then bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint

        type CCtx = cprint.CCtx

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 2..args.length { rest.append(args[i]) }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            let mode = args[0]
            if mode == "corpus" {
                Io.stdout.write(t8Corpus(ref c))
                Io.stdout.write(t8FormatInto(ref c))
            }
            if mode == "failEq" {
                var cc = CCtx {}
                Io.stdout.write(cprint.equalHelper(ref cc, from: ref c, for: t8Param(ref c, at: args[1]), at: "a", to: "b"))
            }
            if mode == "failFmt" {
                var cc = CCtx {}
                Io.stdout.write(cprint.format(ref cc, from: ref c, for: t8Param(ref c, at: args[1]), at: "v", to: "w"))
            }
            return 0
        }

        /// The type of the first parameter of function `name`.
        Type t8Param(ref Ctx c, at string name) uses alloc {
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    if c.functions[fn].name == name { return c.locals[c.functions[fn].parameters[0]].type }
                }
            }
            return -1
        }

        string t8Corpus(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== corpus\n")
            for m in 0..c.modules.length {
                for k in 0..c.modules[m].allFunctions.length {
                    let fn = c.modules[m].allFunctions[k]
                    let name = c.functions[fn].name
                    if c.functions[fn].body == -1 || c.functions[fn].parameters.length != 1 { continue }
                    let t = c.locals[c.functions[fn].parameters[0]].type
                    if name.startsWith("eq") { sb.append($"{name} {cprint.equalHelper(ref cc, from: ref c, for: t, at: "a", to: "b")}\n") }
                    if name.startsWith("fmt") { sb.append($"{name} {cprint.format(ref cc, from: ref c, for: t, at: "v", to: "w")}\n") }
                    if name.startsWith("map") {
                        var args = List<string>()
                        args.append("k")
                        args.append("d")
                        let members = [semantics.BuiltinMember.mapIndexSet, .mapSlot, .mapIndex, .mapTryGet, .mapContains, .mapRemove]
                        for member in members {
                            sb.append($"{name} {cprint.mapMethod(ref cc, from: ref c, for: member, as: t, at: "m", by: args, to: "L")}\n")
                        }
                    }
                }
            }
            sb.append($"hashes {cc.hashes}\n")
            sb.append("== helpers\n")
            for n in cc.helperOrder { sb.append($"{n} {cc.helpers[n].proto} | {cc.helpers[n].body}\n") }
            return $"{sb.finish()}"
        }

        string t8Method(ref Ctx c, for semantics.FuncId fn) uses alloc {
            if fn == -1 { return "-" }
            return $"{c.functions[fn].name}/{c.functions[fn].parameters.length}"
        }

        string t8FormatInto(ref Ctx c) uses alloc {
            var sb = StrBuf()
            sb.append("== formatinto\n")
            for name in ["Plain", "One", "Two", "Both", "Wrong", "Static", "Fails", "ByValue", "NotBuf", "Three", "SpecRef", "Reverse", "StaticSpec", "FailsSpec", "SpecByValue", "SpecNotBuf"] {
                for s in 0..c.structs.length {
                    if c.structs[s].name == name {
                        sb.append($"{name} {t8Method(ref c, for: cprint.formatInto(ref c, for: s))} {t8Method(ref c, for: cprint.formatIntoWithSpec(ref c, for: s))}\n")
                    }
                }
            }
            return $"{sb.finish()}"
        }
        """;

    private static readonly string[] Eq =
    [
        "i64", "u8", "f64", "bool", "rune", "string", "Color", "Symbol", "ParseError", "i64?", "string?",
        "Result<i64, ParseError>", "(i64, string)", "i64[3]", "(i64, (bool, string))", "Nothing", "Owned<i64>", "ParseError?",
        "Result<Nothing, ParseError>", "Nothing[2]", "(i64, Symbol)", "Untrusted<string>",
    ];

    private static readonly string[] Fmt =
    [
        "i64", "u8", "i8", "i16", "u16", "u32", "f64", "f32", "bool", "rune", "string", "i32", "u64", "ParseError", "Owned<i64>",
        "i64?", "string?", "Result<i64, ParseError>", "Result<string, ParseError>", "Owned<i64?>", "Color", "Symbol",
    ];

    private static readonly string[] Maps =
    [
        "Map<i64, string>", "Map<string, i64>", "Map<(i64, string), bool>", "Map<Symbol, i64>", "Map<bool, f64>", "Map<Nothing, i64>",
        "Map<i64, i64?>", "Map<u8, Map<i64, i64>>",
    ];

    private const string PayloadTypes = """
        union error Bad {
            big(i64 n)
        }

        union error Weird {
            bad(List<i64> items)
        }

        union Flag {
            on
            off
        }

        union Shape {
            circle(i64 r)
            square(string side, bool filled)
            none
        }

        struct Pt {
            i64 x
            i64 y
        }

        struct Order {
            i64 id
            states {
                draft
                paid(i64 payment)
            }
        }

        """;

    private static readonly string[] PayloadEq = ["Bad", "Bad?", "Flag", "Shape", "Pt", "Pt[2]", "(Pt, i64)", "Order", "Order?", "Owned<Pt>", "Order.paid", "Weird"];
    private static readonly string[] PayloadFmt = ["Flag", "Shape", "(i64, bool)", "Map<i64, string>", "One", "Two", "Both"];
    private static readonly string[] PayloadMaps = ["Map<Pt, i64>", "Map<Order, i64>", "Map<i64, Shape>"];

    private static string Fixture(bool payload)
    {
        var sb = new StringBuilder("""
            enum Color {
                red
                green
            }

            union error ParseError {
                empty
                badDigit
            }

            struct Nothing {
            }

            struct Plain {
                i64 x
            }

            struct One {
                i64 x
                void formatInto(ref StrBuf sb) { }
            }

            struct Two {
                i64 x
                void formatInto(ref StrBuf sb, as FormatSpec spec) { }
            }

            struct Both {
                i64 x
                void formatInto(ref StrBuf sb, as FormatSpec spec) { }
                void formatInto(ref StrBuf sb) { }
            }

            struct Wrong {
                i64 x
                void formatInto(ref StrBuf sb, as i64 n) { }
            }

            struct Static {
                i64 x
                static void formatInto(ref StrBuf sb) { }
            }

            struct Fails {
                i64 x
                void formatInto(ref StrBuf sb) fails ParseError { }
            }

            struct ByValue {
                i64 x
                void formatInto(in StrBuf sb) { }
            }

            struct NotBuf {
                i64 x
                void formatInto(ref List<i64> xs) { }
            }

            struct Three {
                i64 x
                void formatInto(ref StrBuf sb, as FormatSpec spec, to i64 n) { }
            }

            struct SpecRef {
                i64 x
                void formatInto(ref StrBuf sb, as ref FormatSpec spec) { }
            }

            struct Reverse {
                i64 x
                void formatInto(ref StrBuf sb) { }
                void formatInto(ref StrBuf sb, as FormatSpec spec) { }
            }

            struct StaticSpec {
                i64 x
                static void formatInto(ref StrBuf sb, as FormatSpec spec) { }
            }

            struct FailsSpec {
                i64 x
                void formatInto(ref StrBuf sb, as FormatSpec spec) fails ParseError { }
            }

            struct SpecByValue {
                i64 x
                void formatInto(in StrBuf sb, as FormatSpec spec) { }
            }

            struct SpecNotBuf {
                i64 x
                void formatInto(ref List<i64> xs, as FormatSpec spec) { }
            }

            void zbadEq(List<i64> a) { }
            void zbadFmt(Plain a) { }
            void zbadFmtList(List<i64> a) { }
            void zbadEqBuf(ref StrBuf a) { }
            void zbadFmtBuf(ref StrBuf a) { }

            """);
        if (payload) sb.Append(PayloadTypes);
        var n = 0;
        foreach (var t in payload ? PayloadEq : Eq) sb.Append($"void eq{n++}({t} a) {{ }}\n");
        foreach (var t in payload ? PayloadFmt : Fmt) sb.Append($"void fmt{n++}({t} a) {{ }}\n");
        foreach (var t in payload ? PayloadMaps : Maps) sb.Append($"void map{n++}({t} a) {{ }}\n");
        return sb.Append("i32 main() => 0\n").ToString();
    }

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T8 main did not build:\n{error}");
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

    private static void WithFixture(Action<string> act, bool payload = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, Fixture(payload));
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    private static (int Exit, string Out, string Err) Run(string mode, string function, string path) =>
        SelfhostParserTests.Run(Build(), [mode, function, path]);

    private static CEmitter NewEmitter(LoweredProgram p) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [p, false], null)!;

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static FunctionSymbol Function(Compilation c, string name) =>
        c.Modules.SelectMany(m => m.AllFunctions).First(f => f.Name == name);

    private static string Method(FunctionSymbol? f) => f is null ? "-" : $"{f.Name}/{f.Parameters.Count}";

    private static readonly BuiltinMember[] Members =
        [BuiltinMember.MapIndexSet, BuiltinMember.MapSlot, BuiltinMember.MapIndex, BuiltinMember.MapTryGet, BuiltinMember.MapContains, BuiltinMember.MapRemove];

    private static string Oracle(Compilation c)
    {
        var emitter = NewEmitter(Lowerer.Lower(c));
        var sb = new StringBuilder("== corpus\n");
        foreach (var fn in c.Modules.SelectMany(m => m.AllFunctions).Where(f => f.Body is not null && f.Parameters.Count == 1))
        {
            var t = fn.Parameters[0].Type;
            if (fn.Name.StartsWith("eq", StringComparison.Ordinal)) sb.Append($"{fn.Name} {emitter.Equal(t, "a", "b")}\n");
            if (fn.Name.StartsWith("fmt", StringComparison.Ordinal)) sb.Append($"{fn.Name} {emitter.Format(t, "v", "w")}\n");
            if (fn.Name.StartsWith("map", StringComparison.Ordinal))
                foreach (var member in Members) sb.Append($"{fn.Name} {emitter.MapMethod(member, (MapType)t, "m", ["k", "d"], "L")}\n");
        }
        sb.Append($"hashes {((bool)Field(emitter, "_hashes")!).ToString().ToLowerInvariant()}\n== helpers\n");
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(emitter, "_helpers")!;
        foreach (var n in (List<string>)Field(emitter, "_helperOrder")!) sb.Append($"{n} {helpers[n].Proto} | {helpers[n].Body}\n");
        sb.Append("== formatinto\n");
        foreach (var name in new[] { "Plain", "One", "Two", "Both", "Wrong", "Static", "Fails", "ByValue", "NotBuf", "Three", "SpecRef", "Reverse", "StaticSpec", "FailsSpec", "SpecByValue", "SpecNotBuf" })
        {
            var s = c.EntryModule.Structs.First(x => x.Name == name);
            sb.Append($"{name} {Method(CEmitter.FormatInto(s))} {Method(CEmitter.FormatIntoWithSpec(s))}\n");
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corpus_MatchesCSharp(bool payload) => WithFixture(path =>
    {
        var expected = SelfhostSemanticsHarness.Sections(Oracle(Compile(path)));
        var (exit, stdout, stderr) = Run("corpus", "-", path);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        var actual = SelfhostSemanticsHarness.Sections(stdout);
        foreach (var section in new[] { "corpus", "helpers", "formatinto" })
            SelfhostSemanticsHarness.AssertSection(actual, section, expected[section]);
    }, payload);

    [Theory]
    [InlineData("failEq", "zbadEq")]
    [InlineData("failEq", "zbadEqBuf")]
    [InlineData("failFmt", "zbadFmt")]
    [InlineData("failFmt", "zbadFmtList")]
    [InlineData("failFmt", "zbadFmtBuf")]
    public void NoEqualityOrFormat_FailsLikeCSharp(string mode, string function) => WithFixture(path =>
    {
        var c = Compile(path);
        var emitter = NewEmitter(Lowerer.Lower(c));
        var t = Function(c, function).Parameters[0].Type;
        var e = Assert.Throws<InvalidOperationException>(() => _ = mode == "failEq" ? emitter.Equal(t, "a", "b") : emitter.Format(t, "v", "w"));
        var (exit, _, err) = Run(mode, function, path);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });
}
