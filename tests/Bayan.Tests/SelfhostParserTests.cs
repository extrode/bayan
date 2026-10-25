using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary><c>selfhost/parser</c>, built by the bootstrap compiler and run against the C# parser.</summary>
public sealed class SelfhostParserTests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>
    /// The built <c>bayan-parse</c>, or the compiler's complaint. The package root is <c>selfhost/</c> so that
    /// <c>import lexer</c> resolves; <see cref="Compilation.FromPath"/> would use <c>selfhost/parser/</c> (Q430).
    /// </summary>
    private static readonly Lazy<(string? Path, string Error)> BayanParse = new(() =>
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    });

    private static string Built
    {
        get
        {
            var (path, error) = BayanParse.Value;
            if (path is null) Assert.Fail($"selfhost/parser did not build:\n{error}");
            return path!;
        }
    }

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary><see cref="Run"/> with stdout kept as bytes, so invalid UTF-8 is compared as printed.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, params string[] args) => RunBytes(program, null, args);

    /// <summary><see cref="RunBytes(string, string[])"/> with <paramref name="input"/> fed to stdin.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, byte[]? input, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result);
    }

    /// <summary>The stubbed program builds through <c>import lexer</c> and prints the C# dump of an empty file.</summary>
    [Fact]
    public void EmptyFileDumpsAnEmptyCompilationUnit()
    {
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser", "empty.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        var (exit, stdout, stderr) = Run(Built, path);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal("CompilationUnit\n", stdout);
        Assert.Equal(AstDump.Dump(SyntaxTree.Parse(SourceText.FromFile(path))), stdout);
    }
}
