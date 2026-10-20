using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

public static class CliApp
{
    public const string Version = "0.1.0-dev";

    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    private const string Usage = """
        usage: bayan <command> [args]

        commands:
          lex <file>           print the token stream
          parse [--ast] <file> check syntax; with --ast print the syntax tree
          explain <code>       explain a diagnostic, e.g. `bayan explain BYN0003`
          --version            print the compiler version
          --help               print this message
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        var rest = args[1..];
        switch (args[0])
        {
            case "--version":
            case "version":
                stdout.WriteLine($"bayan {Version}");
                return ExitOk;
            case "--help":
            case "-h":
            case "help":
                stdout.WriteLine(Usage);
                return ExitOk;
            case "lex":
                return Lex(rest, stdout, stderr);
            case "parse":
                return Parse(rest, stdout, stderr);
            case "explain":
                return Explain(rest, stdout, stderr);
            default:
                stderr.WriteLine($"bayan: unknown command `{args[0]}`");
                stderr.WriteLine(Usage);
                return ExitUsage;
        }
    }

    private static int Lex(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryReadSingleFile(args, stderr, out var source)) return ExitUsage;
        var result = Lexer.Lex(source);
        stdout.Write(TokenDump.Dump(result));
        return result.Diagnostics.Any(d => d.IsError) ? ExitFailure : ExitOk;
    }

    private static int Parse(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var printAst = args.Contains("--ast");
        if (!TryReadSingleFile(args.Where(a => a != "--ast").ToArray(), stderr, out var source)) return ExitUsage;
        var tree = SyntaxTree.Parse(source);
        if (printAst)
        {
            stdout.Write(AstDump.Dump(tree));
        }
        else if (tree.Diagnostics.Count > 0)
        {
            stderr.Write(DiagnosticRenderer.RenderAll(tree.Diagnostics, includePath: true));
        }
        return tree.Diagnostics.Any(d => d.IsError) ? ExitFailure : ExitOk;
    }

    private static int Explain(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 1)
        {
            stderr.WriteLine("bayan: expected one diagnostic code, e.g. `bayan explain BYN0003`");
            return ExitUsage;
        }
        var text = ExplainText(args[0]);
        if (text is null)
        {
            stderr.WriteLine($"bayan: no explanation for `{args[0]}`");
            return ExitFailure;
        }
        stdout.Write(text);
        return ExitOk;
    }

    /// <summary>The embedded <c>docs/diagnostics/BYN####.md</c> for a code, matched case-insensitively.</summary>
    public static string? ExplainText(string code)
    {
        var name = code.Trim().ToUpperInvariant();
        if (name.Length != 7 || !name.StartsWith("BYN", StringComparison.Ordinal) || name.AsSpan(3).ContainsAnyExceptInRange('0', '9'))
            return null;
        using var stream = typeof(CliApp).Assembly.GetManifestResourceStream($"diagnostics/{name}.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool TryReadSingleFile(string[] args, TextWriter stderr, out SourceText source)
    {
        source = null!;
        if (args.Length != 1)
        {
            stderr.WriteLine("bayan: expected exactly one file");
            return false;
        }
        if (!File.Exists(args[0]))
        {
            stderr.WriteLine($"bayan: file not found: {args[0]}");
            return false;
        }
        source = SourceText.FromFile(args[0]);
        return true;
    }
}
