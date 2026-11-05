using Bayan.LanguageServer.Analysis;

namespace Bayan.Tests;

public sealed class LanguageServerWorkspaceTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("bayan-lsp-ws-").FullName;

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

    static List<string> Names(Workspace ws) =>
        ws.Documents.Select(d => Path.GetFileName(new Uri(d.Uri).LocalPath)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void IndexFolderSkipsHiddenTmpAndBuildDirectories()
    {
        Touch("src/a.byn");
        Touch("lib/deep/f.byn");
        Touch("tmp/b.byn");
        Touch(".worktrees/w/c.byn");
        Touch("node_modules/d.byn");
        Touch("tests/cases/run/.bayan-staging-1/e.byn");
        Touch("src/obj/g.byn");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        Assert.Equal(["a.byn", "f.byn"], Names(ws));
    }

    [Fact]
    public void WatcherLoadIgnoresExcludedAndOutsidePaths()
    {
        Touch("src/a.byn");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        ws.LoadFromDisk(Touch("tmp/mutant/new.byn"));
        ws.LoadFromDisk(Touch(".worktrees/x/new2.byn"));
        ws.LoadFromDisk(Touch("src/new3.byn"));
        Assert.Equal(["a.byn", "new3.byn"], Names(ws));
        Assert.True(ws.IsExcluded(Path.Combine(Path.GetTempPath(), "elsewhere", "z.byn")));
        Assert.False(ws.IsExcluded(Path.Combine(_root, "z.byn")));
    }

    [Fact]
    public void FileCountLimitStopsIndexingButNotOpenBuffers()
    {
        for (int i = 0; i < 5; i++) Touch($"src/f{i}.byn");
        var ws = new Workspace(maxFiles: 3);
        ws.IndexFolder(_root);
        Assert.Equal(3, ws.Documents.Count());
        Assert.True(ws.LimitReached);
        ws.Open(new Uri(Path.Combine(_root, "src", "open.byn")).AbsoluteUri, "g() {}\n", 1);
        Assert.Equal(4, ws.Documents.Count());
    }

    [Fact]
    public void CharacterBudgetCountsRemovedFilesBack()
    {
        var a = Touch("src/a.byn", "aaaaaa");
        Touch("src/b.byn", "bbbbbb");
        var ws = new Workspace(maxChars: 10);
        ws.IndexFolder(_root);
        Assert.Single(ws.Documents);
        var first = ws.Documents.Single().Uri;
        ws.Remove(first);
        Assert.Empty(ws.Documents);
        ws.LoadFromDisk(a);
        Assert.Single(ws.Documents);
    }

    [Fact]
    public void OversizedFileIsSkipped()
    {
        Touch("src/small.byn", "x");
        Touch("src/big.byn", new string('x', 100));
        var ws = new Workspace(maxFileBytes: 50);
        ws.IndexFolder(_root);
        Assert.Equal(["small.byn"], Names(ws));
    }

    [Fact]
    public void SymlinkLoopIsNotFollowed()
    {
        if (OperatingSystem.IsWindows()) return; // directory symlinks need a privilege there
        Touch("src/a.byn");
        Directory.CreateSymbolicLink(Path.Combine(_root, "src", "loop"), _root);
        var ws = new Workspace();
        ws.IndexFolder(_root);
        Assert.Equal(["a.byn"], Names(ws));
    }

    [Fact]
    public void LimitRecoversAfterRemoveAndSecondRootIsIndexed()
    {
        for (int i = 0; i < 4; i++) Touch($"a/f{i}.byn");
        Touch("b/x.byn");
        var ws = new Workspace(maxFiles: 3);
        ws.IndexFolder(Path.Combine(_root, "a"));
        Assert.True(ws.LimitReached);
        foreach (var d in ws.Documents.ToList()) ws.Remove(d.Uri);
        Assert.False(ws.LimitReached);
        ws.IndexFolder(Path.Combine(_root, "b"));
        Assert.Equal(["x.byn"], Names(ws));
    }

    [Fact]
    public void CharCountTracksOpenApplyAndClose()
    {
        var a = Touch("a.byn", "0123456789");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        Assert.Equal(10, ws.CharCount);
        var uri = new Uri(a).AbsoluteUri;
        var doc = ws.Open(uri, new string('x', 50_000), 1);
        Assert.Equal(50_000, ws.CharCount);
        doc.Apply(0, 0, 0, 0, "abc");
        Assert.Equal(50_003, ws.CharCount);
        File.Delete(a);
        ws.Close(uri);
        Assert.Equal(0, ws.CharCount);
        Assert.Empty(ws.Documents);
    }

    [Fact]
    public void NestedRootUnderSkippedDirIsIndexed()
    {
        Touch("p/a.byn");
        var inner = Touch("p/tmp/demo/b.byn");
        var ws = new Workspace();
        ws.IndexFolder(Path.Combine(_root, "p"));
        ws.IndexFolder(Path.Combine(_root, "p", "tmp", "demo"));
        Assert.Contains("b.byn", Names(ws));
        Assert.False(ws.IsExcluded(inner));
    }

    [Fact]
    public void CloseAppliesExclusionAndSizeRules()
    {
        Touch("src/a.byn");
        var skipped = Touch(".worktrees/x/a.byn");
        var big = Touch("src/big.byn", new string('x', 100));
        var ws = new Workspace(maxFileBytes: 50);
        ws.IndexFolder(_root);
        var u1 = new Uri(skipped).AbsoluteUri;
        var u2 = new Uri(big).AbsoluteUri;
        ws.Open(u1, "f() {}\n", 1);
        ws.Open(u2, "f() {}\n", 1);
        ws.Close(u1);
        ws.Close(u2);
        Assert.Equal(["a.byn"], Names(ws));
        Assert.Equal(7, ws.CharCount);
    }

    [Fact]
    public void KnownFileGrownPastSizeLimitIsRemoved()
    {
        var a = Touch("src/a.byn", "x");
        var ws = new Workspace(maxFileBytes: 50);
        ws.IndexFolder(_root);
        Assert.Single(ws.Documents);
        File.WriteAllText(a, new string('x', 100));
        ws.LoadFromDisk(a);
        Assert.Empty(ws.Documents);
        Assert.Equal(0, ws.CharCount);
    }

    [Fact]
    public void KnownFileGrownPastCharBudgetIsRemoved()
    {
        var a = Touch("src/a.byn", "xxxx");
        var ws = new Workspace(maxChars: 10);
        ws.IndexFolder(_root);
        File.WriteAllText(a, new string('x', 20));
        ws.LoadFromDisk(a);
        Assert.Empty(ws.Documents);
    }

    [Fact]
    public void SymlinkedFileIsNotIndexed()
    {
        if (OperatingSystem.IsWindows()) return; // file symlinks need a privilege there
        var real = Touch("real/a.byn");
        Touch("src/b.byn");
        File.CreateSymbolicLink(Path.Combine(_root, "src", "link.byn"), real);
        var ws = new Workspace();
        ws.IndexFolder(Path.Combine(_root, "src"));
        Assert.Equal(["b.byn"], Names(ws));
    }

    static long Sum(Workspace ws) => ws.Documents.Sum(d => (long)d.Text.Length);

    [Fact]
    public void WalkStopsAfterFileRejectedForBudget()
    {
        for (int i = 0; i < 4; i++) Touch($"src/f{i}.byn");
        var order = Directory.EnumerateFiles(Path.Combine(_root, "src"), "*.byn").ToList();
        File.WriteAllText(order[0], "aaaaaa");
        File.WriteAllText(order[1], new string('b', 20));
        File.WriteAllText(order[2], "c");
        File.WriteAllText(order[3], "d");
        var ws = new Workspace(maxChars: 10);
        var original = Console.Error;
        var log = new StringWriter();
        Console.SetError(log);
        try { ws.IndexFolder(_root); }
        finally { Console.SetError(original); }
        Assert.Equal([Path.GetFileName(order[0])], Names(ws));
        Assert.Contains("does not fit the index budget", log.ToString());
        Assert.Contains("later files are skipped", log.ToString());
        Assert.DoesNotContain("limit reached", log.ToString());
    }

    [Fact]
    public void CloseKeepsClientUri()
    {
        var a = Touch("Abc.byn", "disk");
        var spelled = new Uri(Path.GetDirectoryName(a)! + Path.DirectorySeparatorChar).AbsoluteUri + "%41bc.byn";
        var ws = new Workspace();
        ws.Open(spelled, "buffer text", 1);
        ws.Close(spelled);
        Assert.Equal(spelled, ws.Get(spelled)!.Uri);
        Assert.Equal("disk", ws.Get(spelled)!.Text);
    }

    [Fact]
    public void KnownFileThatDoesNotGrowIsNeverEvictedForBudget()
    {
        var a = Touch("src/a.byn", "12345678");
        var ws = new Workspace(maxChars: 10);
        ws.IndexFolder(_root);
        ws.Open(new Uri(Path.Combine(_root, "src", "open.byn")).AbsoluteUri, new string('x', 20), 1);
        Assert.True(ws.CharCount > 10);
        File.WriteAllText(a, "12345");
        ws.LoadFromDisk(a);
        Assert.Contains("a.byn", Names(ws));
        Assert.Equal(25, ws.CharCount);
    }

    [Fact]
    public void LoadFromDiskSkipsNewSymlinkedFile()
    {
        if (OperatingSystem.IsWindows()) return; // file symlinks need a privilege there
        var real = Touch("real/a.byn");
        var link = Path.Combine(_root, "src", "link.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, real);
        var ws = new Workspace();
        ws.LoadFromDisk(link);
        Assert.Empty(ws.Documents);
    }

    [Fact]
    public void CloseKeepsPreviousDocumentWhenReloadIsDenied()
    {
        if (OperatingSystem.IsWindows()) return;
        var a = Touch("src/a.byn", "disk");
        var ws = new Workspace();
        var uri = new Uri(a).AbsoluteUri;
        ws.Open(uri, "buffer", 1);
        File.SetUnixFileMode(a, UnixFileMode.None);
        try
        {
            try { File.ReadAllText(a); return; } catch (UnauthorizedAccessException) { } // running as root
            ws.Close(uri);
            var d = ws.Get(uri);
            Assert.NotNull(d);
            Assert.False(d!.IsOpen);
        }
        finally { File.SetUnixFileMode(a, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void CharCountStaysExactWhenOpenReplacesDocumentUnderOtherSpelling()
    {
        var a = Touch("Abc.byn", "disk");
        var ws = new Workspace();
        ws.IndexFolder(_root);
        var old = ws.Documents.Single();
        var spelled = new Uri(Path.GetDirectoryName(a)! + Path.DirectorySeparatorChar).AbsoluteUri + "%41bc.byn";
        ws.Open(spelled, "buffer text", 1);
        Assert.NotSame(old, ws.Get(spelled));
        Assert.Equal(Sum(ws), ws.CharCount);
        old.Replace(new string('z', 500));
        Assert.Equal(Sum(ws), ws.CharCount);
    }

    [Fact]
    public void CharCountStaysExactAfterDirectEditsAndCloseReload()
    {
        var a = Touch("a.byn", "disk");
        var ws = new Workspace();
        var uri = new Uri(a).AbsoluteUri;
        var doc = ws.Open(uri, "buffer", 1);
        doc.Replace("a longer buffer");
        Assert.Equal(Sum(ws), ws.CharCount);
        doc.Apply(0, 0, 0, 2, "");
        Assert.Equal(Sum(ws), ws.CharCount);
        File.WriteAllText(a, "different disk content");
        ws.Close(uri);
        Assert.Equal("different disk content", ws.Get(uri)!.Text);
        Assert.Equal(Sum(ws), ws.CharCount);
    }
}
