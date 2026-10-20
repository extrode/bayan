using System.Globalization;
using System.Text;
using Bayan.UnicodeGen;

// Generates selfhost/lexer/unicode-tables.byn from the UCD of a pinned Unicode version, after
// checking that the version matches what this .NET runtime uses for categories and NFC.

string? version = null, outPath = null;
for (var i = 0; i + 1 < args.Length; i += 2)
{
    if (args[i] == "--unicode") version = args[i + 1];
    else if (args[i] == "--out") outPath = args[i + 1];
}
if (version is null || outPath is null || args.Length != 4)
    return Fail("usage: dotnet run --project tools/Bayan.UnicodeGen -- --unicode <version> --out <file.byn>");

var command = "dotnet run --project tools/Bayan.UnicodeGen -- " + string.Join(' ', args);
var dir = Path.Combine("tmp", "ucd", version);
string unicodeData, normProps;
try
{
    unicodeData = await Fetch(dir, version, "UnicodeData.txt");
    normProps = await Fetch(dir, version, "DerivedNormalizationProps.txt");
}
catch (HttpRequestException e)
{
    return Fail($"cannot download UCD {version}: {e.Message}");
}

// UnicodeData.txt: general category, CCC and canonical decomposition mappings.
var category = new string?[0x110000];
var ccc = new SortedDictionary<int, int>();
var rawDecomp = new SortedDictionary<int, int[]>();
var rangeStart = -1;
foreach (var line in File.ReadLines(unicodeData))
{
    var f = line.Split(';');
    var cp = Convert.ToInt32(f[0], 16);
    if (f[1].EndsWith(", First>", StringComparison.Ordinal)) { rangeStart = cp; continue; }
    var lo = f[1].EndsWith(", Last>", StringComparison.Ordinal) ? rangeStart : cp;
    for (var c = lo; c <= cp; c++) category[c] = f[2];
    var cc = int.Parse(f[3], CultureInfo.InvariantCulture);
    if (cc != 0) ccc[cp] = cc;
    if (f[5].Length > 0 && f[5][0] != '<')
        rawDecomp[cp] = f[5].Split(' ').Select(h => Convert.ToInt32(h, 16)).ToArray();
}

// DerivedNormalizationProps.txt: Full_Composition_Exclusion covers the exclusion list,
// singletons and non-starter decompositions, so CompositionExclusions.txt is not needed.
var excluded = new HashSet<int>();
foreach (var line in File.ReadLines(normProps))
{
    var data = line.Split('#')[0];
    var f = data.Split(';');
    if (f.Length < 2 || f[1].Trim() != "Full_Composition_Exclusion") continue;
    var r = f[0].Trim().Split("..");
    var lo = Convert.ToInt32(r[0], 16);
    var hi = r.Length > 1 ? Convert.ToInt32(r[1], 16) : lo;
    for (var c = lo; c <= hi; c++) excluded.Add(c);
}

// Assertion (a): identifier classes from the file equal those from Rune.GetUnicodeCategory.
string[] startCats = ["Lu", "Ll", "Lt", "Lm", "Lo", "Nl"];
string[] continueCats = ["Mn", "Mc", "Nd", "Pc"];
var isStart = new bool[0x110000];
var isContinue = new bool[0x110000];
for (var cp = 0; cp < 0x110000; cp++)
{
    if (cp is >= 0xD800 and <= 0xDFFF) continue;
    var gc = category[cp] ?? "Cn";
    var fileStart = cp == '_' || startCats.Contains(gc);
    var fileContinue = fileStart || cp is 0x200C or 0x200D || continueCats.Contains(gc);
    var rune = new Rune(cp);
    isStart[cp] = RuneIsStart(rune);
    isContinue[cp] = RuneIsContinue(rune);
    if (fileStart != isStart[cp] || fileContinue != isContinue[cp])
        return Fail($"assertion (a) failed: U+{cp:X4} is {gc} in UCD {version} but {Rune.GetUnicodeCategory(rune)} " +
            $"in .NET {Environment.Version}; try another --unicode version");
}

// Full canonical decompositions, recursively expanded.
var decomp = new SortedDictionary<int, int[]>();
foreach (var cp in rawDecomp.Keys) decomp[cp] = Expand(cp).ToArray();
var comp = new SortedDictionary<(int First, int Second), int>();
foreach (var (cp, d) in rawDecomp)
    if (d.Length == 2 && !excluded.Contains(cp)) comp[(d[0], d[1])] = cp;

// Assertion (b): table-driven NFC agrees with the runtime's string.Normalize / IsNormalized.
var nfc = new Nfc(ccc, decomp, comp);
var ident = Enumerable.Range(0, 0x110000).Where(cp => isContinue[cp]).ToArray();
var checks = 0;
var notNfc = 0;
foreach (var cp in ident)
    if (!Agrees(char.ConvertFromUtf32(cp), out var message)) return Fail(message);
foreach (var ((first, second), _) in comp)
    if (!Agrees(char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second), out var message)) return Fail(message);
// Random strings draw half the time from identifier code points that take part in normalization
// (CCC, decomposition, composition, conjoining jamo), so composition and reordering get exercised.
var paired = comp.Keys.SelectMany(k => new[] { k.First, k.Second }).ToHashSet();
var interesting = ident.Where(cp => ccc.ContainsKey(cp) || decomp.ContainsKey(cp) || paired.Contains(cp) || cp is >= 0x1100 and <= 0x11FF or >= 0xAC00 and <= 0xD7A3)
    .ToArray();
var random = new Random(1234);
for (var i = 0; i < 5000; i++)
{
    var sb = new StringBuilder();
    var length = random.Next(1, 7);
    for (var j = 0; j < length; j++)
    {
        var pool = random.Next(2) == 0 ? ident : interesting;
        sb.Append(char.ConvertFromUtf32(pool[random.Next(pool.Length)]));
    }
    if (!Agrees(sb.ToString(), out var message)) return Fail(message);
}
Console.WriteLine($"assertions passed: identifier classes over 0..0x10FFFF, {checks} NFC checks, {notNfc} of them not NFC " +
    $"({ident.Length} code points, {comp.Count} pairs, 5000 random strings)");

// Emit.
var identStart = Ranges(cp => isStart[cp]);
var identContinue = Ranges(cp => isContinue[cp] && !isStart[cp]);
var cccRanges = new List<(int Lo, int Hi, int Ccc)>();
foreach (var (cp, c) in ccc)
{
    if (cccRanges.Count > 0 && cccRanges[^1].Hi == cp - 1 && cccRanges[^1].Ccc == c)
        cccRanges[^1] = (cccRanges[^1].Lo, cp, c);
    else cccRanges.Add((cp, cp, c));
}
var decompIndex = new List<string>();
var decompData = new List<string>();
foreach (var (cp, d) in decomp)
{
    decompIndex.AddRange([Hex(cp), decompData.Count.ToString(CultureInfo.InvariantCulture),
        d.Length.ToString(CultureInfo.InvariantCulture)]);
    decompData.AddRange(d.Select(Hex));
}

var output = new StringBuilder();
output.Append("// Generated by tools/Bayan.UnicodeGen; do not edit.\n");
output.Append($"// Unicode version: {version}\n");
output.Append($"// .NET runtime: {Environment.Version}\n");
output.Append($"// Command: {command}\n");
output.Append("// All ranges are inclusive and sorted by first code point, for binary search.\n");
output.Append("// Hangul syllables are decomposed and composed algorithmically and appear in no table.\n");
Emit("identStartRanges", "identifier start (`_`, Lu Ll Lt Lm Lo Nl): pairs lo, hi", 10,
    identStart.SelectMany(r => new[] { Hex(r.Lo), Hex(r.Hi) }));
Emit("identContinueRanges", "identifier continue minus start (U+200C U+200D, Mn Mc Nd Pc): pairs lo, hi", 10,
    identContinue.SelectMany(r => new[] { Hex(r.Lo), Hex(r.Hi) }));
Emit("cccRanges", "nonzero canonical combining class: triples lo, hi, ccc", 9,
    cccRanges.SelectMany(r => new[] { Hex(r.Lo), Hex(r.Hi), r.Ccc.ToString(CultureInfo.InvariantCulture) }));
Emit("decompIndex", "full canonical decomposition: triples cp, offset, length into decompData", 9, decompIndex);
Emit("decompData", "decomposition code points", 10, decompData);
Emit("compPairs", "primary composites: triples first, second, composite, sorted by (first, second)", 9,
    comp.SelectMany(p => new[] { Hex(p.Key.First), Hex(p.Key.Second), Hex(p.Value) }));
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, output.ToString(), new UTF8Encoding(false));
Console.WriteLine($"wrote {outPath}: identStartRanges {identStart.Count * 2}, identContinueRanges {identContinue.Count * 2}, " +
    $"cccRanges {cccRanges.Count * 3}, decompIndex {decompIndex.Count}, decompData {decompData.Count}, compPairs {comp.Count * 3}");
return 0;

bool Agrees(string s, out string message)
{
    checks++;
    var expected = s.Normalize(NormalizationForm.FormC);
    var actual = nfc.Normalize(s);
    var expectedIs = s.IsNormalized(NormalizationForm.FormC);
    if (!expectedIs) notNfc++;
    var actualIs = nfc.IsNormalized(s);
    message = expected == actual && expectedIs == actualIs ? ""
        : $"assertion (b) failed for {Dump(s)}: Normalize gives {Dump(expected)}, tables give {Dump(actual)}; " +
          $"IsNormalized {expectedIs}, tables {actualIs} (UCD {version}, .NET {Environment.Version})";
    return message.Length == 0;
}

IEnumerable<int> Expand(int cp) =>
    rawDecomp.TryGetValue(cp, out var d) ? d.SelectMany(Expand) : [cp];

List<(int Lo, int Hi)> Ranges(Func<int, bool> member)
{
    var ranges = new List<(int Lo, int Hi)>();
    for (var cp = 0; cp < 0x110000; cp++)
    {
        if (!member(cp)) continue;
        if (ranges.Count > 0 && ranges[^1].Hi == cp - 1) ranges[^1] = (ranges[^1].Lo, cp);
        else ranges.Add((cp, cp));
    }
    return ranges;
}

void Emit(string name, string comment, int perLine, IEnumerable<string> values)
{
    var list = values.ToList();
    output.Append($"\n// {comment}\n");
    output.Append($"const u32[{list.Count}] {name} = [\n");
    for (var i = 0; i < list.Count; i += perLine)
    {
        var line = "    " + string.Join(", ", list.Skip(i).Take(perLine)) + (i + perLine < list.Count ? "," : "");
        output.Append(line.TrimEnd()).Append('\n');
    }
    output.Append("];\n");
}

static string Hex(int cp) => "0x" + cp.ToString("X4", CultureInfo.InvariantCulture);

static string Dump(string s) => string.Join(' ', s.EnumerateRunes().Select(r => $"U+{r.Value:X4}"));

// Same classes as Lexer.IsIdentifierStart / IsIdentifierContinue in src/Bayan.Syntax/Lexing/Lexer.cs.
static bool RuneIsStart(Rune r) =>
    r.Value == '_' || Rune.GetUnicodeCategory(r) is UnicodeCategory.UppercaseLetter
        or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
        or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;

static bool RuneIsContinue(Rune r) =>
    RuneIsStart(r) || r.Value is 0x200C or 0x200D || Rune.GetUnicodeCategory(r) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
        or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;

static async Task<string> Fetch(string dir, string version, string file)
{
    var path = Path.Combine(dir, file);
    if (File.Exists(path)) return path;
    Directory.CreateDirectory(dir);
    using var http = new HttpClient();
    var url = $"https://www.unicode.org/Public/{version}/ucd/{file}";
    Console.WriteLine($"downloading {url}");
    var bytes = await http.GetByteArrayAsync(url);
    File.WriteAllBytes(path + ".part", bytes);
    File.Move(path + ".part", path, overwrite: true);
    return path;
}

static int Fail(string message)
{
    Console.Error.WriteLine("Bayan.UnicodeGen: " + message);
    return 1;
}
