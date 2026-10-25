using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics;

/// <summary>Turns type syntax into types, in the scope of one file (and optionally one struct and the type parameters in scope).</summary>
internal sealed class TypeResolver(Compilation compilation, FileContext file, StructSymbol? container,
    IReadOnlyDictionary<string, TypeSymbol>? typeArgs = null)
{
    public TypeSymbol Resolve(TypeSyntax syntax, bool allowVoid = false)
    {
        var t = ResolveCore(syntax);
        if (t.IsVoid && !allowVoid)
        {
            compilation.Report(DiagnosticCodes.VoidValue, file.Source, syntax.Span, []);
            return ErrorType.Instance;
        }
        if (t is InterfaceSymbol i)
        {
            compilation.Report(DiagnosticCodes.InterfaceAsType, file.Source, syntax.Span, [i.DisplayName], label: "an interface",
                help: $"write `dyn ref {i.DisplayName}` or add a `where` constraint");
            return ErrorType.Instance;
        }
        return t;
    }

    /// <summary>A base or a <c>where</c> bound, where an interface is allowed.</summary>
    public TypeSymbol ResolveBound(NamedType syntax) => ResolveNamed(syntax);

    private TypeSymbol ResolveCore(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case Syntax.Ast.OptionalType o:
            {
                var inner = Resolve(o.Inner);
                if (compilation.FunctionTypeNotHere(inner, file.Source, o.Inner.Span, "the value of an optional")) return ErrorType.Instance;
                if (inner is Symbols.OptionalType)
                {
                    compilation.Report(DiagnosticCodes.NestedOptional, file.Source, syntax.Span, [inner.DisplayName]);
                    return inner;
                }
                return inner.IsError ? inner : new Symbols.OptionalType(inner);
            }
            case Syntax.Ast.ArrayType a:
            {
                var element = Resolve(a.Element);
                if (compilation.FunctionTypeNotHere(element, file.Source, a.Element.Span, "an array element")) return ErrorType.Instance;
                var size = new Binder(compilation, file, null, container, typeArgs).BindArraySize(a.Size);
                return element.IsError || size < 0 ? ErrorType.Instance : new Symbols.ArrayType(element, size);
            }
            case Syntax.Ast.TupleType t:
            {
                var fields = t.Elements.Select(e => new TupleField(e.Name?.Text, Resolve(e.Type))).ToList();
                var placed = fields.Zip(t.Elements).Count(p => compilation.FunctionTypeNotHere(p.First.Type, file.Source, p.Second.Type.Span, "a tuple element"));
                return placed > 0 || fields.Any(f => f.Type.IsError) ? ErrorType.Instance : new Symbols.TupleType(fields);
            }
            case FunctionTypeSyntax f:
            {
                var parameters = f.Params.Select(p => Resolve(p)).ToList();
                var result = Resolve(f.Return, allowVoid: true);
                var failed = false;
                var fails = new List<ErrorSetSymbol>();
                foreach (var e in f.Fails?.Errors ?? [])
                {
                    var t = Resolve(e);
                    if (t is ErrorSetSymbol or ErrorUnionType)
                    {
                        foreach (var set in t is ErrorUnionType u ? u.Sets : [(ErrorSetSymbol)t])
                            if (!fails.Contains(set)) fails.Add(set);
                    }
                    else
                    {
                        if (!t.IsError) compilation.Report(DiagnosticCodes.NotAnErrorSet, file.Source, e.Span, [t.DisplayName]);
                        failed = true;
                    }
                }
                var uses = f.Uses is null ? [] : Effects(f.Uses);
                return failed || result.IsError || parameters.Any(p => p.IsError) ? ErrorType.Instance : new FunctionType(parameters, result, fails, uses);
            }
            case NamedType n:
                return ResolveNamed(n);
            case DynType d:
                return ResolveDyn(d);
            case RefinedType r:
                return ResolveStates(Resolve(r.Inner), r.States);
            default:
                return ErrorType.Instance;
        }
    }

    private TypeSymbol ResolveNamed(NamedType n)
    {
        var name = n.Path[^1];
        if (n.Path.Length == 1)
        {
            var text = name.Text;
            if (typeArgs is not null && typeArgs.TryGetValue(text, out var parameter))
            {
                NoTypeArgs(n);
                return parameter;
            }
            if (PrimitiveType.ByName.TryGetValue(text, out var p))
            {
                NoTypeArgs(n);
                return p;
            }
            switch (text)
            {
                case "Span" or "ReadOnlySpan" or "List" or "Owned" or "Untrusted":
                {
                    if (n.Args.Length != 1)
                    {
                        compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [text, 1, n.Args.Length]);
                        return ErrorType.Instance;
                    }
                    var element = Resolve(n.Args[0]);
                    if (element.IsError) return element;
                    var position = text switch { "List" => "a list element", "Span" or "ReadOnlySpan" => "a span element", _ => "a type argument" };
                    if (compilation.FunctionTypeNotHere(element, file.Source, n.Args[0].Span, position)) return ErrorType.Instance;
                    return text switch
                    {
                        "Span" => new SpanType(element),
                        "ReadOnlySpan" => new SpanType(element, readOnly: true),
                        "Owned" => new OwnedType(element),
                        "Untrusted" => new UntrustedType(element),
                        _ => new ListType(element),
                    };
                }
                case "Result":
                {
                    if (n.Args.Length != 2)
                    {
                        compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [text, 2, n.Args.Length]);
                        return ErrorType.Instance;
                    }
                    var value = Resolve(n.Args[0]);
                    var error = Resolve(n.Args[1]);
                    if (value.IsError || error.IsError) return ErrorType.Instance;
                    if (compilation.FunctionTypeNotHere(value, file.Source, n.Args[0].Span, "a type argument")) return ErrorType.Instance;
                    if (error is not ErrorSetSymbol)
                    {
                        compilation.Report(DiagnosticCodes.NotAnErrorSet, file.Source, n.Args[1].Span, [error.DisplayName]);
                        return ErrorType.Instance;
                    }
                    if (value is PrimitiveType { Kind: PrimitiveKind.Void or PrimitiveKind.Never })
                    {
                        compilation.Report(DiagnosticCodes.Unsupported, file.Source, n.Args[0].Span, [$"`Result<{value.DisplayName}, …>`"]);
                        return ErrorType.Instance;
                    }
                    return new ResultType(value, error);
                }
                case "Map":
                {
                    if (n.Args.Length != 2)
                    {
                        compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [text, 2, n.Args.Length]);
                        return ErrorType.Instance;
                    }
                    var key = Resolve(n.Args[0]);
                    var value = Resolve(n.Args[1]);
                    if (key.IsError || value.IsError) return ErrorType.Instance;
                    if (compilation.FunctionTypeNotHere(key, file.Source, n.Args[0].Span, "a map key") |
                        compilation.FunctionTypeNotHere(value, file.Source, n.Args[1].Span, "a map value")) return ErrorType.Instance;
                    if (!MapType.IsValidKey(key) && key is not TypeParameterType)
                    {
                        compilation.Report(DiagnosticCodes.InvalidMapKey, file.Source, n.Args[0].Span, [key.DisplayName],
                            help: "use an integer type, `rune`, `bool`, `string` or a plain enum as the key");
                        return ErrorType.Instance;
                    }
                    return new MapType(key, value);
                }
                case "Arena":
                    NoTypeArgs(n);
                    return BuiltinType.Arena;
                case "StrBuf":
                    NoTypeArgs(n);
                    return BuiltinType.StrBuf;
                case "ProcessResult":
                    NoTypeArgs(n);
                    return BuiltinType.ProcessResult;
                case "Panic":
                    NoTypeArgs(n);
                    return BuiltinType.Panic;
                case "FormatSpec":
                    NoTypeArgs(n);
                    return BuiltinType.FormatSpec;
                case "Symbol":
                    NoTypeArgs(n);
                    return BuiltinType.Symbol;
                case "dec" or "decimal":
                    compilation.Report(DiagnosticCodes.Unsupported, file.Source, n.Span, ["the `dec` type"]);
                    return ErrorType.Instance;
            }
            if (Lookup(file.Module, text) is { } local)
                return Construct(n, Expand(local));
            if (text == "IoError")
            {
                NoTypeArgs(n);
                return compilation.Builtins.IoError;
            }
            if (text == "Utf8Error")
            {
                NoTypeArgs(n);
                return compilation.Builtins.Utf8Error;
            }
            if (text == "ParseError")
            {
                NoTypeArgs(n);
                return compilation.Builtins.ParseError;
            }
            // `ValidationError<T>`: a set per `T`, whose `invalid` carries the violations of a struct with `requires` (J23).
            // Without `T` there is no `T.Field`, so the bare name is BYN2023 (revision 0011).
            if (text == "ValidationError")
            {
                if (n.Args.Length != 1)
                {
                    compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [text, 1, n.Args.Length]);
                    return ErrorType.Instance;
                }
                return Resolve(n.Args[0]) is { IsError: false } arg ? compilation.ValidationErrorOf(arg) : ErrorType.Instance;
            }
            if (text == "Rule")
            {
                NoTypeArgs(n);
                return compilation.Builtins.Rule;
            }
            // `Violation<T>` only for a struct with `requires`; any other `T` falls through to BYN2002.
            if (text == "Violation" && n.Args.Length == 1 && Resolve(n.Args[0]) is StructSymbol { FieldEnum: not null } of && compilation.ValidationErrorOf(of) is var validation)
                return ((ListType)validation.Variants[0].Fields[0].Type).Element;
            if (compilation.Builtins.FindInterface(text) is { } builtin)
            {
                NoTypeArgs(n);
                return builtin;
            }
            compilation.Report(DiagnosticCodes.UnknownType, file.Source, name.Span, [text],
                help: Suggest(text) is { } s ? $"did you mean `{s}`?" : null);
            return ErrorType.Instance;
        }
        if (n.Path.Length == 2 && typeArgs is not null && typeArgs.TryGetValue(n.Path[0].Text, out var owner))
        {
            NoTypeArgs(n);
            return AssociatedType(n, owner);
        }
        if (n.Path is [var head, { Text: "Field" }] && Lookup(file.Module, head.Text) is StructSymbol { FieldEnum: { } fields })
        {
            NoTypeArgs(n);
            return fields;
        }
        // `Order.paid`: a camelCase last segment names a state of the struct; derived shapes are PascalCase (J56).
        if (n.Path.Length == 2 && char.IsLower(name.Text[0]) && Lookup(file.Module, n.Path[0].Text) is StructSymbol)
            return ResolveStates(ResolveNamed(n with { Path = [n.Path[0]] }), [name]);
        if (n.Path.Length == 2 && file.Imports.TryGetValue(n.Path[0].Text, out var module))
        {
            if (Lookup(module, name.Text) is { } t)
            {
                CheckVisible(t, name);
                return Construct(n, Expand(t));
            }
            compilation.Report(DiagnosticCodes.UnknownType, file.Source, name.Span, [string.Join(".", n.Path.Select(x => x.Text))]);
            return ErrorType.Instance;
        }
        compilation.Report(DiagnosticCodes.UnknownType, file.Source, n.Span, [string.Join(".", n.Path.Select(x => x.Text))],
            help: n.Path.Length == 2 ? $"add `import {n.Path[0].Text}`; `{n.Path[0].Text}` is not an imported module" : null);
        return ErrorType.Instance;
    }

    /// <summary>The state type <c>T.s</c> or <c>T is .a or .b</c> of <paramref name="owner"/>; a state it does not declare is BYN7007.</summary>
    private TypeSymbol ResolveStates(TypeSymbol owner, Name[] names)
    {
        if (owner.IsError) return owner;
        var states = owner is StructSymbol s ? compilation.StatesOf(s) : null;
        var found = new List<UnionVariantSymbol>();
        foreach (var n in names)
        {
            if (states?.Find(n.Text) is not { } state)
            {
                compilation.Report(DiagnosticCodes.UnknownState, file.Source, n.Span, [owner.DisplayName, n.Text],
                    help: $"check the states declared in `{owner.DisplayName}`");
                return ErrorType.Instance;
            }
            found.Add(state);
        }
        return new StateType((StructSymbol)owner, found);
    }

    /// <summary><c>dyn ref I</c> and <c>dyn ref readonly I</c>; <c>dyn I</c> and interfaces that are not dyn-safe are diagnostics.</summary>
    private TypeSymbol ResolveDyn(DynType d)
    {
        var inner = ResolveNamed(d.Inner);
        if (inner.IsError) return inner;
        if (inner is not InterfaceSymbol i)
        {
            compilation.Report(DiagnosticCodes.NotAnInterface, file.Source, d.Inner.Span, [inner.DisplayName], label: "after `dyn`",
                help: "name an `interface` after `dyn ref`");
            return ErrorType.Instance;
        }
        if (!d.IsRef)
        {
            compilation.Report(DiagnosticCodes.DynByValue, file.Source, d.Span, [i.DisplayName], label: "an owned dynamic value",
                help: $"write `dyn ref {i.DisplayName}`");
            return ErrorType.Instance;
        }
        // Checked on the declaration, so it holds before the interface's members are declared.
        foreach (var member in i.Decl.Members)
        {
            var (name, reason) = member switch
            {
                FunctionDecl { TypeParams.Length: > 0 } f => (f.Name.Text, "is generic"),
                AssociatedTypeDecl a => (a.Name.Text, "is an associated type"),
                _ => (null, null),
            };
            if (name is null) continue;
            compilation.Report(DiagnosticCodes.NotDynSafe, file.Source, d.Span, [i.DisplayName, name, reason], label: "used with `dyn` here",
                help: "use a `where` constraint instead");
            return ErrorType.Instance;
        }
        return new DynRefType(i, d.IsReadOnly);
    }

    /// <summary><c>T.Item</c>: an associated type of <c>T</c>'s constraints in a template, the conformer's binding in an instance.</summary>
    private TypeSymbol AssociatedType(NamedType n, TypeSymbol owner)
    {
        var name = n.Path[1].Text;
        switch (owner)
        {
            case TypeParameterType p when p.Constraints.Any(c => c.Decl.Members.Any(m => m is AssociatedTypeDecl a && a.Name.Text == name)):
                return compilation.AssociatedType(p, name);
            case StructSymbol s when compilation.AssociatedBinding(s, name) is { } bound:
                return bound;
        }
        compilation.Report(DiagnosticCodes.UnknownType, file.Source, n.Span, [$"{owner.DisplayName}.{name}"],
            help: owner is TypeParameterType ? $"add a `where {owner.DisplayName} : …` bound on an interface declaring `type {name}`" : null);
        return ErrorType.Instance;
    }

    private static TypeSymbol? Lookup(ModuleSymbol module, string name) =>
        module.Types.TryGetValue(name, out var t) ? t : null;

    private void CheckVisible(TypeSymbol t, Name name)
    {
        var access = t switch
        {
            StructSymbol s => s.Access,
            ErrorSetSymbol e => e.Access,
            EnumSymbol e => e.Access,
            UnionSymbol u => u.Access,
            InterfaceSymbol i => i.Access,
            AliasPlaceholder a => Compilation.AccessOf(a.Modifiers),
            _ => Accessibility.Public,
        };
        if (access != Accessibility.Public)
            compilation.Report(DiagnosticCodes.NotVisible, file.Source, name.Span, [name.Text],
                label: "not `public`", help: $"mark `{name.Text}` `public` to use it from another module");
    }

    /// <summary>Follows aliases to their targets.</summary>
    /// <summary>The type an alias or <c>union error</c> composition names, resolved once.</summary>
    internal TypeSymbol ExpandAlias(AliasPlaceholder alias) => Expand(alias);

    private TypeSymbol Expand(TypeSymbol t)
    {
        if (t is not AliasPlaceholder alias) return t;
        if (alias.Target is { } done) return done;
        if (alias.Resolving)
        {
            compilation.Report(DiagnosticCodes.CircularAlias, alias.File.Source, alias.Name.Span, [alias.Name.Text]);
            return alias.Target = ErrorType.Instance;
        }
        alias.Resolving = true;
        var resolver = new TypeResolver(compilation, alias.File, null);
        var target = alias.Decl is ErrorSetDecl s ? resolver.ErrorSet(s) : resolver.Resolve(((TypeAliasDecl)alias.Decl).Target);
        alias.Resolving = false;
        return alias.Target ??= target;
    }

    /// <summary>The sets named by <c>union error S = A, B except .c</c>, one set or an <see cref="ErrorUnionType"/> less its excluded cases.</summary>
    private TypeSymbol ErrorSet(ErrorSetDecl decl)
    {
        var sets = new List<ErrorSetSymbol>();
        var excluded = new List<ErrorVariantSymbol>();
        foreach (var member in decl.Members)
        {
            var t = Resolve(member);
            if (t is ErrorSetSymbol or ErrorUnionType)
            {
                // A case stays excluded only while every member that brings in its set excludes it.
                var dropped = t is ErrorUnionType { Excluded: var x } ? x : [];
                foreach (var set in t is ErrorUnionType u ? u.Sets : [(ErrorSetSymbol)t])
                {
                    if (sets.Contains(set))
                    {
                        excluded.RemoveAll(v => v.Set == set && !dropped.Contains(v));
                        continue;
                    }
                    sets.Add(set);
                    excluded.AddRange(dropped.Where(v => v.Set == set));
                }
            }
            else if (!t.IsError)
            {
                compilation.Report(DiagnosticCodes.NotAnErrorSet, file.Source, member.Span, [t.DisplayName]);
            }
        }
        if (sets.Count == 0) return ErrorType.Instance;
        foreach (var name in decl.Except)
        {
            var matches = sets.Select(s => s.Find(name.Text)).OfType<ErrorVariantSymbol>().ToList();
            if (matches.Count == 0)
                compilation.Report(DiagnosticCodes.UnknownVariant, file.Source, name.Span, [name.Text, ErrorUnionType.Of(sets).DisplayName]);
            foreach (var v in matches)
                if (!excluded.Contains(v)) excluded.Add(v);
        }
        if (sets.All(s => s.Variants.All(excluded.Contains)))
            compilation.Report(DiagnosticCodes.EmptyErrorSet, file.Source, decl.Name.Span, [decl.Name.Text]);
        return ErrorUnionType.Of(sets, excluded);
    }

    /// <summary>
    /// The effects a function type's <c>uses</c> names, the module's <c>effect</c> aliases expanded; BYN6003 for an
    /// unknown name (one inside an alias is reported by <see cref="EffectAnalysis"/>).
    /// </summary>
    private List<string> Effects(UsesClause uses)
    {
        var aliases = file.Module.Files.SelectMany(f => f.Tree.Root.Members.OfType<EffectDecl>()).ToList();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Name name, bool written)
        {
            if (EffectAnalysis.Primitives.Contains(name.Text)) result.Add(name.Text);
            else if (aliases.FirstOrDefault(a => a.Name.Text == name.Text) is { } alias)
            {
                if (seen.Add(name.Text))
                    foreach (var e in alias.Effects) Add(e, written: false);
            }
            else if (written) EffectAnalysis.ReportUnknown(compilation, file.Source, name);
        }
        foreach (var name in uses.Effects) Add(name, written: true);
        return result.Distinct().ToList();
    }

    /// <summary>Applies the type arguments of <paramref name="n"/> to a generic struct, union or interface (instantiating it), or checks there are none.</summary>
    private TypeSymbol Construct(NamedType n, TypeSymbol t)
    {
        var parameters = t switch
        {
            StructSymbol s => s.TypeParameters, UnionSymbol u => u.TypeParameters, InterfaceSymbol i => i.TypeParameters, _ => [],
        };
        if (parameters.Count == 0)
        {
            NoTypeArgs(n);
            return t;
        }
        if (n.Args.Length != parameters.Count)
        {
            compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [n.Path[^1].Text, parameters.Count, n.Args.Length]);
            return ErrorType.Instance;
        }
        var args = n.Args.Select(a => Resolve(a)).ToArray();
        if (args.Any(a => a.IsError)) return ErrorType.Instance;
        if (args.Zip(n.Args).Count(a => compilation.FunctionTypeNotHere(a.First, file.Source, a.Second.Span, "a type argument")) > 0) return ErrorType.Instance;
        if (args.SequenceEqual(parameters)) return t;
        return compilation.InstantiateType(t, args, file.Source, n.Span);
    }

    private void NoTypeArgs(NamedType n)
    {
        if (n.Args.Length > 0)
            compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [n.Path[^1].Text, 0, n.Args.Length]);
    }

    private string? Suggest(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var name in file.Module.Types.Keys.Concat(["Span", "ReadOnlySpan", "List", "Map", "Owned", "Untrusted", "Result", "Arena", "StrBuf", "IoError", "Utf8Error", "ParseError", "ValidationError", "Violation", "Rule", "ProcessResult", "Panic", "FormatSpec", "Symbol"]))
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase)) return name;
        if (PrimitiveType.ByName.ContainsKey(lower)) return lower;
        return lower switch
        {
            "boolean" => "bool",
            "str" or "String" => "string",
            _ => null,
        };
    }
}
