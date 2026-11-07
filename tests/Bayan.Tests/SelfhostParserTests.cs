using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary><c>selfhost/parser</c>, built by the bootstrap compiler and run against the C# parser.</summary>
public sealed class SelfhostParserTests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>
    /// The built <c>bayan-parse</c>, or the compiler's complaint. The package root is <c>selfhost/</c> so that
    /// <c>import lexer</c> resolves; <see cref="Compilation.FromPath"/> would use <c>selfhost/parser/</c> (Q430).
    /// </summary>
    private static readonly Lazy<(string? Path, string Error)> BayanParse = new(() =>
        BuildParser(Directory.GetFiles(ParserDir, "*.byn").Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f)).ToList()));

    /// <summary><paramref name="sources"/> built as the parser module, or the compiler's complaint.</summary>
    private static (string? Path, string Error) BuildParser(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, sources.Where(s => !File.Exists(s.Path)).Select(s => s.Path).ToHashSet(StringComparer.Ordinal));
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    internal static string Built
    {
        get
        {
            var (path, error) = BayanParse.Value;
            if (path is null) Assert.Fail($"selfhost/parser did not build:\n{error}");
            return path!;
        }
    }

    private const string SpanLine = "span out of range: ";

    /// <summary><paramref name="section"/> up to its first line that starts with L018's <c>span out of range: </c>
    /// (<c>--many</c> appends <c>checkSpans</c> output after each dump); the text inside a dump line does not count.</summary>
    internal static string BeforeSpanLines(string section)
    {
        if (section.StartsWith(SpanLine, StringComparison.Ordinal)) return "";
        var k = section.IndexOf("\n" + SpanLine, StringComparison.Ordinal);
        return k < 0 ? section : section[..(k + 1)];
    }

    [Fact]
    public void SpanLinesAreCutOnlyAtALineStart()
    {
        Assert.Equal("(LiteralExpr \"span out of range: x\")\n", BeforeSpanLines("(LiteralExpr \"span out of range: x\")\n"));
        Assert.Equal("CompilationUnit\n", BeforeSpanLines("CompilationUnit\nspan out of range: 1..9 outside 0..4\n"));
        Assert.Equal("", BeforeSpanLines("span out of range: 1..9\n"));
    }

    /// <summary>Runs <paramref name="program"/>; under <c>BAYAN_SNAPSHOT</c> the run is recorded (spec 013 R14). Stdout is returned as written:
    /// the runtime writes LF on every OS (Q742), so a <c>\r\n</c> in it is the program's own output, such as a dump of a CRLF input.</summary>
    internal static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        if (SelfhostSemanticsHarness.SnapshotDir is { } dir)
        {
            var (exit, o, e) = SelfhostSemanticsHarness.RunRecorded(dir, program, null, args);
            return (exit, Encoding.UTF8.GetString(o), Encoding.UTF8.GetString(e));
        }
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary><see cref="Run"/> with stdout kept as bytes, so invalid UTF-8 is compared as printed.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, params string[] args) => RunBytes(program, null, args);

    /// <summary><see cref="RunBytes(string, string[])"/> with <paramref name="input"/> fed to stdin.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, byte[]? input, params string[] args)
    {
        if (SelfhostSemanticsHarness.SnapshotDir is { } dir)
        {
            var (exit, o, e) = SelfhostSemanticsHarness.RunRecorded(dir, program, input, args);
            return (exit, o, Encoding.UTF8.GetString(e));
        }
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndLfAsync();
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result);
    }

    /// <summary>The stubbed program builds through <c>import lexer</c> and prints the C# dump of an empty file.</summary>
    [Fact]
    public void EmptyFileDumpsAnEmptyCompilationUnit()
    {
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser", "empty.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        var (exit, stdout, stderr) = Run(Built, path);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal("CompilationUnit\n", stdout);
        Assert.Equal(AstDump.Dump(SyntaxTree.Parse(SourceText.FromFile(path))), stdout);
    }

    /// <summary>
    /// The node shapes of <c>nodes.byn</c> (Q435): <c>FunctionDecl</c> → <c>BlockStmt</c> → <c>BinaryExpr</c> of two
    /// <c>LiteralExpr</c>, built under <c>provide allocator: arena</c> with a <c>List&lt;Stmt&gt;</c>, <c>none</c>
    /// children and field defaults, then read back through <c>.value</c>. The parser's files stand in for the
    /// module, with this <c>main</c> in place of <c>main.byn</c>.
    /// </summary>
    [Fact]
    public void NodeShapesBuildAndReadBack()
    {
        const string program = """
            string exprText(in Expr e) {
                switch e {
                    case .literalExpr(let n):
                        return n.value.text
                    case .binaryExpr(let n):
                        return $"({exprText(in n.value.left)} {n.value.op} {exprText(in n.value.right)})"
                    case .none:
                        return "none"
                    default:
                        return "?"
                }
            }

            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    let one = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 26, length: 1 }, kind: .integer, text: "1", suffix: null }))
                    let two = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 30, length: 1 }, kind: .integer, text: "2", suffix: null }))
                    var statements = List<Stmt>()
                    statements.append(Stmt.returnStmt(Owned(ReturnStmt {
                        span: TextSpan { start: 19, length: 12 },
                        value: Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 26, length: 5 }, op: .add, left: move one, right: move two })),
                    })))
                    statements.append(Stmt.returnStmt(Owned(ReturnStmt { span: TextSpan { start: 32, length: 6 }, value: .none })))
                    var path = List<TextSpan>()
                    path.append(TextSpan { start: 0, length: 3 })
                    var members = List<Decl>()
                    members.append(Decl.functionDecl(Owned(FunctionDecl {
                        span: TextSpan { start: 0, length: 40 },
                        modifiers: modifierPublic | modifierStatic,
                        returnType: TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: 0, length: 3 }, path: move path, args: List<TypeSyntax>() })),
                        name: TextSpan { start: 4, length: 1 },
                        params: List<Param>(),
                        failsKeyword: null,
                        body: Stmt.blockStmt(Owned(BlockStmt { span: TextSpan { start: 8, length: 32 }, statements: move statements })),
                        expressionBody: .none,
                    })))
                    let unit = CompilationUnit { span: TextSpan { start: 0, length: 40 }, members: move members }
                    for d in unit.members {
                        switch d {
                            case .functionDecl(let f):
                                print($"function {f.value.span.start}..{f.value.span.end} static {(f.value.modifiers & modifierStatic) != 0} params {f.value.params.length} typeParams {f.value.typeParams.length}")
                                switch f.value.returnType {
                                    case .namedType(let t):
                                        print($"returns path {t.value.path.length} args {t.value.args.length}")
                                    default:
                                        print("returns ?")
                                }
                                switch f.value.body {
                                    case .blockStmt(let b):
                                        print($"block {b.value.span.start} statements {b.value.statements.length}")
                                        for s in b.value.statements {
                                            switch s {
                                                case .returnStmt(let r):
                                                    print($"return {exprText(in r.value.value)}")
                                                default:
                                                    print("statement ?")
                                            }
                                        }
                                    default:
                                        print("body ?")
                                }
                                print($"expressionBody {exprText(in f.value.expressionBody)}")
                            default:
                                print("decl ?")
                        }
                    }
                }
            }
            """;
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "nodes-test.byn"), program)).ToList();
        var (path, error) = BuildParser(sources);
        if (path is null) Assert.Fail($"the node shapes did not build:\n{error}");
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            function 0..40 static true params 0 typeParams 0
            returns path 1 args 0
            block 8 statements 2
            return (1 add 2)
            return none
            expressionBody none

            """, stdout);
    }

    /// <summary>The parser's files with a <c>main</c> that prints every statement under each function's body as
    /// <c>kind start end</c>, the kind being the C# record name.</summary>
    private const string StatementSpansProgram = """
        import lexer

        void spans(in Stmt s, to ref StrBuf sb) {
            switch s {
                case .blockStmt(let n):
                    sb.append($"BlockStmt {n.value.span.start} {n.value.span.end}\n")
                    for st in n.value.statements { spans(in st, to: ref sb) }
                case .localDecl(let n):
                    sb.append($"LocalDecl {n.value.span.start} {n.value.span.end}\n")
                case .exprStmt(let n):
                    sb.append($"ExprStmt {n.value.span.start} {n.value.span.end}\n")
                case .assignStmt(let n):
                    sb.append($"AssignStmt {n.value.span.start} {n.value.span.end}\n")
                case .incDecStmt(let n):
                    sb.append($"IncDecStmt {n.value.span.start} {n.value.span.end}\n")
                case .returnStmt(let n):
                    sb.append($"ReturnStmt {n.value.span.start} {n.value.span.end}\n")
                case .failStmt(let n):
                    sb.append($"FailStmt {n.value.span.start} {n.value.span.end}\n")
                case .breakStmt(let n):
                    sb.append($"BreakStmt {n.value.span.start} {n.value.span.end}\n")
                case .continueStmt(let n):
                    sb.append($"ContinueStmt {n.value.span.start} {n.value.span.end}\n")
                case .becomeStmt(let n):
                    sb.append($"BecomeStmt {n.value.span.start} {n.value.span.end}\n")
                case .ifStmt(let n):
                    sb.append($"IfStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.then, to: ref sb)
                    spans(in n.value.elseKeyword, to: ref sb)
                case .unlessStmt(let n):
                    sb.append($"UnlessStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .guardedStmt(let n):
                    sb.append($"GuardedStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.statement, to: ref sb)
                case .whileStmt(let n):
                    sb.append($"WhileStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .doWhileStmt(let n):
                    sb.append($"DoWhileStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .whileLetStmt(let n):
                    sb.append($"WhileLetStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .forStmt(let n):
                    sb.append($"ForStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .labeledStmt(let n):
                    sb.append($"LabeledStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.statement, to: ref sb)
                case .switchStmt(let n):
                    sb.append($"SwitchStmt {n.value.span.start} {n.value.span.end}\n")
                    for sec in n.value.sections {
                        for st in sec.statements { spans(in st, to: ref sb) }
                    }
                case .deferStmt(let n):
                    sb.append($"DeferStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .assertStmt(let n):
                    sb.append($"AssertStmt {n.value.span.start} {n.value.span.end}\n")
                case .contextStmt(let n):
                    sb.append($"ContextStmt {n.value.span.start} {n.value.span.end}\n")
                    spans(in n.value.body, to: ref sb)
                case .emptyStmt(let n):
                    sb.append($"EmptyStmt {n.value.span.start} {n.value.span.end}\n")
                case .errorStmt(let n):
                    sb.append($"ErrorStmt {n.value.span.start} {n.value.span.end}\n")
                case .none:
                    return
            }
        }

        i32 main(ReadOnlySpan<string> args) {
            let keywords = Keywords.build()
            var bytes = Io.readFile(args[0]) catch return 4
            var p = Parser.create(lexer.lex(SourceText.create(args[0], from: move bytes), by: keywords))
            var unit = parseCompilationUnit(ref p)
            var sb = StrBuf()
            for d in unit.members {
                switch d {
                    case .functionDecl(let f):
                        spans(in f.value.body, to: ref sb)
                    default:
                        continue
                }
            }
            Io.stdout.write(sb.finish())
            return 0
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> StatementSpans = new(() =>
        BuildParser(Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f))
            .Append(SourceText.FromString(Path.Combine(ParserDir, "statement-spans-test.byn"), StatementSpansProgram)).ToList()));

    /// <summary>Every statement under the C# function bodies as <c>kind start end</c>, children after their parent
    /// in the order <see cref="StatementSpansProgram"/> visits them.</summary>
    private static void CSharpStatementSpans(Stmt? s, List<string> lines)
    {
        if (s is null) return;
        lines.Add($"{s.GetType().Name} {s.Span.Start} {s.Span.End}");
        switch (s)
        {
            case BlockStmt b: foreach (var st in b.Statements) CSharpStatementSpans(st, lines); break;
            case IfStmt i: CSharpStatementSpans(i.Then, lines); CSharpStatementSpans(i.Else, lines); break;
            case UnlessStmt u: CSharpStatementSpans(u.Body, lines); break;
            case GuardedStmt g: CSharpStatementSpans(g.Statement, lines); break;
            case WhileStmt w: CSharpStatementSpans(w.Body, lines); break;
            case DoWhileStmt d: CSharpStatementSpans(d.Body, lines); break;
            case WhileLetStmt w: CSharpStatementSpans(w.Body, lines); break;
            case ForStmt f: CSharpStatementSpans(f.Body, lines); break;
            case LabeledStmt l: CSharpStatementSpans(l.Statement, lines); break;
            case SwitchStmt sw: foreach (var st in sw.Sections.SelectMany(sec => sec.Statements)) CSharpStatementSpans(st, lines); break;
            case DeferStmt d: CSharpStatementSpans(d.Body, lines); break;
            case ContextStmt c: CSharpStatementSpans(c.Body, lines); break;
        }
    }

    /// <summary>R7: with statement attributes, every statement's span from the port equals the C# span, including
    /// statements ended by <c>;</c> and by a rejected postfix condition; the expected spans come from the C# tree.</summary>
    [Theory]
    [InlineData("diag/statement-attribute.byn")]
    [InlineData("diag/statement-attribute-because.byn")]
    [InlineData("diag/statement-attribute-semicolon.byn")]
    public void StatementSpansMatchTheCSharpParser(string name)
    {
        var (path, error) = StatementSpans.Value;
        if (path is null) Assert.Fail($"the statement spans program did not build:\n{error}");
        var input = Path.Combine(Golden.CasesRoot, name);
        var tree = SyntaxTree.Parse(SourceText.FromFile(input));
        var expected = new List<string>();
        foreach (var f in tree.Root.Members.OfType<FunctionDecl>()) CSharpStatementSpans(f.Body, expected);
        var (exit, stdout, stderr) = Run(path!, input);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(string.Join("", expected.Select(l => l + "\n")), stdout);
    }

    public static TheoryData<string> ParseCases => Golden.Cases("parse");

    /// <summary>The <c>diag/</c> cases whose <c>.diag</c> holds a parser diagnostic; each <c>.byn</c> is parsed alone.</summary>
    public static TheoryData<string> DiagCases
    {
        get
        {
            var data = new TheoryData<string>();
            var dir = Path.Combine(Golden.CasesRoot, "diag");
            foreach (var diag in Directory.EnumerateFiles(dir, "*.diag", SearchOption.AllDirectories)
                                         .Where(f => File.ReadAllText(f).Contains("BYN1", StringComparison.Ordinal))
                                         .Order(StringComparer.Ordinal))
            {
                var byn = Path.ChangeExtension(diag, ".byn");
                if (File.Exists(byn)) data.Add(Path.GetRelativePath(Golden.CasesRoot, byn).Replace('\\', '/'));
            }
            return data;
        }
    }

    /// <summary>R1, R6: <c>bayan-parse</c> prints each <c>parse/</c> golden byte-for-byte and agrees on the exit code.</summary>
    [Theory]
    [MemberData(nameof(ParseCases))]
    public void ParseGolden(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var tree = SyntaxTree.Parse(SourceText.FromFile(input));
        var (exit, stdout, stderr) = Run(Built, input);
        output.WriteLine(stderr);
        if (Golden.Compare(Path.ChangeExtension(input, ".ast"), stdout, update: false).Failure is { } failure) Assert.Fail(failure);
        Assert.Equal(tree.Diagnostics.Any(d => d.IsError) ? 1 : 0, exit);
    }

    /// <summary>
    /// R6: each diag case with a parser diagnostic is parsed alone and must dump exactly like the C#
    /// parser, whose output is written under <c>tmp/parser-ref/</c> so <see cref="Golden.Compare"/>
    /// names the input and the first differing line.
    /// </summary>
    [Theory]
    [MemberData(nameof(DiagCases))]
    public void DiagGolden(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var tree = SyntaxTree.Parse(SourceText.FromFile(input));
        var refPath = Path.Combine(Golden.RepoRoot, "tmp", "parser-ref", name.Replace('/', '_') + ".ast");
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, AstDump.Dump(tree));
        var (exit, stdout, stderr) = Run(Built, input);
        output.WriteLine(stderr);
        if (Golden.Compare(refPath, stdout, update: false).Failure is { } failure) Assert.Fail(failure);
        Assert.Equal(tree.Diagnostics.Any(d => d.IsError) ? 1 : 0, exit);
    }

    /// <summary>010 T45: inputs the C# parser once threw on (<c>ParserTests.TruncatedInputReportsWithoutThrowing</c>) dump as the C# parser
    /// dumps them, whose output is written under <c>tmp/parser-ref/</c>, and agree on the exit code.</summary>
    [Theory]
    [InlineData("first-primitive-name", "int()fails")]
    [InlineData("first-primitive-name-group", "int(\"\"{])fails")]
    [InlineData("if-comment-at-end", "if(/*")]
    [InlineData("if-control-at-end", "if(\u0017")]
    [InlineData("while-control-at-end", "while(\u0017")]
    [InlineData("unless-unclosed-group", "unless ((((((((((((((((((()")]
    public void TruncatedInputDumpsAsTheCSharpParser(string name, string source) => DumpsAsTheCSharpParser("truncated", name, source);

    /// <summary>010 T45: the rest of <c>expectName</c> and <c>parseBody</c>: a C# primitive name past the first token, after a
    /// <c>.</c> or not, and a braceless body that is itself an error.</summary>
    [Theory]
    [InlineData("primitive-name-later", "let int = 1\n")]
    [InlineData("primitive-member", "let x = a.int\n")]
    [InlineData("primitive-qualified-type", "let x: a.int = 1\n")]
    [InlineData("primitive-function", "void f() {}\nvoid int() {}\n")]
    [InlineData("braceless-error-body", "if (a) )\n")]
    [InlineData("braceless-error-body-while", "while (a) ]\nf()\n")]
    [InlineData("braceless-nested-fix", "if (a) if (b) f()\n")]
    [InlineData("primitive-module-path", "import a.int\n")]
    [InlineData("braceless-else-body", "if (a) else\n")]
    [InlineData("braceless-goto-case-body", "while (a) goto case 1\nf()\n")]
    [InlineData("braceless-empty-body-if", "void main() {\n    if (x) }\n")]
    [InlineData("braceless-empty-body-unless", "void main() {\n    unless (!x) }\n")]
    public void BodyAndNameEdgesDumpAsTheCSharpParser(string name, string source) => DumpsAsTheCSharpParser("body-name", name, source);

    /// <summary><paramref name="source"/>, written under <c>tmp/parser-&lt;kind&gt;/</c>, dumps as the C# parser dumps it (written under
    /// <c>tmp/parser-ref/</c>), and the exit codes agree.</summary>
    private void DumpsAsTheCSharpParser(string kind, string name, string source)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "parser-" + kind);
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, name + ".byn");
        File.WriteAllText(input, source);
        var tree = SyntaxTree.Parse(SourceText.FromFile(input));
        var refPath = Path.Combine(Golden.RepoRoot, "tmp", "parser-ref", kind + "_" + name + ".ast");
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, AstDump.Dump(tree));
        var (exit, stdout, stderr) = Run(Built, input);
        output.WriteLine(stderr);
        if (Golden.Compare(refPath, stdout, update: false).Failure is { } failure) Assert.Fail(failure);
        Assert.Equal(tree.Diagnostics.Any(d => d.IsError) ? 1 : 0, exit);
    }

    /// <summary>Q511: C# takes these nodes' spans before it parses their last child (argument order), so the child
    /// lies outside its parent; the port's <c>checkSpans</c> reports the same child under L018.</summary>
    [Theory]
    [InlineData("not-pattern", "let b = x is not null\n", "constantPattern 17+4 outside 13+3")]
    [InlineData("var-pattern", "let b = x is (1, var y)\n", "varPattern name 21+1 outside 17+3")]
    [InlineData("block-property", "struct S {\n    i32 n { return 1 }\n}\n", "blockStmt 21+12 outside 15+5")]
    public void SpanTakenBeforeTheLastChildLeavesItOutside(string name, string source, string expected)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "parser-span-order", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".byn"), source);
        var (_, stdout, stderr) = Run(Built, "--many", dir);
        output.WriteLine(stdout + stderr);
        Assert.Contains(SpanLine, stdout, StringComparison.Ordinal);
        Assert.Contains(expected, stdout, StringComparison.Ordinal);
    }

    /// <summary>The <c>--codes</c> table matches the C# <see cref="DiagnosticCodes"/> descriptors (Q433: 70 codes).</summary>
    [Fact]
    public void CodesMatchDiagnosticCodes()
    {
        var (exit, stdout, stderr) = Run(Built, "--codes");
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.Equal(70, lines.Length);
        var byCode = DiagnosticCodes.All.ToDictionary(d => d.Code);
        foreach (var line in lines)
        {
            var tab = line.IndexOf('\t');
            Assert.True(tab > 0, $"no tab in --codes line: {line}");
            var code = line[..tab];
            var message = line[(tab + 1)..];
            Assert.True(byCode.TryGetValue(code, out var descriptor), $"no C# descriptor for {code}");
            Assert.True(descriptor.MessageFormat == message || (code == "BYN1042" && message == PostfixOnNewLineMessage),
                $"{code} message differs\n  C#:    {descriptor.MessageFormat}\n  bayan: {message}");
        }
    }

    private const string PostfixOnNewLineMessage = "a postfix `{0}` must be on the line of its statement";

    /// <summary>R8: <c>bayan-parse</c>, built in fast mode, parses and dumps 1 MB within 10x the C# parser.</summary>
    [Fact]
    public void OneMegabyteWithinTenTimes()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_SKIP_TIMING") == "1")
        {
            output.WriteLine("skipped: BAYAN_SKIP_TIMING=1");
            return;
        }
        var source = OneMegabyte();
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-timing", "big.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, source.Bytes.ToArray());

        var bayan = new long[5];
        for (var i = 0; i < bayan.Length; i++)
        {
            var (exit, stdout, stderr) = Run(Built, "--time", path);
            Assert.True(exit == 0, stdout + stderr);
            bayan[i] = long.Parse(stdout.Trim());
        }
        SyntaxTree.Parse(source); // warm up statics and JIT
        var csharp = new long[5];
        for (var i = 0; i < csharp.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            AstDump.Dump(SyntaxTree.Parse(source));
            csharp[i] = (long)watch.Elapsed.TotalNanoseconds;
        }
        var bayanMedian = bayan.Order().ElementAt(2);
        var csharpMedian = csharp.Order().ElementAt(2);
        var ratio = (double)bayanMedian / csharpMedian;
        output.WriteLine($"bayan-parse {bayanMedian} ns, C# {csharpMedian} ns, ratio {ratio:F2}");
        Assert.True(ratio <= 10, $"bayan-parse median {bayanMedian} ns is {ratio:F2}x the C# median {csharpMedian} ns");
    }

    /// <summary>T14's generator 1 MB of valid code (R8).</summary>
    private static SourceText OneMegabyte() => SelfhostParserGenerator.OneMegabyte();
}
