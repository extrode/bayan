using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T11: <c>selfhost/semantics/compile.byn</c> against the C# <c>Compilation</c>. The fixture trees below are
/// written to <c>tmp/t11-cases/</c>; one generated <c>main.byn</c> runs every case and prints a section per
/// case. The oracle runs the same case through <c>Compilation.FromPath</c>/<c>FromProject</c>/<c>Create</c>,
/// or, for one phase, through the private <c>LoadModule</c>, <c>DeclareFile</c> and <c>Declare</c> by
/// reflection, and prints the same lines. Diagnostics print as <c>code path line:col message | label | help</c>.
/// </summary>
public sealed class SelfhostSemanticsT11Tests
{
    private static string CasesDir => Path.Combine(Golden.RepoRoot, "tmp", "t11-cases");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Fixture trees: each file's path under <see cref="CasesDir"/> and its text.</summary>
    private static readonly (string Path, string Text)[] Files =
    [
        // Imports: a cycle (a → b.c → a), an unknown module, a repeated alias, a module with a syntax error.
        ("imports/app/main.byn", "import a\nimport b.c\nimport a as x\nimport a\nimport nope.deep\nimport d\n"),
        ("imports/app/zz.byn", "import a\nimport e\n"),
        ("imports/app/notes.txt", "not a source\n"),
        ("imports/app/main.test.byn", "import e\nimport f\n"),
        ("imports/app/a/a1.byn", "import b.c\n"),
        ("imports/app/a/a2.byn", "struct A { }\n"),
        ("imports/app/a/a.test.byn", "import f\n"),
        ("imports/app/b/c/c.byn", "import a\n"),
        ("imports/app/d/d.byn", "import e\nstruct {\n"),
        ("imports/app/e/e.byn", "struct E { }\n"),
        ("imports/app/f/f.byn", "struct F { }\n"),
        ("imports/app/f/f.test.byn", "struct {\n"),
        // A project: `src/app` is the entry module, imports resolve under `src`, tests join with tests on.
        ("proj/package.byn", "Package { name: \"local/proj\", version: \"0.1.0\" }\n"),
        ("proj/src/app/main.byn", "import lib\nimport lib.sub\n"),
        ("proj/src/app/z.test.byn", "import lib\n"),
        ("proj/src/lib/l.byn", "struct L { }\n"),
        ("proj/src/lib/sub/s.byn", "struct {\n"),
        ("proj/tests/b.test.byn", "import lib\n"),
        ("proj/tests/a.test.byn", "import nope\n"),
        ("proj/tests/notes.byn", "struct N { }\n"),
        // Declarations of every kind, with the diagnostics `DeclareFile` reports.
        ("decls/one.byn", """
            struct S<T, T> {
                i64 x
            }
            struct R {
                i64 x requires it > 0
            }
            struct Q {
                i64 y
                requires y >= 0
            }
            struct G<T> {
                i64 x requires it > 0
            }
            @positional
            struct P {
                i64 x
            }
            @positional(strict)
            struct P2 {
                i64 x
            }
            @positional
            struct Door {
                string label

                states {
                    open
                    closed
                }
            }
            @sealed because "x"
            struct Sealed {
                @exitCode(1)
                i64 x
            }
            @sealed
            struct Sealed2 {
                i64 x
            }
            @sealed
            enum Color {
                red
            }
            @exitCode(2)
            struct E2 { }
            @whatever
            union U<A> : IShape {
                a
            }
            enum Color2 : IShape, IShape where T : IShape {
                red
            }
            interface IShape { }
            interface IEnumerator { }
            interface IBox<B, B> { }
            extension Cents
            extension Money : IShape, IBox<Money> {
                i64 area() => 0
            }
            type distinct Sku = string
            type distinct Sku = i64
            type Alias1 = i64
            struct Alias2 { }
            type Alias2 = string
            union error Errs {
                boom
            }
            union error Errs2 = Errs, IoError
            enum Errs3 {
                a
            }
            union error Errs3 = Errs
            void f(i64 x) { }
            void f(string s) { }
            struct f { }
            const k = 1
            const k = 2
            void k() { }
            void g<A, A>() { }
            void main() { }
            void main() { }
            let x = 1
            test "plain" { }
            test "cased" (i64 n, bool b) { }
            @cases(1, 2) test "two" (i64 n) { }
            @cases((1, true), (2), n, a.b, (3, x), (-4, false), (+5.5, null), ((6), .a), (-x, true), (!true, false)) test "pairs" (i64 n, bool b) { }
            @cases(1) @cases(2) test "dup" (i64 n) { }
            @cases(1) test "noparams" { }
            @cases((1, 2)) test "one" (i64 n) { }
            """),
        ("decls/two.byn", "let y = 2\nstruct S { }\n"),
        // Top-level statements in two entry files, a test file and an imported module.
        ("stmts/a.byn", "import m\nlet x = 1\nlet x2 = 2\n"),
        ("stmts/b.byn", "let y = 2\n"),
        ("stmts/c.test.byn", "let z = 3\n"),
        ("stmts/m/m.byn", "let w = 1\nconst c = 1\n"),
        // `Declare`'s deferred work: `where` clauses and `Map` key checks.
        ("wheres/main.byn", """
            interface IShape { }
            interface IOrder<T> { }
            struct Point {
                i64 x
            }
            struct Box<T> where T : IShape, IOrder<T> where U : IShape {
                i64 n
            }
            struct NoParams where T : IShape {
                i64 n
            }
            enum Color where T : IShape {
                red
            }
            union Maybe<T> where T : Point {
                none
            }
            interface IWrap<T> where T : Missing { }
            void f<T>(T a) where T : IShape, T { }
            void g() where Q : IShape { }
            struct Key {
                i64 x
            }
            struct Bad {
                f64 x
            }
            struct Holder {
                Map<Key, i64> good
                Map<Bad, i64> bad
            }
            """),
        // `main`'s signature, one program per case.
        ("main-i32/main.byn", "i32 main() => 0\n"),
        ("main-void-args/main.byn", "void main(ReadOnlySpan<string> args) { }\n"),
        ("main-string/main.byn", "string main() => \"\"\n"),
        ("main-i64-param/main.byn", "void main(i64 n) { }\n"),
        ("main-ref-args/main.byn", "void main(ref ReadOnlySpan<string> args) { }\n"),
        ("main-span/main.byn", "void main(Span<string> args) { }\n"),
        ("main-two/main.byn", "void main(ReadOnlySpan<string> args, i64 n) { }\n"),
        ("main-i64-span/main.byn", "i32 main(ReadOnlySpan<i64> args) => 0\n"),
        ("unit/main.byn", "let x = 1\n"),
        // No syntax error: `Create` goes on to `Declare` and `BindAll`.
        ("clean/lib.byn", "import util\nstruct A {\n    i64 x\n}\nstruct A { }\n"),
        ("clean/util/u.byn", "struct U { }\n"),
        ("inst/main.byn", "struct S { }\n"),
    ];

    /// <summary>The runs: kind, case name, the path given (under <see cref="CasesDir"/>) and flags.</summary>
    private static readonly (string Kind, string Name, string Path, string Flags)[] Runs =
    [
        ("path", "imports-dir", "imports/app", ""),
        ("path", "imports-dir-tests", "imports/app", "t"),
        ("path", "imports-file", "imports/app/main.byn", ""),
        ("path", "imports-file-whole", "imports/app/main.byn", "w"),
        ("path", "imports-file-whole-tests", "imports/app/zz.byn", "wt"),
        ("path", "imports-test-file", "imports/app/main.test.byn", "w"),
        ("path", "clean", "clean", "l"),
        ("project", "proj", "proj", ""),
        ("project", "proj-tests", "proj", "t"),
        ("declfile", "decls", "decls", ""),
        ("declfile", "decls-tests", "decls", "t"),
        ("declfile", "stmts", "stmts", "t"),
        ("declare", "wheres", "wheres", ""),
        ("mainsig", "main-i32", "main-i32", "i32:"),
        ("mainsig", "main-void-args", "main-void-args", "void:ros"),
        ("mainsig", "main-string", "main-string", "string:"),
        ("mainsig", "main-i64-param", "main-i64-param", "void:i64"),
        ("mainsig", "main-ref-args", "main-ref-args", "void:refros"),
        ("mainsig", "main-span", "main-span", "void:span"),
        ("mainsig", "main-two", "main-two", "void:ros,i64"),
        ("mainsig", "main-i64-span", "main-i64-span", "i32:roi64"),
        ("unit", "assoc", "unit", ""),
        ("unit", "sink", "unit", ""),
        ("unit", "isinstance", "inst", ""),
    ];

    private static readonly Lazy<Dictionary<string, List<string>>> Run = new(() =>
    {
        WriteFixtures();
        var args = new List<string>();
        foreach (var (kind, name, path, flags) in Runs)
            args.AddRange([kind, name, Path.Combine(CasesDir, path), flags]);
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
        foreach (var r in Runs) data.Add($"{r.Kind} {r.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatchesCSharp(string section)
    {
        var sections = Run.Value;
        var run = Runs.Single(r => $"{r.Kind} {r.Name}" == section);
        SelfhostSemanticsHarness.AssertSection(sections, section, Expected(run.Kind, run.Name, Path.Combine(CasesDir, run.Path), run.Flags));
    }

    // ── The C# oracle ───────────────────────────────────────────────────────────

    private static List<string> Expected(string kind, string name, string path, string flags)
    {
        if (!Directory.Exists(CasesDir)) WriteFixtures();
        var options = new CompilationOptions(IncludeTests: flags.Contains('t'), Library: flags.Contains('l'));
        var lines = new List<string>();
        switch (kind)
        {
            case "path":
                DumpLoad(Compilation.FromPath(path, options, wholeDirectory: flags.Contains('w')), lines);
                break;
            case "project":
                DumpLoad(Compilation.FromProject(path, "app", options), lines);
                break;
            case "declfile":
            {
                var c = LoadOnly(path, options);
                foreach (var m in c.Modules)
                    foreach (var f in m.Files)
                        Call(c, "DeclareFile", f);
                DumpLoad(c, lines);
                DumpDecls(c, lines);
                break;
            }
            case "declare":
            {
                var c = LoadOnly(path, options);
                Call(c, "Declare");
                DumpLoad(c, lines);
                DumpDeclare(c, lines);
                break;
            }
            case "mainsig":
            {
                var sources = Directory.GetFiles(path, "*.byn").Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList();
                var c = Compilation.Create(sources, path, options);
                lines.Add("main checked");
                foreach (var d in c.Diagnostics.Items.Where(d => d.Code == DiagnosticCodes.InvalidMainSignature.Code)) lines.Add(Diag(d));
                break;
            }
            case "unit" when name == "assoc":
            {
                var c = New(path, options, null);
                TypeParameterType Assoc(TypeParameterType owner, string n) =>
                    (TypeParameterType)typeof(Compilation).GetMethod("AssociatedType", Any)!.Invoke(c, [owner, n])!;
                var t0 = new TypeParameterType("T");
                var t1 = new TypeParameterType("T");
                var a = Assoc(t0, "Item");
                var b = Assoc(t0, "Item");
                var d = Assoc(t1, "Item");
                var e = Assoc(t0, "Other");
                static string Same(TypeParameterType x, TypeParameterType y) => ReferenceEquals(x, y) ? "same" : "new";
                lines.Add($"assoc {a.Name} {Same(a, b)} {d.Name} {Same(a, d)} {e.Name} {Same(a, e)}");
                break;
            }
            case "unit" when name == "sink":
            {
                var c = New(path, options, null);
                var source = SourceText.FromString("sink.byn", "x");
                var report = typeof(Compilation).GetMethod("Report", Any)!;
                var into = typeof(Compilation).GetMethod("ReportInto", Any)!;
                var bag = new DiagnosticBag();
                var outer = (DiagnosticBag?)into.Invoke(c, [bag]);
                report.Invoke(c, [DiagnosticCodes.UnknownAttribute, source, new TextSpan(0, 1), new object?[] { "a" }, null, null, null]);
                report.Invoke(c, [DiagnosticCodes.UnknownAttribute, source, new TextSpan(0, 1), new object?[] { "b" }, null, null, null]);
                into.Invoke(c, [outer]);
                report.Invoke(c, [DiagnosticCodes.UnknownAttribute, source, new TextSpan(0, 1), new object?[] { "c" }, null, null, null]);
                lines.Add($"sink bag {bag.Count} main {c.Diagnostics.Count} outer {(outer is null ? "none" : "bag")}");
                foreach (var d in bag.Items) lines.Add($"bag {d.Message}");
                foreach (var d in c.Diagnostics.Items) lines.Add($"main {d.Message}");
                break;
            }
            case "unit" when name == "isinstance":
            {
                var c = LoadOnly(path, options);
                foreach (var f in c.EntryModule.Files) Call(c, "DeclareFile", f);
                var s = c.EntryModule.Structs[0];
                var instance = new StructSymbol(s.Name, s.Location, s.Module, s.Access, s.Decl, s.File) { Template = s };
                FunctionSymbol Fn() => new("f", null, c.EntryModule, FunctionKind.Function);
                var plain = Fn();
                var copy = new FunctionSymbol("f", null, c.EntryModule, FunctionKind.Function) { Template = plain };
                var method = Fn();
                method.Owner = s;
                var instanceMethod = Fn();
                instanceMethod.Owner = instance;
                var inInstance = Fn();
                inInstance.Enclosing = instanceMethod;
                var inTemplate = Fn();
                inTemplate.Enclosing = method;
                lines.Add(Words("isinstance", new[] { plain, copy, method, instanceMethod, inInstance, inTemplate }.Select(f => f.IsInstance ? "yes" : "no")));
                break;
            }
            default:
                throw new InvalidOperationException(kind);
        }
        return lines;
    }

    private static Compilation New(string root, CompilationOptions options, string? displayRoot) =>
        (Compilation)typeof(Compilation).GetConstructor(Any, [typeof(string), typeof(CompilationOptions), typeof(string)])!
            .Invoke([root, options, displayRoot]);

    private static object? Call(Compilation c, string method, params object?[] args) =>
        typeof(Compilation).GetMethod(method, Any)!.Invoke(c, args);

    /// <summary>The first half of <c>Create</c>: the entry module loaded, nothing declared.</summary>
    private static Compilation LoadOnly(string dir, CompilationOptions options)
    {
        var c = New(dir, options, null);
        var files = Directory.EnumerateFiles(dir, "*.byn")
            .Where(f => options.IncludeTests || !f.EndsWith(".test.byn", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList();
        var entry = (ModuleSymbol)Call(c, "LoadModule", "main", dir, files, null)!;
        typeof(Compilation).GetProperty("EntryModule")!.SetValue(c, entry);
        return c;
    }

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"diag {d.Code} {d.Source!.Path} {line}:{col} {d.Message} | {d.Label} | {d.Help}";
    }

    private static void DumpLoad(Compilation c, List<string> lines)
    {
        foreach (var m in c.Modules)
        {
            lines.Add($"module {m.Name} {m.Directory} {m.CName}");
            foreach (var f in m.Files)
            {
                lines.Add($"file {f.Source.Path} {(f.IsTestFile ? "test" : "source")}");
                foreach (var (alias, imported) in f.Imports) lines.Add($"import {alias} {imported.Name}");
            }
        }
        foreach (var d in c.Diagnostics.Items) lines.Add(Diag(d));
    }

    private static string KindOf(TypeSymbol t) => t switch
    {
        StructSymbol => "struct", EnumSymbol => "enum", UnionSymbol => "union", InterfaceSymbol => "interface",
        ErrorSetSymbol => "errorset", _ when t.GetType().Name == "AliasPlaceholder" => "alias", _ => "other",
    };

    private static void DumpDecls(Compilation c, List<string> lines)
    {
        foreach (var m in c.Modules)
        {
            lines.Add($"decls {m.Name}");
            foreach (var (n, t) in m.Types) lines.Add($"type {n} {KindOf(t)}");
            foreach (var (n, fs) in m.Functions) lines.Add($"fn {n} {fs.Count}");
            foreach (var (n, _) in m.Constants) lines.Add($"const {n}");
            lines.Add(Words("structs", m.Structs.Select(s => s.Name + "<" + string.Join(",", s.TypeParameters.Select(p => p.Name)) + ">" + (s.FieldEnum is { } fe ? "/" + fe.Name : ""))));
            lines.Add(Words("enums", m.Enums.Select(e => e.Name)));
            lines.Add(Words("unions", m.Unions.Select(u => u.Name + "<" + string.Join(",", u.TypeParameters.Select(p => p.Name)) + ">")));
            lines.Add(Words("interfaces", m.Interfaces.Select(i => i.Name + "<" + string.Join(",", i.TypeParameters.Select(p => p.Name)) + ">")));
            lines.Add(Words("errorsets", m.ErrorSets.Select(e => e.Name)));
            foreach (var f in m.AllFunctions)
            {
                var (line, col) = f.Location!.Value.Source.GetLinePosition(f.Location!.Value.Span.Start);
                lines.Add($"all {f.Name} {f.Kind} {line}:{col} <{string.Join(",", f.TypeParameters.Select(p => p.Name))}>");
            }
            foreach (var t in m.Tests) lines.Add($"test {t.Name}");
            lines.Add($"main {(m.Main is null ? "none" : m.Main.Kind.ToString())}");
        }
        var aliases = (IEnumerable<object>)typeof(Compilation).GetProperty("Aliases", Any)!.GetValue(c)!;
        lines.Add(Words("aliases", aliases.Select(a => ((TypeSymbol)a).DisplayName)));
        var distinct = (IEnumerable<StructSymbol>)typeof(Compilation).GetProperty("Distinct", Any)!.GetValue(c)!;
        lines.Add(Words("distinct", distinct.Select(s => s.Name)));
        lines.Add($"positional {Count(typeof(Compilation).GetProperty("Positional", Any)!.GetValue(c)!)}");
        lines.Add($"wheres {Count(typeof(Compilation).GetField("_wheres", Any)!.GetValue(c)!)}");
        lines.Add($"testcases {Count(typeof(Compilation).GetField("_testCases", Any)!.GetValue(c)!)}");
        var mainStatements = typeof(Compilation).GetField("_mainStatements", Any)!.GetValue(c);
        lines.Add($"mainstatements {(mainStatements is null ? 0 : Count(mainStatements.GetType().GetField("Item2")!.GetValue(mainStatements)!))}");
        var testBodies = (System.Collections.IDictionary)typeof(Compilation).GetField("_testBodies", Any)!.GetValue(c)!;
        var withValues = 0;
        foreach (System.Collections.DictionaryEntry entry in testBodies)
        {
            var val = entry.Value;
            var valuesField = val!.GetType().GetField("Item2")!.GetValue(val);
            if (valuesField is not null) withValues++;
        }
        lines.Add($"testbodies {testBodies.Count} withvalues {withValues}");
    }

    /// <summary><paramref name="head"/> and each word after a space.</summary>
    private static string Words(string head, IEnumerable<string> words) => head + string.Concat(words.Select(w => " " + w));

    private static int Count(object collection) => (int)collection.GetType().GetProperty("Count")!.GetValue(collection)!;

    private static void DumpDeclare(Compilation c, List<string> lines)
    {
        void Where(string owner, List<TypeParameterType> ps)
        {
            foreach (var p in ps) lines.Add(Words($"where {owner} {p.Name}:", p.Constraints.Select(i => i.Name)));
        }
        foreach (var m in c.Modules)
        {
            foreach (var s in m.Structs) Where(s.Name, s.TypeParameters);
            foreach (var u in m.Unions) Where(u.Name, u.TypeParameters);
            foreach (var i in m.Interfaces) Where(i.Name, i.TypeParameters);
            foreach (var f in m.AllFunctions) Where(f.Name, f.TypeParameters);
        }
        foreach (var v in c.ErrorVariants) lines.Add($"variant {v.Set.Name}.{v.Name} {v.Code} {string.Join(",", v.Fields.Select(f => f.Name + ":" + f.Type.DisplayName))}");
        foreach (var v in c.Builtins.Rule.Variants) lines.Add($"rule {v.Name} {v.Tag} {string.Join(",", v.Fields.Select(f => f.Name + ":" + f.Type.DisplayName))}");
        lines.Add($"members declared {typeof(Compilation).GetProperty("MembersDeclared", Any)!.GetValue(c)}");
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private const string MainSource = """
        import lexer
        import parser

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 3 < args.length {
                let kind = args[i]
                let name = args[i + 1]
                let path = args[i + 2]
                let flags = args[i + 3]
                print($"== {kind} {name}")
                var options = CompilationOptions { library: flags.contains("l") }
                if flags.contains("t") { options.includeTests = true }
                if kind == "path" {
                    let c = fromPath(path, by: in options, wholeDirectory: flags.contains("w"))
                    dumpLoad(in c)
                } else if kind == "project" {
                    let c = fromProject(path, for: "app", by: in options)
                    dumpLoad(in c)
                } else if kind == "declfile" {
                    var c = loadOnly(path, by: in options)
                    for m in copyIds(in c.moduleOrder) {
                        for f in copyIds(in c.modules[m].files) { declareFile(ref c, for: f) }
                    }
                    dumpLoad(in c)
                    dumpDecls(ref c)
                } else if kind == "declare" {
                    var c = loadOnly(path, by: in options)
                    declare(ref c)
                    dumpLoad(in c)
                    dumpDeclare(ref c)
                } else if kind == "mainsig" {
                    mainSignature(path, by: in options, for: flags)
                } else if name == "assoc" {
                    var c = loadOnly(path, by: in options)
                    let t0 = newTypeParam(ref c, by: TypeParam { name: "T" })
                    let t1 = newTypeParam(ref c, by: TypeParam { name: "T" })
                    let a = associatedType(ref c, for: t0, by: "Item")
                    let b = associatedType(ref c, for: t0, by: "Item")
                    let d = associatedType(ref c, for: t1, by: "Item")
                    let e = associatedType(ref c, for: t0, by: "Other")
                    print($"assoc {c.typeParams[a].name} {same(a, by: b)} {c.typeParams[d].name} {same(a, by: d)} {c.typeParams[e].name} {same(a, by: e)}")
                } else if name == "isinstance" {
                    var c = loadOnly(path, by: in options)
                    for f in copyIds(in c.modules[c.entryModule].files) { declareFile(ref c, for: f) }
                    let s = c.modules[c.entryModule].structs[0]
                    let instance = newStruct(ref c, by: StructSymbol { name: "S", template: s })
                    let plain = newFunction(ref c, by: FunctionSymbol { name: "f" })
                    let copy = newFunction(ref c, by: FunctionSymbol { name: "f", template: plain })
                    let method = newFunction(ref c, by: FunctionSymbol { name: "f", owner: s })
                    let instanceMethod = newFunction(ref c, by: FunctionSymbol { name: "f", owner: instance })
                    let inInstance = newFunction(ref c, by: FunctionSymbol { name: "f", enclosing: instanceMethod })
                    let inTemplate = newFunction(ref c, by: FunctionSymbol { name: "f", enclosing: method })
                    var sb = StrBuf()
                    sb.append("isinstance")
                    for f in [plain, copy, method, instanceMethod, inInstance, inTemplate] {
                        let answer = t11IsInstance(in c, for: f) ? "yes" : "no"
                        sb.append($" {answer}")
                    }
                    print(sb.finish())
                } else if name == "sink" {
                    var c = loadOnly(path, by: in options)
                    c.diagnostics = List<Diag>()
                    let outer = c.sink
                    c.bags.append(DiagBag { items: List<Diag>() })
                    let bag = c.bags.length - 1
                    reportInto(ref c, to: bag)
                    report(ref c, for: .unknownAttribute, from: 0, at: TextSpan { start: 0, length: 1 }, by: ("a", "", "", "", ""))
                    report(ref c, for: .unknownAttribute, from: 0, at: TextSpan { start: 0, length: 1 }, by: ("b", "", "", "", ""))
                    reportInto(ref c, to: outer)
                    report(ref c, for: .unknownAttribute, from: 0, at: TextSpan { start: 0, length: 1 }, by: ("c", "", "", "", ""))
                    let shown = outer < 0 ? "none" : "bag"
                    print($"sink bag {c.bags[bag].items.length} main {c.diagnostics.length} outer {shown}")
                    for d in c.bags[bag].items { print($"bag {d.message}") }
                    for d in c.diagnostics { print($"main {d.message}") }
                }
                i += 4
            }
            return 0
        }

        string same(i64 a, by i64 b) => a == b ? "same" : "new"

        Ctx loadOnly(string dir, by in CompilationOptions options) {
            var sources = List<lexer.SourceText>()
            for f in t11ModuleFiles(dir, includeTests: options.includeTests) { sources.append(lexer.SourceText.create(f, from: t11ReadAllBytes(f))) }
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
                packageRoot: dir,
                displayRoot: dir,
                includeTests: options.includeTests,
            }
            c.entryModule = loadModule(ref c, for: "main", at: dir, from: move sources, by: -1)
            return c
        }

        string diagLine(in Ctx c, for in Diag d) {
            let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
            return $"diag {d.code} {c.files[d.file].displayPath} {pos.line}:{pos.column} {d.message} | {d.label} | {d.help}"
        }

        void dumpLoad(in Ctx c) {
            for m in c.moduleOrder {
                print($"module {c.modules[m].name} {c.modules[m].directory} {c.modules[m].cName}")
                for f in c.modules[m].files {
                    let kind = c.files[f].isTestFile ? "test" : "source"
                    print($"file {c.files[f].displayPath} {kind}")
                    for (alias, imported) in c.files[f].imports { print($"import {alias} {c.modules[imported].name}") }
                }
            }
            for d in c.diagnostics { print(diagLine(in c, for: in d)) }
        }

        string kindName(Type t) {
            if t8IsAlias(t) { return "alias" }
            switch kindOf(t) {
                case .structType: return "struct"
                case .enumType: return "enum"
                case .unionType: return "union"
                case .interfaceType: return "interface"
                case .errorSet: return "errorset"
                default: return "other"
            }
        }

        string paramNames(in Ctx c, for in List<TypeParamId> ps) {
            var sb = StrBuf()
            for i in 0..ps.length {
                if i > 0 { sb.append(",") }
                sb.append(c.typeParams[ps[i]].name)
            }
            return $"{sb.finish()}"
        }

        string functionKind(FunctionKind k) {
            switch k {
                case .main: return "Main"
                case .testKeyword: return "Test"
                case .function: return "Function"
                default: return "Other"
            }
        }

        void dumpDecls(ref Ctx c) {
            for m in copyIds(in c.moduleOrder) {
                print($"decls {c.modules[m].name}")
                for (n, t) in c.modules[m].types { print($"type {n} {kindName(t)}") }
                // `functions` in insertion order: every accepted function is in `allFunctions` too (a `for` over the map copies its lists).
                var seen = List<string>()
                for f in c.modules[m].allFunctions {
                    let n = c.functions[f].name
                    if c.functions[f].kind == .function && !listContainsString(in seen, by: n) {
                        seen.append(n)
                        print($"fn {n} {c.modules[m].functions[n].length}")
                    }
                }
                for (n, k) in c.modules[m].constants { print($"const {n}") }
                var sb = StrBuf()
                sb.append("structs")
                for s in c.modules[m].structs {
                    sb.append($" {c.structs[s].name}<{paramNames(in c, for: in c.structs[s].typeParams)}>")
                    if c.structs[s].fieldEnum >= 0 { sb.append($"/{c.enums[c.structs[s].fieldEnum].name}") }
                }
                print(sb.finish())
                var eb = StrBuf()
                eb.append("enums")
                for e in c.modules[m].enums { eb.append($" {c.enums[e].name}") }
                print(eb.finish())
                var ub = StrBuf()
                ub.append("unions")
                for u in c.modules[m].unions { ub.append($" {c.unions[u].name}<{paramNames(in c, for: in c.unions[u].typeParams)}>") }
                print(ub.finish())
                var ib = StrBuf()
                ib.append("interfaces")
                for x in c.modules[m].interfaces { ib.append($" {c.interfaces[x].name}<{paramNames(in c, for: in c.interfaces[x].typeParams)}>") }
                print(ib.finish())
                var sets = StrBuf()
                sets.append("errorsets")
                for x in c.modules[m].errorSets { sets.append($" {c.errorSets[x].name}") }
                print(sets.finish())
                for f in c.modules[m].allFunctions {
                    let pos = linePosition(c.files[c.functions[f].file].text.span, at: c.functions[f].span.start)
                    print($"all {c.functions[f].name} {functionKind(c.functions[f].kind)} {pos.line}:{pos.column} <{paramNames(in c, for: in c.functions[f].typeParams)}>")
                }
                for t in c.modules[m].tests { print($"test {c.tests[t].title}") }
                let main = c.modules[m].main
                let mainKind = main < 0 ? "none" : functionKind(c.functions[main].kind)
                print($"main {mainKind}")
            }
            var ab = StrBuf()
            ab.append("aliases")
            for a in c.aliasOrder { ab.append($" {c.aliases[a].name}") }
            print(ab.finish())
            var db = StrBuf()
            db.append("distinct")
            for (s, _) in c.distinct { db.append($" {c.structs[s].name}") }
            print(db.finish())
            print($"positional {c.positional.length}")
            print($"wheres {c.wheres.length}")
            print($"testcases {c.testCases.length}")
            print($"mainstatements {c.mainStatements.length}")
            var withValues = 0
            for (_, tb) in c.testBodies {
                if tb.hasValues { withValues += 1 }
            }
            print($"testbodies {c.testBodies.length} withvalues {withValues}")
        }

        void dumpDeclare(ref Ctx c) {
            for m in copyIds(in c.moduleOrder) {
                for s in c.modules[m].structs { dumpWhere(in c, for: c.structs[s].name, by: in c.structs[s].typeParams) }
                for u in c.modules[m].unions { dumpWhere(in c, for: c.unions[u].name, by: in c.unions[u].typeParams) }
                for x in c.modules[m].interfaces { dumpWhere(in c, for: c.interfaces[x].name, by: in c.interfaces[x].typeParams) }
                for f in c.modules[m].allFunctions { dumpWhere(in c, for: c.functions[f].name, by: in c.functions[f].typeParams) }
            }
            for v in 0..c.errorVariants.length {
                print($"variant {c.errorSets[c.errorVariants[v].set].name}.{c.errorVariants[v].name} {c.errorVariants[v].code} {fieldList(ref c, for: copyIds(in c.errorVariants[v].fields))}")
            }
            let rule = typeIndex(t9Type(ref c, for: "Rule"))
            for v in copyIds(in c.unions[rule].variants) {
                print($"rule {c.unionVariants[v].name} {c.unionVariants[v].tag} {fieldList(ref c, for: copyIds(in c.unionVariants[v].fields))}")
            }
            let declaredMembers = c.membersDeclared ? "True" : "False"
            print($"members declared {declaredMembers}")
        }

        void dumpWhere(in Ctx c, for string owner, by in List<TypeParamId> ps) {
            for p in ps {
                var sb = StrBuf()
                sb.append($"where {owner} {c.typeParams[p].name}:")
                for x in c.typeParams[p].constraints { sb.append($" {c.interfaces[x].name}") }
                print(sb.finish())
            }
        }

        string fieldList(ref Ctx c, for List<LocalId> fields) {
            var sb = StrBuf()
            for i in 0..fields.length {
                if i > 0 { sb.append(",") }
                sb.append($"{c.locals[fields[i]].name}:{typeDisplay(ref c, by: c.locals[fields[i]].type)}")
            }
            return $"{sb.finish()}"
        }

        /// `main`'s signature built by hand as `DeclareSignature` (T38) builds it: `flags` is `ret:param,param`.
        void mainSignature(string dir, by in CompilationOptions options, for string flags) {
            var c = loadOnly(dir, by: in options)
            for f in copyIds(in c.modules[c.entryModule].files) { declareFile(ref c, for: f) }
            let main = c.modules[c.entryModule].main
            let colon = flags.position(":") ?? 0
            c.functions[main].returnType = typeNamed(ref c, for: flags[0..colon])
            var rest = flags[colon + 1..flags.length]
            while rest.length > 0 {
                let comma = rest.position(",") ?? rest.length
                let part = rest[0..comma]
                var modifier = parser.ParamModifier.none
                var type = typeNamed(ref c, for: part)
                if part == "refros" {
                    modifier = .refKeyword
                    type = typeNamed(ref c, for: "ros")
                }
                let p = newLocal(ref c, by: LocalSymbol { name: "args", type: type, kind: .parameter, isParameter: true, modifier: modifier })
                c.functions[main].parameters.append(p)
                rest = comma < rest.length ? rest[comma + 1..rest.length] : ""
            }
            c.diagnostics = List<Diag>()
            t11CheckMainSignature(ref c, for: main)
            print("main checked")
            for d in c.diagnostics { print(diagLine(in c, for: in d)) }
        }

        Type typeNamed(ref Ctx c, for string name) {
            switch name {
                case "i32": return prim(.i32)
                case "void": return prim(.void)
                case "string": return prim(.string)
                case "i64": return prim(.i64)
                case "ros": return internSpan(ref c, by: prim(.string), readOnly: true)
                case "span": return internSpan(ref c, by: prim(.string), readOnly: false)
                case "roi64": return internSpan(ref c, by: prim(.i64), readOnly: true)
            }
            return errorType()
        }
        """;
}
