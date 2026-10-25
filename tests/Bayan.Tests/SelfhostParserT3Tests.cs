using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// 009 T3: <c>recover.byn</c> and <c>patterns.byn</c> driven directly, with a test <c>main</c> in place of
/// <c>main.byn</c>, so they run before the sibling parsing files are merged. Expected texts follow the C# parser.
/// </summary>
public sealed class SelfhostParserT3Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>The parser's files with <paramref name="program"/> as <c>main</c>, built and run; its stdout.</summary>
    private string RunProgram(string program)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t3-test.byn"), program)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) Assert.Fail($"did not build:\n{DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true)}");
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        if (!build.Succeeded) Assert.Fail($"did not build:\n{build.Errors}");
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        output.WriteLine(stderr.Result);
        output.WriteLine(stdout.Result);
        Assert.Equal(0, p.ExitCode);
        return stdout.Result;
    }

    private const string Prelude = """
        import lexer

        Parser parserOf(string text) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            return Parser.create(lexer.lex(SourceText.create("t.byn", from: move bytes), by: Keywords.build()))
        }

        string diags(ref Parser p) {
            var sb = StrBuf()
            for d in p.lexed.diagnostics {
                sb.append($" [{d.code} {d.span.start}+{d.span.length} {d.message}")
                if d.hasFix {
                    sb.append($" fix {d.fixSpan.start}+{d.fixSpan.length} \"")
                    lexer.escape(d.fixReplacement.bytes, to: ref sb)
                    sb.append("\"")
                }
                sb.append("]")
            }
            return $"{sb.finish()}"
        }

        string patternText(in Pattern pattern, from ref Parser p) {
            switch pattern {
                case .discardPattern(let n):
                    return "_"
                case .varPattern(let n):
                    return $"({n.value.isLet ? "let" : "var"} {p.lexed.source.getText(n.value.name)})"
                case .notPattern(let n):
                    return $"(not {patternText(in n.value.operand, from: ref p)})"
                case .binaryPattern(let n):
                    return $"({n.value.isAnd ? "and" : "or"} {patternText(in n.value.left, from: ref p)} {patternText(in n.value.right, from: ref p)})"
                case .variantPattern(let n):
                    var sb = StrBuf()
                    sb.append($"(variant {n.value.span.start}+{n.value.span.length}")
                    for e in n.value.elements { sb.append($" {patternText(in e, from: ref p)}") }
                    if n.value.binding is not null { sb.append($" bind {p.lexed.source.getText(n.value.binding ?? n.value.span)}") }
                    sb.append(")")
                    return $"{sb.finish()}"
                case .positionalPattern(let n):
                    var sb = StrBuf()
                    sb.append("(positional")
                    for e in n.value.elements { sb.append($" {patternText(in e, from: ref p)}") }
                    sb.append(")")
                    return $"{sb.finish()}"
                case .listPattern(let n):
                    var sb = StrBuf()
                    sb.append($"(list {n.value.span.start}+{n.value.span.length}")
                    for e in n.value.elements { sb.append($" {patternText(in e, from: ref p)}") }
                    sb.append(")")
                    return $"{sb.finish()}"
                case .slicePattern(let n):
                    return $"(.. {patternText(in n.value.inner, from: ref p)})"
                case .propertyPattern(let n):
                    var sb = StrBuf()
                    sb.append("(property")
                    for s in n.value.properties { sb.append($" {s.path.length}:{patternText(in s.pattern, from: ref p)}") }
                    if n.value.designation is not null { sb.append($" as {p.lexed.source.getText(n.value.designation ?? n.value.span)}") }
                    sb.append(")")
                    return $"{sb.finish()}"
                case .constantPattern(let n):
                    return "const"
                case .relationalPattern(let n):
                    return $"(rel {n.value.op})"
                case .none:
                    return "none"
                default:
                    return "?"
            }
        }

        void pattern(string text) {
            var p = parserOf(text)
            let pattern = parsePattern(ref p)
            print($"{patternText(in pattern, from: ref p)} pos {p.pos} noLambda {p.noLambda}{diags(ref p)}")
        }

        """;

    /// <summary>
    /// Every pattern form whose C# parse needs no sibling helper (the two <c>ParseBinary</c> and the
    /// <c>ScanType</c> shims are placeholders until T15, Q441), with spans and the restored <c>noLambda</c>.
    /// </summary>
    [Fact]
    public void PatternsParse()
    {
        var stdout = RunProgram(Prelude + """
            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    pattern("_")
                    pattern("var x or let y and not _")
                    pattern(".some(let v, _)")
                    pattern(".percentOff p")
                    pattern(".percentOff when")
                    pattern("(_)")
                    pattern("(_, var a)")
                    pattern("[_, .., .. var r]")
                    pattern("{ a.b: _, c: var d } e")
                    pattern("{ a: _ ")
                    pattern("< 3")
                    pattern("var int")
                }
            }
            """);
        Assert.Equal(
            """
            _ pos 1 noLambda false
            (or (var x) (and (let y) (not _))) pos 8 noLambda false
            (variant 0+15 (let v) _) pos 8 noLambda false
            (variant 0+13 bind p) pos 3 noLambda false
            const pos 0 noLambda false
            _ pos 3 noLambda false
            (positional _ (var a)) pos 6 noLambda false
            (list 0+17 _ (.. none) (.. (var r))) pos 9 noLambda false
            (property 2:_ 1:(var d) as e) pos 13 noLambda false
            (property 1:_) pos 4 noLambda false [BYN1001 7+0 expected `}`, found end of file]
            (rel lt) pos 1 noLambda false
            (var int) pos 2 noLambda false [BYN0012 4+3 `int` is reserved and cannot be used as an identifier]

            """, stdout);
    }

    /// <summary>
    /// <c>expectEnd</c> and <c>reportSemicolon</c> fixes, <c>synchronize</c> and <c>recoverIfNeeded</c> stopping
    /// points, the line-structure predicates, and <c>unreport</c> leaving a tombstone that <c>errorCount</c> skips
    /// while <c>syntaxErrors</c> and <c>lastErrorAt</c> stay as C# leaves them.
    /// </summary>
    [Fact]
    public void RecoveryAndLineStructure()
    {
        var stdout = RunProgram(Prelude + """
            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    var a = parserOf("  x y")
                    let _ = a.advance()
                    expectEnd(ref a)
                    print($"missing line break{diags(ref a)}")

                    var b = parserOf("  x ; y")
                    let _ = b.advance()
                    expectEnd(ref b)
                    print($"semicolon before y{diags(ref b)}")

                    var c = parserOf("x ;  \ny")
                    let _ = c.advance()
                    expectEnd(ref c)
                    print($"semicolon at line end{diags(ref c)} pos {c.pos}")

                    var d = parserOf("a b { c ; d } e\nlet f")
                    synchronize(ref d)
                    print($"synchronize stops at {d.pos} {d.kind()}")

                    var e = parserOf("a ; b")
                    synchronize(ref e)
                    print($"synchronize past ; at {e.pos}")

                    var f = parserOf("a ) b")
                    let _ = f.advance()
                    recoverIfNeeded(ref f, from: 0)
                    print($"no new error stays at {f.pos}")
                    let _ = f.report(.expected, at: TextSpan { start: 2, length: 1 }, arg0: "x", arg1: "y")
                    recoverIfNeeded(ref f, from: 0)
                    print($"new error skips to {f.pos} {f.kind()}")

                    var g = parserOf("x\n\n  .y\n// c\nz")
                    print($"blank {hasBlankLineBefore(ref g, at: 1)} {hasBlankLineBefore(ref g, at: 3)}")
                    print($"wasl {isWasl(ref g, at: 1)} {isWasl(ref g, at: 3)} breaks {breaksLine(ref g, at: 1)} {breaksLine(ref g, at: 3)}")
                    print($"ends {endsBefore(ref g, at: 2)} {endsBefore(ref g, at: 3)} {endsBefore(ref g, at: 9)}")

                    var h = parserOf("f(a,\n b) +\n where x: c")
                    print($"group {breaksLine(ref h, at: 4)} continues {lineContinues(ref h, at: 7)} {lineContinues(ref h, at: 4)} where {isWasl(ref h, at: 7)}")
                    print($"inGroup {inGroup(ref h, at: 1, for: .comma)} {inGroup(ref h, at: 1, for: .plus)}")
                    h.pos = 7
                    print($"stopsHere {stopsHere(ref h)}")

                    var i = parserOf("(a) b")
                    let first = i.report(.parenthesizedCondition, at: TextSpan { start: 0, length: 3 })
                    let second = i.report(.expected, at: TextSpan { start: 4, length: 1 }, arg0: "x", arg1: "y")
                    unreport(ref i, at: first)
                    unreport(ref i, at: -1)
                    print($"unreport count {i.errorCount()} of {i.lexed.diagnostics.length} syntaxErrors {i.syntaxErrors} lastErrorAt {i.lastErrorAt}{diags(ref i)}")

                    var j = parserOf("error")
                    let isNameError = isName(j.kind())
                    let named = expectName(ref j)
                    let missing = expectName(ref j)
                    print($"names {isNameError} {isName(.structKeyword)} {named.start}+{named.length} missing {missing.start}+{missing.length}{diags(ref j)}")
                    print($"tables {isParticle("until")} {isParticle("of")} {isItName("it")} {isCSharpPrimitive("nuint")} {isCSharpPrimitive("u8")} {isStatementKeyword(.distinctKeyword)} {isContextualKeyword(.effectKeyword)}")
                }
            }
            """);
        Assert.Equal(
            """
            missing line break [BYN1005 3+0 expected a line break after the statement fix 3+1 "`\n  `"]
            semicolon before y [BYN1040 4+1 `;` is an input form; a statement ends at the newline fix 3+3 "`\n  `"]
            semicolon at line end [BYN1040 2+1 `;` is an input form; a statement ends at the newline fix 1+2 "``"] pos 2
            synchronize stops at 7 identifier
            synchronize past ; at 2
            no new error stays at 1
            new error skips to 3 endOfFile
            blank true false
            wasl true false breaks true true
            ends false true true
            group false continues true true where true
            inGroup true false
            stopsHere false
            unreport count 1 of 2 syntaxErrors 1 lastErrorAt 4 [ 0+3 parentheses around a condition are an input form] [BYN1001 4+1 expected x, found y]
            names true false 0+5 missing 5+0 [BYN1001 5+0 expected a name, found end of file]
            tables true false true true false true true

            """, stdout);
    }
}
