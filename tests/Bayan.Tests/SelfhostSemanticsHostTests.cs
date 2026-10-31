using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// <c>host</c> in <c>selfhost/semantics/express4.byn</c>, pre-ported for T17/T18, against the C# private getter
/// <c>Binder.Host</c> (BinderExpressions.cs:1939-1946), read by reflection on a fresh <c>Binder</c>. The shapes are
/// built by hand on both sides; only the answers come from the C#.
/// </summary>
public sealed class SelfhostSemanticsHostTests
{
    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private static readonly string[] Cases = ["plain", "lambda", "nested", "static", "none"];

    private static string MainSource() => """
        import lexer
        import parser

        string hostName(Binder b, through Ctx c) {
            let h = host(b, through: c)
            return h == -1 ? "none" : c.functions[h].name
        }

        i32 main() {
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
            }
            let m = newFunction(ref c, by: FunctionSymbol { name: "m", kind: .method })
            let l1 = newFunction(ref c, by: FunctionSymbol { name: "l1", kind: .lambda, enclosing: m })
            let l2 = newFunction(ref c, by: FunctionSymbol { name: "l2", kind: .lambda, enclosing: l1 })
            let s = newFunction(ref c, by: FunctionSymbol { name: "s", kind: .function, isStatic: true })

            var b = Binder {
                file: 0,
                fn: -1,
                owner: -1,
                scopes: [],
                targets: [],
                lambdas: [],
                lambdaCount: [],
                seeds: [],
                pending: [],
                constraints: [],
                peers: [],
                nonNull: [],
                stateFacts: [],
                errorNarrowed: [],
            }
            b.fn = m
            print("== plain")
            print(hostName(b, through: c))
            b.fn = l1
            print("== lambda")
            print(hostName(b, through: c))
            b.fn = l2
            print("== nested")
            print(hostName(b, through: c))
            b.fn = s
            print("== static")
            print(hostName(b, through: c))
            b.fn = -1
            print("== none")
            print(hostName(b, through: c))
            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource()));

    private static List<string> Expected(string name)
    {
        var c = Compilation.Create([SourceText.FromString("/host.byn", "void f() {\n}\n")], packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        var file = c.Modules.SelectMany(x => x.Files).First();
        FunctionSymbol Make(string n, FunctionKind kind, FunctionSymbol? enclosing = null, bool isStatic = false) =>
            new(n, null, file.Module, kind) { Enclosing = enclosing, IsStatic = isStatic };
        var m = Make("m", FunctionKind.Method);
        var l1 = Make("l1", FunctionKind.Lambda, m);
        var l2 = Make("l2", FunctionKind.Lambda, l1);
        var s = Make("s", FunctionKind.Function, isStatic: true);
        var fn = name switch { "plain" => m, "lambda" => l1, "nested" => l2, "static" => s, _ => null };
        var binder = Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [c, file, fn, null, null, null], null)!;
        var host = (FunctionSymbol?)BinderType.GetProperty("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder);
        return [host?.Name ?? "none"];
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void HostMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, name, Expected(name));

    public static IEnumerable<object[]> CaseNames() => Cases.Select(x => new object[] { x });
}
