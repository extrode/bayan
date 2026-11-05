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
        "BYN1028", "BYN1029", "BYN1031", "BYN1033",
        "BYN1040", "BYN1041", "BYN1042", "BYN1043", "BYN1044", "BYN1045", "BYN1047", "BYN1048", "BYN1050", "BYN1051", "BYN1053", "BYN1055", "BYN1057", "BYN1062",
        "BYN1058", "BYN1059", "BYN1060", "BYN1061", "BYN1063", "BYN1065", "BYN1066", "BYN1068",
        "BYN6001", "BYN6006", "BYN7006",
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

    /// <summary>Test-only: replaces each pass's rewrite, to force one that does not parse.</summary>
    internal static Func<byte[], byte[]>? RewriteHook;

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
        var exit = CliApp.ExitOk;
        string[] files;
        if (Directory.Exists(path)) files = BynFiles(path, stderr, ref exit, RealPath(path));
        else if (File.Exists(path)) files = [path];
        else
        {
            stderr.WriteLine(Messages.NotFound.Text(path));
            return CliApp.ExitUsage;
        }
        Array.Sort(files, StringComparer.Ordinal);
        var manifests = new Dictionary<string, (Manifest Manifest, string Dir)?>(StringComparer.Ordinal);
        var packageNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                if (Path.GetFileName(file) == "package.byn") continue;
                var package = ManifestFor(file, manifests);
                var manifest = package?.Manifest;
                if (manifest is { HasErrors: true })
                {
                    stderr.Write(DiagnosticRenderer.RenderAll(manifest.Diagnostics, includePath: true));
                    exit = CliApp.ExitFailure;
                    continue;
                }
                var booleans = manifest?.Booleans ?? BooleanStyle.Symbols;
                var primitives = manifest?.Primitives ?? PrimitiveStyle.Sized;
                var declared = package is { Dir: var root }
                    ? packageNames.TryGetValue(root, out var known) ? known : packageNames[root] = PackageNames(root)
                    : [];
                // Read and write the file the kernel opens, not the one a lexical `..` collapse names.
                var real = RealPath(file);
                var original = File.ReadAllBytes(real);
                var formatted = FormatFile(file, original, booleans, primitives, declared, stderr, out var unfixable);
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
                            WriteReplacing(real, formatted);
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

    /// <summary>
    /// Every <c>.byn</c> file under <paramref name="dir"/>. A directory or entry that cannot be read is reported, sets
    /// <paramref name="exit"/> to the failure code, and is skipped. With <paramref name="within"/>, a file or directory
    /// whose resolved path lies outside that resolved path is neither listed nor entered.
    /// Dot-directories, <c>node_modules</c>, <c>bin</c>, <c>obj</c>, <c>dist</c> and <c>tmp</c> are not entered.
    /// </summary>
    internal static string[] BynFiles(string dir, TextWriter stderr, ref int exit, string? within = null)
    {
        var prefix = within is null || Path.EndsInDirectorySeparator(within) ? within : within + Path.DirectorySeparatorChar;
        bool Inside(string real) => prefix is null || real == within || real.StartsWith(prefix, StringComparison.Ordinal);
        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var seenDirs = new HashSet<string>(StringComparer.Ordinal);
        var failed = false;
        void Report(string path, Exception e)
        {
            stderr.WriteLine(Messages.FormatIoError.Text(path, e.Message));
            failed = true;
        }
        // Each directory travels as its path as written and its canonical path, which no symlink crosses.
        var pending = new Stack<(string Path, string? Canonical)>([(dir, null)]);
        while (pending.TryPop(out var at))
        {
            string canonical;
            string[] dirFiles, subs;
            try
            {
                canonical = at.Canonical ?? RealPath(at.Path);
                if (!seenDirs.Add(canonical)) continue;
                dirFiles = Directory.GetFiles(canonical, "*.byn");
                subs = Directory.GetDirectories(canonical);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Report(at.Path, e);
                continue;
            }
            foreach (var name in dirFiles.Select(Path.GetFileName))
            {
                var file = Path.Combine(at.Path, name!);
                try
                {
                    var real = RealPath(Path.Combine(canonical, name!));
                    if (Inside(real) && seenFiles.Add(real)) files.Add(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Report(file, e); }
            }
            foreach (var name in subs.Select(Path.GetFileName))
            {
                if (IsSkippedDirectory(name!)) continue;
                var sub = Path.Combine(at.Path, name!);
                try
                {
                    var real = RealPath(Path.Combine(canonical, name!));
                    if (Inside(real)) pending.Push((sub, real));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Report(sub, e); }
            }
        }
        if (failed) exit = CliApp.ExitFailure;
        return [.. files];
    }

    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", "tmp" };

    /// <summary>The directories the language server skips: dot-directories and build or scratch output.</summary>
    private static bool IsSkippedDirectory(string name) => name.StartsWith('.') || SkippedDirs.Contains(name);

    /// <summary>The most symlinks <see cref="RealPath"/> follows, as the kernel's own limit: 32 on macOS, 40 elsewhere.</summary>
    private static readonly int MaxLinks = OperatingSystem.IsMacOS() ? 32 : 40;

    /// <summary>
    /// The absolute path with every symlink in it resolved as the kernel does: component by component, each link's
    /// own target joined to the resolved directory holding it, and <c>..</c> stepping back from the resolved prefix.
    /// </summary>
    internal static string RealPath(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        var current = Path.GetPathRoot(full)!;
        var rest = new Stack<string>();
        void PushParts(string text)
        {
            var parts = text.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--) rest.Push(parts[i]);
        }
        PushParts(full[current.Length..]);
        var links = 0;
        while (rest.TryPop(out var part))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }
            var next = Path.Combine(current, part);
            if (new FileInfo(next).LinkTarget is not { } target)
            {
                current = next;
                continue;
            }
            if (++links > MaxLinks) throw new IOException("Too many levels of symbolic links");
            if (Path.IsPathRooted(target)) current = Path.GetPathRoot(target)!;
            PushParts(target[Path.GetPathRoot(target)!.Length..]);
        }
        return current;
    }

    /// <summary>
    /// The <c>package.byn</c> in the file's directory or the nearest one above it, with its directory; null when
    /// there is none.
    /// </summary>
    private static (Manifest Manifest, string Dir)? ManifestFor(string file, Dictionary<string, (Manifest Manifest, string Dir)?> cache)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        var seen = new List<string>();
        (Manifest Manifest, string Dir)? found = null;
        for (; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (cache.TryGetValue(dir, out found)) break;
            seen.Add(dir);
            var candidate = Path.Combine(dir, "package.byn");
            if (File.Exists(candidate))
            {
                found = (Manifest.Read(candidate), dir);
                break;
            }
        }
        foreach (var d in seen) cache[d] = found;
        return found;
    }

    /// <summary>
    /// Every name the package's <c>.byn</c> files under <paramref name="dir"/> use other than as a type, callee or
    /// receiver, which includes every name they declare. A file or directory link resolving outside the package is not
    /// followed. A file that cannot be read is reported when it is formatted.
    /// </summary>
    private static HashSet<string> PackageNames(string dir)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ignored = CliApp.ExitOk;
        string root;
        try
        {
            root = RealPath(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return names; }
        foreach (var file in BynFiles(dir, TextWriter.Null, ref ignored, root))
        {
            if (Path.GetFileName(file) == "package.byn") continue;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            names.UnionWith(OtherUses(file, bytes, SyntaxTree.Parse(new SourceText(file, bytes))));
        }
        return names;
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to a temp file beside <paramref name="file"/>, already resolved by
    /// <see cref="RealPath"/>, gives it the original's Unix mode, then renames it over the original; a symlink to it
    /// stays a symlink.
    /// </summary>
    private static void WriteReplacing(string file, byte[] bytes)
    {
        var temp = Path.Combine(Path.GetDirectoryName(file)!, Path.GetRandomFileName() + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, File.GetUnixFileMode(file));
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
        IReadOnlySet<string> declared, TextWriter stderr, out bool unfixable)
    {
        unfixable = false;
        var converged = false;
        bool Applies(Diagnostic d) => InputForms.Contains(d.Code);
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var tree = SyntaxTree.Parse(new SourceText(file, bytes));
            var blocking = tree.Diagnostics.Where(d => d.IsError && !InputForms.Contains(d.Code)).ToList();
            if (blocking.Count > 0)
            {
                // After a rewrite the positions and snippets would point at text the file on disk does not hold.
                if (pass == 0) stderr.Write(DiagnosticRenderer.RenderAll(blocking, includePath: true));
                else stderr.WriteLine(Messages.FormatRewriteAbandoned.Text(file));
                return null;
            }
            var fixes = tree.Diagnostics.Where(d => Applies(d) && d.Fix is not null).Select(d => d.Fix!).ToList();
            var next = fixes.Count > 0 ? ApplyFixes(bytes, fixes) : Restyle(file, bytes, tree, booleans, primitives, declared);
            if (RewriteHook is not null) next = RewriteHook(next);
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
    private static byte[] Restyle(string file, byte[] bytes, SyntaxTree tree, BooleanStyle booleans, PrimitiveStyle primitives,
        IReadOnlySet<string> declared)
    {
        var names = primitives == PrimitiveStyle.Sized ? CSharpPrimitives : SizedPrimitives;
        var tokens = Lexer.Lex(new SourceText(file, bytes)).Tokens;
        // Only a type, or a callee or receiver (`int(x)`, `i64.max`), is renamed: a field, binding, label or member
        // spelled like a primitive keeps its name. A callee or receiver is kept when the file, or another file of
        // its package, uses the spelling as anything else, since it may then be that binding.
        var typeNames = AstDump.TypeNames(tree).Select(n => n.Span.Start).ToHashSet();
        var calleeNames = AstDump.CalleeAndReceiverNames(tree).Select(n => n.Span.Start).ToHashSet();
        var otherUses = OtherUses(file, bytes, tree);
        otherUses.UnionWith(declared);
        var renames = new List<Fix>();
        var operators = new List<Fix>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Identifier && (typeNames.Contains(token.Start) || calleeNames.Contains(token.Start))
                && System.Text.Encoding.UTF8.GetString(bytes, token.Start, token.Length) is var text
                && names.TryGetValue(text, out var name)
                && (typeNames.Contains(token.Start) || !otherUses.Contains(text)))
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

    /// <summary>Every identifier the file spells other than as a type, a callee or receiver, or a member after a dot.</summary>
    private static HashSet<string> OtherUses(string file, byte[] bytes, SyntaxTree tree)
    {
        var tokens = Lexer.Lex(new SourceText(file, bytes)).Tokens;
        var typeNames = AstDump.TypeNames(tree).Select(n => n.Span.Start).ToHashSet();
        var calleeNames = AstDump.CalleeAndReceiverNames(tree).Select(n => n.Span.Start).ToHashSet();
        var uses = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t.Kind != TokenKind.Identifier || typeNames.Contains(t.Start) || calleeNames.Contains(t.Start)) continue;
            if (i > 0 && tokens[i - 1].Kind is TokenKind.Dot or TokenKind.QuestionDot) continue;
            uses.Add(System.Text.Encoding.UTF8.GetString(bytes, t.Start, t.Length));
        }
        return uses;
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
