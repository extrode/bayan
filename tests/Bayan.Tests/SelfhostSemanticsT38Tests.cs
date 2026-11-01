using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T38: <c>selfhost/semantics/declare2.byn</c> against the C# <c>Compilation</c>. Each case is a module directory
/// under <c>tmp/t38-cases/</c>, loaded and declared file by file (T11), then run through the phases its flags name,
/// in flag order: <c>u</c> DeclareUnionVariants, <c>m</c> DeclareMembers (each method's DeclareSignature,
/// DeclareCheck), <c>s</c> DeclareSignature of each free function, <c>i</c> DeclareInterfaceMembers, <c>t</c>
/// CheckStateNames, <c>h</c> CheckStructShape then CheckUnionShape. The oracle calls the same private methods by
/// reflection and prints the same lines. Diagnostics print as <c>code line:col message | label | help | fix</c>.
/// A struct with a <c>states</c> block reaches T12's <c>declareUnionVariants</c>, which reads only a
/// <c>UnionDecl</c> (Q689), so no case declares one.
/// </summary>
public sealed class SelfhostSemanticsT38Tests
{
    private static string CasesDir => Path.Combine(Golden.RepoRoot, "tmp", "t38-cases");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly (string Path, string Text)[] Files =
    [
        // DeclareSignature and DeclareParameters of free functions: `fails` with and without sets, repeats and
        // `except`, a non-set, `ref this` outside a struct, duplicate and badly named parameters.
        ("sigs/main.byn", """
            union error E1 {
                a
                b
            }
            union error E2 {
                c
            }
            void f1(i64 a, by string a) { }
            i64 f2() fails { return 1 }
            public void f3() fails { }
            void f4() fails E1, E1 { }
            void f5() fails E1 except .a, E1 { }
            void f6() fails E1 except .a, E1 except .b { }
            void f7() fails E1, E1 except .a { }
            void f8() fails i64 { }
            void f9() fails Missing { }
            void f10(ref this) { }
            void f11(Missing x) { }
            void f12(i64 Bad) { }
            void f13() fails E1 except .a, E2, E1 except .a { }
            void f14(move i64 m, out i64 o, ref i64 r) { }
            """),
        // DeclareInterfaceMembers: associated types, abstract and default methods, getters and properties,
        // `static`, duplicates, a generic method and another kind of member.
        ("ifaces/main.byn", """
            interface IB { }
            interface IA {
                type Item
                type Item
                type bad
                Item next()
                i64 count
                i64 twice => 2
                void d() { }
                i64 e() => 1
                void g<T>(T x)
                i64 count
                void next()
                void Bad()
                void p(ref this)
                public i64 f() fails
            }
            public interface IC {
                void q() fails
            }
            """),
        // DeclareMembers (each method's signature, `requires` checks), CheckStateNames on structs without states,
        // CheckStructShape and CheckUnionShape.
        ("structs/main.byn", """
            struct S {
                i64 x requires it > 0
                i64 y requires it < 9
                static i64 sf
                static const i64 sc = 1
                void m1(ref this) { }
                static void m2(ref this) { }
                void m3() becomes .open { }
                void m4(move this is .closed, i64 a) becomes .open { }
                void m5(this is .closed) becomes .open { }
                void m6(i64 a) becomes .open { }
                void m7(move this) becomes .open { }
                void m8(ref this) becomes .open { }
                static void m9() becomes .open { }
                void m10(this is .closed) { }
                void m11(this is .a, .b) becomes .open { }
            }
            struct R {
                i64 y
                requires y >= 0
            }
            struct G<T> {
                T v requires it > 0
            }
            type distinct P = i64 requires it > 0
            struct Rec {
                Rec inner
            }
            struct A1 {
                B1 b
            }
            struct B1 {
                A1 a
            }
            struct O {
                O? o
            }
            struct Tu {
                (i64, Tu) t
            }
            struct Bo {
                ReadOnlySpan<i64> s
                string st
            }
            struct Ok {
                List<Ok> xs
            }
            union U {
                a(U inner)
                b(ReadOnlySpan<i64> s)
                c(string t)
            }
            union V {
                a(W w)
            }
            struct W {
                V v
            }
            """),
    ];

    private static readonly (string Name, string Flags)[] Runs =
    [
        ("sigs", "s"),
        ("ifaces", "i"),
        ("structs", "umth"),
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
        foreach (var flag in flags)
        {
            switch (flag)
            {
                case 'u': foreach (var u in m.Unions) Call(c, "DeclareUnionVariants", u); break;
                case 'm': foreach (var s in m.Structs) Call(c, "DeclareMembers", s); break;
                case 's':
                    foreach (var f in m.AllFunctions.Where(f => f.Kind == FunctionKind.Function).ToList()) Call(c, "DeclareSignature", f, f.Decl, f.File, null);
                    break;
                case 'i': foreach (var i in m.Interfaces) Call(c, "DeclareInterfaceMembers", i); break;
                case 't': foreach (var s in m.Structs) Call(c, "CheckStateNames", s); break;
                case 'h':
                    foreach (var s in m.Structs) Call(c, "CheckStructShape", s);
                    foreach (var u in m.Unions) Call(c, "CheckUnionShape", u);
                    break;
            }
        }
        return Dump(c, m);
    }

    private static object? Call(Compilation c, string method, params object?[] args) =>
        typeof(Compilation).GetMethod(method, Any)!.Invoke(c, args);

    private static string At(SourceText source, int start)
    {
        var (line, col) = source.GetLinePosition(start);
        return $"{line}:{col}";
    }

    private static string Lower(object o) => o.ToString()!.ToLowerInvariant();

    private static string Params(IEnumerable<LocalSymbol> ps) =>
        string.Join(",", ps.Select(p => $"{p.Name}:{p.Type.DisplayName}@{At(p.Location!.Value.Source, p.Location!.Value.Span.Start)}{(p is ParameterSymbol q ? $"#{q.Ordinal}:{Lower(q.Modifier)}" : "")}"));

    private static string Set(ErrorSetSymbol set) =>
        $"{set.Name}{{{string.Join(",", set.Variants.Select(v => $"{v.Name}({Params(v.Fields)})"))}}}";

    private static string Fn(FunctionSymbol f)
    {
        var keys = f.TypeMap is null ? "" : string.Join(",", f.TypeMap.Keys);
        var at = f.Location is { } l ? At(l.Source, l.Span.Start) : "none";
        return $"{f.Name} {Lower(f.Kind)} {Lower(f.Access)} static={f.IsStatic} abstract={f.IsAbstract} at={at} ret={f.ReturnType.DisplayName} ({Params(f.Parameters)})"
            + $" fails={f.Fails} infers={f.InfersErrors} mutating={f.IsMutating} sets=[{string.Join(",", f.ErrorSets.Select(Set))}]"
            + $" excluded=[{string.Join(",", f.ExcludedErrors.Select(v => $"{v.Set.Name}.{v.Name}"))}] tparams=[{string.Join(",", f.TypeParameters.Select(t => t.DisplayName))}] map=[{keys}]";
    }

    private static List<string> Dump(Compilation c, ModuleSymbol m)
    {
        var lines = new List<string>();
        foreach (var d in c.Diagnostics.Items)
        {
            var fix = d.Fix is { } x ? $"{At(d.Source!, x.Span.Start)}+{x.Span.Length} \"{x.Replacement}\"" : "none";
            lines.Add($"diag {d.Code} {At(d.Source!, d.Span.Start)} {d.Message} | {d.Label} | {d.Help} | {fix}");
        }
        foreach (var i in m.Interfaces)
        {
            lines.Add($"interface {i.Name}");
            foreach (var t in i.AssociatedTypes) lines.Add($"  associated {t.DisplayName}");
            foreach (var f in i.Methods) lines.Add($"  method {Fn(f)}");
            foreach (var f in i.Properties) lines.Add($"  property {Fn(f)}");
        }
        var checks = (System.Collections.IDictionary)typeof(Compilation).GetProperty("Checks", Any)!.GetValue(c)!;
        foreach (var s in m.Structs)
        {
            lines.Add($"struct {s.Name}");
            foreach (var f in s.Methods) lines.Add($"  method {Fn(f)}");
            if (checks.Contains(s))
            {
                var check = (FunctionSymbol)checks[s]!.GetType().GetProperty("Check")!.GetValue(checks[s])!;
                lines.Add($"  check {Fn(check)}");
            }
        }
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
                for k in 0..flags.length {
                    let flag = flags[k..k + 1]
                    if flag == "u" {
                        for u in copyIds(in c.modules[m].unions) { declareUnionVariants(ref c, for: u) }
                    } else if flag == "m" {
                        for s in copyIds(in c.modules[m].structs) { declareMembers(ref c, for: s) }
                    } else if flag == "s" {
                        freeSignatures(ref c, for: m)
                    } else if flag == "i" {
                        for x in copyIds(in c.modules[m].interfaces) { declareInterfaceMembers(ref c, for: x) }
                    } else if flag == "t" {
                        for s in copyIds(in c.modules[m].structs) { checkStateNames(ref c, for: s) }
                    } else if flag == "h" {
                        for s in copyIds(in c.modules[m].structs) { checkStructShape(ref c, for: s) }
                        for u in copyIds(in c.modules[m].unions) { checkUnionShape(ref c, for: u) }
                    }
                }
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

        void freeSignatures(ref Ctx c, for ModId m) {
            for f in copyIds(in c.modules[m].allFunctions) {
                if c.functions[f].kind != .function { continue }
                let file = c.functions[f].file
                switch c.files[file].unit.members[c.functions[f].declIndex] {
                    case .functionDecl(let n):
                        declareSignature(ref c, for: f, from: in n.value, by: file)
                    default:
                        panic("a free function is declared by a `FunctionDecl`")
                }
            }
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

        string lowerModifier(parser.ParamModifier p) {
            switch p {
                case .none: return "none"
                case .refKeyword: return "ref"
                case .moveKeyword: return "move"
                case .outKeyword: return "out"
                case .inKeyword: return "in"
            }
        }

        string truth(bool on) => on ? "True" : "False"

        /// Each parameter's location is in file `f`.
        string paramList(ref Ctx c, for in List<LocalId> ps, from FileId f) {
            var sb = StrBuf()
            for k in 0..ps.length {
                if k > 0 { sb.append(",") }
                let p = ps[k]
                let type = typeDisplay(ref c, by: c.locals[p].type)
                let ordinal = c.locals[p].isParameter ? $"#{c.locals[p].ordinal}:{lowerModifier(c.locals[p].modifier)}" : ""
                sb.append($"{c.locals[p].name}:{type}@{spot(in c, from: f, by: c.locals[p].span)}{ordinal}")
            }
            return $"{sb.finish()}"
        }

        string setText(ref Ctx c, for ErrorSetId set) {
            var sb = StrBuf()
            sb.append(c.errorSets[set].name)
            sb.append("{")
            var first = true
            for v in copyIds(in c.errorSets[set].variants) {
                if !first { sb.append(",") }
                first = false
                let ps = paramList(ref c, for: in copyIds(in c.errorVariants[v].fields), from: c.errorVariants[v].file)
                sb.append($"{c.errorVariants[v].name}({ps})")
            }
            sb.append("}")
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
            let keyList = keys.finish()
            var sets = StrBuf()
            first = true
            for s in copyIds(in c.functions[f].errorSets) {
                if !first { sets.append(",") }
                first = false
                sets.append(setText(ref c, for: s))
            }
            let setList = sets.finish()
            var excluded = StrBuf()
            first = true
            for v in copyIds(in c.functions[f].excludedErrors) {
                if !first { excluded.append(",") }
                first = false
                excluded.append($"{c.errorSets[c.errorVariants[v].set].name}.{c.errorVariants[v].name}")
            }
            let excludedList = excluded.finish()
            var tparams = StrBuf()
            first = true
            for t in copyIds(in c.functions[f].typeParams) {
                if !first { tparams.append(",") }
                first = false
                tparams.append(c.typeParams[t].name)
            }
            let tparamList = tparams.finish()
            let fn = c.functions[f].name
            let kind = lowerKind(c.functions[f].kind)
            let access = lowerAccess(c.functions[f].access)
            let ret = typeDisplay(ref c, by: c.functions[f].returnType)
            let ps = paramList(ref c, for: in copyIds(in c.functions[f].parameters), from: file)
            return $"{fn} {kind} {access} static={truth(on: c.functions[f].isStatic)} abstract={truth(on: c.functions[f].isAbstract)} at={spot(in c, from: file, by: c.functions[f].span)} ret={ret} ({ps}) fails={truth(on: c.functions[f].hasFails)} infers={truth(on: c.functions[f].infersErrors)} mutating={truth(on: c.functions[f].isMutating)} sets=[{setList}] excluded=[{excludedList}] tparams=[{tparamList}] map=[{keyList}]"
        }

        void dump(ref Ctx c, for ModId m) {
            for d in c.diagnostics {
                let fix = d.hasFix ? $"{spot(in c, from: d.file, by: d.fixSpan)}+{d.fixSpan.length} \"{d.fixReplacement}\"" : "none"
                print($"diag {d.code} {spot(in c, from: d.file, by: d.span)} {d.message} | {d.label} | {d.help} | {fix}")
            }
            for x in copyIds(in c.modules[m].interfaces) {
                print($"interface {c.interfaces[x].name}")
                for t in copyIds(in c.interfaces[x].associatedTypes) { print($"  associated {c.typeParams[t].name}") }
                for f in copyIds(in c.interfaces[x].methods) {
                    let line = fnLine(ref c, for: f)
                    print($"  method {line}")
                }
                for f in copyIds(in c.interfaces[x].properties) {
                    let line = fnLine(ref c, for: f)
                    print($"  property {line}")
                }
            }
            for s in copyIds(in c.modules[m].structs) {
                print($"struct {c.structs[s].name}")
                for f in copyIds(in c.structs[s].methods) {
                    let line = fnLine(ref c, for: f)
                    print($"  method {line}")
                }
                if (c.checksOf.tryGet(at: s) is { } ci) {
                    let line = fnLine(ref c, for: c.checks[ci].check)
                    print($"  check {line}")
                }
            }
            for f in copyIds(in c.modules[m].allFunctions) {
                let line = fnLine(ref c, for: f)
                print($"all {line}")
            }
        }
        """;
}
