namespace Bayan.LanguageServer.Analysis;

public readonly record struct Diagnostic(int Start, int Length, string Message, string? Code, int Severity);

/// <summary>One `.byn` file: text, line table and analysis results.</summary>
public sealed class Document
{
    public string Uri { get; }
    public string Text { get; private set; } = "";
    public int Version { get; private set; }
    public bool IsOpen { get; set; }
    public int[] LineStarts { get; private set; } = [0];
    public List<Token> Tokens { get; private set; } = [];
    public List<Symbol> Symbols { get; private set; } = [];
    public List<Symbol> Roots { get; private set; } = [];
    public List<Diagnostic> Diagnostics { get; private set; } = [];

    /// <summary>Called after every analysis so the workspace can drop its symbol index.</summary>
    internal Action? Analyzed;

    public Document(string uri, string text, int version) { Uri = uri; Set(text, version); }

    public void Set(string text, int version)
    {
        if (!ReferenceEquals(text, Text)) Replace(text);
        Version = version;
        Analyze();
    }

    /// <summary>Replaces the text without analysing (full-sync change inside a batch).</summary>
    public void Replace(string text)
    {
        Text = text;
        LineStarts = ComputeLines(text);
    }

    /// <summary>Applies one LSP incremental change (UTF-16 line/character range).</summary>
    public void Apply(int startLine, int startChar, int endLine, int endChar, string newText)
    {
        int a = Offset(startLine, startChar), b = Offset(endLine, endChar);
        Text = string.Concat(Text.AsSpan(0, a), newText, Text.AsSpan(b));
        LineStarts = ComputeLines(Text);
    }

    public void Analyze()
    {
        var lx = Lexer.Run(Text);
        Tokens = lx.Tokens;
        var ix = Indexer.Run(Text, lx.Tokens);
        Symbols = ix.Symbols;
        Roots = ix.Roots;
        var diags = new List<Diagnostic>(lx.Problems.Count);
        foreach (var p in lx.Problems) diags.Add(new Diagnostic(p.Start, p.Length, p.Message, p.Code, p.Code == "BYN1047" ? 2 : 1));
        Diagnostics = diags;
        Analyzed?.Invoke();
    }

    static int[] ComputeLines(string s)
    {
        var span = s.AsSpan();
        var starts = new int[span.Count('\n') + 1];
        int n = 1;
        for (int i = span.IndexOf('\n'); i >= 0; )
        {
            starts[n++] = i + 1;
            int next = span[(i + 1)..].IndexOf('\n');
            i = next < 0 ? -1 : i + 1 + next;
        }
        return starts;
    }

    /// <summary>UTF-16 line/character to offset, clamped to the line's content (before `\r\n`).</summary>
    public int Offset(int line, int character)
    {
        if (line < 0) return 0;
        if (line >= LineStarts.Length) return Text.Length;
        int start = LineStarts[line];
        int end = line + 1 < LineStarts.Length ? LineStarts[line + 1] - 1 : Text.Length;
        if (end > start && Text[end - 1] == '\r' && end < Text.Length) end--;
        return start + Math.Clamp(character, 0, Math.Max(0, end - start));
    }

    public (int Line, int Character) Position(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        int line = Array.BinarySearch(LineStarts, offset);
        if (line < 0) line = ~line - 1;
        return (line, offset - LineStarts[line]);
    }

    /// <summary>Token containing or ending at <paramref name="offset"/>.</summary>
    public int TokenIndexAt(int offset)
    {
        int lo = 0, hi = Tokens.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var t = Tokens[mid];
            if (offset < t.Start) hi = mid - 1;
            else if (offset > t.End) lo = mid + 1;
            else
            {
                // Prefer the identifier when touching two tokens (`foo|.`).
                if (offset == t.End && mid + 1 < Tokens.Count && Tokens[mid + 1].Start == offset && t.Kind is not (TokenKind.Ident or TokenKind.Keyword)) return mid + 1;
                return mid;
            }
        }
        return -1;
    }

    /// <summary>Token containing the character at <paramref name="offset"/>, else the last token before it.</summary>
    public int TokenBefore(int offset)
    {
        int lo = 0, hi = Tokens.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (Tokens[mid].Start <= offset) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        // Skip zero-length Eof.
        while (best > 0 && Tokens[best].Length == 0) best--;
        return best;
    }

    public string TextOf(Token t) => Text.Substring(t.Start, t.Length);
    public ReadOnlySpan<char> SpanOf(Token t) => Text.AsSpan(t.Start, t.Length);
    /// <summary>Allocation-free token text comparison.</summary>
    public bool Is(Token t, string value) => t.Length == value.Length && Text.AsSpan(t.Start, t.Length).SequenceEqual(value);
    public bool Is(int index, string value) => (uint)index < (uint)Tokens.Count && Is(Tokens[index], value);
}

/// <summary>All documents of the workspace: open buffers plus `.byn` files on disk.</summary>
/// <remarks>
/// Disk files are bounded: hidden directories (`.git`, `.worktrees`, `.bayan-staging-*`), `tmp`
/// and build output are never walked, and indexing stops at <paramref name="maxFiles"/> files or
/// <paramref name="maxChars"/> characters of disk text. Open buffers are always held.
/// </remarks>
public sealed class Workspace(int maxFiles = Workspace.DefaultMaxFiles, long maxChars = Workspace.DefaultMaxChars, long maxFileBytes = Workspace.DefaultMaxFileBytes)
{
    public const int DefaultMaxFiles = 20_000;
    public const long DefaultMaxChars = 256L * 1024 * 1024;
    public const long DefaultMaxFileBytes = 4L * 1024 * 1024;

    // Windows and macOS file systems are case-insensitive by default; Linux is not.
    static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", "tmp" };

    readonly Dictionary<string, Document> _docs = new(PathComparer);
    readonly List<string> _roots = [];
    long _chars;
    bool _limitLogged;

    /// <summary>True when the file budget is spent; further disk files are not indexed.</summary>
    public bool LimitReached { get; private set; }

    /// <summary>A directory the indexer never enters.</summary>
    public static bool IsSkippedDirectory(string name) => name.StartsWith('.') || SkippedDirs.Contains(name);

    /// <summary>
    /// True when <paramref name="fullPath"/> lies outside every indexed root, or under a skipped
    /// directory of one. With no root indexed, nothing is excluded.
    /// </summary>
    public bool IsExcluded(string fullPath)
    {
        if (_roots.Count == 0) return false;
        var cmp = PathComparer == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var root in _roots)
        {
            if (!fullPath.StartsWith(root, cmp) || fullPath.Length <= root.Length) continue;
            if (fullPath[root.Length] != Path.DirectorySeparatorChar && !Path.EndsInDirectorySeparator(root)) continue;
            var dirs = Path.GetDirectoryName(fullPath.AsSpan(root.Length)).Trim(Path.DirectorySeparatorChar).ToString();
            if (dirs.Length == 0) return false;
            foreach (var seg in dirs.Split(Path.DirectorySeparatorChar))
                if (IsSkippedDirectory(seg)) return true;
            return false;
        }
        return true;
    }

    bool OverBudget(int addChars)
    {
        if (_docs.Count < maxFiles && _chars + addChars <= maxChars) return false;
        LimitReached = true;
        if (!_limitLogged)
        {
            _limitLogged = true;
            Console.Error.WriteLine($"bayan-lsp: index limit reached ({_docs.Count} files, {_chars} chars); further .byn files on disk are not indexed");
        }
        return true;
    }
    readonly Dictionary<string, string> _keyCache = new(StringComparer.Ordinal); // uri as sent → key

    public IEnumerable<Document> Documents => _docs.Values;

    /// <summary>Normalises a URI so `file:///c%3A/x` and `file:///C:/x` hit the same document.</summary>
    public string Key(string uri)
    {
        if (_keyCache.TryGetValue(uri, out var k)) return k;
        try
        {
            var u = new Uri(uri);
            k = u.IsFile ? Path.GetFullPath(u.LocalPath) : uri;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or NotSupportedException) { k = uri; }
        if (_keyCache.Count > 4096) _keyCache.Clear();
        _keyCache[uri] = k;
        return k;
    }

    static string? LocalPath(string uri)
    {
        try { var u = new Uri(uri); return u.IsFile ? u.LocalPath : null; }
        catch (UriFormatException) { return null; }
    }

    public Document? Get(string uri) => _docs.GetValueOrDefault(Key(uri));

    Document Add(string key, Document d)
    {
        d.Analyzed = Invalidate;
        if (_docs.TryGetValue(key, out var old)) _chars -= old.Text.Length;
        _chars += d.Text.Length;
        _docs[key] = d;
        Invalidate();
        return d;
    }

    public Document Open(string uri, string text, int version)
    {
        var key = Key(uri);
        if (_docs.TryGetValue(key, out var d) && d.Uri == uri) d.Set(text, version);
        else d = Add(key, new Document(uri, text, version));
        d.IsOpen = true;
        return d;
    }

    /// <summary>Closes a buffer; a file that still exists on disk stays indexed from its disk content.</summary>
    public void Close(string uri)
    {
        var key = Key(uri);
        if (!_docs.TryGetValue(key, out var d)) return;
        d.IsOpen = false;
        if (LocalPath(uri) is { } path && File.Exists(path))
        {
            try { d.Set(File.ReadAllText(path), 0); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _chars -= d.Text.Length;
        _docs.Remove(key);
        Invalidate();
    }

    /// <summary>
    /// Indexes or re-reads one file on disk. A file not yet indexed is skipped when it is excluded
    /// (<see cref="IsExcluded"/>), larger than the per-file limit, or over the workspace budget.
    /// </summary>
    public void LoadFromDisk(string path)
    {
        var full = Path.GetFullPath(path);
        var uri = new Uri(full).AbsoluteUri;
        var key = Key(uri);
        bool known = _docs.TryGetValue(key, out var existing);
        if (known && existing!.IsOpen) return;
        if (!known && IsExcluded(full)) return;
        string text;
        try
        {
            if (new FileInfo(full).Length > maxFileBytes) return;
            text = File.ReadAllText(full);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (existing is not null && existing.Text == text) return; // watcher echo of our own state
        if (!known && OverBudget(text.Length)) return;
        Add(key, new Document(uri, text, 0));
    }

    /// <summary>Removes a deleted file. Returns the URI of the removed document, if any.</summary>
    public string? Remove(string uri)
    {
        var key = Key(uri);
        if (!_docs.TryGetValue(key, out var d) || d.IsOpen) return null;
        _chars -= d.Text.Length;
        _docs.Remove(key);
        Invalidate();
        return d.Uri;
    }

    /// <summary>
    /// Indexes the `.byn` files under <paramref name="root"/>. Skipped directories and symbolic
    /// links are not entered; the walk stops once the budget is spent.
    /// </summary>
    public void IndexFolder(string root)
    {
        if (!Directory.Exists(root)) return;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!_roots.Contains(root, PathComparer)) _roots.Add(root);
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && !LimitReached)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.byn"))
                {
                    LoadFromDisk(f);
                    if (LimitReached) return;
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (IsSkippedDirectory(Path.GetFileName(sub))) continue;
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(sub);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    // ---- symbol index -------------------------------------------------------
    // Built lazily from all documents and dropped on any change, so requests between
    // edits (hover, completion, definition) are dictionary lookups instead of full scans.

    static readonly List<(Document, Symbol)> Empty = [];
    bool _indexValid;
    readonly List<(Document Doc, Symbol Sym)> _global = [];      // non-local, no container
    readonly List<(Document Doc, Symbol Sym)> _casesStates = [];
    readonly Dictionary<string, List<(Document, Symbol)>> _byName = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<(Document, Symbol)>> _byContainer = new(StringComparer.Ordinal);
    readonly Dictionary<string, (Document, Symbol)> _types = new(StringComparer.Ordinal);

    void Invalidate() => _indexValid = false;

    void EnsureIndex()
    {
        if (_indexValid) return;
        _global.Clear(); _casesStates.Clear(); _byName.Clear(); _byContainer.Clear(); _types.Clear();
        foreach (var d in _docs.Values)
            foreach (var s in d.Symbols)
            {
                if (s.IsLocal) continue;
                var e = (d, s);
                if (s.Container is null) _global.Add(e);
                else Bucket(_byContainer, s.Container).Add(e);
                if (s.Kind is SymbolKind.Case or SymbolKind.State) _casesStates.Add(e);
                Bucket(_byName, s.Name).Add(e);
                if (s.IsType) _types.TryAdd(s.Name, e);
            }
        _indexValid = true;
    }

    static List<(Document, Symbol)> Bucket(Dictionary<string, List<(Document, Symbol)>> map, string key)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        return list;
    }

    /// <summary>Every non-local symbol of the workspace.</summary>
    public IEnumerable<(Document Doc, Symbol Sym)> AllSymbols()
    {
        foreach (var d in _docs.Values)
            foreach (var s in d.Symbols)
                if (!s.IsLocal) yield return (d, s);
    }

    public List<(Document Doc, Symbol Sym)> Globals() { EnsureIndex(); return _global; }
    public List<(Document Doc, Symbol Sym)> CasesAndStates() { EnsureIndex(); return _casesStates; }
    public List<(Document Doc, Symbol Sym)> Named(string name) { EnsureIndex(); return _byName.GetValueOrDefault(name) ?? Empty; }

    public (Document Doc, Symbol Sym)? FindType(string name)
    {
        EnsureIndex();
        return _types.TryGetValue(name, out var e) ? e : null;
    }

    public List<(Document Doc, Symbol Sym)> MembersOf(string typeName)
    {
        EnsureIndex();
        return _byContainer.GetValueOrDefault(typeName) ?? Empty;
    }

    public (Document Doc, Symbol Sym)? Member(string typeName, string name)
    {
        foreach (var m in MembersOf(typeName)) if (m.Sym.Name == name) return m;
        return null;
    }

    public static Symbol? EnclosingType(Document doc, int offset)
    {
        Symbol? best = null;
        foreach (var s in doc.Symbols)
            if (s.IsType && s.DeclStart <= offset && offset < s.DeclEnd && (best is null || s.DeclStart > best.DeclStart)) best = s;
        return best;
    }

    /// <summary>Locals and parameters visible at <paramref name="offset"/>, innermost first.</summary>
    public static IEnumerable<Symbol> LocalsAt(Document doc, int offset)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = doc.Symbols.Count - 1; i >= 0; i--)
        {
            var s = doc.Symbols[i];
            if (s.IsLocal && s.ScopeStart <= offset && offset <= s.ScopeEnd && s.NameStart < offset && seen.Add(s.Name))
                yield return s;
        }
    }

    /// <summary>Resolves a bare name at an offset: locals, then members of the enclosing type, then globals.</summary>
    public (Document Doc, Symbol Sym)? Resolve(Document doc, string name, int offset)
    {
        // Innermost first: the last matching local declared before the offset wins.
        for (int i = doc.Symbols.Count - 1; i >= 0; i--)
        {
            var s = doc.Symbols[i];
            if (s.IsLocal && s.ScopeStart <= offset && offset <= s.ScopeEnd && s.NameStart < offset && s.Name == name)
                return (doc, s);
        }
        if (EnclosingType(doc, offset) is { } ty && Member(ty.Name, name) is { } m) return m;
        var named = Named(name);
        // Prefer a top-level symbol, then one in this document, then any.
        foreach (var e in named) if (e.Sym.Container is null) return e;
        foreach (var e in named) if (e.Doc == doc) return e;
        return named.Count > 0 ? named[0] : null;
    }

    /// <summary>Static type name of a receiver expression ending at token <paramref name="idx"/>.</summary>
    public string? TypeOfReceiver(Document doc, int idx)
    {
        if (idx < 0) return null;
        var t = doc.Tokens[idx];
        if (t.Kind == TokenKind.String) return "string";
        if (t.Kind is not (TokenKind.Ident or TokenKind.Keyword)) return null;
        if (doc.Is(t, "this")) return EnclosingType(doc, t.Start)?.Name;
        var text = doc.TextOf(t);
        if (char.IsUpper(text[0])) return text; // `Order.` → static members / cases
        // `a.b.` → type of b inside type of a.
        if (idx >= 2 && (doc.Is(idx - 1, ".") || doc.Is(idx - 1, "?.")))
            return TypeOfReceiver(doc, idx - 2) is { } outer && Member(outer, text) is { } m ? TypeOfSymbol(m.Doc, m.Sym, 1) : null;
        return Resolve(doc, text, t.Start) is { } r ? TypeOfSymbol(r.Doc, r.Sym, 0) : null;
    }

    string? TypeOfSymbol(Document doc, Symbol s, int depth)
    {
        if (Normalize(s.TypeName) is { } declared) return declared == "Self" ? s.Container : declared;
        if (s.Kind == SymbolKind.Local && s.InitStart >= 0 && depth < 8)
            return ExprType(doc, doc.TokenIndexAt(s.InitStart), depth + 1);
        return null;
    }

    /// <summary>Type of a simple chain `a.b.c(…)`, `Type(…)`, `Type { … }`, `f(…)`.</summary>
    public string? ExprType(Document doc, int i, int depth = 0)
    {
        if (i < 0 || depth > 8) return null;
        var toks = doc.Tokens;
        while (i < toks.Count && (doc.Is(toks[i], "try") || doc.Is(toks[i], "move"))) i++;
        if (i >= toks.Count) return null;
        var t = toks[i];
        string? type;
        if (t.Kind == TokenKind.String) type = "string";
        else if (t.Kind is not (TokenKind.Ident or TokenKind.Keyword)) return null;
        else if (doc.Is(t, "this")) type = EnclosingType(doc, t.Start)?.Name;
        else if (doc.TextOf(t) is var text && char.IsUpper(text[0])) type = text;
        else if (Resolve(doc, text, t.Start) is { } r) type = r.Sym.Kind == SymbolKind.Function ? Normalize(r.Sym.TypeName) : TypeOfSymbol(r.Doc, r.Sym, depth + 1);
        else type = null;
        i = SkipCall(doc, i + 1);
        while (type is not null && i + 1 < toks.Count && (doc.Is(toks[i], ".") || doc.Is(toks[i], "?.")) && toks[i + 1].Kind == TokenKind.Ident)
        {
            var member = doc.TextOf(toks[i + 1]);
            string? next = Member(type, member) is { } m ? TypeOfSymbol(m.Doc, m.Sym, depth + 1) : null;
            if (next is null && type == "string")
                foreach (var b in Builtins.StringMembers)
                    if (b.Name == member) { int sp = b.Signature.IndexOf(' '); next = sp > 0 ? Normalize(b.Signature[..sp]) : null; break; }
            type = next;
            i = SkipCall(doc, i + 2);
        }
        return type;
    }

    static int SkipCall(Document doc, int i)
    {
        var toks = doc.Tokens;
        if (i >= toks.Count || !(doc.Is(toks[i], "(") || doc.Is(toks[i], "{"))) return i;
        int depth = 0;
        for (; i < toks.Count; i++)
        {
            var t = toks[i];
            if (t.Kind != TokenKind.Punct || t.Length != 1) continue;
            char c = doc.Text[t.Start];
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']' && --depth == 0) return i + 1;
        }
        return i;
    }

    /// <summary>`Order.paid` → `Order`, `List<T>?` → `List`.</summary>
    public static string? Normalize(string? type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        var span = type.AsSpan().Trim();
        int cut = span.IndexOfAny("<?*[ ");
        if (cut >= 0) span = span[..cut];
        int dot = span.IndexOf('.');
        if (dot > 0 && (dot + 1 == span.Length || char.IsLower(span[dot + 1]))) span = span[..dot];
        if (span.IsEmpty) return null;
        return span.ToString();
    }
}
