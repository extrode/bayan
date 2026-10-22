using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Interface use (spec 006 T6): constraint members, constraint checks and <c>dyn ref</c> conversions.</summary>
public sealed class InterfaceUseTests
{
    private static Compilation Compile(string text) =>
        Compilation.Create([SourceText.FromString("main.byn", text)], Path.GetTempPath(), new CompilationOptions(Library: true));

    private const string Shapes = """
        interface IShape {
            i64 area();
            string describe() => $"area {area()}";
        }

        struct Circle : IShape {
            i64 r;

            i64 area() => 3 * r * r;
        }

        struct Square {
            i64 side;
        }

        """;

    [Fact]
    public void ConformingValuesConvertToDynRef()
    {
        var c = Compile(Shapes + """
            void show(dyn ref IShape s) => print(s.describe());

            void peek(dyn ref readonly IShape s) => print($"{s.area()}");

            dyn ref IShape pick(ref Circle c) => c;

            void forward<T>(ref T shape) where T : IShape {
                show(shape);
                dyn ref readonly IShape r = shape;
                peek(r);
            }

            void run() {
                var c = Circle { r = 2 };
                show(c);
                dyn ref IShape d = c;
                peek(d);
                peek(pick(ref c));
                forward(ref c);
            }
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var module = c.Modules[0];
        var shape = Assert.Single(module.Interfaces);
        var circle = module.Structs.Single(s => s.Name == "Circle");
        var square = module.Structs.Single(s => s.Name == "Square");
        Assert.Equal(ConversionKind.ToDynRef, Conversions.Implicit(circle, new DynRefType(shape)));
        Assert.Equal(ConversionKind.ToDynRef, Conversions.Implicit(circle, new DynRefType(shape, readOnly: true)));
        Assert.Equal(ConversionKind.Identity, Conversions.Implicit(new DynRefType(shape), new DynRefType(shape, readOnly: true)));
        Assert.Null(Conversions.Implicit(new DynRefType(shape, readOnly: true), new DynRefType(shape)));
        Assert.Null(Conversions.Implicit(square, new DynRefType(shape)));
        Assert.Null(Conversions.Implicit(PrimitiveType.I64, new DynRefType(shape)));
    }

    [Fact]
    public void AnUnsatisfiedConstraintInsideAGenericIsReportedOnceAtTheOuterCall()
    {
        var c = Compile(Shapes + """
            i64 measure<T>(ref T shape) where T : IShape => shape.area();

            i64 twice<T>(ref T shape) where T : IShape => measure(ref shape) * 2;

            void run() {
                var s = Square { side = 2 };
                print($"{twice(ref s)}");
            }
            """);
        var error = Assert.Single(c.Diagnostics.Items);
        Assert.Equal("BYN2807", error.Code);
        Assert.Equal("`Square` does not conform to `IShape`: missing `area`", error.Message);
    }
}
