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
}
