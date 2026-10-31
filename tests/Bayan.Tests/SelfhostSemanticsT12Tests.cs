using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T12: <c>selfhost/semantics/declare.byn</c> against the C# <c>Compilation</c>. Each case is a module directory
/// under <c>tmp/t12-cases/</c>, loaded and declared file by file (T11), then run through the T12 phases its flags
/// name, in <c>Declare</c>'s order: <c>v</c> DeclareVariants, <c>c</c> CheckComposition, <c>e</c>
/// DeclareEnumMembers, <c>u</c> DeclareUnionVariants, <c>p</c> DeclarePayloads, <c>m</c> DeclareMembers,
/// <c>s</c> DeclareSignature of each free function, <c>o</c> CheckOverloads. The oracle calls the same private methods by reflection and prints the same lines.
/// Diagnostics print as <c>code line:col message | label | help</c>.
/// </summary>
public sealed class SelfhostSemanticsT12Tests
{
    private static string CasesDir => Path.Combine(Golden.RepoRoot, "tmp", "t12-cases");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly (string Path, string Text)[] Files =
    [
        // CheckUnique and LocationOf: a second declaration of a name already taken by each kind of type, a
        // function and a constant, in this file and in another one.
        ("unique/a.byn", """
            struct T1 { }
            union error T2 {
                a
            }
            enum T3 {
                a
            }
            union T4 {
                a
            }
            interface T5 { }
            type T6 = i64
            type distinct T7 = i64
            const k = 1
            void fn1() { }
            struct T1 { }
            void T2() { }
            const T3 = 2
            struct fn1 { }
            struct k { }
            void T4() { }
            void T5() { }
            void T6() { }
            void T7() { }
            """),
        ("unique/b.byn", "struct T1 { }\nconst fn1 = 3\n"),
        // DeclareVariants, DeclarePayloads, CheckComposition.
        ("errors/main.byn", """
            union error E1 {
                a
                a(i64 q)
                @cases(1) b
                @foo c
                @exitCode(3) @exitCode(4) d
                @exitCode(5) @cases(2) @bar e
                Bad
                p(i64 x, string x, Missing y, i64 Z, bool w)
            }
            union error E2 { }
            union error A1 {
                x
                shared
            }
            union error B1 {
                y
                shared
            }
            union error C1 = A1, B1
            union error C2 = A1
            union error C3 = A1, A1
            union error C4 = Missing
            union error C5 = C1, B1
            """),
        // CheckComposition of an alias that names no set, in a module with no sets: it composes nothing.
        ("compose/main.byn", "union error C1 = Missing\n"),
        // DeclareEnumMembers, DeclareEnumFields, `T.Field`.
        ("enums/main.byn", """
            enum Color {
                red
                green
                red
                Blue
            }
            enum Empty { }
            enum Planet(ref i64 a, i64 b = 1, List<i64> c, i64 d, i64 d, i64 name, i64 ordinal, Color col, Planet2 p2, bool ok, rune r, string s, f64 f, Missing m, u8 n) {
                earth
            }
            enum Planet2(i64 mass) {
                x
            }
            struct R {
                i64 x requires it > 0
                i64 x
                i64 Y
            }
            enum Modes(move i64 a, out i64 b, R.Field rf) {
                z
            }
            """),
        // DeclareUnionVariants and UnionValue.
        ("unions/main.byn", """
            union U {
                a
                b = 10
                c = -5
                d = 0x20
                e = 10
                f(i64 x) = 1
                g = "text"
                h(i64 p, string p, Missing q)
                a
                i = 2147483648
                j = -2147483649
                k = 2147483647
                l = -2147483648
                m = 7u
                n = -x
                o = !1
                p2 = 40
                q = 39
                r
                Bad
            }
            union Empty { }
            union G<T> {
                one(T v)
                two(List<T> vs)
            }
            union W {
                a = 2147483647
                b
                c = -2147483648
                d
            }
            """),
        // DeclareMembers.
        ("structs/main.byn", """
            interface IShape { }
            interface IOther { }
            struct NotI { }
            struct S : IShape, IShape, IOther, Missing, NotI {
                required i64 r = 5
                required i64 r2
                i64 x
                i64 x
                public i64 pub
                private i64 priv
                i64 area => 42
                i64 area => 43
                public string label => "s"
                void m() { }
                void m() { }
                public static void sm() { }
                void g<T>() { }
                type Item = i64
                S() { }
                S() { }
                i64 m
                void Bad() { }
                i64 Bad2
            }
            struct G<T> {
                T v
                void h() { }
                void k<U>() { }
                void t<T>() { }
                List<T> all => [v]
                G() { }
            }
            struct E { }
            interface IG<T> { }
            struct H : IG<i64>, IG<i64> {
                public H() { }
            }
            """),
        // DeclareMembers of a `type distinct` struct: its one field `value`. One with `requires` reaches T38's
        // `declareCheck` stub (Q579).
        ("distinct/main.byn", """
            type distinct X = i64
            public type distinct P = List<string>
            type distinct M = Missing
            """),
        // CheckOverloads: the same name three times, across files, and a lone function.
        ("overloads/a.byn", "void f() { }\nvoid f() { }\nvoid f() { }\nvoid h() { }\nvoid g() { }\n"),
        ("overloads/b.byn", "void g() { }\nvoid h() { }\n"),
        // Overlap, with the free signatures declared: positional counts, required particles, defaults, `rest`. A `bool`
        // is positional to `Overlap` but exempt from the particle rule, so no signature reports anything.
        ("params/main.byn", """
            void p(i64 a) { }
            void p(i64 b) { }
            void p(i64 a, bool b) { }
            void q(by i64 x) { }
            void q(for i64 y) { }
            void q(by i64 z) { }
            void r(i64 a, by i64 b = 1) { }
            void r(i64 c) { }
            void s(rest ReadOnlySpan<i64> xs) { }
            void s(i64 a, bool b, bool c) { }
            void t(i64 a = 1) { }
            void t() { }
            void u(i64 a, for i64 b) { }
            void u(i64 a, bool b) { }
            void v(i64 a, for i64 b) { }
            void v(i64 c) { }
            void w(i64 c) { }
            void w(i64 a, for i64 b) { }
            void x(by i64 k = 1) { }
            void x(i64 a) { }
            """),
    ];

    private static readonly (string Name, string Flags)[] Runs =
    [
        ("unique", ""),
        ("errors", "vcp"),
        ("compose", "c"),
        ("enums", "e"),
        ("unions", "u"),
        ("structs", "m"),
        ("distinct", "m"),
        ("overloads", "o"),
        // `DeclareMembers` adds methods to `AllFunctions` before `CheckOverloads`, which skips them.
        ("structs-o", "mo"),
        ("params", "so"),
    ];

    private static string DirOf(string run) => Path.Combine(CasesDir, run.Split('-')[0]);

    private static readonly Lazy<Dictionary<string, List<string>>> Run = new(() =>
    {
        WriteFixtures();
        var args = new List<string>();
        foreach (var (name, flags) in Runs) args.AddRange([name, DirOf(name), flags == "" ? "-" : flags]);
        return SelfhostSemanticsHarness.RunSections(MainSource, [.. args]);
    });

    private static void WriteFixtures()
    {
        if (Directory.Exists(CasesDir)) Directory.Delete(CasesDir, recursive: true);
        foreach (var (path, text) in Files)
        {
            var full = Path.Combine(CasesDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text.EndsWith('\n') ? text : text + "\n", new UTF8Encoding(false));
        }
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var r in Runs) data.Add(r.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatchesCSharp(string section)
    {
        var sections = Run.Value;
        var run = Runs.Single(r => r.Name == section);
        SelfhostSemanticsHarness.AssertSection(sections, section, Expected(DirOf(run.Name), run.Flags));
    }

    // ── The C# oracle ───────────────────────────────────────────────────────────

    private static List<string> Expected(string dir, string flags)
    {
        if (!Directory.Exists(CasesDir)) WriteFixtures();
        var c = (Compilation)typeof(Compilation).GetConstructor(Any, [typeof(string), typeof(CompilationOptions), typeof(string)])!
            .Invoke([dir, new CompilationOptions(IncludeTests: false, Library: false), null]);
        var sources = Directory.EnumerateFiles(dir, "*.byn").Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList();
        var m = (ModuleSymbol)Call(c, "LoadModule", "main", dir, sources, null)!;
        typeof(Compilation).GetProperty("EntryModule")!.SetValue(c, m);
        foreach (var f in m.Files) Call(c, "DeclareFile", f);
        if (flags.Contains('v')) foreach (var set in m.ErrorSets) Call(c, "DeclareVariants", set);
        if (flags.Contains('c'))
        {
            var aliases = (System.Collections.IEnumerable)typeof(Compilation).GetField("_aliases", Any)!.GetValue(c)!;
            foreach (var alias in aliases.Cast<object>().ToList())
                if (alias.GetType().GetProperty("Decl")!.GetValue(alias) is ErrorSetDecl composed) Call(c, "CheckComposition", alias, composed);
        }
        if (flags.Contains('e')) foreach (var e in m.Enums) Call(c, "DeclareEnumMembers", e);
        if (flags.Contains('u')) foreach (var u in m.Unions) Call(c, "DeclareUnionVariants", u);
        if (flags.Contains('p')) foreach (var set in m.ErrorSets) Call(c, "DeclarePayloads", set);
        if (flags.Contains('m')) foreach (var s in m.Structs) Call(c, "DeclareMembers", s);
        if (flags.Contains('s'))
            foreach (var f in m.AllFunctions.Where(f => f.Kind == FunctionKind.Function).ToList()) Call(c, "DeclareSignature", f, f.Decl, f.File, null);
        if (flags.Contains('o')) Call(c, "CheckOverloads", m);
        return Dump(c, m);
    }

    private static object? Call(Compilation c, string method, params object?[] args) =>
        typeof(Compilation).GetMethod(method, Any)!.Invoke(c, args);

    private static string At(Location? l)
    {
        if (l is not { } loc) return "none";
        var (line, col) = loc.Source.GetLinePosition(loc.Span.Start);
        return $"{line}:{col}";
    }

    private static string Lower(object o) => o.ToString()!.ToLowerInvariant();

    /// <summary>Each local; a <c>ParameterSymbol</c> adds <c>#</c> and its ordinal.</summary>
    private static string Params(IEnumerable<LocalSymbol> ps) =>
        string.Join(",", ps.Select(p => $"{p.Name}:{p.Type.DisplayName}@{At(p.Location)}{(p is ParameterSymbol q ? $"#{q.Ordinal}" : "")}"));

    /// <summary>
    /// A function's T12 facts. A method's or free function's return type and parameters come from
    /// <c>DeclareSignature</c> (T38), and a builtin's <c>File</c> is null where the port sets it (Q578), so neither is printed.
    /// </summary>
    private static string Fn(FunctionSymbol f)
    {
        var keys = f.TypeMap is null ? "" : string.Join(",", f.TypeMap.Keys);
        var signature = f.Kind is FunctionKind.Method or FunctionKind.Function ? "" : $" ret={f.ReturnType.DisplayName} ({Params(f.Parameters)})";
        var file = f.Kind == FunctionKind.Builtin ? "" : $" file={f.File?.Source.Path ?? "none"}";
        return $"{f.Name} {Lower(f.Kind)} {Lower(f.Access)} static={f.IsStatic} at={At(f.Location)}{file} map=[{keys}]{signature}";
    }

    private static List<string> Dump(Compilation c, ModuleSymbol m)
    {
        var lines = new List<string>();
        foreach (var d in c.Diagnostics.Items)
        {
            var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
            lines.Add($"diag {d.Code} {line}:{col} {d.Message} | {d.Label} | {d.Help}");
        }
        foreach (var set in m.ErrorSets)
        {
            lines.Add($"set {set.Name}");
            foreach (var v in set.Variants) lines.Add($"  variant {v.Name} code={v.Code} at={At(v.Location)} ({Params(v.Fields)})");
        }
        var exitCodes = (System.Collections.IEnumerable)typeof(Compilation).GetField("_exitCodes", Any)!.GetValue(c)!;
        foreach (var x in exitCodes)
        {
            var v = (ErrorVariantSymbol)x.GetType().GetField("Item1")!.GetValue(x)!;
            var a = (AttributeSyntax)x.GetType().GetField("Item2")!.GetValue(x)!;
            var (line, col) = v.Location!.Value.Source.GetLinePosition(a.Span.Start);
            lines.Add($"exit {v.Set.Name}.{v.Name} {line}:{col}");
        }
        foreach (var e in m.Enums)
        {
            lines.Add($"enum {e.Name}");
            foreach (var x in e.Members) lines.Add($"  member {x.Name} ordinal={x.Ordinal} at={At(x.Location)} doc={x.Doc ?? "none"}");
            foreach (var x in e.Fields) lines.Add($"  field {x.Name} {x.Type.DisplayName} index={x.Index} at={At(x.Location)}");
            if (e.FromOrdinal is { } fo) lines.Add($"  fromOrdinal {Fn(fo)} {Lower(fo.Builtin)}");
            if (e.Parse is { } pa) lines.Add($"  parse {Fn(pa)} {Lower(pa.Builtin)}");
        }
        foreach (var u in m.Unions)
        {
            lines.Add($"union {u.Name}");
            foreach (var v in u.Variants) lines.Add($"  variant {v.Name} tag={v.Tag} at={At(v.Location)} ({Params(v.Fields)})");
        }
        var baseSpans = (System.Collections.IDictionary)typeof(Compilation).GetField("_baseSpans", Any)!.GetValue(c)!;
        foreach (var s in m.Structs)
        {
            lines.Add($"struct {s.Name}");
            foreach (var i in s.Bases)
            {
                var span = (TextSpan)baseSpans[(s, i)]!;
                var (line, col) = s.Location.Source.GetLinePosition(span.Start);
                lines.Add($"  base {i.Name} {line}:{col}+{span.Length}");
            }
            foreach (var f in s.Fields) lines.Add($"  field {f.Name} {f.Type.DisplayName} {Lower(f.Access)} required={f.IsRequired} default={f.HasDefault} at={At(f.Location)}");
            foreach (var p in s.Properties) lines.Add($"  property {p.Name} {p.Type.DisplayName} {Lower(p.Access)} at={At(p.Location)} getter={Fn(p.Getter)}");
            foreach (var f in s.Methods) lines.Add($"  method {Fn(f)}");
            foreach (var f in s.Constructors) lines.Add($"  constructor {Fn(f)}");
        }
        foreach (var (n, fs) in m.Functions) lines.Add($"fn {n} {fs.Count}");
        foreach (var f in m.AllFunctions) lines.Add($"all {Fn(f)}");
        return lines;
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private const string MainSource = """
        import lexer
        import parser

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 2 < args.length {
                let name = args[i]
                let dir = args[i + 1]
                let flags = args[i + 2]
                print($"== {name}")
                var c = loadOnly(dir)
                let m = c.entryModule
                for f in copyIds(in c.modules[m].files) { declareFile(ref c, for: f) }
                if flags.contains("v") {
                    for set in copyIds(in c.modules[m].errorSets) { declareVariants(ref c, for: set) }
                }
                if flags.contains("c") {
                    for alias in copyIds(in c.aliasOrder) {
                        let file = c.aliases[alias].file
                        switch c.files[file].unit.members[c.aliases[alias].declIndex] {
                            case .errorSetDecl(let composed):
                                checkComposition(ref c, for: alias, from: in composed.value)
                            default:
                                continue
                        }
                    }
                }
                if flags.contains("e") {
                    for e in copyIds(in c.modules[m].enums) { declareEnumMembers(ref c, for: e) }
                }
                if flags.contains("u") {
                    for u in copyIds(in c.modules[m].unions) { declareUnionVariants(ref c, for: u) }
                }
                if flags.contains("p") {
                    for set in copyIds(in c.modules[m].errorSets) { declarePayloads(ref c, for: set) }
                }
                if flags.contains("m") {
                    for s in copyIds(in c.modules[m].structs) { declareMembers(ref c, for: s) }
                }
                if flags.contains("s") { t12Signatures(ref c, for: m) }
                if flags.contains("o") { checkOverloads(ref c, for: m) }
                dump(ref c, for: m)
                i += 3
            }
            return 0
        }

        Ctx loadOnly(string dir) {
            var sources = List<lexer.SourceText>()
            for f in t11ModuleFiles(dir, includeTests: false) { sources.append(lexer.SourceText.create(f, from: t11ReadAllBytes(f))) }
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
                packageRoot: dir,
                displayRoot: dir,
            }
            c.entryModule = loadModule(ref c, for: "main", at: dir, from: move sources, by: -1)
            return c
        }

        string spot(in Ctx c, from FileId f, by TextSpan span) {
            let pos = linePosition(c.files[f].text.span, at: span.start)
            return $"{pos.line}:{pos.column}"
        }

        string lowerKind(FunctionKind k) {
            switch k {
                case .function: return "function"
                case .method: return "method"
                case .constructor: return "constructor"
                case .getter: return "getter"
                case .main: return "main"
                case .testKeyword: return "test"
                case .builtin: return "builtin"
                case .lambda: return "lambda"
                case .invoke: return "invoke"
            }
        }

        string lowerAccess(Accessibility a) {
            switch a {
                case .publicKeyword: return "public"
                case .privateKeyword: return "private"
                case .module: return "module"
            }
        }

        string truth(bool on) => on ? "True" : "False"

        /// Each parameter's location is in file `f`; a builtin's (C# `null`) has an empty span. `at=` prints `span`, the C# `Location`.
        string paramList(ref Ctx c, for in List<LocalId> ps, from FileId f) {
            var sb = StrBuf()
            for k in 0..ps.length {
                if k > 0 { sb.append(",") }
                let p = ps[k]
                let place = c.locals[p].span.length == 0 ? "none" : spot(in c, from: f, by: c.locals[p].span)
                let type = typeDisplay(ref c, by: c.locals[p].type)
                let ordinal = c.locals[p].isParameter ? $"#{c.locals[p].ordinal}" : ""
                sb.append($"{c.locals[p].name}:{type}@{place}{ordinal}")
            }
            return $"{sb.finish()}"
        }

        string fnLine(ref Ctx c, for FuncId f) {
            let file = c.functions[f].file
            var keys = StrBuf()
            var first = true
            for (k, _) in c.functions[f].typeMap {
                if !first { keys.append(",") }
                keys.append(k)
                first = false
            }
            let kind = c.functions[f].kind
            var signature = ""
            if kind != .method && kind != .function {
                let ret = typeDisplay(ref c, by: c.functions[f].returnType)
                let ps = paramList(ref c, for: in copyIds(in c.functions[f].parameters), from: file)
                signature = $" ret={ret} ({ps})"
            }
            let fileName = kind == .builtin ? "" : $" file={c.files[file].displayPath}"
            let keyList = keys.finish()
            return $"{c.functions[f].name} {lowerKind(kind)} {lowerAccess(c.functions[f].access)} static={truth(on: c.functions[f].isStatic)} at={spot(in c, from: file, by: c.functions[f].span)}{fileName} map=[{keyList}]{signature}"
        }

        void dump(ref Ctx c, for ModId m) {
            for d in c.diagnostics {
                let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
                print($"diag {d.code} {pos.line}:{pos.column} {d.message} | {d.label} | {d.help}")
            }
            for set in copyIds(in c.modules[m].errorSets) {
                print($"set {c.errorSets[set].name}")
                for v in copyIds(in c.errorSets[set].variants) {
                    let f = c.errorVariants[v].file
                    let ps = paramList(ref c, for: in copyIds(in c.errorVariants[v].fields), from: f)
                    print($"  variant {c.errorVariants[v].name} code={c.errorVariants[v].code} at={spot(in c, from: f, by: c.errorVariants[v].span)} ({ps})")
                }
            }
            for x in c.exitCodes {
                let v = x.variant
                let r = x.attribute
                switch c.files[r.file].unit.members[r.declIndex] {
                    case .errorEnumDecl(let d):
                        let span = d.value.variants[r.memberIndex].attributes[r.itemIndex].span
                        print($"exit {c.errorSets[c.errorVariants[v].set].name}.{c.errorVariants[v].name} {spot(in c, from: r.file, by: span)}")
                    default:
                        panic("an exit code is on an error variant")
                }
            }
            for e in copyIds(in c.modules[m].enums) {
                let f = c.enums[e].file
                print($"enum {c.enums[e].name}")
                for x in copyIds(in c.enums[e].members) {
                    let doc = c.enumMembers[x].doc ?? "none"
                    print($"  member {c.enumMembers[x].name} ordinal={c.enumMembers[x].ordinal} at={spot(in c, from: f, by: c.enumMembers[x].span)} doc={doc}")
                }
                for x in copyIds(in c.enums[e].fields) {
                    let type = typeDisplay(ref c, by: c.enumFields[x].type)
                    print($"  field {c.enumFields[x].name} {type} index={c.enumFields[x].index} at={spot(in c, from: f, by: c.enumFields[x].span)}")
                }
                if c.enums[e].fromOrdinal >= 0 {
                    let line = fnLine(ref c, for: c.enums[e].fromOrdinal)
                    print($"  fromOrdinal {line} {t12BuiltinWord(c.functions[c.enums[e].fromOrdinal].builtin)}")
                }
                if c.enums[e].parse >= 0 {
                    let line = fnLine(ref c, for: c.enums[e].parse)
                    print($"  parse {line} {t12BuiltinWord(c.functions[c.enums[e].parse].builtin)}")
                }
            }
            for u in copyIds(in c.modules[m].unions) {
                let f = c.unions[u].file
                print($"union {c.unions[u].name}")
                for v in copyIds(in c.unions[u].variants) {
                    let ps = paramList(ref c, for: in copyIds(in c.unionVariants[v].fields), from: f)
                    print($"  variant {c.unionVariants[v].name} tag={c.unionVariants[v].tag} at={spot(in c, from: f, by: c.unionVariants[v].span)} ({ps})")
                }
            }
            for s in copyIds(in c.modules[m].structs) {
                let f = c.structs[s].file
                print($"struct {c.structs[s].name}")
                for i in copyIds(in c.structs[s].bases) {
                    let span = c.baseSpans[$"{s} {i}"]
                    print($"  base {c.interfaces[i].name} {spot(in c, from: f, by: span)}+{span.length}")
                }
                for x in copyIds(in c.structs[s].fields) {
                    let type = typeDisplay(ref c, by: c.fields[x].type)
                    print($"  field {c.fields[x].name} {type} {lowerAccess(c.fields[x].access)} required={truth(on: (c.fields[x].modifiers & 16) != 0)} default={truth(on: c.fields[x].hasDefault)} at={spot(in c, from: f, by: c.fields[x].span)}")
                }
                for x in copyIds(in c.structs[s].properties) {
                    let type = typeDisplay(ref c, by: c.properties[x].type)
                    let getter = fnLine(ref c, for: c.properties[x].getter)
                    print($"  property {c.properties[x].name} {type} {lowerAccess(c.properties[x].access)} at={spot(in c, from: f, by: c.properties[x].span)} getter={getter}")
                }
                for x in copyIds(in c.structs[s].methods) {
                    let line = fnLine(ref c, for: x)
                    print($"  method {line}")
                }
                for x in copyIds(in c.structs[s].constructors) {
                    let line = fnLine(ref c, for: x)
                    print($"  constructor {line}")
                }
            }
            // `functions` in insertion order: every key enters `allFunctions` with it (a `for` over the map copies its lists).
            var seen = List<string>()
            for f in copyIds(in c.modules[m].allFunctions) {
                let n = c.functions[f].name
                if c.functions[f].kind == .function && !listContainsString(in seen, by: n) {
                    seen.append(n)
                    print($"fn {n} {c.modules[m].functions[n].length}")
                }
            }
            for f in copyIds(in c.modules[m].allFunctions) {
                let line = fnLine(ref c, for: f)
                print($"all {line}")
            }
        }

        /// What `overlap` reads of T38's `declareSignature` (a stub): each free function's parameters, named and in order.
        void t12Signatures(ref Ctx c, for ModId m) {
            for f in copyIds(in c.modules[m].allFunctions) {
                if c.functions[f].kind != .function { continue }
                let file = c.functions[f].file
                switch c.files[file].unit.members[c.functions[f].declIndex] {
                    case .functionDecl(let n):
                        for k in 0..n.value.params.length {
                            let name = n.value.params[k].name
                            let p = newLocal(ref c, by: LocalSymbol { name: $"{getText(c.files[file].text.span, at: name)}", nameSpan: name, span: name, kind: .parameter, isParameter: true, modifier: .none, ordinal: k })
                            c.functions[f].parameters.append(p)
                        }
                    default:
                        panic("a free function is declared by a `FunctionDecl`")
                }
            }
        }

        string t12BuiltinWord(BuiltinFunction b) {
            if b == .enumFromOrdinal { return "enumfromordinal" }
            if b == .enumParse { return "enumparse" }
            return "other"
        }
        """;
}
