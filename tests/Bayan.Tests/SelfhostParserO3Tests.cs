using Bayan.Syntax.Ast;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// 011 T13 (Q819): the O3 split of a field's <c>requires</c> rules (Parser.cs:783-797), against the C# parser's dump and diagnostics.
/// A rule naming another field, or with <c>at .f</c>, moves to the struct's rules; one that also reads <c>it</c> is BYN3218 and dropped.
/// A struct member that parses to nothing (a C# <c>delegate</c>) takes no slot, and a field outside a struct (an extension's) keeps its rules.
/// </summary>
public sealed class SelfhostParserO3Tests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("names-field", "struct Range {\n    i64 lo\n    i64 hi requires hi >= lo\n}\n")]
    [InlineData("at-field", "struct Range {\n    i64 lo\n    i64 hi requires it >= 0 at .hi\n}\n")]
    [InlineData("mixes-it", "struct Range {\n    i64 lo\n    i64 hi requires it >= lo\n}\n")]
    [InlineData("own-it", "struct Range {\n    i64 lo requires it >= 0\n    i64 hi requires it >= 0\n}\n")]
    [InlineData("ordered", "struct Range {\n    i64 lo\n    i64 hi requires hi >= lo\n    i64 mid requires it >= 0\n        requires mid <= hi\n\n    requires lo >= 0\n}\n")]
    [InlineData("member-none", "struct Range {\n    i64 lo requires lo >= 0\n    delegate int F(int x);\n    i64 hi\n}\n")]
    [InlineData("extension-field", "extension Range {\n    i64 lo requires lo >= 0\n}\n")]
    public void FieldRulesSplitAsTheCSharpParser(string name, string source)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "parser-o3");
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, name + ".byn");
        File.WriteAllText(input, source);
        var tree = SyntaxTree.Parse(SourceText.FromFile(input));
        var refPath = Path.Combine(Golden.RepoRoot, "tmp", "parser-ref", "o3_" + name + ".ast");
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, AstDump.Dump(tree));
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, input);
        output.WriteLine(stderr);
        if (Golden.Compare(refPath, stdout, update: false).Failure is { } failure) Assert.Fail(failure);
        Assert.Equal(tree.Diagnostics.Any(d => d.IsError) ? 1 : 0, exit);
    }
}
