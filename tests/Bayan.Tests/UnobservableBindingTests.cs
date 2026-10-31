using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Spec 013 R7: <c>@unobservable</c> marks reach <see cref="Compilation.Unobservable"/>.</summary>
public sealed class UnobservableBindingTests
{
    private static Compilation Compile(string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "byn-unobservable");
        return Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
    }

    private static string Statement(string text, UnobservableMark mark) => text.Substring(mark.Span.Start, mark.Span.Length);

    private const string Head = "i32 main() {\n    var y = 1\n";

    [Fact]
    public void NoArgumentMarksAllOperators()
    {
        var text = Head + "    @unobservable because \"rounding\"\n    y += 1\n    return y\n}\n";
        var c = Compile(text);
        Assert.False(c.Diagnostics.HasErrors);
        var mark = Assert.Single(c.Unobservable);
        Assert.Null(mark.Operator);
        Assert.Equal("rounding", mark.Reason);
        Assert.EndsWith("y += 1", Statement(text, mark));
    }

    [Fact]
    public void OneValidOperatorNarrowsTheMark()
    {
        var c = Compile(Head + "    @unobservable(boundary) because \"clamped\"\n    y += 1\n    return y\n}\n");
        Assert.False(c.Diagnostics.HasErrors);
        var mark = Assert.Single(c.Unobservable);
        Assert.Equal("boundary", mark.Operator);
        Assert.Equal("clamped", mark.Reason);
    }

    [Fact]
    public void UnknownOperatorIsBYN3219AndRecordsNothing()
    {
        var c = Compile(Head + "    @unobservable(bogus) because \"clamped\"\n    y += 1\n    return y\n}\n");
        Assert.Contains(c.Diagnostics.Items, d => d.Code == "BYN3219");
        Assert.Empty(c.Unobservable);
    }

    [Fact]
    public void TwoAttributedStatementsGetOneMarkEachCoveringOnlyTheirOwn()
    {
        var text = Head + "    @unobservable because \"a\"\n    y += 1\n    @unobservable(literal) because \"b\"\n    y += 2\n    return y\n}\n";
        var c = Compile(text);
        Assert.False(c.Diagnostics.HasErrors);
        Assert.Equal(2, c.Unobservable.Count);
        Assert.EndsWith("y += 1", Statement(text, c.Unobservable[0]));
        Assert.DoesNotContain("y += 2", Statement(text, c.Unobservable[0]));
        Assert.Equal("literal", c.Unobservable[1].Operator);
        Assert.EndsWith("y += 2", Statement(text, c.Unobservable[1]));
    }
}
