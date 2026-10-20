namespace Bayan.Syntax.Lexing;

/// <summary>
/// Stand-in for the generated src/Bayan.Syntax/Lexing/UnicodeTables.g.cs, so the linked Nfc.cs
/// runs over the tables computed from the UCD in this run before they are written.
/// </summary>
internal static class UnicodeTables
{
    public static uint[] IdentStart = [], IdentContinue = [], Ccc = [], DecompIdx = [], Decomp = [], Comp = [];

    public static ReadOnlySpan<uint> IdentStartRanges => IdentStart;
    public static ReadOnlySpan<uint> IdentContinueRanges => IdentContinue;
    public static ReadOnlySpan<uint> CccRanges => Ccc;
    public static ReadOnlySpan<uint> DecompIndex => DecompIdx;
    public static ReadOnlySpan<uint> DecompData => Decomp;
    public static ReadOnlySpan<uint> CompPairs => Comp;
}
