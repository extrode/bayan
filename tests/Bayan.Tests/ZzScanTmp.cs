using Bayan.Semantics;
namespace Bayan.Tests;
public sealed class ZzScanTmp
{
    [Fact]
    public void Scan()
    {
        var root = Golden.RepoRoot;
        var lines = new List<string>();
        foreach (var dir in new[] { "tests/cases", "suq", "selfhost", "examples", "tools" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root, dir), "*.byn", SearchOption.AllDirectories))
            {
                if (f.EndsWith("package.byn")) continue;
                try
                {
                    var c = Compilation.FromPath(f, new CompilationOptions(IncludeTests: true), wholeDirectory: false);
                    foreach (var d in c.Diagnostics.Items.Where(d => d.Code is "BYN5014" or "BYN5009" or "BYN5001"))
                        lines.Add($"{d.Code}\t{Path.GetRelativePath(root, f)}\t{d.Message}");
                }
                catch (Exception e) { lines.Add($"EXC\t{f}\t{e.Message}"); }
            }
        File.WriteAllLines(Path.Combine(root, "tmp", "scan.txt"), lines);
    }
}
