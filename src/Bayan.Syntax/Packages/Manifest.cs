using System.Globalization;
using System.Text;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Packages;

public enum PackageKind { App, Lib }

/// <summary>
/// <c>package.byn</c> (spec §19.2): data only. The grammar is <c>Type { field: value, … }</c>
/// (<c>field = value</c> is still read) where a value is a string, an integer, <c>true</c>,
/// <c>false</c>, <c>.name</c>, <c>[value, …]</c> or another
/// <c>Type { … }</c>. Fields the compiler doesn't use yet are checked for syntax only.
/// </summary>
public sealed class Manifest
{
    /// <summary>Fields of <c>Package</c>; the first four are read, the rest are accepted as written.</summary>
    private static readonly string[] PackageFields = ["name", "version", "edition", "kind", "codes", "capabilities", "deps", "test", "dist", "style"];

    private static readonly string[] DistFields = ["targets", "mode"];

    /// <summary>Fields of <c>Style</c> (HANDOFF §18); checked here, read by nothing yet.</summary>
    private static readonly string[] StyleFields = ["indent", "braces", "maxLine", "trailingCommas"];

    private Manifest(SourceText source) => Source = source;

    public SourceText Source { get; }

    /// <summary><c>namespace/name</c>, e.g. <c>local/tally</c>.</summary>
    public string Name { get; private set; } = "";

    /// <summary>The part of <see cref="Name"/> after the <c>/</c>.</summary>
    public string ShortName => Name[(Name.IndexOf('/') + 1)..];

    public string Version { get; private set; } = "";

    public int Edition { get; private set; } = 2027;

    public PackageKind Kind { get; private set; } = PackageKind.App;

    /// <summary><c>dist.targets</c> as written (<c>host</c>, <c>linux-x64</c>, …); empty when absent.</summary>
    public List<string> DistTargets { get; } = new();

    /// <summary><c>dist.mode</c>; <c>safe</c> when absent.</summary>
    public string DistMode { get; private set; } = "safe";

    public List<Diagnostic> Diagnostics { get; } = new();

    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    public static Manifest Read(string path) => Parse(new SourceText("package.byn", File.ReadAllBytes(path)));

    public static Manifest Parse(SourceText source)
    {
        var m = new Manifest(source);
        var lex = Lexer.Lex(source);
        m.Diagnostics.AddRange(lex.Diagnostics);
        if (m.HasErrors) return m;
        var reader = new Reader(source, lex.Buffer, lex.Count, m.Diagnostics);
        var root = reader.ReadValue();
        if (root is not null && !reader.AtEnd) reader.Fail("the end of the file after `Package { … }`");
        if (m.HasErrors) return m;
        if (root is not ObjectValue { Type: "Package" } package)
        {
            m.Report(DiagnosticCodes.ManifestNotData, root!.Span, ["`Package { … }`"]);
            return m;
        }
        m.Bind(package);
        return m;
    }

    private void Bind(ObjectValue package)
    {
        var fields = Fields(package, PackageFields);
        if (Required(fields, "name", package) is { } name && Text(name, "name", "a string `namespace/name` of lowercase words joined by `-`, e.g. `\"local/tally\"`") is { } n)
        {
            if (IsPackageName(n)) Name = n;
            else Invalid(name, "name", "a string `namespace/name` of lowercase words joined by `-`, e.g. `\"local/tally\"`");
        }
        if (Required(fields, "version", package) is { } version && Text(version, "version", "a semantic version string, e.g. `\"0.1.0\"`") is { } v)
        {
            if (IsSemanticVersion(v)) Version = v;
            else Invalid(version, "version", "a semantic version string, e.g. `\"0.1.0\"`");
        }
        if (fields.TryGetValue("edition", out var edition))
        {
            if (edition is IntValue { Value: 2027 }) Edition = 2027;
            else Invalid(edition, "edition", "`2027`, the only edition so far");
        }
        if (fields.TryGetValue("kind", out var kind))
        {
            switch (kind)
            {
                case EnumValue { Name: "app" }: Kind = PackageKind.App; break;
                case EnumValue { Name: "lib" }: Kind = PackageKind.Lib; break;
                default: Invalid(kind, "kind", "`.app` or `.lib`"); break;
            }
        }
        if (fields.TryGetValue("style", out var style))
        {
            if (style is not ObjectValue { Type: "Style" } st)
            {
                Invalid(style, "style", "a `Style { … }`");
            }
            else
            {
                var styleFields = Fields(st, StyleFields);
                if (styleFields.TryGetValue("indent", out var indent) && indent is not IntValue { Value: > 0 })
                    Invalid(indent, "style.indent", "a positive integer, e.g. `4`");
                if (styleFields.TryGetValue("braces", out var braces) && braces is not EnumValue { Name: "sameLine" or "nextLine" })
                    Invalid(braces, "style.braces", "`.sameLine` or `.nextLine`");
                if (styleFields.TryGetValue("maxLine", out var maxLine) && maxLine is not IntValue { Value: > 0 })
                    Invalid(maxLine, "style.maxLine", "a positive integer, e.g. `100`");
                if (styleFields.TryGetValue("trailingCommas", out var trailingCommas) && trailingCommas is not BoolValue)
                    Invalid(trailingCommas, "style.trailingCommas", "`true` or `false`");
            }
        }
        if (fields.TryGetValue("dist", out var dist))
        {
            if (dist is not ObjectValue { Type: "Dist" } d)
            {
                Invalid(dist, "dist", "a `Dist { … }`");
                return;
            }
            var distFields = Fields(d, DistFields);
            if (distFields.TryGetValue("targets", out var targets))
            {
                if (targets is ListValue list && list.Items.All(t => t is EnumValue))
                    DistTargets.AddRange(list.Items.Cast<EnumValue>().Select(t => KebabOf(t.Name)));
                else
                    Invalid(targets, "dist.targets", "a list of targets, e.g. `[.host, .linuxX64]`");
            }
            if (distFields.TryGetValue("mode", out var mode))
            {
                if (mode is EnumValue { Name: "debug" or "safe" or "fast" } e) DistMode = e.Name;
                else Invalid(mode, "dist.mode", "`.debug`, `.safe` or `.fast`");
            }
        }
    }

    private Dictionary<string, Value> Fields(ObjectValue obj, string[] known)
    {
        var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var (name, span, value) in obj.Fields)
        {
            if (!known.Contains(name))
                Report(DiagnosticCodes.ManifestUnknownField, span, [name, obj.Type], help: "use one of the fields " + string.Join(", ", known.Select(k => $"`{k}`")));
            else if (!fields.TryAdd(name, value))
                Report(DiagnosticCodes.ManifestNotData, span, [$"`{name}` only once"]);
        }
        return fields;
    }

    private Value? Required(Dictionary<string, Value> fields, string name, ObjectValue package)
    {
        if (fields.TryGetValue(name, out var value)) return value;
        Report(DiagnosticCodes.ManifestMissingField, new TextSpan(package.Span.Start, package.Type.Length), [name]);
        return null;
    }

    private string? Text(Value value, string field, string expected)
    {
        if (value is StringValue s) return s.Value;
        Invalid(value, field, expected);
        return null;
    }

    private void Invalid(Value value, string field, string expected) =>
        Report(DiagnosticCodes.ManifestInvalidValue, value.Span, [field, expected]);

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, object?[] args, string? help = null) =>
        Diagnostics.Add(new Diagnostic(descriptor.Severity, descriptor.Code, Source, span, descriptor.Format(args), Help: help));

    /// <summary><c>linuxX64</c> → <c>linux-x64</c>: enum names are identifiers, target names are kebab-case.</summary>
    public static string KebabOf(string camel)
    {
        var sb = new StringBuilder();
        foreach (var c in camel)
        {
            if (char.IsAsciiLetterUpper(c)) sb.Append('-').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary><c>my-lib</c> → <c>myLib</c>.</summary>
    public static string CamelOf(string kebab)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < kebab.Length; i++)
        {
            if (kebab[i] == '-' && i + 1 < kebab.Length) sb.Append(char.ToUpperInvariant(kebab[++i]));
            else sb.Append(kebab[i]);
        }
        return sb.ToString();
    }

    /// <summary>Lowercase words joined by <c>-</c>, e.g. <c>tally</c> or <c>my-app</c>.</summary>
    public static bool IsKebabName(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetterLower(text[0]) || text[^1] == '-') return false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '-' ? text[i - 1] == '-' : !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    public static bool IsPackageName(string text) =>
        text.Split('/') is [var ns, var name] && IsKebabName(ns) && IsKebabName(name);

    /// <summary>
    /// <c>MAJOR.MINOR.PATCH</c> (each at most 18 digits, so it fits a <c>long</c>), optionally
    /// <c>-</c> and dot-separated identifiers of <c>[0-9A-Za-z-]</c> (semver 2.0 §9).
    /// </summary>
    public static bool IsSemanticVersion(string text)
    {
        var build = text.Split('+', 2);
        var parts = build[0].Split('-', 2);
        return parts[0].Split('.') is [var a, var b, var c]
            && new[] { a, b, c }.All(p => p.Length is > 0 and <= 18 && p.All(char.IsAsciiDigit) && (p == "0" || p[0] != '0'))
            && (parts.Length == 1 || Identifiers(parts[1]))
            && (build.Length == 1 || Identifiers(build[1]));

        static bool Identifiers(string text) =>
            text.Split('.').All(id => id.Length > 0 && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'));
    }

    /// <summary>
    /// Orders semantic versions by their numeric core, then a release above its prereleases, then
    /// prerelease text ordinally.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var (ca, pa) = SplitVersion(a);
        var (cb, pb) = SplitVersion(b);
        for (var i = 0; i < 3; i++)
        {
            var c = ca[i].CompareTo(cb[i]);
            if (c != 0) return c;
        }
        if (pa is null || pb is null) return (pa is null ? 1 : 0) - (pb is null ? 1 : 0);
        return string.CompareOrdinal(pa, pb);
    }

    private static (long[] Core, string? Pre) SplitVersion(string v)
    {
        var parts = v.Split('+', 2)[0].Split('-', 2);
        var core = parts[0].Split('.').Select(p => long.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return (core, parts.Length > 1 ? parts[1] : null);
    }

    // ── Values ───────────────────────────────────────────────────────────────────

    private abstract record Value(TextSpan Span);
    private sealed record StringValue(TextSpan Span, string Value) : Value(Span);
    private sealed record IntValue(TextSpan Span, long Value) : Value(Span);
    private sealed record BoolValue(TextSpan Span, bool Value) : Value(Span);
    private sealed record EnumValue(TextSpan Span, string Name) : Value(Span);
    private sealed record ListValue(TextSpan Span, List<Value> Items) : Value(Span);
    private sealed record ObjectValue(TextSpan Span, string Type, List<(string Name, TextSpan NameSpan, Value Value)> Fields) : Value(Span);

    private sealed class Reader(SourceText source, Token[] tokens, int count, List<Diagnostic> diagnostics)
    {
        private int _pos;

        private Token Current => tokens[Math.Min(_pos, count - 1)];

        public bool AtEnd => Current.Kind == TokenKind.EndOfFile;

        private string TextOf(Token t) => Encoding.UTF8.GetString(source.Bytes.Span.Slice(t.Start, t.Length));

        public void Fail(string expected)
        {
            if (diagnostics.Any(d => d.IsError)) return;
            var d = DiagnosticCodes.ManifestNotData;
            diagnostics.Add(new Diagnostic(d.Severity, d.Code, source, Current.Span, d.Format([expected]),
                Help: "write strings, integers, `true`/`false`, `.names`, `[lists]` or `Type { field: value }`; no calls or expressions"));
        }

        private bool Accept(TokenKind kind)
        {
            if (Current.Kind != kind) return false;
            _pos++;
            return true;
        }

        /// <summary>Field and enum names may be keywords (<c>test</c>, <c>async</c>).</summary>
        private static bool IsName(TokenKind kind) => kind == TokenKind.Identifier || kind.IsKeyword();

        private bool AcceptName()
        {
            if (!IsName(Current.Kind)) return false;
            _pos++;
            return true;
        }

        public Value? ReadValue()
        {
            var t = Current;
            switch (t.Kind)
            {
                case TokenKind.StringLiteral:
                    _pos++;
                    return new StringValue(t.Span, LiteralDecoder.DecodeString(TextOf(t)));
                case TokenKind.IntegerLiteral when long.TryParse(TextOf(t).Replace("_", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var n):
                    _pos++;
                    return new IntValue(t.Span, n);
                case TokenKind.TrueKeyword or TokenKind.FalseKeyword:
                    _pos++;
                    return new BoolValue(t.Span, t.Kind == TokenKind.TrueKeyword);
                case TokenKind.Dot when IsName(tokens[_pos + 1].Kind):
                    _pos += 2;
                    return new EnumValue(TextSpan.FromBounds(t.Start, tokens[_pos - 1].End), TextOf(tokens[_pos - 1]));
                case TokenKind.OpenBracket:
                {
                    _pos++;
                    var items = new List<Value>();
                    while (!Accept(TokenKind.CloseBracket))
                    {
                        if (ReadValue() is not { } item) return null;
                        items.Add(item);
                        if (!Accept(TokenKind.Comma) && Current.Kind != TokenKind.CloseBracket)
                        {
                            Fail("`,` or `]`");
                            return null;
                        }
                    }
                    return new ListValue(TextSpan.FromBounds(t.Start, tokens[_pos - 1].End), items);
                }
                case TokenKind.Identifier when tokens[_pos + 1].Kind == TokenKind.OpenBrace:
                {
                    _pos += 2;
                    var fields = new List<(string, TextSpan, Value)>();
                    while (!Accept(TokenKind.CloseBrace))
                    {
                        var name = Current;
                        if (!AcceptName() || !Accept(TokenKind.Colon) && !Accept(TokenKind.Equals))
                        {
                            Fail("`field: value` or `}`");
                            return null;
                        }
                        if (ReadValue() is not { } value) return null;
                        fields.Add((TextOf(name), name.Span, value));
                        if (!Accept(TokenKind.Comma) && Current.Kind != TokenKind.CloseBrace)
                        {
                            Fail("`,` or `}`");
                            return null;
                        }
                    }
                    return new ObjectValue(TextSpan.FromBounds(t.Start, tokens[_pos - 1].End), TextOf(t), fields);
                }
                default:
                    Fail("a string, an integer, `true`, `false`, `.name`, `[…]` or `Type { … }`");
                    return null;
            }
        }
    }
}
