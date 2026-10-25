using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>The symbol literal table and the runtime symbol code in the emitted C (spec 008 R2, R10).</summary>
public sealed class SymbolEmitTests
{
    private static string Emit(SourceText source)
    {
        var c = Compilation.Create([source], Path.GetDirectoryName(source.Path)!);
        Assert.False(c.Diagnostics.HasErrors);
        return CliApp.EmitC(c, BuildMode.Safe);
    }

    private static string Emit(string text) => Emit(SourceText.FromString(Path.Combine(Golden.CasesRoot, "run", "main.byn"), text));

    private static List<string> Table(string code)
    {
        var table = Regex.Match(code, @"static const byn_str byn_symtab\[\] = \{\n(.*?)\};", RegexOptions.Singleline);
        Assert.True(table.Success);
        return table.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
    }

    [Fact]
    public void LiteralTableStartsWithTheEmptyText()
    {
        var code = Emit(Golden.Load(Path.Combine(Golden.CasesRoot, "run/symbol-literal-runtime.byn")));
        Assert.Equal(["BYN_STR(\"\", 0),", "BYN_STR(\"total\", 5),"], Table(code));
        Assert.Contains("((byn_sym){1})", code);
        Assert.True(code.IndexOf("byn_sym_init(byn_symtab", StringComparison.Ordinal) > code.IndexOf("byn_hash_seed_init(", StringComparison.Ordinal));
    }

    [Fact]
    public void LiteralTableIsSortedByUtf8Bytes()
    {
        // U+FF61 sorts before U+1F600 in UTF-8 and after it in UTF-16.
        var code = Emit("void main() {\n    let a: Symbol = \"b\"\n    let b: Symbol = \"\\u{1F600}\"\n    let c: Symbol = \"\\u{FF61}\"\n    let d: Symbol = \"B\"\n    let e: Symbol = \"b\"\n    print($\"{a}{b}{c}{d}{e}\")\n}\n");
        Assert.Equal(
            ["BYN_STR(\"\", 0),", "BYN_STR(\"B\", 1),", "BYN_STR(\"b\", 1),", "BYN_STR(\"\\357\\275\\241\", 3),", "BYN_STR(\"\\360\\237\\230\\200\", 4),"],
            Table(code));
    }

    [Fact]
    public void ProgramWithoutSymbolEmitsNoSymbolCode()
    {
        var code = Emit("void main() {\n    var m = Map<string, i64>()\n    m[\"a\"] = 1\n    print($\"{m.length}\")\n}\n");
        Assert.DoesNotContain("byn_sym", code);
    }
}
