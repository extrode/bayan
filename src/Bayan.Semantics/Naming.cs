using System.Runtime.CompilerServices;
using System.Text;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;
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

    /// <summary>An interface name is <c>I</c>, then an uppercase letter, then PascalCase (<c>IShape</c>).</summary>
    internal static void CheckInterface(Compilation c, SourceText source, Name name)
    {
        if (name.Text.Length > 1 && name.Text[0] == 'I' && char.IsUpper(name.Text[1]))
        {
            CheckType(c, source, name);
            return;
        }
        var fixedName = "I" + ToPascal(name.Text);
        c.Report(DiagnosticCodes.InterfaceName, source, name.Span, [name.Text], label: "an interface name",
            help: $"rename it to `{fixedName}`", fix: new Fix(name.Span, fixedName));
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
    /// BYN50xx are pattern contracts on all code, BYN51xx word-class lints on the public API only (J84).
    /// Math vocabulary (J70) is exempt from BYN5001 and BYN5014; typestate states are not members and are not checked.
    /// </summary>
    internal static void CheckMembers(Compilation c, StructSymbol s)
    {
        var source = s.File.Source;
        var grammar = GrammarOf(c);
        foreach (var fn in s.Methods)
        {
            var name = fn.Decl!.Name;
            var noArgs = !fn.IsStatic && fn.Parameters.Count == 0;
            var isPublic = s.Access == Accessibility.Public && fn.Access == Accessibility.Public;
            if (fn.IsMutating && !fn.ReturnType.IsBool) CheckParticiple(c, grammar, source, name);
            CheckParticleName(c, s, source, name, fn.IsStatic);
            CheckWords(c, grammar, source, name);
            if (noArgs && fn.IsMutating && IsStateName(fn.Name))
                c.Report(DiagnosticCodes.StateMemberMutates, source, name.Span, [fn.Name], label: "declared `ref this`",
                    help: "remove `ref this` so it only reads, or name the action with a verb");
            if (fn.Name.Length > 3 && fn.Name.StartsWith("try", StringComparison.Ordinal) && char.IsUpper(fn.Name[3])
                && s.Methods.FirstOrDefault(m => m.Fails && m.Name == char.ToLowerInvariant(fn.Name[3]) + fn.Name[4..]) is { } failing
                && fn.ReturnType is not OptionalType && !fn.ReturnType.IsBool && !fn.ReturnType.IsError)
                c.Report(DiagnosticCodes.TryWrongReturn, source, fn.Decl.ReturnType.Span, [fn.Name[3..], failing.ReturnType.DisplayName, failing.Name],
                    label: $"returns `{fn.ReturnType.DisplayName}`",
                    help: $"change it to return `{failing.ReturnType.DisplayName}?` or `bool`, or remove `{fn.Name}` and call `try {failing.Name}()`");
            if (isPublic && fn.IsMutating && Words(fn.Name) is [var first, ..] && Nouns.Contains(first.ToLowerInvariant()))
                c.Report(DiagnosticCodes.NounOnMutator, source, name.Span, [fn.Name],
                    help: "rename it with the verb for what it does to `this`");
            if (isPublic && noArgs && !fn.IsMutating && fn.ReturnType.IsBool)
                CheckBoolState(c, source, name);
        }
        foreach (var p in s.Properties)
        {
            CheckParticleName(c, s, source, p.Decl.Name, isStatic: false);
            CheckWords(c, grammar, source, p.Decl.Name);
            if (p.Type.IsBool && s.Access == Accessibility.Public && p.Access == Accessibility.Public) CheckBoolState(c, source, p.Decl.Name);
        }
    }

    /// <summary>BYN5001: an <c>-ed</c> name is a changed copy or a view, so a member that takes <c>ref this</c> is a plain verb (revision 0005).</summary>
    private static void CheckParticiple(Compilation c, Grammar grammar, SourceText source, Name name)
    {
        if (Lexicon.Math.Contains(name.Text)) return;
        var stems = grammar.Forms.Where(f => f.Value == name.Text).Select(f => f.Key).ToList();
        if (stems.Count == 0) stems = ParticipleStems(name.Text);
        if (stems.Count == 0) return;
        var stem = stems.FirstOrDefault(w => Lexicon.Verbs.Contains(w) || grammar.Verbs.Contains(w)) ?? stems[0];
        c.Report(DiagnosticCodes.ParticipleChangesThis, source, name.Span, [name.Text, stem],
            help: "name the member that changes `this` with the plain verb, and keep `-ed` for the copy or view");
    }

    /// <summary>
    /// BYN5009: a particle is not a member name, and no instance member spells a conversion with <c>to</c> or <c>as</c>:
    /// a view is a noun (<c>asSpan</c> → <c>span</c>), a conversion is <c>T(x)</c> (<c>toList</c> → <c>List(x)</c>).
    /// </summary>
    private static void CheckParticleName(Compilation c, StructSymbol s, SourceText source, Name name, bool isStatic)
    {
        if (Lexicon.Particles.Contains(name.Text))
        {
            c.Report(DiagnosticCodes.ParticleAsMemberName, source, name.Span, [name.Text],
                help: "rename the member, for example `tryGet(at:)` instead of `at(…)`");
            return;
        }
        if (isStatic || Words(name.Text) is not [var particle and ("to" or "as"), var first, ..] || !char.IsUpper(first[0])) return;
        var rest = name.Text[particle.Length..];
        var instead = particle == "as"
            ? char.ToLowerInvariant(rest[0]) + rest[1..]
            : $"{(Primitives.Contains(rest.ToLowerInvariant()) ? rest.ToLowerInvariant() : rest)}({ToCamel(s.Name)})";
        c.Report(DiagnosticCodes.ConversionMember, source, name.Span, [name.Text, particle, instead],
            help: "use a view noun, `copy()`, or a `T(x)` conversion");
    }

    /// <summary>The primitive type names a <c>toX</c> conversion is written in lowercase: <c>toString</c> → <c>string(x)</c>.</summary>
    private static readonly HashSet<string> Primitives =
    [
        "string", "bool", "rune", "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64", "f32", "f64",
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double",
    ];

    /// <summary>
    /// BYN5014: every word of a member name is a word the checker knows, from <see cref="Lexicon"/> or the
    /// project's <c>grammar</c> (J85), directly or by a regular ending (<see cref="Bases"/>). Names with letters
    /// outside ASCII, single letters and math names are not checked.
    /// </summary>
    private static void CheckWords(Compilation c, Grammar grammar, SourceText source, Name name)
    {
        if (Lexicon.Math.Contains(name.Text) || !name.Text.All(char.IsAscii)) return;
        foreach (var (start, text) in WordSpans(name.Text))
        {
            var word = text.ToLowerInvariant().TrimEnd("0123456789".ToCharArray());
            if (word.Length < 2 || !word.All(char.IsAsciiLetterLower) || Lexicon.Math.Contains(text.ToLowerInvariant())) continue;
            if (IsKnown(word, grammar)) continue;
            c.Report(DiagnosticCodes.UnknownWord, source, new TextSpan(name.Span.Start + start, text.Length), [word],
                help: $"add it to `grammar` in package.byn: `verbs: [\"{word}\"]`, `nouns: [\"{word}\"]` or `adjectives: [\"{word}\"]`");
        }
    }

    private static bool IsKnown(string word, Grammar grammar)
    {
        bool Listed(string w) => Lexicon.Verbs.Contains(w) || Lexicon.Nouns.Contains(w) || Lexicon.Adjectives.Contains(w)
            || Lexicon.Particles.Contains(w) || Lexicon.FunctionWords.Contains(w)
            || grammar.Verbs.Contains(w) || grammar.Nouns.Contains(w) || grammar.Adjectives.Contains(w);
        return Listed(word) || grammar.Forms.ContainsValue(word) || Bases(word).Any(Listed);
    }

    /// <summary>
    /// The words <paramref name="word"/> may be a regular form of: plurals and third person (<c>-s</c>, <c>-es</c>,
    /// <c>-ies</c>), <c>-ed</c>, <c>-ing</c>, doers (<c>-er</c>, <c>-or</c>), <c>-est</c>, <c>-ly</c>, and <c>un-</c>/<c>re-</c>;
    /// a doubled final consonant is undone (<c>runner</c> → <c>run</c>).
    /// </summary>
    private static IEnumerable<string> Bases(string word)
    {
        foreach (var (ending, replacements) in Endings)
        {
            if (word.Length <= ending.Length + 1 || !word.EndsWith(ending, StringComparison.Ordinal)) continue;
            var bare = word[..^ending.Length];
            foreach (var r in replacements) yield return bare + r;
            if (bare.Length >= 3 && bare[^1] == bare[^2]) yield return bare[..^1];
            if (ending == "ers") foreach (var b in Bases(word[..^1])) yield return b;
        }
        foreach (var prefix in new[] { "un", "re" })
        {
            if (word.Length <= prefix.Length + 2 || !word.StartsWith(prefix, StringComparison.Ordinal)) continue;
            yield return word[prefix.Length..];
            foreach (var b in Bases(word[prefix.Length..])) yield return b;
        }
    }

    private static readonly (string Ending, string[] Replacements)[] Endings =
    [
        ("ies", ["y"]), ("es", ["", "e"]), ("s", [""]), ("ied", ["y"]), ("ed", ["", "e"]), ("ing", ["", "e"]),
        ("ier", ["y"]), ("er", ["", "e"]), ("ers", []), ("or", ["", "e"]), ("est", ["", "e"]), ("ly", [""]),
    ];

    /// <summary>The project dictionary from <c>package.byn</c> beside the package's sources; empty without one.</summary>
    private static Grammar GrammarOf(Compilation c) => Grammars.GetValue(c, _ =>
    {
        var path = Path.Combine(c.DisplayRoot, "package.byn");
        var grammar = File.Exists(path) ? Manifest.Read(path).Grammar : new Grammar();
        var lower = new Grammar();
        lower.Verbs.UnionWith(grammar.Verbs.Select(w => w.ToLowerInvariant()));
        lower.Nouns.UnionWith(grammar.Nouns.Select(w => w.ToLowerInvariant()));
        lower.Adjectives.UnionWith(grammar.Adjectives.Select(w => w.ToLowerInvariant()));
        foreach (var (word, form) in grammar.Forms) lower.Forms[word.ToLowerInvariant()] = form.ToLowerInvariant();
        return lower;
    });

    private static readonly ConditionalWeakTable<Compilation, Grammar> Grammars = new();

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
    private static List<string> Words(string name) => WordSpans(name).Select(w => w.Text).ToList();

    /// <summary><see cref="Words"/>, each with its offset in <paramref name="name"/>.</summary>
    private static List<(int Start, string Text)> WordSpans(string name)
    {
        var words = new List<(int, string)>();
        var current = new StringBuilder();
        var start = 0;
        void Flush()
        {
            if (current.Length > 0) words.Add((start, current.ToString()));
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
            if (current.Length == 0) start = i;
            current.Append(ch);
        }
        Flush();
        return words;
    }
}
