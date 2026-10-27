using System.Text;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T10: <c>selfhost/semantics/manifest.byn</c> against the C# <c>Manifest</c>. Every manifest input (the
/// <c>tests/cases/manifest/</c> goldens, the two <c>diag/</c> <c>package.byn</c> files and the inline cases
/// below, written to <c>tmp/t10-cases/</c>) goes through <c>manifestRead</c> in one generated
/// <c>main.byn</c>, which prints a section per input: the rendered diagnostics, or the fields read. The
/// oracle is <c>Manifest.Read</c> on the same file, rendered by <c>DiagnosticRenderer.RenderAll</c>.
/// </summary>
public sealed class SelfhostSemanticsT10Tests
{
    private static string CasesDir => Path.Combine(Golden.RepoRoot, "tmp", "t10-cases");

    /// <summary>Inline manifests, each aimed at one branch of the reader or of <c>Bind</c>.</summary>
    private static readonly (string Name, string Text)[] Inline =
    [
        ("full", """
            Package {
                name: "local/my-lib",
                version: "1.2.3-rc.1+build.5",
                edition: 2_027,
                kind: .lib,
                capabilities: [.fs, .net],
                test: .async,
                style: Style { indent: 4, braces: .nextLine, maxLine: 100, trailingCommas: true, booleans: .words, primitives: .csharp },
                grammar: Grammar { verbs: ["upsert", "upsert", "regrind"], nouns: ["SKU"], adjectives: ["idempotent"], forms: ["regrind": "reground", "a": "b", "a": "c"] },
                dist: Dist { targets: [.host, .linuxX64, .macosArm64], mode: .fast },
            }
            """),
        ("app-defaults", "Package { name: \"local/shop\", version: \"0.1.0\", capabilities: [], grammar: Grammar { forms: [] } }"),
        ("style-words-symbols", "Package { name: \"a/b\", version: \"0.0.0\", kind: .app, style: Style { booleans: .symbols, primitives: .sized, braces: .sameLine }, dist: Dist { mode: .debug } }"),
        ("dist-safe", "Package { name: \"a/b\", version: \"0.0.0\", dist: Dist { mode: .safe, targets: [] } }"),
        ("escapes", """
            Package {
                name: "local/esc",
                version: "0.1.0",
                grammar: Grammar { verbs: ["a\nb", "\u{1F600}", "\u{e9}x", "\u{800}", "\u{D800}", "\q", "\u{000000041}", "\u{FFFFFFFF}", "\u{110000}", "t\t\r\0", "\\", "\"", "\'", "\u{}", "\u{41", "\u{4g}", "\u{0041}", "\u{7FFFFFFF}", "end\"] },
            }
            """),
        ("int-overflow", "Package { name: \"local/a\", version: \"0.1.0\", edition: 99999999999999999999 }"),
        ("int-max", "Package { name: \"local/a\", version: \"0.1.0\", style: Style { indent: 9223372036854775807, maxLine: 9223372036854775808 } }"),
        ("int-hex", "Package { name: \"local/a\", version: \"0.1.0\", edition: 0x7E3 }"),
        ("int-zero", "Package { name: \"local/a\", version: \"0.1.0\", style: Style { indent: 0, maxLine: -1 } }"),
        ("capabilities-mixed", "Package { name: \"local/a\", version: \"0.1.0\", capabilities: [.fs, \"net\"] }"),
        ("capabilities-bare", "Package { name: \"local/a\", version: \"0.1.0\", capabilities: .fs }"),
        ("style-bad", "Package { name: \"local/a\", version: \"0.1.0\", style: Style { indent: \"4\", braces: .left, maxLine: true, trailingCommas: .yes, booleans: .both, primitives: .int, tabs: 1 } }"),
        ("style-scalar", "Package { name: \"local/a\", version: \"0.1.0\", style: 3 }"),
        ("style-other-type", "Package { name: \"local/a\", version: \"0.1.0\", style: Grammar { } }"),
        ("grammar-other-type", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Style { } }"),
        ("grammar-forms-bad", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Grammar { adjectives: [1], forms: [1: \"x\"] } }"),
        ("grammar-forms-value", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Grammar { forms: [\"x\": .y] } }"),
        ("grammar-forms-scalar", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Grammar { forms: \"x\" } }"),
        ("dist-scalar", "Package { name: \"local/a\", version: \"0.1.0\", dist: [.host], kind: .lib }"),
        ("dist-targets-strings", "Package { name: \"local/a\", version: \"0.1.0\", dist: Dist { targets: [\"host\"], mode: \"fast\" } }"),
        ("duplicate", "Package { name: \"local/a\", version: \"0.1.0\", name: \"local/b\", grammar: Grammar { verbs: [], verbs: [] } }"),
        ("empty-package", "Package { }"),
        ("missing-version", "Package { name: \"local/a\" }"),
        ("name-not-string", "Package { name: .a, version: 1 }"),
        ("names-bad", "Package { name: \"a/b/c\", version: \"01.2.3\" }"),
        ("names-slash", "Package { name: \"ab\", version: \"1.2.3-\" }"),
        ("names-upper", "Package { name: \"Local/a\", version: \"1.2.3+\" }"),
        ("names-dash", "Package { name: \"lo--cal/a-\", version: \"1.2\" }"),
        ("names-digit", "Package { name: \"2a/b\", version: \"1.2.3.4\" }"),
        ("other-root", "Project { name: \"local/a\" }"),
        ("list-root", "[1, 2]"),
        ("string-root", "\"x\""),
        ("trailing", "Package { name: \"local/a\", version: \"0.1.0\" } extra"),
        ("empty-file", ""),
        ("map-no-value", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Grammar { forms: [\"a\": ] } }"),
        ("map-no-colon", "Package { name: \"local/a\", version: \"0.1.0\", grammar: Grammar { forms: [\"a\": \"b\", \"c\"] } }"),
        ("list-no-comma", "Package { name: \"local/a\", version: \"0.1.0\", capabilities: [.fs .net] }"),
        ("list-unclosed", "Package { name: \"local/a\", version: \"0.1.0\", capabilities: [.fs, "),
        ("object-no-comma", "Package { name: \"local/a\" version: \"0.1.0\" }"),
        ("object-string-key", "Package { \"name\": \"local/a\" }"),
        ("object-colon-first", "Package { : \"local/a\" }"),
        ("object-equals-first", "Package { = \"local/a\" }"),
        ("object-int-key", "Package { 123: \"local/a\" }"),
        ("object-no-colon", "Package { name \"local/a\" }"),
        ("object-keyword-type", "struct { }"),
        ("dot-alone", "Package { name: \"local/a\", version: \"0.1.0\", kind: . }"),
        ("dot-keyword", "Package { name: \"local/a\", version: \"0.1.0\", kind: .struct, if: 1 }"),
        ("bools", "Package { name: \"local/a\", version: \"0.1.0\", style: Style { trailingCommas: false } }"),
        ("nested-equals", "Package { name: \"local/a\", version: \"0.1.0\", dist: Dist { mode: = .fast, targets = [.host] } }"),
        ("equals-then-fail", "Package { name = \"local/a\", version: 1 + 2 }"),
        ("lexer-error", "Package { name: \"local/a, version: \"0.1.0\" }"),
        ("lexer-error-char", "Package { name: \"local/a\", version: \"0.1.0\" } \u0001"),
    ];

    private static readonly string[] Versions =
    [
        "0.1.0", "10.20.30-rc.1", "1.0", "01.0.0", "1.0.0-", "1.0.0-rc..1", "1.0.0-a/b", "1.0.0-a\\b",
        "1.0.0-x-y.2", "1.0.0-rc.1+build.5", "1.0.0+", "1.0.0+a/b", "123456789012345678.0.0",
        "1234567890123456789.0.0", "1.0.0-x/../..", "99999999999999999999.0.0", "0.0.0", "1..0", ".1.0", "1.0.",
        "1.0.0-.a", "1.0.0-a.", "1.0.0+b-c.D9", "1.0.0-a+b+c", "1.0.0-a-b-", "", "a.b.c", "1.0.0 ", "1.0.0-é",
    ];

    private static readonly string[] Kebabs =
        ["tally", "my-app2", "My-app", "my--app", "my-app-", "2app", "my_app", "", "a", "-a", "a-1", "é"];

    private static readonly string[] PackageNames =
        ["local/tally", "local/my-app", "local", "local/", "/a", "a/b/c", "a//b", "Local/a", "a/B", "a-/b"];

    private static readonly string[] CamelInputs =
        ["my-lib", "my-", "-x", "a--b", "a-1", "plain", "", "x-y-z", "kebab-Case", "a-é"];

    private static readonly string[] KebabInputs =
        ["macosArm64", "linuxX64", "host", "ABC", "", "aZ", "é", "x-y", "lowerUpperLower"];

    private static readonly Lazy<(List<(string Name, string Path)> Cases, Dictionary<string, List<string>> Sections)> Run = new(() =>
    {
        var cases = AllCases();
        var main = MainSource();
        var args = new List<string>();
        foreach (var (name, path) in cases)
        {
            args.Add(name);
            args.Add(path);
        }
        return (cases, SelfhostSemanticsHarness.RunSections(main, [.. args]));
    });

    private static List<(string Name, string Path)> AllCases()
    {
        Directory.CreateDirectory(CasesDir);
        var cases = new List<(string, string)>();
        var manifestDir = Path.Combine(Golden.CasesRoot, "manifest");
        foreach (var path in Directory.GetFiles(manifestDir, "*.byn").Order(StringComparer.Ordinal))
            cases.Add(($"manifest/{Path.GetFileNameWithoutExtension(path)}", path));
        foreach (var dir in new[] { "capability-exceeded", "grammar-dictionary" })
            cases.Add(($"diag/{dir}", Path.Combine(Golden.CasesRoot, "diag", dir, "package.byn")));
        foreach (var (name, text) in Inline)
        {
            var path = Path.Combine(CasesDir, name + ".byn");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            cases.Add(($"inline/{name}", path));
        }
        cases.Add(("unreadable", Path.Combine(CasesDir, "no-such-dir", "package.byn")));
        return cases;
    }

    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '\\' || c == '"') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string MainSource()
    {
        var sb = new StringBuilder(Prelude);
        sb.AppendLine("void predicates() {");
        sb.AppendLine("    print(\"== versions\")");
        for (var i = 0; i < Versions.Length; i++)
            sb.AppendLine($"    print($\"{i} {{yn(b: t10IsSemanticVersion({Literal(Versions[i])}))}}\")");
        sb.AppendLine("    print(\"== kebab names\")");
        for (var i = 0; i < Kebabs.Length; i++)
            sb.AppendLine($"    print($\"{i} {{yn(b: t10IsKebabName({Literal(Kebabs[i])}.bytes))}}\")");
        sb.AppendLine("    print(\"== package names\")");
        for (var i = 0; i < PackageNames.Length; i++)
            sb.AppendLine($"    print($\"{i} {{yn(b: t10IsPackageName({Literal(PackageNames[i])}))}}\")");
        sb.AppendLine("    print(\"== camelOf\")");
        for (var i = 0; i < CamelInputs.Length; i++)
            sb.AppendLine($"    print($\"{i} {{hexOf(camelOf({Literal(CamelInputs[i])}))}}\")");
        sb.AppendLine("    print(\"== kebabOf\")");
        for (var i = 0; i < KebabInputs.Length; i++)
            sb.AppendLine($"    print($\"{i} {{hexOf(kebabOf({Literal(KebabInputs[i])}))}}\")");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private const string Prelude = """
        import lexer
        import parser

        string yn(bool b) => b ? "yes" : "no"

        string hexOf(string s) {
            let digits = "0123456789abcdef"
            var sb = StrBuf()
            sb.append("x")
            for b in s.bytes {
                let hi = i64(b >> 4)
                let lo = i64(b & 15)
                sb.append(digits[hi..hi + 1])
                sb.append(digits[lo..lo + 1])
            }
            return $"{sb.finish()}"
        }

        bool lessBytes(string a, by string b) {
            let x = a.bytes
            let y = b.bytes
            var i: i64 = 0
            while i < x.length && i < y.length {
                if x[i] != y[i] { return x[i] < y[i] }
                i += 1
            }
            return x.length < y.length
        }

        void printWords(string label, by in List<string> words) {
            var sb = StrBuf()
            sb.append(label)
            for w in words { sb.append($" {hexOf(w)}") }
            print($"{sb.finish()}")
        }

        List<u8> readOrEmpty(string path) {
            let bytes = Io.readFile(path) catch return List<u8>()
            return bytes
        }

        SourceFile makeFile(move List<u8> text) {
            let empty = List<u8>()
            let source = lexer.SourceText.create("<t10>", from: move empty)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            return SourceFile {
                path: "package.byn",
                displayPath: "package.byn",
                text: move text,
                unit: move unit,
                imports: Map<string, i64>(),
            }
        }

        string kindName(PackageKind k) => k switch {
            .app => "App",
            .lib => "Lib",
        }

        string booleansName(BooleanStyle b) => b switch {
            .symbols => "Symbols",
            .words => "Words",
        }

        string primitivesName(PrimitiveStyle p) => p switch {
            .sized => "Sized",
            .csharp => "Csharp",
        }

        void showCase(string name, at string path) {
            let m = manifestRead(path)
            print($"== case {name}")
            print($"errors {manifestErrorCount(in m)}")
            if m.diagnostics.length > 0 {
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                }
                c.files.append(makeFile(readOrEmpty(path)))
                for d in m.diagnostics {
                    var copy = t10CopyDiag(in d)
                    copy.file = 0
                    c.diagnostics.append(move copy)
                }
                var omitted: i64 = 0
                let rendered = renderAll(in c, from: in c.diagnostics, by: false, until: 1000000, to: ref omitted)
                var sb = StrBuf()
                sb.append(rendered)
                sb.append("\n")
                Io.stdout.write(sb.finish())
                return
            }
            print($"name {m.name}")
            print($"version {m.version}")
            print($"edition {m.edition}")
            print($"kind {kindName(m.kind)}")
            var targets = StrBuf()
            targets.append("targets")
            for t in m.distTargets { targets.append($" {t}") }
            print($"{targets.finish()}")
            print($"mode {m.distMode}")
            print($"booleans {booleansName(m.booleans)}")
            print($"primitives {primitivesName(m.primitives)}")
            if (m.capabilities is { } caps) {
                printWords("capabilities", by: in caps)
            } else {
                print("capabilities -")
            }
            printWords("verbs", by: in m.grammar.verbs)
            printWords("nouns", by: in m.grammar.nouns)
            printWords("adjectives", by: in m.grammar.adjectives)
            var forms = List<string>()
            for (k, v) in m.grammar.forms { forms.append($"{hexOf(k)}={hexOf(v)}") }
            for i in 1..forms.length {
                var j = i
                while j > 0 && lessBytes(forms[j], by: forms[j - 1]) {
                    let a = forms[j]
                    forms[j] = forms[j - 1]
                    forms[j - 1] = a
                    j -= 1
                }
            }
            var formsBuf = StrBuf()
            formsBuf.append("forms")
            for f in forms { formsBuf.append($" {f}") }
            print($"{formsBuf.finish()}")
            print($"short {t10ShortName(in m)}")
            print($"entry {t10EntryModule(in m)}")
        }

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 1 < args.length {
                showCase(args[i], at: args[i + 1])
                i += 2
            }
            predicates()
            return 0
        }

        """;

    private static string Hex(string s) => "x" + Convert.ToHexString(Encoding.UTF8.GetBytes(s)).ToLowerInvariant();

    private static string Words(string label, IEnumerable<string> words) =>
        label + string.Concat(words.Select(w => " " + Hex(w)));

    private static List<string> Lines(string text) =>
        text.Split('\n').Where(l => l.Length > 0).ToList();

    /// <summary>The oracle's lines for one manifest: the C# reader's diagnostics, or the fields it read.</summary>
    private static List<string> Expected(string path)
    {
        var m = File.Exists(path) ? Manifest.Read(path) : Manifest.Parse(SourceText.FromString("package.byn", ""));
        var lines = new List<string> { $"errors {m.Diagnostics.Count(d => d.IsError)}" };
        if (m.Diagnostics.Count > 0)
        {
            lines.AddRange(Lines(DiagnosticRenderer.RenderAll(m.Diagnostics)));
            return lines;
        }
        lines.Add($"name {m.Name}");
        lines.Add($"version {m.Version}");
        lines.Add($"edition {m.Edition}");
        lines.Add($"kind {m.Kind}");
        lines.Add("targets" + string.Concat(m.DistTargets.Select(t => " " + t)));
        lines.Add($"mode {m.DistMode}");
        lines.Add($"booleans {m.Booleans}");
        lines.Add($"primitives {m.Primitives}");
        lines.Add(m.Capabilities is null ? "capabilities -" : Words("capabilities", m.Capabilities));
        lines.Add(Words("verbs", m.Grammar.Verbs));
        lines.Add(Words("nouns", m.Grammar.Nouns));
        lines.Add(Words("adjectives", m.Grammar.Adjectives));
        var forms = m.Grammar.Forms.Select(f => $"{Hex(f.Key)}={Hex(f.Value)}").Order(StringComparer.Ordinal);
        lines.Add("forms" + string.Concat(forms.Select(f => " " + f)));
        lines.Add($"short {m.ShortName}");
        lines.Add($"entry {Bayan.Cli.CliApp.EntryModule(m)}");
        return lines;
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(Golden.CasesRoot, "manifest"), "*.byn").Order(StringComparer.Ordinal))
            data.Add($"manifest/{Path.GetFileNameWithoutExtension(path)}");
        data.Add("diag/capability-exceeded");
        data.Add("diag/grammar-dictionary");
        foreach (var (name, _) in Inline) data.Add($"inline/{name}");
        data.Add("unreadable");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ManifestMatchesCSharp(string name)
    {
        var (cases, sections) = Run.Value;
        var path = cases.Single(c => c.Name == name).Path;
        SelfhostSemanticsHarness.AssertSection(sections, $"case {name}", Expected(path));
    }

    /// <summary>The two <c>diag/</c> projects read without diagnostics, so the checker gets their fields.</summary>
    [Fact]
    public void DiagPackagesReadClean()
    {
        var (_, sections) = Run.Value;
        Assert.Equal("errors 0", sections["case diag/capability-exceeded"][0]);
        Assert.Contains("capabilities x6e6574", sections["case diag/capability-exceeded"]);
        Assert.Equal("errors 0", sections["case diag/grammar-dictionary"][0]);
        Assert.Contains("entry app", sections["case diag/grammar-dictionary"]);
    }

    [Fact]
    public void LibraryEntryModuleIsCamelShortName()
    {
        var (_, sections) = Run.Value;
        Assert.Contains("entry myLib", sections["case inline/full"]);
        Assert.Contains("entry app", sections["case inline/app-defaults"]);
    }

    [Fact]
    public void SemanticVersions() =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Sections, "versions",
            Versions.Select((v, i) => $"{i} {(Manifest.IsSemanticVersion(v) ? "yes" : "no")}").ToList());

    [Fact]
    public void KebabNames() =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Sections, "kebab names",
            Kebabs.Select((v, i) => $"{i} {(Manifest.IsKebabName(v) ? "yes" : "no")}").ToList());

    [Fact]
    public void PackageNamesMatch() =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Sections, "package names",
            PackageNames.Select((v, i) => $"{i} {(Manifest.IsPackageName(v) ? "yes" : "no")}").ToList());

    /// <summary>ASCII inputs match C#; <c>a-é</c> differs by design (ASCII casing, Q566).</summary>
    [Fact]
    public void CamelOfMatches() =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Sections, "camelOf",
            CamelInputs.Select((v, i) => $"{i} {Hex(v == "a-é" ? "aé" : Manifest.CamelOf(v))}").ToList());

    [Fact]
    public void KebabOfMatches() =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Sections, "kebabOf",
            KebabInputs.Select((v, i) => $"{i} {Hex(Manifest.KebabOf(v))}").ToList());
}
