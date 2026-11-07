namespace Bayan.Semantics;

/// <summary>
/// The std modules a build ships (spec 016): <c>lib/std/&lt;module path&gt;/*.byn</c>, embedded in this assembly as
/// <c>std/&lt;module path&gt;/&lt;file&gt;.byn</c>. A module is the files directly in one directory; tests are not part of it.
/// </summary>
public sealed class StdLibrary
{
    private readonly Dictionary<string, List<(string File, byte[] Bytes)>> _modules = new(StringComparer.Ordinal);

    /// <param name="files">Each file's path under <c>std/</c> (<c>json/reader.byn</c>) and its bytes.</param>
    public StdLibrary(IEnumerable<(string Path, byte[] Bytes)> files)
    {
        foreach (var (path, bytes) in files)
        {
            var slash = path.LastIndexOf('/');
            var file = path[(slash + 1)..];
            if (slash < 0 || !file.EndsWith(".byn", StringComparison.Ordinal) || file.EndsWith(".test.byn", StringComparison.Ordinal)) continue;
            var module = path[..slash];
            if (!_modules.TryGetValue(module, out var list)) _modules[module] = list = new();
            list.Add((file, bytes));
        }
        foreach (var list in _modules.Values) list.Sort((a, b) => string.CompareOrdinal(a.File, b.File));
        Names = _modules.Keys.Select(k => "std." + k.Replace('/', '.')).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The modules embedded in this build.</summary>
    public static StdLibrary Shipped { get; } = FromResources();

    /// <summary>Every module's import name (<c>std.json</c>), sorted.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>The files of the module at <paramref name="modulePath"/> (<c>a/b</c> for <c>std.a.b</c>), in name order; null if none ships.</summary>
    public IReadOnlyList<(string File, byte[] Bytes)>? Module(string modulePath) => _modules.GetValueOrDefault(modulePath);

    private static StdLibrary FromResources()
    {
        var assembly = typeof(StdLibrary).Assembly;
        var files = new List<(string, byte[])>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // `RecursiveDir` uses `\` on Windows.
            var path = name.Replace('\\', '/');
            if (!path.StartsWith("std/", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            files.Add((path["std/".Length..], bytes.ToArray()));
        }
        return new StdLibrary(files);
    }
}
