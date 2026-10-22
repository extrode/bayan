using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary><c>for … in</c> over user types (spec 006 T7): <c>IIndexable</c> and <c>IEnumerator</c> conformers bind with a <see cref="UserIteration"/>.</summary>
public sealed class UserIterationTests
{
    private static Compilation Compile(string text) =>
        Compilation.Create([SourceText.FromString("main.byn", text)], Path.GetTempPath(), new CompilationOptions(Library: true));

    private const string Types = """
        struct Digits : IIndexable {
            type Item = i64;

            i64 count => 3;

            i64 get(i64 index) => index * 10;
        }

        struct Countdown : IEnumerator {
            type Item = i64;

            i64 left;

            bool moveNext(ref this) {
                left -= 1;
                return left >= 0;
            }

            i64 current => left;
        }

        struct Both : IEnumerator, IIndexable {
            type Item = i64;

            bool moveNext(ref this) => false;

            i64 current => 0;

            i64 count => 0;

            i64 get(i64 index) => index;
        }

        Countdown from(i64 n) => Countdown { left: n };

        """;

    private static List<BoundFor> Loops(Compilation c)
    {
        var fn = c.Modules[0].AllFunctions.Single(f => f.Name == "run");
        return BoundTree.Descendants(fn.Body!).OfType<BoundFor>().ToList();
    }

    [Fact]
    public void ConformersBindWithUserIterationInBothForms()
    {
        var c = Compile(Types + """
            void run() {
                let digits = Digits {};
                for d in digits { print($"{d}"); }
                for i64 d in digits { print($"{d}"); }
                var steps = Countdown { left: 3 };
                for n in steps { print($"{n}"); }
                for i64 n in from(2) { print($"{n}"); }
                var both = Both {};
                for b in both { print($"{b}"); }
            }
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var loops = Loops(c);
        Assert.Equal(5, loops.Count);
        Assert.All(loops, l => Assert.Equal(PrimitiveType.I64, l.Variable.Type));
        Assert.All(loops, l => Assert.NotNull(l.UserIteration));
        Assert.Equal([IterationKind.Indexable, IterationKind.Indexable, IterationKind.Enumerator, IterationKind.Enumerator, IterationKind.Indexable],
            loops.Select(l => l.UserIteration!.Kind));
        var digits = loops[0].UserIteration!;
        Assert.Equal(("count", "get"), (digits.Advance.Name, digits.Item.Name));
        Assert.Equal("Digits", ((FunctionSymbol)digits.Advance).Owner?.Name);
        Assert.Null(digits.Spill);
        var steps = loops[2].UserIteration!;
        Assert.Equal(("moveNext", "current"), (steps.Advance.Name, steps.Item.Name));
        Assert.Null(steps.Spill);
    }

    [Fact]
    public void AnRvalueEnumeratorIsSpilledToAHiddenVar()
    {
        var c = Compile(Types + """
            void run() {
                for n in from(2) { print($"{n}"); }
            }
            """);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var spill = Loops(c).Single().UserIteration!.Spill;
        Assert.NotNull(spill);
        Assert.Equal(LocalKind.Var, spill.Kind);
        Assert.Equal("Countdown", spill.Type.DisplayName);
        Assert.Contains(spill, c.Modules[0].AllFunctions.Single(f => f.Name == "run").Locals);
    }
}
