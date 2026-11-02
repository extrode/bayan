namespace Bayan.Tests;

public sealed class SelfhostSemanticsT99Tests
{
    public void Test()
    {
        var expected1 = new List<string> { "ok1" }; // no-oracle: Q901
        var expected2 = new List<string> { "ok2" }; // no-oracle: Q901
        var expected3 = new List<string> { "too-many-exemptions" }; // no-oracle: Q901
        var expected4 = new List<string>
        {
            "multiline-hand-typed"
        };
    }
}
