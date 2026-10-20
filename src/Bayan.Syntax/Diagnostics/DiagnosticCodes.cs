namespace Bayan.Syntax.Diagnostics;

/// <summary>
/// The message table. Every code here has <c>docs/diagnostics/BYN####.md</c> and at least one
/// golden case; <c>DiagnosticCodesTests</c> enforces both.
/// </summary>
public static class DiagnosticCodes
{
    private static readonly List<DiagnosticDescriptor> Registry = new();

    private static DiagnosticDescriptor Error(string code, string format) => Register(new(code, Severity.Error, format));
    private static DiagnosticDescriptor Warning(string code, string format) => Register(new(code, Severity.Warning, format));

    private static DiagnosticDescriptor Register(DiagnosticDescriptor d)
    {
        Registry.Add(d);
        return d;
    }

    public static IReadOnlyList<DiagnosticDescriptor> All => Registry;

    // ── Lexer 0001–0999 ──────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor BidiControl = Error("BYN0001", "bidirectional control character U+{0} is not allowed outside string literals");
    public static readonly DiagnosticDescriptor UnexpectedCharacter = Error("BYN0002", "unexpected character `{0}`");
    public static readonly DiagnosticDescriptor UnterminatedString = Error("BYN0003", "unterminated string literal");
    public static readonly DiagnosticDescriptor UnterminatedComment = Error("BYN0004", "unterminated block comment");
    public static readonly DiagnosticDescriptor IdentifierNotNfc = Error("BYN0005", "identifier `{0}` is not in Unicode normalization form C");
    public static readonly DiagnosticDescriptor InvalidEscape = Error("BYN0006", "invalid escape sequence `{0}`");
    public static readonly DiagnosticDescriptor EmptyCharLiteral = Error("BYN0007", "empty char literal");
    public static readonly DiagnosticDescriptor UnterminatedCharLiteral = Error("BYN0008", "unterminated char literal");
    public static readonly DiagnosticDescriptor CharLiteralTooLong = Error("BYN0009", "a char literal holds exactly one Unicode scalar");
    public static readonly DiagnosticDescriptor InvalidNumber = Error("BYN0010", "invalid numeric literal: {0}");
    public static readonly DiagnosticDescriptor SuffixOnHexOrBinary = Error("BYN0011", "unit suffix `{0}` is not allowed on a {1} literal");
    public static readonly DiagnosticDescriptor ReservedKeyword = Error("BYN0012", "`{0}` is reserved and cannot be used as an identifier");
    public static readonly DiagnosticDescriptor InvalidUnicodeEscape = Error("BYN0013", "`{0}` is not a Unicode scalar value");
    public static readonly DiagnosticDescriptor UnescapedCloseBrace = Error("BYN0014", "`}}` in an interpolated string must be doubled");
    public static readonly DiagnosticDescriptor InvalidUtf8 = Error("BYN0015", "source is not valid UTF-8 (byte 0x{0})");
    public static readonly DiagnosticDescriptor InterpolationTooDeep = Error("BYN0016", "interpolated strings are nested too deeply");

    // ── Parser 1000–1999 ─────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor Expected = Error("BYN1001", "expected {0}, found {1}");
    public static readonly DiagnosticDescriptor ExpectedExpression = Error("BYN1002", "expected an expression, found {0}");
    public static readonly DiagnosticDescriptor ExpectedType = Error("BYN1003", "expected a type, found {0}");
    public static readonly DiagnosticDescriptor ExpectedMember = Error("BYN1004", "expected a field, property, constructor or method, found {0}");
    public static readonly DiagnosticDescriptor MissingSemicolon = Error("BYN1005", "expected `;` after the statement");
    public static readonly DiagnosticDescriptor UnlessWithElse = Error("BYN1006", "`unless` cannot have an `else` branch");
    public static readonly DiagnosticDescriptor UnlessNegated = Error("BYN1007", "`unless` with a negated condition is a double negative");
    public static readonly DiagnosticDescriptor PostfixNotSimple = Error("BYN1008", "a postfix `{0}` applies only to a simple statement");
    public static readonly DiagnosticDescriptor PostfixChained = Error("BYN1009", "postfix conditions cannot be chained");
    public static readonly DiagnosticDescriptor Goto = Error("BYN1010", "`goto` does not exist in Bayan");
    public static readonly DiagnosticDescriptor InvalidAssignmentTarget = Error("BYN1011", "cannot assign to this expression");
    public static readonly DiagnosticDescriptor InvalidExpressionStatement = Error("BYN1012", "this expression does nothing as a statement");
    public static readonly DiagnosticDescriptor TernaryInCollection = Error("BYN1013", "a conditional expression inside a collection literal must be parenthesized");
    public static readonly DiagnosticDescriptor StackedCaseLabels = Error("BYN1014", "stacked `case` labels");
    public static readonly DiagnosticDescriptor CStyleFor = Error("BYN1015", "C-style `for` loops do not exist in Bayan");
    public static readonly DiagnosticDescriptor ModifierNotAllowed = Error("BYN1016", "`{0}` is not allowed here");
}
