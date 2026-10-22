using System.Globalization;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Diagnostics;

public enum Severity
{
    Error,
    Warning,
    Note,
}

/// <summary>A machine-applicable edit: replace <see cref="Span"/> with <see cref="Replacement"/>.</summary>
public sealed record Fix(TextSpan Span, string Replacement);

/// <summary>A secondary location in the diagnostic's source, underlined with <c>---</c>.</summary>
public sealed record RelatedSpan(TextSpan Span, string Label);

/// <summary>
/// A compiler diagnostic. Codes are <c>BYN####</c>, allocated by range:
/// 0001–0999 lexer, 1000–1999 parser, 2000–3999 semantics, 4000–4999 backend/toolchain.
/// </summary>
/// <param name="Source">The file the span points into; null for diagnostics without a location.</param>
/// <param name="Label">Text printed after the carets under <paramref name="Span"/>.</param>
/// <param name="Related">Secondary spans in <paramref name="Source"/>; rendered only, not written to JSON.</param>
public sealed record Diagnostic(
    Severity Severity,
    string Code,
    SourceText? Source,
    TextSpan Span,
    string Message,
    string? Label = null,
    string? Help = null,
    Fix? Fix = null,
    IReadOnlyList<RelatedSpan>? Related = null)
{
    public bool IsError => Severity == Severity.Error;
}

/// <summary>One entry of the message table: a code, its severity and its message template.</summary>
public sealed record DiagnosticDescriptor(string Code, Severity Severity, string MessageFormat)
{
    public string Format(params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, MessageFormat, args);
}

/// <summary>Collects diagnostics; allocates nothing until the first one is reported.</summary>
public sealed class DiagnosticBag
{
    private List<Diagnostic>? _items;

    public int Count => _items?.Count ?? 0;

    public bool HasErrors { get; private set; }

    public IReadOnlyList<Diagnostic> Items => (IReadOnlyList<Diagnostic>?)_items ?? Array.Empty<Diagnostic>();

    public void Add(Diagnostic diagnostic)
    {
        (_items ??= new List<Diagnostic>()).Add(diagnostic);
        if (diagnostic.IsError) HasErrors = true;
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics) Add(d);
    }

    public void Remove(Diagnostic diagnostic) => _items?.Remove(diagnostic);

    /// <summary>Puts <paramref name="replacement"/> in the place of <paramref name="diagnostic"/>, if it is still reported.</summary>
    public void Replace(Diagnostic diagnostic, Diagnostic replacement)
    {
        var i = _items?.FindIndex(d => ReferenceEquals(d, diagnostic)) ?? -1;
        if (i >= 0) _items![i] = replacement;
    }

    /// <summary>Drops each diagnostic that repeats an earlier one's code, file, span and message.</summary>
    public void RemoveRepeats()
    {
        var seen = new HashSet<(string, SourceText?, TextSpan, string)>();
        _items?.RemoveAll(d => !seen.Add((d.Code, d.Source, d.Span, d.Message)));
    }

    public Diagnostic Report(
        DiagnosticDescriptor descriptor,
        SourceText? source,
        TextSpan span,
        object?[]? args = null,
        string? label = null,
        string? help = null,
        Fix? fix = null)
    {
        var d = new Diagnostic(descriptor.Severity, descriptor.Code, source, span,
            descriptor.Format(args ?? Array.Empty<object?>()), label, help, fix);
        Add(d);
        return d;
    }
}
