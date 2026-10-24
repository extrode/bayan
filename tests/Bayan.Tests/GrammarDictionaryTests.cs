using Bayan.Semantics;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The grammar checker's words (J85): <c>grammar</c> in <c>package.byn</c>, and the one verb list R15 and BYN5014 share.
/// The checks themselves are the <c>diag/</c> goldens (<c>unknown-word</c>, <c>grammar-dictionary</c>).
/// </summary>
public sealed class GrammarDictionaryTests
{
    private static Manifest Parse(string text) => Manifest.Parse(new SourceText("package.byn", System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ManifestReadsTheGrammar()
    {
        var m = Parse("""
            Package {
                name: "local/shop",
                version: "0.1.0",
                grammar: Grammar {
                    verbs: ["upsert", "reindex"],
                    nouns: ["sku"],
                    adjectives: ["idempotent"],
                    forms: ["regrind": "reground", "upsert": "upserted"],
                },
            }
            """);
        Assert.Empty(m.Diagnostics);
        Assert.Equal(["reindex", "upsert"], m.Grammar.Verbs.Order());
        Assert.Equal(["sku"], m.Grammar.Nouns);
        Assert.Equal(["idempotent"], m.Grammar.Adjectives);
        Assert.Equal("reground", m.Grammar.Forms["regrind"]);
        Assert.Equal("upserted", m.Grammar.Forms["upsert"]);
    }

    [Fact]
    public void GrammarIsEmptyWithoutTheField()
    {
        var m = Parse("""Package { name: "local/shop", version: "0.1.0", grammar: Grammar { forms: [] } }""");
        Assert.Empty(m.Diagnostics);
        Assert.Empty(m.Grammar.Verbs);
        Assert.Empty(m.Grammar.Forms);
    }

    [Fact]
    public void MapEntriesNeedAValue()
    {
        var m = Parse("""Package { name: "local/shop", version: "0.1.0", grammar: Grammar { forms: ["a": "b", "c"] } }""");
        Assert.Contains(m.Diagnostics, d => d.Message.Contains("`:` and a value", StringComparison.Ordinal));
    }

    /// <summary>The verbs a help may start with are the checker's verbs, so a verb R15 accepts is never an unknown word.</summary>
    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("write")]
    [InlineData("propagate")]
    public void HelpVerbsAreCheckerVerbs(string verb) => Assert.Contains(verb, Lexicon.Verbs);

    [Fact]
    public void WordClassesAreLowercaseWords() =>
        Assert.All(Lexicon.Verbs.Concat(Lexicon.Nouns).Concat(Lexicon.Adjectives).Concat(Lexicon.FunctionWords),
            w => Assert.Matches("^[a-z]+$", w));
}
