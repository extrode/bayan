using Bayan.Cli;

namespace Bayan.Tests;

public sealed class FormatTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>A fresh, empty directory under <c>tmp/</c>.</summary>
    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "format", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AFixIsApplied()
    {
        var file = Path.Combine(Fresh("fix"), "main.byn");
        File.WriteAllText(file, "let a = true\nunless !a { print(\"y\") }\n");
        var (exit, stdout, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true\nif a { print(\"y\") }\n", File.ReadAllText(file));
        Assert.Contains("formatted", stdout);
    }

    [Fact]
    public void TwoFixesOnOneLineBothApply()
    {
        var file = Path.Combine(Fresh("two"), "main.byn");
        File.WriteAllText(file, "let a = true\nlet b = true\nif (a) { if (b) { print(\"x\") } }\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true\nlet b = true\nif a { if b { print(\"x\") } }\n", File.ReadAllText(file));
    }

    [Fact]
    public void AttributesCommentsAndRunesAreRewritten()
    {
        var file = Path.Combine(Fresh("round3"), "main.byn");
        File.WriteAllText(file,
            "/* one\n   two */\nbool isA(char c) => c == 'a'\n" +
            "[Cases(1, 2)]\ntest \"positive\" (i32 n) {\n    assert(n > 0)\n}\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(
            "// one\n// two\nbool isA(rune c) => c == 'a'\n" +
            "@cases(1, 2)\ntest \"positive\" (i32 n) {\n    assert(n > 0)\n}\n",
            File.ReadAllText(file));
    }

    [Fact]
    public void RequiresValueBecomesIt()
    {
        var file = Path.Combine(Fresh("requires"), "main.byn");
        File.WriteAllText(file, "type distinct Percent = i32 requires value in 0..=100\n");
        Run("format", file);
        Assert.Equal("type distinct Percent = i32 requires it in 0..=100\n", File.ReadAllText(file));
    }

    [Fact]
    public void DeclarationsBecomeTypeFirstAndLocalsNameFirst()
    {
        var file = Path.Combine(Fresh("order"), "main.byn");
        File.WriteAllText(file,
            "struct Box { weight: i64 }\nunion Event { timeout(after: i64) }\n" +
            "add(a: i64, i64 b) -> i64 { return a + b }\n" +
            "main() {\n    i64 t = add(1, 2)\n    i64 u = 0\n    u = t\n    const i64 k = 3\n    print($\"{u}{k}\")\n}\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(
            "struct Box { i64 weight }\nunion Event { timeout(i64 after) }\n" +
            "i64 add(i64 a, i64 b) { return a + b }\n" +
            "void main() {\n    let t: i64 = add(1, 2)\n    var u: i64 = 0\n    u = t\n    const k: i64 = 3\n    print($\"{u}{k}\")\n}\n",
            File.ReadAllText(file));
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        var file = Path.Combine(Fresh("idempotent"), "main.byn");
        File.WriteAllText(file, "let a = true\nif (a) { print(\"x\") }\n");
        Run("format", file);
        var once = File.ReadAllText(file);
        Assert.Equal("let a = true\nif a { print(\"x\") }\n", once);
        var (exit, stdout, _) = Run("format", file);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Empty(stdout);
        Assert.Equal(once, File.ReadAllText(file));
    }

    private const string WordsAndCSharp =
        "bool f(bool a, bool b, int n) => a and not b or n is not 0\n" +
        "void main() {\n    let x: nint = 3\n    switch x {\n        case 1 or 2: print(\"a\")\n        default: print(\"b\")\n    }\n" +
        "    print(\"s\") if not f(true, false, 1) and x > 0\n}\n";

    private const string SymbolsAndSized =
        "bool f(bool a, bool b, i32 n) => a && !b || n is not 0\n" +
        "void main() {\n    let x: isize = 3\n    switch x {\n        case 1 or 2: print(\"a\")\n        default: print(\"b\")\n    }\n" +
        "    print(\"s\") if !f(true, false, 1) && x > 0\n}\n";

    private static void Package(string dir, string style) =>
        File.WriteAllText(Path.Combine(dir, "package.byn"),
            $"Package {{\n    name: \"local/shop\",\n    version: \"0.1.0\",\n    style: Style {{ {style} }},\n}}\n");

    [Fact]
    public void WithoutAPackageBooleansBecomeSymbolsAndPrimitivesSized()
    {
        var file = Path.Combine(Fresh("defaults"), "main.byn");
        File.WriteAllText(file, WordsAndCSharp);
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(SymbolsAndSized, File.ReadAllText(file));
    }

    [Fact]
    public void ThePackageStyleIsApplied()
    {
        var dir = Fresh("words");
        Package(dir, "booleans: .words, primitives: .csharp");
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        var file = Path.Combine(dir, "src", "main.byn");
        File.WriteAllText(file, SymbolsAndSized);
        var (exit, _, stderr) = Run("format", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(WordsAndCSharp, File.ReadAllText(file));
        Assert.Contains("is not 0", File.ReadAllText(file));
    }

    [Fact]
    public void SymbolsWithoutSpacesGetThem()
    {
        var dir = Fresh("spacing");
        Package(dir, "booleans: .words");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, "bool f(bool a, bool b) => a&&!b||!(a)\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("bool f(bool a, bool b) => a and not b or not (a)\n", File.ReadAllText(file));
    }

    [Fact]
    public void AMemberNamedLikeAPrimitiveIsKept()
    {
        var file = Path.Combine(Fresh("member"), "main.byn");
        File.WriteAllText(file, "i64 f(Box b) => b.long\n");
        Run("format", file);
        Assert.Equal("i64 f(Box b) => b.long\n", File.ReadAllText(file));
    }

    [Fact]
    public void AnInvalidPackageIsReportedAndItsFilesAreLeftAlone()
    {
        var dir = Fresh("badstyle");
        Package(dir, "booleans: .both");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, WordsAndCSharp);
        var (exit, _, stderr) = Run("format", file);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("style.booleans", stderr);
        Assert.Equal(WordsAndCSharp, File.ReadAllText(file));
    }

    [Fact]
    public void CheckListsWhatWouldChangeAndWritesNothing()
    {
        var dir = Fresh("check");
        var changed = Path.Combine(dir, "a.byn");
        var clean = Path.Combine(dir, "b.byn");
        File.WriteAllText(changed, WordsAndCSharp);
        File.WriteAllText(clean, SymbolsAndSized);
        var (exit, stdout, _) = Run("format", "--check", dir);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Equal($"would format {changed}\n", stdout.Replace("\r\n", "\n"));
        Assert.Equal(WordsAndCSharp, File.ReadAllText(changed));
    }

    [Fact]
    public void CheckPassesOnAFormattedTree()
    {
        var dir = Fresh("check-clean");
        File.WriteAllText(Path.Combine(dir, "main.byn"), SymbolsAndSized);
        var (exit, stdout, stderr) = Run("format", "--check", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Empty(stdout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("booleans: .words, primitives: .csharp")]
    public void FormattingIsIdempotent(string style)
    {
        var dir = Fresh("idempotent-" + style.Length);
        if (style != "") Package(dir, style);
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file,
            "struct Box { weight: long }\nadd(a: int, b: i32) -> long { return a + b; }\n" +
            "void main() {\n    let a = true\n    if (a && !false) { print(\"x\") }\n    unless !a { print(\"y\") }\n" +
            "    int t = add(1, 2)\n    print($\"{t}\") if not a or t is not 0\n}\n");
        Run("format", file);
        var once = File.ReadAllText(file);
        var (exit, stdout, stderr) = Run("format", "--check", file);
        Assert.True(exit == CliApp.ExitOk, stderr + stdout + once);
        Run("format", file);
        Assert.Equal(once, File.ReadAllText(file));
    }

    [Fact]
    public void AFileWithARealErrorIsLeftAloneAndOthersAreFormatted()
    {
        var dir = Fresh("error");
        var broken = Path.Combine(dir, "a.byn");
        var fixable = Path.Combine(dir, "b.byn");
        var brokenText = "let a = true\nif (a) { print(\"x\"; }\n";
        File.WriteAllText(broken, brokenText);
        File.WriteAllText(fixable, "let a = true\nif (a) { print(\"x\") }\n");
        var (exit, _, stderr) = Run("format", dir);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("a.byn", stderr);
        Assert.Equal(brokenText, File.ReadAllText(broken));
        Assert.Equal("let a = true\nif a { print(\"x\") }\n", File.ReadAllText(fixable));
    }

    [Fact]
    public void DirectoriesAreFormattedRecursively()
    {
        var dir = Fresh("recursive");
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
        var file = Path.Combine(dir, "sub", "deep", "main.byn");
        File.WriteAllText(file, "let a = true\nif (a) { print(\"x\") }\n");
        var (exit, _, stderr) = Run("format", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true\nif a { print(\"x\") }\n", File.ReadAllText(file));
    }

    [Fact]
    public void AnInputFormWithoutAFixIsReported()
    {
        var file = Path.Combine(Fresh("nofix"), "main.byn");
        var text = "i32!Foo? f() { return 1 }\n";
        File.WriteAllText(file, text);
        var (exit, _, stderr) = Run("format", file);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("BYN1031", stderr);
        Assert.Contains("main.byn", stderr);
        Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void NonConvergenceIsReported()
    {
        var file = Path.Combine(Fresh("passes"), "main.byn");
        File.WriteAllText(file, "let a = true\nif (a) { print(\"x\") }\n");
        var passes = Format.MaxPasses;
        Format.MaxPasses = 1;
        try
        {
            var (exit, _, stderr) = Run("format", file);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("still changing after 1 passes", stderr);
        }
        finally { Format.MaxPasses = passes; }
    }

    [Fact]
    public void AnUnreadableFileIsReportedAndTheRestAreFormatted()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("io");
        var locked = Path.Combine(dir, "a.byn");
        var fixable = Path.Combine(dir, "b.byn");
        File.WriteAllText(locked, "let a = true;\n");
        File.WriteAllText(fixable, "let a = true\nif (a) { print(\"x\") }\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var (exit, _, stderr) = Run("format", dir);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("a.byn", stderr);
            Assert.Equal("let a = true\nif a { print(\"x\") }\n", File.ReadAllText(fixable));
        }
        finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void NoTempFileIsLeftBehind()
    {
        var dir = Fresh("temp");
        File.WriteAllText(Path.Combine(dir, "main.byn"), "let a = true\nif (a) { print(\"x\") }\n");
        Run("format", dir);
        Assert.Equal(["main.byn"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void IdenticalFixesApplyOnce()
    {
        var at = new Bayan.Syntax.Text.TextSpan(1, 0);
        var fix = new Bayan.Syntax.Diagnostics.Fix(at, "X");
        var result = Format.ApplyFixes("ab"u8.ToArray(), [fix, new Bayan.Syntax.Diagnostics.Fix(at, "X")]);
        Assert.Equal("aXb", System.Text.Encoding.UTF8.GetString(result));
    }
}
