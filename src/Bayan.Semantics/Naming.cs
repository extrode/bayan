using System.Text;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <summary>
/// Casing rules (spec §5.1): types are <c>PascalCase</c>, everything else <c>camelCase</c>,
/// acronyms longer than two letters are written as words (<c>HttpClient</c>, <c>IO</c>).
/// Scripts without case (Arabic, …) are not constrained. Also the grammar checker (BYN5xxx).
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

    /// <summary>
    /// Grammar rules on the members of <paramref name="s"/> (spec §5.2, <c>docs/bayan-grammar.md</c>):
    /// BYN5001–5003 are pattern contracts, BYN5101–5102 word-class lints.
    /// </summary>
    internal static void CheckMembers(Compilation c, StructSymbol s)
    {
        var source = s.File.Source;
        foreach (var fn in s.Methods)
        {
            var name = fn.Decl!.Name;
            var noArgs = !fn.IsStatic && fn.Parameters.Count == 0;
            if (!fn.IsMutating && !fn.ReturnType.IsBool) CheckParticiple(c, s, source, name);
            if (noArgs && fn.IsMutating && IsStateName(fn.Name))
                c.Report(DiagnosticCodes.StateMemberMutates, source, name.Span, [fn.Name], label: "declared `ref this`",
                    help: "remove `ref this` so it only reads, or name the action with a verb");
            if (fn.Name.Length > 3 && fn.Name.StartsWith("try", StringComparison.Ordinal) && char.IsUpper(fn.Name[3])
                && s.Methods.FirstOrDefault(m => m.Fails && m.Name == char.ToLowerInvariant(fn.Name[3]) + fn.Name[4..]) is { } failing
                && fn.ReturnType is not OptionalType && !fn.ReturnType.IsBool && !fn.ReturnType.IsError)
                c.Report(DiagnosticCodes.TryWrongReturn, source, fn.Decl.ReturnType.Span, [fn.Name[3..], failing.ReturnType.DisplayName, failing.Name],
                    label: $"returns `{fn.ReturnType.DisplayName}`",
                    help: $"change it to return `{failing.ReturnType.DisplayName}?` or `bool`, or remove `{fn.Name}` and call `try {failing.Name}()`");
            if (fn.IsMutating && Words(fn.Name) is [var first, ..] && Nouns.Contains(first.ToLowerInvariant()))
                c.Report(DiagnosticCodes.NounOnMutator, source, name.Span, [fn.Name],
                    help: "rename it with the verb for what it does to `this`");
            if (noArgs && !fn.IsMutating && fn.ReturnType.IsBool)
                CheckBoolState(c, source, name);
        }
        foreach (var p in s.Properties)
        {
            if (p.Type.IsBool) CheckBoolState(c, source, p.Decl.Name);
            else CheckParticiple(c, s, source, p.Decl.Name);
        }
    }

    private static void CheckParticiple(Compilation c, StructSymbol s, SourceText source, Name name)
    {
        if (ParticipleStems(name.Text) is not { Count: > 0 } stems || stems.Any(stem => s.Methods.Any(m => m.IsMutating && m.Name == stem))) return;
        c.Report(DiagnosticCodes.ParticipleWithoutVerb, source, name.Span, [name.Text, stems[0]],
            help: $"declare `{stems[0]}(ref this)` and `{name.Text}` is derived; a member that returns a new value is a plain verb");
    }

    private static void CheckBoolState(Compilation c, SourceText source, Name name)
    {
        if (IsStateName(name.Text) || NotStates.Any(w => name.Text.StartsWith(w, StringComparison.Ordinal))) return;
        var fixedName = "is" + char.ToUpperInvariant(name.Text[0]) + name.Text[1..];
        c.Report(DiagnosticCodes.BoolWithoutPrefix, source, name.Span, [name.Text],
            help: $"rename it `{fixedName}`", fix: new Fix(name.Span, fixedName));
    }

    /// <summary><c>isX</c>, <c>hasX</c>, <c>canX</c>: the prefix, then a capital.</summary>
    private static bool IsStateName(string name) =>
        new[] { "is", "has", "can" }.Any(p => name.Length > p.Length && name.StartsWith(p, StringComparison.Ordinal) && char.IsUpper(name[p.Length]));

    /// <summary>
    /// The verbs <paramref name="name"/> may be the <c>-ed</c> form of, most likely first; empty when it is
    /// not a participle. Only a single lowercase word ending in <c>-ed</c> counts: <c>sorted</c> → sort,
    /// <c>reversed</c> → reverse, <c>trimmed</c> → trim, <c>copied</c> → copy. Words ending in
    /// <c>-eed</c> (<c>need</c>, <c>speed</c>), words shorter than five letters (<c>red</c>, <c>bed</c>, <c>used</c>)
    /// and <see cref="NotParticiples"/> are not participles.
    /// </summary>
    public static List<string> ParticipleStems(string name)
    {
        var stems = new List<string>();
        if (name.Length < 5 || !name.EndsWith("ed", StringComparison.Ordinal) || name.EndsWith("eed", StringComparison.Ordinal)
            || !name.All(char.IsAsciiLetterLower) || NotParticiples.Contains(name)) return stems;
        var bare = name[..^2];
        if (bare.EndsWith('i')) stems.Add(bare[..^1] + "y");
        if (bare.Length >= 4 && bare[^1] == bare[^2] && !"aeiouls".Contains(bare[^1])) stems.Add(bare[..^1]);
        // `reversed`, `parsed`, `doubled`, `named` drop only the `d`; `sorted`, `joined` drop `ed`.
        var keepsE = "cuvz".Contains(bare[^1]) || (bare[^1] == 's' && bare[^2] != 's') || (bare[^1] == 'l' && !IsVowel(bare[^2]) && bare[^2] != 'l')
            || (bare.Length == 3 && !IsVowel(bare[0]) && IsVowel(bare[1]) && !IsVowel(bare[2]) && !"wxy".Contains(bare[2]));
        stems.AddRange(keepsE ? [name[..^1], bare] : [bare, name[..^1]]);
        return stems;
    }

    private static bool IsVowel(char ch) => "aeiou".Contains(ch);

    /// <summary>Common words ending in <c>-ed</c> that are not the <c>-ed</c> form of a verb.</summary>
    private static readonly HashSet<string> NotParticiples = ["hundred", "embed", "shred", "naked", "wicked", "sacred", "kindred"];

    /// <summary>The first word of a <c>ref this</c> member that names a thing, not an action (BYN5101).</summary>
    private static readonly HashSet<string> Nouns =
    [
        "size", "length", "total", "sum", "value", "values", "data", "item", "items", "element", "elements",
        "result", "results", "name", "id", "state", "status", "info", "content", "contents", "capacity",
        "amount", "number", "key", "keys", "entry", "entries", "node", "nodes",
    ];

    /// <summary>Verbs that ask a question and need no prefix (BYN5102): <c>contains</c>, <c>equals</c>, …; <c>tryX</c> is an attempt.</summary>
    private static readonly string[] NotStates = ["contains", "equals", "matches", "startsWith", "endsWith", "try"];

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
