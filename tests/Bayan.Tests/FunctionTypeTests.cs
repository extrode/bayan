using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>The semantic model of function types and lambdas (spec 007 R1, R3, R8, R10, R16).</summary>
public sealed class FunctionTypeTests
{
    private static readonly Builtins B = new();

    private static FunctionType Fn(TypeSymbol[] parameters, TypeSymbol ret, ErrorSetSymbol[]? fails = null, string[]? uses = null) =>
        new(parameters, ret, fails ?? [], uses ?? []);

    [Fact]
    public void EqualByParametersReturnErrorSetsAndEffects()
    {
        var a = Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.Bool, [B.IoError, B.Utf8Error], ["net", "fs"]);
        var b = Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.Bool, [B.Utf8Error, B.IoError], ["fs", "net"]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, Fn([PrimitiveType.I64, PrimitiveType.String], PrimitiveType.Bool, [B.IoError, B.Utf8Error], ["net", "fs"]));
        Assert.NotEqual(a, Fn([PrimitiveType.I32], PrimitiveType.Bool, [B.IoError, B.Utf8Error], ["net", "fs"]));
        Assert.NotEqual(a, Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.I32, [B.IoError, B.Utf8Error], ["net", "fs"]));
        Assert.NotEqual(a, Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.Bool, [B.IoError], ["net", "fs"]));
        Assert.NotEqual(a, Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.Bool, [B.IoError, B.Utf8Error], ["net"]));
        Assert.NotEqual<TypeSymbol>(Fn([], PrimitiveType.Void), new TupleType([]));
    }

    [Fact]
    public void DisplaysInSourceFormWithArrow()
    {
        Assert.Equal("i32 -> bool", Fn([PrimitiveType.I32], PrimitiveType.Bool).DisplayName);
        Assert.Equal("(i32, string) -> i32", Fn([PrimitiveType.I32, PrimitiveType.String], PrimitiveType.I32).DisplayName);
        Assert.Equal("() -> void", Fn([], PrimitiveType.Void).DisplayName);
        Assert.Equal("() -> string fails IoError", Fn([], PrimitiveType.String, [B.IoError]).DisplayName);
        Assert.Equal("i32 -> void uses fs, net", Fn([PrimitiveType.I32], PrimitiveType.Void, uses: ["net", "fs"]).DisplayName);
        Assert.Equal("i32 -> bool -> rune", Fn([PrimitiveType.I32], Fn([PrimitiveType.Bool], PrimitiveType.Char)).DisplayName);
        Assert.Equal("(i32 -> bool) -> rune", Fn([Fn([PrimitiveType.I32], PrimitiveType.Bool)], PrimitiveType.Char).DisplayName);
        Assert.Equal("((i32, i32)) -> bool", Fn([new TupleType([new(null, PrimitiveType.I32), new(null, PrimitiveType.I32)])], PrimitiveType.Bool).DisplayName);
        Assert.Equal("i32 -> (bool -> rune) fails IoError", Fn([PrimitiveType.I32], Fn([PrimitiveType.Bool], PrimitiveType.Char), [B.IoError]).DisplayName);
    }

    private static TypeSymbol ParameterType(Compilation c, string function) => c.EntryModule.Functions[function][0].Parameters[0].Type;

    private static Compilation Compile(string text)
    {
        var dir = Path.Combine(Golden.CasesRoot, "typed");
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
        Assert.False(c.Diagnostics.HasErrors);
        return c;
    }

    [Fact]
    public void AnAliasNamedTwiceIsOneType()
    {
        var c = Compile("type Pred = i32 -> bool\n\nbool a(Pred p) => true\n\nbool b(Pred p) => true\n\nbool d((i32 -> bool) p) => true\n\nvoid main() {\n}\n");
        Assert.IsType<FunctionType>(ParameterType(c, "a"));
        Assert.Equal(ParameterType(c, "a"), ParameterType(c, "b"));
        Assert.Equal(ParameterType(c, "a"), ParameterType(c, "d"));
    }

    [Fact]
    public void EffectsMakeADifferentType()
    {
        var c = Compile("effect io = net\n\nbool a((i32 -> bool) p) => true\n\nbool b((i32 -> bool uses net) p) => true\n\nbool d((i32 -> bool uses io) p) => true\n\nvoid main() {\n}\n");
        Assert.NotEqual(ParameterType(c, "a"), ParameterType(c, "b"));
        Assert.Equal(ParameterType(c, "b"), ParameterType(c, "d"));
    }

    [Fact]
    public void IsABorrow() => Assert.True(Fn([PrimitiveType.I32], PrimitiveType.Bool).IsBorrow);

    [Fact]
    public void OccursFindsATypeParameterInside()
    {
        var t = new TypeParameterType("T");
        Assert.True(TypeParameterType.Occurs(Fn([t], PrimitiveType.Bool)));
        Assert.True(TypeParameterType.Occurs(Fn([PrimitiveType.I32], new ListType(t))));
        Assert.True(TypeParameterType.Occurs(new OptionalType(Fn([], t))));
        Assert.False(TypeParameterType.Occurs(Fn([PrimitiveType.I32], PrimitiveType.Bool)));
    }

    private static IEnumerable<BoundNode> BodyOf(Compilation c, string function) => BoundTree.Descendants(c.EntryModule.Functions[function][0].Body!);

    [Fact]
    public void AModuleFunctionOfTheExactTypeIsAFunctionRef()
    {
        var c = Compile("bool isBig(i64 x) => x > 2\n\nbool keep(i64 x, by (i64 -> bool) p) => p(x)\n\nvoid main() {\n    let a = keep(3, by: isBig)\n}\n");
        var reference = Assert.Single(BodyOf(c, "main").OfType<BoundFunctionRef>());
        Assert.Equal("isBig", reference.Function.Name);
        Assert.Equal("i64 -> bool", reference.Type.DisplayName);
    }

    [Fact]
    public void AFunctionWithAParticleIsAValueWithoutIt()
    {
        var c = Compile("i64 clamp(i64 x, to i64 max) => max\n\nvoid main() {\n    let f: (i64, i64) -> i64 = clamp\n}\n");
        var reference = Assert.Single(BodyOf(c, "main").OfType<BoundFunctionRef>());
        Assert.Equal("clamp", reference.Function.Name);
        Assert.Equal("(i64, i64) -> i64", reference.Type.DisplayName);
    }

    [Fact]
    public void ACallThroughAValueCallsTheInvokeSymbol()
    {
        var c = Compile("i64 apply(i64 x, by ((i64, i64) -> i64) f) => f(x, 1)\n\nvoid main() {\n}\n");
        var call = Assert.Single(BodyOf(c, "apply").OfType<BoundCall>());
        Assert.Equal(FunctionKind.Invoke, call.Function.Kind);
        Assert.Equal(PrimitiveType.I64, call.Type);
        Assert.Equal("f", Assert.IsType<BoundLocal>(call.Receiver).Local.Name);
        Assert.Equal(2, call.Arguments.Length);
        Assert.Equal(["_0", "_1"], call.Function.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void ACallThroughAFunctionTypedLocal()
    {
        var c = Compile("bool isBig(i64 x) => x > 2\n\nvoid main() {\n    let f: i64 -> bool = isBig\n    let b = f(3)\n}\n");
        var call = Assert.Single(BodyOf(c, "main").OfType<BoundCall>());
        Assert.Equal(FunctionKind.Invoke, call.Function.Kind);
        Assert.Equal(PrimitiveType.Bool, call.Type);
    }

    [Fact]
    public void AFunctionValueArgumentInfersTheTypeArgument()
    {
        var c = Compile("bool isBig(i64 x) => x > 2\n\nbool holds<T>((T -> bool) p) => true\n\nvoid main() {\n    let f: i64 -> bool = isBig\n    let a = holds(isBig)\n    let b = holds(f)\n}\n");
        var calls = BodyOf(c, "main").OfType<BoundCall>().Where(x => x.Function.Name == "holds").ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, x => Assert.Equal<TypeSymbol>([PrimitiveType.I64], x.Function.TypeArguments));
    }

    [Fact]
    public void ChildrenOfALambdaAreItsCaptures()
    {
        var module = new ModuleSymbol("main", "");
        var type = Fn([PrimitiveType.I32], PrimitiveType.Bool);
        var limit = new LocalSymbol("limit", null, PrimitiveType.I32, LocalKind.Let);
        var name = new LocalSymbol("name", null, PrimitiveType.String, LocalKind.Parameter);
        var captures = new[] { new BoundLocal(null!, limit), new BoundLocal(null!, name) };
        var lambda = new BoundLambda(null!, new FunctionSymbol("lambda1", null, module, FunctionKind.Lambda), captures, type);
        Assert.Equal<BoundNode>(captures, BoundTree.Children(lambda));

        var reference = new BoundFunctionRef(null!, new FunctionSymbol("isBig", null, module, FunctionKind.Function), type);
        Assert.Empty(BoundTree.Children(reference));
    }
}
