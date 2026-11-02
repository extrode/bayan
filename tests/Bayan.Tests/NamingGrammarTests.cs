using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>BYN5001: which names read as the <c>-ed</c> form of a verb, and which verb (docs/bayan-grammar.md).</summary>
public sealed class NamingGrammarTests
{
    [Theory]
    [InlineData("sorted", "sort")]
    [InlineData("joined", "join")]
    [InlineData("reversed", "reverse")]
    [InlineData("parsed", "parse")]
    [InlineData("closed", "close")]
    [InlineData("trimmed", "trim")]
    [InlineData("stopped", "stop")]
    [InlineData("copied", "copy")]
    [InlineData("doubled", "double")]
    [InlineData("named", "name")]
    [InlineData("fixed", "fix")]
    [InlineData("called", "call")]
    [InlineData("passed", "pass")]
    [InlineData("counted", "count")]
    public void ParticipleNamesItsVerbFirst(string name, string verb) =>
        Assert.Equal(verb, Naming.ParticipleStems(name)[0]);

    [Theory]
    [InlineData("speed")]
    [InlineData("need")]
    [InlineData("seed")]
    [InlineData("red")]
    [InlineData("bed")]
    [InlineData("used")]
    [InlineData("hundred")]
    [InlineData("embed")]
    [InlineData("isSorted")]
    [InlineData("sortedBy")]
    [InlineData("sort")]
    public void OrdinaryWordsAreNotParticiples(string name) =>
        Assert.Empty(Naming.ParticipleStems(name));
}
