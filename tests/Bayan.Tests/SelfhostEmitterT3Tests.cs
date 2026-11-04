using System.Text;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

public sealed class SelfhostEmitterT3Tests
{
    // args: mode, then bayan-check's. The dump is one line per node, indented by depth, so a divergence reads as a diff.
    private const string Main = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CompilationOptions = semantics.CompilationOptions

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 1..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            sb.append("== stmts\n")
            for f in p.functions {
                sb.append($"fn {c.functions[f.symbol].name} locals={f.locals.length} ctx={f.contextSlots} panic={f.panicSlots}\n")
                sb.append(t3Stmt(in c, through: in l, at: 1, for: f.body))
            }
            Io.stdout.write($"{sb.finish()}")
            return 0
        }

        string t3Pad(i64 depth) uses alloc {
            var sb = StrBuf()
            for k in 0..depth { sb.append("  ") }
            return $"{sb.finish()}"
        }

        string t3Exprs(in Ctx c, through in LCtx l, for in List<BExpr> xs) uses alloc {
            var sb = StrBuf()
            for k in 0..xs.length { sb.append($"{k == 0 ? "" : ", "}{t3Expr(in c, through: in l, for: xs[k])}") }
            return $"{sb.finish()}"
        }

        string t3Expr(in Ctx c, through in LCtx l, for BExpr e) uses alloc {
            if e == -1 { return "-" }
            let i = i64(e & 0xFFFFFFFF)
            if ltree.isLowered(e) {
                switch ltree.lKindOf(e) {
                    case .indexUnchecked: return $"idxu({t3Expr(in c, through: in l, for: l.tree.indexUncheckeds[i].target)}, {t3Expr(in c, through: in l, for: l.tree.indexUncheckeds[i].index)})"
                    case .nextChar: return $"nextchar({t3Expr(in c, through: in l, for: l.tree.nextChars[i].text)}, {c.locals[l.tree.nextChars[i].position].name})"
                    case .neverValue: return "never"
                    default: return "?"
                }
            }
            switch semantics.boundKind(e) {
                case .literal: return "lit"
                case .local: return c.locals[c.bLocals[i].local].name
                case .binary: return $"({c.bBinaries[i].op.ordinal} {t3Expr(in c, through: in l, for: c.bBinaries[i].left)} {t3Expr(in c, through: in l, for: c.bBinaries[i].right)})"
                case .index: return $"idx({t3Expr(in c, through: in l, for: c.bIndexes[i].target)}, {t3Expr(in c, through: in l, for: c.bIndexes[i].index)})"
                case .field: return $"fld({t3Expr(in c, through: in l, for: c.bFields[i].receiver)})"
                case .tupleElement: return $"tup({t3Expr(in c, through: in l, for: c.bTupleElements[i].receiver)})"
                case .unary: return $"un({c.bUnaries[i].op.ordinal} {t3Expr(in c, through: in l, for: c.bUnaries[i].operand)})"
                case .call: return $"call {c.functions[c.bCalls[i].function].name}({t3Expr(in c, through: in l, for: c.bCalls[i].receiver)}; {t3Exprs(in c, through: in l, for: in c.bCalls[i].arguments)})"
                case .errorVariant: return "errvar"
                case .conversion: return $"conv({t3Expr(in c, through: in l, for: c.bConversions[i].operand)})"
                case .builtinCall: return $"bc{c.bBuiltinCalls[i].member.ordinal}({t3Expr(in c, through: in l, for: c.bBuiltinCalls[i].receiver)}; {t3Exprs(in c, through: in l, for: in c.bBuiltinCalls[i].arguments)})"
                default: return "?"
            }
        }

        string t3Stmt(in Ctx c, through in LCtx l, at i64 depth, for BStmt s) uses alloc {
            let pad = t3Pad(depth)
            let i = i64(s & 0xFFFFFFFF)
            if ltree.isLowered(s) {
                switch ltree.lKindOf(s) {
                    case .label: return $"{pad}label {l.tree.labels[i].name}\n"
                    case .gotoStmt: return $"{pad}goto {l.tree.gotoStmts[i].label}\n"
                    case .loop: return $"{pad}loop\n{t3Stmt(in c, through: in l, at: depth + 1, for: l.tree.loops[i].body)}"
                    case .returnStmt: return $"{pad}lreturn {t3Expr(in c, through: in l, for: l.tree.returnStmts[i].value)}\n"
                    case .returnError: return $"{pad}returnerror {t3Expr(in c, through: in l, for: l.tree.returnErrors[i].code)}\n"
                    case .setContext: return $"{pad}setcontext {t3Expr(in c, through: in l, for: l.tree.setContexts[i].arena)} {l.tree.setContexts[i].slot}\n"
                    case .freeArena: return $"{pad}freearena {t3Expr(in c, through: in l, for: l.tree.freeArenas[i].arena)}\n"
                    case .pushPanicHandler:
                        let panic = l.tree.pushPanicHandlers[i].panic
                        return $"{pad}pushpanic {l.tree.pushPanicHandlers[i].slot} {panic == -1 ? "-" : c.locals[panic].name}\n{t3Stmt(in c, through: in l, at: depth + 1, for: l.tree.pushPanicHandlers[i].body)}"
                    case .popPanicHandler: return $"{pad}poppanic {l.tree.popPanicHandlers[i].slot}\n"
                    default: return $"{pad}?\n"
                }
            }
            switch semantics.boundKind(s) {
                case .block:
                    var sb = StrBuf()
                    sb.append($"{pad}block\n")
                    for inner in c.bBlocks[i].statements { sb.append(t3Stmt(in c, through: in l, at: depth + 1, for: inner)) }
                    return $"{sb.finish()}"
                case .localDecl: return $"{pad}decl {c.locals[c.bLocalDecls[i].local].name} {t3Expr(in c, through: in l, for: c.bLocalDecls[i].initializer)}\n"
                case .exprStmt: return $"{pad}expr {t3Expr(in c, through: in l, for: c.bExprStmts[i].expr)}\n"
                case .assign:
                    let op = c.bAssigns[i].op
                    var opText = "-"
                    if op is not null { opText = $"{op.ordinal}" }
                    return $"{pad}assign {opText} {t3Expr(in c, through: in l, for: c.bAssigns[i].target)} {t3Expr(in c, through: in l, for: c.bAssigns[i].value)}\n"
                case .returnStmt: return $"{pad}return {t3Expr(in c, through: in l, for: c.bReturns[i].value)}\n"
                case .setOnPanic: return $"{pad}setonpanic\n"
                case .ifStmt:
                    var sb = StrBuf()
                    sb.append($"{pad}if {t3Expr(in c, through: in l, for: c.bIfs[i].condition)}\n")
                    sb.append(t3Stmt(in c, through: in l, at: depth + 1, for: c.bIfs[i].then))
                    if c.bIfs[i].elseBranch != -1 {
                        sb.append($"{pad}else\n")
                        sb.append(t3Stmt(in c, through: in l, at: depth + 1, for: c.bIfs[i].elseBranch))
                    }
                    return $"{sb.finish()}"
                default: return $"{pad}?\n"
            }
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter with the T3 main did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var dir = SelfhostEmitterHarness.EmitterDir;
        var sources = Directory.GetFiles(dir, "*.byn")
            .Where(p => Path.GetFileName(p) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(dir, "main.byn"), Main));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Writes <paramref name="source"/> to a temporary file, then runs <paramref name="act"/> on its path.</summary>
    private static void WithFixture(string source, Action<string> act)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-t3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, source);
            act(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Compilation Compile(string path) =>
        Compilation.Create([SourceText.FromFile(path)], Path.GetDirectoryName(path)!);

    // ── The C# oracle: the same dump as the generated main's ─────────────────────

    private static List<string> Oracle(Compilation c)
    {
        var sb = new StringBuilder();
        foreach (var f in Lowerer.Lower(c).Functions)
        {
            sb.Append($"fn {f.Symbol.Name} locals={f.Locals.Count} ctx={f.ContextSlots} panic={f.PanicSlots}\n");
            Stmt(sb, 1, f.Body);
        }
        return sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Expr(BoundExpr? e) => e switch
    {
        null => "-",
        LIndexUnchecked x => $"idxu({Expr(x.Target)}, {Expr(x.Index)})",
        LNextChar x => $"nextchar({Expr(x.Text)}, {x.Position.Name})",
        LNever => "never",
        BoundLiteral => "lit",
        BoundLocal x => x.Local.Name,
        BoundBinary x => $"({(int)x.Op} {Expr(x.Left)} {Expr(x.Right)})",
        BoundIndex x => $"idx({Expr(x.Target)}, {Expr(x.Index)})",
        BoundField x => $"fld({Expr(x.Receiver)})",
        BoundTupleElement x => $"tup({Expr(x.Receiver)})",
        BoundConversion x => $"conv({Expr(x.Operand)})",
        BoundBuiltinCall x => $"bc{(int)x.Member}({Expr(x.Receiver)}; {string.Join(", ", x.Arguments.Select(Expr))})",
        BoundUnary x => $"un({(int)x.Op} {Expr(x.Operand)})",
        BoundCall x => $"call {x.Function.Name}({Expr(x.Receiver)}; {string.Join(", ", x.Arguments.Select(Expr))})",
        BoundErrorVariant => "errvar",
        _ => "?",
    };

    private static void Stmt(StringBuilder sb, int depth, BoundStmt s)
    {
        var pad = new string(' ', depth * 2);
        switch (s)
        {
            case LLabel x: sb.Append($"{pad}label {x.Name}\n"); break;
            case LGoto x: sb.Append($"{pad}goto {x.Label}\n"); break;
            case LLoop x: sb.Append($"{pad}loop\n"); Stmt(sb, depth + 1, x.Body); break;
            case LReturn x: sb.Append($"{pad}lreturn {Expr(x.Value)}\n"); break;
            case LReturnError x: sb.Append($"{pad}returnerror {Expr(x.Code)}\n"); break;
            case LSetContext x: sb.Append($"{pad}setcontext {Expr(x.Arena)} {x.Slot}\n"); break;
            case LFreeArena x: sb.Append($"{pad}freearena {Expr(x.Arena)}\n"); break;
            case LPushPanicHandler x: sb.Append($"{pad}pushpanic {x.Slot} {x.Panic?.Name ?? "-"}\n"); Stmt(sb, depth + 1, x.Body); break;
            case LPopPanicHandler x: sb.Append($"{pad}poppanic {x.Slot}\n"); break;
            case BoundBlock x:
                sb.Append($"{pad}block\n");
                foreach (var inner in x.Statements) Stmt(sb, depth + 1, inner);
                break;
            case BoundLocalDecl x: sb.Append($"{pad}decl {x.Local.Name} {Expr(x.Initializer)}\n"); break;
            case BoundExprStmt x: sb.Append($"{pad}expr {Expr(x.Expr)}\n"); break;
            case BoundAssign x: sb.Append($"{pad}assign {(x.Op is { } op ? ((int)op).ToString() : "-")} {Expr(x.Target)} {Expr(x.Value)}\n"); break;
            case BoundReturn x: sb.Append($"{pad}return {Expr(x.Value)}\n"); break;
            case BoundSetOnPanic: sb.Append($"{pad}setonpanic\n"); break;
            case BoundIf x:
                sb.Append($"{pad}if {Expr(x.Condition)}\n");
                Stmt(sb, depth + 1, x.Then);
                if (x.Else is not null)
                {
                    sb.Append($"{pad}else\n");
                    Stmt(sb, depth + 1, x.Else);
                }
                break;
            default: sb.Append($"{pad}?\n"); break;
        }
    }

    private static List<string> Generated(string path)
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(), ["stmts", path]);
        Assert.True(exit == 0, $"generated main exited {exit}:\n{stderr}");
        return SelfhostSemanticsHarness.Sections(stdout)["stmts"];
    }

    private static void AssertMatches(string source) => WithFixture(source, path =>
    {
        var c = Compile(path);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: false));
        var expected = Oracle(c);
        Assert.Contains(expected, l => l.Length > 0);
        Assert.Equal(expected, Generated(path));
    });

    // ── Fixtures: trivial expressions only, since `lowerExpr` is T4's ─────────────

    private const string Loops = """
        void loops(i64 n) {
            var i = 0
            while i < n { i += 1 }
            do { i -= 1 } while i > 0
            while i < n {
                if i == 3 { break }
                if i == 4 { continue }
                i += 1
            }
            for k in 0..n { i = k }
            for k in 0..=n {
                if k == 2 { continue }
                i = k
            }
            for k in 0..n {
                for j in 0..k { i = j }
            }
        }

        void main() { }
        """;

    private const string Iterables = """
        void iterables(List<i64> xs, at i64[4] a, by ReadOnlySpan<i64> sp, for string s, to Map<string, i64> m) {
            var t = 0
            for x in xs { t = x }
            for x in a { t = x }
            for x in sp { t = x }
            for ch in s { t = 1 }
            for e in m { t = 2 }
            for x in xs {
                if x == 1 { break }
            }
        }

        void main() { }
        """;

    private const string Branches = """
        i64 branches(i64 a, by i64 b) {
            var r = 0
            if a < b { return a }
            if a == b { r = 1 } else { r = 2 }
            r = a + b
            r *= b
            if never r == 9 { r = 1 }
            switch a {
                default:
                    r = 2
            }
            if r == 3 { return b } else { return r }
        }

        void main() { }
        """;

    private const string Cleanups = """
        union error E {
            bad
        }

        void cleanups(i64 n) {
            var i = 0
            defer i = 1
            {
                defer i = 2
                defer i = 3
                i = n
            }
            for k in 0..n {
                defer i = k
                if k == 1 { continue }
                if k == 2 { break }
            }
            defer panicking print("p")
            while i < n {
                defer i = 5
                i += 1
            }
        }

        void failing(i64 n) fails E {
            var m = n
            defer failing m = 1
            if m == 2 { fail .bad }
            while m < 3 {
                defer failing m = 4
                m += 1
            }
        }

        void arenas(Arena arena, by i64 n) {
            var i = 0
            provide allocator: arena {
                i = n
            }
            {
                using let local = Arena()
                i = 1
            }
        }

        void main() { }
        """;

    private const string Places = """
        struct P {
            i64 x
            i64[3] ys
        }

        void places(ref P p, through ref List<i64> xs, at ref i64[4] a, for i64 n) {
            p.x = n
            p.x += n
            p.ys[1] = n
            p.ys[n] += n
            a[n] = 1
            a[n + 1] -= 2
            xs[n] = 3
            xs[n] += 1
            xs[n + 1] *= 2
        }

        void main() { }
        """;

    private const string Users = """
        struct Digits : IIndexable {
            type Item = i64
            i64 count => 3
            i64 get(i64 index) => index * 10
        }

        struct Sized : IIndexable {
            type Item = i64
            i64 count
            i64 get(i64 index) => index
        }

        struct Countdown : IEnumerator {
            type Item = i64
            i64 left
            bool moveNext(ref this) {
                left -= 1
                return left >= 0
            }
            i64 current => left
        }

        void users(Digits d, by Sized s, to ref Countdown cd) {
            var t = 0
            for x in d { t = x }
            for x in s {
                if x == 1 { break }
                t = x
            }
            for x in cd { t = x }
        }

        void main() { }
        """;

    [Theory]
    [InlineData(nameof(Users), Users)]
    [InlineData(nameof(Loops), Loops)]
    [InlineData(nameof(Iterables), Iterables)]
    [InlineData(nameof(Branches), Branches)]
    [InlineData(nameof(Cleanups), Cleanups)]
    [InlineData(nameof(Places), Places)]
    public void Statements_MatchCSharp(string name, string source)
    {
        Assert.NotEmpty(name);
        AssertMatches(source);
    }

    /// A compilation with errors: `lower` panics with the message the C# throws.
    [Fact]
    public void Lower_WithErrorsFailsLikeCSharp() => WithFixture("void main() { let x: i64 = \"s\" }\n", path =>
    {
        var e = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(Compile(path)));
        var (exit, _, err) = SelfhostParserTests.Run(Build(), ["stmts", path]);
        Assert.NotEqual(0, exit);
        Assert.Contains($"panic: {e.Message}", err);
    });
}
