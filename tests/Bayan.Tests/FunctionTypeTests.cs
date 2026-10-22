using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;

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
        Assert.Equal("i32 -> bool -> char", Fn([PrimitiveType.I32], Fn([PrimitiveType.Bool], PrimitiveType.Char)).DisplayName);
        Assert.Equal("(i32 -> bool) -> char", Fn([Fn([PrimitiveType.I32], PrimitiveType.Bool)], PrimitiveType.Char).DisplayName);
        Assert.Equal("((i32, i32)) -> bool", Fn([new TupleType([new(null, PrimitiveType.I32), new(null, PrimitiveType.I32)])], PrimitiveType.Bool).DisplayName);
        Assert.Equal("i32 -> (bool -> char) fails IoError", Fn([PrimitiveType.I32], Fn([PrimitiveType.Bool], PrimitiveType.Char), [B.IoError]).DisplayName);
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
