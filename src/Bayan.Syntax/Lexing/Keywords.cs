using System.Text;

namespace Bayan.Syntax.Lexing;

public static class Keywords
{
    private static readonly (string Text, TokenKind Kind)[] All =
    [
        ("struct", TokenKind.StructKeyword), ("unique", TokenKind.UniqueKeyword), ("union", TokenKind.UnionKeyword),
        ("enum", TokenKind.EnumKeyword), ("flags", TokenKind.FlagsKeyword), ("open", TokenKind.OpenKeyword),
        ("extensible", TokenKind.ExtensibleKeyword), ("interface", TokenKind.InterfaceKeyword),
        ("delegate", TokenKind.DelegateKeyword), ("extension", TokenKind.ExtensionKeyword),
        ("derivation", TokenKind.DerivationKeyword), ("attribute", TokenKind.AttributeKeyword),
        ("error", TokenKind.ErrorKeyword), ("set", TokenKind.SetKeyword), ("unit", TokenKind.UnitKeyword),
        ("distinct", TokenKind.DistinctKeyword), ("type", TokenKind.TypeKeyword), ("import", TokenKind.ImportKeyword),
        ("as", TokenKind.AsKeyword), ("public", TokenKind.PublicKeyword), ("private", TokenKind.PrivateKeyword),
        ("static", TokenKind.StaticKeyword), ("readonly", TokenKind.ReadonlyKeyword),
        ("required", TokenKind.RequiredKeyword), ("const", TokenKind.ConstKeyword), ("let", TokenKind.LetKeyword),
        ("var", TokenKind.VarKeyword), ("embed", TokenKind.EmbedKeyword), ("extern", TokenKind.ExternKeyword),
        ("unsafe", TokenKind.UnsafeKeyword), ("if", TokenKind.IfKeyword), ("else", TokenKind.ElseKeyword),
        ("unless", TokenKind.UnlessKeyword), ("switch", TokenKind.SwitchKeyword), ("case", TokenKind.CaseKeyword),
        ("default", TokenKind.DefaultKeyword), ("for", TokenKind.ForKeyword), ("in", TokenKind.InKeyword),
        ("while", TokenKind.WhileKeyword), ("do", TokenKind.DoKeyword), ("break", TokenKind.BreakKeyword),
        ("continue", TokenKind.ContinueKeyword), ("return", TokenKind.ReturnKeyword), ("yield", TokenKind.YieldKeyword),
        ("defer", TokenKind.DeferKeyword), ("errdefer", TokenKind.ErrdeferKeyword), ("using", TokenKind.UsingKeyword),
        ("context", TokenKind.ContextKeyword), ("fails", TokenKind.FailsKeyword), ("try", TokenKind.TryKeyword),
        ("catch", TokenKind.CatchKeyword), ("never", TokenKind.NeverKeyword), ("noalloc", TokenKind.NoallocKeyword),
        ("nosuspend", TokenKind.NosuspendKeyword), ("suspends", TokenKind.SuspendsKeyword),
        ("async", TokenKind.AsyncKeyword), ("await", TokenKind.AwaitKeyword), ("is", TokenKind.IsKeyword),
        ("and", TokenKind.AndKeyword), ("or", TokenKind.OrKeyword), ("not", TokenKind.NotKeyword),
        ("with", TokenKind.WithKeyword), ("move", TokenKind.MoveKeyword), ("ref", TokenKind.RefKeyword),
        ("out", TokenKind.OutKeyword), ("this", TokenKind.ThisKeyword), ("null", TokenKind.NullKeyword),
        ("true", TokenKind.TrueKeyword), ("false", TokenKind.FalseKeyword), ("comptime", TokenKind.ComptimeKeyword),
        ("checked", TokenKind.CheckedKeyword), ("test", TokenKind.TestKeyword), ("assert", TokenKind.AssertKeyword),
        ("property", TokenKind.PropertyKeyword),
        ("fuzz", TokenKind.FuzzKeyword), ("model", TokenKind.ModelKeyword), ("foreach", TokenKind.ForeachKeyword),
        ("new", TokenKind.NewKeyword), ("goto", TokenKind.GotoKeyword), ("actor", TokenKind.ActorKeyword),
        ("effect", TokenKind.EffectKeyword), ("macro", TokenKind.MacroKeyword), ("select", TokenKind.SelectKeyword),
        ("become", TokenKind.BecomeKeyword), ("fail", TokenKind.FailKeyword), ("law", TokenKind.LawKeyword),
        ("requires", TokenKind.RequiresKeyword), ("because", TokenKind.BecauseKeyword), ("uses", TokenKind.UsesKeyword),
        ("except", TokenKind.ExceptKeyword), ("provide", TokenKind.ProvideKeyword),
    ];

    // Bucketed by first letter so lookup compares a handful of byte strings and allocates nothing.
    private static readonly (byte[] Utf8, TokenKind Kind)[][] ByFirstByte = Build();

    private static readonly string[] TextByKind = BuildText();

    public static IEnumerable<(string Text, TokenKind Kind)> List => All;

    // SPEC §2.4: reserved in every project so C# muscle memory cannot declare a name that shadows a sized type.
    private static readonly HashSet<string> CSharpPrimitives =
        ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double"];

    /// <summary>True for a C# primitive type name, which may not be declared as an identifier.</summary>
    public static bool IsCSharpPrimitive(string text) => CSharpPrimitives.Contains(text);

    public static string TextOf(TokenKind kind) => TextByKind[(int)kind - (int)TokenKindFacts.KeywordFirst];

    /// <summary>The keyword kind for an ASCII identifier, or <see cref="TokenKind.Identifier"/>.</summary>
    public static TokenKind Lookup(ReadOnlySpan<byte> text)
    {
        if (text.Length < 2 || text[0] is < (byte)'a' or > (byte)'z') return TokenKind.Identifier;
        foreach (var (utf8, kind) in ByFirstByte[text[0] - 'a'])
        {
            if (text.SequenceEqual(utf8)) return kind;
        }
        return TokenKind.Identifier;
    }

    private static (byte[], TokenKind)[][] Build()
    {
        var buckets = new List<(byte[], TokenKind)>[26];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = new();
        foreach (var (text, kind) in All) buckets[text[0] - 'a'].Add((Encoding.ASCII.GetBytes(text), kind));
        return buckets.Select(b => b.ToArray()).ToArray();
    }

    private static string[] BuildText()
    {
        var result = new string[TokenKindFacts.KeywordLast - TokenKindFacts.KeywordFirst + 1];
        foreach (var (text, kind) in All) result[kind - TokenKindFacts.KeywordFirst] = text;
        return result;
    }
}
