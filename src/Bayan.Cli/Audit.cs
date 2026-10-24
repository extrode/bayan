using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan audit &lt;path&gt;</c>: every <c>because</c> bypass (spec §4.4), <c>unsafe</c> and <c>extern</c>
/// declaration in a <c>.byn</c> file or a directory of them, with its reason, in file and source order.
/// Reads tokens only, so a file that does not compile is still audited.
/// </summary>
internal static class Audit
{
    private sealed record Bypass(int Line, int Column, string Construct, string? Reason);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Style style)
    {
        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            stderr.WriteLine(Messages.CommandUsage.Text("bayan audit <path>"));
            return CliApp.ExitUsage;
        }
        var path = args[0];
        var exit = CliApp.ExitOk;
        string[] files;
        if (Directory.Exists(path)) files = Format.BynFiles(path, stderr, ref exit);
        else if (File.Exists(path)) files = [path];
        else
        {
            stderr.WriteLine(Messages.NotFound.Text(path));
            return CliApp.ExitUsage;
        }
        Array.Sort(files, StringComparer.Ordinal);
        var count = 0;
        foreach (var file in files)
        {
            if (Path.GetFileName(file) == "package.byn") continue;
            SourceText source;
            try
            {
                source = SourceText.FromFile(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine(Messages.FormatIoError.Text(file, e.Message));
                exit = CliApp.ExitFailure;
                continue;
            }
            foreach (var b in Bypasses(source))
            {
                var reason = b.Reason is null ? style.Paint(Role.Warning, Messages.AuditNoReason.Text()) : Messages.AuditBecause.Text(b.Reason);
                stdout.WriteLine(Messages.AuditLine.Text(style.Paint(Role.Note, $"{file}:{b.Line}:{b.Column}"), b.Construct, reason));
                count++;
            }
        }
        var bypasses = (count == 1 ? Messages.AuditCountOne : Messages.AuditCountMany).Text(count);
        stdout.WriteLine(style.Paint(Role.Success, Messages.AuditSummary.Text(style.Symbols.Dot, bypasses, path)));
        return exit;
    }

    /// <summary>
    /// Each <c>because</c> with the construct before it (<c>@name(…)</c>, a call, <c>unsafe</c>) and its reason,
    /// and each <c>unsafe</c> or <c>extern</c> with none, read from the tokens.
    /// </summary>
    private static IEnumerable<Bypass> Bypasses(SourceText source)
    {
        var tokens = Lexer.Lex(source).Tokens.ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.BecauseKeyword && i > 0)
            {
                var (start, construct) = Construct(source, tokens, i - 1);
                var reason = i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.StringLiteral ? source.GetText(tokens[i + 1].Span) : null;
                var (line, column) = source.GetLinePosition(start);
                yield return new Bypass(line, column, construct, reason);
            }
            else if (token.Kind is TokenKind.UnsafeKeyword or TokenKind.ExternKeyword
                && !(i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.BecauseKeyword))
            {
                // The declaration's head: up to its body or the end of the line.
                var last = i;
                while (last + 1 < tokens.Length && !tokens[last + 1].HasNewlineBefore
                    && tokens[last + 1].Kind is not (TokenKind.OpenBrace or TokenKind.EndOfFile)) last++;
                var (line, column) = source.GetLinePosition(token.Start);
                yield return new Bypass(line, column, source.GetText(TextSpan.FromBounds(token.Start, tokens[last].End)), null);
            }
        }
    }

    /// <summary>The construct a <c>because</c> follows, ending at token <paramref name="end"/>, and where it starts.</summary>
    private static (int Start, string Text) Construct(SourceText source, Token[] tokens, int end)
    {
        var name = end;
        if (tokens[end].Kind == TokenKind.CloseParen)
        {
            var depth = 0;
            var open = end;
            for (; open >= 0; open--)
            {
                if (tokens[open].Kind == TokenKind.CloseParen) depth++;
                else if (tokens[open].Kind == TokenKind.OpenParen && --depth == 0) break;
            }
            if (open <= 0) return (tokens[end].Start, source.GetText(tokens[end].Span));
            name = open - 1;
            if (name == 0 || tokens[name - 1].Kind != TokenKind.At) return (tokens[name].Start, source.GetText(tokens[name].Span) + "()");
        }
        var start = name > 0 && tokens[name - 1].Kind == TokenKind.At ? tokens[name - 1].Start : tokens[name].Start;
        return (start, source.GetText(TextSpan.FromBounds(start, tokens[end].End)));
    }
}
