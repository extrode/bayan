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

/// <summary>
/// A compiler diagnostic. Codes are <c>BYN####</c>, allocated by range:
/// 0001–0999 lexer, 1000–1999 parser, 2000–3999 semantics, 4000–4999 backend/toolchain.
/// </summary>
/// <param name="Label">Text printed after the carets under <paramref name="Span"/>.</param>
public sealed record Diagnostic(
    Severity Severity,
    string Code,
    TextSpan Span,
    string Message,
    string? Label = null,
    string? Help = null,
    Fix? Fix = null);
