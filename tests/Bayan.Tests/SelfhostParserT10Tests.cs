using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// 009 T10: <c>selfhost/parser/exprs2.byn</c> in isolation. The siblings' parsing files are stubs on this branch,
/// so the program replaces <c>exprs.byn</c> with a <c>parseExpression</c> that is <c>parsePrimary</c> and
/// prints its own S-expressions in place of <c>dump2.byn</c>. Full dumps against the C# are T15's.
/// </summary>
public sealed class SelfhostParserT10Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private const string Program = """
        import lexer

        void span(TextSpan s, from ref Parser p, to ref StrBuf sb) {
            sb.append(p.lexed.source.getText(s))
        }

        void expr(in Expr e, from ref Parser p, to ref StrBuf sb) {
            switch e {
                case .literalExpr(let n):
                    sb.append($"(lit {n.value.kind} {n.value.text}")
                    let suffix = n.value.suffix
                    if suffix is not null {
                        sb.append(" ")
                        span(suffix, from: ref p, to: ref sb)
                    }
                    sb.append(")")
                case .nameExpr(let n):
                    sb.append("(name ")
                    span(n.value.name, from: ref p, to: ref sb)
                    sb.append(")")
                case .thisExpr(_):
                    sb.append("this")
                case .targetMemberExpr(let n):
                    sb.append("(target ")
                    span(n.value.member, from: ref p, to: ref sb)
                    sb.append(")")
                case .structLiteralExpr(let n):
                    sb.append("(struct")
                    switch n.value.type {
                        case .namedType(let t):
                            for s in t.value.path {
                                sb.append(" ")
                                span(s, from: ref p, to: ref sb)
                            }
                        default:
                            sb.append(" _")
                    }
                    for f in n.value.fields {
                        sb.append(" ")
                        span(f.name, from: ref p, to: ref sb)
                        sb.append("=")
                        expr(in f.value, from: ref p, to: ref sb)
                    }
                    sb.append(")")
                case .parenExpr(let n):
                    sb.append("(paren ")
                    expr(in n.value.inner, from: ref p, to: ref sb)
                    sb.append(")")
                case .tupleExpr(let n):
                    sb.append("(tuple")
                    for el in n.value.elements {
                        sb.append(" ")
                        let name = el.name
                        if name is not null {
                            span(name, from: ref p, to: ref sb)
                            sb.append(":")
                        }
                        expr(in el.value, from: ref p, to: ref sb)
                    }
                    sb.append(")")
                case .collectionExpr(let n):
                    sb.append("(list")
                    for el in n.value.elements {
                        sb.append(" ")
                        expr(in el, from: ref p, to: ref sb)
                    }
                    sb.append(")")
                case .spreadExpr(let n):
                    sb.append("(spread ")
                    expr(in n.value.operand, from: ref p, to: ref sb)
                    sb.append(")")
                case .mapEntryExpr(let n):
                    sb.append("(entry ")
                    expr(in n.value.key, from: ref p, to: ref sb)
                    sb.append(" ")
                    expr(in n.value.value, from: ref p, to: ref sb)
                    sb.append($" {n.value.span.start}..{n.value.span.end})")
                case .lambdaExpr(let n):
                    sb.append("(lambda")
                    for s in n.value.params {
                        sb.append(" ")
                        span(s, from: ref p, to: ref sb)
                    }
                    sb.append(" ")
                    expr(in n.value.body, from: ref p, to: ref sb)
                    sb.append(")")
                case .interpolatedExpr(let n):
                    sb.append("(interp")
                    for part in n.value.parts {
                        switch part {
                            case .interpolationText(let t):
                                sb.append(" text")
                            case .interpolationHole(let h):
                                sb.append(" (hole ")
                                expr(in h.value.expr, from: ref p, to: ref sb)
                                for a in h.value.specs {
                                    sb.append(" ")
                                    let name = a.name
                                    if name is not null { span(name, from: ref p, to: ref sb) }
                                    sb.append("=")
                                    expr(in a.value, from: ref p, to: ref sb)
                                }
                                sb.append(")")
                        }
                    }
                    sb.append(")")
                case .stackallocExpr(let n):
                    sb.append("(stackalloc ")
                    switch n.value.element {
                        case .namedType(let t):
                            span(t.value.path[0], from: ref p, to: ref sb)
                        default:
                            sb.append("?")
                    }
                    sb.append(" ")
                    expr(in n.value.count, from: ref p, to: ref sb)
                    sb.append(")")
                case .patternExpr(_):
                    sb.append("pattern")
                case .errorExpr(_):
                    sb.append("error")
                case .none:
                    sb.append("none")
                default:
                    sb.append("?")
            }
        }

        void main() {
            let keywords = Keywords.build()
            let inputs = [
                "42u", "1.5", "'a'", "true", "false", "null", "this", "never", ".red", "foo",
                "Point { x: 1, y: 2 }", "Point { x = 1 }", "Order.paid { at: 3 }", "point { x: 1 }",
                "(1)", "(a: 1, 2)", "()", "[1, ..xs, k: v]", "x => 1", "(a, b) => 2", "(a, int b) => 3", "(, a) => 4",
                "$\"a{x}b\"", "$\"{n, as: .hex}\"", "$\"{n:hex}\"", "$\"{n:.2}\"", "$\"{n:wat}\"",
                "stackalloc u8[4]", "stackalloc char[2]", "pattern Foo", "+",
            ]
            for input in inputs {
                var bytes = List<u8>()
                for b in input.bytes { bytes.append(b) }
                var p = Parser.create(lexer.lex(SourceText.create("t.byn", from: move bytes), by: keywords))
                let e = parsePrimary(ref p)
                var sb = StrBuf()
                expr(in e, from: ref p, to: ref sb)
                sb.append($" @{p.pos}")
                for d in p.lexed.diagnostics {
                    sb.append($" [{d.code} {d.span.start}+{d.span.length} {d.message}")
                    if d.hasFix { sb.append($" fix={d.fixReplacement}") }
                    sb.append("]")
                }
                print(sb.finish())
            }
        }
        """;

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>Each primary form of <c>ParsePrimary</c>, with the diagnostics and fixes it reports.</summary>
    [Fact]
    public void PrimariesParseAsInCSharp()
    {
        var sources = Directory.GetFiles(ParserDir, "*.byn")
            .Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t10-test.byn"), Program)).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) Assert.Fail(DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        if (!build.Succeeded) Assert.Fail(build.Errors);
        var (exit, stdout, stderr) = Run(build.ExecutablePath!);
        output.WriteLine(stderr);
        output.WriteLine(stdout);
        Assert.Equal(0, exit);
        Assert.Equal("""
            (lit integer 42 u) @2
            (lit floatKeyword 1.5) @1
            (lit char 'a') @1
            (lit trueKeyword true) @1
            (lit falseKeyword false) @1
            (lit nullKeyword null) @1
            this @1
            (name never) @1
            (target red) @2
            (name foo) @1
            (struct Point x=(lit integer 1) y=(lit integer 2)) @10
            (struct Point x=(lit integer 1)) @6 [BYN1021 10+1 `x = …` in a literal is an input form; write `x: …` fix=:]
            (struct Order paid at=(lit integer 3)) @8
            (name point) @1
            (paren (lit integer 1)) @3
            (tuple a:(lit integer 1) (lit integer 2)) @7
            error @2 [BYN1002 1+1 expected an expression, found `)`]
            (list (lit integer 1) (spread (name xs)) (entry (name k) (name v) 10..12)) @10
            (lambda x (lit integer 1)) @3
            (lambda a b (lit integer 2)) @7
            (lambda a (lit integer 3)) @8 [BYN1001 4+5 expected a lambda parameter name, found `int b`]
            (lambda a (lit integer 4)) @6 [BYN1001 1+1 expected a lambda parameter name, found `,`]
            (interp text (hole (name x)) text) @7
            (interp (hole (name n) as=(target hex))) @10
            (interp (hole (name n) :=(target hex))) @7 [BYN1068 4+4 `:hex` is an input form; write `, as: .hex` fix=, as: .hex]
            (interp (hole (name n) :=(lit integer 2))) @8 [BYN1068 4+3 `:.2` is an input form; write `, digits: 2` fix=, digits: 2]
            (interp (hole (name n))) @7 [BYN1001 4+4 expected a named format spec such as `, as: .hex`, found `:wat`]
            (stackalloc u8 (lit integer 4)) @5
            (stackalloc char (lit integer 2)) @5 [BYN1048 11+4 `char` is now `rune` fix=rune]
            pattern @2 [BYN1046 0+7 `pattern` is allowed only after `=` in a `const`]
            error @0 [BYN1002 0+1 expected an expression, found `+`]

            """, stdout);
    }
}
