using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Emitted C for interfaces (spec 006 T9): vtables only for <c>dyn ref</c> conversions, and no allocation for them.</summary>
public sealed class DynRefEmitTests
{
    private static string Emit(SourceText source)
    {
        var c = Compilation.Create([source], Path.Combine(Golden.CasesRoot, "run"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return CliApp.EmitC(c, BuildMode.Safe);
    }

    private static string EmitGolden(string name) => Emit(Golden.Load(Path.Combine(Golden.CasesRoot, "run", name)));

    [Fact]
    public void ConstraintsEmitNoVtables()
    {
        Assert.DoesNotContain("byn_vt_", EmitGolden("interface-constraint-unique.byn"));
    }

    [Fact]
    public void OnlyAConformerConvertedToDynRefGetsAVtable()
    {
        var code = Emit(SourceText.FromString(Path.Combine(Golden.CasesRoot, "run", "main.byn"), """
            interface IShape {
                i64 area();
            }

            struct Circle : IShape {
                i64 r;

                i64 area() => 3 * r * r;
            }

            struct Square : IShape {
                i64 side;

                i64 area() => side * side;
            }

            void show(dyn ref readonly IShape s) => print($"{s.area()}");

            void main() {
                let c = Circle { r: 1 };
                let s = Square { side: 2 };
                show(c);
                print($"{s.area()}");
            }
            """));
        Assert.Contains("bynh_vtab_main__Circle_main__IShape", code);
        Assert.DoesNotContain("main__Square_main__IShape", code);
    }

    [Fact]
    public void DynRefDispatchDoesNotAllocate()
    {
        var code = EmitGolden("dyn-ref-dispatch.byn");
        Assert.Contains("byn_vt_", code);
        // `List.add` grows through `byn_list_reserve`; every allocation goes through a `byn_alloc`, by default `byn_context`.
        Assert.DoesNotContain("byn_list_reserve", code);
        Assert.DoesNotContain("byn_copy_bytes", code);
        Assert.DoesNotContain("byn_context", code);
        Assert.DoesNotContain("->resize(", code);
    }
}
