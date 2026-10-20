using System.Text;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

public sealed class DiagnosticRendererTests
{
    private static readonly SourceText Ledger = SourceText.FromString("ledger.byn", """
        i64 sum(Line line) {
            // line 2
            // line 3
            // line 4
            // line 5
            // line 6
            // line 7
            // line 8
            let total = 0;
            // line 10
            // line 11
            total += line.price;
            return total;
        }
        """);

    private static TextSpan Find(SourceText source, string needle)
    {
        var text = Encoding.UTF8.GetString(source.Bytes.Span);
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"`{needle}` not in source");
        return new TextSpan(Encoding.UTF8.GetByteCount(text.AsSpan(0, index)), Encoding.UTF8.GetByteCount(needle));
    }

    [Fact]
    public void RendersHandoffExample()
    {
        var diagnostic = new Diagnostic(
            Severity.Error,
            "BYN2104",
            new TextSpan(Find(Ledger, "total +=").Start, "total".Length),
            "`total` is immutable",
            Label: "declared with `let` on line 9",
            Help: "declare it with `var` if it should change",
            Fix: new Fix(Find(Ledger, "let total"), "var total"));

        Assert.Equal("""
            error BYN2104: `total` is immutable

              12 │     total += line.price;
                 │     ^^^^^ declared with `let` on line 9
                 │
              help: declare it with `var` if it should change
                 │  9 │     var total = 0;

            """, DiagnosticRenderer.Render(diagnostic, Ledger));
    }

    [Fact]
    public void RendersWithoutHelpOrFix()
    {
        var source = SourceText.FromString("a.byn", "let x = 1;\n");
        var diagnostic = new Diagnostic(Severity.Warning, "BYN2000", Find(source, "x"), "unused");

        Assert.Equal("""
            warning BYN2000: unused

              1 │ let x = 1;
                │     ^

            """, DiagnosticRenderer.Render(diagnostic, source));
    }

    [Fact]
    public void CaretsCountCodePointsNotBytes()
    {
        var source = SourceText.FromString("a.byn", "let بيان = \"é\";\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN0005", Find(source, "بيان"), "m");

        var caretLine = DiagnosticRenderer.Render(diagnostic, source).Split('\n')[3];
        Assert.Equal("    │     ^^^^", caretLine);
    }

    [Fact]
    public void TabsInPrefixAreKept()
    {
        var source = SourceText.FromString("a.byn", "\tx;\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1000", Find(source, "x"), "m");

        Assert.Equal("    │ \t^", DiagnosticRenderer.Render(diagnostic, source).Split('\n')[3]);
    }

    [Fact]
    public void EmptySpanGetsOneCaret()
    {
        var source = SourceText.FromString("a.byn", "let x = 1\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1001", new TextSpan(9, 0), "expected `;`", Label: "here");

        Assert.Equal("    │          ^ here", DiagnosticRenderer.Render(diagnostic, source).Split('\n')[3]);
    }

    [Fact]
    public void GutterWidensForFixOnLongerLineNumber()
    {
        var lines = string.Concat(Enumerable.Range(1, 10).Select(i => $"x{i};\n"));
        var source = SourceText.FromString("a.byn", lines);
        var diagnostic = new Diagnostic(Severity.Error, "BYN1002", Find(source, "x9;"), "m",
            Fix: new Fix(Find(source, "x10"), "y10"));

        var rendered = DiagnosticRenderer.Render(diagnostic, source).Split('\n');
        Assert.Equal("   9 │ x9;", rendered[2]);
        Assert.Equal("     │ 10 │ y10;", rendered[5]);
    }

    [Fact]
    public void CrlfLineEndingsAreNotRendered()
    {
        var source = SourceText.FromString("a.byn", "a;\r\nb;\r\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1003", Find(source, "b"), "m");

        Assert.Equal("  2 │ b;", DiagnosticRenderer.Render(diagnostic, source).Split('\n')[2]);
    }
}
