using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Interface declarations (spec 006 T3): built-in iteration interfaces, bases and <c>where</c> constraints.</summary>
public sealed class InterfaceDeclarationTests
{
    private static Compilation Compile(string text) =>
        Compilation.Create([SourceText.FromString("main.byn", text)], Path.GetTempPath(), new CompilationOptions(Library: true));

    [Fact]
    public void TheIterationInterfacesAreBuiltIn()
    {
        var c = Compile("");
        var enumerator = c.Builtins.IEnumerator;
        Assert.Equal(["Item"], enumerator.AssociatedTypes.Select(t => t.Name));
        var moveNext = Assert.Single(enumerator.Methods);
        Assert.True(moveNext is { Name: "moveNext", IsAbstract: true, IsMutating: true });
        Assert.Same(PrimitiveType.Bool, moveNext.ReturnType);
        var current = Assert.Single(enumerator.Properties);
        Assert.Same(enumerator.AssociatedTypes[0], current.ReturnType);

        var indexable = c.Builtins.IIndexable;
        var get = Assert.Single(indexable.Methods);
        Assert.True(get is { Name: "get", IsAbstract: true, IsMutating: false });
        Assert.Same(indexable.AssociatedTypes[0], get.ReturnType);
        Assert.Same(PrimitiveType.I64, Assert.Single(get.Parameters).Type);
        Assert.Same(PrimitiveType.I64, Assert.Single(indexable.Properties, p => p.Name == "count").ReturnType);
    }

    [Fact]
    public void BasesAndBoundsResolveToInterfaces()
    {
        var c = Compile("""
            interface IShape {
                i64 area()
                string describe() => "shape"
            }

            struct Tokens : IShape {
                i64 n

                i64 area() => n
            }

            void show<T>(ref T shape) where T : IShape, IEnumerator { }
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var module = c.Modules[0];
        var shape = Assert.Single(module.Interfaces);
        Assert.True(shape.Methods.Single(m => m.Name == "describe") is { IsAbstract: false, Interface: not null, Owner: null });
        Assert.Equal([shape], module.Structs.Single().Bases);
        var t = Assert.Single(module.AllFunctions.Single(f => f.Name == "show").TypeParameters);
        Assert.Equal([shape, c.Builtins.IEnumerator], t.Constraints);
    }

    [Fact]
    public void DynRefResolvesToABorrowOfTheInterface()
    {
        var c = Compile("""
            interface IShape {
                i64 area()
            }

            void show(dyn ref IShape s, dyn ref readonly IShape r) {
                let t: dyn ref readonly IShape = r
            }
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var shape = Assert.Single(c.Modules[0].Interfaces);
        var show = c.Modules[0].AllFunctions.Single(f => f.Name == "show");
        Assert.Equal([new DynRefType(shape), new DynRefType(shape, readOnly: true)], show.Parameters.Select(p => p.Type));
        Assert.True(show.Parameters[0].Type.IsBorrow);
    }

    [Fact]
    public void BoundsResolveThroughInterfaceInstancesAndAssociatedTypes()
    {
        var c = Compile("""
            interface IOrder<T> {
                bool less(T other)
            }

            T max<T>(move T a, T b) where T : IOrder<T> => a

            T.Item first<T>(ref T e, move T.Item fallback) where T : IEnumerator => fallback
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var order = Assert.Single(c.Modules[0].Interfaces);
        var t = Assert.Single(c.Modules[0].AllFunctions.Single(f => f.Name == "max").TypeParameters);
        var bound = Assert.Single(t.Constraints);
        Assert.Same(order, bound.Template);
        Assert.Equal([t], bound.TypeArguments);
        Assert.Same(t, Assert.Single(bound.Methods.Single().Parameters).Type);

        var first = c.Modules[0].AllFunctions.Single(f => f.Name == "first");
        Assert.Equal("T.Item", first.ReturnType.DisplayName);
        Assert.Same(first.ReturnType, first.Parameters[1].Type);
    }
}
