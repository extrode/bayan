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
        var tree = Parse(text + ";");
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
    [InlineData("x = (i64)a + b", "(+ (cast i64 a) b)")]
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
    [InlineData("x = try f() + 1", "(+ (try (call f)) 1)")]
    [InlineData("x = f() catch 0", "(catch (call f) 0)")]
    [InlineData("x = a ?? return 1", "(?? a (return 1))")]
    [InlineData("x = n is > 0 and < 10", "(is n (and (> 0) (< 10)))")]
    [InlineData("x = n is 500..=599", "(is n (..= 500 599))")]
    [InlineData("x = a is .shipped && b", "(&& (is a .shipped) b)")]
    [InlineData("x = Point { x = 1, y = 2 }", "(struct Point (x 1) (y 2))")]
    [InlineData("x = { x = 1 }", "(struct _ (x 1))")]
    [InlineData("x = p with { x = 1 }", "(with p (x 1))")]
    [InlineData("x = [1, ..rest]", "(list 1 (spread rest))")]
    [InlineData("x = (1, name: \"a\")", "(tuple 1 (name: \"a\"))")]
    [InlineData("x = new Point()", "(call Point)")]
    public void Precedence(string source, string expected) => Assert.Equal(expected, Expr(source));

    [Fact]
    public void MissingSemicolonIsReportedOnceWithoutCascade()
    {
        var tree = Parse("let a = 1\nlet b = 2;\nprint(a + b);\n");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("BYN1005", diagnostic.Code);
        Assert.Equal(new Fix(new TextSpan(9, 0), ";"), diagnostic.Fix);
        Assert.Equal(3, tree.Root.Members.Length);
    }

    [Fact]
    public void RecoversAtTheNextStatementAfterGarbage()
    {
        var tree = Parse("let a = ) ) );\nlet b = 2;\n");
        Assert.Single(tree.Diagnostics);
        Assert.Equal(2, tree.Root.Members.Length);
    }

    [Fact]
    public void RecoversAtTheNextMemberInsideAStruct()
    {
        var tree = Parse("struct S {\n  i64 a = ;\n  i64 b;\n}\nlet x = 1;\n");
        Assert.Single(tree.Diagnostics);
        var s = Assert.IsType<StructDecl>(tree.Root.Members[0]);
        Assert.Equal(2, s.Members.Length);
        Assert.Equal(2, tree.Root.Members.Length);
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
}
