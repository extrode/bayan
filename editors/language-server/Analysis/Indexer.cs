namespace Bayan.LanguageServer.Analysis;

public enum SymbolKind : byte
{
    Struct, Union, ErrorUnion, TypeAlias, Interface, Effect,
    Function, Field, Case, State, Const, Local, Param, Test,
}

public class Symbol
{
    public required string Name;
    public required SymbolKind Kind;
    public string? Container;      // enclosing type name for members
    public string? TypeName;       // declared type of fields, locals, params
    public required string Detail; // header line as written
    public int NameStart;          // offset of the name
    public int DeclStart;          // offset of the header
    public int DeclEnd;            // end of the body (or header)

    // Docs and children are rare; one side object keeps every other symbol smaller.
    sealed class Rare { public string? Doc; public List<Symbol>? Children; }
    Rare? _rare;
    /// <summary>Joined `///` lines.</summary>
    public string? Doc { get => _rare?.Doc; set { if (value is not null || _rare is not null) (_rare ??= new()).Doc = value; } }
    /// <summary>Document outline children; null until the first one.</summary>
    public List<Symbol>? Children { get => _rare?.Children; set { if (value is not null || _rare is not null) (_rare ??= new()).Children = value; } }

    public bool IsType => Kind is SymbolKind.Struct or SymbolKind.Union or SymbolKind.ErrorUnion
        or SymbolKind.TypeAlias or SymbolKind.Interface;
    public bool IsLocal => Kind is SymbolKind.Local or SymbolKind.Param;
}

/// <summary>A local or parameter: only these carry a scope, so other symbols stay smaller.</summary>
public sealed class LocalSymbol : Symbol
{
    public int InitStart = -1;     // locals: offset of the initializer expression
    public int ScopeStart = 0;     // visible from here…
    public int ScopeEnd = int.MaxValue; // …to here
}

/// <summary>
/// Line-based declaration scanner. Bayan statements end at newlines and declarations
/// are type-first, so a header fits on one line; that keeps this scan simple and
/// tolerant of half-typed code. Replaced by the compiler's binder (HANDOFF §3).
/// </summary>
public sealed class Indexer
{
    enum FrameKind : byte { Type, States, Function, Other }

    sealed class Frame(FrameKind kind, Symbol? owner)
    {
        public readonly FrameKind Kind = kind;
        public readonly Symbol? Owner = owner;
        public readonly List<LocalSymbol> Scoped = [];
    }

    readonly string _s;
    readonly List<Token> _t;
    readonly List<Frame> _frames = [];
    readonly List<string> _doc = [];
    readonly List<LocalSymbol> _scoped = []; // reused per line
    // Names, types and headers repeat within a file; symbols share one string per distinct text.
    readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _pool = new Dictionary<string, string>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
    public readonly List<Symbol> Symbols = [];
    public readonly List<Symbol> Roots = [];

    Indexer(string text, List<Token> tokens) { _s = text; _t = tokens; }

    public static Indexer Run(string text, List<Token> tokens)
    {
        var ix = new Indexer(text, tokens);
        ix.Scan();
        return ix;
    }

    ReadOnlySpan<char> Text(Token t) => _s.AsSpan(t.Start, t.Length);
    string Str(Token t) => Pool(Text(t));

    string Pool(ReadOnlySpan<char> text)
    {
        if (_pool.TryGetValue(text, out var s)) return s;
        s = text.ToString();
        _pool[s] = s;
        return s;
    }
    bool Is(Token t, string v) => t.Length == v.Length && t.Kind is TokenKind.Keyword or TokenKind.Punct or TokenKind.Ident && Text(t).SequenceEqual(v);
    Frame? Top => _frames.Count > 0 ? _frames[^1] : null;

    Symbol? EnclosingType()
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
            if (_frames[i].Kind == FrameKind.Type) return _frames[i].Owner;
        return null;
    }

    bool InFunction()
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].Kind == FrameKind.Function) return true;
            if (_frames[i].Kind is FrameKind.Type or FrameKind.States) return false;
        }
        return false;
    }

    void Scan()
    {
        var line = new List<Token>(32);
        int i = 0;
        while (i < _t.Count && _t[i].Kind != TokenKind.Eof)
        {
            line.Clear();
            bool hadDoc = false;
            while (_t[i].Kind is not (TokenKind.Newline or TokenKind.Eof))
            {
                var tk = _t[i++];
                if (tk.Kind == TokenKind.DocComment) { _doc.Add(_s.Substring(tk.Start + 3, tk.Length - 3).Trim()); hadDoc = true; continue; }
                if (tk.Kind is TokenKind.Comment or TokenKind.BlockComment) continue;
                line.Add(tk);
            }
            if (_t[i].Kind == TokenKind.Newline) i++;

            // A blank or plain-comment line detaches pending `///` docs.
            if (line.Count == 0) { if (!hadDoc) _doc.Clear(); continue; }
            ProcessLine(line);
        }
        // Close anything left open (half-typed code).
        while (_frames.Count > 0) Pop(_s.Length);
    }

    void ProcessLine(List<Token> line)
    {
        int k = 0;
        // Leading `}` close frames first (`} else {`).
        while (k < line.Count && Is(line[k], "}")) Pop(line[k++].Start);
        if (k >= line.Count) return;

        // Attribute-only line: keep pending docs.
        if (line[k].Kind == TokenKind.At && AllAttributes(line, k)) return;
        k = SkipAttributesAndModifiers(line, k);

        Symbol? opener = null;      // symbol whose body the line's first `{` opens
        FrameKind openerKind = FrameKind.Other;
        var scoped = _scoped;       // params / loop vars scoped to that body
        scoped.Clear();

        if (k < line.Count) Declare(line, k, ref opener, ref openerKind, scoped);
        _doc.Clear();

        // Brace bookkeeping for the rest of the line.
        int firstHeaderOpen = -1;
        if (opener is not null || scoped.Count > 0)
            for (int j = k; j < line.Count; j++) if (Is(line[j], "{")) { firstHeaderOpen = j; break; }

        for (int j = k; j < line.Count; j++)
        {
            if (Is(line[j], "{"))
            {
                bool isHeader = j == firstHeaderOpen;
                var f = new Frame(isHeader && opener is not null ? openerKind : FrameKind.Other, isHeader ? opener : null);
                if (isHeader)
                    foreach (var p in scoped) { p.ScopeStart = line[j].Start; f.Scoped.Add(p); }
                _frames.Add(f);
            }
            else if (Is(line[j], "}")) Pop(line[j].Start);
        }
        // Parameters of a body-less declaration (interface method, extern) end with the line.
        if (firstHeaderOpen < 0) foreach (var p in scoped) { p.ScopeStart = p.NameStart; p.ScopeEnd = line[^1].End; }
    }

    void Pop(int at)
    {
        if (_frames.Count == 0) return;
        var f = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        foreach (var s in f.Scoped) s.ScopeEnd = at;
        if (f.Owner is not null) f.Owner.DeclEnd = at + 1;
    }

    bool AllAttributes(List<Token> line, int k)
    {
        int j = SkipAttributes(line, k);
        if (j >= line.Count) return true;
        // `@allow(x) because "…"` alone on a line.
        return Is(line[j], "because") && j + 2 >= line.Count;
    }

    int SkipAttributes(List<Token> line, int k)
    {
        while (k < line.Count && line[k].Kind == TokenKind.At)
        {
            k += 2; // @name
            if (k < line.Count && Is(line[k], "(")) k = SkipGroup(line, k);
            if (k < line.Count && Is(line[k], "because")) k += 2;
        }
        return k;
    }

    int SkipAttributesAndModifiers(List<Token> line, int k)
    {
        k = SkipAttributes(line, k);
        while (k < line.Count && Keywords.IsModifier(Text(line[k]))) k++;
        return k;
    }

    int SkipGroup(List<Token> line, int k)
    {
        int depth = 0;
        for (; k < line.Count; k++)
        {
            if (Is(line[k], "(") || Is(line[k], "[") || Is(line[k], "<")) depth++;
            else if (Is(line[k], ")") || Is(line[k], "]") || Is(line[k], ">")) { if (--depth == 0) return k + 1; }
        }
        return k;
    }

    string Header(List<Token> line, int from)
    {
        int end = line[^1].End;
        if (Is(line[^1], "{")) end = line.Count > 1 ? line[^2].End : line[^1].Start;
        return Pool(_s.AsSpan(line[from].Start, end - line[from].Start).Trim());
    }

    Symbol Add(SymbolKind kind, Token name, List<Token> line, int from, string? typeName = null, string? container = null, bool docs = true)
    {
        var parentType = EnclosingType();
        bool local = kind is SymbolKind.Local or SymbolKind.Param;
        Symbol sym = local
            ? new LocalSymbol { Name = Str(name), Kind = kind, Detail = Header(line, from) }
            : new Symbol { Name = Str(name), Kind = kind, Detail = Header(line, from) };
        sym.NameStart = name.Start;
        sym.DeclStart = line[from].Start;
        sym.DeclEnd = line[^1].End;
        sym.TypeName = typeName;
        sym.Container = container ?? (local ? null : parentType?.Name);
        sym.Doc = docs && _doc.Count > 0 ? string.Join('\n', _doc) : null;
        Symbols.Add(sym);
        if (!local)
        {
            var parent = parentType;
            if (parent is not null && kind != SymbolKind.Test) (parent.Children ??= []).Add(sym);
            else Roots.Add(sym);
        }
        return sym;
    }

    void Declare(List<Token> line, int k, ref Symbol? opener, ref FrameKind openerKind, List<LocalSymbol> scoped)
    {
        var first = line[k];
        var w = Text(first);
        Token? At(int j) => j < line.Count ? line[j] : null;

        // Inside a `states { … }` block every bare name is a state.
        if (Top?.Kind == FrameKind.States && first.Kind == TokenKind.Ident)
        {
            Add(SymbolKind.State, first, line, k, container: Top.Owner?.Name);
            return;
        }

        switch (w)
        {
            case "struct" or "union" or "type" or "interface":
            {
                int j = k + 1;
                bool error = false;
                if (At(j) is { } m && (Is(m, "unique") || Is(m, "distinct") || Is(m, "error"))) { error = Is(m, "error"); j++; }
                if (At(j) is not { Kind: TokenKind.Ident } name) return;
                var kind = w switch
                {
                    "struct" => SymbolKind.Struct,
                    "union" => error ? SymbolKind.ErrorUnion : SymbolKind.Union,
                    "type" => SymbolKind.TypeAlias,
                    _ => SymbolKind.Interface,
                };
                opener = Add(kind, name, line, k);
                openerKind = FrameKind.Type;
                return;
            }
            case "effect":
                if (At(k + 1) is { Kind: TokenKind.Ident } en) Add(SymbolKind.Effect, en, line, k);
                return;
            case "states":
                opener = EnclosingType();
                openerKind = FrameKind.States;
                return;
            case "case":
                if (!InFunction() && At(k + 1) is { Kind: TokenKind.Ident } cn) Add(SymbolKind.Case, cn, line, k);
                return;
            case "const":
                if (At(k + 1) is { Kind: TokenKind.Ident } cst && At(k + 2) is { } eq && Is(eq, "="))
                    Add(SymbolKind.Const, cst, line, k);
                return;
            case "test" or "bench":
                if (At(k + 1) is { Kind: TokenKind.String } tn)
                {
                    var t = Add(SymbolKind.Test, tn, line, k);
                    t.Name = _s.Substring(tn.Start + 1, Math.Max(0, tn.Length - 2));
                    opener = t; openerKind = FrameKind.Function;
                }
                return;
            case "let" or "var":
                DeclareLocal(line, k + 1, k);
                return;
            case "for":
            {
                int j = k + 1;
                if (At(j) is { } tr && Is(tr, "try")) j++;
                if (At(j) is { Kind: TokenKind.Ident } lv)
                {
                    string? ty = null;
                    if (At(j + 1) is { } c && Is(c, ":")) ty = TypeText(line, j + 2, stopAt: "in");
                    scoped.Add((LocalSymbol)Add(SymbolKind.Local, lv, line, k, typeName: ty, docs: false));
                }
                return;
            }
            case "while":
                if (At(k + 1) is { } wl && Is(wl, "let")) DeclareLocal(line, k + 2, k, scoped);
                return;
        }

        // Type-first function or field: `Type name(…)` / `Type name`.
        int typeEnd = ScanType(line, k);
        if (typeEnd <= k || typeEnd >= line.Count) return;
        var nameTok = line[typeEnd];
        if (nameTok.Kind != TokenKind.Ident || char.IsUpper(_s[nameTok.Start])) return;
        string typeName = TypeText(line, k, end: typeEnd);

        if (At(typeEnd + 1) is { } paren && Is(paren, "("))
        {
            if (InFunction()) return; // a call inside a body, not a declaration
            var fn = Add(SymbolKind.Function, nameTok, line, k, typeName: typeName);
            opener = fn; openerKind = FrameKind.Function;
            DeclareParams(line, typeEnd + 1, k, scoped);
            return;
        }
        if (Top?.Kind == FrameKind.Type)
            Add(SymbolKind.Field, nameTok, line, k, typeName: typeName);
    }

    void DeclareLocal(List<Token> line, int j, int from, List<LocalSymbol>? scoped = null)
    {
        if (j >= line.Count || line[j].Kind != TokenKind.Ident) return;
        var nameTok = line[j];
        string? ty = null;
        if (j + 1 < line.Count && Is(line[j + 1], ":")) ty = TypeText(line, j + 2, stopAt: "=");
        else if (j + 2 < line.Count && Is(line[j + 1], "=")) ty = InferType(line, j + 2);
        var sym = (LocalSymbol)Add(SymbolKind.Local, nameTok, line, from, typeName: ty, docs: false);
        sym.ScopeStart = line[^1].End; // not visible in its own initializer
        for (int e = j + 1; e + 1 < line.Count; e++) if (Is(line[e], "=")) { sym.InitStart = line[e + 1].Start; break; }
        if (scoped is not null) scoped.Add(sym);
        else if (Top is { } f) f.Scoped.Add(sym);
    }

    /// <summary>Cheap inference for `let x = Type { … }`, `Type(…)`, `Type.make(…)`, literals.</summary>
    string? InferType(List<Token> line, int j)
    {
        if (Is(line[j], "try")) j++;
        if (j >= line.Count) return null;
        var t = line[j];
        if (t.Kind == TokenKind.String) return "string";
        if (t.Kind == TokenKind.Rune) return "rune";
        if (t.Kind == TokenKind.Ident && char.IsUpper(_s[t.Start])) return Str(t);
        return null;
    }

    void DeclareParams(List<Token> line, int open, int from, List<LocalSymbol> scoped)
    {
        int depth = 0, start = open + 1;
        for (int j = open; j < line.Count; j++)
        {
            if (Is(line[j], "(") || Is(line[j], "[") || Is(line[j], "<")) { depth++; continue; }
            bool close = Is(line[j], ")") || Is(line[j], "]") || Is(line[j], ">");
            if (close) depth--;
            if ((depth == 1 && Is(line[j], ",")) || (depth == 0 && close))
            {
                DeclareParam(line, start, j, from, scoped);
                start = j + 1;
                if (depth == 0) return;
            }
        }
    }

    void DeclareParam(List<Token> line, int a, int b, int from, List<LocalSymbol> scoped)
    {
        // Cut default value and inline state refinement: `Order order is .paid = …`.
        for (int j = a; j < b; j++) if (Is(line[j], "=") || Is(line[j], "is")) { b = j; break; }
        if (b - a < 2) return;
        if (Is(line[a], "ref") || Is(line[a], "move")) { if (Is(line[a + 1], "this")) return; a++; }
        if (Is(line[a], "rest") || Is(line[a], "const")) a++;
        var nameTok = line[b - 1];
        if (nameTok.Kind != TokenKind.Ident) return;
        // Particle: a lowercase word before the type (`at i64 i`).
        if (b - a >= 3 && line[a].Kind is TokenKind.Ident or TokenKind.Keyword && char.IsLower(_s[line[a].Start])
            && !Keywords.IsPrimitive(Text(line[a]))) a++;
        var p = (LocalSymbol)Add(SymbolKind.Param, nameTok, line, from, typeName: TypeText(line, a, end: b - 1), docs: false);
        p.Detail = _s[line[a].Start..nameTok.End];
        scoped.Add(p);
    }

    /// <summary>Returns the index after a type starting at <paramref name="k"/>, or k when none.</summary>
    int ScanType(List<Token> line, int k)
    {
        var t = line[k];
        bool startsType = (t.Kind == TokenKind.Ident && (char.IsUpper(_s[t.Start]) || Keywords.IsPrimitive(Text(t))));
        if (!startsType) return k;
        int j = k + 1;
        while (j < line.Count)
        {
            var x = line[j];
            if (Is(x, "<") || Is(x, "[")) { j = SkipGroup(line, j); continue; }
            if (Is(x, "?") || Is(x, "*")) { j++; continue; }
            // `Order.paid` single-state type.
            if (Is(x, ".") && j + 1 < line.Count && line[j + 1].Kind == TokenKind.Ident && j + 2 < line.Count
                && line[j + 2].Kind == TokenKind.Ident) { j += 2; continue; }
            break;
        }
        return j;
    }

    string TypeText(List<Token> line, int a, int end = -1, string? stopAt = null)
    {
        if (end < 0)
        {
            end = a;
            while (end < line.Count && !(stopAt is not null && Is(line[end], stopAt)) && !Is(line[end], "{")) end++;
        }
        if (a >= end || a >= line.Count) return "";
        return Pool(_s.AsSpan(line[a].Start, line[end - 1].End - line[a].Start).Trim());
    }
}
