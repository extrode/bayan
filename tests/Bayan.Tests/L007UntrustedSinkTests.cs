using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// L007 (docs/laws/007-untrusted-never-reaches-a-command.md): an <c>Untrusted</c> program, path or argument of
/// <c>Io.run</c> or <c>Io.runBatch</c> is BYN2786; the same call with the value trusted or plain compiles.
/// Each row is run against both APIs.
/// </summary>
[Trait("Law", "L007")]
public sealed class L007UntrustedSinkTests
{
    private const string Prelude = """
        struct Holder {
            Untrusted<string> value
        }

        void main() fails IoError {
            let name: Untrusted<string> = Untrusted("x")
            let many: List<Untrusted<string>> = List<Untrusted<string>>()
            let span: Span<Untrusted<string>> = many.span
            let h: Holder = Holder { value: Untrusted("x") }
            let holders: List<Holder> = List<Holder>()
            let none: List<string> = List<string>()

        """;

    // Program or path, argument list ("{p}" is a plain program or path), and the one expected error code, or "" for none.
    public static TheoryData<string, string, string, string> Cases => new()
    {
        { "untrusted program", "name", "none.span", "BYN2786" },
        { "untrusted element in a list literal", "{p}", "[\"a\", name]", "BYN2786" },
        { "list of untrusted as args", "{p}", "many", "BYN2786" },
        { "span of untrusted as args", "{p}", "span", "BYN2786" },
        { "untrusted struct field as program", "h.value", "none.span", "BYN2786" },
        { "untrusted struct field in a list literal", "{p}", "[h.value]", "BYN2786" },
        // The deep check does not look inside structs; a struct never converts to `string`, so it is a plain mismatch.
        { "struct holding untrusted as program", "h", "none.span", "BYN2202" },
        { "list of structs holding untrusted as args", "{p}", "holders", "BYN2202" },
        { "trusted program", "name.trustUnchecked() because \"checked against the allow list\"", "none.span", "" },
        { "trusted element", "{p}", "[\"a\", name.trustUnchecked() because \"checked against the allow list\"]", "" },
        { "plain program and args", "{p}", "[\"a\", \"b\"]", "" },
        { "plain list as args", "{p}", "none.span", "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Run(string name, string program, string args, string expected) =>
        Check(name, "Io.run", program.Replace("{p}", "\"git\""), args, expected);

    [Theory]
    [MemberData(nameof(Cases))]
    public void RunBatch(string name, string program, string args, string expected) =>
        Check(name, "Io.runBatch", program.Replace("{p}", "\"./build.cmd\""), args, expected);

    private static void Check(string name, string api, string program, string args, string expected)
    {
        var source = $"{Prelude}    let _ = try {api}({program}, {args})\n}}\n";
        var c = Compilation.Create([new SourceText("l007.byn", System.Text.Encoding.UTF8.GetBytes(source))], ".");
        var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
        if (expected.Length == 0)
        {
            Assert.True(errors.Count == 0, $"{name}: unexpected errors: {string.Join("; ", errors.Select(e => e.Code + " " + e.Message))}\n{source}");
            return;
        }
        var d = Assert.Single(errors);
        Assert.Equal(expected, d.Code);
        if (expected == "BYN2786")
            Assert.Contains($"`{api}`", d.Message);
    }
}
