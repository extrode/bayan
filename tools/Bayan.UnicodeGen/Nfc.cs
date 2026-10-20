namespace Bayan.UnicodeGen;

/// <summary>
/// Table-driven NFC (UAX #15): full canonical decomposition (Hangul algorithmic), canonical
/// ordering by CCC, canonical composition with blocking. Written to be ported method by method
/// to Bayan; the tables it reads are exactly the ones emitted into unicode-tables.byn.
/// </summary>
public sealed class Nfc(
    IReadOnlyDictionary<int, int> ccc,
    IReadOnlyDictionary<int, int[]> decomp,
    IReadOnlyDictionary<(int First, int Second), int> comp)
{
    const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
    const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

    public int Ccc(int cp) => ccc.TryGetValue(cp, out var c) ? c : 0;

    /// <summary>Appends the full canonical decomposition of <paramref name="cp"/>.</summary>
    public void Decompose(int cp, List<int> output)
    {
        var s = cp - SBase;
        if (s is >= 0 and < SCount)
        {
            output.Add(LBase + s / NCount);
            output.Add(VBase + s % NCount / TCount);
            if (s % TCount != 0) output.Add(TBase + s % TCount);
            return;
        }
        if (decomp.TryGetValue(cp, out var d)) output.AddRange(d);
        else output.Add(cp);
    }

    /// <summary>Stable sort of each run of non-starters by CCC.</summary>
    public void Reorder(List<int> cps)
    {
        for (var i = 1; i < cps.Count; i++)
        {
            var c = Ccc(cps[i]);
            if (c == 0) continue;
            var cp = cps[i];
            var j = i;
            while (j > 0 && Ccc(cps[j - 1]) > c)
            {
                cps[j] = cps[j - 1];
                j--;
            }
            cps[j] = cp;
        }
    }

    /// <summary>Primary composite of the pair, or -1.</summary>
    public int ComposePair(int first, int second)
    {
        var l = first - LBase;
        var v = second - VBase;
        if (l is >= 0 and < LCount && v is >= 0 and < VCount) return SBase + (l * VCount + v) * TCount;
        var s = first - SBase;
        var t = second - TBase;
        if (s is >= 0 and < SCount && s % TCount == 0 && t is > 0 and < TCount) return first + t;
        return comp.TryGetValue((first, second), out var c) ? c : -1;
    }

    /// <summary>Canonical composition in place; a character is blocked by an intervening one of equal or higher CCC.</summary>
    public void Compose(List<int> cps)
    {
        if (cps.Count == 0) return;
        var starterIndex = 0;
        var lastCcc = Ccc(cps[0]) == 0 ? 0 : 256;
        var count = 1;
        for (var i = 1; i < cps.Count; i++)
        {
            var cp = cps[i];
            var c = Ccc(cp);
            var composite = ComposePair(cps[starterIndex], cp);
            if (composite >= 0 && (lastCcc < c || lastCcc == 0))
            {
                cps[starterIndex] = composite;
                continue;
            }
            if (c == 0) starterIndex = count;
            lastCcc = c;
            cps[count++] = cp;
        }
        cps.RemoveRange(count, cps.Count - count);
    }

    public List<int> Normalize(IEnumerable<int> input)
    {
        var cps = new List<int>();
        foreach (var cp in input) Decompose(cp, cps);
        Reorder(cps);
        Compose(cps);
        return cps;
    }

    public string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var cp in Normalize(s.EnumerateRunes().Select(r => r.Value)))
            sb.Append(new System.Text.Rune(cp).ToString());
        return sb.ToString();
    }

    /// <summary>What the lexer does: normalize and compare with the input.</summary>
    public bool IsNormalized(string s) => Normalize(s) == s;
}
