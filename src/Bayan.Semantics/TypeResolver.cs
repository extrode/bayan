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
                var size = new Binder(compilation, file, null, container, typeArgs).BindArraySize(a.Size);
                return element.IsError || size < 0 ? ErrorType.Instance : new Symbols.ArrayType(element, size);
            }
            case Syntax.Ast.TupleType t:
            {
                var fields = t.Elements.Select(e => new TupleField(e.Name?.Text, Resolve(e.Type))).ToList();
                return fields.Any(f => f.Type.IsError) ? ErrorType.Instance : new Symbols.TupleType(fields);
            }
            case NamedType n:
                return ResolveNamed(n);
            case DynType d:
                return ResolveDyn(d);
            case RefinedType r:
                // States are checked in 5.3c; until then a refinement is its inner type.
                return Resolve(r.Inner);
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
                    if (!MapType.IsValidKey(key) && key is not TypeParameterType)
                    {
                        compilation.Report(DiagnosticCodes.InvalidMapKey, file.Source, n.Args[0].Span, [key.DisplayName],
                            help: "use an integer type, `char`, `bool`, `string` or a plain enum as the key");
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

    /// <summary>The sets named by <c>union error S = A, B</c>, one set or an <see cref="ErrorUnionType"/>.</summary>
    private TypeSymbol ErrorSet(ErrorSetDecl decl)
    {
        var sets = new List<ErrorSetSymbol>();
        foreach (var member in decl.Members)
        {
            var t = Resolve(member);
            if (t is ErrorSetSymbol or ErrorUnionType)
            {
                foreach (var set in t is ErrorUnionType u ? u.Sets : [(ErrorSetSymbol)t])
                    if (!sets.Contains(set)) sets.Add(set);
            }
            else if (!t.IsError)
            {
                compilation.Report(DiagnosticCodes.NotAnErrorSet, file.Source, member.Span, [t.DisplayName]);
            }
        }
        return sets.Count == 0 ? ErrorType.Instance : ErrorUnionType.Of(sets);
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
        foreach (var name in file.Module.Types.Keys.Concat(["Span", "ReadOnlySpan", "List", "Map", "Owned", "Untrusted", "Result", "Arena", "StrBuf", "IoError", "Utf8Error", "ProcessResult", "Panic"]))
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
