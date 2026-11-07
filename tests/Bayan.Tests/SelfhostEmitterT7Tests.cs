using System.Collections;
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
/// 011 T7: <c>selfhost/emitter/cprint/types.byn</c> (CEmitter.cs 477-587, 605-681) and <c>cprint/own.byn</c> (682-778) against the C#
/// as an in-process oracle. The expected text is whatever the C# <c>CEmitter</c> prints for the same program; the emitter's sources but
/// <c>main.byn</c> are built with a generated <c>main.byn</c> that prints what the ported functions return (Q788).
/// Every section compares whole, with nothing masked (Q791).
/// </summary>
public sealed class SelfhostEmitterT7Tests
{
    // args: mode, then (thunk: the function and the local whose type it is a value of) bayan-check's.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CCtx = cprint.CCtx
        type CompilationOptions = semantics.CompilationOptions

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            let mode = args[0]
            let first = mode == "thunk" ? 3 : 1
            for i in first..args.length { rest.append(args[i]) }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            if mode == "corpus" {
                Io.stdout.write(t7PrintTypes(ref c))
                Io.stdout.write(t7PrintOwn(ref c))
                Io.stdout.write(t7PrintSuffix())
                Io.stdout.write(t7PrintDyn(ref c))
            }
            if mode == "thunk" { Io.stdout.write(t7PrintThunk(ref c, for: args[1], by: args[2])) }
            if mode == "vtablefail" { Io.stdout.write(t7VtableFail(ref c)) }
            if mode == "iface" { Io.stdout.write(t7PrintIface(ref c)) }
            if mode == "suffixfail" { Io.stdout.write(cprint.intSuffix(c.locals[c.functions[t7Func(ref c, for: "f")].parameters[0]].type)) }
            return 0
        }

        /// `Lowerer.Lower`'s filter: the functions it lowers, in order.
        List<FuncId> t7Functions(ref Ctx c) uses alloc {
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

        /// Each function's parameter, return and top-level local types.
        List<Type> t7Gather(ref Ctx c) uses alloc {
            var types = List<Type>()
            for fn in t7Functions(ref c) {
                for p in c.functions[fn].parameters { types.append(c.locals[p].type) }
                types.append(c.functions[fn].returnType)
                for s in c.bBlocks[c.functions[fn].body & 0xFFFFFFFF].statements {
                    if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl { types.append(c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].type) }
                }
            }
            return types
        }

        string t7Helpers(in CCtx cc) uses alloc {
            var sb = StrBuf()
            for n in cc.helperOrder { sb.append($"helper {n}\n{cc.helpers[n].body}") }
            return $"{sb.finish()}"
        }

        string t7PrintTypes(ref Ctx c) uses alloc {
            var cc = CCtx {}
            for t in t7Gather(ref c) { let _ = cprint.cType(ref cc, from: ref c, for: t) }
            return $"== types\n{cprint.emitTypes(ref cc, from: ref c)}"
        }

        string t7PrintOwn(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== own\n")
            for t in t7Gather(ref c) {
                let owns = cprint.ownsStorage(ref c, for: t)
                let claim = cprint.ownClaim(ref cc, from: ref c, for: t)
                let stored = cprint.ownSet(ref cc, from: ref c, for: t)
                let moved = cprint.ownMove(ref cc, from: ref c, for: t)
                let ret = cprint.ownReturn(ref cc, from: ref c, for: t)
                sb.append($"{owns} {claim} {stored} {moved} {ret}\n")
            }
            sb.append(t7Helpers(in cc))
            return $"{sb.finish()}"
        }

        string t7PrintSuffix() uses alloc {
            var sb = StrBuf()
            sb.append("== suffix\n")
            var kinds = List<semantics.PrimitiveKind>()
            kinds.append(.i8)
            kinds.append(.i16)
            kinds.append(.i32)
            kinds.append(.i64)
            kinds.append(.u8)
            kinds.append(.u16)
            kinds.append(.u32)
            kinds.append(.u64)
            kinds.append(.isize)
            kinds.append(.usize)
            kinds.append(.f32)
            kinds.append(.f64)
            kinds.append(.bool)
            kinds.append(.char)
            kinds.append(.string)
            kinds.append(.void)
            kinds.append(.neverKind)
            for k in kinds { sb.append($"{cprint.intSuffix(semantics.prim(k))}\n") }
            return $"{sb.finish()}"
        }

        string t7PrintDyn(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var sb = StrBuf()
            sb.append("== dyn\n")
            for m in 0..c.modules.length {
                var structs = List<semantics.StructId>()
                for s in c.modules[m].structs { structs.append(s) }
                for s in structs {
                    if semantics.structIsGeneric(ref c, by: s) { continue }
                    var ifaces = List<semantics.InterfaceId>()
                    for k in c.structs[s].conformances {
                        // An interface with an associated type or a type parameter has slots no C type exists for (C# throws too).
                        let i = c.conformances[k].iface
                        if c.interfaces[i].associatedTypes.length == 0 && c.interfaces[i].typeParams.length == 0 { ifaces.append(i) }
                    }
                    for i in ifaces {
                        sb.append($"vtab {cprint.vtable(ref cc, from: ref c, for: s, as: i)}\n")
                        sb.append($"again {cprint.vtable(ref cc, from: ref c, for: s, as: i)}\n")
                        var slots = List<semantics.FuncId>()
                        for slot in c.interfaces[i].methods { slots.append(slot) }
                        for slot in c.interfaces[i].properties { slots.append(slot) }
                        for slot in slots { sb.append($"slot {cprint.slotName(ref c, for: slot)}\n") }
                    }
                }
            }
            sb.append($"count {cc.vtableOrder.length}\n")
            for n in cc.vtableOrder { sb.append(cc.vtables[n]) }
            sb.append(t7Helpers(in cc))
            return $"{sb.finish()}"
        }

        FuncId t7Func(ref Ctx c, for string name) uses alloc {
            for fn in t7Functions(ref c) {
                if c.functions[fn].name == name { return fn }
            }
            return -1
        }

        string t7PrintThunk(ref Ctx c, for string name, by string local) uses alloc {
            var cc = CCtx {}
            let main = t7Func(ref c, for: "main")
            var type: Type = -1
            for s in c.bBlocks[c.functions[main].body & 0xFFFFFFFF].statements {
                if !ltree.isLowered(s) && semantics.boundKind(s) == .localDecl && c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].name == local {
                    type = c.locals[c.bLocalDecls[s & 0xFFFFFFFF].local].type
                }
            }
            let thunk = cprint.functionThunk(ref cc, from: ref c, for: t7Func(ref c, for: name), as: type)
            return $"== thunk\n{thunk}\n{t7Helpers(in cc)}"
        }

        /// `EmitTypes` after `CType` of every interface: a type no case of the switch matches.
        string t7PrintIface(ref Ctx c) uses alloc {
            var cc = CCtx {}
            for m in 0..c.modules.length {
                var ifaces = List<semantics.InterfaceId>()
                for k in c.modules[m].interfaces { ifaces.append(k) }
                for k in ifaces { let _ = cprint.cType(ref cc, from: ref c, for: semantics.makeType(.interfaceType, at: k)) }
            }
            return $"== types\n{cprint.emitTypes(ref cc, from: ref c)}"
        }

        /// `Vtable` of struct `S` and interface `I`, which `S` does not conform to.
        string t7VtableFail(ref Ctx c) uses alloc {
            var cc = CCtx {}
            var s: semantics.StructId = -1
            var i: semantics.InterfaceId = -1
            for m in 0..c.modules.length {
                for k in c.modules[m].structs {
                    if c.structs[k].name == "S" { s = k }
                }
                for k in c.modules[m].interfaces {
                    if c.interfaces[k].name == "IBox" { i = k }
                }
            }
            return cprint.vtable(ref cc, from: ref c, for: s, as: i)
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T7 main did not build:\n{error}");
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
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t7-" + Guid.NewGuid().ToString("N"));
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

    private static (int Exit, string Out, string Err) Run(params string[] args) => SelfhostParserTests.Run(Build(), args);

    private static object? Call(object? target, string name, params object?[] args) =>
        typeof(CEmitter).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static CEmitter NewEmitter(Compilation c) =>
        (CEmitter)Activator.CreateInstance(typeof(CEmitter), BindingFlags.Instance | BindingFlags.NonPublic, null, [Lowerer.Lower(c), false], null)!;

    private static string Flag(object? b) => (bool)b! ? "true" : "false";

    private static List<FunctionSymbol> Functions(Compilation c) =>
        c.Modules.SelectMany(m => m.AllFunctions).Where(fn => fn.Body is not null && fn.Kind != FunctionKind.Test && !fn.IsGeneric).ToList();

    /// <summary>Each function's parameter, return and top-level local types, as the generated main gathers them.</summary>
    private static List<TypeSymbol> Gather(Compilation c)
    {
        var types = new List<TypeSymbol>();
        foreach (var fn in Functions(c))
        {
            types.AddRange(fn.Parameters.Select(p => p.Type));
            types.Add(fn.ReturnType);
            types.AddRange(fn.Body!.Statements.OfType<BoundLocalDecl>().Select(d => d.Local.Type));
        }
        return types;
    }

    private static string Helpers(CEmitter e)
    {
        var helpers = (Dictionary<string, (string Proto, string Body)>)Field(e, "_helpers")!;
        var sb = new StringBuilder();
        foreach (var n in (List<string>)Field(e, "_helperOrder")!) sb.Append($"helper {n}\n{helpers[n].Body}");
        return sb.ToString();
    }

    private static string Section(string name, string text) => $"== {name}\n{text}";

    // ── The oracle ───────────────────────────────────────────────────────────────

    private static string TypesOracle(Compilation c)
    {
        var e = NewEmitter(c);
        foreach (var t in Gather(c)) e.CType(t);
        return Section("types", (string)Call(e, "EmitTypes")!);
    }

    private static string OwnOracle(Compilation c)
    {
        var e = NewEmitter(c);
        var sb = new StringBuilder();
        foreach (var t in Gather(c))
        {
            var owns = Flag(Call(null, "OwnsStorage", t));
            sb.Append($"{owns} {Call(e, "OwnClaim", t)} {Call(e, "OwnSet", t)} {Call(e, "OwnMove", t)} {Call(e, "OwnReturn", t)}\n");
        }
        sb.Append(Helpers(e));
        return Section("own", sb.ToString());
    }

    private static readonly PrimitiveType[] Primitives =
    [
        PrimitiveType.I8, PrimitiveType.I16, PrimitiveType.I32, PrimitiveType.I64, PrimitiveType.U8, PrimitiveType.U16, PrimitiveType.U32,
        PrimitiveType.U64, PrimitiveType.Isize, PrimitiveType.Usize, PrimitiveType.F32, PrimitiveType.F64, PrimitiveType.Bool, PrimitiveType.Char,
        PrimitiveType.String, PrimitiveType.Void, PrimitiveType.Never,
    ];

    private static string SuffixOracle() =>
        Section("suffix", string.Concat(Primitives.Select(p => $"{Call(null, "IntSuffix", p)}\n")));

    private static string DynOracle(Compilation c)
    {
        var e = NewEmitter(c);
        var sb = new StringBuilder();
        foreach (var s in c.Modules.SelectMany(m => m.Structs).Where(s => !s.IsGeneric))
        {
            foreach (var conformance in s.Conformances.Where(k => k.Interface.AssociatedTypes.Count == 0 && k.Interface.TypeParameters.Count == 0).ToList())
            {
                sb.Append($"vtab {Call(e, "Vtable", s, conformance.Interface)}\n");
                sb.Append($"again {Call(e, "Vtable", s, conformance.Interface)}\n");
                foreach (var slot in conformance.Interface.Methods.Concat(conformance.Interface.Properties)) sb.Append($"slot {CEmitter.SlotName(slot)}\n");
            }
        }
        var order = (List<string>)Field(e, "_vtableOrder")!;
        var vtables = (Dictionary<string, string>)Field(e, "_vtables")!;
        sb.Append($"count {order.Count}\n");
        foreach (var n in order) sb.Append(vtables[n]);
        sb.Append(Helpers(e));
        return Section("dyn", sb.ToString());
    }

    private static string Oracle(Compilation c) =>
        TypesOracle(c) + OwnOracle(c) + SuffixOracle() + DynOracle(c);

    private static void AssertSections(string name, string expected, string actual)
    {
        var e = SelfhostSemanticsHarness.Sections(expected);
        var a = SelfhostSemanticsHarness.Sections(actual);
        foreach (var (section, lines) in e)
        {
            if (section == "") continue;
            SelfhostEmitterHarness.AssertSame($"{name} {section}", string.Join('\n', lines), string.Join('\n', a.GetValueOrDefault(section) ?? []));
        }
    }

    // ── Cases ────────────────────────────────────────────────────────────────────

    /// <summary>No struct has a field: types, ownership and vtables compare whole.</summary>
    private const string PlainFixture = """
        union error Bad {
            bad
        }

        interface IBox {
            i64 n
        }

        struct E : IBox {
            i64 n => 1
        }

        struct Pt {
        }

        i64 f1(Map<string, i64> m) fails Bad => 0
        i64 f2(List<i64> l) => 0
        i64 f3(StrBuf b) => 0
        i64 f4(i64? o) => 0
        i64 f5(E e) => 0
        i64 f6((i64, bool) t) => 0
        i64 f7((i64) -> i64 fn) => 0
        i64 f8(dyn ref IBox d) => 0
        i64 f9(Owned<Pt> w) => 0
        i64 f10(List<Pt> ps) => 0
        Pt? f11(Pt p) => p
        void f12(Result<i64, Bad> r) { }
        void f13(i64[3] a) { }
        i64 f14(Span<i64> s) => 0

        void f15() {
            let local = (List<i64>(), Map<string, i64>())
        }

        void main() {
        }
        """;

    [Fact]
    public void Types_Own_Dyn_MatchCSharpOnPlainTypes() => WithFixture(PlainFixture, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var (exit, stdout, stderr) = Run("corpus", path);
        Assert.True(exit == 0, $"exited {exit}: {stderr}");
        AssertSections("plain", Oracle(c), stdout);
    });

    private const string SlotFixture = """
        union error Bad {
            bad
        }

        interface IProbe {
            void fill(ref List<i64> xs)
            void put(to out i64 g)
            i64 trying(i64 x) fails Bad
            never stop()
        }

        struct Probe : IProbe {
            void fill(ref List<i64> xs) { }
            void put(to out i64 g) { g = 1 }
            i64 trying(i64 x) fails Bad => x
            never stop() { panic("stop") }
        }

        struct Door {
            i64 id
            states {
                open
                shut
            }
        }

        i64 f1(Door d, from Untrusted<string> u, by Probe p) => 0

        void main() {
        }
        """;

    [Fact]
    public void Types_Own_Dyn_MatchCSharpOnStatesAndSlots() => WithFixture(SlotFixture, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var (exit, stdout, stderr) = Run("corpus", path);
        Assert.True(exit == 0, $"exited {exit}: {stderr}");
        AssertSections("slots", Oracle(c), stdout);
    });

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
            // The selfhost parser keeps a field rule that names another field on the field (C# moves it to the struct), so the checker gains a bool flag local: Q792.
            if (Path.GetFileName(corpus) == "positional-requires.byn") return;
            var (exit, stdout, stderr) = Run("corpus", path);
            if (exit != 0)
            {
                failures.Add($"{Path.GetFileName(corpus)}: exited {exit}: {stderr}");
                return;
            }
            try { AssertSections(Path.GetFileName(corpus), Oracle(c), stdout); }
            catch (Exception ex) { failures.Add(ex.Message); return; }
            Interlocked.Increment(ref compared);
        }));
        Assert.True(compared > 100, $"only {compared} corpus files compared:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(4))}");
        Assert.True(failures.IsEmpty, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Order(StringComparer.Ordinal).Take(8))}");
    }

    private const string ThunkFixture = """
        union error Bad {
            bad
        }

        i64 plain(i64 x) => x

        void unit() { }

        i64 failing(i64 x) fails Bad {
            fail .bad if x < 0
            return x
        }

        void failingUnit() fails Bad { }

        never halt() { panic("stop") }

        never haltFails() fails Bad { panic("stop") }

        void main() {
            let a: (i64) -> i64 = plain
            let b: () -> void = unit
            let c: (i64) -> i64 fails Bad = failing
            let d: () -> void fails Bad = failingUnit
            let e: () -> never = halt
            let g: () -> never fails Bad = haltFails
        }
        """;

    [Theory]
    [InlineData("plain", "a")]
    [InlineData("unit", "b")]
    [InlineData("failing", "c")]
    [InlineData("failingUnit", "d")]
    [InlineData("halt", "e")]
    [InlineData("haltFails", "g")]
    public void Thunk_MatchesCSharp(string function, string local) => WithFixture(ThunkFixture, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var e = NewEmitter(c);
        var fn = Functions(c).First(f => f.Name == function);
        var main = Functions(c).First(f => f.Name == "main");
        var type = main.Body!.Statements.OfType<BoundLocalDecl>().First(d => d.Local.Name == local).Local.Type;
        var expected = Section("thunk", $"{Call(e, "FunctionThunk", fn, type)}\n{Helpers(e)}");
        var (exit, stdout, stderr) = Run("thunk", function, local, path);
        Assert.True(exit == 0, $"exited {exit}: {stderr}");
        AssertSections($"thunk {function}", expected, stdout);
    });

    [Fact]
    public void Vtable_OfAStructThatDoesNotConformFailsLikeCSharp() => WithFixture("""
        interface IBox {
            i64 n
        }

        struct S {
        }

        void main() { }
        """, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var s = c.Modules.SelectMany(m => m.Structs).First(x => x.Name == "S");
        var i = c.Modules.SelectMany(m => m.Interfaces).First(x => x.Name == "IBox");
        var thrown = Assert.Throws<TargetInvocationException>(() => Call(NewEmitter(c), "Vtable", s, i));
        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        var (exit, stdout, _) = Run("vtablefail", path);
        Assert.NotEqual(0, exit);
        Assert.Equal("", stdout);
    });

    [Fact]
    public void Types_OfATypeNoCaseMatchesIsAnEmptyStructLikeCSharp() => WithFixture("""
        interface IBox {
            i64 n
        }

        void main() { }
        """, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var e = NewEmitter(c);
        foreach (var i in c.Modules.SelectMany(m => m.Interfaces)) e.CType(i);
        var expected = Section("types", (string)Call(e, "EmitTypes")!);
        var (exit, stdout, stderr) = Run("iface", path);
        Assert.True(exit == 0, $"exited {exit}: {stderr}");
        AssertSections("iface", expected, stdout);
    });

    [Fact]
    public void IntSuffix_OfANonPrimitiveFailsLikeCSharp() => WithFixture("""
        struct S {
        }

        void f(S s) { }

        void main() { }
        """, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var type = Functions(c).First(f => f.Name == "f").Parameters[0].Type;
        var thrown = Assert.Throws<TargetInvocationException>(() => Call(null, "IntSuffix", type));
        Assert.IsType<InvalidCastException>(thrown.InnerException);
        var (exit, stdout, _) = Run("suffixfail", path);
        Assert.NotEqual(0, exit);
        Assert.Equal("", stdout);
    });

    /// <summary>States and unions of unit variants only (no `u`) and with a payload (`u`).</summary>
    private const string StateFixture = """
        struct Lamp {
            states {
                on
                off
            }
        }

        struct Gate {
            states {
                closed
                open(i64 width)
            }
        }

        union Mode {
            fast
            slow
        }

        union Shape {
            dot
            square(i64 side)
        }

        i64 f1(Lamp l) => 0
        i64 f2(Gate g) => 0
        i64 f3(Mode m) => 0
        i64 f4(Shape s) => 0

        void main() {
        }
        """;

    [Fact]
    public void Types_Own_Dyn_MatchCSharpOnStatesAndUnions() => WithFixture(StateFixture, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, string.Join('\n', c.Diagnostics.Items));
        var (exit, stdout, stderr) = Run("corpus", path);
        Assert.True(exit == 0, $"exited {exit}: {stderr}");
        AssertSections("states", Oracle(c), stdout);
    });
}
