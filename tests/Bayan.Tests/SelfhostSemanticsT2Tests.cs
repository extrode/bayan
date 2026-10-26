using System.Globalization;
using System.Reflection;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// <c>selfhost/semantics/codes.byn</c> (the semantic message table) and <c>diag.byn</c> (the
/// diagnostic sink and the plain renderer), checked against C# through one generated driver.
/// </summary>
public sealed class SelfhostSemanticsT2Tests
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string SemanticsDir => Path.Combine(SelfhostDir, "semantics");

    private static string DataDir => Path.Combine(Golden.RepoRoot, "tmp", "sema-t2");

    /// <summary>
    /// The fixture driver, compiled as the module's <c>main.byn</c>. It reads a length-prefixed
    /// record file (`M` mode, `F` file, `D` diagnostic, `Q` render query, `X` removeRepeats) and
    /// prints what the port does with it, so the tests compare one process run against C#.
    /// </summary>
    private const string Driver = """
        // The T2 fixture driver: reads a record file and prints the port's renderer, removeRepeats
        // and sink results. Every field is `<byte length>:<bytes>`; numbers and bools are decimal.
        import lexer
        import parser

        struct Reader {
            List<u8> bytes
            i64 pos
        }

        bool atEnd(ref Reader r) => r.pos >= r.bytes.length

        u8 nextKind(ref Reader r) {
            let kind = r.bytes[r.pos]
            r.pos += 1
            return kind
        }

        /// Reads a `<decimal length>:` header and returns the length.
        i64 readHeader(ref Reader r) {
            var value: i64 = 0
            while r.pos < r.bytes.length && r.bytes[r.pos] != 0x3A {
                value = value * 10 + i64(r.bytes[r.pos] - 0x30)
                r.pos += 1
            }
            if r.pos < r.bytes.length { r.pos += 1 }
            return value
        }

        string readField(ref Reader r) {
            let n = readHeader(ref r)
            let start = r.pos
            var stop = start + n
            if stop > r.bytes.length { stop = r.bytes.length }
            r.pos = stop
            return utf8(r.bytes.span[start..stop])
        }

        i64 parseInt(string text) {
            let t = text.bytes
            var value: i64 = 0
            var negative = false
            var i: i64 = 0
            if t.length > 0 && t[0] == 0x2D {
                negative = true
                i = 1
            }
            while i < t.length {
                value = value * 10 + i64(t[i] - 0x30)
                i += 1
            }
            return negative ? -value : value
        }

        i64 readInt(ref Reader r) => parseInt(readField(ref r))

        bool readBool(ref Reader r) => readField(ref r) == "1"

        SourceFile makeFile(string display, by string text) {
            let empty = List<u8>()
            let source = lexer.SourceText.create("<t2>", from: move empty)
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

        Diag readDiag(ref Reader r) {
            let code = readField(ref r)
            let file = readInt(ref r)
            let start = readInt(ref r)
            let length = readInt(ref r)
            let message = readField(ref r)
            let label = readField(ref r)
            let help = readField(ref r)
            let warning = readBool(ref r)
            let hasFix = readBool(ref r)
            let fixStart = readInt(ref r)
            let fixLength = readInt(ref r)
            let fixReplacement = readField(ref r)
            let relatedCount = readInt(ref r)
            var related = List<Related>()
            for i in 0..relatedCount {
                let rStart = readInt(ref r)
                let rLength = readInt(ref r)
                let rLabel = readField(ref r)
                related.append(Related { span: TextSpan { start: rStart, length: rLength }, label: rLabel })
            }
            return Diag {
                code: code,
                file: file,
                span: TextSpan { start: start, length: length },
                message: message,
                label: label,
                help: help,
                isWarning: warning,
                hasFix: hasFix,
                fixSpan: TextSpan { start: fixStart, length: fixLength },
                fixReplacement: fixReplacement,
                related: move related,
            }
        }

        Ctx emptyCtx() => Ctx {
            files: List<SourceFile>(),
            modules: List<Mod>(),
            diagnostics: List<Diag>(),
            bags: List<DiagBag>(),
        }

        SourceFile readFile(ref Reader r) {
            let display = readField(ref r)
            let text = readField(ref r)
            return makeFile(display, by: text)
        }

        i32 runRender(ref Reader r) {
            var c = emptyCtx()
            var section: i64 = 0
            while !atEnd(ref r) {
                let kind = nextKind(ref r)
                if kind == 0x46 {
                    c.files.append(readFile(ref r))
                } else if kind == 0x44 {
                    c.diagnostics.append(readDiag(ref r))
                } else if kind == 0x51 {
                    let includePath = readBool(ref r)
                    let errorLimit = readInt(ref r)
                    let clear = readBool(ref r)
                    var omitted: i64 = 0
                    let rendered = renderAll(in c, from: in c.diagnostics, by: includePath, until: errorLimit, to: ref omitted)
                    var sb = StrBuf()
                    sb.append($"==T2Q== {section} {omitted}\n")
                    sb.append(rendered)
                    Io.stdout.write(sb.finish())
                    section += 1
                    if clear { c = emptyCtx() }
                }
            }
            return 0
        }

        i32 runRemove(ref Reader r) {
            var c = emptyCtx()
            while !atEnd(ref r) {
                let kind = nextKind(ref r)
                if kind == 0x46 {
                    c.files.append(readFile(ref r))
                } else if kind == 0x44 {
                    c.diagnostics.append(readDiag(ref r))
                } else if kind == 0x58 {
                    removeRepeats(ref c)
                    for d in c.diagnostics {
                        print($"{d.code}|{d.file}|{d.span.start}|{d.span.length}|{d.message}")
                    }
                    print($"==T2N== {c.diagnostics.length}")
                }
            }
            return 0
        }

        (string a0, string a1, string a2, string a3, string a4) noArgs() =>
            (a0: "", a1: "", a2: "", a3: "", a4: "")

        /// Reports with no file into no bag, one bag, two nested bags, after each `popBag`, and
        /// while quiet, then prints where each diagnostic landed.
        i32 runSink() {
            var c = emptyCtx()
            report(ref c, for: .undefinedName, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            let outer = pushBag(ref c)
            report(ref c, for: .unknownType, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            let inner = pushBag(ref c)
            report(ref c, for: .unknownMember, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            popBag(ref c, to: inner)
            report(ref c, for: .notVisible, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            popBag(ref c, to: outer)
            report(ref c, for: .shadowing, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            c.quiet = true
            report(ref c, for: .duplicateDeclaration, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            c.quiet = false
            report(ref c, for: .importCycle, from: -1, at: TextSpan { start: 0, length: 1 }, by: noArgs())
            var sb = StrBuf()
            sb.append("top")
            for d in c.diagnostics { sb.append($" {d.code}") }
            sb.append("\n")
            for i in 0..c.bags.length {
                sb.append($"bag{i} {c.bags[i].previous}")
                for d in c.bags[i].items { sb.append($" {d.code}") }
                sb.append("\n")
            }
            Io.stdout.write(sb.finish())
            return 0
        }

        /// Reports with every argument and with only the required ones, then renders both, so the
        /// defaults and the field wiring of `report` are compared against C#.
        i32 runReport() {
            var c = emptyCtx()
            c.files.append(makeFile("r.byn", by: "let total = 0\nlet count = 1\n"))
            report(ref c, for: .undefinedName, from: 0, at: TextSpan { start: 4, length: 5 },
                by: (a0: "total", a1: "", a2: "", a3: "", a4: ""), label: "not in scope", help: "declare it first")
            report(ref c, for: .unreachableCode, from: 0, at: TextSpan { start: 4, length: 5 }, by: noArgs())
            report(ref c, for: .undefinedName, from: 0, at: TextSpan { start: 18, length: 5 }, by: noArgs(),
                label: "here", hasFix: true, fixSpan: TextSpan { start: 18, length: 5 }, fixReplacement: "value")
            var omitted: i64 = 0
            let text = renderAll(in c, from: in c.diagnostics, by: true, until: 1000, to: ref omitted)
            var sb = StrBuf()
            sb.append(text)
            sb.append($"==T2R== {c.diagnostics.length} {omitted}\n")
            Io.stdout.write(sb.finish())
            return 0
        }

        i32 runCodes() {
            for code in allSemaCodes() {
                let warning = isWarning(code) ? "1" : "0"
                print($"{codeText(code)}\t{warning}\t{messageFormat(code)}")
            }
            return 0
        }

        i32 runFormat() {
            let args = (a0: "a0", a1: "a1", a2: "a2", a3: "a3", a4: "a4")
            for code in allSemaCodes() {
                print($"{codeText(code)}\t{format(messageFormat(code), by: args)}")
            }
            return 0
        }

        i32 main(ReadOnlySpan<string> args) {
            if args.length < 1 { return 2 }
            let bytes = Io.readFile(args[0]) catch return 1
            var r = Reader { bytes: move bytes, pos: 0 }
            if atEnd(ref r) { return 1 }
            let _ = nextKind(ref r)
            let mode = readField(ref r)
            if mode == "render" { return runRender(ref r) }
            if mode == "remove" { return runRemove(ref r) }
            if mode == "sink" { return runSink() }
            if mode == "report" { return runReport() }
            if mode == "codes" { return runCodes() }
            if mode == "format" { return runFormat() }
            return 3
        }
        """;

    /// <summary>The built driver, or the compiler's complaint; the package root is <c>selfhost/</c>.</summary>
    private static readonly Lazy<(string? Path, string Error)> BayanCheck = new(() =>
    {
        var sources = Directory.GetFiles(SemanticsDir, "*.byn")
            .Where(f => Path.GetFileName(f) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .Append(SourceText.FromString("main.byn", Driver))
            .ToList();
        return Build(sources);
    });

    private static (string? Path, string Error) Build(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static string Built
    {
        get
        {
            var (path, error) = BayanCheck.Value;
            if (path is null) Assert.Fail($"selfhost/semantics did not build:\n{error}");
            return path!;
        }
    }

    /// <summary>Runs the driver on <paramref name="dataPath"/> and returns its stdout.</summary>
    private static string Run(string dataPath)
    {
        var (exit, output, error) = SelfhostParserTests.Run(Built, dataPath);
        Assert.True(exit == 0, $"the T2 driver exited {exit}: {error}");
        return output;
    }

    /// <summary>The driver's record file: a kind byte, then `<byte length>:<bytes>` fields.</summary>
    private sealed class DataWriter
    {
        private readonly MemoryStream _bytes = new();

        public void Record(char kind) => _bytes.WriteByte((byte)kind);

        public void Text(string? value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value ?? "");
            Length(utf8.Length);
            _bytes.WriteByte((byte)':');
            _bytes.Write(utf8);
        }

        public void Number(long value) => Text(value.ToString(CultureInfo.InvariantCulture));

        public void Bool(bool value) => Text(value ? "1" : "0");

        private void Length(int value)
        {
            foreach (var b in Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture))) _bytes.WriteByte(b);
        }

        public string Save(string name)
        {
            Directory.CreateDirectory(DataDir);
            var path = Path.Combine(DataDir, name);
            File.WriteAllBytes(path, _bytes.ToArray());
            return path;
        }
    }

    /// <summary>Compiles one <c>tests/cases/diag</c> case the way <see cref="DiagTests"/> does.</summary>
    private static (Compilation Compilation, bool IncludePath) CompileCase(string caseName)
    {
        var path = Path.Combine(Golden.CasesRoot, caseName);
        var dir = Path.GetDirectoryName(path)!;
        var manifest = File.Exists(Path.Combine(dir, "package.byn")) ? Manifest.Read(Path.Combine(dir, "package.byn")) : null;
        var c = Compilation.Create([Golden.Load(path)], dir, new CompilationOptions(
            IncludeTests: path.EndsWith(".test.byn", StringComparison.Ordinal),
            Capabilities: manifest?.Capabilities,
            PackageName: manifest?.Name));
        return (c, Path.GetFileName(dir) != "diag");
    }

    /// <summary>Writes `F` records for every source then `D` records for every diagnostic.</summary>
    private static List<SourceText> WriteItems(DataWriter w, IReadOnlyList<Diagnostic> items)
    {
        var files = new List<SourceText>();
        foreach (var d in items)
            if (d.Source is { } source && FileIndex(files, source) < 0) files.Add(source);
        foreach (var source in files)
        {
            w.Record('F');
            w.Text(source.Path);
            w.Text(Encoding.UTF8.GetString(source.Bytes.Span));
        }
        foreach (var d in items)
        {
            w.Record('D');
            w.Text(d.Code);
            w.Number(d.Source is { } source ? FileIndex(files, source) : -1);
            w.Number(d.Span.Start);
            w.Number(d.Span.Length);
            w.Text(d.Message);
            w.Text(d.Label);
            w.Text(d.Help);
            w.Bool(d.Severity == Severity.Warning);
            w.Bool(d.Fix is not null);
            w.Number(d.Fix?.Span.Start ?? 0);
            w.Number(d.Fix?.Span.Length ?? 0);
            w.Text(d.Fix?.Replacement);
            w.Number(d.Related?.Count ?? 0);
            foreach (var r in d.Related ?? [])
            {
                w.Number(r.Span.Start);
                w.Number(r.Span.Length);
                w.Text(r.Label);
            }
        }
        return files;
    }

    private static int FileIndex(List<SourceText> files, SourceText source)
    {
        for (var i = 0; i < files.Count; i++)
            if (ReferenceEquals(files[i], source)) return i;
        return -1;
    }

    private static void WriteQuery(DataWriter w, bool includePath, int errorLimit, bool clear)
    {
        w.Record('Q');
        w.Bool(includePath);
        w.Number(errorLimit);
        w.Bool(clear);
    }

    /// <summary>The `==T2Q== <section> <omitted>` headers and the rendered text between them.</summary>
    private static List<(int Omitted, string Text)> ReadSections(string output)
    {
        var sections = new List<(int, string)>();
        const string prefix = "==T2Q==";
        var at = output.IndexOf(prefix, StringComparison.Ordinal);
        while (at >= 0)
        {
            var lineEnd = output.IndexOf('\n', at);
            Assert.True(lineEnd >= 0, "the driver printed an unterminated section header");
            var parts = output[(at + prefix.Length)..lineEnd].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var omitted = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var bodyStart = lineEnd + 1;
            var next = output.IndexOf(prefix, bodyStart, StringComparison.Ordinal);
            var body = next < 0 ? output[bodyStart..] : output[bodyStart..next];
            sections.Add((omitted, body));
            at = next;
        }
        return sections;
    }

    private static bool IsSemaCode(string code) =>
        int.Parse(code.AsSpan(3), CultureInfo.InvariantCulture) is >= 2000 and <= 8999;

    /// <summary>BYN2000–BYN8999 plus the three BYN1xxx codes the checker reports.</summary>
    private static bool IsT2Code(string code) =>
        IsSemaCode(code) || code is "BYN1016" or "BYN1054" or "BYN1056";

    /// <summary>
    /// The C# table the port must reproduce: `DiagnosticCodes.All` for the semantic ranges and the
    /// three BYN1xxx codes, plus the three second message forms. Those three are declared
    /// `new(...)`, not `Error(...)`, so they stay out of `All`, but `Naming.cs` and
    /// `BinderStates.cs` report them and the port carries them (Q527). Each goes right after the
    /// registered descriptor with the same code, in `DiagnosticCodes.cs` order.
    /// </summary>
    private static List<DiagnosticDescriptor> SemanticTable()
    {
        var table = DiagnosticCodes.All.Where(d => IsT2Code(d.Code)).ToList();
        foreach (var name in new[] { "ConversionMember", "BecomeOtherState", "BecomesCallUndeclared" })
        {
            var field = typeof(DiagnosticCodes).GetField(name, BindingFlags.Public | BindingFlags.Static)!;
            var extra = (DiagnosticDescriptor)field.GetValue(null)!;
            table.Insert(table.FindLastIndex(d => d.Code == extra.Code) + 1, extra);
        }
        return table;
    }

    [Fact]
    public void Builds()
    {
        Assert.True(File.Exists(Built));
    }

    /// <summary>Every `.diag` golden's diagnostics render byte for byte like `DiagnosticRenderer`.</summary>
    [Fact]
    public void EveryDiagCaseRendersLikeCSharp()
    {
        var expected = new List<(string Name, string Text)>();
        var w = new DataWriter();
        w.Record('M');
        w.Text("render");
        foreach (var name in Golden.Cases("diag"))
        {
            var (c, includePath) = CompileCase(name);
            WriteItems(w, c.Diagnostics.Items);
            WriteQuery(w, includePath, int.MaxValue, clear: true);
            expected.Add((name, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath)));
        }
        // Two files sort by display path, which is `a.byn` before `b.byn` whatever the report order.
        var fileA = SourceText.FromString("a.byn", "let x = 1\n");
        var fileB = SourceText.FromString("b.byn", "let y = 2\n");
        var multi = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", fileB, new TextSpan(4, 1), "`y` is not defined"),
            new(Severity.Error, "BYN2001", fileA, new TextSpan(4, 1), "`x` is not defined"),
        };
        WriteItems(w, multi);
        WriteQuery(w, includePath: true, int.MaxValue, clear: true);
        expected.Add(("multi", DiagnosticRenderer.RenderAll(multi, includePath: true)));

        // Four files whose display paths share a prefix and are reported out of ordinal order, so
        // the sort must compare past the first byte (the C# ordinal order is a/x, a/y, ab, b/x).
        var prefix = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", SourceText.FromString("b/x.byn", "let bx = 1\n"), new TextSpan(4, 2), "`bx` is not defined"),
            new(Severity.Error, "BYN2001", SourceText.FromString("a/y.byn", "let ay = 1\n"), new TextSpan(4, 2), "`ay` is not defined"),
            new(Severity.Error, "BYN2001", SourceText.FromString("a/x.byn", "let ax = 1\n"), new TextSpan(4, 2), "`ax` is not defined"),
            new(Severity.Error, "BYN2001", SourceText.FromString("ab.byn", "let ab = 1\n"), new TextSpan(4, 2), "`ab` is not defined"),
        };
        WriteItems(w, prefix);
        WriteQuery(w, includePath: true, int.MaxValue, clear: true);
        expected.Add(("prefix", DiagnosticRenderer.RenderAll(prefix, includePath: true)));

        // 2-, 3- and 4-byte scalars, a control character and a tab in the prefix exercise the UTF-8
        // decoder and `display` at every sequence length; the trailing U+F8FF has lead byte 0xEF, the
        // top of the 3-byte range, so a span over it exercises the decoder's upper bound too.
        var unicodeText = "\tlet s = \"\u0B82\U00010000\u0001\u00E9\uF8FF\"\n";
        var unicode = SourceText.FromString("u.byn", unicodeText);
        var unicodeStart = Encoding.UTF8.GetByteCount(unicodeText[..unicodeText.IndexOf('\u0B82')]);
        var efStart = Encoding.UTF8.GetByteCount(unicodeText[..unicodeText.IndexOf('\uF8FF')]);
        var scalars = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", unicode, new TextSpan(unicodeStart, 3), "`s` is not defined", "label `in code`"),
            new(Severity.Error, "BYN2001", unicode, new TextSpan(efStart, 3), "`p` is not defined"),
        };
        WriteItems(w, scalars);
        WriteQuery(w, includePath: true, int.MaxValue, clear: true);
        expected.Add(("unicode", DiagnosticRenderer.RenderAll(scalars, includePath: true)));

        // A diagnostic with no source renders its header line only, with or without includePath.
        var synthetic = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", null, default, "`x` is not defined"),
            new(Severity.Warning, "BYN2002", null, new TextSpan(3, 2), "unknown type `y`"),
            // A message long enough to wrap past the first line, which uses a narrower width.
            new(Severity.Error, "BYN2001", null, default,
                "`a` is not defined and this message is long enough that it must wrap onto the second and third lines so that the first line's narrower width is compared against the rest width"),
        };
        WriteItems(w, synthetic);
        WriteQuery(w, includePath: true, int.MaxValue, clear: true);
        expected.Add(("synthetic", DiagnosticRenderer.RenderAll(synthetic, includePath: true)));
        var sections = ReadSections(Run(w.Save("render.dat")));
        Assert.Equal(expected.Count, sections.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var (omitted, text) = sections[i];
            Assert.Equal(0, omitted);
            Assert.True(expected[i].Text == text,
                $"case {expected[i].Name} differs:\n--- expected ---\n{expected[i].Text}\n--- actual ---\n{text}");
        }
    }

    /// <summary>The error cap stops at the limit, counts what it skipped, and never skips a warning.</summary>
    [Fact]
    public void ErrorLimitCapsErrorsNotWarnings()
    {
        var cases = new List<(string Name, Compilation Compilation, bool IncludePath)>();
        foreach (var name in Golden.Cases("diag"))
        {
            var (c, includePath) = CompileCase(name);
            if (c.Diagnostics.Items.Any(d => d.IsError) && c.Diagnostics.Items.Any(d => d.Severity == Severity.Warning))
                cases.Add((name, c, includePath));
            if (cases.Count == 3) break;
        }
        Assert.Equal(3, cases.Count);
        var limits = new[] { 0, 1, 2, int.MaxValue };
        var expected = new List<string>();
        var w = new DataWriter();
        w.Record('M');
        w.Text("render");
        foreach (var (_, c, includePath) in cases)
        {
            foreach (var limit in limits)
            {
                WriteItems(w, c.Diagnostics.Items);
                WriteQuery(w, includePath, limit, clear: true);
                expected.Add(DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath, Style.Plain, limit, out _));
            }
        }
        var sections = ReadSections(Run(w.Save("limits.dat")));
        Assert.Equal(expected.Count, sections.Count);
        var at = 0;
        foreach (var (name, c, includePath) in cases)
        {
            foreach (var limit in limits)
            {
                var (omitted, text) = sections[at];
                var want = DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath, Style.Plain, limit, out var wantOmitted);
                Assert.True(want == text, $"case {name} with limit {limit} differs:\n--- expected ---\n{want}\n--- actual ---\n{text}");
                Assert.Equal(wantOmitted, omitted);
                at++;
            }
        }
    }

    /// <summary>`removeRepeats` keeps the first of each (code, file, span, message) group.</summary>
    [Fact]
    public void RemoveRepeatsMatchesCSharp()
    {
        var a = SourceText.FromString("a.byn", "let a = 1\n");
        var b = SourceText.FromString("b.byn", "let a = 1\n");
        var span = new TextSpan(0, 3);
        var input = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", a, span, "`a` is not defined"),
            new(Severity.Error, "BYN2001", a, span, "`a` is not defined"),
            new(Severity.Error, "BYN2002", a, span, "`a` is not defined"),
            new(Severity.Error, "BYN2001", b, span, "`a` is not defined"),
            new(Severity.Error, "BYN2001", a, new TextSpan(0, 4), "`a` is not defined"),
            new(Severity.Error, "BYN2001", a, new TextSpan(1, 2), "`a` is not defined"),
            new(Severity.Error, "BYN2001", a, span, "`b` is not defined"),
            new(Severity.Warning, "BYN2001", a, span, "`a` is not defined"),
            new(Severity.Error, "BYN2001", null, span, "no source"),
            new(Severity.Error, "BYN2001", null, span, "no source"),
        };
        var bag = new DiagnosticBag();
        foreach (var d in input) bag.Add(d);
        bag.RemoveRepeats();

        var w = new DataWriter();
        w.Record('M');
        w.Text("remove");
        var files = WriteItems(w, input);
        w.Record('X');
        var actual = Run(w.Save("repeats.dat"));
        var text = new StringBuilder();
        foreach (var d in bag.Items)
            text.Append($"{d.Code}|{(d.Source is { } s ? FileIndex(files, s) : -1)}|{d.Span.Start}|{d.Span.Length}|{d.Message}\n");
        text.Append($"==T2N== {bag.Items.Count}\n");
        Assert.Equal(text.ToString(), actual);
    }

    /// <summary>`report` formats the code and wires every optional field; render them both ways.</summary>
    [Fact]
    public void ReportBuildsTheDiag()
    {
        var w = new DataWriter();
        w.Record('M');
        w.Text("report");
        var source = SourceText.FromString("r.byn", "let total = 0\nlet count = 1\n");
        var items = new List<Diagnostic>
        {
            new(Severity.Error, "BYN2001", source, new TextSpan(4, 5), "`total` is not defined", "not in scope", "declare it first"),
            new(Severity.Warning, "BYN2405", source, new TextSpan(4, 5), "unreachable code"),
            new(Severity.Error, "BYN2001", source, new TextSpan(18, 5), "`` is not defined", "here", null, new Fix(new TextSpan(18, 5), "value")),
        };
        var expected = DiagnosticRenderer.RenderAll(items, includePath: true) + "==T2R== 3 0\n";
        Assert.Equal(expected, Run(w.Save("report.dat")));
    }

    /// <summary>Reports go to the innermost open bag, else `Ctx.diagnostics`, and nowhere while quiet.</summary>
    [Fact]
    public void SinkGoesToTheInnermostBag()
    {
        var w = new DataWriter();
        w.Record('M');
        w.Text("sink");
        Assert.Equal(
            "top BYN2001 BYN2007 BYN2010\nbag0 -1 BYN2002 BYN2005\nbag1 0 BYN2003\n",
            Run(w.Save("sink.dat")));
    }

    /// <summary>The ported table is `DiagnosticCodes.All` for BYN2000–BYN8999 plus the three BYN1 codes.</summary>
    [Fact]
    public void CodeTableMatchesDiagnosticCodes()
    {
        var w = new DataWriter();
        w.Record('M');
        w.Text("codes");
        var expected = new StringBuilder();
        foreach (var d in SemanticTable())
            expected.Append($"{d.Code}\t{(d.Severity == Severity.Warning ? "1" : "0")}\t{d.MessageFormat}\n");
        Assert.Equal(expected.ToString(), Run(w.Save("codes.dat")));
    }

    /// <summary>`format` is `string.Format` for every template: `{0}`–`{4}`, `{{`/`}}` unescaped.</summary>
    [Fact]
    public void FormatMatchesStringFormat()
    {
        var w = new DataWriter();
        w.Record('M');
        w.Text("format");
        var expected = new StringBuilder();
        foreach (var d in SemanticTable())
            expected.Append($"{d.Code}\t{d.Format("a0", "a1", "a2", "a3", "a4")}\n");
        Assert.Equal(expected.ToString(), Run(w.Save("format.dat")));
    }
}
