using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan format [--check] &lt;path&gt;</c>: rewrites the input forms in <c>.byn</c> files by applying their
/// diagnostics' fixes, then the boolean and primitive spellings to the nearest <c>package.byn</c>'s <c>style</c>.
/// </summary>
internal static class Format
{
    /// <summary>The input-form codes whose fix the formatter applies.</summary>
    private static readonly HashSet<string> InputForms = new(StringComparer.Ordinal)
    {
        "BYN1007", "BYN1017", "BYN1018", "BYN1019", "BYN1020", "BYN1021", "BYN1022", "BYN1023", "BYN1025",
        "BYN1029", "BYN1030", "BYN1031", "BYN1033",
        "BYN1040", "BYN1041", "BYN1042", "BYN1043", "BYN1044", "BYN1045", "BYN1047", "BYN1048", "BYN1050", "BYN1051", "BYN1053", "BYN1062",
        "BYN6001", "BYN6006",
    };

    /// <summary>C# primitive names and the sized names they mean (spec §3.1).</summary>
    private static readonly Dictionary<string, string> CSharpPrimitives = new(StringComparer.Ordinal)
    {
        ["sbyte"] = "i8", ["byte"] = "u8", ["short"] = "i16", ["ushort"] = "u16", ["int"] = "i32", ["uint"] = "u32",
        ["long"] = "i64", ["ulong"] = "u64", ["nint"] = "isize", ["nuint"] = "usize", ["float"] = "f32", ["double"] = "f64",
    };

    private static readonly Dictionary<string, string> SizedPrimitives =
        CSharpPrimitives.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    internal static int MaxPasses = 10;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var check = args.Contains("--check");
        args = args.Where(a => a != "--check").ToArray();
        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            stderr.WriteLine(Messages.CommandUsage.Text("bayan format [--check] <path>"));
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
        var manifests = new Dictionary<string, Manifest?>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                if (Path.GetFileName(file) == "package.byn") continue;
                var manifest = ManifestFor(file, manifests);
                if (manifest is { HasErrors: true })
                {
                    stderr.Write(DiagnosticRenderer.RenderAll(manifest.Diagnostics, includePath: true));
                    exit = CliApp.ExitFailure;
                    continue;
                }
                var booleans = manifest?.Booleans ?? BooleanStyle.Symbols;
                var primitives = manifest?.Primitives ?? PrimitiveStyle.Sized;
                var original = File.ReadAllBytes(file);
                var formatted = FormatFile(file, original, booleans, primitives, stderr, out var unfixable);
                if (formatted is null) exit = CliApp.ExitFailure;
                else
                {
                    if (unfixable) exit = CliApp.ExitFailure;
                    if (!formatted.AsSpan().SequenceEqual(original))
                    {
                        if (check)
                        {
                            stdout.WriteLine(Messages.FormatWouldFormat.Text(file));
                            exit = CliApp.ExitFailure;
                        }
                        else
                        {
                            WriteReplacing(file, formatted);
                            stdout.WriteLine(Messages.FormatFormatted.Text(file));
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine(Messages.FormatIoError.Text(file, e.Message));
                exit = CliApp.ExitFailure;
            }
        }
        return exit;
    }

    /// <summary>The <c>package.byn</c> in the file's directory or the nearest one above it; null when there is none.</summary>
    private static Manifest? ManifestFor(string file, Dictionary<string, Manifest?> cache)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        var seen = new List<string>();
        Manifest? found = null;
        for (; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (cache.TryGetValue(dir, out found)) break;
            seen.Add(dir);
            var candidate = Path.Combine(dir, "package.byn");
            if (File.Exists(candidate))
            {
                found = Manifest.Read(candidate);
                break;
            }
        }
        foreach (var d in seen) cache[d] = found;
        return found;
    }

    /// <summary>Writes <paramref name="bytes"/> to a temp file beside <paramref name="file"/>, then renames it over the original.</summary>
    private static void WriteReplacing(string file, byte[] bytes)
    {
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetRandomFileName() + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    /// <summary>The bytes with each distinct fix applied in order; a fix overlapping an earlier one is skipped.</summary>
    internal static byte[] ApplyFixes(byte[] bytes, IEnumerable<Fix> fixes)
    {
        var result = new List<byte>(bytes.Length);
        var at = 0;
        foreach (var fix in fixes.Distinct().OrderBy(f => f.Span.Start))
        {
            if (fix.Span.Start < at) continue;
            result.AddRange(bytes.AsSpan(at, fix.Span.Start - at));
            result.AddRange(System.Text.Encoding.UTF8.GetBytes(fix.Replacement));
            at = fix.Span.End;
        }
        result.AddRange(bytes.AsSpan(at));
        return [.. result];
    }

    /// <summary>
    /// The file's bytes with every input-form fix applied, then the project's spellings; null after reporting a
    /// blocking error. An input-form diagnostic left with no fix is reported and sets <paramref name="unfixable"/>.
    /// </summary>
    private static byte[]? FormatFile(string file, byte[] bytes, BooleanStyle booleans, PrimitiveStyle primitives,
        TextWriter stderr, out bool unfixable)
    {
        unfixable = false;
        var converged = false;
        // Under `.csharp` the C# names are the project's spelling, so their input-form fix is not applied.
        bool Applies(Diagnostic d) => InputForms.Contains(d.Code) && !(primitives == PrimitiveStyle.Csharp && d.Code == "BYN1030");
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var tree = SyntaxTree.Parse(new SourceText(file, bytes));
            var blocking = tree.Diagnostics.Where(d => d.IsError && !InputForms.Contains(d.Code)).ToList();
            if (blocking.Count > 0)
            {
                stderr.Write(DiagnosticRenderer.RenderAll(blocking, includePath: true));
                return null;
            }
            var fixes = tree.Diagnostics.Where(d => Applies(d) && d.Fix is not null).Select(d => d.Fix!).ToList();
            var next = fixes.Count > 0 ? ApplyFixes(bytes, fixes) : Restyle(file, bytes, tree, booleans, primitives);
            if (next.AsSpan().SequenceEqual(bytes))
            {
                var fixless = tree.Diagnostics.Where(Applies).ToList();
                if (fixless.Count > 0)
                {
                    stderr.Write(DiagnosticRenderer.RenderAll(fixless, includePath: true));
                    unfixable = true;
                }
                converged = true;
                break;
            }
            bytes = next;
        }
        if (!converged)
        {
            stderr.WriteLine(Messages.FormatNotConverged.Text(file, MaxPasses));
            unfixable = true;
        }
        return bytes;
    }

    /// <summary>
    /// The bytes with primitive type names and boolean operators in the project's spelling (spec §3.1, §7.2).
    /// A boolean rewrite is kept only when the file parses to the same tree after it, so <c>and</c>, <c>or</c> and
    /// <c>not</c> in patterns, and a <c>!</c> that is not the operator, stay as written.
    /// </summary>
    private static byte[] Restyle(string file, byte[] bytes, SyntaxTree tree, BooleanStyle booleans, PrimitiveStyle primitives)
    {
        var names = primitives == PrimitiveStyle.Sized ? CSharpPrimitives : SizedPrimitives;
        var tokens = Lexer.Lex(new SourceText(file, bytes)).Tokens;
        var renames = new List<Fix>();
        var operators = new List<Fix>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Identifier
                && names.TryGetValue(System.Text.Encoding.UTF8.GetString(bytes, token.Start, token.Length), out var name)
                && (i == 0 || tokens[i - 1].Kind is not (TokenKind.Dot or TokenKind.QuestionDot)))
                renames.Add(new Fix(new TextSpan(token.Start, token.Length), name));
            else if (OperatorFix(bytes, tokens, i, booleans) is { } fix)
                operators.Add(fix);
        }
        // The operators' offsets move with a rename, so they wait for the next pass.
        if (renames.Count > 0) return ApplyFixes(bytes, renames);
        if (operators.Count == 0) return bytes;
        var shape = Shape(tree);
        var all = ApplyFixes(bytes, operators);
        if (Shape(SyntaxTree.Parse(new SourceText(file, all))) == shape) return all;
        // Some operator is not a boolean one: try each alone, last first, so earlier offsets stay valid.
        for (var i = operators.Count - 1; i >= 0; i--)
        {
            var one = ApplyFixes(bytes, [operators[i]]);
            if (Shape(SyntaxTree.Parse(new SourceText(file, one))) == shape) bytes = one;
        }
        return bytes;
    }

    /// <summary>The rewrite of token <paramref name="i"/> into the other boolean spelling, or null when it is not one.</summary>
    private static Fix? OperatorFix(byte[] bytes, ReadOnlySpan<Token> tokens, int i, BooleanStyle booleans)
    {
        var token = tokens[i];
        var span = new TextSpan(token.Start, token.Length);
        bool SpaceBefore() => token.Start > 0 && bytes[token.Start - 1] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
        bool SpaceAfter() => token.End < bytes.Length && bytes[token.End] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
        if (booleans == BooleanStyle.Words)
        {
            var word = token.Kind switch
            {
                TokenKind.AmpAmp => "and",
                TokenKind.BarBar => "or",
                TokenKind.Bang => "not",
                _ => null,
            };
            if (word is null) return null;
            var before = token.Kind != TokenKind.Bang && !SpaceBefore() ? " " : "";
            return new Fix(span, before + word + (SpaceAfter() ? "" : " "));
        }
        var symbol = token.Kind switch
        {
            TokenKind.AndKeyword => "&&",
            TokenKind.OrKeyword => "||",
            TokenKind.NotKeyword => "!",
            _ => null,
        };
        if (symbol is null) return null;
        if (token.Kind != TokenKind.NotKeyword) return new Fix(span, symbol);
        // `not x` becomes `!x`: the blanks up to the operand go too, unless a comment or line break is there.
        var next = tokens[i + 1];
        for (var at = token.End; at < next.Start; at++)
            if (bytes[at] is not ((byte)' ' or (byte)'\t')) return null;
        return new Fix(TextSpan.FromBounds(token.Start, next.Start), symbol);
    }

    /// <summary>The tree's dump and its diagnostic codes, which a respelling must leave unchanged.</summary>
    private static string Shape(SyntaxTree tree)
    {
        var dump = AstDump.Dump(tree);
        var diagnostics = tree.Diagnostics.Count > 0 ? "\n" + DiagnosticRenderer.RenderAll(tree.Diagnostics) : "";
        return dump[..^diagnostics.Length] + string.Join(" ", tree.Diagnostics.Select(d => d.Code));
    }
}
