using System.Text;

namespace Bayan.Syntax.Lexing;

/// <summary>
/// Table-driven NFC (UAX #15) over <see cref="UnicodeTables"/>: full canonical decomposition (Hangul
/// algorithmic), canonical ordering by CCC, canonical composition with blocking. Ported method by
/// method to Bayan; tools/Bayan.UnicodeGen compiles this file and checks it against the runtime.
/// </summary>
internal static class Nfc
{
    const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
    const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

    /// <summary>Whether <paramref name="cp"/> lies in one of the inclusive (lo, hi) pairs.</summary>
    public static bool InRanges(ReadOnlySpan<uint> ranges, int cp)
    {
        int lo = 0, hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cp < ranges[mid * 2]) hi = mid - 1;
            else if (cp > ranges[mid * 2 + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    public static int Ccc(int cp)
    {
        var ranges = UnicodeTables.CccRanges;
        int lo = 0, hi = ranges.Length / 3 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cp < ranges[mid * 3]) hi = mid - 1;
            else if (cp > ranges[mid * 3 + 1]) lo = mid + 1;
            else return (int)ranges[mid * 3 + 2];
        }
        return 0;
    }

    /// <summary>Appends the full canonical decomposition of <paramref name="cp"/>.</summary>
    public static void Decompose(int cp, List<int> output)
    {
        var s = cp - SBase;
        if (s is >= 0 and < SCount)
        {
            output.Add(LBase + s / NCount);
            output.Add(VBase + s % NCount / TCount);
            if (s % TCount != 0) output.Add(TBase + s % TCount);
            return;
        }
        var index = UnicodeTables.DecompIndex;
        int lo = 0, hi = index.Length / 3 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cp < index[mid * 3]) hi = mid - 1;
            else if (cp > index[mid * 3]) lo = mid + 1;
            else
            {
                foreach (var d in UnicodeTables.DecompData.Slice((int)index[mid * 3 + 1], (int)index[mid * 3 + 2]))
                    output.Add((int)d);
                return;
            }
        }
        output.Add(cp);
    }

    /// <summary>Stable sort of each run of non-starters by CCC.</summary>
    public static void Reorder(List<int> cps)
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
    public static int ComposePair(int first, int second)
    {
        var l = first - LBase;
        var v = second - VBase;
        if (l is >= 0 and < LCount && v is >= 0 and < VCount) return SBase + (l * VCount + v) * TCount;
        var s = first - SBase;
        var t = second - TBase;
        if (s is >= 0 and < SCount && s % TCount == 0 && t is > 0 and < TCount) return first + t;
        var pairs = UnicodeTables.CompPairs;
        int lo = 0, hi = pairs.Length / 3 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var cmp = first != pairs[mid * 3] ? first.CompareTo((int)pairs[mid * 3]) : second.CompareTo((int)pairs[mid * 3 + 1]);
            if (cmp < 0) hi = mid - 1;
            else if (cmp > 0) lo = mid + 1;
            else return (int)pairs[mid * 3 + 2];
        }
        return -1;
    }

    /// <summary>Canonical composition in place; a character is blocked by an intervening one of equal or higher CCC.</summary>
    public static void Compose(List<int> cps)
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

    /// <summary>The NFC form of <paramref name="s"/>.</summary>
    public static string ToNfc(string s)
    {
        var cps = new List<int>();
        foreach (var r in s.EnumerateRunes()) Decompose(r.Value, cps);
        Reorder(cps);
        Compose(cps);
        var sb = new StringBuilder();
        foreach (var cp in cps) sb.Append(new Rune(cp).ToString());
        return sb.ToString();
    }

    /// <summary>What the lexer does: normalize and compare with the input.</summary>
    public static bool IsNfc(string s) => ToNfc(s) == s;
}
