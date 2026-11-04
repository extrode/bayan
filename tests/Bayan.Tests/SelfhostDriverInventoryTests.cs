using System.Reflection;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Spec 012 R12 (T8). K4: <c>src/runtime/libc-uses.txt</c> is exactly the library functions <c>bayan-rt.{h,c}</c> call.
/// K2 secondary scan: the C that <c>CliApp.EmitC</c> gives for every <c>run/</c> input, literals assembled at run time included,
/// has only extension tokens that are rows of <c>011-c-inventory.md</c>, and its direct library calls are the ones that file lists (Q7).
/// </summary>
public sealed partial class SelfhostDriverT8Tests
{
    private const string LibcPath = "src/runtime/libc-uses.txt";
    private const string InventoryPath = "docs/specs/011-self-hosted-emitter/011-c-inventory.md";

    /// <summary>Comments, string literals and character literals, matched in one pass so a quote inside a comment (or the reverse) is read once.</summary>
    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*|""(\\.|[^""\\])*""|'(\\.|[^'\\])+'", RegexOptions.Singleline)]
    private static partial Regex CommentsAndLiterals();

    [GeneratedRegex(@"(?<![.>\w])(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Call();

    [GeneratedRegex(@"^\| (?<site>[^ |]+:\d+) \| `(?<token>[^`]+)` \| (?<why>.+) \|$")]
    private static partial Regex Row();

    [GeneratedRegex(@"^- `(?<name>\w+)`")]
    private static partial Regex Listed();

    /// <summary>Words that precede <c>(</c> in the runtime or emitted C without being a function: statements, types, macros of the language or of the platform headers.</summary>
    private static readonly HashSet<string> NotFunctions = new(StringComparer.Ordinal)
    {
        "defined", "if", "for", "while", "switch", "return", "sizeof", "else", "void", "double", "bool",
        "int32_t", "int64_t", "uint8_t", "uint32_t", "uint64_t", "UINT64_C", "_Alignas", "_WIN32", "__attribute__",
        "WEXITSTATUS", "WIFSIGNALED", "WTERMSIG",
        // Function-pointer parameters and macro parameters of the runtime, not library functions.
        "hook", "eq", "less", "flush", "N", "X", "_i", "_u",
    };

    private static string Read(string repoPath) => File.ReadAllText(Path.Combine(Golden.RepoRoot, repoPath));

    private static string Code(string text) => CommentsAndLiterals().Replace(text, " ");

    private static bool OurName(string name) =>
        name.StartsWith("byn_", StringComparison.Ordinal) || name.StartsWith("BYN_", StringComparison.Ordinal)
        || name.StartsWith("bynt_", StringComparison.Ordinal) || name.StartsWith("bayan_", StringComparison.Ordinal) || name.StartsWith("__builtin_", StringComparison.Ordinal);

    /// <summary>The functions the runtime calls and does not define: every called name of both files except the runtime's own and <see cref="NotFunctions"/>.</summary>
    private static List<string> RuntimeLibc()
    {
        var code = Code(Read("src/runtime/bayan-rt.c")) + "\n" + Code(Read("src/runtime/bayan-rt.h"));
        return Call().Matches(code).Select(m => m.Groups["name"].Value)
            .Where(n => !OurName(n) && !NotFunctions.Contains(n))
            .Distinct().Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void LibcListIsTheRuntimesLibcCalls()
    {
        var listed = Read(LibcPath).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var actual = RuntimeLibc();
        var notListed = actual.Except(listed).ToList();
        var notCalled = listed.Except(actual).ToList();
        Assert.True(notListed.Count == 0 && notCalled.Count == 0,
            $"called but not in {LibcPath}:\n{string.Join("\n", notListed)}\nlisted but not called:\n{string.Join("\n", notCalled)}");
        Assert.Equal(listed.Order(StringComparer.Ordinal), listed);
        Assert.Contains("memcpy", listed);
    }

    private static Regex Extensions() =>
        (Regex)typeof(CInventoryTests).GetMethod("Extensions", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

    private static HashSet<string> InventoryTokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Read(InventoryPath).ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| Site ", StringComparison.Ordinal)) continue;
            var m = Row().Match(line);
            Assert.True(m.Success, $"inventory table line is not a row: {line}");
            tokens.Add(m.Groups["token"].Value);
        }
        return tokens;
    }

    private static HashSet<string> InventoryLibcCalls() =>
        Read(InventoryPath).ReplaceLineEndings("\n").Split('\n')
            .Select(l => Listed().Match(l)).Where(m => m.Success).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The direct library calls of one emitted translation unit: called names that are not the runtime's, not a keyword, type or
    /// <c>*_C</c> constant macro, and not defined at column 0 of the unit itself (the emitter writes definitions there and indents every call).
    /// </summary>
    internal static IEnumerable<string> DirectLibcCalls(string emitted)
    {
        var code = Code(emitted);
        foreach (var name in Call().Matches(code).Select(m => m.Groups["name"].Value).Distinct())
        {
            if (OurName(name) || NotFunctions.Contains(name) || name.EndsWith("_C", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(code, $@"(?m)^[A-Za-z_][\w \t\*]*[ \t\*]{Regex.Escape(name)}\s*\(")) continue;
            yield return name;
        }
    }

    /// <summary>The C of every <c>run/</c> input in each build mode (a <c>*.test.byn</c> as <c>bayan test</c> builds it); inputs the C# rejects are skipped.</summary>
    private static List<string> CorpusC()
    {
        var dir = Path.Combine(Golden.CasesRoot, "run");
        var all = new List<string>();
        foreach (var path in Directory.GetFiles(dir, "*.byn").Order(StringComparer.Ordinal))
        {
            if (path.EndsWith(".test.byn", StringComparison.Ordinal))
            {
                var work = Path.Combine(Golden.RepoRoot, "tmp", "t8-inventory", Path.GetFileNameWithoutExtension(path));
                if (SelfhostEmitterHarness.TestRunC(path, work) is { } testC) all.Add(testC);
                continue;
            }
            var c = Compilation.Create([SourceText.FromFile(path)], dir);
            if (c.Diagnostics.HasErrors) continue;
            foreach (var mode in new[] { BuildMode.Debug, BuildMode.Safe, BuildMode.Fast }) all.Add(CliApp.EmitC(c, mode));
        }
        return all;
    }

    private static readonly Lazy<List<string>> Corpus = new(CorpusC);

    [Fact]
    public void EmittedCHasOnlyInventoryExtensionTokens()
    {
        Assert.True(Corpus.Value.Count > 500, $"only {Corpus.Value.Count} emitted units");
        var rows = InventoryTokens();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var unit in Corpus.Value)
            foreach (Match m in Extensions().Matches(unit))
                foreach (var token in CInventoryTests.Split(m.Value)) if (!rows.Contains(token)) found.Add(token);
        Assert.True(found.Count == 0, $"emitted C has tokens without a row in {InventoryPath}:\n{string.Join("\n", found)}");
    }

    [Fact]
    public void EmittedCDirectLibcCallsAreListed()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var unit in Corpus.Value) foreach (var name in DirectLibcCalls(unit)) found.Add(name);
        var listed = InventoryLibcCalls();
        Assert.True(found.SetEquals(listed),
            $"direct libc calls in emitted C:\n{string.Join("\n", found)}\nlisted in {InventoryPath}:\n{string.Join("\n", listed.Order(StringComparer.Ordinal))}");
    }

    [Fact]
    public void ScanFindsABraceAssembledAcrossLiterals()
    {
        var rows = InventoryTokens();
        var unit = "int f(void) { return " + "(" + "{ 1; }); }";
        Assert.Contains(Extensions().Matches(unit), m => !rows.Contains(m.Value));
    }

    [Fact]
    public void ScanFindsAnUnlistedLibcCall()
    {
        Assert.Equal(["strlen"], DirectLibcCalls("int f(const char* s) {\n    return strlen(s);\n}\nint g(void) {\n    return f(\"a\");\n}\n"));
    }
}
