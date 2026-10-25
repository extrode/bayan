using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>The key hash helpers in the emitted C: a tuple or struct key hashes its parts, never its bytes (spec 008 R6).</summary>
public sealed class MapKeyEmitTests
{
    private static string Emit(string golden)
    {
        var source = Golden.Load(Path.Combine(Golden.CasesRoot, "run", golden + ".byn"));
        var c = Compilation.Create([source], Path.GetDirectoryName(source.Path)!);
        Assert.False(c.Diagnostics.HasErrors);
        return CliApp.EmitC(c, BuildMode.Safe);
    }

    [Theory]
    [InlineData("map-key-struct-padding")]
    [InlineData("map-key-tuple")]
    [InlineData("map-key-symbol")]
    [InlineData("map-basics")]
    public void NoCompositeKeyHashesItsBytes(string golden)
    {
        var hashes = Regex.Matches(Emit(golden), @"static uint64_t bynh_hash_\w+\((\w+) k\) \{\n(.*?)\n\}", RegexOptions.Singleline);
        Assert.NotEmpty(hashes);
        foreach (Match h in hashes)
            if (h.Groups[2].Value.Contains("sizeof k)"))
                Assert.DoesNotMatch("^bynt_", h.Groups[1].Value);
    }

    [Fact]
    public void StructKeyFoldsItsFieldsInOrder()
    {
        var code = Emit("map-key-struct-padding");
        Assert.Contains("h = byn_hash_mix(h, bynh_hash_bool(k.flag));\n    h = byn_hash_mix(h, bynh_hash_i64(k.id));", code);
    }
}
