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
    public void ACSharpPrimitivesProjectFormatsAndBuilds()
    {
        var dir = Fresh("csharp-builds");
        Package(dir, "primitives: .csharp");
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        var file = Path.Combine(dir, "src", "main.byn");
        const string Source = "long twice(int n) => n * 2\n\nvoid main() {\n    let r: double = 0.5\n    print($\"{twice(3)} {r}\")\n}\n";
        File.WriteAllText(file, Source);
        var (exit, _, stderr) = Run("format", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(Source, File.ReadAllText(file));
        (exit, _, stderr) = Run("build", "-o", Path.Combine(dir, "out"), file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.DoesNotContain("BYN1030", stderr);
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
    public void ANameSpelledLikeAPrimitiveIsKeptAndOnlyTypesAreRenamed()
    {
        var dir = Fresh("primitive-names");
        Package(dir, "primitives: .csharp");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file,
            "struct P { i8: int }\nvoid main() {\n    let u8 = 1\n    let p = P { i8: 3 }\n    let w: i8 = 2\n    print($\"{u8}{p.i8}{w}\")\n}\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(
            "struct P { int i8 }\nvoid main() {\n    let u8 = 1\n    let p = P { i8: 3 }\n    let w: sbyte = 2\n    print($\"{u8}{p.i8}{w}\")\n}\n",
            File.ReadAllText(file));
    }

    [Fact]
    public void ConversionsAndStaticReceiversAreRespelledToTheProjectStyle()
    {
        var sized = Path.Combine(Fresh("conv-sized"), "main.byn");
        File.WriteAllText(sized, "void main() {\n    let y = 1\n    let z: long = long(y)\n    let a = int.tryFrom(y)\n    let b = long.max\n}\n");
        var (exit, _, stderr) = Run("format", sized);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("void main() {\n    let y = 1\n    let z: i64 = i64(y)\n    let a = i32.tryFrom(y)\n    let b = i64.max\n}\n", File.ReadAllText(sized));

        var dir = Fresh("conv-csharp");
        Package(dir, "primitives: .csharp");
        var csharp = Path.Combine(dir, "main.byn");
        File.WriteAllText(csharp, "void main() {\n    let y = 1\n    let a = i32.tryFrom(y)\n    let b = i64.max\n    let c = i8(y)\n}\n");
        (exit, _, stderr) = Run("format", csharp);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("void main() {\n    let y = 1\n    let a = int.tryFrom(y)\n    let b = long.max\n    let c = sbyte(y)\n}\n", File.ReadAllText(csharp));
    }

    [Fact]
    public void ACalleeOrReceiverSpelledLikeADeclaredNameIsKept()
    {
        var dir = Fresh("conv-declared");
        Package(dir, "primitives: .csharp");
        var file = Path.Combine(dir, "main.byn");
        const string source = "void main() {\n    let i8 = 1\n    let n = i8.foo\n    let m = i8(2)\n}\n";
        File.WriteAllText(file, source);
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(source, File.ReadAllText(file));
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

    /// <summary>One of each round-3 input form (BYN1040–1053, 1062, 6001, 6006).</summary>
    private const string Round3InputForms =
        "/* Shapes. */\nunion Shape { circle(f64 r), square(f64 side) }\nenum Size { /* small */ s, m /* medium */, l }\n" +
        "unique struct File {\n    i64 fd\n}\nerror enum StockError {\n    outOfStock\n}\n" +
        "distinct type Percent = i32 requires value in 0..=100\n[Derive(Json)]\nstruct Parcel {\n    weight: i64\n}\n" +
        "i32 twice(i32 n) noalloc => n * 2\nbool isA(char c) => c == 'a'\n" +
        "Shape pick(Shape s) {\n    switch s {\n        case _ except .circle: return s\n        default: return s\n    }\n}\n" +
        "add(a: i64, i64 b) -> i64 {\n    return a + b;\n}\n" +
        "void run(Arena arena) {\n    context (allocator: arena) {\n        print(\"x\")\n    }\n}\n" +
        "main() {\n    i64 t = add(1, b: 2); print($\"{t}\")\n    if t > 0 {\n        print(\"pos\")\n    }\n    else {\n        print(\"other\")\n    }\n" +
        "    let u: Untrusted<string> = Untrusted(\"x\")\n    print(trust(u, reason: \"checked\"))\n}\n";

    private const string Round3Canonical =
        "// Shapes.\nunion Shape {\n    circle(f64 r)\n    square(f64 side)\n}\nenum Size { // small\n    s\n    m // medium\n    l\n}\n" +
        "struct unique File {\n    i64 fd\n}\nunion error StockError {\n    outOfStock\n}\n" +
        "type distinct Percent = i32 requires it in 0..=100\n@derive(Json)\nstruct Parcel {\n    i64 weight\n}\n" +
        "i32 twice(i32 n) => n * 2\nbool isA(rune c) => c == 'a'\n" +
        "Shape pick(Shape s) {\n    switch s {\n        case not .circle: return s\n        default: return s\n    }\n}\n" +
        "i64 add(i64 a, i64 b) {\n    return a + b\n}\n" +
        "void run(Arena arena) {\n    provide allocator: arena {\n        print(\"x\")\n    }\n}\n" +
        "void main() {\n    let t: i64 = add(1, b: 2)\n    print($\"{t}\")\n    if t > 0 {\n        print(\"pos\")\n    } else {\n        print(\"other\")\n    }\n" +
        "    let u: Untrusted<string> = Untrusted(\"x\")\n    print(u.trustUnchecked() because \"checked\")\n}\n";

    [Fact]
    public void EveryRound3InputFormIsRewrittenOnce()
    {
        var file = Path.Combine(Fresh("round3-all"), "main.byn");
        File.WriteAllText(file, Round3InputForms);
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal(Round3Canonical, File.ReadAllText(file));
        var (again, stdout, _) = Run("format", "--check", file);
        Assert.Equal(CliApp.ExitOk, again);
        Assert.Empty(stdout);
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
    public void ARewriteThatDoesNotParseIsAbandoned()
    {
        var file = Path.Combine(Fresh("abandoned"), "main.byn");
        const string Text = "let a = true\nif (a) { print(\"x\") }\n";
        File.WriteAllText(file, Text);
        Format.RewriteHook = next => [.. next, .. "let broken = (\n"u8.ToArray()];
        try
        {
            var (exit, _, stderr) = Run("format", file);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("main.byn", stderr);
            Assert.Contains("rewrite was abandoned", stderr);
            Assert.DoesNotContain("broken", stderr);
            Assert.Equal(Text, File.ReadAllText(file));
        }
        finally { Format.RewriteHook = null; }
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
    public void AnUnreadableDirectoryIsReportedAndTheRestAreFormatted()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("io-dir");
        var locked = Directory.CreateDirectory(Path.Combine(dir, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "a.byn"), "let a = true;\n");
        var fixable = Path.Combine(dir, "b.byn");
        File.WriteAllText(fixable, "let a = true\nif (a) { print(\"x\") }\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            try
            {
                Directory.GetFiles(locked);
                return; // root reads it anyway
            }
            catch (UnauthorizedAccessException) { }
            var (exit, _, stderr) = Run("format", dir);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("locked", stderr);
            Assert.Equal("let a = true\nif a { print(\"x\") }\n", File.ReadAllText(fixable));
        }
        finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public void AnExecutableKeepsItsMode()
    {
        if (OperatingSystem.IsWindows()) return;
        var file = Path.Combine(Fresh("mode"), "script.byn");
        File.WriteAllText(file, "#!/usr/bin/env bayan\nlet a = true\nif (a) { print(\"x\") }\n");
        const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(file, Mode);
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Contains("if a {", File.ReadAllText(file));
        Assert.Equal(Mode, File.GetUnixFileMode(file));
    }

    [Fact]
    public void FormattingASymlinkUpdatesItsTarget()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("symlink");
        var target = Path.Combine(dir, "real.byn");
        File.WriteAllText(target, "let a = true\nif (a) { print(\"x\") }\n");
        var link = Path.Combine(dir, "link.byn");
        File.CreateSymbolicLink(link, "real.byn");
        var (exit, _, stderr) = Run("format", link);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true\nif a { print(\"x\") }\n", File.ReadAllText(target));
        Assert.Equal("real.byn", new FileInfo(link).LinkTarget);
    }

    [Fact]
    public async Task SymlinkCyclesAreWalkedOnce()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("cycle");
        File.WriteAllText(Path.Combine(dir, "main.byn"), "let a = true\nif (a) { print(\"x\") }\n");
        var sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(sub, "up"), "..");
        Directory.CreateSymbolicLink(Path.Combine(sub, "up2"), "..");
        File.CreateSymbolicLink(Path.Combine(dir, "alias.byn"), "main.byn");
        var result = await Task.Run(() => Run("format", "--check", dir)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CliApp.ExitFailure, result.Exit);
        Assert.Single(result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
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
