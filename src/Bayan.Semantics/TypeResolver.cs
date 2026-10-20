using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics;

/// <summary>Turns type syntax into types, in the scope of one file (and optionally one struct).</summary>
internal sealed class TypeResolver(Compilation compilation, FileContext file, StructSymbol? container)
{
    public TypeSymbol Resolve(TypeSyntax syntax, bool allowVoid = false)
    {
        var t = ResolveCore(syntax);
        if (t.IsVoid && !allowVoid)
        {
            compilation.Report(DiagnosticCodes.VoidValue, file.Source, syntax.Span, []);
            return ErrorType.Instance;
        }
        return t;
    }

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
                var size = new Binder(compilation, file, null, container).BindArraySize(a.Size);
                return element.IsError || size < 0 ? ErrorType.Instance : new Symbols.ArrayType(element, size);
            }
            case Syntax.Ast.TupleType t:
            {
                var fields = t.Elements.Select(e => new TupleField(e.Name?.Text, Resolve(e.Type))).ToList();
                return fields.Any(f => f.Type.IsError) ? ErrorType.Instance : new Symbols.TupleType(fields);
            }
            case NamedType n:
                return ResolveNamed(n);
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
            if (PrimitiveType.ByName.TryGetValue(text, out var p))
            {
                NoTypeArgs(n);
                return p;
            }
            switch (text)
            {
                case "Span" or "List":
                {
                    if (n.Args.Length != 1)
                    {
                        compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [text, 1, n.Args.Length]);
                        return ErrorType.Instance;
                    }
                    var element = Resolve(n.Args[0]);
                    if (element.IsError) return element;
                    return text == "Span" ? new SpanType(element) : new ListType(element);
                }
                case "Arena":
                    NoTypeArgs(n);
                    return BuiltinType.Arena;
                case "StrBuf":
                    NoTypeArgs(n);
                    return BuiltinType.StrBuf;
                case "dec" or "decimal":
                    compilation.Report(DiagnosticCodes.Unsupported, file.Source, n.Span, ["the `dec` type"]);
                    return ErrorType.Instance;
            }
            if (Lookup(file.Module, text) is { } local)
            {
                NoTypeArgs(n);
                return Expand(local);
            }
            compilation.Report(DiagnosticCodes.UnknownType, file.Source, name.Span, [text],
                help: Suggest(text) is { } s ? $"did you mean `{s}`?" : null);
            return ErrorType.Instance;
        }
        if (n.Path.Length == 2 && file.Imports.TryGetValue(n.Path[0].Text, out var module))
        {
            if (Lookup(module, name.Text) is { } t)
            {
                NoTypeArgs(n);
                CheckVisible(t, name);
                return Expand(t);
            }
            compilation.Report(DiagnosticCodes.UnknownType, file.Source, name.Span, [string.Join(".", n.Path.Select(x => x.Text))]);
            return ErrorType.Instance;
        }
        compilation.Report(DiagnosticCodes.UnknownType, file.Source, n.Span, [string.Join(".", n.Path.Select(x => x.Text))],
            help: n.Path.Length == 2 ? $"`{n.Path[0].Text}` is not an imported module; add `import {n.Path[0].Text};`" : null);
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
            AliasPlaceholder a => Compilation.AccessOf(a.Decl.Modifiers),
            _ => Accessibility.Public,
        };
        if (access != Accessibility.Public)
            compilation.Report(DiagnosticCodes.NotVisible, file.Source, name.Span, [name.Text],
                label: "not `public`", help: $"mark `{name.Text}` `public` to use it from another module");
    }

    /// <summary>Follows aliases to their targets.</summary>
    private TypeSymbol Expand(TypeSymbol t)
    {
        if (t is not AliasPlaceholder alias) return t;
        if (alias.Target is { } done) return done;
        if (alias.Resolving)
        {
            compilation.Report(DiagnosticCodes.CircularAlias, alias.File.Source, alias.Decl.Name.Span, [alias.Decl.Name.Text]);
            return alias.Target = ErrorType.Instance;
        }
        alias.Resolving = true;
        var target = new TypeResolver(compilation, alias.File, null).Resolve(alias.Decl.Target);
        alias.Resolving = false;
        return alias.Target ??= target;
    }

    private void NoTypeArgs(NamedType n)
    {
        if (n.Args.Length > 0)
            compilation.Report(DiagnosticCodes.WrongTypeArgumentCount, file.Source, n.Span, [n.Path[^1].Text, 0, n.Args.Length]);
    }

    private string? Suggest(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var name in file.Module.Types.Keys.Concat(["Span", "List", "Arena", "StrBuf"]))
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
