using System.Runtime.CompilerServices;
using System.Text;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

public sealed class LexerTests
{
    public static TheoryData<string> Cases => Golden.Cases("lex");

    [Theory]
    [MemberData(nameof(Cases))]
    public void GoldenCase(string name) =>
        Golden.Check(name, ".tokens", path => TokenDump.Dump(Lexer.Lex(Golden.Load(path))));

    private static TokenKind[] Kinds(string text) =>
        Lexer.Lex(SourceText.FromString("t.byn", text)).Tokens.ToArray().Select(t => t.Kind).ToArray();

    [Fact]
    public void EveryKeywordLexesToItsKind()
    {
        foreach (var (text, kind) in Keywords.List)
        {
            var expected = kind.IsReservedKeyword() ? TokenKind.Identifier : kind;
            Assert.Equal([expected, TokenKind.EndOfFile], Kinds(text));
        }
    }

    [Fact]
    public void KeywordPrefixIsIdentifier()
    {
        Assert.Equal([TokenKind.Identifier, TokenKind.EndOfFile], Kinds("letter"));
        Assert.Equal([TokenKind.Identifier, TokenKind.EndOfFile], Kinds("Let"));
    }

    [Fact]
    public void AdjacentGreaterTokensHaveNoTriviaBetween()
    {
        var tokens = Lexer.Lex(SourceText.FromString("t.byn", "a >> b > > c")).Tokens.ToArray();
        Assert.False(tokens[2].HasTriviaBefore);
        Assert.True(tokens[5].HasTriviaBefore);
    }

    [Fact]
    public void DocCommentAttachesToNextTokenOnly()
    {
        var tokens = Lexer.Lex(SourceText.FromString("t.byn", "/// Summary.\n/// More.\nstruct S {}")).Tokens.ToArray();
        Assert.True(tokens[0].HasDoc);
        Assert.Equal("/// Summary.\n/// More.", Lexer.Lex(SourceText.FromString("t.byn", "/// Summary.\n/// More.\nstruct S {}")).Source.GetText(tokens[0].Doc));
        Assert.False(tokens[1].HasDoc);
    }

    [Fact]
    public void LexingContinuesAfterInvalidCharacter()
    {
        var result = Lexer.Lex(SourceText.FromString("t.byn", "a ` b"));
        Assert.Single(result.Diagnostics);
        Assert.Equal([TokenKind.Identifier, TokenKind.Identifier, TokenKind.EndOfFile],
            result.Tokens.ToArray().Select(t => t.Kind).ToArray());
    }

    [Fact]
    public void LastTokenIsAlwaysEndOfFile()
    {
        foreach (var text in new[] { "", "$\"{", "\"", "'", "/*", "0x" })
            Assert.Equal(TokenKind.EndOfFile, Lexer.Lex(SourceText.FromString("t.byn", text)).Tokens[^1].Kind);
    }

    [Fact]
    public void OneMegabyteAllocatesOnlyTheTokenArray()
    {
        var unit = """
            /// A ledger line.
            struct Line { i64 price = 0; string name; }
            i64 total(Span<Line> lines) {
                var sum = 0;
                for (let line in lines) { sum += line.price * 2 + 0x_FF; }
                print($"total {sum} for {lines.length} lines");
                return sum;
            }

            """u8.ToArray();
        var bytes = new byte[1024 * 1024];
        for (var i = 0; i + unit.Length <= bytes.Length; i += unit.Length) unit.CopyTo(bytes, i);
        for (var i = bytes.Length - bytes.Length % unit.Length; i < bytes.Length; i++) bytes[i] = (byte)' ';
        var source = new SourceText("big.byn", bytes);

        Lexer.Lex(source); // warm up statics and JIT
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = Lexer.Lex(source);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(result.Diagnostics);
        var tokenBytes = (long)result.Buffer.Length * Unsafe.SizeOf<Token>();
        // The array may have doubled once from its initial estimate; nothing else scales with input.
        Assert.True(allocated < tokenBytes + tokenBytes / 2 + 4096,
            $"allocated {allocated} bytes; final token array is {tokenBytes} bytes");
        Assert.True(result.Count > 100_000);
    }
}
