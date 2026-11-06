using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Bayan.LanguageServer;
using Bayan.LanguageServer.Analysis;
using Bayan.LanguageServer.Rpc;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>Memory measurements need the process to themselves.</summary>
[CollectionDefinition(nameof(LanguageServerLeakTests), DisableParallelization = true)]
public sealed class LanguageServerLeakCollection;

[Collection(nameof(LanguageServerLeakTests))]
public sealed class LanguageServerLeakTests(ITestOutputHelper output) : IDisposable
{
    // Real path (macOS /var is a link to /private/var), the form disk documents report.
    readonly string _root = new Workspace().Key(Uri(Directory.CreateTempSubdirectory("bayan-lsp-leak-").FullName));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    string Touch(string rel, string text = "f() {}\n")
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static string Uri(string path) => new System.Uri(path).AbsoluteUri;

    static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    /// <summary>
    /// Input that hands out one message per read and runs an <see cref="Action"/> item when the
    /// server asks for the message after it, so a test can change the disk between messages.
    /// </summary>
    sealed class Script(IEnumerable<object> items) : Stream
    {
        readonly Queue<object> _items = new(items.Select(m => m is Action ? m : Frame(JsonSerializer.Serialize(m))));
        byte[] _cur = [];
        int _pos;

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_pos == _cur.Length)
            {
                if (_items.Count == 0) return 0;
                var next = _items.Dequeue();
                if (next is Action a) { a(); continue; }
                _cur = (byte[])next;
                _pos = 0;
            }
            int n = Math.Min(count, _cur.Length - _pos);
            Array.Copy(_cur, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Runs a server over the messages to end of input and returns every message it wrote.</summary>
    static List<JsonElement> RunServer(params object[] messages)
    {
        var input = new Script(messages);
        var output = new MemoryStream();
        new Server(new JsonRpcTransport(input, output)).Run();
        output.Position = 0;
        var reader = new JsonRpcTransport(output, Stream.Null);
        var res = new List<JsonElement>();
        while (reader.Read() is { } doc) using (doc) res.Add(doc.RootElement.Clone());
        return res;
    }

    static object Notify(string method, object p) => new { jsonrpc = "2.0", method, @params = p };
    static object Request(int id, string method, object p) => new { jsonrpc = "2.0", id, method, @params = p };

    static JsonElement Result(List<JsonElement> msgs, int id) =>
        msgs.Single(m => m.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.GetInt32() == id).GetProperty("result");

    /// <summary>Live heap bytes after full collections; one `GC.GetTotalMemory(true)` alone can stop early.</summary>
    static long Live()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(true);
    }

    static string Dense(int chars)
    {
        var sb = new StringBuilder(chars + 64);
        while (sb.Length < chars) sb.Append("struct S{i64 a}\nvoid f(i64 x){let y=x}\n");
        return sb.ToString();
    }

    [Fact]
    public void ClosedDiskDocumentsRetainAtMostTwelveBytesPerChar()
    {
        var text = Dense(1 << 20);
        long chars = 0;
        for (int i = 0; i < 8; i++) { Touch($"src/d{i}.byn", text); chars += text.Length; }
        var ws = new Workspace();
        long before = Live();
        ws.IndexFolder(_root);
        long after = Live();
        Assert.Equal(chars, ws.CharCount);
        double perChar = (double)(after - before) / chars;
        output.WriteLine($"retained {perChar:F2} bytes per char over {chars} chars");
        Assert.True(perChar <= 12, $"retained {perChar:F2} bytes per char");
        Assert.All(ws.Documents, d => { Assert.Empty(d.Tokens); Assert.Empty(d.Diagnostics); Assert.DoesNotContain(d.Symbols, s => s.IsLocal); });
        Assert.NotEmpty(ws.Named("S"));
        GC.KeepAlive(ws);
    }

    /// <summary>Runs <paramref name="body"/> on a 1 MiB stack and asserts it finished.</summary>
    static void OnSmallStack(Action body)
    {
        var t = new Thread(() => body(), 1 << 20);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(60)), "did not finish");
    }

    [Fact]
    public void DeepInterpolationAndLongReceiverChainDoNotOverflowTheStack()
    {
        var uri = Uri(Path.Combine(_root, "deep.byn"));
        string interp = string.Concat(Enumerable.Repeat("$\"{", 100_000)) + "x" + string.Concat(Enumerable.Repeat("}\"", 100_000));
        string chain = string.Concat(Enumerable.Repeat("a.", 300_000));
        List<JsonElement>? interpMsgs = null, chainMsgs = null;
        OnSmallStack(() => interpMsgs = RunServer(
            Notify("textDocument/didOpen", new { textDocument = new { uri, version = 1, text = interp } })));
        OnSmallStack(() => chainMsgs = RunServer(
            Notify("textDocument/didOpen", new { textDocument = new { uri, version = 1, text = chain } }),
            Request(1, "textDocument/completion", new { textDocument = new { uri }, position = new { line = 0, character = chain.Length } })));
        var diags = Assert.Single(interpMsgs!).GetProperty("params").GetProperty("diagnostics");
        Assert.Single(diags.EnumerateArray(), d => d.GetProperty("message").GetString()!.Contains("nested deeper than 256"));
        Assert.Equal(JsonValueKind.Object, Result(chainMsgs!, 1).ValueKind);
    }

    [Fact]
    public void FolderDeleteAndRenameUpdateTheIndex()
    {
        Touch("a/b/x.byn", "struct InB {}\n");
        Touch("a/b/deep/y.byn", "struct InB2 {}\n");
        Touch("a/keep.byn", "struct Kept {}\n");
        var b = Path.Combine(_root, "a", "b");
        var c = Path.Combine(_root, "a", "c");
        object Query(int id) => Request(id, "workspace/symbol", new { query = "" });
        object Event(string path, int type) => Notify("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri = Uri(path), type } } });
        var msgs = RunServer(
            Request(1, "initialize", new { rootUri = Uri(_root) }),
            Query(2),
            (Action)(() => Directory.Move(b, c)),
            Event(b, 3),
            Query(3),
            Event(c, 1),
            Query(4));
        List<string> Files(int id) => [.. Result(msgs, id).EnumerateArray()
            .Select(s => Path.GetRelativePath(_root, new System.Uri(s.GetProperty("location").GetProperty("uri").GetString()!).LocalPath).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        Assert.Equal(["a/b/deep/y.byn", "a/b/x.byn", "a/keep.byn"], Files(2));
        Assert.Equal(["a/keep.byn"], Files(3));
        Assert.Equal(["a/c/deep/y.byn", "a/c/x.byn", "a/keep.byn"], Files(4));
    }

    [Fact]
    public void FolderCreatedUnderSkippedDirectoryIsNotIndexed()
    {
        Touch("a.byn");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        Touch("tmp/new/b.byn");
        ws.LoadFolder(Path.Combine(_root, "tmp", "new"));
        ws.LoadFolder(Path.Combine(_root, "tmp"));
        Assert.Single(ws.Documents);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference IndexAndReplace(Workspace ws, string root, List<string> files)
    {
        ws.IndexFolder(root);
        Assert.NotEmpty(ws.Globals()); // builds the index
        var old = new WeakReference(ws.Documents.First());
        foreach (var f in files) { File.WriteAllText(f, "struct Changed {}\n"); ws.LoadFromDisk(f); }
        return old;
    }

    [Fact]
    public void ReplacedDocumentsAreCollectableBeforeTheNextRequest()
    {
        var files = Enumerable.Range(0, 4).Select(i => Touch($"src/f{i}.byn", $"struct T{i} {{}}\n")).ToList();
        var ws = new Workspace();
        var old = IndexAndReplace(ws, _root, files);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(old.IsAlive);
        GC.KeepAlive(ws);
    }

    [Fact]
    public void OversizedOpenBufferIsNotAnalysedOrKept()
    {
        var path = Touch("big.byn", "struct Small {}\n");
        var uri = Uri(path);
        var sb = new StringBuilder(5 << 20);
        while (sb.Length < 5 << 20) sb.Append("struct Big {}\n");
        var big = sb.ToString();
        sb = null;
        object[] messages =
        [
            Request(1, "initialize", new { rootUri = Uri(_root) }),
            Notify("textDocument/didOpen", new { textDocument = new { uri, version = 1, text = big } }),
            Notify("textDocument/didChange", new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } }, text = "x" } } }),
            Request(2, "workspace/symbol", new { query = "" }),
            Notify("textDocument/didChange", new { textDocument = new { uri, version = 3 }, contentChanges = new[] { new { text = "struct Fits {}\n" } } }),
            Request(3, "workspace/symbol", new { query = "" }),
        ];
        var sink = new MemoryStream();
        var server = new Server(new JsonRpcTransport(new Script(messages), sink));
        long before = Live();
        server.Run();
        long after = Live();
        output.WriteLine($"retained {after - before} bytes after a {big.Length * 2}-byte didOpen");
        Assert.True(after - before < 2L * big.Length * 2, $"retained {after - before} bytes");
        GC.KeepAlive(server);
        sink.Position = 0;
        var msgs = new List<JsonElement>();
        var reader = new JsonRpcTransport(sink, Stream.Null);
        while (reader.Read() is { } d) using (d) msgs.Add(d.RootElement.Clone());
        // One notice for the oversized buffer, then the normal publish once it fits again.
        var publishes = msgs.Where(m => m.TryGetProperty("method", out _)).Select(m => m.GetProperty("params").GetProperty("diagnostics")).ToList();
        Assert.Equal(2, publishes.Count);
        Assert.Contains("does not analyse it", Assert.Single(publishes[0].EnumerateArray()).GetProperty("message").GetString());
        Assert.Empty(publishes[1].EnumerateArray());
        Assert.Empty(Result(msgs, 2).EnumerateArray());
        Assert.Equal(["Fits"], Result(msgs, 3).EnumerateArray().Select(s => s.GetProperty("name").GetString()!));
    }

    [Fact]
    public void DefinitionAndWorkspaceSymbolReachClosedDocuments()
    {
        var lib = Touch("lib.byn", "struct Order {\n  i64 total\n}\nvoid pay(Order o) {\n  let x = o\n}\n");
        var main = Touch("main.byn", "void run() {\n  let o = Order { total: 1 }\n}\n");
        var msgs = RunServer(
            Request(1, "initialize", new { rootUri = Uri(_root) }),
            Notify("textDocument/didOpen", new { textDocument = new { uri = Uri(main), version = 1, text = File.ReadAllText(main) } }),
            Request(2, "textDocument/definition", new { textDocument = new { uri = Uri(main) }, position = new { line = 1, character = 11 } }),
            Request(3, "workspace/symbol", new { query = "Order" }),
            // A request on a document that was never opened analyses it on demand.
            Request(4, "textDocument/definition", new { textDocument = new { uri = Uri(lib) }, position = new { line = 4, character = 10 } }),
            Request(5, "textDocument/documentSymbol", new { textDocument = new { uri = Uri(lib) } }));
        var def = Result(msgs, 2);
        Assert.Equal(Uri(lib), def.GetProperty("uri").GetString());
        Assert.Equal(0, def.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Contains(Result(msgs, 3).EnumerateArray(), s => s.GetProperty("name").GetString() == "Order");
        var local = Result(msgs, 4);
        Assert.Equal(3, local.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()); // the parameter `o`
        Assert.Contains(Result(msgs, 5).EnumerateArray(), s => s.GetProperty("name").GetString() == "Order" && s.GetProperty("children").GetArrayLength() == 1);
    }

    [Fact]
    public void DiagnosticsAreCappedWithASummaryEntry()
    {
        var doc = new Document("file:///semis.byn", new string(';', 256 * 1024), 1, open: true);
        doc.Analyze();
        Assert.True(doc.Diagnostics.Count <= 1001, $"{doc.Diagnostics.Count} diagnostics");
        Assert.Equal($"{256 * 1024 - 1000} more problems not shown", doc.Diagnostics[^1].Message);
    }

    [Fact]
    public void TransportDropsItsBodyBufferAfterALargeMessage()
    {
        var t = new JsonRpcTransport(new MemoryStream(), Stream.Null);
        var big = new string('x', 2 << 20);
        t.Write(w => w.WriteString("big", big));
        t.Write(w => w.WriteString("small", "y"));
        // No public view of the buffer; read the private field rather than widen the API.
        var body = (System.Buffers.ArrayBufferWriter<byte>)typeof(JsonRpcTransport)
            .GetField("_body", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(t)!;
        Assert.True(body.Capacity < 1 << 20, $"capacity {body.Capacity}");
    }

    static MemoryStream Raw(string text) => new(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void TruncatedBodyEndsTheReadLikeEndOfStream()
    {
        var t = new JsonRpcTransport(Raw("Content-Length: 1000\r\n\r\n{\"id\":1"), Stream.Null);
        Assert.Null(t.Read());
    }

    [Fact]
    public void ContentLengthDoesNotAllocateTheBodyUpFront()
    {
        var t = new JsonRpcTransport(Raw($"Content-Length: {60 << 20}\r\n\r\n{{}}"), Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Null(t.Read());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
    }

    [Fact]
    public void MessagesOverTheLimitAreSkippedAndLargeOnesStillParse()
    {
        var text = new string('a', 300_000);
        var json = $"{{\"text\":\"{text}\"}}";
        var t = new JsonRpcTransport(Raw($"Content-Length: 500000\r\n\r\n{new string(' ', 500_000)}Content-Length: {json.Length}\r\n\r\n{json}"), Stream.Null, maxMessage: 400_000);
        using var doc = t.Read();
        Assert.Equal(text, doc!.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void RewalkingAFolderDoesNotSpendTheDirectoryBound()
    {
        Touch("a/f.byn");
        Touch("a/s/f.byn");
        var ws = new Workspace(maxDirs: 3);
        ws.IndexFolder(Path.Combine(_root, "a"));
        for (int i = 0; i < 5; i++) ws.LoadFolder(Path.Combine(_root, "a")); // 12 entries if re-walks counted
        Touch("a/t/f.byn");
        ws.LoadFolder(Path.Combine(_root, "a", "t"));
        Assert.Equal(3, ws.Documents.Count());
    }

    [Fact]
    public void RemovedFolderRefundsItsDirectories()
    {
        Touch("a/f.byn");
        Touch("a/s/f.byn");
        var ws = new Workspace(maxDirs: 2);
        ws.IndexFolder(Path.Combine(_root, "a"));
        ws.RemoveFolder(Uri(Path.Combine(_root, "a", "s")));
        Touch("a/t/f.byn");
        ws.LoadFolder(Path.Combine(_root, "a", "t"));
        Assert.Equal(2, ws.Documents.Count());
    }

    [Fact]
    public void TooLargeCountsUtf8BytesNotChars()
    {
        var ws = new Workspace(maxFileBytes: 100);
        Assert.False(ws.TooLarge(new string('a', 100)));
        Assert.False(ws.TooLarge(new string('\u00e9', 50)));
        Assert.True(ws.TooLarge(new string('\u00e9', 60))); // 60 chars, 120 bytes
    }

    [Fact]
    public void WatcherChangeDoesNotReloadATooLargeOpenBuffer()
    {
        var file = Touch("big.byn", "struct A {}\n");
        var uri = Uri(file);
        var msgs = RunServer(
            Notify("textDocument/didOpen", new { textDocument = new { uri, languageId = "bayan", version = 1, text = new string('a', Workspace.DefaultMaxFileBytes > int.MaxValue ? 0 : (int)Workspace.DefaultMaxFileBytes + 1) } }),
            Notify("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri, type = 2 } } }),
            Request(1, "textDocument/documentSymbol", new { textDocument = new { uri } }));
        var res = Result(msgs, 1);
        Assert.True(res.ValueKind == JsonValueKind.Null || res.GetArrayLength() == 0, res.ToString());
    }

    [Fact]
    public void FolderWalkStopsAtTheDirectoryBound()
    {
        Touch("r.byn");
        for (int i = 0; i < 5; i++) Touch($"d{i}/f.byn");
        var ws = new Workspace(maxDirs: 3);
        ws.IndexFolder(_root);
        Assert.Equal(3, ws.Documents.Count());
        ws.LoadFolder(Path.Combine(_root, "d0")); // the bound covers every walk together
        Assert.Equal(3, ws.Documents.Count());
    }

    [Fact]
    public void BuildOutputAndLibraryDirectoriesAreSkipped()
    {
        Touch("a.byn");
        foreach (var dir in new[] { "Library", "node_modules", "target", "out", "build" }) Touch($"{dir}/x.byn");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        Assert.Single(ws.Documents);
    }

    [Fact]
    public void SymlinkedRootAndItsTargetAreOneDocument()
    {
        var file = Touch("src/a.byn", "struct A {}\n");
        var links = Directory.CreateTempSubdirectory("bayan-lsp-link-").FullName;
        try
        {
            var link = Path.Combine(links, "root");
            Directory.CreateSymbolicLink(link, _root);
            var ws = new Workspace();
            ws.IndexFolder(link);
            var viaLink = Uri(Path.Combine(link, "src", "a.byn"));
            Assert.Same(ws.Get(Uri(file)), ws.Get(viaLink));
            ws.Open(viaLink, "struct B {}\n", 1);
            ws.Open(Uri(file), "struct C {}\n", 2);
            Assert.Equal(["C"], ws.Documents.SelectMany(d => d.Symbols).Select(s => s.Name));
            Assert.False(ws.IsExcluded(ws.Key(viaLink)));
        }
        finally { Directory.Delete(links, recursive: true); }
    }

    [Fact]
    public void ResolvePrefersTheRecomputedCopyOfTheSameDocument()
    {
        var a = Touch("a.byn", "\nstruct A {\n  i64 total\n}\n");
        var b = Touch("b.byn", "\nstruct B {\n  i64 total\n}\n");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        foreach (var f in new[] { a, b })
        {
            var full = ws.Get(Uri(f))!.Full();
            Assert.Equal(Uri(f), ws.Resolve(full, "total", 0)?.Doc.Uri);
        }
    }

    [Fact]
    public void IsExcludedResolvesLinksLikeRoots()
    {
        var links = Directory.CreateTempSubdirectory("bayan-lsp-link-").FullName;
        try
        {
            var link = Path.Combine(links, "root");
            Directory.CreateSymbolicLink(link, _root);
            var ws = new Workspace();
            ws.IndexFolder(_root);
            Assert.False(ws.IsExcluded(Path.Combine(link, "src", "a.byn")));
            Assert.True(ws.IsExcluded(Path.Combine(link, "tmp", "a.byn")));
            ws = new Workspace();
            ws.IndexFolder(link);
            Assert.False(ws.IsExcluded(Path.Combine(_root, "src", "a.byn")));
        }
        finally { Directory.Delete(links, recursive: true); }
    }
}
