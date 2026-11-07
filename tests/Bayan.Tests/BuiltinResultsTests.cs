using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Region analysis's builtin results table (Q745): a builtin whose result is fresh does not borrow its arguments.</summary>
public sealed class BuiltinResultsTests
{
    private static IEnumerable<string> Errors(string type, string fails, string result)
    {
        var src = $"{type} f(){fails} {{\n    using let arena = Arena()\n    let name = \"ann\".copy(to: arena)\n    let bytes = name.bytes\n"
            + $"    var args = List<string>()\n    args.append(name)\n    return {result}\n}}\n\nvoid main() {{\n}}\n";
        var c = Compilation.Create([new SourceText("main.byn", Encoding.UTF8.GetBytes(src))], ".");
        return c.Diagnostics.Items.Where(d => d.IsError).Select(d => $"{d.Code} {d.Message}");
    }

    [Fact]
    public void ReturningTheArenaTextIsRefused() =>
        Assert.Contains(Errors("string", "", "name"), e => e.StartsWith("BYN3003", StringComparison.Ordinal));

    [Theory]
    [InlineData("List<u8>", " fails IoError", "try Io.readFile(name)")]
    [InlineData("string", " fails IoError", "try Io.readText(name)")]
    [InlineData("List<string>", " fails IoError", "try Io.listDir(name)")]
    [InlineData("ProcessResult", " fails IoError", "try Io.run(name, args.span)")]
    [InlineData("ProcessResult", " fails IoError", "try Io.runBatch(name, args.span)")]
    [InlineData("string", " fails Utf8Error", "try Utf8.decode(bytes)")]
    [InlineData("string", "", "Utf8.tryDecode(bytes) ?? \"none\"")]
    [InlineData("string", "", "Utf8.decode(bytes, invalid: .replace)")]
    [InlineData("i64", "", "string.invalidUtf8At(bytes) ?? -1")]
    [InlineData("string", "", "Hex.encode(bytes)")]
    [InlineData("List<u8>", " fails ParseError", "try Hex.decode(name)")]
    [InlineData("Symbol", "", "Symbol.intern(name)")]
    public void AFreshBuiltinResultDoesNotBorrowItsArguments(string type, string fails, string result) =>
        Assert.Empty(Errors(type, fails, result));
}
