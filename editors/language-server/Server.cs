using System.Text;
using System.Text.Json;
using Bayan.LanguageServer.Analysis;
using Bayan.LanguageServer.Rpc;

namespace Bayan.LanguageServer;

public sealed class Server(JsonRpcTransport rpc)
{
    readonly Workspace _ws = new();
    bool _shutdown;
    // Docs of the last completion list, sent on `completionItem/resolve` instead of with every item.
    readonly List<string?> _completionDocs = new(256);
    // Open buffers over the per-file limit: not analysed, and incremental changes are ignored until
    // a didOpen or full change brings them under it.
    readonly HashSet<string> _tooLarge = new(StringComparer.Ordinal);

    public int Run()
    {
        while (rpc.Read() is { } msg)
        {
            using (msg)
            {
                var root = msg.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                // A message without `method` is a response to a server request; the server sends none.
                if (!root.TryGetProperty("method"u8, out var m) || m.ValueKind != JsonValueKind.String) continue;
                string method = m.GetString()!;
                bool isRequest = root.TryGetProperty("id"u8, out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String;
                var p = root.TryGetProperty("params"u8, out var pp) ? pp : default;
                if (method == "exit") return _shutdown ? 0 : 1;
                try
                {
                    if (isRequest)
                    {
                        if (_shutdown) Error(id, -32600, "server is shutting down");
                        else HandleRequest(method, id, p);
                    }
                    else HandleNotification(method, p);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"bayan-lsp: {method}: {ex}");
                    if (isRequest) Error(id, -32603, ex.Message);
                }
            }
        }
        return 0;
    }

    // ---- dispatch -----------------------------------------------------------

    void HandleRequest(string method, JsonElement id, JsonElement p)
    {
        switch (method)
        {
            case "initialize": Initialize(id, p); break;
            case "shutdown": _shutdown = true; Reply(id, w => w.WriteNull("result"u8)); break;
            case "textDocument/completion": Completion(id, p); break;
            case "completionItem/resolve": ResolveCompletion(id, p); break;
            case "textDocument/hover": Hover(id, p); break;
            case "textDocument/definition": Definition(id, p); break;
            case "textDocument/documentSymbol": DocumentSymbols(id, p); break;
            case "textDocument/signatureHelp": SignatureHelp(id, p); break;
            case "workspace/symbol": WorkspaceSymbols(id, p); break;
            default: Error(id, -32601, "method not found: " + method); break;
        }
    }

    void HandleNotification(string method, JsonElement p)
    {
        switch (method)
        {
            case "textDocument/didOpen":
            {
                var td = p.GetProperty("textDocument"u8);
                var uri = td.GetProperty("uri"u8).GetString()!;
                var text = td.GetProperty("text"u8).GetString()!;
                if (_ws.TooLarge(text)) { DropTooLarge(uri); break; }
                _tooLarge.Remove(uri);
                var doc = _ws.Open(uri, text, td.GetProperty("version"u8).GetInt32());
                Publish(doc);
                break;
            }
            case "textDocument/didChange":
            {
                var td = p.GetProperty("textDocument"u8);
                var uri = td.GetProperty("uri"u8).GetString()!;
                var doc = _tooLarge.Contains(uri) ? null : _ws.Get(uri);
                if (doc is null && !_tooLarge.Contains(uri)) break;
                int version = td.GetProperty("version"u8).GetInt32();
                foreach (var ch in p.GetProperty("contentChanges"u8).EnumerateArray())
                {
                    bool full = !ch.TryGetProperty("range"u8, out var r);
                    if (doc is null)
                    {
                        // Too large so far: only a full change can bring the buffer back.
                        if (!full || _ws.TooLarge(ch.GetProperty("text"u8).GetString()!)) continue;
                        _tooLarge.Remove(uri);
                        doc = _ws.Open(uri, ch.GetProperty("text"u8).GetString()!, version);
                        continue;
                    }
                    if (!full)
                    {
                        var s = r.GetProperty("start"u8); var e = r.GetProperty("end"u8);
                        doc.Apply(s.GetProperty("line"u8).GetInt32(), s.GetProperty("character"u8).GetInt32(),
                                  e.GetProperty("line"u8).GetInt32(), e.GetProperty("character"u8).GetInt32(),
                                  ch.GetProperty("text"u8).GetString()!);
                    }
                    else doc.Replace(ch.GetProperty("text"u8).GetString()!);
                    if (_ws.TooLarge(doc.Text)) { DropTooLarge(uri); doc = null; }
                }
                if (doc is null) break;
                doc.Set(doc.Text, version); // one analysis per notification, however many changes
                Publish(doc);
                break;
            }
            case "textDocument/didClose":
            {
                var uri = p.GetProperty("textDocument"u8).GetProperty("uri"u8).GetString()!;
                // A dropped buffer comes back from disk under the usual rules.
                if (_tooLarge.Remove(uri) && new Uri(uri) is { IsFile: true } lu) _ws.LoadFromDisk(lu.LocalPath, uri);
                _ws.Close(uri);
                if (_ws.Get(uri) is { } d) Publish(d); else PublishEmpty(uri);
                break;
            }
            case "workspace/didChangeWatchedFiles":
                foreach (var ch in p.GetProperty("changes"u8).EnumerateArray())
                {
                    var uri = ch.GetProperty("uri"u8).GetString()!;
                    if (ch.GetProperty("type"u8).GetInt32() == 3)
                    {
                        if (_ws.Remove(uri) is { } removed) PublishEmpty(removed);
                        else foreach (var r in _ws.RemoveFolder(uri)) PublishEmpty(r); // a folder delete or rename
                        continue;
                    }
                    if (_tooLarge.Contains(uri)) continue; // the editor holds a buffer we dropped; disk text would be stale
                    try
                    {
                        if (new Uri(uri) is { IsFile: true } u)
                        {
                            if (Directory.Exists(u.LocalPath)) _ws.LoadFolder(u.LocalPath);
                            else _ws.LoadFromDisk(u.LocalPath);
                        }
                    }
                    catch (UriFormatException) { }
                }
                break;
        }
    }

    // ---- lifecycle ------------------------------------------------------------

    void Initialize(JsonElement id, JsonElement p)
    {
        if (p.TryGetProperty("workspaceFolders"u8, out var wf) && wf.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in wf.EnumerateArray()) IndexUri(f.GetProperty("uri"u8).GetString());
        }
        else if (p.TryGetProperty("rootUri"u8, out var ru) && ru.ValueKind == JsonValueKind.String) IndexUri(ru.GetString());

        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            w.WriteStartObject("capabilities"u8);
            w.WriteStartObject("textDocumentSync"u8);
            w.WriteBoolean("openClose"u8, true);
            w.WriteNumber("change"u8, 2); // incremental
            w.WriteEndObject();
            w.WriteStartObject("completionProvider"u8);
            w.WriteBoolean("resolveProvider"u8, true);
            w.WriteStartArray("triggerCharacters"u8); w.WriteStringValue("."u8); w.WriteStringValue("@"u8); w.WriteEndArray();
            w.WriteEndObject();
            w.WriteBoolean("hoverProvider"u8, true);
            w.WriteBoolean("definitionProvider"u8, true);
            w.WriteBoolean("documentSymbolProvider"u8, true);
            w.WriteBoolean("workspaceSymbolProvider"u8, true);
            w.WriteStartObject("signatureHelpProvider"u8);
            w.WriteStartArray("triggerCharacters"u8); w.WriteStringValue("("u8); w.WriteStringValue(","u8); w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("serverInfo"u8);
            w.WriteString("name"u8, "bayan-lsp"u8);
            w.WriteString("version"u8, "0.0.1"u8);
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    void IndexUri(string? uri)
    {
        if (uri is null) return;
        try { _ws.IndexFolder(new Uri(uri).LocalPath); } catch (UriFormatException) { }
    }

    // ---- diagnostics ----------------------------------------------------------

    void Publish(Document doc) => rpc.Write(w =>
    {
        w.WriteString("method"u8, "textDocument/publishDiagnostics"u8);
        w.WriteStartObject("params"u8);
        w.WriteString("uri"u8, doc.Uri);
        if (doc.IsOpen) w.WriteNumber("version"u8, doc.Version); // versions only mean something for open buffers
        w.WriteStartArray("diagnostics"u8);
        foreach (var d in doc.Diagnostics)
        {
            w.WriteStartObject();
            WriteRange(w, doc, d.Start, d.Start + d.Length);
            w.WriteNumber("severity"u8, d.Severity);
            if (d.Code is not null) w.WriteString("code"u8, d.Code);
            w.WriteString("source"u8, "bayan"u8);
            w.WriteString("message"u8, d.Message);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    });

    void DropTooLarge(string uri)
    {
        _ws.Drop(uri);
        if (!_tooLarge.Add(uri)) return;
        rpc.Write(w =>
        {
            w.WriteString("method"u8, "textDocument/publishDiagnostics"u8);
            w.WriteStartObject("params"u8);
            w.WriteString("uri"u8, uri);
            w.WriteStartArray("diagnostics"u8);
            w.WriteStartObject();
            w.WriteStartObject("range"u8);
            w.WriteStartObject("start"u8); w.WriteNumber("line"u8, 0); w.WriteNumber("character"u8, 0); w.WriteEndObject();
            w.WriteStartObject("end"u8); w.WriteNumber("line"u8, 0); w.WriteNumber("character"u8, 0); w.WriteEndObject();
            w.WriteEndObject();
            w.WriteNumber("severity"u8, 2);
            w.WriteString("source"u8, "bayan"u8);
            w.WriteString("message"u8, $"file is larger than {Workspace.DefaultMaxFileBytes / (1024 * 1024)} MiB; the language server does not analyse it");
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    void PublishEmpty(string uri) => rpc.Write(w =>
    {
        w.WriteString("method"u8, "textDocument/publishDiagnostics"u8);
        w.WriteStartObject("params"u8);
        w.WriteString("uri"u8, uri);
        w.WriteStartArray("diagnostics"u8); w.WriteEndArray();
        w.WriteEndObject();
    });

    // ---- completion -----------------------------------------------------------

    void Completion(JsonElement id, JsonElement p)
    {
        if (!Locate(p, out var doc, out int offset)) { Reply(id, w => w.WriteNull("result"u8)); return; }
        var items = new List<(string Label, int Kind, string? Detail, string? Doc, string? Sort)>(128);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Item(string label, int kind, string? detail, string? docs, string sort = "5")
        { if (seen.Add(label)) items.Add((label, kind, detail, docs, sort + label)); }

        int ti = offset == 0 ? -1 : doc.TokenBefore(offset - 1);
        var tok = ti >= 0 ? doc.Tokens[ti] : default;
        if (ti >= 0 && tok.Kind is TokenKind.Comment or TokenKind.DocComment or TokenKind.String or TokenKind.Rune
            && offset > tok.Start && offset < tok.End + (tok.Kind == TokenKind.String ? 0 : 1)
            && !(tok.Kind == TokenKind.String && doc.Text[tok.Start] == '$'))
        { Reply(id, w => w.WriteNull("result"u8)); return; }

        // Partial word under the cursor and the token before it.
        int before = ti;
        if (ti >= 0 && tok.Kind is TokenKind.Ident or TokenKind.Keyword && tok.End == offset) before = ti - 1;
        bool afterDot = before >= 0 && (doc.Is(before, ".") || doc.Is(before, "?."));
        bool afterAt = before >= 0 && doc.Tokens[before].Kind == TokenKind.At;

        if (afterAt)
        {
            foreach (var (n, d) in Attributes) Item(n, 3, "@" + n, d);
        }
        else if (afterDot)
        {
            int recv = before - 1;
            if (IsTargetTyped(doc, recv))
            {
                // Narrow by context: `fail .` → error cases; `is .` / `becomes .` → states.
                var rt = recv >= 0 ? doc.Tokens[recv] : default;
                bool errorsOnly = recv >= 0 && doc.Is(rt, "fail");
                bool statesOnly = recv >= 0 && (doc.Is(rt, "is") || doc.Is(rt, "becomes") || doc.Is(rt, "become"));
                foreach (var (_, s) in _ws.CasesAndStates())
                {
                    if (statesOnly && s.Kind != SymbolKind.State) continue;
                    if (errorsOnly && (s.Kind != SymbolKind.Case || s.Container is null
                        || _ws.FindType(s.Container) is not { Sym.Kind: SymbolKind.ErrorUnion })) continue;
                    Item(s.Name, 20, s.Container + "." + s.Name, s.Doc);
                }
            }
            else if (_ws.TypeOfReceiver(doc, recv) is { } type)
            {
                bool onValue = !char.IsUpper(doc.Text[doc.Tokens[recv].Start]);
                foreach (var (_, m) in _ws.MembersOf(type))
                    if (!(onValue && m.Kind is SymbolKind.State or SymbolKind.Case)) Item(m.Name, MemberKind(m), m.Detail, m.Doc, "1");
                if (_ws.FindType(type) is { Sym: { Kind: SymbolKind.TypeAlias } alias } && alias.Detail.Contains(" distinct ", StringComparison.Ordinal)
                    && UnderlyingType(alias.Detail) is { } under)
                    Item("value", 10, under + " value", "The underlying value.", "1");
                foreach (var b in BuiltinMembers(type)) Item(b.Name, b.IsMethod ? 2 : 10, b.Signature, b.Doc, "2");
            }
        }
        else
        {
            foreach (var l in Workspace.LocalsAt(doc, offset)) Item(l.Name, 6, l.TypeName is { Length: > 0 } t ? $"{l.Name}: {t}" : l.Name, null, "0");
            if (Workspace.EnclosingType(doc, offset) is { } enc)
                foreach (var (_, m) in _ws.MembersOf(enc.Name)) Item(m.Name, MemberKind(m), m.Detail, m.Doc, "1");
            foreach (var (_, s) in _ws.Globals())
                if (s.Kind != SymbolKind.Test) Item(s.Name, MemberKind(s), s.Detail, s.Doc, "3");
            foreach (var b in Builtins.Functions) Item(b.Name, 3, b.Signature, b.Doc, "4");
            foreach (var t in Keywords.Primitives) Item(t, 25, "primitive type", null, "6");
            foreach (var (k, d) in Keywords.Docs) Item(k, 14, "keyword", d, "7");
        }

        _completionDocs.Clear();
        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            w.WriteBoolean("isIncomplete"u8, false);
            w.WriteStartArray("items"u8);
            foreach (var it in items)
            {
                w.WriteStartObject();
                w.WriteString("label"u8, it.Label);
                w.WriteNumber("kind"u8, it.Kind);
                if (it.Detail is not null) w.WriteString("detail"u8, it.Detail);
                if (it.Doc is not null) { w.WriteNumber("data"u8, _completionDocs.Count); _completionDocs.Add(it.Doc); }
                if (it.Sort is not null) w.WriteString("sortText"u8, it.Sort);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    /// <summary>Echoes the item back with its documentation filled in.</summary>
    void ResolveCompletion(JsonElement id, JsonElement item)
    {
        string? doc = item.TryGetProperty("data"u8, out var d) && d.TryGetInt32(out int i) && (uint)i < (uint)_completionDocs.Count
            ? _completionDocs[i] : null;
        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            foreach (var prop in item.EnumerateObject())
                if (!prop.NameEquals("documentation"u8)) prop.WriteTo(w);
            if (doc is not null) WriteMarkup(w, "documentation"u8, doc);
            w.WriteEndObject();
        });
    }

    static readonly (string Name, string Doc)[] Attributes =
    [
        ("derive", "Derives members: `@derive(equality)`."),
        ("allow", "Silences a diagnostic; needs `because \"…\"`."),
        ("json", "JSON mapping options; `@json(expose)` needs `because \"…\"`."),
        ("sealed", "Blocks outside field writes; construct and change only through methods or `with`."),
        ("positional", "Opts a constructor into positional arguments of the same kind."),
        ("column", "Database column mapping."),
        ("link", "Links a native library for `extern` declarations."),
    ];

    /// <summary>`.x` with nothing typed before it (or after a keyword / operator) is a target-typed case or state.</summary>
    static bool IsTargetTyped(Document doc, int recv)
    {
        if (recv < 0) return true;
        var rt = doc.Tokens[recv];
        return rt.Kind switch
        {
            TokenKind.Newline or TokenKind.At => true,
            TokenKind.Punct => !(doc.Is(rt, ")") || doc.Is(rt, "]") || doc.Is(rt, "}") || doc.Is(rt, "?")),
            TokenKind.Keyword => !(doc.Is(rt, "this") || doc.Is(rt, "it") || doc.Is(rt, "Self")),
            _ => false,
        };
    }

    static Builtins.Member[] BuiltinMembers(string? type) => type switch
    {
        "string" => Builtins.StringMembers,
        "List" or "Set" or "Map" or "Span" or "ReadOnlySpan" or "Array" => Builtins.CollectionMembers,
        _ => [],
    };

    /// <summary>`type distinct Grams = i64 requires …` → `i64`.</summary>
    static string? UnderlyingType(string detail)
    {
        int eq = detail.IndexOf('=');
        if (eq < 0) return null;
        var rest = detail.AsSpan(eq + 1).TrimStart();
        int sp = rest.IndexOf(' ');
        var t = sp < 0 ? rest : rest[..sp];
        return t.IsEmpty ? null : t.ToString();
    }

    static int MemberKind(Symbol s) => s.Kind switch
    {
        SymbolKind.Struct => 22, SymbolKind.Union or SymbolKind.ErrorUnion => 13, SymbolKind.TypeAlias => 7,
        SymbolKind.Interface => 8, SymbolKind.Effect => 9, SymbolKind.Function => s.Container is null ? 3 : 2,
        SymbolKind.Field => 5, SymbolKind.Case or SymbolKind.State => 20, SymbolKind.Const => 21,
        _ => 6,
    };

    // ---- hover / definition -----------------------------------------------------

    (Document Doc, Symbol Sym)? SymbolAt(Document doc, int offset, out string word, out Token tok)
    {
        word = ""; tok = default;
        int ti = doc.TokenIndexAt(offset);
        if (ti < 0) return null;
        tok = doc.Tokens[ti];
        if (tok.Kind is not (TokenKind.Ident or TokenKind.Keyword)) return null;
        word = doc.TextOf(tok);
        // On a declaration's own name (`let x`, `Type name(`, a param): that symbol.
        foreach (var d in doc.Symbols) if (d.NameStart == tok.Start && d.Kind != SymbolKind.Test) return (doc, d);
        // Member access: `recv.word`.
        if (ti >= 1 && (doc.Is(ti - 1, ".") || doc.Is(ti - 1, "?.")))
        {
            if (!IsTargetTyped(doc, ti - 2))
                return _ws.TypeOfReceiver(doc, ti - 2) is { } type ? _ws.Member(type, word) : null;
            foreach (var e in _ws.CasesAndStates()) if (e.Sym.Name == word) return e;
            return null;
        }
        return _ws.Resolve(doc, word, tok.Start);
    }

    void Hover(JsonElement id, JsonElement p)
    {
        if (!Locate(p, out var doc, out int offset)) { Reply(id, w => w.WriteNull("result"u8)); return; }
        var hit = SymbolAt(doc, offset, out var word, out var tok);
        string? md = null;
        if (hit is { } h)
        {
            var s = h.Sym;
            string code = s.Kind switch
            {
                SymbolKind.Local => $"{(s.Detail.StartsWith("var", StringComparison.Ordinal) ? "var" : "let")} {s.Name}{(s.TypeName is { Length: > 0 } t ? ": " + t : "")}",
                SymbolKind.Param => s.Detail,
                _ => (s.Container is { } c && s.Kind != SymbolKind.Case && s.Kind != SymbolKind.State ? $"// in {c}\n" : "")
                     + (s.Kind is SymbolKind.Case or SymbolKind.State ? $"{s.Container}.{s.Name}" : s.Detail),
            };
            md = $"```bayan\n{code}\n```" + (s.Doc is null ? "" : "\n\n" + s.Doc);
        }
        else if (tok.Kind == TokenKind.Keyword && Keywords.Docs.TryGetValue(word, out var kd)) md = $"**{word}** — {kd}";
        else if (word.Length > 0)
        {
            foreach (var b in Builtins.Functions) if (b.Name == word) { md = $"```bayan\n{b.Signature}\n```\n\n{b.Doc}"; break; }
            int ti = doc.TokenIndexAt(offset);
            bool member = ti >= 1 && (doc.Is(ti - 1, ".") || doc.Is(ti - 1, "?."));
            if (member)
            {
                md = null; // a member never resolves to a free builtin function
                foreach (var b in BuiltinMembers(_ws.TypeOfReceiver(doc, ti - 2)))
                    if (b.Name == word) { md = $"```bayan\n{b.Signature}\n```\n\n{b.Doc}"; break; }
            }
        }
        if (md is null) { Reply(id, w => w.WriteNull("result"u8)); return; }
        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            WriteMarkup(w, "contents"u8, md);
            WriteRange(w, doc, tok.Start, tok.End);
            w.WriteEndObject();
        });
    }

    void Definition(JsonElement id, JsonElement p)
    {
        if (!Locate(p, out var doc, out int offset) || SymbolAt(doc, offset, out _, out _) is not { } hit)
        { Reply(id, w => w.WriteNull("result"u8)); return; }
        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            w.WriteString("uri"u8, hit.Doc.Uri);
            WriteRange(w, hit.Doc, hit.Sym.NameStart, hit.Sym.NameStart + NameLength(hit.Doc, hit.Sym));
            w.WriteEndObject();
        });
    }

    static int NameLength(Document d, Symbol s) =>
        s.Kind == SymbolKind.Test ? s.Name.Length + 2 : s.Name.Length;

    // ---- signature help -----------------------------------------------------------

    void SignatureHelp(JsonElement id, JsonElement p)
    {
        if (!Locate(p, out var doc, out int offset)) { Reply(id, w => w.WriteNull("result"u8)); return; }
        // Walk back to the unmatched `(` and count commas at depth 0.
        int ti = offset == 0 ? -1 : doc.TokenBefore(offset - 1);
        // The token under the cursor only counts if the cursor is past its start (`f(|` vs `|(`).
        if (ti >= 0 && doc.Tokens[ti].Start >= offset) ti--;
        int depth = 0, commas = 0, open = -1;
        for (int i = ti, steps = 0; i >= 0 && steps < 4096; i--, steps++)
        {
            var t = doc.Tokens[i];
            if (t.Kind == TokenKind.Newline)
            {
                // A newline ends the search unless the call's argument list continues across lines.
                if (depth > 0) continue;
                int prev = i - 1;
                while (prev >= 0 && doc.Tokens[prev].Kind is TokenKind.Newline or TokenKind.Comment or TokenKind.DocComment) prev--;
                if (prev >= 0 && (doc.Is(prev, ",") || doc.Is(prev, "("))) continue;
                break;
            }
            if (t.Kind != TokenKind.Punct || t.Length != 1) continue;
            char c = doc.Text[t.Start];
            if (c is ')' or ']' or '}') depth++;
            else if (c is '(' or '[' or '{') { if (depth == 0) { open = i; break; } depth--; }
            else if (c == ',' && depth == 0) commas++;
        }
        if (open < 1 || !doc.Is(open, "(") || doc.Tokens[open - 1].Kind is not (TokenKind.Ident or TokenKind.Keyword))
        { Reply(id, w => w.WriteNull("result"u8)); return; }

        var name = doc.TextOf(doc.Tokens[open - 1]);
        string? label = null, docs = null;
        var hit = SymbolAt(doc, doc.Tokens[open - 1].Start, out _, out _);
        if (hit is { Sym.Kind: SymbolKind.Function } h) { label = h.Sym.Detail; docs = h.Sym.Doc; }
        else foreach (var b in Builtins.Functions) if (b.Name == name) { label = b.Signature; docs = b.Doc; break; }
        if (label is null) { Reply(id, w => w.WriteNull("result"u8)); return; }

        var parms = SplitParams(label);
        Reply(id, w =>
        {
            w.WriteStartObject("result"u8);
            w.WriteStartArray("signatures"u8);
            w.WriteStartObject();
            w.WriteString("label"u8, label);
            if (docs is not null) WriteMarkup(w, "documentation"u8, docs);
            w.WriteStartArray("parameters"u8);
            foreach (var (a, b) in parms)
            {
                w.WriteStartObject();
                w.WriteStartArray("label"u8); w.WriteNumberValue(a); w.WriteNumberValue(b); w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteNumber("activeSignature"u8, 0);
            w.WriteNumber("activeParameter"u8, Math.Min(commas, Math.Max(0, parms.Count - 1)));
            w.WriteEndObject();
        });
    }

    static List<(int, int)> SplitParams(string label)
    {
        var res = new List<(int, int)>();
        int open = label.IndexOf('(');
        if (open < 0) return res;
        int depth = 0, start = open + 1;
        for (int i = open; i < label.Length; i++)
        {
            char c = label[i];
            if (c is '(' or '<' or '[') { depth++; continue; }
            // `->` / `=>` inside a function-typed parameter is not a closing bracket.
            bool close = c is ')' or ']' || (c == '>' && i > 0 && label[i - 1] is not ('-' or '='));
            if (close) depth--;
            if ((c == ',' && depth == 1) || (close && depth == 0))
            {
                int a = start; while (a < i && label[a] == ' ') a++;
                var part = label.AsSpan(a, i - a);
                bool receiver = part.StartsWith("ref this") || part.StartsWith("move this") || part.StartsWith("this ");
                if (i > a && !receiver) res.Add((a, i));
                start = i + 1;
                if (depth == 0) break;
            }
        }
        return res;
    }

    // ---- symbols ---------------------------------------------------------------

    static int OutlineKind(Symbol s) => s.Kind switch
    {
        SymbolKind.Struct => 23, SymbolKind.Union or SymbolKind.ErrorUnion => 10, SymbolKind.TypeAlias => 5,
        SymbolKind.Interface => 11, SymbolKind.Effect => 2, SymbolKind.Function => s.Container is null ? 12 : 6,
        SymbolKind.Field => 8, SymbolKind.Case or SymbolKind.State => 22, SymbolKind.Const => 14,
        SymbolKind.Test => 12, _ => 13,
    };

    void DocumentSymbols(JsonElement id, JsonElement p)
    {
        var uri = p.GetProperty("textDocument"u8).GetProperty("uri"u8).GetString()!;
        if (_ws.Get(uri)?.Full() is not { } doc) { Reply(id, w => w.WriteNull("result"u8)); return; }
        Reply(id, w =>
        {
            w.WriteStartArray("result"u8);
            foreach (var s in doc.Roots) WriteDocSymbol(w, doc, s);
            w.WriteEndArray();
        });
    }

    static void WriteDocSymbol(Utf8JsonWriter w, Document doc, Symbol s)
    {
        w.WriteStartObject();
        w.WriteString("name"u8, s.Kind == SymbolKind.Test ? $"test \"{s.Name}\"" : s.Name);
        w.WriteString("detail"u8, s.Kind is SymbolKind.Function or SymbolKind.Field ? s.TypeName ?? "" : "");
        w.WriteNumber("kind"u8, OutlineKind(s));
        WriteRange(w, doc, s.DeclStart, Math.Max(s.DeclEnd, s.NameStart + s.Name.Length));
        w.WritePropertyName("selectionRange"u8);
        WriteRangeValue(w, doc, s.NameStart, s.NameStart + NameLength(doc, s));
        if (s.Children is { Count: > 0 } children)
        {
            w.WriteStartArray("children"u8);
            foreach (var c in children) WriteDocSymbol(w, doc, c);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    void WorkspaceSymbols(JsonElement id, JsonElement p)
    {
        string q = p.TryGetProperty("query"u8, out var qq) ? qq.GetString() ?? "" : "";
        Reply(id, w =>
        {
            w.WriteStartArray("result"u8);
            int n = 0;
            foreach (var (d, s) in _ws.AllSymbols())
            {
                if (q.Length > 0 && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                w.WriteStartObject();
                w.WriteString("name"u8, s.Name);
                w.WriteNumber("kind"u8, OutlineKind(s));
                if (s.Container is not null) w.WriteString("containerName"u8, s.Container);
                w.WriteStartObject("location"u8);
                w.WriteString("uri"u8, d.Uri);
                WriteRange(w, d, s.NameStart, s.NameStart + NameLength(d, s));
                w.WriteEndObject();
                w.WriteEndObject();
                if (++n >= 500) break;
            }
            w.WriteEndArray();
        });
    }

    // ---- helpers ---------------------------------------------------------------

    bool Locate(JsonElement p, out Document doc, out int offset)
    {
        doc = null!; offset = 0;
        var uri = p.GetProperty("textDocument"u8).GetProperty("uri"u8).GetString()!;
        if (_ws.Get(uri) is not { } d) return false;
        var pos = p.GetProperty("position"u8);
        doc = d.Full();
        offset = d.Offset(pos.GetProperty("line"u8).GetInt32(), pos.GetProperty("character"u8).GetInt32());
        return true;
    }

    static void WriteRange(Utf8JsonWriter w, Document doc, int start, int end)
    {
        w.WritePropertyName("range"u8);
        WriteRangeValue(w, doc, start, end);
    }

    static void WriteRangeValue(Utf8JsonWriter w, Document doc, int start, int end)
    {
        var (sl, sc) = doc.Position(Math.Min(start, doc.Text.Length));
        var (el, ec) = doc.Position(Math.Min(end, doc.Text.Length));
        w.WriteStartObject();
        w.WriteStartObject("start"u8); w.WriteNumber("line"u8, sl); w.WriteNumber("character"u8, sc); w.WriteEndObject();
        w.WriteStartObject("end"u8); w.WriteNumber("line"u8, el); w.WriteNumber("character"u8, ec); w.WriteEndObject();
        w.WriteEndObject();
    }

    static void WriteMarkup(Utf8JsonWriter w, ReadOnlySpan<byte> name, string md)
    {
        w.WriteStartObject(name);
        w.WriteString("kind"u8, "markdown"u8);
        w.WriteString("value"u8, md);
        w.WriteEndObject();
    }

    void Reply(JsonElement id, Action<Utf8JsonWriter> result) => rpc.Write(w =>
    {
        w.WritePropertyName("id"u8);
        id.WriteTo(w);
        result(w);
    });

    void Error(JsonElement id, int code, string message) => rpc.Write(w =>
    {
        w.WritePropertyName("id"u8);
        id.WriteTo(w);
        w.WriteStartObject("error"u8);
        w.WriteNumber("code"u8, code);
        w.WriteString("message"u8, message);
        w.WriteEndObject();
    });
}
