using Bayan.Semantics.Symbols;

namespace Bayan.Semantics;

/// <summary>Functions every module sees without an import.</summary>
public sealed class Builtins
{
    public Builtins()
    {
        Module = new ModuleSymbol("builtin", "") { CName = "builtin" };
        Print = Make("print", BuiltinFunction.Print, PrimitiveType.Void, ("text", PrimitiveType.String));
        Panic = Make("panic", BuiltinFunction.Panic, PrimitiveType.Never, ("message", PrimitiveType.String));
        Assert = Make("assert", BuiltinFunction.Assert, PrimitiveType.Void, ("condition", PrimitiveType.Bool));
        ArenaNew = Make("Arena", BuiltinFunction.ArenaNew, BuiltinType.Arena);
        StrBufNew = Make("StrBuf", BuiltinFunction.StrBufNew, BuiltinType.StrBuf, ("buffer", new SpanType(PrimitiveType.U8)));
    }

    /// <summary><c>Arena()</c>: a new region allocator.</summary>
    public FunctionSymbol ArenaNew { get; }

    /// <summary><c>StrBuf(Span&lt;u8&gt; buffer)</c>: text output into caller-provided bytes.</summary>
    public FunctionSymbol StrBufNew { get; }

    public ModuleSymbol Module { get; }

    /// <summary><c>print(string)</c>: writes the text and a newline to standard output.</summary>
    public FunctionSymbol Print { get; }

    /// <summary><c>panic(string)</c>: aborts with a message and location.</summary>
    public FunctionSymbol Panic { get; }

    /// <summary><c>assert(bool)</c>: panics with the condition's source text when false.</summary>
    public FunctionSymbol Assert { get; }

    public FunctionSymbol? Find(string name) => name switch
    {
        "print" => Print,
        "panic" => Panic,
        "assert" => Assert,
        _ => null,
    };

    private FunctionSymbol Make(string name, BuiltinFunction kind, TypeSymbol returns, params (string Name, TypeSymbol Type)[] parameters)
    {
        var fn = new FunctionSymbol(name, null, Module, FunctionKind.Builtin)
        {
            Builtin = kind, ReturnType = returns, Access = Accessibility.Public,
        };
        foreach (var (n, t) in parameters)
            fn.Parameters.Add(new ParameterSymbol(n, null, t, Syntax.Ast.ParamModifier.None, fn.Parameters.Count));
        return fn;
    }
}
