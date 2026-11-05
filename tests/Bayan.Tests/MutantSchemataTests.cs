using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T5: mutant schemata in lowering and C (R2, R11).</summary>
public sealed class MutantSchemataTests
{
    private static readonly string RunDir = Path.Combine(Golden.CasesRoot, "run");

    /// <summary>A case's build mode, from its first line, as <see cref="RunTests"/> reads it.</summary>
    internal static BuildMode ModeOf(string input)
    {
        var firstLine = File.ReadLines(input).FirstOrDefault() ?? "";
        var mode = BuildMode.Safe;
        if (firstLine.StartsWith("// mode: ", StringComparison.Ordinal))
            Toolchain.TryParseMode(firstLine["// mode: ".Length..].Trim(), out mode);
        return mode;
    }

    internal static Compilation CompileCase(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var tests = name.EndsWith(".test.byn", StringComparison.Ordinal);
        var c = Compilation.Create([Golden.Load(input)], Path.GetDirectoryName(input)!,
            tests ? new CompilationOptions(IncludeTests: true, Library: true, Target: "linux-x64") : new CompilationOptions(Target: "linux-x64"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return c;
    }

    /// <summary>The C of a <c>--mutate</c> build.</summary>
    internal static string EmitSchemata(Compilation c, BuildMode mode, out Schema schema)
    {
        schema = Schemata.Build(c);
        return CEmitter.Emit(Lowerer.Lower(c, schema.Switches), mode == BuildMode.Debug);
    }

    private sealed record Outcome(string Stdout, string Stderr, int ExitCode);

    /// <summary>Runs <paramref name="exe"/> with <c>BAYAN_MUTANT</c> set to <paramref name="mutant"/>, or unset when it is null.</summary>
    private static Outcome Run(string exe, string? mutant, string? input = null, string? work = null, string[]? args = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        if (work is not null) psi.WorkingDirectory = work;
        if (mutant is null) psi.Environment.Remove("BAYAN_MUTANT");
        else psi.Environment["BAYAN_MUTANT"] = mutant;
        foreach (var arg in args ?? []) psi.ArgumentList.Add(arg);
        if (input is not null && File.Exists(Path.ChangeExtension(input, ".args")))
            foreach (var arg in File.ReadAllLines(Path.ChangeExtension(input, ".args"))) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        if (input is not null && File.Exists(Path.ChangeExtension(input, ".stdin")))
            p.StandardInput.BaseStream.Write(File.ReadAllBytes(Path.ChangeExtension(input, ".stdin")));
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            Assert.Fail($"{exe} timed out after 120 s");
        }
        var code = p.ExitCode;
        code = PanicExit.Portable(code);
        return new Outcome(stdout.Result, stderr.Result, code);
    }

    /// <summary>The <c>run/</c> cases that are programs; a <c>*.test.byn</c> case is a test binary, checked by <see cref="MutantZeroIsThePlainTestBinary"/>.</summary>
    public static TheoryData<string> ProgramCases() => Select(tests: false);

    /// <summary>The <c>run/</c> cases <c>bayan test</c> runs.</summary>
    public static TheoryData<string> TestCases() => Select(tests: true);

    private static TheoryData<string> Select(bool tests)
    {
        var data = new TheoryData<string>();
        foreach (var row in (IEnumerable<object[]>)RunTests.Cases)
            if (((string)row[0]).EndsWith(".test.byn", StringComparison.Ordinal) == tests) data.Add((string)row[0]);
        return data;
    }

    private static readonly ConcurrentDictionary<string, Lazy<string>> Builds = new();

    /// <summary>Starts every schemata build in the background (<see cref="WarmUp"/>), so the sequential theory mostly waits on finished work.</summary>
    private static readonly Lazy<bool> Warm = new(() =>
    {
        WarmUp.Start(((IEnumerable<object[]>)ProgramCases()).Select(row => (string)row[0]), name => SchemataBuild(name));
        return true;
    });

    private static string SchemataBuild(string name) => Builds.GetOrAdd(name, n => new Lazy<string>(() =>
    {
        var input = Path.Combine(Golden.CasesRoot, n);
        var mode = ModeOf(input);
        var build = Toolchain.Build(EmitSchemata(CompileCase(n), mode, out _), mode);
        return build.Succeeded ? build.ExecutablePath! : throw new InvalidOperationException($"{n} did not build:\n{build.Errors}");
    }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>R2 law: with no mutant selected (unset, <c>0</c>, or not an integer) a schemata build behaves as the plain program.</summary>
    [Theory]
    [MemberData(nameof(ProgramCases))]
    public void MutantZeroIsThePlainProgram(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var head = File.ReadLines(input).Take(2).ToList();
        if (OperatingSystem.IsWindows() ? head.Contains("// os: unix") : head.Contains("// os: windows")) return;
        _ = Warm.Value;
        var exe = SchemataBuild(name);
        var work = Path.Combine(Golden.RepoRoot, "tmp", "run-schemata", Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_'));
        foreach (var mutant in new[] { null, "0", "x1" })
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            var outcome = Run(exe, mutant, input, work);
            string Expected(string extension) => File.Exists(Path.ChangeExtension(input, extension))
                ? File.ReadAllText(Path.ChangeExtension(input, extension)).Replace("\r\n", "\n") : "";
            var label = $"{name}, BAYAN_MUTANT={mutant ?? "unset"}";
            Assert.True(Expected(".stdout") == outcome.Stdout.Replace("\r\n", "\n"), $"{label}: stdout differs:\n{outcome.Stdout}");
            Assert.True(Expected(".stderr") == outcome.Stderr.Replace("\r\n", "\n"), $"{label}: stderr differs:\n{outcome.Stderr}");
            Assert.True(Expected(".exitcode").Trim() == (outcome.ExitCode == 0 ? "" : outcome.ExitCode.ToString()), $"{label}: exit code {outcome.ExitCode}");
        }
    }

    /// <summary>
    /// R2 law for a <c>*.test.byn</c> case: every test of its schemata test binary, run as <c>bayan test</c> runs it
    /// (<c>--test i</c>), gives the plain test binary's stdout, stderr and exit code with no mutant selected. The case's
    /// golden files are the CLI's report of those runs (RunTests checks them), so equal runs give the plain output.
    /// </summary>
    [Theory]
    [MemberData(nameof(TestCases))]
    public void MutantZeroIsThePlainTestBinary(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var mode = ModeOf(input);
        var c = CompileCase(name);
        var tests = Lowerer.Lower(c).Tests.Count;
        Assert.True(tests > 0, $"{name} has no tests");
        var plain = Toolchain.Build(CliApp.EmitC(c, mode), mode);
        Assert.True(plain.Succeeded, plain.Errors);
        var schemata = Toolchain.Build(EmitSchemata(c, mode, out _), mode);
        Assert.True(schemata.Succeeded, schemata.Errors);
        for (var i = 0; i < tests; i++)
        {
            string[] args = ["--test", i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            var expected = Run(plain.ExecutablePath!, null, args: args);
            foreach (var mutant in new[] { null, "0", "x1" })
                Assert.True(expected == Run(schemata.ExecutablePath!, mutant, args: args),
                    $"{name}, test {i}, BAYAN_MUTANT={mutant ?? "unset"}: differs from the plain test binary");
        }
    }

    private static (Schema Schema, string Exe) Program(string text)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "schemata");
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(EmitSchemata(c, BuildMode.Safe, out var schema), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        return (schema, build.ExecutablePath!);
    }

    private const string Variants = """
        union error E {
            bad
            worse
        }

        i64 check(i64 n) fails E {
            fail .bad if n < 0
            return n
        }

        i64 handle(i64 n) => check(n) catch switch {
            E.bad => 1,
            E.worse => 2
        }

        print($"{handle(-1)}")

        """;

    /// <summary>R2: selecting a mutant's id runs that mutant, and only it; one row per operator family.</summary>
    [Theory]
    [InlineData(Mutator.Boundary, "bool f(i32 a) {\n    return a < 10\n}\n\nprint($\"{f(10)}\")\n", "a < 10", "a <= 10", "false", "true")]
    [InlineData(Mutator.Relational, "bool f(i32 a) {\n    return a == 2\n}\n\nprint($\"{f(2)}\")\n", "a == 2", "a != 2", "true", "false")]
    [InlineData(Mutator.Logical, "bool f(bool a, bool b) {\n    return a && b\n}\n\nprint($\"{f(a: true, b: false)}\")\n", "a && b", "a || b", "false", "true")]
    [InlineData(Mutator.Condition, "i32 f(i32 n) {\n    if n > 3 {\n        return 1\n    }\n    return 0\n}\n\nprint($\"{f(5)}\")\n", "n > 3", "!(n > 3)", "1", "0")]
    [InlineData(Mutator.Bool, "bool f() {\n    return true\n}\n\nprint($\"{f()}\")\n", "true", "false", "true", "false")]
    [InlineData(Mutator.Arithmetic, "i32 f(i32 a) {\n    return a + 1\n}\n\nprint($\"{f(2)}\")\n", "a + 1", "a - 1", "3", "1")]
    [InlineData(Mutator.Literal, "i32 f() {\n    return 7\n}\n\nprint($\"{f()}\")\n", "7", "8", "7", "8")]
    [InlineData(Mutator.Negation, "i32 f(i32 x) {\n    return -x\n}\n\nprint($\"{f(3)}\")\n", "-x", "x", "-3", "3")]
    [InlineData(Mutator.Statement, "void f() {\n    print(\"hi\")\n}\n\nf()\n", "print(\"hi\")", "", "hi", "")]
    [InlineData(Mutator.EmptyReturn, "i32 f(i32 x) {\n    return x\n}\n\nprint($\"{f(5)}\")\n", "x", "0", "5", "0")]
    [InlineData(Mutator.Variant, Variants, ".bad", ".worse", "1", "2")]
    public void SelectingAMutantRunsIt(string op, string text, string original, string mutated, string plain, string expected)
    {
        var (schema, exe) = Program(text);
        var site = Assert.Single(schema.Sites, s => s.Site.Operator == op && s.Site.Original == original && s.Site.Mutated == mutated);
        Assert.True(site.Emitted);
        string Out(string s) => s.Length == 0 ? "" : s + "\n";
        Assert.Equal(Out(plain), Run(exe, null).Stdout);
        Assert.Equal(Out(expected), Run(exe, site.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Stdout);
    }

    /// <summary>A mutant that re-binds with an error is not emitted: no arm in the C, <c>"emitted": false</c> in the site map, counted per operator.</summary>
    [Fact]
    public void AMutantThatDoesNotRebindIsNotEmitted()
    {
        // Removing `a = List<i64>()` leaves `a` used after its move (BYN2781); `4 => 5` makes `(250 + 5) + 1` overflow u8.
        var (schema, exe) = Program("""
            i64 total() {
                var a = List<i64>()
                var b = move a
                a = List<i64>()
                a.append(1)
                b.append(2)
                return a.length + b.length
            }

            u8 top() {
                return (250 + 4) + 1
            }

            print($"{total()} {top()}")

            """);
        var moved = Assert.Single(schema.Sites, s => s.Site.Operator == Mutator.Statement && s.Site.Original == "a = List<i64>()");
        var overflow = Assert.Single(schema.Sites, s => s.Site.Operator == Mutator.Literal && s.Site.Original == "4" && s.Site.Mutated == "5");
        Assert.False(moved.Emitted);
        Assert.False(overflow.Emitted);
        Assert.Contains(schema.Sites, s => s.Emitted);
        Assert.DoesNotContain(schema.Switches.Values, sw => sw.Mutants.Any(m => m.Id == moved.Id || m.Id == overflow.Id));
        Assert.True(schema.NotEmitted[Mutator.Statement] >= 1 && schema.NotEmitted[Mutator.Literal] >= 1);
        var json = schema.SitesJson();
        Assert.Contains($"\"id\": {moved.Id},", json);
        Assert.Equal(schema.Sites.Count(s => !s.Emitted), json.Split("\"emitted\": false").Length - 1);
        Assert.Equal("2 255\n", Run(exe, moved.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Stdout);
    }

    /// <summary>The instances of a generic share their template's site: one id, an arm in every instance.</summary>
    [Fact]
    public void GenericInstancesShareTheirSite()
    {
        var (schema, exe) = Program("i64 count<T>(T v) {\n    var n: i64 = 7\n    return n\n}\n\nprint($\"{count(1) + count(true)}\")\n");
        var site = Assert.Single(schema.Sites, s => s.Site.Operator == Mutator.Literal && s.Site.Original == "7" && s.Site.Mutated == "8");
        Assert.Equal(2, schema.Switches.Values.Count(sw => sw.Mutants.Any(m => m.Id == site.Id)));
        Assert.Equal("14\n", Run(exe, null).Stdout);
        Assert.Equal("16\n", Run(exe, site.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Stdout);
    }

    /// <summary>A switch is a C conditional only when no arm has a prelude; otherwise each arm's prelude runs inside its own branch.</summary>
    [Fact]
    public void ArmsWithPreludesBranch()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "schemata");
        string C(string text, out Schema schema) => EmitSchemata(Compilation.Create([SourceText.FromString(Path.Combine(dir, "main.byn"), text)], dir), BuildMode.Safe, out schema);
        var plain = C("i32 f(i32 a) {\n    return a + 1\n}\n\nprint($\"{f(2)}\")\n", out _);
        Assert.Contains("return (byn_mutant_is(1) ? ", plain);
        Assert.DoesNotContain("if (byn_mutant_is", plain);
        // `try` lowers to a failing call before the `+`: the `-` mutant gets its own call in its own branch.
        var prelude = C(Variants.Replace("return n", "return (try check(n - 1)) + 1"), out var schema);
        var site = Assert.Single(schema.Sites, s => s.Site.Operator == Mutator.Arithmetic && s.Site.Original == "(try check(n - 1)) + 1");
        var branch = prelude.IndexOf($"if (byn_mutant_is({site.Id})) {{", StringComparison.Ordinal);
        Assert.True(branch >= 0, prelude);
        var body = prelude[branch..prelude.IndexOf("\n}\n", branch, StringComparison.Ordinal)];
        Assert.Equal(2, body.Split("= byn_main__check(").Length - 1);
    }

    /// <summary>The site map: ids 1..N in site order, the same across two builds of the same input.</summary>
    [Fact]
    public void SiteMapIsStable()
    {
        const string text = "i32 f(i32 a) {\n    return a + 1\n}\n\nprint($\"{f(2)}\")\n";
        var first = Program(text).Schema;
        Assert.Equal(Enumerable.Range(1, first.Sites.Count), first.Sites.Select(s => s.Id));
        Assert.StartsWith("{\n  \"version\": 1,\n  \"sites\": [\n    {\n      \"id\": 1,\n      \"file\": ", first.SitesJson());
        Assert.Equal(first.SitesJson(), Program(text).Schema.SitesJson());
    }

    /// <summary>R11: a build without <c>--mutate</c> emits the same C as before T5; the hashes were taken from the merge-base build.</summary>
    [Fact]
    [Trait("Law", "L020")]
    public void PlainBuildEmitsTheSameC()
    {
        var lines = new StringBuilder();
        foreach (var row in (IEnumerable<object[]>)RunTests.Cases)
        {
            var name = (string)row[0];
            var c = CompileCase(name);
            var source = CliApp.EmitC(c, ModeOf(Path.Combine(Golden.CasesRoot, name)));
            lines.Append(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))).Append("  ").Append(name).Append('\n');
        }
        var result = Golden.Compare(Path.Combine(Golden.CasesRoot, "emit", "run-c.sha256"), lines.ToString(), Golden.UpdateMode);
        if (result.Failure is { } failure) Assert.Fail(failure);
    }
}
