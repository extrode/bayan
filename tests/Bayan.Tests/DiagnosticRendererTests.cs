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
    public void ErrorLimitSkipsErrorsButNeverWarnings()
    {
        var diagnostics = Enumerable.Range(0, 25).Select(i => new Diagnostic(Severity.Error, "BYN0001", Ledger, new TextSpan(i, 1), "bad"))
            .Concat(Enumerable.Range(0, 3).Select(i => new Diagnostic(Severity.Warning, "BYN0002", Ledger, new TextSpan(30 + i, 1), "odd")))
            .ToList();

        var text = DiagnosticRenderer.RenderAll(diagnostics, false, Style.Plain, 20, out var omitted);

        Assert.Equal(5, omitted);
        Assert.Equal(20, text.Split("error BYN0001").Length - 1);
        Assert.Equal(3, text.Split("warning BYN0002").Length - 1);
    }

    [Fact]
    public void RendersHandoffExample()
    {
        var diagnostic = new Diagnostic(
            Severity.Error,
            "BYN2104",
            Ledger,
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
                 │    │     +++

            """, DiagnosticRenderer.Render(diagnostic));
    }

    [Fact]
    public void ColorOnlyAddsEscapes()
    {
        var diagnostic = new Diagnostic(
            Severity.Error,
            "BYN2104",
            Ledger,
            new TextSpan(Find(Ledger, "total +=").Start, "total".Length),
            "`total` is immutable",
            Label: "declared with `let` on line 9",
            Help: "declare it with `var` if it should change",
            Fix: new Fix(Find(Ledger, "let total"), "var total"));
        var colored = DiagnosticRenderer.Render(diagnostic, includePath: true, color: true);
        Assert.Equal(DiagnosticRenderer.Render(diagnostic, includePath: true), System.Text.RegularExpressions.Regex.Replace(colored, "\u001b\\[[0-9;]*m", ""));
        Assert.StartsWith("\u001b[1m\u001b[31merror BYN2104\u001b[0m: `total` is immutable\n", colored);
        Assert.Contains("\u001b[1m\u001b[31m^^^^^ declared with `let` on line 9\u001b[0m", colored);
        Assert.Contains("\u001b[1m\u001b[34mhelp:\u001b[0m declare", colored);
        Assert.Contains("\u001b[2m  -->\u001b[0m", colored);
        Assert.Contains("\u001b[34m+++\u001b[0m", colored);
    }

    [Fact]
    public void RendersWithoutHelpOrFix()
    {
        var source = SourceText.FromString("a.byn", "let x = 1;\n");
        var diagnostic = new Diagnostic(Severity.Warning, "BYN2000", source, Find(source, "x"), "unused");

        Assert.Equal("""
            warning BYN2000: unused

              1 │ let x = 1;
                │     ^

            """, DiagnosticRenderer.Render(diagnostic));
    }

    [Fact]
    public void CaretsCountCodePointsNotBytes()
    {
        var source = SourceText.FromString("a.byn", "let بيان = \"é\";\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN0005", source, Find(source, "بيان"), "m");

        var caretLine = DiagnosticRenderer.Render(diagnostic).Split('\n')[3];
        Assert.Equal("    │     ^^^^", caretLine);
    }

    [Fact]
    public void TabsInPrefixAreKept()
    {
        var source = SourceText.FromString("a.byn", "\tx;\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1000", source, Find(source, "x"), "m");

        Assert.Equal("    │ \t^", DiagnosticRenderer.Render(diagnostic).Split('\n')[3]);
    }

    [Fact]
    public void EmptySpanGetsOneCaret()
    {
        var source = SourceText.FromString("a.byn", "let x = 1\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1001", source, new TextSpan(9, 0), "expected `;`", Label: "here");

        Assert.Equal("    │          ^ here", DiagnosticRenderer.Render(diagnostic).Split('\n')[3]);
    }

    [Fact]
    public void GutterWidensForFixOnLongerLineNumber()
    {
        var lines = string.Concat(Enumerable.Range(1, 10).Select(i => $"x{i};\n"));
        var source = SourceText.FromString("a.byn", lines);
        var diagnostic = new Diagnostic(Severity.Error, "BYN1002", source, Find(source, "x9;"), "m",
            Fix: new Fix(Find(source, "x10"), "y10"));

        var rendered = DiagnosticRenderer.Render(diagnostic).Split('\n');
        Assert.Equal("   9 │ x9;", rendered[2]);
        Assert.Equal("     │ 10 │ y10;", rendered[5]);
    }

    [Fact]
    public void CrlfLineEndingsAreNotRendered()
    {
        var source = SourceText.FromString("a.byn", "a;\r\nb;\r\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1003", source, Find(source, "b"), "m");

        Assert.Equal("  2 │ b;", DiagnosticRenderer.Render(diagnostic).Split('\n')[2]);
    }

    [Fact]
    public void IncludePathAddsLocationLine()
    {
        var source = SourceText.FromString("dir/a.byn", "a;\n  bé c;\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1004", source, Find(source, "c"), "m");

        Assert.Equal("  --> dir/a.byn:2:6", DiagnosticRenderer.Render(diagnostic, includePath: true).Split('\n')[1]);
    }

    [Fact]
    public void DiagnosticWithoutSourceRendersHeaderOnly()
    {
        var diagnostic = new Diagnostic(Severity.Error, "BYN4001", null, default, "zig not found");

        Assert.Equal("error BYN4001: zig not found\n", DiagnosticRenderer.Render(diagnostic));
    }

    private static readonly Style Dark = new(Palette.TruecolorDark, Symbols.Unicode, 100);

    private static string Strip(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;]*m", "");

    [Fact]
    public void RemovalFixMarksRemovedText()
    {
        var source = SourceText.FromString("a.byn", "let x = 1;;\n");
        var semi = Find(source, ";;");
        var diagnostic = new Diagnostic(Severity.Warning, "BYN1005", source, new TextSpan(semi.Start + 1, 1), "extra `;`",
            Help: "remove the extra `;`", Fix: new Fix(new TextSpan(semi.Start + 1, 1), ""));

        var rendered = DiagnosticRenderer.Render(diagnostic).Split('\n');
        Assert.Equal("    │ 1 │ let x = 1;;", rendered[6]);
        Assert.Equal("    │   │           -", rendered[7]);
    }

    [Fact]
    public void ReplacementMarksOnlyChangedText()
    {
        var source = SourceText.FromString("a.byn", "let bayan = 1;\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN1006", source, Find(source, "bayan"), "m",
            Fix: new Fix(Find(source, "bayan"), "bayaan"));

        var rendered = DiagnosticRenderer.Render(diagnostic).Split('\n');
        Assert.Equal("    │ 1 │ let bayaan = 1;", rendered[5]);
        Assert.Equal("    │   │         +", rendered[6]);
    }

    [Fact]
    public void RelatedSpansRenderInLineOrderInTheNoteColour()
    {
        var diagnostic = new Diagnostic(
            Severity.Error,
            "BYN2104",
            Ledger,
            new TextSpan(Find(Ledger, "total +=").Start, "total".Length),
            "`total` is immutable",
            Label: "cannot be changed",
            Related: [new RelatedSpan(new TextSpan(Find(Ledger, "let total").Start + 4, "total".Length), "declared with `let` here")]);

        Assert.Equal("""
            error BYN2104: `total` is immutable

               9 │     let total = 0;
                 │         ----- declared with `let` here
                 │
              12 │     total += line.price;
                 │     ^^^^^ cannot be changed

            """, DiagnosticRenderer.Render(diagnostic));
        var colored = DiagnosticRenderer.Render(diagnostic, includePath: false, Dark);
        Assert.Contains("\u001b[38;2;180;178;169m----- declared with `let` here\u001b[0m", colored);
        Assert.Contains("\u001b[1m\u001b[38;2;255;138;128m^^^^^ cannot be changed\u001b[0m", colored);
        Assert.Equal(DiagnosticRenderer.Render(diagnostic), Strip(colored));
    }

    [Fact]
    public void AsciiSymbolsReplaceTheGutter()
    {
        var source = SourceText.FromString("a.byn", "let x = 1;\n");
        var diagnostic = new Diagnostic(Severity.Warning, "BYN2000", source, Find(source, "x"), "unused");

        Assert.Equal("""
            warning BYN2000: unused

              1 | let x = 1;
                |     ^

            """, DiagnosticRenderer.Render(diagnostic, includePath: false, Style.Plain with { Symbols = Symbols.Ascii }));
    }

    [Fact]
    public void LongHelpWrapsUnderItsIndent()
    {
        var help = string.Join(" ", Enumerable.Repeat("declare it with `var` if it should change", 4)) + ".";
        Assert.Equal(168, help.Length);
        var diagnostic = new Diagnostic(Severity.Error, "BYN2104", Ledger,
            new TextSpan(Find(Ledger, "total +=").Start, "total".Length), "`total` is immutable", Help: help);

        var rendered = DiagnosticRenderer.Render(diagnostic).Split('\n');
        var helpLines = rendered.SkipWhile(l => !l.StartsWith("  help:", StringComparison.Ordinal)).TakeWhile(l => l.Length > 0).ToArray();
        Assert.Equal(2, helpLines.Length);
        Assert.All(helpLines, l => Assert.True(l.Length <= 100, l));
        Assert.StartsWith("        ", helpLines[1]);
        Assert.Equal(help, string.Join(" ", helpLines.Select(l => l[8..])));
    }

    [Fact]
    public void LongMessageAndLabelWrapAtTheStyleWidth()
    {
        var source = SourceText.FromString("a.byn", "let x = 1;\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN2000", source, Find(source, "x"),
            "one two three four five six", Label: "alpha beta gamma delta");

        Assert.Equal("""
            error BYN2000: one two
            three four five six

              1 │ let x = 1;
                │     ^ alpha beta
                │       gamma delta

            """, DiagnosticRenderer.Render(diagnostic, includePath: false, Style.Plain with { Width = 26 }));
    }

    [Fact]
    public void SourceAndFixLinesNeverWrap()
    {
        var code = "let x = " + string.Join(" + ", Enumerable.Range(1, 60)) + ";";
        var source = SourceText.FromString("a.byn", code + "\n");
        var diagnostic = new Diagnostic(Severity.Error, "BYN2000", source, Find(source, "x"), "m",
            Fix: new Fix(Find(source, "let"), "var"));

        var rendered = DiagnosticRenderer.Render(diagnostic, includePath: false, Style.Plain with { Width = 40 }).Split('\n');
        Assert.Equal("  1 │ " + code, rendered[2]);
        Assert.Equal("    │ 1 │ var" + code[3..], rendered[5]);
    }
}
