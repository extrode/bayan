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
}
