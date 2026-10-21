using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <summary>
/// Casing rules (spec §5.1): types are <c>PascalCase</c>, everything else <c>camelCase</c>,
/// acronyms longer than two letters are written as words (<c>HttpClient</c>, <c>IO</c>).
/// Scripts without case (Arabic, …) are not constrained.
/// </summary>
public static class Naming
{
    internal static void CheckType(Compilation c, SourceText source, Name name)
    {
        if (name.Text == "_") return;
        var fixedName = ToPascal(name.Text);
        if (fixedName != name.Text)
            c.Report(DiagnosticCodes.TypeCasing, source, name.Span, [name.Text],
                help: $"rename it `{fixedName}`", fix: new Fix(name.Span, fixedName));
    }

    internal static void CheckValue(Compilation c, SourceText source, Name name)
    {
        if (name.Text == "_") return;
        var fixedName = ToCamel(name.Text);
        if (fixedName != name.Text)
            c.Report(DiagnosticCodes.ValueCasing, source, name.Span, [name.Text],
                help: char.IsUpper(name.Text[0]) && fixedName.Length > 0
                    ? $"rename it `{fixedName}`; only types are PascalCase"
                    : $"rename it `{fixedName}`",
                fix: new Fix(name.Span, fixedName));
    }

    public static string ToPascal(string name)
    {
        var words = Words(name);
        if (words.Count == 0) return name;
        var sb = new StringBuilder();
        foreach (var w in words) sb.Append(Capitalize(w));
        return sb.ToString();
    }

    public static string ToCamel(string name)
    {
        var words = Words(name);
        if (words.Count == 0) return name;
        var sb = new StringBuilder(words[0].ToLowerInvariant());
        foreach (var w in words.Skip(1)) sb.Append(Capitalize(w));
        return sb.ToString();
    }

    /// <summary>Two-letter acronyms stay uppercase (<c>IO</c>); other words are capitalized.</summary>
    private static string Capitalize(string word)
    {
        if (word.Length == 0) return word;
        if (word.Length == 2 && word.All(char.IsUpper)) return word;
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Splits at underscores and case changes: <c>HTTPClient</c> → HTTP, Client;
    /// <c>parse_url</c> → parse, url; <c>IOError</c> → IO, Error. Uncased letters and digits
    /// stay with the word they follow.
    /// </summary>
    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (ch == '_')
            {
                Flush();
                continue;
            }
            if (char.IsUpper(ch) && current.Length > 0)
            {
                var prev = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextIsLower)) Flush();
            }
            current.Append(ch);
        }
        Flush();
        return words;
    }
}
