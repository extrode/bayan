using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// 009 T9: <c>selfhost/parser/exprs.byn</c> on its own. The primaries (T10) are stubs on this branch, so the program
/// replaces <c>exprs2.byn</c> with a <c>parsePrimary</c> for names, integer and string literals and parentheses, and
/// <c>main.byn</c> with a driver that parses the expression after <c>x =</c> and prints it in <c>AstDump.Expr</c>'s
/// form with its span and diagnostics. Each line is compared with the C# parse of the same input.
/// </summary>
public sealed class SelfhostParserT9Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary><c>ParserTests.Precedence</c> rows whose primaries are names, numbers and parentheses, and a few more.</summary>
    private static readonly string[] Inputs =
    [
        "x = 1 + 2 * 3",
        "x = a - b - c",
        "x = a ?? b ?? c",
        "x = a || b && c",
        "x = a == b < c",
        "x = a >> 1",
        "x = a > b",
        "x = 0..n - 1",
        "x = a | b ^ c & d",
        "x = -a * b",
        "x = c ? a : b ? d : e",
        "x = i64(a) + b",
        "x = (a) + b",
        "x = xs[^1]",
        "x = xs[1..^1]",
        "x = xs[..3]",
        "x = xs[2..]",
        "x = a +% b *| c",
        "x = f(a, b: 2, ref c)",
        "x = a < b",
        "x = a?.b.c",
        "x = try f() + 1",
        "x = (try f()) + 1",
        "x = f() catch 0",
        "x = a ?? return 1",
        "x = new Point()",
        "x = a and b or not c",
        "x = a in 0..=9",
        "x = move a + b",
        "x = a ?? break",
        "x = a ?? fail b",
        "x = f(for: a, as: b, move c, out d, in e)",
        "x = f() catch (e) 0",
        "x = f() catch (g()) 0",
        "x = trust(a, reason: \"r\")",
        "x = trust(a + b, reason: c)",
        "x = a <<\n  b\n",
        "x = a\n  ?? b\n",
        "x = a +\n\nb\n",
    ];

    private const string Driver = """
        import lexer

        string modifierText(ArgModifier m) => m switch {
            .refKeyword => "ref ", .moveKeyword => "move ", .outKeyword => "out ", .inKeyword => "in ", .none => "",
        }

        void show(in Expr e, from ref Parser p, to ref StrBuf sb) {
            switch e {
                case .none:
                    sb.append("_")
                case .nameExpr(let n):
                    sb.append(p.lexed.source.getText(n.value.name))
                case .literalExpr(let n):
                    sb.append(p.lexed.source.getText(n.value.span))
                case .parenExpr(let n):
                    sb.append("(paren ")
                    show(in n.value.inner, from: ref p, to: ref sb)
                    sb.append(")")
                case .binaryExpr(let n):
                    sb.append($"({binaryText(n.value.op)} ")
                    show(in n.value.left, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.right, from: ref p, to: ref sb)
                    sb.append(")")
                case .unaryExpr(let n):
                    sb.append($"({unaryText(n.value.op)} ")
                    show(in n.value.operand, from: ref p, to: ref sb)
                    sb.append(")")
                case .rangeExpr(let n):
                    sb.append(n.value.inclusive ? "(..= " : "(.. ")
                    show(in n.value.start, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.end, from: ref p, to: ref sb)
                    sb.append(")")
                case .inExpr(let n):
                    sb.append("(in ")
                    show(in n.value.value, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.range, from: ref p, to: ref sb)
                    sb.append(")")
                case .conditionalExpr(let n):
                    sb.append("(? ")
                    show(in n.value.condition, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.whenTrue, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.whenFalse, from: ref p, to: ref sb)
                    sb.append(")")
                case .indexExpr(let n):
                    sb.append("(index ")
                    show(in n.value.target, from: ref p, to: ref sb)
                    sb.append(" ")
                    show(in n.value.index, from: ref p, to: ref sb)
                    sb.append(")")
                case .tryExpr(let n):
                    sb.append("(try ")
                    show(in n.value.operand, from: ref p, to: ref sb)
                    sb.append(")")
                case .moveExpr(let n):
                    sb.append("(move ")
                    show(in n.value.operand, from: ref p, to: ref sb)
                    sb.append(")")
                case .memberExpr(let n):
                    sb.append(n.value.nullConditional ? "(?. " : "(. ")
                    show(in n.value.target, from: ref p, to: ref sb)
                    sb.append($" {p.lexed.source.getText(n.value.member)})")
                case .callExpr(let n):
                    sb.append("(call ")
                    show(in n.value.callee, from: ref p, to: ref sb)
                    for i in 0..n.value.args.length {
                        sb.append(" ")
                        let named = n.value.args[i].name is not null
                        let wrapped = named || n.value.args[i].modifier != .none
                        if wrapped { sb.append("(") }
                        if named { sb.append($"{p.lexed.source.getText(n.value.args[i].name ?? TextSpan { start: 0, length: 0 })}: ") }
                        sb.append(modifierText(n.value.args[i].modifier))
                        show(in n.value.args[i].value, from: ref p, to: ref sb)
                        if wrapped { sb.append(")") }
                    }
                    sb.append(")")
                case .catchExpr(let n):
                    sb.append("(catch ")
                    show(in n.value.operand, from: ref p, to: ref sb)
                    if n.value.binding is not null { sb.append($" ({p.lexed.source.getText(n.value.binding ?? TextSpan { start: 0, length: 0 })})") }
                    switch n.value.variant {
                        case .none:
                            sb.append("")
                        default:
                            sb.append(" (variant ")
                            show(in n.value.variant, from: ref p, to: ref sb)
                            sb.append(")")
                    }
                    sb.append(" ")
                    show(in n.value.handler, from: ref p, to: ref sb)
                    sb.append(")")
                case .jumpExpr(let n):
                    switch n.value.jump {
                        case .returnStmt(let r):
                            sb.append("(return")
                            switch r.value.value {
                                case .none:
                                    sb.append("")
                                default:
                                    sb.append(" ")
                                    show(in r.value.value, from: ref p, to: ref sb)
                            }
                            sb.append(")")
                        case .failStmt(let f):
                            sb.append("(fail ")
                            show(in f.value.error, from: ref p, to: ref sb)
                            sb.append(")")
                        case .breakStmt(let b):
                            sb.append(b.value.label is not null ? $"(break {p.lexed.source.getText(b.value.label ?? TextSpan { start: 0, length: 0 })})" : "(break)")
                        case .continueStmt(let c):
                            sb.append(c.value.label is not null ? $"(continue {p.lexed.source.getText(c.value.label ?? TextSpan { start: 0, length: 0 })})" : "(continue)")
                        default:
                            sb.append("?")
                    }
                default:
                    sb.append("?")
            }
        }

        void parseOne(string text, by Keywords keywords) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            var p = Parser.create(lexer.lex(SourceText.create("t.byn", from: move bytes), by: keywords))
            p.pos = 2
            p.prevEnd = p.lexed.tokens[1].end
            let e = parseExpression(ref p)
            var sb = StrBuf()
            show(in e, from: ref p, to: ref sb)
            let span = exprSpan(in e)
            sb.append($" @{span.start}..{span.end}")
            for d in p.lexed.diagnostics { sb.append($" {d.code}@{d.span.start}..{d.span.end}") }
            print(sb.finish())
        }

        void main() {
            let keywords = Keywords.build()
            using let arena = Arena()
            provide allocator: arena {
                INPUTS
            }
        }
        """;

    private static string CSharpLine(string input)
    {
        var tree = SyntaxTree.Parse(SourceText.FromString("t.byn", input));
        var statement = ((GlobalStatement)tree.Root.Members[0]).Statement;
        var value = statement switch
        {
            AssignStmt a => a.Value,
            _ => throw new InvalidOperationException(statement.GetType().Name),
        };
        return $"{AstDump.Expr(value)} @{value.Span.Start}..{value.Span.End}" +
            string.Concat(tree.Diagnostics.Select(d => $" {d.Code}@{d.Span.Start}..{d.Span.End}"));
    }

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    [Fact]
    public void ExpressionsAgreeWithTheCSharpParser()
    {
        var program = Driver.Replace("INPUTS", string.Join("\n        ", Inputs.Select(i => $"parseOne({Quote(i)}, by: keywords)")));
        var sources = Directory.GetFiles(ParserDir, "*.byn")
            .Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t9-test.byn"), program)).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        var own = c.Diagnostics.Items.Where(d => d.IsError).ToList();
        if (own.Count > 0) Assert.Fail(DiagnosticRenderer.RenderAll(own, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        if (!build.Succeeded) Assert.Fail(build.Errors);

        var (exit, stdout, stderr) = Run(build.ExecutablePath!);
        output.WriteLine(stdout);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        var expected = string.Concat(Inputs.Select(i => CSharpLine(i) + "\n"));
        Assert.Equal(expected, stdout);
    }
}
