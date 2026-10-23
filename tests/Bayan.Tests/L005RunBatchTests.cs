using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// L005 (docs/laws/005-run-batch-escape-or-fail.md): <c>Io.runBatch</c> passes every argument unchanged or runs
/// nothing. Generated paths and argument lists go through the real <c>byn_batch_cmdline</c>
/// (<c>tests/native/batch-cmdline-cases.c</c>); each accepted command line is read back with a model of
/// <c>cmd.exe /d /e:ON /v:OFF /s /c</c> and the batch file's <c>%~1</c> + <c>shift</c> loop. Off Windows,
/// <c>tests/cases/run/io-run-batch</c> shows <c>unsupported</c> before the arguments are looked at.
/// </summary>
[Trait("Law", "L005")]
public sealed class L005RunBatchTests
{
    private const string Prefix = "cmd.exe /d /e:ON /v:OFF /s /c ";

    private static readonly string[] Chars =
    [
        "a", "Z", "q", "0", "9", " ", "\t",
        "&", "|", "<", ">", "(", ")", "^", "!", "\"", "%", ",", ";", "=", "\\", "/",
        "\r", "\n", "\0", "\u001a", "\v", "\f", "\u007f", "\u00a0", "\u00ff", "é", "ß", "中", "😀",
    ];

    private static readonly string[] SafeChars = Chars.Where(c => !IsRefused(c)).ToArray();

    private static readonly string[] Extensions = [".bat", ".cmd", ".BAT", ".Cmd", ".exe", ".bat.", ".cmdx", ""];

    private static readonly (string Path, string[] Args)[] Edges =
    [
        (".\\echo.cmd", ["a\"&calc"]),
        (".\\echo.cmd", ["%PATH%"]),
        (".\\echo.cmd", ["!PATH!"]),
        (".\\echo.cmd", ["a\nb"]),
        (".\\echo.cmd", ["a\rb"]),
        (".\\echo.cmd", ["a\0b"]),
        (".\\echo.cmd", ["é"]),
        (".\\echo.cmd", ["😀 中 ß"]),
        (".\\echo.cmd", [""]),
        (".\\echo.cmd", ["", ""]),
        (".\\echo.cmd", ["a\\"]),
        (".\\echo.cmd", ["\\"]),
        (".\\echo.cmd", ["^&|<>()!"]),
        (".\\echo.cmd", [",;= \t"]),
        (".\\echo.cmd", ["a,b", "c;d", "e=f"]),
        (".\\echo.cmd", []),
        (".\\echo.cmd", ["1", "2", "3", "4", "5", "6", "7", "8", "9", "ten", "eleven", "twelve"]),
        (".\\echo.cmd", ["1", "2", "3", "4", "5", "6", "7", "8", "9", "a b", "^&", "%"]),
        (".\\my dir\\a&b (1).bat", ["x"]),
        ("C:\\dir\\X.CMD", ["a"]),
        ("./run.Bat", ["a"]),
        ("/", []),
        ("\\.cmd", []),
        ("100%.bat", ["a"]),
        (".\\100%.bat", ["a"]),
        (".\\a\"b.bat", ["a"]),
        (".\\a\nb.bat", ["a"]),
        ("echo.cmd", ["a"]),
        ("echo.cmd.", ["a"]),
        (".\\echo.cmd.", ["a"]),
        (".\\echo.exe", ["a"]),
        (".cmd", []),
        ("", []),
    ];

    [Fact]
    public void EveryCaseIsRefusedOrReadBackUnchanged()
    {
        var cases = new List<(string Path, string[] Args)>(Edges);
        var random = new Random(5005);
        for (var i = 0; i < 4000; i++)
        {
            var alphabet = random.Next(2) == 0 ? SafeChars : Chars;
            var dir = Text(random, alphabet, 4);
            var separator = random.Next(10) switch { 0 => "", < 5 => "\\", _ => "/" };
            var path = dir + separator + Text(random, alphabet, 6) + Extensions[random.Next(Extensions.Length)];
            var args = new string[random.Next(13)];
            for (var j = 0; j < args.Length; j++) args[j] = Text(random, alphabet, 8);
            cases.Add((path, args));
        }

        var input = new StringBuilder();
        foreach (var (path, args) in cases)
            input.Append(Hex(path)).Append(string.Concat(args.Select(a => " " + Hex(a)))).Append('\n');
        var lines = Run(input.ToString()).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(cases.Count, lines.Length);

        var accepted = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            var (path, args) = cases[i];
            var shown = $"case {i}: path {Show(path)}, args [{string.Join(", ", args.Select(Show))}]";
            var refuse = IsRefused(path) || args.Any(IsRefused) || !IsBatchPath(path);
            if (lines[i] == "refused")
            {
                Assert.True(refuse, $"{shown}: refused without a reason");
                continue;
            }
            accepted++;
            var line = new UTF8Encoding(false, true).GetString(Convert.FromHexString(lines[i]));
            var (tokens, error) = Cmd(line);
            Assert.True(error is null, $"{shown}: {Show(line)}: {error}");
            Assert.Equal([path, .. args], tokens!);
            Assert.False(refuse, $"{shown}: accepted, but it must be refused");
        }
        // Both outcomes are exercised often.
        Assert.InRange(accepted, cases.Count / 10, cases.Count * 9 / 10);
    }

    [Theory]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a b\"\"", ".\\echo.cmd|a b")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"\"\"", ".\\echo.cmd|")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"^&|<>()!\"\"", ".\\echo.cmd|^&|<>()!")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a\\\"\"", ".\\echo.cmd|a\\")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"x\" \"y z\"\"", ".\\echo.cmd|x|y z")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\"\"", ".\\echo.cmd")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\"C:\\dir\\X.CMD\" \"a\"\"", "C:\\dir\\X.CMD|a")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a,b;c=d\"\"", ".\\echo.cmd|a,b;c=d")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" a,b;c=d\t e\"", ".\\echo.cmd|a|b|c|d|e")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a\"b\"", ".\\echo.cmd|a\"b")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"!x!\"\"", ".\\echo.cmd|!x!")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a\"&calc\"\"", "error: `&` outside quotes")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" a^&b\"", "error: `^` outside quotes")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" (a)\"", "error: `(` outside quotes")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"%PATH%\"\"", "error: `%` is expanded even inside quotes")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a\"", "error: unterminated quote")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \".\\echo.cmd\"", "error: `/s` strips the outer quotes, leaving an unquoted command")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c .\\echo.cmd", "error: no outer quotes for `/s` to strip")]
    [InlineData("cmd.exe /e:ON /v:OFF /s /c \"\".\\echo.cmd\"\"", "error: not the expected `cmd.exe /d /e:ON /v:OFF /s /c ` prefix")]
    [InlineData("cmd.exe /d /e:ON /v:ON /s /c \"\".\\echo.cmd\"\"", "error: not the expected `cmd.exe /d /e:ON /v:OFF /s /c ` prefix")]
    [InlineData("cmd.exe /d /e:ON /v:OFF /s /c \"\".\\echo.cmd\" \"a\nb\"\"", "error: NUL, CR or LF ends the command")]
    public void ModelReadsKnownCommandLines(string line, string expected)
    {
        var (tokens, error) = Cmd(line);
        Assert.Equal(expected, error is null ? string.Join("|", tokens!) : "error: " + error);
    }

    /// <summary>Not on Windows, a refused and an accepted path and argument list both fail with <c>unsupported</c>.</summary>
    [NotOnWindowsFact]
    public void RunBatchIsUnsupportedOffWindows()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "l005-run-batch-unsupported");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "main.byn");
        File.WriteAllText(path, """
            i32 refusedIsUnsupported() {
                string[1] refused = ["%PATH%"]
                let r = Io.runBatch("echo", refused.asSpan()) catch switch {
                    .unsupported => return 0,
                    _ => return 3,
                }
                return 4
            }

            i32 main() {
                let refused = refusedIsUnsupported()
                if refused != 0 { return refused }
                string[3] args = ["a b", "^&|<>()!", "é"]
                let r = Io.runBatch(".\\echo.cmd", args.asSpan()) catch switch {
                    .unsupported => return 0,
                    _ => return 2,
                }
                print($"exit {r.exitCode}")
                return 1
            }

            """);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["run", path], stdout, stderr);
        Assert.True(exit == CliApp.ExitOk, $"exit {exit}: {stderr}");
    }

    /// <summary>
    /// Reads <paramref name="line"/> as <c>cmd.exe /d /e:ON /v:OFF /s /c</c> would, then as a batch file reading
    /// <c>%~0</c> and <c>%~1</c> with a <c>shift</c> loop: the command and its arguments, or why the line is unsafe.
    /// <c>/d</c> skips AutoRun and <c>/v:OFF</c> makes <c>!</c> literal, so the prefix must be exactly
    /// <see cref="Prefix"/>.
    /// </summary>
    private static (List<string>? Tokens, string? Error) Cmd(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            return (null, $"not the expected `{Prefix}` prefix");
        var rest = line[Prefix.Length..];
        if (rest.IndexOfAny(['\0', '\r', '\n']) >= 0) return (null, "NUL, CR or LF ends the command");
        // Percent expansion runs before quotes are looked at.
        if (rest.Contains('%')) return (null, "`%` is expanded even inside quotes");
        // `/s`: the first and the last quote go, whatever is between them.
        if (rest.Length < 2 || rest[0] != '"' || rest[^1] != '"') return (null, "no outer quotes for `/s` to strip");
        var inner = rest[1..^1];
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        var inToken = false;
        foreach (var c in inner)
        {
            if (c == '"')
            {
                quoted = !quoted;
                token.Append(c);
                inToken = true;
            }
            else if (quoted)
                token.Append(c);
            else if ("&|<>()^".Contains(c))
                return (null, $"`{c}` outside quotes");
            else if (c is ' ' or '\t' or ',' or ';' or '=')
            {
                if (inToken) tokens.Add(token.ToString());
                token.Clear();
                inToken = false;
            }
            else
            {
                token.Append(c);
                inToken = true;
            }
        }
        if (quoted) return (null, "unterminated quote");
        if (inToken) tokens.Add(token.ToString());
        if (tokens.Count == 0 || !tokens[0].StartsWith('"'))
            return (null, "`/s` strips the outer quotes, leaving an unquoted command");
        // `%~N`: one leading and one trailing quote are removed.
        return (tokens.Select(t =>
        {
            if (t.StartsWith('"')) t = t[1..];
            if (t.EndsWith('"')) t = t[..^1];
            return t;
        }).ToList(), null);
    }

    private static bool IsRefused(string s) => s.IndexOfAny(['\0', '\r', '\n', '"', '%']) >= 0;

    private static bool IsBatchPath(string path) =>
        path.IndexOfAny(['/', '\\']) >= 0
        && (path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));

    private static string Text(Random random, string[] alphabet, int max)
    {
        var sb = new StringBuilder();
        for (var n = random.Next(max + 1); n > 0; n--) sb.Append(alphabet[random.Next(alphabet.Length)]);
        return sb.ToString();
    }

    private static string Hex(string s) => s.Length == 0 ? "-" : Convert.ToHexStringLower(Encoding.UTF8.GetBytes(s));

    private static string Show(string s) => System.Text.Json.JsonSerializer.Serialize(s,
        new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static string Run(string input)
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "batch-cmdline-cases.c"));
        var build = Toolchain.Build(driver, BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        p.StandardInput.Write(input);
        p.StandardInput.Close();
        var output = stdout.Result;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return output;
    }

    private sealed class NotOnWindowsFactAttribute : FactAttribute
    {
        public NotOnWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Io.runBatch runs cmd.exe on Windows (RuntimeNativeTests.RunBatchRoundTrips)";
        }
    }
}
