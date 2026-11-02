namespace Bayan.Tests;

public sealed class WarmUpTests
{
    [Fact]
    public async Task NoMoreBuildsRunAtOnceThanTheGateAllows()
    {
        int running = 0, peak = 0;
        using var full = new ManualResetEventSlim();
        var built = new System.Collections.Concurrent.ConcurrentBag<string>();
        var names = Enumerable.Range(0, 40).Select(i => $"case-{i}").ToArray();
        await WarmUp.Start(names, name =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            if (now == 3) full.Set();
            if (!full.Wait(TimeSpan.FromSeconds(10))) full.Set(); // the first builds hold their slots until the gate is full; a serial Start times out once and shows peak 1
            built.Add(name);
            Interlocked.Decrement(ref running);
        }, new SemaphoreSlim(3));
        Assert.Equal(3, peak);
        Assert.Equal(names.Order(), built.Order());
    }

    [Fact]
    public async Task AFailedBuildDoesNotStopTheOthers()
    {
        var built = new System.Collections.Concurrent.ConcurrentBag<string>();
        await WarmUp.Start(["a", "b", "c"], name =>
        {
            if (name == "b") throw new InvalidOperationException("b did not build");
            built.Add(name);
        }, new SemaphoreSlim(1));
        Assert.Equal(["a", "c"], built.Order());
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
