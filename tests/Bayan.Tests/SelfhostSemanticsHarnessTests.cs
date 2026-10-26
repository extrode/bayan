namespace Bayan.Tests;

public sealed class SelfhostSemanticsHarnessTests
{
    private const string Main = """
        i32 main() {
            print("before")
            print("== alpha")
            print("a1")
            print("a2")
            print("== beta")
            return 0
        }
        """;

    [Fact]
    public void RunsAndSplitsSections()
    {
        var s = SelfhostSemanticsHarness.RunSections(Main);
        Assert.Equal(["before"], s[""]);
        SelfhostSemanticsHarness.AssertSection(s, "alpha", ["a1", "a2"]);
        Assert.Empty(s["beta"]);
    }

    [Fact]
    public void FirstDifferingLineFails()
    {
        var s = SelfhostSemanticsHarness.Sections("== x\nl1\nl2\n");
        var e = Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.AssertSection(s, "x", ["l1", "L2"]));
        Assert.Contains("line 2", e.Message);
    }

    [Fact]
    public void CountMismatchMissingAndEmptyFail()
    {
        var s = SelfhostSemanticsHarness.Sections("== x\nl1\n== empty\n");
        Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.AssertSection(s, "x", ["l1", "l2"]));
        Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.AssertSection(s, "missing", ["l1"]));
        Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.AssertSection(s, "empty", []));
    }

    [Fact]
    public void RepeatedSectionFails() =>
        Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.Sections("== x\n== x\n"));

    [Fact]
    public void BuildErrorFailsWithTheComplaint()
    {
        var e = Assert.ThrowsAny<Exception>(() => SelfhostSemanticsHarness.Build("i32 main() => undefinedName"));
        Assert.Contains("BYN", e.Message);
    }
}
