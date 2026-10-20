using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Semantics;

/// <summary>Properties of types that several passes share.</summary>
public static class TypeFacts
{
    /// <summary>
    /// A unique type owns storage, so a value of it has one owner: it is moved or copied
    /// explicitly, never implicitly (spec §3.11). <c>List</c>, <c>Map</c>, <c>StrBuf</c>,
    /// <c>Owned</c>, a <c>unique struct</c>, a type parameter in a generic body, and anything
    /// holding one of these by value.
    /// </summary>
    public static bool IsUnique(TypeSymbol t) => UniquePart(t) is not null;

    /// <summary>The innermost unique type <paramref name="t"/> holds by value (<paramref name="t"/> itself for a <c>List</c>), or null.</summary>
    public static TypeSymbol? UniquePart(TypeSymbol t) => Find(t, [])?.Part;

    /// <summary>Why <paramref name="t"/> is unique, as a sentence naming the type; null when it is not.</summary>
    public static string? WhyUnique(TypeSymbol t) => Find(t, [])?.Why;

    private static (TypeSymbol Part, string Why)? Find(TypeSymbol t, HashSet<TypeSymbol> seen)
    {
        switch (t)
        {
            case ListType or MapType or OwnedType or BuiltinType { Kind: BuiltinKind.StrBuf }:
                return (t, $"`{t.DisplayName}` is unique: it owns its storage");
            case TypeParameterType:
                return (t, $"type parameter `{t.DisplayName}` may stand for a unique type");
            case UntrustedType u:
                return Held(t, Find(u.Inner, seen));
            case OptionalType o:
                return Held(t, Find(o.Inner, seen));
            case ResultType r:
                return Held(t, Find(r.Value, seen));
            case ArrayType a:
                return Held(t, Find(a.Element, seen));
            case TupleType tt:
                foreach (var f in tt.Fields)
                    if (Find(f.Type, seen) is { } found) return Held(t, found);
                return null;
            case StructSymbol s:
                if (s.Decl.Modifiers.HasFlag(Modifiers.Unique)) return (t, $"`{s.DisplayName}` is declared `unique struct`");
                if (!seen.Add(s)) return null;
                foreach (var f in s.Fields)
                    if (Find(f.Type, seen) is { } found)
                        return (found.Part, $"`{s.DisplayName}` is unique because field `{f.Name}` is a `{f.Type.DisplayName}`");
                return null;
            case UnionSymbol u:
                if (!seen.Add(u)) return null;
                foreach (var v in u.Variants)
                    foreach (var f in v.Fields)
                        if (Find(f.Type, seen) is { } found)
                            return (found.Part, $"`{u.DisplayName}` is unique because variant `{v.Name}` holds a `{f.Type.DisplayName}`");
                return null;
            default:
                return null;
        }
    }

    private static (TypeSymbol Part, string Why)? Held(TypeSymbol outer, (TypeSymbol Part, string Why)? inner) =>
        inner is { } i ? (i.Part, $"`{outer.DisplayName}` is unique because it holds a `{i.Part.DisplayName}`") : null;
}
