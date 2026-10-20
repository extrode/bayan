namespace Bayan.Tests;

/// <summary>
/// Model check for <c>Map</c> (L3): <c>run/map-model.byn</c> applies 5,000 xorshift-chosen sets and removes
/// over 64 keys; <see cref="RunTests"/> pins its output to <c>map-model.stdout</c>, and this test pins that
/// golden to an insertion-ordered reference model running the same sequence.
/// </summary>
public sealed class MapModelTests
{
    [Fact]
    public void GoldenMatchesReferenceModel()
    {
        var keys = new List<long>();
        var values = new Dictionary<long, long>();
        var state = 0x9E3779B97F4A7C15UL;
        for (var step = 0L; step < 5000; step++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            var key = (long)(state % 64);
            if ((state >> 8) % 3 == 2)
            {
                if (values.Remove(key)) keys.Remove(key);
            }
            else
            {
                if (!values.ContainsKey(key)) keys.Add(key);
                values[key] = step;
            }
        }
        var expected = $"count {keys.Count}\n" + string.Concat(keys.Select(k => $"{k} {values[k]}\n"));
        var golden = File.ReadAllText(Path.Combine(Golden.CasesRoot, "run", "map-model.stdout")).ReplaceLineEndings("\n");
        Assert.Equal(expected, golden);
    }
}
