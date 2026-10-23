using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Span literals live on the stack: checked in the emitted C rather than the program's output.</summary>
public sealed class CollectionLiteralTests
{
    private static string Body(string text, string function)
    {
        var dir = Path.Combine(Golden.CasesRoot, "run");
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
        Assert.False(c.Diagnostics.HasErrors);
        var code = CliApp.EmitC(c, BuildMode.Safe);
        return Regex.Match(code, $@"\bbyn_main__{function}\(void\) \{{\n.*?\n\}}\n", RegexOptions.Singleline).Value;
    }

    [Theory]
    [InlineData("Span<i32> s = [1, 2, 3]")]
    [InlineData("i32[2] more = [5, 6]\n    Span<i32> s = [..more, 3]")]
    public void ASpanLiteralIsAStackArray(string declaration)
    {
        var body = Body($"i32 first() {{\n    {declaration}\n    return s[0]\n}}\n\nprint($\"{{first()}}\")\n", "first");
        Assert.Contains("bynt_arr3_i32 ", body);
        Assert.DoesNotContain("byn_context", body);
        Assert.DoesNotContain("alloc", body);
    }

    [Fact]
    public void AListLiteralAllocates()
    {
        var body = Body("i32 first() {\n    List<i32> s = [1, 2, 3]\n    return s[0]\n}\n\nprint($\"{first()}\")\n", "first");
        Assert.Contains("byn_context", body);
    }
}
