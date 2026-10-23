using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

/// <summary><c>bayan format &lt;path&gt;</c>: rewrites the input forms in <c>.byn</c> files by applying their diagnostics' fixes.</summary>
internal static class Format
{
    /// <summary>The input-form codes whose fix the formatter applies.</summary>
    private static readonly HashSet<string> InputForms = new(StringComparer.Ordinal)
    {
        "BYN1007", "BYN1017", "BYN1018", "BYN1019", "BYN1020", "BYN1021", "BYN1022", "BYN1023", "BYN1025",
        "BYN1029", "BYN1030", "BYN1031", "BYN1033",
        "BYN1040", "BYN1041", "BYN1042", "BYN1053",
    };

    private const int MaxPasses = 10;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            stderr.WriteLine(Messages.CommandUsage.Text("bayan format <path>"));
            return CliApp.ExitUsage;
        }
        var path = args[0];
        string[] files;
        if (Directory.Exists(path)) files = Directory.GetFiles(path, "*.byn", SearchOption.AllDirectories);
        else if (File.Exists(path)) files = [path];
        else
        {
            stderr.WriteLine(Messages.NotFound.Text(path));
            return CliApp.ExitUsage;
        }
        Array.Sort(files, StringComparer.Ordinal);
        var exit = CliApp.ExitOk;
        foreach (var file in files)
        {
            var original = File.ReadAllBytes(file);
            var formatted = FormatFile(file, original, stderr, out var unfixable);
            if (formatted is null) exit = CliApp.ExitFailure;
            else
            {
                if (unfixable) exit = CliApp.ExitFailure;
                if (!formatted.AsSpan().SequenceEqual(original))
                {
                    File.WriteAllBytes(file, formatted);
                    stdout.WriteLine(Messages.FormatFormatted.Text(file));
                }
            }
        }
        return exit;
    }

    /// <summary>The file's bytes with every input-form fix applied; null after reporting a blocking error. An input-form diagnostic left with no fix is reported and sets <paramref name="unfixable"/>.</summary>
    private static byte[]? FormatFile(string file, byte[] bytes, TextWriter stderr, out bool unfixable)
    {
        unfixable = false;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var tree = SyntaxTree.Parse(new SourceText(file, bytes));
            var blocking = tree.Diagnostics.Where(d => d.IsError && !InputForms.Contains(d.Code)).ToList();
            if (blocking.Count > 0)
            {
                stderr.Write(DiagnosticRenderer.RenderAll(blocking, includePath: true));
                return null;
            }
            var fixes = tree.Diagnostics.Where(d => InputForms.Contains(d.Code) && d.Fix is not null)
                .Select(d => d.Fix!).OrderBy(f => f.Span.Start).ToList();
            if (fixes.Count == 0)
            {
                var fixless = tree.Diagnostics.Where(d => InputForms.Contains(d.Code)).ToList();
                if (fixless.Count > 0)
                {
                    stderr.Write(DiagnosticRenderer.RenderAll(fixless, includePath: true));
                    unfixable = true;
                }
                break;
            }
            var result = new List<byte>(bytes.Length);
            var at = 0;
            foreach (var fix in fixes)
            {
                if (fix.Span.Start < at) continue;
                result.AddRange(bytes.AsSpan(at, fix.Span.Start - at));
                result.AddRange(System.Text.Encoding.UTF8.GetBytes(fix.Replacement));
                at = fix.Span.End;
            }
            result.AddRange(bytes.AsSpan(at));
            bytes = [.. result];
        }
        return bytes;
    }
}
