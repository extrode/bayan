using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T7: <c>selfhost/semantics/naming.byn</c> (casing, the grammar checker and the particle rule) against
/// <c>Bayan.Semantics</c>. Each case is compiled by the C# checker; its naming diagnostics are the oracle.
/// The generated <c>main</c> rebuilds the C# symbols of each case in a <c>Ctx</c> over the same parsed
/// source, runs the ported check and prints one <c>== case</c> section of diagnostics.
/// </summary>
public sealed class SelfhostSemanticsT7Tests
{
    private static string DiagDir => Path.Combine(Golden.RepoRoot, "tests", "cases", "diag");

    /// <summary>The codes <c>checkMembers</c> reports.</summary>
    private static readonly HashSet<string> MemberCodes = ["BYN5001", "BYN5002", "BYN5003", "BYN5009", "BYN5014", "BYN5101", "BYN5102"];

    /// <summary>The codes <c>checkParticles</c> reports.</summary>
    private static readonly HashSet<string> ParticleCodes = ["BYN5004", "BYN5006", "BYN5013"];

    /// <summary>The codes <c>checkType</c>, <c>checkInterface</c> and <c>checkValue</c> report.</summary>
    private static readonly HashSet<string> CasingCodes = ["BYN2008", "BYN2009", "BYN2800"];

    /// <summary>The <c>tests/cases/diag</c> goldens about member names and particles, plus one written here.</summary>
    private static readonly string[] MemberCases =
    [
        "bool-without-prefix", "noun-on-mutator", "participle-changes-this", "particle-as-member-name", "try-wrong-return",
        "state-member-mutates", "unknown-word", "particle-required", "parameter-needs-particle", "repeated-particle",
        "particle-exemptions",
    ];

    /// <summary>Written here: the forms the goldens do not reach (extern, `to`/`as` conversions, endings, more stems).</summary>
    private const string ExtraSource = """
        public struct Shelf {
            i64 n
            public void copied(ref this) {
                n = 1
            }
            public void doubled(ref this) {
                n = 2
            }
            public void trimmed(ref this) {
                n = 3
            }
            public void hundred(ref this) {
                n = 4
            }
            public void stopped(ref this) {
                n = 5
            }
            public void named(ref this) {
                n = 6
            }
            public void joined(ref this) {
                n = 7
            }
            public i64 toString() => n
            public i64 toWidget() => n
            public i64 asView() => n
            public i64 to() => n
            public void runners(ref this) {
                n = 8
            }
            public void unreadable(ref this) {
                n = 9
            }
            public void rebuilt(ref this) {
                n = 10
            }
            public i64 utf8Count() => n
            public i64 md5sum() => n
            public void hoped(ref this) {
                n = 13
            }
            public void towed(ref this) {
                n = 14
            }
            public i64 x() => n
            public i64 frobs() => n
            public i64 copiers() => n
            public bool ready() => true
            public bool startsWithA() => true
            public bool tryIt() => true
            public bool canGo() => true
            public void hasRoom(ref this) {
                n = 11
            }
            public void entries(ref this) {
                n = 12
            }
            public i64 fetch() fails E {
                return 0
            }
            public string tryFetch() => ""
            public bool tryParse() => true
            public i64 parse() fails E {
                return 0
            }
            public i64 tryNothing() => 0
            public static i64 toThing() => 0
            public bool visible => true
            public bool isShown => true
            i64 for_count => 0
        }
        union error E { missing }
        void three(i64 a, i64 b, i64 c, bool d, i64 e = 1) {
            return
        }
        void spread(i64 a, by i64 b, by i64 c, by i64 d, rest ReadOnlySpan<i64> xs) {
            return
        }
        void twoBools(bool a, bool b) {
            return
        }
        """;

    private sealed record Casing(string Name, string Kind, string Text);

    /// <summary>Names for the casing checks: `type` → checkType, `iface` → checkInterface, `value` → checkValue.</summary>
    private static readonly Casing[] CasingCases =
    [
        new("type-snake", "type", "my_point"), new("type-acronym", "type", "HTTPClient"), new("type-io", "type", "IOError"),
        new("type-ok", "type", "Point"), new("type-lower", "type", "point"), new("type-mixed", "type", "XMLHttpRequest"),
        new("type-digits", "type", "http_server_v2"), new("type-uncased", "type", "café_shop"), new("type-two", "type", "ABc"),
        new("iface-ok", "iface", "IShape"), new("iface-plain", "iface", "Shape"), new("iface-lower-i", "iface", "Ishape"),
        new("iface-bad-casing", "iface", "IHTTPThing"), new("iface-snake", "iface", "shape_thing"),
        new("value-pascal", "value", "DoThing"), new("value-snake", "value", "parse_url"), new("value-acronym", "value", "parseURL"),
        new("value-ok", "value", "parse"), new("value-upper", "value", "HTTPGet"), new("value-io", "value", "IO"),
        new("value-digit", "value", "get2Things"), new("type-z", "type", "ZZTop"), new("value-z", "value", "zapZed"),
    ];

    private sealed record Generated(string Main, Dictionary<string, List<string>> Expected);

    private static readonly Lazy<Generated> Cases = new(Generate);

    private static readonly Lazy<Dictionary<string, List<string>>> Output =
        new(() => SelfhostSemanticsHarness.RunSections(Cases.Value.Main));

    public static IEnumerable<object[]> MemberCaseNames() =>
        MemberCases.Select(n => new object[] { n }).Append(["extra"]).Append(["grammar-dictionary"]);

    public static IEnumerable<object[]> CasingCaseNames() => CasingCases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(MemberCaseNames))]
    public void MembersAndParticlesMatchCSharp(string name) => Compare(name);

    [Theory]
    [MemberData(nameof(CasingCaseNames))]
    public void CasingMatchesCSharp(string name) => Compare(name);

    /// <summary>The generated cases between them reach every BYN50xx/51xx form T7 reports.</summary>
    [Fact]
    public void EveryFormIsReached()
    {
        var codes = Cases.Value.Expected.Values.SelectMany(lines => lines).Select(l => l[..7]).ToHashSet();
        foreach (var code in MemberCodes.Concat(ParticleCodes).Concat(CasingCodes)) Assert.Contains(code, codes);
    }

    /// <summary>`buildLexicon` fills the three word lists once; a second call leaves them as they are.</summary>
    [Fact]
    public void LexiconIsBuiltOnce()
    {
        var expected = new List<string>
        {
            $"{Lexicon.Verbs.Count} {Lexicon.Nouns.Count} {Lexicon.Adjectives.Count}",
            string.Join(" ", Lexicon.Verbs.Order(StringComparer.Ordinal).Take(3)),
            string.Join(" ", Lexicon.Nouns.Order(StringComparer.Ordinal).TakeLast(3)),
            string.Join(" ", Lexicon.Adjectives.Order(StringComparer.Ordinal).Skip(40).Take(3)),
        };
        SelfhostSemanticsHarness.AssertSection(Output.Value, "lexicon", expected);
    }

    /// <summary>The grammar is read from the file loaded as `package.byn`, and only from it; none means an empty grammar.</summary>
    [Fact]
    public void PackageFileIsFound() =>
        SelfhostSemanticsHarness.AssertSection(Output.Value, "package-file", ["-1 1", "0 0"]);

    private static void Compare(string name)
    {
        // Both sides are sorted (C# reports in walk order) and end with `end`, so a clean case still compares a line.
        Assert.True(Output.Value.TryGetValue(name, out var lines), $"section '{name}' was not printed");
        var actual = new Dictionary<string, List<string>> { [name] = [.. lines!.Order(StringComparer.Ordinal), "end"] };
        SelfhostSemanticsHarness.AssertSection(actual, name, [.. Cases.Value.Expected[name], "end"]);
    }

    // ── The oracle and the generated main ─────────────────────────────────────────────────

    private static Generated Generate()
    {
        var main = new StringBuilder();
        var expected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var calls = new List<string>();
        main.AppendLine(Prelude);
        var index = 0;
        foreach (var name in MemberCases)
            AddMembers(main, calls, expected, name, File.ReadAllText(Path.Combine(DiagDir, name + ".byn")), null, index++);
        AddMembers(main, calls, expected, "extra", ExtraSource, null, index++);
        var grammarDir = Path.Combine(DiagDir, "grammar-dictionary");
        AddMembers(main, calls, expected, "grammar-dictionary", File.ReadAllText(Path.Combine(grammarDir, "main.byn")),
            Manifest.Read(Path.Combine(grammarDir, "package.byn")).Grammar, index++);
        foreach (var casing in CasingCases) AddCasing(main, calls, expected, casing, index++);
        main.AppendLine("i32 main() {");
        main.AppendLine("    lexiconCase()");
        main.AppendLine("    packageFileCase()");
        foreach (var call in calls) main.AppendLine($"    {call}()");
        main.AppendLine("    return 0");
        main.AppendLine("}");
        return new Generated(main.ToString(), expected);
    }

    private const string Prelude = """
        import lexer
        import parser

        SourceFile makeFile(string display, by string text) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            let source = lexer.SourceText.create(display, from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            var textBytes = List<u8>()
            for b in text.bytes { textBytes.append(b) }
            return SourceFile {
                path: display,
                displayPath: display,
                text: move textBytes,
                unit: move unit,
                imports: Map<string, i64>(),
            }
        }

        Ctx newCtx(string text) {
            var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
            c.files.append(makeFile("main.byn", by: text))
            return c
        }

        void dump(ref Ctx c, for string section) {
            print($"== {section}")
            for d in c.diagnostics {
                let fix = d.hasFix ? $"{d.fixSpan.start}+{d.fixSpan.length}={d.fixReplacement}" : ""
                print($"{d.code} {d.span.start}+{d.span.length} {d.message} | {d.label} | {d.help} | {fix}")
            }
        }

        void lexiconCase() {
            var c = newCtx("")
            buildLexicon(ref c)
            buildLexicon(ref c)
            print("== lexicon")
            print($"{c.lexiconVerbs.length} {c.lexiconNouns.length} {c.lexiconAdjectives.length}")
            print($"{c.lexiconVerbs[0]} {c.lexiconVerbs[1]} {c.lexiconVerbs[2]}")
            let n = c.lexiconNouns.length
            print($"{c.lexiconNouns[n - 3]} {c.lexiconNouns[n - 2]} {c.lexiconNouns[n - 1]}")
            print($"{c.lexiconAdjectives[40]} {c.lexiconAdjectives[41]} {c.lexiconAdjectives[42]}")
        }

        void packageFileCase() {
            var c = newCtx("")
            let before = packageFileIndex(in c.files)
            let none = grammarOf(ref c)
            c.files.append(makeFile("package.byn", by: ""))
            let empty = grammarOf(ref c)
            print("== package-file")
            print($"{before} {packageFileIndex(in c.files)}")
            print($"{none.verbs.length + none.nouns.length + none.adjectives.length} {empty.verbs.length}")
        }

        """;

    /// <summary>One diagnostic as the generated main prints it.</summary>
    private static string Line(Bayan.Syntax.Diagnostics.Diagnostic d)
    {
        var fix = d.Fix is null ? "" : $"{d.Fix.Span.Start}+{d.Fix.Span.Length}={d.Fix.Replacement}";
        return $"{d.Code} {d.Span.Start}+{d.Span.Length} {d.Message} | {d.Label} | {d.Help} | {fix}";
    }

    private static Compilation Compile(string name, string source, string? packageText)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t7", name);
        Directory.CreateDirectory(dir);
        var package = Path.Combine(dir, "package.byn");
        if (packageText is null) File.Delete(package);
        else File.WriteAllText(package, packageText);
        return Compilation.Create([SourceText.FromString("main.byn", source)], packageRoot: dir);
    }

    private static void AddMembers(StringBuilder main, List<string> calls, Dictionary<string, List<string>> expected,
        string name, string source, Grammar? grammar, int index)
    {
        var packageText = grammar is null ? null : File.ReadAllText(Path.Combine(DiagDir, "grammar-dictionary", "package.byn"));
        var c = Compile(name, source, packageText);
        var oracle = c.Diagnostics.Items.Where(d => d.Source?.Path == "main.byn" && (MemberCodes.Contains(d.Code) || ParticleCodes.Contains(d.Code)));
        expected[name] = oracle.Select(Line).Order(StringComparer.Ordinal).ToList();

        var root = c.Modules.SelectMany(m => m.Structs).Select(s => s.File.Tree.Root).FirstOrDefault()
            ?? c.Modules.SelectMany(m => m.Functions.Values.SelectMany(f => f)).First(f => f.File?.Tree.Source.Path == "main.byn").File!.Tree.Root;
        var structs = c.Modules.SelectMany(m => m.Structs).Where(s => s.File.Tree.Root == root).ToList();
        var functions = c.Modules.SelectMany(m => m.Functions.Values.SelectMany(f => f))
            .Where(f => f.Decl is not null && f.File?.Tree.Root == root).ToList();

        var body = new StringBuilder();
        body.AppendLine($"void runCase{index}() {{");
        body.AppendLine($"    var c = newCtx(\"{Escape(source)}\")");
        var ids = new Dictionary<StructSymbol, string>();
        for (var i = 0; i < structs.Count; i++)
        {
            var s = structs[i];
            ids[s] = $"s{i}";
            body.AppendLine($"    let s{i} = newStruct(ref c, by: StructSymbol {{ name: \"{s.Name}\", nameSpan: {Span(s.Decl.Name.Span)}, file: 0, " +
                $"declIndex: {Array.IndexOf(root.Members, s.Decl)}, access: {Access(s.Access)} }})");
        }
        var fnIds = new List<string>();
        var next = 0;
        string Function(FunctionSymbol fn, string owner, int declIndex, int memberIndex)
        {
            var id = $"f{next++}";
            var at = fn.Decl?.Name.Span ?? fn.Location!.Value.Span;
            var extern_ = fn.Decl?.Modifiers.HasFlag(Modifiers.Extern) == true ? 128 : 0;
            body.AppendLine($"    let {id} = newFunction(ref c, by: FunctionSymbol {{ name: \"{fn.Name}\", nameSpan: {Span(at)}, " +
                $"kind: .{(fn.Kind == FunctionKind.Constructor ? "constructor" : fn.Owner is null ? "function" : "method")}, access: {Access(fn.Access)}, " +
                $"modifiers: {extern_}, file: 0, declIndex: {declIndex}, memberIndex: {memberIndex}, owner: {owner}, isStatic: {Bool(fn.IsStatic)}, " +
                $"isMutating: {Bool(fn.IsMutating)}, hasFails: {Bool(fn.Fails)}, returnType: {TypeExpr(fn.ReturnType, ids)} }})");
            foreach (var p in fn.Parameters)
                body.AppendLine($"    c.functions[{id}].parameters.append(newLocal(ref c, by: LocalSymbol {{ name: \"{p.Name}\", type: {TypeExpr(p.Type, ids)}, isParameter: true }}))");
            fnIds.Add(id);
            return id;
        }
        foreach (var s in structs)
        {
            var d = Array.IndexOf(root.Members, s.Decl);
            foreach (var fn in s.Methods)
            {
                var id = Function(fn, ids[s], d, Array.IndexOf(s.Decl.Members, fn.Decl));
                body.AppendLine($"    c.structs[{ids[s]}].methods.append({id})");
            }
            foreach (var p in s.Properties)
            {
                body.AppendLine($"    c.structs[{ids[s]}].properties.append(newProperty(ref c, by: PropertySymbol {{ name: \"{p.Name}\", " +
                    $"nameSpan: {Span(p.Decl.Name.Span)}, owner: {ids[s]}, file: 0, access: {Access(p.Access)}, type: {TypeExpr(p.Type, ids)} }}))");
            }
        }
        foreach (var fn in functions) Function(fn, "-1", Array.IndexOf(root.Members, fn.Decl), -1);
        if (grammar is null) body.AppendLine("    let grammar = Grammar {}");
        else
        {
            body.AppendLine($"    var raw = Grammar {{ verbs: [{Words(grammar.Verbs)}], nouns: [{Words(grammar.Nouns)}], adjectives: [{Words(grammar.Adjectives)}] }}");
            foreach (var (word, form) in grammar.Forms) body.AppendLine($"    raw.forms[\"{word}\"] = \"{form}\"");
            body.AppendLine("    let grammar = lowerGrammar(in raw)");
        }
        foreach (var id in ids.Values) body.AppendLine($"    checkMembersWith(ref c, for: {id}, by: grammar)");
        foreach (var id in fnIds) body.AppendLine($"    checkParticles(ref c, from: 0, for: {id})");
        body.AppendLine($"    dump(ref c, for: \"{name}\")");
        body.AppendLine("}");
        main.Append(body);
        calls.Add($"runCase{index}");
    }

    private static void AddCasing(StringBuilder main, List<string> calls, Dictionary<string, List<string>> expected, Casing casing, int index)
    {
        var source = casing.Kind switch
        {
            "type" => $"struct {casing.Text} {{\n    i64 n\n}}\n",
            "iface" => $"interface {casing.Text} {{\n    i64 size()\n}}\n",
            _ => $"void {casing.Text}() {{\n    return\n}}\n",
        };
        var start = Encoding.UTF8.GetByteCount(source[..source.IndexOf(casing.Text, StringComparison.Ordinal)]);
        var length = Encoding.UTF8.GetByteCount(casing.Text);
        var c = Compile(casing.Name, source, null);
        expected[casing.Name] = c.Diagnostics.Items
            .Where(d => d.Source?.Path == "main.byn" && CasingCodes.Contains(d.Code) && d.Span.Start == start)
            .Select(Line).ToList();
        var check = casing.Kind switch { "type" => "checkType", "iface" => "checkInterface", _ => "checkValue" };
        main.AppendLine($"void runCase{index}() {{");
        main.AppendLine($"    var c = newCtx(\"{Escape(source)}\")");
        main.AppendLine($"    {check}(ref c, from: 0, at: TextSpan {{ start: {start}, length: {length} }})");
        main.AppendLine($"    dump(ref c, for: \"{casing.Name}\")");
        main.AppendLine("}");
        calls.Add($"runCase{index}");
    }

    private static string Span(TextSpan s) => $"TextSpan {{ start: {s.Start}, length: {s.Length} }}";

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Access(Accessibility a) => a switch
    {
        Accessibility.Public => ".publicKeyword",
        Accessibility.Private => ".privateKeyword",
        _ => ".module",
    };

    private static string Words(IEnumerable<string> words) => string.Join(", ", words.Select(w => $"\"{w}\""));

    /// <summary>The Bayan expression for a type the cases use: a primitive, a struct of the case, or an optional of one.</summary>
    private static string TypeExpr(TypeSymbol t, Dictionary<StructSymbol, string> ids) => t switch
    {
        Bayan.Semantics.Symbols.OptionalType o => $"internOptional(ref c, by: {TypeExpr(o.Inner, ids)})",
        StructSymbol s when ids.ContainsKey(s) => $"structTypeOf({ids[s]})",
        PrimitiveType p => $"primitiveByName(\"{p.DisplayName}\") ?? errorType()",
        _ when t.DisplayName == "ReadOnlySpan<i64>" => "errorType()",
        _ => throw new InvalidOperationException($"T7 test: no Bayan form for type {t.DisplayName}"),
    };

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
}
