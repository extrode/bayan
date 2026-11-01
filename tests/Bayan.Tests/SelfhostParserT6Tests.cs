using System.Diagnostics;
using System.Reflection;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// <c>selfhost/parser/types.byn</c> and <c>literals.byn</c> (009 T6) against the C# parser, before the other tasks merge.
/// The parser's files stand in for the module without <c>main.byn</c> and <c>exprs2.byn</c>; this file's program adds a
/// <c>parsePrimary</c> for an array size (an integer or a name) and a <c>main</c> that either parses one type from the
/// start of a file and prints its shape with spans, then the diagnostics with their fixes, or prints every string,
/// char, interpolation text and doc comment the lexer finds, decoded, as UTF-8 bytes. The C# side calls the private
/// <c>ParseType</c> through reflection and the public <see cref="LiteralDecoder"/>.
/// </summary>
public sealed class SelfhostParserT6Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private const string Program = """
        import lexer

        string range(TextSpan s) => $"{s.start}..{s.end}"

        string exprRange(in Expr e) {
            switch e {
                case .literalExpr(let n):
                    return range(n.value.span)
                case .nameExpr(let n):
                    return range(n.value.span)
                default:
                    return "?"
            }
        }

        string nameText(TextSpan? name) {
            if name is not null { return range(name) }
            return "-"
        }

        string failsText(in FailsClause? clause) {
            if clause is not null { return $" fails {range(clause.span)}{shapes(in clause.errors)}" }
            return ""
        }

        string usesText(in UsesClause? clause) {
            if clause is null { return "" }
            var text = $" uses {range(clause.span)}"
            for s in clause.effects { text = $"{text} {range(s)}" }
            return text
        }

        string shapes(in List<TypeSyntax> types) {
            var text = ""
            for t in types { text = $"{text} {shape(in t)}" }
            return text
        }

        string shape(in TypeSyntax t) {
            switch t {
                case .namedType(let n):
                    var text = $"(named {range(n.value.span)}"
                    for s in n.value.path { text = $"{text} {range(s)}" }
                    return $"{text} <{shapes(in n.value.args)} >)"
                case .optionalType(let n):
                    return $"(opt {range(n.value.span)} {shape(in n.value.inner)})"
                case .arrayType(let n):
                    return $"(array {range(n.value.span)} {shape(in n.value.element)} {exprRange(in n.value.size)})"
                case .tupleType(let n):
                    var text = $"(tuple {range(n.value.span)}"
                    for e in n.value.elements {
                        let name = nameText(e.name)
                        text = $"{text} [{range(e.span)} {shape(in e.type)} {name}]"
                    }
                    return $"{text})"
                case .functionTypeSyntax(let n):
                    var text = $"(fn {range(n.value.span)}{shapes(in n.value.params)} -> {shape(in n.value.returnKeyword)}"
                    return $"{text}{failsText(in n.value.failsKeyword)}{usesText(in n.value.usesKeyword)})"
                case .dynType(let n):
                    return $"(dyn {range(n.value.span)} {n.value.isRef} {n.value.isReadOnly} {shape(in n.value.inner)})"
                case .pointerType(let n):
                    return $"(ptr {range(n.value.span)} {n.value.readOnly} {shape(in n.value.pointee)})"
                case .none:
                    return "none"
                default:
                    return "?"
            }
        }

        string bytesOf(string s) {
            var text = ""
            for b in s.bytes { text = $"{text} {b}" }
            return text
        }

        i32 main(ReadOnlySpan<string> args) {
            let keywords = Keywords.build()
            var bytes = Io.readFile(args[0]) catch return 4
            var p = Parser.create(lexer.lex(SourceText.create(args[0], from: move bytes), by: keywords))
            let mode = args[1]
            if mode == "literals" {
                for i in 0..p.count {
                    let token = p.lexed.tokens[i]
                    let text = p.text(token)
                    if token.hasDoc { print($"doc{bytesOf(decodeDoc(p.lexed.source.bytes.span[token.docStart..token.docStart + token.docLength]))}") }
                    if token.kind == .stringLiteral || token.kind == .cStringLiteral || token.kind == .u16StringLiteral {
                        print($"string{bytesOf(decodeString(text))}")
                    } else if token.kind == .rawStringLiteral {
                        print($"raw{bytesOf(decodeRawString(text))}")
                    } else if token.kind == .interpolatedStringText {
                        print($"text{bytesOf(decodeInterpolatedText(text))}")
                    } else if token.kind == .charLiteral {
                        print($"char {decodeChar(text)}")
                    }
                }
                return 0
            }
            p.inExtern = mode.contains("extern")
            let type = parseType(ref p, fatArrow: mode.contains("fat"), lists: mode.contains("lists"))
            print($"{shape(in type)} at {p.pos}")
            for d in p.lexed.diagnostics {
                if d.code.isEmpty { continue }
                let fix = d.hasFix ? $" fix {d.fixSpan.start}+{d.fixSpan.length} '{d.fixReplacement}'" : ""
                print($"{d.code} {d.span.start}+{d.span.length} {d.message} [{d.label}] [{d.help}]{fix}")
            }
            return 0
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(() =>
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn")
            .Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f))
            .Append(SourceText.FromString(Path.Combine(ParserDir, "t6-test.byn"), Program)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var mine = c.Diagnostics.Items.Where(d => d.Source?.Path is { } p && (p.EndsWith("types.byn") || p.EndsWith("literals.byn"))).ToList();
        if (mine.Count > 0) return (null, DiagnosticRenderer.RenderAll(mine, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    });

    /// <summary>The C# <c>ParseType(fatArrow, lists)</c> at the start of <paramref name="source"/>, printed as <see cref="Program"/> does.</summary>
    private static string ExpectedType(SourceText source, string mode)
    {
        var parser = (Parser)Activator.CreateInstance(typeof(Parser), BindingFlags.NonPublic | BindingFlags.Instance, null, [Lexer.Lex(source)], null)!;
        typeof(Parser).GetField("_inExtern", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(parser, mode.Contains("extern"));
        var type = (TypeSyntax)typeof(Parser).GetMethod("ParseType", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(parser, [mode.Contains("fat"), mode.Contains("lists")])!;
        var pos = (int)typeof(Parser).GetField("_pos", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(parser)!;
        var bag = (DiagnosticBag)typeof(Parser).GetField("_diagnostics", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(parser)!;
        var lines = new List<string> { $"{Shape(type)} at {pos}" };
        foreach (var d in bag.Items)
        {
            var fix = d.Fix is { } f ? $" fix {f.Span.Start}+{f.Span.Length} '{f.Replacement}'" : "";
            lines.Add($"{d.Code} {d.Span.Start}+{d.Span.Length} {d.Message} [{d.Label}] [{d.Help}]{fix}");
        }
        return string.Join("\n", lines) + "\n";
    }

    private static string Range(TextSpan s) => $"{s.Start}..{s.End}";

    private static string Shapes(IEnumerable<TypeSyntax> types) => string.Concat(types.Select(t => " " + Shape(t)));

    private static string Shape(TypeSyntax? t) => t switch
    {
        null => "none",
        NamedType n => $"(named {Range(n.Span)}{string.Concat(n.Path.Select(s => " " + Range(s.Span)))} <{Shapes(n.Args)} >)",
        OptionalType o => $"(opt {Range(o.Span)} {Shape(o.Inner)})",
        ArrayType a => $"(array {Range(a.Span)} {Shape(a.Element)} {Range(a.Size.Span)})",
        TupleType u => $"(tuple {Range(u.Span)}{string.Concat(u.Elements.Select(e => $" [{Range(e.Span)} {Shape(e.Type)} {(e.Name is { } n ? Range(n.Span) : "-")}]"))})",
        FunctionTypeSyntax f => $"(fn {Range(f.Span)}{Shapes(f.Params)} -> {Shape(f.Return)}"
            + (f.Fails is { } fl ? $" fails {Range(fl.Span)}{Shapes(fl.Errors)}" : "")
            + (f.Uses is { } us ? $" uses {Range(us.Span)}{string.Concat(us.Effects.Select(e => " " + Range(e.Span)))}" : "") + ")",
        DynType d => $"(dyn {Range(d.Span)} {Bool(d.IsRef)} {Bool(d.IsReadOnly)} {Shape(d.Inner)})",
        PointerType p => $"(ptr {Range(p.Span)} {Bool(p.ReadOnly)} {Shape(p.Pointee)})",
        _ => "?",
    };

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Bytes(string s) => string.Concat(Encoding.UTF8.GetBytes(s).Select(b => $" {b}"));

    /// <summary>The C# <see cref="LiteralDecoder"/> over the tokens of <paramref name="source"/>, printed as <see cref="Program"/> does.</summary>
    private static string ExpectedLiterals(SourceText source)
    {
        var lines = new List<string>();
        foreach (var token in Lexer.Lex(source).Tokens)
        {
            var text = source.GetText(token.Span);
            if (token.HasDoc) lines.Add($"doc{Bytes(LiteralDecoder.DecodeDoc(source.GetText(token.Doc)))}");
            switch (token.Kind)
            {
                case TokenKind.StringLiteral or TokenKind.CStringLiteral or TokenKind.U16StringLiteral:
                    lines.Add($"string{Bytes(LiteralDecoder.DecodeString(text))}");
                    break;
                case TokenKind.RawStringLiteral:
                    lines.Add($"raw{Bytes(LiteralDecoder.DecodeRawString(text))}");
                    break;
                case TokenKind.InterpolatedStringText:
                    lines.Add($"text{Bytes(LiteralDecoder.DecodeInterpolatedText(text))}");
                    break;
                case TokenKind.CharLiteral:
                    lines.Add($"char {LiteralDecoder.DecodeChar(text)}");
                    break;
            }
        }
        return string.Concat(lines.Select(l => l + "\n"));
    }

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Utf8Child.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser-t6", $"{name}-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]}.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string BuiltPath
    {
        get
        {
            var (path, error) = Built.Value;
            if (path is null) Assert.Fail($"types.byn or literals.byn did not build:\n{error}");
            return path!;
        }
    }

    /// <summary>
    /// Types from the start of a file, with the <c>ParseType</c> flags the mode names (<c>fat</c>, <c>lists</c>) and
    /// <c>extern</c> for <c>_inExtern</c>: names, qualified names and type arguments, the C# names and <c>char</c>,
    /// optional, array, tuple and its names, function types with <c>=&gt;</c>, <c>fails</c> and <c>uses</c> (bare, listed,
    /// parenthesized), a parenthesized lone function type, <c>dyn</c>, postfix and prefix pointers, <c>readonly T*</c>
    /// and a missing type.
    /// </summary>
    [Theory]
    [InlineData("i32", "")]
    [InlineData("m.List<i32, string>", "")]
    [InlineData("int", "")]
    [InlineData("char", "")]
    [InlineData("List<char>", "")]
    [InlineData("never", "")]
    [InlineData("i32?[4]?", "")]
    [InlineData("u8[N]", "")]
    [InlineData("(i32, string name)", "")]
    [InlineData("(i32 a, b)", "")]
    [InlineData("()", "")]
    [InlineData("(i32)", "")]
    [InlineData("i32 -> bool", "")]
    [InlineData("i32 => bool", "")]
    [InlineData("i32 => bool", "fat")]
    [InlineData("(i32, bool) -> string fails E uses alloc", "")]
    [InlineData("() -> void fails A, B uses a, b", "")]
    [InlineData("() -> void fails A, B uses a, b", "fat lists")]
    [InlineData("() -> void fails (A, B) uses (a, b)", "")]
    [InlineData("(i32 x, bool y) -> void", "")]
    [InlineData("i32 -> i32 -> i32", "")]
    [InlineData("(Token -> bool) p", "")]
    [InlineData("(Token -> bool)? p", "")]
    [InlineData("((A -> B)) p", "")]
    [InlineData("(A -> B) -> C", "")]
    [InlineData("(A -> B, C)", "")]
    [InlineData("(A => B) x", "fat")]
    [InlineData("dyn I", "")]
    [InlineData("dyn ref I", "")]
    [InlineData("dyn ref readonly m.I<T>", "")]
    [InlineData("u8* p", "")]
    [InlineData("u8* p", "extern")]
    [InlineData("u8** p", "")]
    [InlineData("u8*? p", "extern")]
    [InlineData("readonly u8* p", "extern")]
    [InlineData("readonly u8** p", "extern")]
    [InlineData("readonly u8*?* p", "extern")]
    [InlineData("readonly readonly u8* p", "extern")]
    [InlineData("readonly A* -> B* p", "extern")]
    [InlineData("readonly u8 p", "")]
    [InlineData("*u8", "")]
    [InlineData("*const u8", "")]
    [InlineData("*mut u8", "extern")]
    [InlineData("**u8", "")]
    [InlineData("*const *u8", "extern")]
    [InlineData("= x", "")]
    [InlineData("(i32, ", "")]
    [InlineData("List<i32", "")]
    [InlineData("() -> void uses", "")]
    public void TypesMatchTheCSharpParser(string type, string mode)
    {
        var input = Write("type", Encoding.UTF8.GetBytes(type + "\n"));
        var (exit, stdout, stderr) = Run(BuiltPath, input, "type " + mode);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(ExpectedType(SourceText.FromFile(input), mode), stdout);
    }

    /// <summary>
    /// Literal decoding: escapes (including <c>\u{…}</c> valid, invalid, surrogate, too large and unclosed), <c>c"…"</c>
    /// and <c>u16"…"</c>, raw strings with and without indentation and line breaks (LF and CRLF), interpolation text with
    /// doubled braces, char literals of one, none or two scalars, doc comments with indentation, and invalid UTF-8.
    /// </summary>
    [Theory]
    [InlineData("let a = \"plain\"\n")]
    [InlineData("let a = \"tab\\tnl\\ncr\\rnul\\0bs\\\\q\\\"ap\\'x\\q\"\n")]
    [InlineData("let a = \"\\u{41}\\u{1F600}\\u{D800}\\u{110000}\\u{}\\u{zz}\\u{0000000041}\\u{FFFFFFFF}\\u{41\"\n")]
    [InlineData("let a = c\"c\\n\" + u16\"wide\"\n")]
    [InlineData("let a = \"unterminated\n")]
    [InlineData("let a = \"\"\"raw \\n\"\"\"\n")]
    [InlineData("let a = \"\"\"\n    one\n      two\n\n    three\n    \"\"\"\n")]
    [InlineData("let a = \"\"\"\r\n    one\r\n    \"\"\"\n")]
    [InlineData("let a = \"\"\"\n    one\n  x \"\"\"\n")]
    [InlineData("let a = \"\"\"\nabc\"\"\"\n")]
    [InlineData("let a = $\"a {{b}} {x} \\t }}\"\n")]
    [InlineData("let a = 'x'\nlet b = '\\n'\nlet c = '\\u{1F600}'\nlet d = ''\nlet e = 'ab'\n")]
    [InlineData("/// One.\n///Two.\n   ///   Three.\nvoid f() {}\n")]
    [InlineData("/// a\r\n/// b\r\nvoid f() {}\n")]
    public void LiteralsMatchTheCSharpDecoder(string source)
    {
        var input = Write("literals", Encoding.UTF8.GetBytes(source));
        var (exit, stdout, stderr) = Run(BuiltPath, input, "literals");
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(ExpectedLiterals(SourceText.FromFile(input)), stdout);
    }

    /// <summary>Invalid UTF-8 and a non-breaking space before <c>///</c> decode as <c>Encoding.UTF8</c> and <c>TrimStart</c> do.</summary>
    [Fact]
    public void InvalidUtf8AndUnicodeWhiteSpaceDecodeAsCSharpDoes()
    {
        var bytes = Encoding.UTF8.GetBytes("let a = \"x").Concat(new byte[] { 0xC3, 0x28, 0xE2, 0x82, 0xF0, 0x9F, 0x98 })
            .Concat(Encoding.UTF8.GetBytes("\\n\"\n\u00a0\u3000/// doc\nvoid f() {}\n")).ToArray();
        var input = Write("utf8", bytes);
        var (exit, stdout, stderr) = Run(BuiltPath, input, "literals");
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(ExpectedLiterals(SourceText.FromFile(input)), stdout);
    }
}
