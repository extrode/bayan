using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

public sealed class ParserTests
{
    public static TheoryData<string> ParseCases => Golden.Cases("parse");

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void ParseGolden(string name) =>
        Golden.Check(name, ".ast", path => AstDump.Dump(SyntaxTree.Parse(Golden.Load(path))));

    private static SyntaxTree Parse(string text) => SyntaxTree.Parse(SourceText.FromString("t.byn", text));

    private static string Expr(string text)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        var statement = (GlobalStatement)Assert.Single(tree.Root.Members);
        return statement.Statement switch
        {
            ExprStmt e => AstDump.Expr(e.Expr),
            AssignStmt a => AstDump.Expr(a.Value),
            _ => throw new InvalidOperationException(statement.Statement.GetType().Name),
        };
    }

    [Theory]
    [InlineData("x = 1 + 2 * 3", "(+ 1 (* 2 3))")]
    [InlineData("x = a - b - c", "(- (- a b) c)")]
    [InlineData("x = a ?? b ?? c", "(?? a (?? b c))")]
    [InlineData("x = a || b && c", "(|| a (&& b c))")]
    [InlineData("x = a == b < c", "(== a (< b c))")]
    [InlineData("x = a >> 1", "(>> a 1)")]
    [InlineData("x = a > b", "(> a b)")]
    [InlineData("x = 0..n - 1", "(.. 0 (- n 1))")]
    [InlineData("x = a | b ^ c & d", "(| a (^ b (& c d)))")]
    [InlineData("x = -a * b", "(* (- a) b)")]
    [InlineData("x = c ? a : b ? d : e", "(? c a (? b d e))")]
    [InlineData("x = i64(a) + b", "(+ (call i64 a) b)")]
    [InlineData("x = (a) + b", "(+ (paren a) b)")]
    [InlineData("x = xs[^1]", "(index xs (^ 1))")]
    [InlineData("x = xs[1..^1]", "(index xs (.. 1 (^ 1)))")]
    [InlineData("x = xs[..3]", "(index xs (.. _ 3))")]
    [InlineData("x = xs[2..]", "(index xs (.. 2 _))")]
    [InlineData("x = a +% b *| c", "(+% a (*| b c))")]
    [InlineData("x = f(a, b: 2, ref c)", "(call f a (b: 2) (ref c))")]
    [InlineData("x = List<string>()", "(call List<string>)")]
    [InlineData("x = a < b", "(< a b)")]
    [InlineData("x = v.truncate<u8>()", "(call (. v truncate<u8>))")]
    [InlineData("x = a?.b.c", "(. (?. a b) c)")]
    [InlineData("x = try f() + 1", "(try (+ (call f) 1))")]
    [InlineData("x = (try f()) + 1", "(+ (paren (try (call f))) 1)")]
    [InlineData("x = f() catch 0", "(catch (call f) 0)")]
    [InlineData("x = a ?? return 1", "(?? a (return 1))")]
    [InlineData("x = n is > 0 and < 10", "(is n (and (> 0) (< 10)))")]
    [InlineData("x = n is 500..=599", "(is n (..= 500 599))")]
    [InlineData("x = a is .shipped && b", "(&& (is a .shipped) b)")]
    [InlineData("x = Point { x: 1, y: 2 }", "(struct Point (x 1) (y 2))")]
    [InlineData("x = { x: 1 }", "(struct _ (x 1))")]
    [InlineData("x = p with { x: 1 }", "(with p (x 1))")]
    [InlineData("x = move p with { x: 1 }", "(with (move p) (x 1))")]
    [InlineData("x = move (p with { x: 1 })", "(move (paren (with p (x 1))))")]
    [InlineData("x = -p with { x: 1 }", "(with (- p) (x 1))")]
    [InlineData("x = [1, ..rest]", "(list 1 (spread rest))")]
    [InlineData("x = [\"a\": 1, k: v + 1]", "(map (entry \"a\" 1) (entry k (+ v 1)))")]
    [InlineData("x = [\"a\": 1, 2]", "(list (entry \"a\" 1) 2)")]
    [InlineData("x = (1, name: \"a\")", "(tuple 1 (name: \"a\"))")]
    [InlineData("x = new Point()", "(call Point)")]
    public void Precedence(string source, string expected) => Assert.Equal(expected, Expr(source));

    [Fact]
    public void MissingLineBreakIsReportedOnceWithoutCascade()
    {
        var tree = Parse("let a = 1 let b = 2\nprint(a + b)\n");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1005", diagnostic.Code);
        Assert.Equal(new Fix(new TextSpan(9, 1), "\n"), diagnostic.Fix);
        Assert.Equal(3, tree.Root.Members.Length);
    }

    [Fact]
    public void RecoversAtTheNextStatementAfterGarbage()
    {
        var tree = Parse("let a = ) ) )\nlet b = 2\n");
        Assert.Single(tree.Diagnostics);
        Assert.Equal(2, tree.Root.Members.Length);
    }

    [Fact]
    public void RecoversAtTheNextMemberInsideAStruct()
    {
        var tree = Parse("struct S {\n  i64 a = )\n  i64 b\n}\nlet x = 1\n");
        Assert.Single(tree.Diagnostics);
        var s = Assert.IsType<StructDecl>(tree.Root.Members[0]);
        Assert.Equal(2, s.Members.Length);
        Assert.Equal(2, tree.Root.Members.Length);
    }

    [Theory]
    [InlineData("defer failing close()", DeferKind.Failing, null)]
    [InlineData("defer panicking (p) close()", DeferKind.Panicking, "p")]
    [InlineData("defer failing (e) { close() }", DeferKind.Failing, "e")]
    [InlineData("defer failing()", DeferKind.Always, null)]
    [InlineData("defer failing.close()", DeferKind.Always, null)]
    [InlineData("defer panicking = 1", DeferKind.Always, null)]
    public void FailingAndPanickingAreWordsOnlyAfterDefer(string text, DeferKind kind, string? binding)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        var defer = Assert.IsType<DeferStmt>(Assert.IsType<GlobalStatement>(Assert.Single(tree.Root.Members)).Statement);
        Assert.Equal(kind, defer.Kind);
        Assert.Equal(binding, defer.Binding?.Text);
    }

    [Fact]
    public void FailingAndPanickingAreIdentifiersElsewhere()
    {
        var tree = Parse("let failing = 1\nvar panicking = failing + 1\nvoid f(i64 failing) { panicking = failing }\n");
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(3, tree.Root.Members.Length);
    }

    [Theory]
    [InlineData("if not a { }")]
    [InlineData("if !a { }")]
    public void NegatedIfWithoutElseIsAccepted(string text) => Assert.Empty(Parse("let a = true\n" + text + "\n").Diagnostics);

    private static Stmt[] Body(SyntaxTree tree) =>
        Assert.IsType<FunctionDecl>(Assert.Single(tree.Root.Members)).Body!.Statements;

    [Theory]
    [InlineData("let a = 1;\n", 9, 10, "")]
    [InlineData("let a = 1 ;\n", 9, 11, "")]
    [InlineData("let a = 1; let b = 2\n", 9, 11, "\n")]
    public void SemicolonIsAnInputFormWithAFix(string text, int start, int end, string replacement)
    {
        var tree = Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1040", diagnostic.Code);
        Assert.Equal(new Fix(TextSpan.FromBounds(start, end), replacement), diagnostic.Fix);
    }

    [Theory]
    [InlineData("struct S { w: i64 }\n", "struct S { i64 w }\n")]
    [InlineData("union E { t(after: i64) }\n", "union E { t(i64 after) }\n")]
    [InlineData("void f(a: i64) { }\n", "void f(i64 a) { }\n")]
    [InlineData("f(i64 a) -> i64 => a\n", "i64 f(i64 a) => a\n")]
    [InlineData("f() { }\n", "void f() { }\n")]
    [InlineData("interface I { f() -> i64 }\n", "interface I { i64 f() }\n")]
    [InlineData("i64 x = 1\n", "let x: i64 = 1\n")]
    [InlineData("i64 x\n", "var x: i64\n")]
    [InlineData("i64 x = 1\nx += 1\n", "var x: i64 = 1\nx += 1\n")]
    [InlineData("i64 x = 1\nx++\n", "var x: i64 = 1\nx++\n")]
    [InlineData("i64 x = 1\nf(ref x)\n", "var x: i64 = 1\nf(ref x)\n")]
    [InlineData("i64 x = 1\nx.y = 2\n", "var x: i64 = 1\nx.y = 2\n")]
    [InlineData("i64 x = 1\ny.x = 2\n", "let x: i64 = 1\ny.x = 2\n")]
    [InlineData("let i64 x = 1\n", "let x: i64 = 1\n")]
    [InlineData("const i64 x = 1\n", "const x: i64 = 1\n")]
    public void TypeNameOrderIsAnInputFormWithAFix(string text, string fixedText)
    {
        var tree = Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1043", diagnostic.Code);
        var fix = diagnostic.Fix!;
        Assert.Equal(fixedText, text[..fix.Span.Start] + fix.Replacement + text[fix.Span.End..]);
    }

    [Theory]
    [InlineData("unique struct S {\n}\n", "struct unique S {\n}\n")]
    [InlineData("unique public struct S {\n}\n", "public struct unique S {\n}\n")]
    [InlineData("error enum E {\n    a\n}\n", "union error E {\n    a\n}\n")]
    [InlineData("public error union E {\n    a\n}\n", "public union error E {\n    a\n}\n")]
    [InlineData("error union S = A, B\n", "union error S = A, B\n")]
    [InlineData("distinct type Id = i64\n", "type distinct Id = i64\n")]
    public void DeclarationKeywordOrderIsAnInputFormWithAFix(string text, string fixedText)
    {
        var tree = Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1044", diagnostic.Code);
        var fix = diagnostic.Fix!;
        Assert.Equal(fixedText, text[..fix.Span.Start] + fix.Replacement + text[fix.Span.End..]);
    }

    [Theory]
    [InlineData("error set S = A, B\n", "union error S = A, B\n")]
    [InlineData("public error set S = A | B | C\n", "public union error S = A, B, C\n")]
    [InlineData("union error S = A | B except .x\n", "union error S = A, B except .x\n")]
    public void ErrorSetIsAnInputFormWithAFix(string text, string fixedText)
    {
        var tree = Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1029", diagnostic.Code);
        var fix = diagnostic.Fix!;
        Assert.Equal(fixedText, text[..fix.Span.Start] + fix.Replacement + text[fix.Span.End..]);
    }

    [Theory]
    [InlineData("struct unique S {\n}\n", "Struct unique S")]
    [InlineData("union error E {\n    a\n}\n", "ErrorEnum E")]
    [InlineData("union error S = A, B except .x, .y\n", "ErrorSet S = A, B except .x, .y")]
    [InlineData("type distinct Id = i64\n", "TypeAlias distinct Id = i64")]
    public void HeadFirstDeclarationsAreClean(string text, string dump)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        Assert.Contains(dump, AstDump.Dump(tree));
    }

    [Theory]
    [InlineData("let x: i64 = 1\n")]
    [InlineData("var x: i64\n")]
    [InlineData("const x: i64 = 1\n")]
    [InlineData("i64 f(i64 a) => a\n")]
    public void NameFirstLocalsAndTypeFirstDeclarationsAreClean(string text) => Assert.Empty(Parse(text).Diagnostics);

    [Theory]
    [InlineData("struct S {\n    void f(this is .open) {\n    }\n}\n", "Function f(this is .open) -> void")]
    [InlineData("struct S {\n    void f(ref this is .open, i64 a) {\n    }\n}\n", "Function f(ref this is .open, i64 a) -> void")]
    [InlineData("struct S {\n    i64 a\n    i64 b {\n        return a\n    }\n}\n", "Property i64 b\n      Block\n        Return a")]
    [InlineData("const p = pattern S { a: 0 or 1 }\nf()\n", "Const p = (pattern (props S (a: (or 0 1))))\n  Expr (call f)")]
    [InlineData("let pattern = 1\nlet x = pattern\n", "Let x = pattern")]
    [InlineData("test \"t\" (i32 input, bool valid) {\n}\n", "Test `t` (i32 input, bool valid)")]
    public void ReceiverStatesPatternsAndBlockPropertiesParse(string text, string dump)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        Assert.Contains(dump, AstDump.Dump(tree));
    }

    [Theory]
    [InlineData("let p = pattern S { a: 0 }\n")]
    [InlineData("void f() {\n    g(pattern S { a: 0 })\n}\n")]
    public void PatternOutsideConstIsReported(string text)
    {
        var diagnostic = Assert.Single(Parse(text).Diagnostics);
        Assert.Equal("BYN1046", diagnostic.Code);
        Assert.Null(diagnostic.Fix);
    }

    [Fact]
    public void NameFirstTestParamsAreAnInputForm()
    {
        var diagnostic = Assert.Single(Parse("test \"t\" (input: i32) {\n}\n").Diagnostics);
        Assert.Equal("BYN1043", diagnostic.Code);
    }

    [Theory]
    [InlineData("let a = 1 +\n    2\n")]
    [InlineData("let a = f(1,\n    2)\n")]
    [InlineData("let a = [\n    1,\n    2,\n]\n")]
    [InlineData("let a = b\n    .c()\n")]
    [InlineData("let a = b\n    ?.c\n")]
    [InlineData("let a = b\n    ?? c\n")]
    [InlineData("let a = b\n    && c\n")]
    [InlineData("let a = b\n    || c\n")]
    [InlineData("let a = n is > 0\n    and < 10\n")]
    [InlineData("let a = f()\n    catch 0\n")]
    [InlineData("let a = f(1,\n\n    2)\n")]
    [InlineData("T id<T>(T a)\n    where T : IA => a\n")]
    [InlineData("void f(i32 n)\n    expects n > 0\n    expects n < 9\n    fails E {\n}\n")]
    [InlineData("T id<T>(T a)\n    where T : IA\n    expects a == a\n    fails E => a\n")]
    public void ALineBreakContinuesAfterAnOperatorOrBeforeAJoiner(string text)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        Assert.Single(tree.Root.Members);
    }

    [Theory]
    [InlineData("let a = b\n(c)\n")]
    [InlineData("let a = b\n[c]\n")]
    [InlineData("let a = b\n-c\n")]
    [InlineData("let a = b\n++c\n")]
    [InlineData("let a = b\n\n    .c()\n")]
    [InlineData("let a = b\n\n    && c\n")]
    [InlineData("f()\nif ok {\n}\n")]
    public void ALineBreakEndsTheStatementOtherwise(string text)
    {
        var tree = Parse(text);
        Assert.Equal(2, tree.Root.Members.Length);
    }

    [Fact]
    public void ABlankLineAfterAnOperatorIsAnError()
    {
        var tree = Parse("let a = 1 +\n\nlet b = 2\n");
        Assert.Equal("BYN1002", tree.Diagnostics[0].Code);
        Assert.Equal("a blank line", tree.Diagnostics[0].Message[^"a blank line".Length..]);
    }

    [Fact]
    public void ElseOnANewLineIsAnInputForm()
    {
        var tree = Parse("if a {\n}\nelse {\n}\n");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1041", diagnostic.Code);
        Assert.Equal(new Fix(TextSpan.FromBounds(8, 9), " "), diagnostic.Fix);
        var statement = Assert.IsType<GlobalStatement>(Assert.Single(tree.Root.Members)).Statement;
        Assert.NotNull(Assert.IsType<IfStmt>(statement).Else);
    }

    [Fact]
    public void AReturnValueAloneOnTheNextLineIsAnInputForm()
    {
        var tree = Parse("i64 f() {\n    return\n        1\n}\n");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1042", diagnostic.Code);
        Assert.Equal(new Fix(TextSpan.FromBounds(20, 29), " "), diagnostic.Fix);
        Assert.NotNull(Assert.IsType<ReturnStmt>(Assert.Single(Body(tree))).Value);
    }

    [Theory]
    [InlineData("void f() {\n    return\n}\n", 1)]
    [InlineData("void f() {\n    return\n    g()\n}\n", 2)]
    [InlineData("void f() {\n    return\n\n    g()\n}\n", 2)]
    [InlineData("void f() {\n    return\n    (a + b).f()\n}\n", 2)]
    public void AReturnAtALineBreakHasNoValue(string text, int statements)
    {
        var tree = Parse(text);
        Assert.Empty(tree.Diagnostics);
        var body = Body(tree);
        Assert.Equal(statements, body.Length);
        Assert.Null(Assert.IsType<ReturnStmt>(body[0]).Value);
    }

    [Fact]
    public void ABreakLabelOnTheNextLineIsAnInputForm()
    {
        var tree = Parse("outer: while x {\n    break\n        outer\n}\n");
        Assert.Equal("BYN1042", Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void APostfixConditionOnTheNextLineIsAnInputForm()
    {
        var tree = Parse("stop()\n    if done\n");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1042", diagnostic.Code);
        Assert.Equal(new Fix(TextSpan.FromBounds(6, 11), " "), diagnostic.Fix);
        Assert.Single(tree.Root.Members);
    }

    [Fact]
    public void AnIfAfterAPostfixConditionStartsTheNextStatement()
    {
        var tree = Parse("void f() {\n    return if a\n    if b {\n        g()\n    }\n}\n");
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(2, Body(tree).Length);
    }

    [Theory]
    [InlineData("enum E { a, b }\n", "enum E {\n    a\n    b\n}\n")]
    [InlineData("enum E {\n    a,\n    b,\n}\n", "enum E {\n    a\n    b\n}\n")]
    [InlineData("union U { A(i64 x), B }\n", "union U {\n    A(i64 x)\n    B\n}\n")]
    [InlineData("union error E { a, b }\n", "union error E {\n    a\n    b\n}\n")]
    public void CommaSeparatedCasesAreAnInputForm(string text, string fixedText)
    {
        var tree = Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1053", diagnostic.Code);
        var fix = diagnostic.Fix!;
        Assert.Equal(fixedText, text[..fix.Span.Start] + fix.Replacement + text[fix.Span.End..]);
    }

    [Fact]
    public void CasesOnTheirOwnLinesNeedNoCommas()
    {
        var tree = Parse("enum E {\n    a\n    b\n}\nunion U {\n    A(i64 x)\n    B\n}\n");
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("x = f() catch { y: 1 }", "(catch (call f) (struct _ (y 1)))")]
    [InlineData("x = f() catch { y: 1, z: 2 }", "(catch (call f) (struct _ (y 1) (z 2)))")]
    public void ACatchBraceWithFieldsIsAStructLiteral(string source, string expected) => Assert.Equal(expected, Expr(source));

    [Fact]
    public void ACatchBraceWithStatementsIsABlock()
    {
        var tree = Parse("x = f() catch {\n    y = 1\n    z = 2\n}\n");
        Assert.Empty(tree.Diagnostics);
        var statement = Assert.IsType<GlobalStatement>(Assert.Single(tree.Root.Members)).Statement;
        var handler = Assert.IsType<CatchExpr>(Assert.IsType<AssignStmt>(statement).Value).Handler;
        Assert.IsType<BlockExpr>(handler);
    }

    [Fact]
    public void ADeferWordAtALineBreakIsTheBody()
    {
        var tree = Parse("defer failing\nclose()\n");
        Assert.Equal(2, tree.Root.Members.Length);
        var defer = Assert.IsType<DeferStmt>(Assert.IsType<GlobalStatement>(tree.Root.Members[0]).Statement);
        Assert.Equal(DeferKind.Always, defer.Kind);
    }

    /// <summary>Inputs that once threw: a C# primitive name as the file's first token, a braceless body cut off by the end of the
    /// file, and an unclosed condition group whose end lands on the token buffer's last slot.</summary>
    [Theory]
    [InlineData("int()fails")]
    [InlineData("int(\"\"{])fails")]
    [InlineData("if(/*")]
    [InlineData("if(\u0017")]
    [InlineData("while(\u0017")]
    [InlineData("unless ((((((((((((((((((()")]
    public void TruncatedInputReportsWithoutThrowing(string text) =>
        Assert.Contains(Parse(text).Diagnostics, d => d.IsError);

    /// <summary>A braceless body that takes no token before <c>}</c>: the header's fix takes the braces too, so applied alone it parses.</summary>
    [Theory]
    [InlineData("void main() {\n    if (x) }\n", "BYN1017")]
    [InlineData("void main() {\n    unless (!x) }\n", "BYN1007")]
    public void AnEmptyBracelessBodyHeaderFixParses(string text, string code)
    {
        var fix = Assert.Single(Parse(text).Diagnostics, d => d.Code == code).Fix!;
        var fixedText = text[..fix.Span.Start] + fix.Replacement + text[fix.Span.End..];
        Assert.DoesNotContain(Parse(fixedText).Diagnostics, d => d.Code.StartsWith("BYN1", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryTokenSequenceTerminates()
    {
        var fragments = new[] { "{", "}", "(", ")", "[", "]", "if", "for", "let", "x", "=", ";", "switch", "case", ":", "$\"{", "struct", "..", "?", "catch", "is", "<", ">", "=>", "," };
        var random = new Random(1);
        for (var n = 0; n < 2000; n++)
        {
            var parts = Enumerable.Range(0, random.Next(1, 12)).Select(_ => fragments[random.Next(fragments.Length)]);
            Parse(string.Join(' ', parts));
        }
    }

    /// <summary>BYN5103 finds each joined line's statement start once, so a long chain or a long switch lints in linear time.</summary>
    [Theory]
    [InlineData("chain")]
    [InlineData("switch")]
    public void TheLineJoinLintIsLinear(string shape)
    {
        static string Source(string shape, int n) => shape == "chain"
            ? "let n = s\n" + string.Concat(Enumerable.Repeat("    .x\n", n)) + ".y\n"
            : "let r = v switch {\n" + string.Concat(Enumerable.Repeat("    i => a\n        .x,\n", n)) + "    _ => a\n    .x,\n}\n";
        static string Lints(SyntaxTree tree) => string.Join("\n", tree.Lints.Select(d => $"{d.Code} {d.Fix!.Replacement.Length} {d.Label}"));
        var expected = shape == "chain" ? "BYN5103 4 joins the statement on line 1" : "BYN5103 8 joins the statement on line 2";
        Assert.Equal(expected, Lints(Parse(Source(shape, 100))));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var tree = Parse(Source(shape, shape == "chain" ? 20000 : 10000));
        watch.Stop();
        Assert.Equal(expected, Lints(tree));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"parsed in {watch.Elapsed}");
    }
}
