using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Every output shape of <c>docs/bayan-terminal-style.md</c> §4, rendered three ways into
/// <c>tests/cases/style/&lt;shape&gt;</c>: <c>.color</c> (a dark truecolour terminal), <c>.plain</c> (the same
/// with <c>NO_COLOR=1</c>) and <c>.ascii</c> (colour with <c>--ascii</c>). Stdout and stderr share one file.
/// </summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed partial class StyleTests
{
    public enum Rendering { Color, Plain, Ascii, AsciiPlain }

    private const int Width = 100;

    public static readonly string[] Shapes =
    [
        "diag-error-fix", "diag-removal-fix", "diag-secondary", "diag-cap", "diag-cap-all",
        "build-ok", "build-ok-cache", "build-ok-verbose", "build-fail", "build-ice", "check-ok", "run-fail",
        "test-ok", "test-fail", "test-pending", "test-verbose", "test-assert-values", "test-control-chars",
        "doctor-ok", "doctor-fail", "audit",
    ];

    public static TheoryData<string> AllShapes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var shape in Shapes) data.Add(shape);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllShapes))]
    public void RendersThreeWays(string shape)
    {
        foreach (var (rendering, extension) in (ReadOnlySpan<(Rendering, string)>)[(Rendering.Color, ".color"), (Rendering.Plain, ".plain"), (Rendering.Ascii, ".ascii")])
        {
            var result = Golden.Compare(Path.Combine(Golden.CasesRoot, "style", shape + extension), Render(shape, rendering), Golden.UpdateMode);
            if (result.Failure is { } failure) Assert.Fail(failure);
        }
    }

    /// <summary>One shape's output, masked, in a terminal environment set for <paramref name="rendering"/>.</summary>
    internal static string Render(string shape, Rendering rendering)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "style", shape);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var program = Path.Combine(dir, "tally");
        Directory.CreateDirectory(program);
        var cache = Path.Combine(Golden.RepoRoot, "tmp", "style-cache", shape);
        // A shape that shows the compile time builds into an empty cache, so it is never a hit.
        if (shape is "build-ok" or "build-ok-cache" or "build-ok-verbose" && Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        var ascii = rendering is Rendering.Ascii or Rendering.AsciiPlain;
        var plain = rendering is Rendering.Plain or Rendering.AsciiPlain;
        var text = "";
        CliEnvironmentTests.WithTerminalEnv(
            () => text = Mask(Produce(shape, program, ascii), dir),
            ("COLORTERM", "truecolor"), ("BAYAN_THEME", "dark"), ("CI", "1"), ("NO_COLOR", plain ? "1" : null), ("BAYAN_CACHE", cache));
        return text;
    }

    private const string Ok = "void main() {\n    print(\"hi\")\n}\n";

    private const string Broken = "void main() {\n    let x = 1\n    x = 2\n    print($\"{x}\")\n}\n";

    private static string Produce(string shape, string program, bool ascii)
    {
        var main = Path.Combine(program, "main.byn");
        var output = Path.Combine(program, Targets.ExecutableName("tally", CliApp.HostTarget));
        string Command(string source, params string[] args)
        {
            File.WriteAllText(main, source);
            return Run([.. ascii ? ["--ascii"] : Array.Empty<string>(), .. args]);
        }
        string Tests(string tests, params string[] args)
        {
            File.WriteAllText(Path.Combine(program, "main.test.byn"), tests);
            return Command(Ok, ["test", .. args, program]);
        }
        switch (shape)
        {
            case "diag-error-fix":
                return Command("void main() {\n    let total = 0\n    total += 1\n    print($\"{total}\")\n}\n", "check", main);
            case "diag-removal-fix":
                return Command("i64 twice(i64 x) => x * 2\n\nvoid main() {\n    let n = try twice(2)\n    print($\"{n}\")\n}\n", "check", main);
            case "diag-secondary":
                return Secondary(ascii);
            case "diag-cap":
            case "diag-cap-all":
                var many = "void main() {\n    var y = 0\n" + string.Concat(Enumerable.Range(1, 25).Select(i => $"    y = x{i}\n")) + "}\n";
                return shape == "diag-cap" ? Command(many, "check", main) : Command(many, "check", "--all", main);
            case "build-ok":
                return Command(Ok, "build", "-o", output, main);
            case "build-ok-cache":
                Command(Ok, "build", "-o", output, main);
                return Command(Ok, "build", "-o", output, main);
            case "build-ok-verbose":
                return Command(Ok, "build", "-v", "-o", output, main);
            case "build-fail":
                return Command(Broken, "build", "-o", output, main);
            case "build-ice":
                return Ice(ascii);
            case "check-ok":
                return Command(Ok, "check", main);
            case "run-fail":
                return Command(Broken, "run", main);
            case "test-ok":
                return Tests("test \"adds\" {\n    assert(1 + 1 == 2)\n}\n\ntest \"compares\" {\n    assert(\"a\" != \"b\")\n}\n");
            case "test-fail":
                return Tests("test \"adds\" {\n    assert(1 + 1 == 2)\n}\n\ntest \"is false\" {\n    assert(false)\n}\n\ntest \"panics\" {\n    panic(\"boom\")\n}\n");
            case "test-pending":
                return Tests("test \"adds\" {\n    assert(1 + 1 == 2)\n}\n\ntest \"later\" {\n    todo(\"split by weight\")\n}\n");
            case "test-verbose":
                return Tests("test \"adds\" {\n    assert(1 + 1 == 2)\n}\n\ntest \"later\" {\n    todo(\"split by weight\")\n}\n\ntest \"is false\" {\n    assert(false)\n}\n", "-v");
            case "test-assert-values":
                return Tests("i64 cents(i64 units) {\n    return units * 100\n}\n\ntest \"leftover cents\" {\n    let net = cents(2) - 1\n    assert(net == cents(2))\n}\n");
            case "test-control-chars":
                return Tests("test \"ok\\u{1b}]0;PWNED\\u{7}\" {\n    assert(true);\n}\n\ntest \"later\\u{1b}[2J\" {\n    todo(\"soon\");\n}\n\n"
                    + "test \"y\\u{1b}]0;PWNED\\u{7}\\u{1b}[2J\" {\n    panic(\"boom\\u{1b}[2J\\u{9b}31m\");\n}\n", "-v");
            case "doctor-ok":
                return DoctorShape(ascii, [
                    new(Doctor.Level.Ok, "bayan", $"{CliApp.Version} (commit a1b2c3d4e5f6)"),
                    new(Doctor.Level.Ok, "host", CliApp.HostTarget),
                    new(Doctor.Level.Ok, "C compiler", "zig cc (0.14.1)"),
                    new(Doctor.Level.Ok, "zig", "/usr/local/bin/zig (0.14.1)"),
                    new(Doctor.Level.Ok, "git", "/usr/bin/git (git version 2.47.0)"),
                    new(Doctor.Level.Ok, "cache", "~/.cache/bayan (writable, 3 programs, 1.2 MB)"),
                ]);
            case "doctor-fail":
                return DoctorShape(ascii, [
                    new(Doctor.Level.Ok, "bayan", $"{CliApp.Version} (commit a1b2c3d4e5f6)"),
                    new(Doctor.Level.Ok, "host", CliApp.HostTarget),
                    new(Doctor.Level.Fail, "C compiler", "none found", "install zig (https://ziglang.org), or set BAYAN_CC to a C compiler"),
                    new(Doctor.Level.Note, "zig", "not found; building for other targets needs it", "install zig (https://ziglang.org)"),
                    new(Doctor.Level.Ok, "git", "/usr/bin/git (git version 2.47.0)"),
                    new(Doctor.Level.Fail, "cache", "~/.cache/bayan isn't writable: Permission denied", "set BAYAN_CACHE to a writable directory"),
                ]);
            case "audit":
                return Command("@allow(BYN2230) because \"kept for the C ABI\"\nvoid toHex() {\n}\n\nunsafe void poke() {\n}\n", "audit", main);
            default:
                throw new ArgumentException($"unknown shape `{shape}`", nameof(shape));
        }
    }

    private static string Run(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, new CliApp.Terminal(true, true, Width));
        return $"# bayan {string.Join(' ', args)}\nexit: {exit}\n" + Streams(stdout.ToString(), stderr.ToString());
    }

    private static string Streams(string stdout, string stderr) => $"--- stdout ---\n{stdout}--- stderr ---\n{stderr}";

    /// <summary>The style <c>CliApp.Run</c> would pick for a terminal stream in the current environment.</summary>
    private static Style TerminalStyleFor(bool ascii) =>
        TerminalStyle.Decide("auto", ascii, isTty: true, Environment.GetEnvironmentVariable, Width);

    /// <summary>A diagnostic with a related span, which no binder site produces yet (handoff Q81): renderer only.</summary>
    private static string Secondary(bool ascii)
    {
        const string text = "i64 sum(Line line) {\n    let total = 0\n    total += line.price\n    return total\n}\n";
        var source = SourceText.FromString("src/ledger/ledger.byn", text);
        var use = text.IndexOf("total +=", StringComparison.Ordinal);
        var declared = text.IndexOf("total = 0", StringComparison.Ordinal);
        var diagnostic = new Diagnostic(Severity.Error, "BYN2104", source, new TextSpan(use, "total".Length), "`total` is immutable",
            Label: "cannot be changed",
            Help: "declare it with `var` if it should change",
            Fix: new Fix(new TextSpan(declared - "let ".Length, "let".Length), "var"),
            Related: [new RelatedSpan(new TextSpan(declared, "total".Length), "declared with `let` here")]);
        return "# DiagnosticRenderer.RenderAll\n" + Streams("", DiagnosticRenderer.RenderAll([diagnostic], includePath: true, TerminalStyleFor(ascii)));
    }

    /// <summary>Generated C the C compiler rejected (BYN4001), which no program should produce: reporting only.</summary>
    private static string Ice(bool ascii)
    {
        var rejected = new NativeBuild(null, "C compiler failed (exit 1):\nmain.byn:2:5: error: use of undeclared identifier 'l0_y'\n", Rejected: true);
        var stderr = new StringWriter();
        CliApp.ReportToolchain(rejected, stderr, json: false, TerminalStyleFor(ascii));
        return "# CliApp.ReportToolchain\n" + Streams("", stderr.ToString());
    }

    private static string DoctorShape(bool ascii, Doctor.Check[] checks)
    {
        var writer = new StringWriter();
        Doctor.Render(checks, TerminalStyleFor(ascii), writer);
        return "# Doctor.Render\n" + Streams(writer.ToString(), "");
    }

    [GeneratedRegex(@"\d+\.\d+s")]
    private static partial Regex Seconds();

    /// <summary>Replaces what differs between runs and machines: seconds, paths, host target, version.</summary>
    private static string Mask(string text, string dir)
    {
        text = text.ReplaceLineEndings("\n")
            .Replace(dir + Path.DirectorySeparatorChar, "<dir>/")
            .Replace(dir, "<dir>");
        text = Regex.Replace(text, @"<dir>/\S*", m => m.Value.Replace('\\', '/'))
            .Replace(Toolchain.CacheDirectory, "<cache>")
            .Replace(CliApp.HostTarget, "<host>")
            .Replace(CliApp.Version, "<version>")
            .Replace(".exe", "");
        return Seconds().Replace(text, "0.00s");
    }
}
