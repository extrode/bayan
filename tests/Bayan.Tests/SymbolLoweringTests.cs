using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;

namespace Bayan.Tests;

/// <summary>Interpolating a <c>Symbol</c> lowers to its text (spec 008 R4).</summary>
public sealed class SymbolLoweringTests
{
    [Fact]
    public void InterpolatedSymbolLowersAsItsText()
    {
        var path = Path.Combine(Golden.CasesRoot, "typed/symbol-text.byn");
        var program = Lowerer.Lower(Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!));
        var writes = program.Functions.SelectMany(f => BoundTree.Descendants(f.Body)).OfType<LWrite>().ToList();
        // `$"[{s}] {s, width: 8}"` and `print($"{s} {name(s)}")`.
        Assert.Equal(2, writes.Count);
        var values = writes.SelectMany(w => w.Parts).Where(p => p.Value is not null).Select(p => p.Value!).ToList();
        Assert.DoesNotContain(values, v => v.Type is BuiltinType { Kind: BuiltinKind.Symbol });
        Assert.Contains(values, v => v is BoundBuiltinCall { Member: BuiltinMember.SymbolText });
    }
}
