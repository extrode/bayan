using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>A payload-free case's declared value is its C tag; a case without one follows the previous tag.</summary>
public sealed class UnionValueTests
{
    [Fact]
    public void DeclaredValuesAreTheEmittedTags()
    {
        const string text = """
            union Reply { ok = 200, notFound = 404, moved(string to), none = -1, last }
            void show(Reply r) {
                switch (r) {
                    case .ok: print("ok");
                    case .notFound: print("not found");
                    case .moved(let to): print(to);
                    case .none: print("none");
                    case .last: print("last");
                }
            }
            show(.ok);
            show(.notFound);
            show(.moved("x"));
            show(.none);
            show(.last);

            """;
        var dir = Path.Combine(Golden.CasesRoot, "run");
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
        Assert.False(c.Diagnostics.HasErrors);
        var code = CliApp.EmitC(c, BuildMode.Safe);
        Assert.Contains(".tag = 200}", code);
        Assert.Contains(".tag = 404}", code);
        Assert.Contains(".tag = 405, .u.moved", code);
        Assert.Contains(".tag = -1}", code);
        Assert.Contains(".tag = 0}", code);
    }
}
