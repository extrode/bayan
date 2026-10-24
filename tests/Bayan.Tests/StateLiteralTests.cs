using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>A state literal <c>T.s { … }</c> runs its struct's <c>requires</c> checks like a plain literal (spec §3.17, J37).</summary>
public sealed class StateLiteralTests
{
    private const string Source = """
        struct Plain {
            i64 id requires it > 0
        }

        struct Order {
            i64 id requires it > 0
            states {
                draft
                paid(i64 payment)
            }
        }

        Plain plain(i64 n) fails ValidationError => try Plain { id: n }

        Order.draft draft(i64 n) fails ValidationError => try Order.draft { id: n }

        """;

    private static BoundCall Check(Compilation c, string function)
    {
        var fn = c.Modules[0].AllFunctions.Single(f => f.Name == function);
        return BoundTree.Descendants(fn.Body!).OfType<BoundCall>().Single(call => call.Receiver is BoundStructLiteral);
    }

    [Fact]
    public void ANonConstantStateLiteralCarriesTheRuntimeCheck()
    {
        var c = Compilation.Create([SourceText.FromString("main.byn", Source)], Path.GetTempPath(), new CompilationOptions(Library: true));
        Assert.False(c.Diagnostics.HasErrors);
        var plain = Check(c, "plain");
        var state = Check(c, "draft");
        Assert.Equal(("requires", plain.Type), (plain.Function.Name, (TypeSymbol)plain.Function.Owner!));
        var order = Assert.IsType<StateType>(state.Type);
        Assert.Equal(("requires", order.Struct), (state.Function.Name, state.Function.Owner));
        Assert.Equal("draft", Assert.IsType<BoundStructLiteral>(state.Receiver).State?.Name);
    }
}
