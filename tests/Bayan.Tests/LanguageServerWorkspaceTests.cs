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
}
