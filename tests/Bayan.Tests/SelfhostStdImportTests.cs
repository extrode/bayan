using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>
/// 016 T2: the selfhost's <c>import std.…</c> (compile.byn <c>resolveImport</c>, <c>stdLibraryCreate</c>) against the C#
/// <c>Compilation</c>, the cases of <see cref="StdImportTests"/>. The fixture trees below are written to
/// <c>tmp/std-cases/</c>; one generated <c>main.byn</c> runs every case with the std table read from the case's std
/// directory and prints a section per case. The oracle compiles the same case with <c>CompilationOptions.Std</c> built
/// from the same directory and prints the same lines.
/// </summary>
public sealed class SelfhostStdImportTests
{
    private static string CasesDir => Path.Combine(Golden.RepoRoot, "tmp", "std-cases");

    private const string Probe = """
        public string greet() {
            return "probe"
        }

        string hidden() {
            return "hidden"
        }

        public void save(string text) uses fs {
            Io.writeFile("probe.txt", text) catch { return }
        }
        """;

    /// <summary>Fixture trees: each file's path under <see cref="CasesDir"/> and its text.</summary>
    private static readonly (string Path, string Text)[] Files =
    [
        // Std tables.
        ("stds/probe/probe/probe.byn", Probe),
        ("stds/probe/probe/probe.test.byn", "test \"t\" {}"),
        ("stds/nested/a/a.byn", "public i64 one() {\n    return 1\n}"),
        ("stds/nested/a/b/b.byn", "public string name() {\n    return \"b\"\n}"),
        ("stds/many/zeta/z.byn", ""),
        ("stds/many/probe/p.byn", ""),
        ("stds/many/a/b/c.byn", ""),
        ("stds/chain/a/a.byn", "import std.b\n\npublic i64 twice() {\n    return b.once() + b.once()\n}"),
        ("stds/chain/b/b.byn", "public i64 once() {\n    return 1\n}"),
        ("stds/cycle/a/a.byn", "import std.b"),
        ("stds/cycle/b/b.byn", "import std.a"),
        ("stds/package/a/a.byn", "import util"),
        // Programs.
        ("script/a.byn", "#!/usr/bin/env bayan\nimport std.probe\nprint(probe.greet())"),
        ("script/other.byn", "this does not parse {"),
        ("file/main.byn", "import std.probe\nprint(probe.greet())"),
        ("proj/src/app/main.byn", "import std.probe\nprint(probe.greet())"),
        ("proj/src/std/probe/probe.byn", "public string other( {"),
        ("nested/main.byn", "import std.a.b\nprint(b.name())"),
        ("pkgstd/main.byn", "import std.probe\nprint(probe.greet())"),
        ("pkgstd/std/probe/probe.byn", "public string other( {"),
        ("missing/main.byn", "import std.probe\nprint(probe.greet())"),
        ("missing/std/probe/probe.byn", Probe),
        ("unknown/main.byn", "import std.nope\nimport std\nprint(\"hi\")"),
        ("chain/main.byn", "import std.a\nprint($\"{a.twice()}\")"),
        ("cycle/main.byn", "import std.a\nprint(\"hi\")"),
        ("package/main.byn", "import std.a\nprint(\"hi\")"),
        ("package/util/util.byn", "public i64 one() {\n    return 1\n}"),
        ("hidden/main.byn", "import std.probe\nprint(probe.hidden())"),
        ("effect/main.byn", "import std.probe\n\npublic void store() {\n    probe.save(\"x\")\n}\n\nvoid main() {\n    store()\n}"),
        ("capability/main.byn", "import std.probe\n\nvoid main() {\n    probe.save(\"x\")\n}"),
    ];

    /// <summary>The empty std table, a directory with no files.</summary>
    private const string EmptyStd = "stds/empty";

    /// <summary>The runs: kind, case name, the path given and the std directory (under <see cref="CasesDir"/>), and flags
    /// (<c>w</c> whole directory, <c>c</c> capabilities <c>[net]</c> of package <c>local/p</c>).</summary>
    private static readonly (string Kind, string Name, string Path, string Std, string Flags)[] Runs =
    [
        ("path", "script", "script/a.byn", "stds/probe", ""),
        ("path", "file", "file/main.byn", "stds/probe", "w"),
        ("path", "dir", "file", "stds/probe", ""),
        ("project", "project", "proj", "stds/probe", ""),
        ("path", "nested", "nested", "stds/nested", ""),
        ("path", "package-std", "pkgstd", "stds/probe", ""),
        ("path", "package-std-missing", "missing", EmptyStd, ""),
        ("path", "unknown", "unknown", "stds/many", ""),
        ("path", "chain", "chain", "stds/chain", ""),
        ("path", "cycle", "cycle", "stds/cycle", ""),
        ("path", "std-imports-package", "package", "stds/package", ""),
        ("path", "hidden", "hidden", "stds/probe", ""),
        ("path", "effect", "effect", "stds/probe", ""),
        ("path", "capability", "capability", "stds/probe", "c"),
    ];

    private static readonly Lazy<Dictionary<string, List<string>>> Run = new(() =>
    {
        WriteFixtures();
        var args = new List<string>();
        foreach (var (kind, name, path, std, flags) in Runs)
            args.AddRange([kind, name, Path.Combine(CasesDir, path), Path.Combine(CasesDir, std), flags]);
        return SelfhostSemanticsHarness.RunSections(MainSource, [.. args]);
    });

    private static void WriteFixtures()
    {
        if (Directory.Exists(CasesDir)) Directory.Delete(CasesDir, recursive: true);
        foreach (var (path, text) in Files)
        {
            var full = Path.Combine(CasesDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text.EndsWith('\n') ? text : text + "\n", new UTF8Encoding(false));
        }
        Directory.CreateDirectory(Path.Combine(CasesDir, EmptyStd));
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var r in Runs) data.Add($"{r.Kind} {r.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatchesCSharp(string section)
    {
        var sections = Run.Value;
        var run = Runs.Single(r => $"{r.Kind} {r.Name}" == section);
        SelfhostSemanticsHarness.AssertSection(sections, section, Expected(run.Kind, Path.Combine(CasesDir, run.Path), Path.Combine(CasesDir, run.Std), run.Flags));
    }

    // ── The C# oracle ───────────────────────────────────────────────────────────

    private static List<string> Expected(string kind, string path, string stdDir, string flags)
    {
        if (!Directory.Exists(CasesDir)) WriteFixtures();
        var std = new StdLibrary(Directory.EnumerateFiles(stdDir, "*", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(stdDir, f).Replace('\\', '/'), File.ReadAllBytes(f))));
        var options = flags.Contains('c')
            ? new CompilationOptions(Capabilities: ["net"], PackageName: "local/p", Std: std)
            : new CompilationOptions(Std: std);
        var c = kind == "project"
            ? Compilation.FromProject(path, "app", options)
            : Compilation.FromPath(path, options, wholeDirectory: Directory.Exists(path) || flags.Contains('w'));
        var lines = new List<string>();
        foreach (var m in c.Modules)
        {
            lines.Add($"module {m.Name} {m.Directory} {m.CName}");
            foreach (var f in m.Files)
            {
                lines.Add($"file {f.Source.Path}");
                foreach (var (alias, imported) in f.Imports) lines.Add($"import {alias} {imported.Name}");
            }
        }
        foreach (var d in c.Diagnostics.Items) lines.Add(Diag(d));
        return lines;
    }

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"diag {d.Code} {d.Source!.Path} {line}:{col} {d.Message} | {d.Label} | {d.Help}";
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private const string MainSource = """
        import lexer
        import parser

        i32 main(ReadOnlySpan<string> args) {
            var i: i64 = 0
            while i + 4 < args.length {
                let kind = args[i]
                let name = args[i + 1]
                let path = args[i + 2]
                let stdDir = args[i + 3]
                let flags = args[i + 4]
                print($"== {kind} {name}")
                var std = List<StdSource>()
                readStd(stdDir, at: "", to: ref std)
                var options = CompilationOptions { std: move std }
                if flags.contains("c") {
                    var capabilities = List<string>()
                    capabilities.append("net")
                    options.capabilities = move capabilities
                    options.packageName = "local/p"
                }
                if kind == "project" {
                    let c = fromProject(path, for: "app", by: in options)
                    dump(in c)
                } else {
                    let c = fromPath(path, by: in options, wholeDirectory: flags.contains("w"))
                    dump(in c)
                }
                i += 5
            }
            return 0
        }

        /// Every file under `dir`, its path under the std directory (`prefix` so far) and its text.
        void readStd(string dir, at string prefix, to ref List<StdSource> std) {
            for name in t11ListFiles(dir) {
                let full = t11Combine(dir, by: name)
                let path = prefix.length == 0 ? name : $"{prefix}/{name}"
                if t11DirectoryExists(full) {
                    readStd(full, at: path, to: ref std)
                } else {
                    let bytes = t11ReadAllBytes(full)
                    std.append(StdSource { path: path, text: $"{Utf8.decode(bytes.span, invalid: .replace)}" })
                }
            }
        }

        string diagLine(in Ctx c, for in Diag d) {
            let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
            return $"diag {d.code} {c.files[d.file].displayPath} {pos.line}:{pos.column} {d.message} | {d.label} | {d.help}"
        }

        void dump(in Ctx c) {
            for m in c.moduleOrder {
                print($"module {c.modules[m].name} {c.modules[m].directory} {c.modules[m].cName}")
                for f in c.modules[m].files {
                    print($"file {c.files[f].displayPath}")
                    for (alias, imported) in c.files[f].imports { print($"import {alias} {c.modules[imported].name}") }
                }
            }
            for d in c.diagnostics { print(diagLine(in c, for: in d)) }
        }
        """;
}
