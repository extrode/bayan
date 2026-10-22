using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <summary>
/// Conformance: each struct's bases are checked once, before bodies, into <see cref="StructSymbol.Conformances"/>.
/// A default method is bound once on its interface (where <c>this</c> is <c>Self</c>, which has no fields) and
/// cloned onto each conformer that does not provide it, as an ordinary method of that struct.
/// </summary>
public sealed partial class Compilation
{
    /// <summary>The declaration of each interface getter: a <see cref="GetterDecl"/>, or a <see cref="PropertyDecl"/> for a default.</summary>
    private readonly Dictionary<FunctionSymbol, Decl> _interfaceGetters = new();

    /// <summary>The <c>type Item = T;</c> members of structs, resolved, by struct and name.</summary>
    private readonly Dictionary<(StructSymbol, string), TypeSymbol> _associatedBindings = new();

    private bool _conformancesChecked;

    private void CheckConformances()
    {
        foreach (var module in Modules)
            foreach (var i in module.Interfaces)
                module.AllFunctions.AddRange(i.Methods.Concat(i.Properties).Where(f => !f.IsAbstract));
        // Instances made while declaring are conformed here; later ones as they are made.
        var instances = _instances.Values.OfType<StructSymbol>().ToList();
        _conformancesChecked = true;
        foreach (var module in Modules)
            foreach (var s in module.Structs) Conform(s);
        foreach (var s in instances)
            Instantiating(s.File.Source, s.Decl.Name.Span, () => Conform(s));
        foreach (var check in _pendingConstraints) check();
        _pendingConstraints.Clear();
    }

    /// <summary>Witnesses, associated types and the <c>ref this</c> match for each base of <paramref name="s"/> (BYN2801–2803, 2814).</summary>
    private void Conform(StructSymbol s)
    {
        var source = s.File.Source;
        var cloned = new Dictionary<FunctionSymbol, InterfaceSymbol>();
        foreach (var i in s.Bases)
        {
            var c = new Conformance(s, i);
            var at = s.Decl.Bases.FirstOrDefault(b => b.Path[^1].Text == i.Name)?.Span ?? s.Decl.Name.Span;
            var map = i.TypeMap is null ? new Dictionary<string, TypeSymbol>(StringComparer.Ordinal) : new Dictionary<string, TypeSymbol>(i.TypeMap, StringComparer.Ordinal);
            foreach (var a in i.AssociatedTypes)
            {
                if (_associatedBindings.TryGetValue((s, a.Name), out var bound))
                {
                    c.AssociatedTypes[a.Name] = map[a.Name] = bound;
                    continue;
                }
                map[a.Name] = ErrorType.Instance;
                Report(DiagnosticCodes.MissingAssociatedType, source, at, [s.Name, a.Name, i.DisplayName], label: $"needs `type {a.Name}`",
                    help: $"add `type {a.Name} = …;` to `{s.Name}`");
            }
            foreach (var member in i.Methods.Concat(i.Properties).OrderBy(m => m.Location!.Value.Span.Start))
            {
                var expected = Expected(i, member, map);
                var own = s.FindProperty(member.Name)?.Getter ?? s.FindMethods(member.Name).FirstOrDefault();
                if (own is null)
                {
                    if (member.IsAbstract)
                        Report(DiagnosticCodes.MissingInterfaceMember, source, at, [s.Name, member.Name, i.DisplayName, SignatureText(expected)],
                            label: $"needs `{member.Name}`", help: $"add `{SignatureText(expected)}` to `{s.Name}`", fix: InsertMember(s, expected));
                    else
                        cloned[c.Witnesses[member] = CloneDefault(s, i, member, map, source, at)] = i;
                    continue;
                }
                c.Witnesses[member] = own;
                // Two bases defaulting one name: the first clone would silently win.
                if (!member.IsAbstract && cloned.TryGetValue(own, out var first))
                {
                    Report(DiagnosticCodes.InterfaceMemberMismatch, source, at, [s.Name, member.Name, $"{first.DisplayName}.{member.Name}", i.DisplayName, SignatureText(expected)],
                        label: $"`{member.Name}` also has a default in `{first.DisplayName}`", help: $"add `{SignatureText(expected)}` to `{s.Name}` to choose one");
                    continue;
                }
                var span = own.Location!.Value.Span;
                if (!SameShape(own, expected))
                    Report(DiagnosticCodes.InterfaceMemberMismatch, source, span, [s.Name, member.Name, SignatureText(own), i.DisplayName, SignatureText(expected)],
                        label: $"does not match `{i.DisplayName}`", help: $"change the signature to `{SignatureText(expected)}`");
                else if (own.IsMutating != expected.IsMutating)
                    Report(DiagnosticCodes.RefThisMismatch, source, span,
                        [s.Name, member.Name, i.DisplayName, expected.IsMutating ? "takes" : "does not take", expected.IsMutating ? "add" : "remove"],
                        label: own.IsMutating ? "declared `ref this`" : "not declared `ref this`",
                        help: $"{(expected.IsMutating ? "add" : "remove")} `ref this` on `{s.Name}.{member.Name}`", fix: RefThisFix(s, own));
            }
            s.Conformances.Add(c);
        }
    }

    /// <summary>The signature <paramref name="member"/> asks of a conformer: its declaration resolved with the interface's arguments and the conformer's associated types.</summary>
    private FunctionSymbol Expected(InterfaceSymbol i, FunctionSymbol member, Dictionary<string, TypeSymbol> map)
    {
        var fn = new FunctionSymbol(member.Name, member.Location, i.Module, member.Kind) { File = i.File, Access = member.Access, TypeMap = map };
        // The interface reported its own signatures already.
        Quietly(() =>
        {
            if (member.Decl is { } decl) DeclareSignature(fn, decl, i.File);
            else fn.ReturnType = new TypeResolver(this, i.File, null, map).Resolve(GetterType(member));
        });
        return fn;
    }

    private TypeSyntax GetterType(FunctionSymbol getter) => _interfaceGetters[getter] switch
    {
        GetterDecl g => g.Type,
        PropertyDecl p => p.Type,
        var d => throw new InvalidOperationException($"not an interface getter: {d}"),
    };

    /// <summary>
    /// The default <paramref name="member"/> as a method (or property) of <paramref name="s"/>, bound from the interface's
    /// syntax with <c>this</c> as <paramref name="s"/>; its body was checked on the interface, so binding it reports nothing new.
    /// </summary>
    private FunctionSymbol CloneDefault(StructSymbol s, InterfaceSymbol i, FunctionSymbol member, Dictionary<string, TypeSymbol> map, SourceText site, TextSpan span)
    {
        var template = i.Template is { } t ? t.FindProperty(member.Name) ?? t.FindMethods(member.Name).FirstOrDefault() ?? member : member;
        var fn = new FunctionSymbol(member.Name, member.Location, s.Module, member.Kind)
        {
            Owner = s, File = i.File, Decl = member.Decl, Access = member.Access, Template = template, TypeMap = map.Count == 0 ? null : map,
        };
        if (member.Decl is { } decl)
        {
            Quietly(() => DeclareSignature(fn, decl, i.File, s));
            s.Methods.Add(fn);
        }
        else
        {
            Quietly(() => fn.ReturnType = new TypeResolver(this, i.File, s, fn.TypeMap).Resolve(GetterType(member)));
            var p = (PropertyDecl)_interfaceGetters[member];
            s.Properties.Add(new PropertySymbol(fn.Name, fn.Location!.Value, s, fn.Access, p) { Type = fn.ReturnType, Getter = fn });
        }
        s.Module.AllFunctions.Add(fn);
        if (!s.IsGeneric) _instantiationWork.Enqueue((site, span, () => BindFunction(fn), null));
        return fn;
    }

    private void Quietly(Action work)
    {
        var outer = ReportInto(new DiagnosticBag());
        try { work(); }
        finally { ReportInto(outer); }
    }

    /// <summary>Same kind, parameters, return type and <c>fails</c>; an error type matches anything, so one mistake is reported once.</summary>
    private static bool SameShape(FunctionSymbol own, FunctionSymbol expected) =>
        own.Kind == expected.Kind && !own.IsStatic && Same(own.ReturnType, expected.ReturnType)
        && own.Parameters.Count == expected.Parameters.Count
        && own.Parameters.Zip(expected.Parameters).All(p => p.First.Modifier == p.Second.Modifier && Same(p.First.Type, p.Second.Type))
        && own.Fails == expected.Fails
        && (own.InfersErrors || expected.InfersErrors || own.ErrorSets.ToHashSet().SetEquals(expected.ErrorSets));

    private static bool Same(TypeSymbol a, TypeSymbol b) => a.IsError || b.IsError || a.Equals(b);

    /// <summary><c>f32 area()</c>, <c>i64 next(ref this)</c>, <c>i64 count { get; }</c>.</summary>
    private static string SignatureText(FunctionSymbol f)
    {
        if (f.Kind == FunctionKind.Getter) return $"{f.ReturnType.DisplayName} {f.Name} {{ get; }}";
        var parameters = f.Parameters.Select(p => (p.Modifier == ParamModifier.None ? "" : p.Modifier.ToString().ToLowerInvariant() + " ") + p.Type.DisplayName + " " + p.Name);
        if (f.IsMutating) parameters = parameters.Prepend("ref this");
        var fails = !f.Fails ? "" : f.ErrorSets.Count == 0 ? " fails" : " fails " + string.Join(", ", f.ErrorSets.Select(e => e.DisplayName));
        return $"{f.ReturnType.DisplayName} {f.Name}({string.Join(", ", parameters)}){fails}";
    }

    /// <summary>Inserts the missing member before the struct's closing brace, with a <c>todo</c> body.</summary>
    private static Fix InsertMember(StructSymbol s, FunctionSymbol expected)
    {
        var text = expected.Kind == FunctionKind.Getter
            ? $"{expected.ReturnType.DisplayName} {expected.Name} => todo(\"{expected.Name}\");"
            : $"{SignatureText(expected)} => todo(\"{expected.Name}\");";
        var source = s.File.Source;
        var brace = s.Decl.Span.End - 1;
        var lineStart = source.GetLineSpan(source.GetLineIndex(brace)).Start;
        return string.IsNullOrWhiteSpace(source.GetText(TextSpan.FromBounds(lineStart, brace)))
            ? new Fix(new TextSpan(lineStart, 0), $"    {text}\n")
            : new Fix(new TextSpan(brace, 0), text + " ");
    }

    /// <summary>Adds or removes <c>ref this</c> on the witness, when it is declared in the struct itself.</summary>
    private static Fix? RefThisFix(StructSymbol s, FunctionSymbol own)
    {
        if (own.File != s.File || own.Decl is not { } decl) return null;
        if (decl.RefThis is not { } refThis)
            return new Fix(new TextSpan(decl.ParamsStart, 0), decl.Params.Length > 0 ? "ref this, " : "ref this");
        return new Fix(decl.Params.Length > 0 ? TextSpan.FromBounds(refThis.Start, decl.Params[0].Span.Start) : refThis, "");
    }
}
