using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T4: the mutator finds each v1 operator's sites, with source text, and only mutants that type-check.</summary>
public sealed class MutatorTests
{
    private static readonly string Dir = Path.Combine(Golden.CasesRoot, "run");

    private static Mutations Find(string text, string file = "lib.byn", IReadOnlySet<string>? skipFiles = null)
    {
        var c = Compilation.Create([SourceText.FromString(Path.Combine(Dir, file), text)], Dir,
            new CompilationOptions(IncludeTests: file.EndsWith(".test.byn", StringComparison.Ordinal), Library: true));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return Mutator.Find(c, skipFiles);
    }

    /// <summary>The sites of <paramref name="op"/> as <c>line:column original => mutated</c>, in source order.</summary>
    private static string[] Sites(Mutations m, string op) =>
        m.Sites.Where(s => s.Operator == op).OrderBy(s => s.Line).ThenBy(s => s.Column)
            .Select(s => $"{s.Line}:{s.Column} {s.Original} => {s.Mutated}").ToArray();

    [Fact]
    public void Boundary()
    {
        var m = Find("bool small(i32 a) {\n    return a < 10 || a >= 2\n}\n");
        Assert.Equal(["2:14 a < 10 => a <= 10", "2:24 a >= 2 => a > 2"], Sites(m, Mutator.Boundary));
    }

    [Fact]
    public void Relational()
    {
        var m = Find("bool same(i32 a) {\n    return a == 2 && a > 0\n}\n");
        Assert.Equal(["2:14 a == 2 => a != 2", "2:24 a > 0 => a <= 0"], Sites(m, Mutator.Relational));
    }

    [Fact]
    public void Logical()
    {
        var m = Find("bool both(bool a, bool b) {\n    return a && b\n}\n");
        Assert.Equal(["2:14 a && b => a || b"], Sites(m, Mutator.Logical));
    }

    [Fact]
    public void Condition()
    {
        var m = Find("i32 count(i32 n) {\n    var i: i32 = 0\n    while i < n {\n        i += 1\n    }\n    if i > 3 {\n        return 3\n    }\n    return 0 if n == 0\n    return i\n}\n");
        Assert.Equal(["3:11 i < n => !(i < n)", "6:8 i > 3 => !(i > 3)", "9:17 n == 0 => !(n == 0)"], Sites(m, Mutator.Condition));
    }

    [Fact]
    public void Bool()
    {
        var m = Find("bool no(bool x) {\n    return x == false\n}\n");
        Assert.Equal(["2:17 false => true"], Sites(m, Mutator.Bool));
    }

    [Fact]
    public void Arithmetic()
    {
        var m = Find("i32 mix(i32 a) {\n    var t: i32 = a + a * 2\n    t -= a % 3\n    return t\n}\n");
        Assert.Equal(["2:20 a + a * 2 => a - a * 2", "2:24 a * 2 => a / 2", "3:7 t -= a % 3 => t += a % 3", "3:12 a % 3 => a * 3"],
            Sites(m, Mutator.Arithmetic));
    }

    [Fact]
    public void Literal()
    {
        var m = Find("i64 seven() {\n    return 7\n}\n\ni64 neg() {\n    return -5\n}\n");
        Assert.Equal(["2:12 7 => 8", "2:12 7 => 6", "6:12 -5 => -4", "6:12 -5 => -6"], Sites(m, Mutator.Literal));
    }

    [Fact]
    public void Negation()
    {
        var m = Find("i32 flip(i32 x) {\n    return -x\n}\n");
        Assert.Equal(["2:12 -x => x"], Sites(m, Mutator.Negation));
    }

    [Fact]
    public void Statement()
    {
        var m = Find("void note(i32 x) {\n    print($\"{x}\")\n}\n\ni32 run(i32 x) {\n    note(x)\n    var y: i32 = x\n    y = 3\n    y += 1\n    y++\n    return y\n}\n");
        Assert.Equal(["2:5 print($\"{x}\") => ", "6:5 note(x) => ", "8:5 y = 3 => ", "9:5 y += 1 => ", "10:5 y++ => "], Sites(m, Mutator.Statement));
    }

    [Fact]
    public void EmptyReturn()
    {
        var m = Find("i32 size(i32 x) {\n    return x\n}\n\nstring name(string s) {\n    return s\n}\n\ni32? find(i32 x) {\n    return x\n}\n\nbool ok(bool b) {\n    return b\n}\n\ni32 zero() {\n    return 0\n}\n");
        Assert.Equal(["2:12 x => 0", "6:12 s => \"\"", "10:12 x => null", "14:12 b => false"], Sites(m, Mutator.EmptyReturn));
    }

    [Fact]
    public void Variant()
    {
        var m = Find("union error E {\n    bad\n    worse\n    other(i32 code)\n}\n\ni64 check(i64 n) fails E {\n    fail .bad if n < 0\n    return n\n}\n");
        Assert.Equal(["8:11 .bad => .worse"], Sites(m, Mutator.Variant));
        Assert.Equal(1, m.NotEmitted[Mutator.Variant]);
    }

    [Fact]
    public void AMutantThatWouldNotTypeCheckIsCountedNotEmitted()
    {
        // `2147483647 + 1` overflows i32 at compile time; `2147483647 + 1` and `255 + 1` do not fit; `while !true` would fall off the end.
        var m = Find("i32 big() {\n    return 2147483647 - 1\n}\n\nu8 top() {\n    return 255\n}\n\ni32 spin(i32 n) {\n    while true {\n        return n\n    }\n}\n");
        Assert.Empty(Sites(m, Mutator.Arithmetic));
        Assert.Equal(1, m.NotEmitted[Mutator.Arithmetic]);
        Assert.Contains("6:12 255 => 254", Sites(m, Mutator.Literal));
        Assert.Equal(2, m.NotEmitted[Mutator.Literal]);
        Assert.Equal(1, m.NotEmitted[Mutator.Bool]);
        Assert.Equal(1, m.NotEmitted[Mutator.Condition]);
        Assert.Equal(Mutator.Operators.Order(), m.NotEmitted.Keys.Order());
    }

    [Fact]
    public void TestBodiesAndSkippedFilesYieldNoSites()
    {
        var text = "i32 twice(i32 x) {\n    return x * 2\n}\n\ntest \"twice\" {\n    assert twice(2) == 4\n    let dbl: i32 -> i32 = x => x * 2\n    assert dbl(1 + 1) == 4\n    assert 1 < 2 && true\n}\n";
        var m = Find(text, "lib.test.byn");
        Assert.All(m.Sites, s => Assert.Equal("twice", s.Function.Name));
        Assert.All(m.Sites, s => Assert.Equal(2, s.Line));
        Assert.Empty(Find("i32 twice(i32 x) {\n    return x * 2\n}\n", "main.byn", new HashSet<string> { Path.Combine(Dir, "main.byn") }).Sites);
    }
}
