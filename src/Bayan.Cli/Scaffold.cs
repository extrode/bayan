using System.Text;
using Bayan.Backend.C;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Packages;

namespace Bayan.Cli;

public enum Layout { App, Lib, Script }

/// <summary>
/// <c>bayan new</c> and <c>bayan init</c>: an app, library or script that builds and passes its
/// tests with no edits. Git use is <c>git init</c> only, and only outside an existing work tree.
/// </summary>
public static class Scaffold
{
    private sealed record Options(string? Target, Layout Layout, bool Git, bool CiGithub);

    public static int New(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseOptions(args, "new", stderr) is not { } o) return CliApp.ExitUsage;
        if (o.Target is null)
        {
            stderr.WriteLine("bayan: expected a name, e.g. `bayan new tally`");
            return CliApp.ExitUsage;
        }
        var full = Path.GetFullPath(o.Target).TrimEnd(Path.DirectorySeparatorChar);
        var dirName = Path.GetFileName(full);
        if (Names(dirName, o.Layout, stderr) is not { } names) return CliApp.ExitUsage;
        if (o.Layout == Layout.Script)
        {
            var file = full + ".byn";
            if (File.Exists(file))
            {
                stderr.WriteLine($"bayan: `{o.Target}.byn` already exists");
                return CliApp.ExitUsage;
            }
            Write(Path.GetDirectoryName(file)!, Files(names, o), stdout: null);
            stdout.WriteLine($"created script `{o.Target}.byn`; run it with `bayan {o.Target}.byn`");
            return CliApp.ExitOk;
        }
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
        {
            stderr.WriteLine($"bayan: `{o.Target}` already exists and isn't empty");
            stderr.WriteLine($"  help: run `bayan init` inside it to add only the missing files");
            return CliApp.ExitUsage;
        }
        Directory.CreateDirectory(full);
        Write(full, Files(names, o), stdout: null);
        stdout.WriteLine($"created {(o.Layout == Layout.Lib ? "library" : "app")} `local/{names.Kebab}` in `{o.Target}`");
        InitGit(full, o, stdout);
        stdout.WriteLine();
        stdout.WriteLine($"  cd {o.Target}");
        if (o.Layout == Layout.App) stdout.WriteLine("  bayan run");
        stdout.WriteLine("  bayan test");
        return CliApp.ExitOk;
    }

    public static int Init(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseOptions(args, "init", stderr) is not { } o) return CliApp.ExitUsage;
        var dir = Path.GetFullPath(o.Target ?? ".").TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(dir))
        {
            stderr.WriteLine($"bayan: `{o.Target}` doesn't exist; `bayan new {o.Target}` creates it");
            return CliApp.ExitUsage;
        }
        if (File.Exists(Path.Combine(dir, "package.byn")))
        {
            stderr.WriteLine("bayan: this directory already has a `package.byn`");
            return CliApp.ExitUsage;
        }
        if (Names(Path.GetFileName(dir), o.Layout, stderr) is not { } names) return CliApp.ExitUsage;
        Write(dir, Files(names, o), stdout);
        if (o.Layout != Layout.Script) InitGit(dir, o, stdout);
        return CliApp.ExitOk;
    }

    private static Options? ParseOptions(string[] args, string command, TextWriter stderr)
    {
        string? target = null;
        var layout = Layout.App;
        bool git = true, ci = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lib": layout = Layout.Lib; break;
                case "--script": layout = Layout.Script; break;
                case "--vcs" when i + 1 < args.Length && args[i + 1] is "git" or "none":
                    git = args[++i] == "git";
                    break;
                case "--ci" when i + 1 < args.Length && args[i + 1] is "github" or "none":
                    ci = args[++i] == "github";
                    break;
                case var a when a.StartsWith('-') || target is not null:
                    stderr.WriteLine($"bayan: unexpected argument `{a}`");
                    stderr.WriteLine($"usage: bayan {command} {(command == "new" ? "<name>" : "[<dir>]")} [--lib|--script] [--vcs git|none] [--ci github|none]");
                    return null;
                default:
                    target = args[i];
                    break;
            }
        }
        return new Options(target, layout, git, ci);
    }

    /// <summary>The directory name as given, its kebab-case package name and camelCase module name.</summary>
    private sealed record ProjectNames(string Kebab, string Module);

    /// <summary>
    /// A directory name may be kebab-case (<c>my-app</c>) or camelCase (<c>myApp</c>); the package
    /// name is its kebab-case form and a library's module its camelCase form.
    /// </summary>
    private static ProjectNames? Names(string dirName, Layout layout, TextWriter stderr)
    {
        var camel = dirName.Length > 0 && char.IsAsciiLetterLower(dirName[0]) && dirName.All(char.IsAsciiLetterOrDigit);
        var kebab = camel ? Manifest.KebabOf(dirName) : dirName;
        if (!Manifest.IsKebabName(kebab))
        {
            stderr.WriteLine($"bayan: `{dirName}` can't be a package name");
            stderr.WriteLine($"  help: use lowercase words joined by `-` (`my-app`) or camelCase (`myApp`); start with a letter");
            return null;
        }
        var module = Manifest.CamelOf(kebab);
        if (layout == Layout.Lib && Keywords.Lookup(Encoding.UTF8.GetBytes(module)) != TokenKind.Identifier)
        {
            stderr.WriteLine($"bayan: `{module}` is a keyword, so `import {module};` couldn't name the library");
            stderr.WriteLine($"  help: pick another name, e.g. `{kebab}-lib`");
            return null;
        }
        return new ProjectNames(kebab, module);
    }

    /// <summary>Writes every file that doesn't exist yet; with <paramref name="stdout"/>, lists what it did.</summary>
    private static void Write(string root, SortedDictionary<string, string> files, TextWriter? stdout)
    {
        foreach (var (relative, contents) in files)
        {
            var path = Path.Combine(root, relative);
            if (File.Exists(path))
            {
                stdout?.WriteLine($"skipped {relative} (already exists)");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            if (contents.StartsWith("#!", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            stdout?.WriteLine($"created {relative}");
        }
    }

    private static void InitGit(string dir, Options o, TextWriter stdout)
    {
        if (!o.Git) return;
        if (Toolchain.FindOnPath("git") is not { } git)
        {
            stdout.WriteLine("note: git isn't installed, so no repository was created");
            return;
        }
        if (Git(git, dir, "rev-parse", "--is-inside-work-tree") is (0, "true"))
        {
            stdout.WriteLine("note: already inside a git repository, so no new one was created");
            return;
        }
        if (Git(git, dir, "init", "--quiet") is (0, _)) stdout.WriteLine("initialized a git repository (nothing committed)");
        else stdout.WriteLine("note: `git init` failed, so no repository was created");
    }

    private static (int Code, string Stdout) Git(string git, string dir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(git)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        _ = stderr.Result;
        return (p.ExitCode, stdout.Trim());
    }

    // ── Templates ────────────────────────────────────────────────────────────────

    private static SortedDictionary<string, string> Files(ProjectNames n, Options o)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (o.Layout == Layout.Script)
        {
            files[n.Kebab + ".byn"] = """
                #!/usr/bin/env bayan
                #:io threaded

                print("Hello, world!");

                """;
            return files;
        }
        var lib = o.Layout == Layout.Lib;
        files["package.byn"] = $$"""
            Package {
                name    = "local/{{n.Kebab}}",      // `local/` until published under a verified namespace
                version = "0.1.0",
                edition = 2027,
                kind    = .{{(lib ? "lib" : "app")}},
                capabilities = [],
                deps    = [],
                dist = Dist {
                    targets = [.host],        // the machine that builds it
                    mode    = .safe,
                },
            }

            """;
        files["README.md"] = lib
            ? $"""
                # {n.Kebab}

                A Bayan library. Its code is in `src/{n.Module}/`; other packages use it with `import {n.Module};`.

                ## Test

                ```
                bayan test
                ```

                """
            : $"""
                # {n.Kebab}

                A Bayan app. Its code is in `src/app/`.

                ## Run

                ```
                bayan run
                ```

                ## Test

                ```
                bayan test
                ```

                """;
        files[".gitignore"] = "/out/\n/dist/\n/gen/\n";
        files[".gitattributes"] = "* text=auto eol=lf\n*.byn linguist-language=C#\n";
        files[".editorconfig"] = """
            root = true

            [*]
            charset = utf-8
            end_of_line = lf
            indent_style = space
            indent_size = 4
            insert_final_newline = true
            trim_trailing_whitespace = true

            """;
        if (lib)
        {
            files[$"src/{n.Module}/{n.Module}.byn"] = """
                /// Returns twice `x`.
                public i64 twice(i64 x) {
                    return x * 2;
                }

                """;
            files[$"tests/{n.Module}.test.byn"] = """
                test "twice doubles" {
                    assert(twice(21) == 42);
                }

                """;
        }
        else
        {
            files["src/app/main.byn"] = """
                /// The line this app prints.
                string greeting() {
                    return "Hello, world!";
                }

                print(greeting());

                """;
            files["tests/app.test.byn"] = """
                test "greets the world" {
                    assert(greeting() == "Hello, world!");
                }

                """;
        }
        files["testdata/.gitkeep"] = "";
        files["docs/.gitkeep"] = "";
        if (o.CiGithub)
        {
            files[".github/workflows/bayan.yml"] = $"""
                name: bayan

                on: [push, pull_request]

                jobs:
                  build:
                    runs-on: ubuntu-latest
                    steps:
                      - uses: actions/checkout@v4
                      # Install zig and bayan here; neither has an official setup action yet.
                      - run: bayan {(lib ? "check" : "build")}
                      - run: bayan test

                """;
        }
        return files;
    }
}
