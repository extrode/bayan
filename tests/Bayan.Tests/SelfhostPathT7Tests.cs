using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>
/// 014 T7: the four path helpers of <c>selfhost/semantics/compile.byn</c> (<c>t11DirectoryName</c>,
/// <c>t11FullPath</c>, <c>t11Combine</c>, <c>t11RelativePath</c>) against the C# <c>System.IO.Path</c> call each one
/// ports (<c>Compilation.cs</c> 93, 95, 110, 111, 113, 116, 187, 209; <c>Naming.cs</c> 189). One generated
/// <c>main.byn</c> runs every input; every expected value is computed by <c>System.IO.Path</c> at run time. The
/// inputs are built from <c>tmp/t7-path/helpers</c> with the OS separator, so the class runs on macOS and Windows.
/// </summary>
public sealed class SelfhostPathT7Tests
{
    private static string Base => Path.Combine(Golden.RepoRoot, "tmp", "t7-path", "helpers");

    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    /// <summary>Kind, first and second argument for each case the generated main runs.</summary>
    private static List<(string Kind, string A, string B)> Cases()
    {
        var b = Base;
        return
        [
            ("dir", Path.Combine(b, "src", "main.byn"), ""),
            ("dir", Path.Combine(b, "src") + Sep, ""),
            ("dir", b, ""),
            ("full", Path.Combine(b, "a", ".", "x", "..", "c.byn"), ""),
            ("full", b + Sep + ".." + Sep + "t7-path" + Sep + "helpers" + Sep, ""),
            ("full", Path.Combine(b, "a") + Sep + Sep + "b", ""),
            ("combine", b, "src"),
            ("combine", b + Sep, "src"),
            ("combine", b, Path.Combine(b, "other")),
            ("combine", "rel", "x.byn"),
            ("rel", b, Path.Combine(b, "src", "app", "main.byn")),
            ("rel", b, b),
            ("rel", b + Sep, Path.Combine(b, "tests")),
            ("rel", Path.Combine(b, "src", "app"), Path.Combine(b, "src", "lib", "x.byn")),
            ("rel", b, Path.Combine(b, "..", "other", "y.byn")),
            ("relslash", b, Path.Combine(b, "src", "app", "main.byn")),
        ];
    }

    private const string MainSource = """
        i32 main(ReadOnlySpan<string> args) {
            print("== all")
            var i: i64 = 0
            while i + 2 < args.length {
                let kind = args[i]
                let a = args[i + 1]
                let b = args[i + 2]
                if kind == "dir" {
                    print($"dir {a} {t11DirectoryName(a)}")
                } else if kind == "full" {
                    print($"full {a} {t11FullPath(a)}")
                } else if kind == "combine" {
                    print($"combine {a} {b} {t11Combine(a, by: b)}")
                } else if kind == "rel" {
                    print($"rel {a} {b} {t11RelativePath(a, to: b)}")
                } else if kind == "relslash" {
                    print($"relslash {a} {b} {t11RelativePath(a, to: b).replaced("\\", by: "/")}")
                } else if kind == "dirpanic" {
                    print($"dirpanic {a} {t11DirectoryName(a)}")
                }
                i += 3
            }
            return 0
        }
        """;

    /// <summary>A project whose entry file imports a module, a dotted module (resolved by <c>Path.combine</c> over its parts) and a missing one.</summary>
    private static readonly (string Path, string Text)[] Files =
    [
        ("package.byn", "Package { name: \"local/proj\", version: \"0.1.0\" }\n"),
        ("src/app/main.byn", "import a.b\nimport nope.deep\nimport a.b as x\nimport a.c\nimport a.c\n"),
        ("src/a/b/b.byn", "import a.c\n"),
        ("src/a/c/c.byn", "import a.b\n"),
        ("tests/notes.txt", "not a source\n"),
        ("tests/sub.test.byn/keep.txt", "a directory\n"),
        ("tests/x.test.byn", "import a.b\n"),
    ];

    private static string LoadDir => Path.Combine(Golden.RepoRoot, "tmp", "t7-path", "load");

    private const string LoadMain = """
        import lexer
        import parser

        i32 main(ReadOnlySpan<string> args) {
            var options = CompilationOptions { includeTests: args[2] == "t" }
            let c = args[0] != "path" ? fromProject(args[1], for: "app", by: in options) : fromPath(args[1], by: in options, wholeDirectory: true)
            print("== load")
            dumpLoad(in c)
            return 0
        }

        string diagLine(in Ctx c, for in Diag d) {
            let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
            return $"diag {d.code} {c.files[d.file].displayPath} {pos.line}:{pos.column} {d.message} | {d.label} | {d.help}"
        }

        void dumpLoad(in Ctx c) {
            for m in c.moduleOrder {
                print($"module {c.modules[m].name} {c.modules[m].directory} {c.modules[m].cName}")
                for f in c.modules[m].files {
                    let kind = c.files[f].isTestFile ? "test" : "source"
                    print($"file {c.files[f].displayPath} {kind}")
                    for (alias, imported) in c.files[f].imports { print($"import {alias} {c.modules[imported].name}") }
                }
            }
            for d in c.diagnostics { print(diagLine(in c, for: in d)) }
        }
        """;

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"diag {d.Code} {d.Source!.Path} {line}:{col} {d.Message} | {d.Label} | {d.Help}";
    }

    private static string LoadArg(string kind) => kind switch
    {
        "path" => Path.Combine(LoadDir, "src", "app"),
        "empty" => Path.Combine(LoadDir, "empty"),
        _ => LoadDir,
    };

    private static List<string> ExpectedLoad(string kind)
    {
        var options = new CompilationOptions { IncludeTests = kind != "notests" };
        var c = kind != "path"
            ? Compilation.FromProject(LoadArg(kind), "app", options)
            : Compilation.FromPath(LoadArg(kind), options, wholeDirectory: true);
        var lines = new List<string>();
        foreach (var m in c.Modules)
        {
            lines.Add($"module {m.Name} {m.Directory} {m.CName}");
            foreach (var f in m.Files)
            {
                lines.Add($"file {f.Source.Path} {(f.IsTestFile ? "test" : "source")}");
                foreach (var (alias, imported) in f.Imports) lines.Add($"import {alias} {imported.Name}");
            }
        }
        foreach (var d in c.Diagnostics.Items) lines.Add(Diag(d));
        return lines;
    }

    [Theory]
    [InlineData("project")]
    [InlineData("path")]
    [InlineData("notests")]
    [InlineData("empty")]
    public void LoadingMatchesCSharp(string kind)
    {
        if (Directory.Exists(LoadDir)) Directory.Delete(LoadDir, recursive: true);
        foreach (var (path, text) in Files)
        {
            var full = Path.Combine(LoadDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }
        Directory.CreateDirectory(LoadArg("empty"));
        var sections = SelfhostSemanticsHarness.RunSections(LoadMain, kind, LoadArg(kind), kind == "notests" ? "" : "t");
        SelfhostSemanticsHarness.AssertSection(sections, "load", ExpectedLoad(kind));
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Run = new(() =>
    {
        var args = new List<string>();
        foreach (var (k, a, b) in Cases()) args.AddRange([k, a, b]);
        return SelfhostSemanticsHarness.RunSections(MainSource, [.. args]);
    });

    private static string Expected((string Kind, string A, string B) c) => c.Kind switch
    {
        "dir" => $"dir {c.A} {Path.GetDirectoryName(c.A)!}",
        "full" => $"full {c.A} {Path.GetFullPath(c.A)}",
        "combine" => $"combine {c.A} {c.B} {Path.Combine(c.A, c.B)}",
        "rel" => $"rel {c.A} {c.B} {Path.GetRelativePath(c.A, c.B)}",
        "relslash" => $"relslash {c.A} {c.B} {Path.GetRelativePath(c.A, c.B).Replace('\\', '/')}",
        _ => throw new InvalidOperationException(c.Kind),
    };

    private static void AssertKind(string kind)
    {
        var expected = Cases().Where(c => c.Kind == kind).Select(Expected).ToList();
        var actual = Run.Value["all"].Where(l => l.StartsWith(kind + " ", StringComparison.Ordinal)).ToList();
        SelfhostSemanticsHarness.AssertSection(new() { [kind] = actual }, kind, expected);
    }

    [Fact] public void DirectoryNameMatchesCSharp() => AssertKind("dir");

    [Fact] public void FullPathMatchesCSharp() => AssertKind("full");

    [Fact] public void CombineMatchesCSharp() => AssertKind("combine");

    [Fact] public void RelativePathMatchesCSharp() => AssertKind("rel");

    [Fact] public void RelativePathWithSlashNormalizationMatchesCSharp() => AssertKind("relslash");

    [Fact]
    public void DirectoryNameOfRootPanicsWhereCSharpIsNull()
    {
        var root = Path.GetPathRoot(Base)!;
        Assert.Null(Path.GetDirectoryName(root));
        var (exit, stdout, _) = SelfhostParserTests.Run(SelfhostSemanticsHarness.Build(MainSource), "dirpanic", root, "");
        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("dirpanic", stdout);
    }
}
