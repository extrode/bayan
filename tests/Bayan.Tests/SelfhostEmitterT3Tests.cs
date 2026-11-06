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
                for local in f.locals {
                    let name = c.locals[local].name
                    let type = c.locals[local].type
                    sb.append($"  local {name}:{semantics.typeDisplay(ref c, by: type)}\n")
                }
                sb.append(t3Stmt(ref c, through: in l, at: 1, for: f.body))
            }
            Io.stdout.write($"{sb.finish()}")
            return 0
        }

        string t3Pad(i64 depth) uses alloc {
            var sb = StrBuf()
            for k in 0..depth { sb.append("  ") }
            return $"{sb.finish()}"
        }

        string t3Args(ref Ctx c, through in LCtx l, for BExpr e) uses alloc {
            let i = i64(e & 0xFFFFFFFF)
            let call = semantics.boundKind(e) == .call
            let n = call ? c.bCalls[i].arguments.length : c.bBuiltinCalls[i].arguments.length
            var sb = StrBuf()
            for k in 0..n {
                let a = call ? c.bCalls[i].arguments[k] : c.bBuiltinCalls[i].arguments[k]
                let x = t3Expr(ref c, through: in l, for: a)
                sb.append($"{k == 0 ? "" : ", "}{x}")
            }
            return $"{sb.finish()}"
        }

        string t3Value(in semantics.Constant k) uses alloc {
            switch k.kind {
                case .integer: return $"{k.integer.hi}:{k.integer.lo}"
                case .text: return $"\"{k.text}\""
                case .flag: return k.flag ? "true" : "false"
                default: return "?"
            }
        }

        string t3Type(ref Ctx c, by Type t) uses alloc => semantics.typeDisplay(ref c, by: t)

        string t3Expr(ref Ctx c, through in LCtx l, for BExpr e) uses alloc {
            if e == -1 { return "-" }
            let i = i64(e & 0xFFFFFFFF)
            if ltree.isLowered(e) {
                switch ltree.lKindOf(e) {
                    case .indexUnchecked:
                        let target = t3Expr(ref c, through: in l, for: l.tree.indexUncheckeds[i].target)
                        let index = t3Expr(ref c, through: in l, for: l.tree.indexUncheckeds[i].index)
                        let type = t3Type(ref c, by: l.tree.indexUncheckeds[i].type)
                        return $"idxu({target}, {index}):{type}"
                    case .nextChar:
                        let text = t3Expr(ref c, through: in l, for: l.tree.nextChars[i].text)
                        return $"nextchar({text}, {c.locals[l.tree.nextChars[i].position].name})"
                    case .neverValue: return "never"
                    default: return "?"
                }
            }
            switch semantics.boundKind(e) {
                case .literal:
                    let type = t3Type(ref c, by: c.bLiterals[i].type)
                    return $"lit:{type} {t3Value(in c.bLiterals[i].value)}"
                case .local: return c.locals[c.bLocals[i].local].name
                case .binary:
                    let op = c.bBinaries[i].op.ordinal
                    let left = t3Expr(ref c, through: in l, for: c.bBinaries[i].left)
                    let right = t3Expr(ref c, through: in l, for: c.bBinaries[i].right)
                    let type = t3Type(ref c, by: c.bBinaries[i].type)
                    return $"({op} {left} {right}):{type}"
                case .index:
                    let target = t3Expr(ref c, through: in l, for: c.bIndexes[i].target)
                    let index = t3Expr(ref c, through: in l, for: c.bIndexes[i].index)
                    let type = t3Type(ref c, by: c.bIndexes[i].type)
                    return $"idx({target}, {index}, end={c.bIndexes[i].fromEnd ? 1 : 0}):{type}"
                case .field:
                    let receiver = t3Expr(ref c, through: in l, for: c.bFields[i].receiver)
                    return $"fld({receiver}).{c.fields[c.bFields[i].field].name}"
                case .tupleElement:
                    let receiver = t3Expr(ref c, through: in l, for: c.bTupleElements[i].receiver)
                    let type = t3Type(ref c, by: c.bTupleElements[i].type)
                    return $"tup({receiver}).{c.bTupleElements[i].index}:{type}"
                case .unary:
                    let op = c.bUnaries[i].op.ordinal
                    let operand = t3Expr(ref c, through: in l, for: c.bUnaries[i].operand)
                    return $"un({op} {operand})"
                case .call:
                    let receiver = t3Expr(ref c, through: in l, for: c.bCalls[i].receiver)
                    let arguments = t3Args(ref c, through: in l, for: e)
                    let type = t3Type(ref c, by: c.bCalls[i].type)
                    return $"call {c.functions[c.bCalls[i].function].name}({receiver}; {arguments}):{type}"
                case .errorVariant: return "errvar"
                case .conversion:
                    let operand = t3Expr(ref c, through: in l, for: c.bConversions[i].operand)
                    let type = t3Type(ref c, by: c.bConversions[i].type)
                    return $"conv{c.bConversions[i].kind.ordinal}({operand}):{type}"
                case .builtinCall:
                    let member = c.bBuiltinCalls[i].member.ordinal
                    let receiver = t3Expr(ref c, through: in l, for: c.bBuiltinCalls[i].receiver)
                    let arguments = t3Args(ref c, through: in l, for: e)
                    let type = t3Type(ref c, by: c.bBuiltinCalls[i].type)
                    return $"bc{member}({receiver}; {arguments}):{type}"
                default: return "?"
            }
        }

        string t3Stmt(ref Ctx c, through in LCtx l, at i64 depth, for BStmt s) uses alloc {
            let pad = t3Pad(depth)
            let i = i64(s & 0xFFFFFFFF)
            if ltree.isLowered(s) {
                switch ltree.lKindOf(s) {
                    case .label: return $"{pad}label {l.tree.labels[i].name}\n"
                    case .gotoStmt: return $"{pad}goto {l.tree.gotoStmts[i].label}\n"
                    case .loop: return $"{pad}loop\n{t3Stmt(ref c, through: in l, at: depth + 1, for: l.tree.loops[i].body)}"
                    case .returnStmt: return $"{pad}lreturn {t3Expr(ref c, through: in l, for: l.tree.returnStmts[i].value)}\n"
                    case .returnError: return $"{pad}returnerror {t3Expr(ref c, through: in l, for: l.tree.returnErrors[i].code)}\n"
                    case .setContext: return $"{pad}setcontext {t3Expr(ref c, through: in l, for: l.tree.setContexts[i].arena)} {l.tree.setContexts[i].slot}\n"
                    case .freeArena: return $"{pad}freearena {t3Expr(ref c, through: in l, for: l.tree.freeArenas[i].arena)}\n"
                    case .pushPanicHandler:
                        let panic = l.tree.pushPanicHandlers[i].panic
                        return $"{pad}pushpanic {l.tree.pushPanicHandlers[i].slot} {panic == -1 ? "-" : c.locals[panic].name}\n{t3Stmt(ref c, through: in l, at: depth + 1, for: l.tree.pushPanicHandlers[i].body)}"
                    case .popPanicHandler: return $"{pad}poppanic {l.tree.popPanicHandlers[i].slot}\n"
                    default: return $"{pad}?\n"
                }
            }
            switch semantics.boundKind(s) {
                case .block:
                    var sb = StrBuf()
                    sb.append($"{pad}block\n")
                    for k in 0..c.bBlocks[i].statements.length {
                        let inner = c.bBlocks[i].statements[k]
                        sb.append(t3Stmt(ref c, through: in l, at: depth + 1, for: inner))
                    }
                    return $"{sb.finish()}"
                case .localDecl: return $"{pad}decl {c.locals[c.bLocalDecls[i].local].name} {t3Expr(ref c, through: in l, for: c.bLocalDecls[i].initializer)}\n"
                case .exprStmt: return $"{pad}expr {t3Expr(ref c, through: in l, for: c.bExprStmts[i].expr)}\n"
                case .assign:
                    let op = c.bAssigns[i].op
                    var opText = "-"
                    if op is not null { opText = $"{op.ordinal}" }
                    return $"{pad}assign {opText} {t3Expr(ref c, through: in l, for: c.bAssigns[i].target)} {t3Expr(ref c, through: in l, for: c.bAssigns[i].value)}\n"
                case .returnStmt: return $"{pad}return {t3Expr(ref c, through: in l, for: c.bReturns[i].value)}\n"
                case .setOnPanic: return $"{pad}setonpanic\n"
                case .ifStmt:
                    var sb = StrBuf()
                    sb.append($"{pad}if {t3Expr(ref c, through: in l, for: c.bIfs[i].condition)}\n")
                    sb.append(t3Stmt(ref c, through: in l, at: depth + 1, for: c.bIfs[i].then))
                    if c.bIfs[i].elseBranch != -1 {
                        sb.append($"{pad}else\n")
                        sb.append(t3Stmt(ref c, through: in l, at: depth + 1, for: c.bIfs[i].elseBranch))
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
            foreach (var local in f.Locals) sb.Append($"  local {local.Name}:{local.Type.DisplayName}\n");
            Stmt(sb, 1, f.Body);
        }
        return sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Value(ConstantValue v) => v.Value switch
    {
        Int128 n => $"{(ulong)(n >> 64)}:{(ulong)n}",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => "?",
    };

    private static string Expr(BoundExpr? e) => e switch
    {
        null => "-",
        LIndexUnchecked x => $"idxu({Expr(x.Target)}, {Expr(x.Index)}):{x.Type.DisplayName}",
        LNextChar x => $"nextchar({Expr(x.Text)}, {x.Position.Name})",
        LNever => "never",
        BoundLiteral x => $"lit:{x.Type.DisplayName} {Value(x.Value)}",
        BoundLocal x => x.Local.Name,
        BoundBinary x => $"({(int)x.Op} {Expr(x.Left)} {Expr(x.Right)}):{x.Type.DisplayName}",
        BoundIndex x => $"idx({Expr(x.Target)}, {Expr(x.Index)}, end={(x.FromEnd ? 1 : 0)}):{x.Type.DisplayName}",
        BoundField x => $"fld({Expr(x.Receiver)}).{x.Field.Name}",
        BoundTupleElement x => $"tup({Expr(x.Receiver)}).{x.Index}:{x.Type.DisplayName}",
        BoundConversion x => $"conv{(int)x.Kind}({Expr(x.Operand)}):{x.Type.DisplayName}",
        BoundBuiltinCall x => $"bc{(int)x.Member}({Expr(x.Receiver)}; {string.Join(", ", x.Arguments.Select(Expr))}):{x.Type.DisplayName}",
        BoundUnary x => $"un({(int)x.Op} {Expr(x.Operand)})",
        BoundCall x => $"call {x.Function.Name}({Expr(x.Receiver)}; {string.Join(", ", x.Arguments.Select(Expr))}):{x.Type.DisplayName}",
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

    private const string More = """
        union error E {
            bad
        }

        interface IC {
            void bump(ref this)
            i64 n
        }

        struct C : IC {
            i64 n
            void bump(ref this) {
                n += 1
            }
        }

        @positional
        struct Pt {
            i64 x
            i64 y
        }

        struct Box {
            (i64 a, i64 b) pair
            i64 total
            states {
                open
                done(i64 final)
            }
            void finish(ref this is .open, i64 v) becomes .done {
                become .done(v)
            }
        }

        struct Cn {
            i64 v
            i64 w = 7

            Cn(i64 n) {
                v = n
            }
        }

        E mk() { return .bad }

        void declared(i64 n) {
            var x: i64
            x = n
            using let xs = List<i64>()
        }

        void bare(i64 n) {
            if n == 1 { return }
        }

        i64 viaDefer(i64 a, by i64 b) {
            var r = 0
            defer r = 1
            return a + b
        }

        void more(i64 n, through ref (i64 a, i64 b) t, for string s, to ref Pt pt) {
            var r = 0
            r *= n + 1
            t.a += n
            for b in s.bytes { r = 1 }
            for l in s.lines { r = 2 }
            pt.x = n
        }

        void items(List<i64> xs, to List<C> cs, through Span<C> w) {
            for x: i64? in xs { }
            for x: dyn ref IC in w { x.bump() }
        }

        struct Cs : IIndexable {
            type Item = C
            i64 count => 1
            C get(i64 index) => C { n: index }
        }

        void items2(Cs cs, by ReadOnlySpan<C> r) {
            for x: dyn ref IC in cs { }
            for x: dyn ref readonly IC in r { }
        }

        i64 id(i64 n) => n

        void switched(i64 n) {
            var r = 0
            switch id(n) {
                default:
                    r = 1
            }
        }

        void hooks() {
            defer panicking (p) print("p")
        }

        void hooks2(Panic p) { }

        void main() {
            Panic.handler = hooks2
        }
        """;

    private const string Rvalues = """
        struct Digits : IIndexable {
            type Item = i64
            i64 count => 3
            i64 get(i64 index) => index * 10
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

        struct Holder {
            ((i64 a, i64 b) p, i64 c) t
        }

        Digits mkDigits() { return Digits {} }
        Countdown mkCount() { return Countdown { left: 3 } }
        List<i64> mkList() {
            var l = List<i64>()
            return l
        }
        i64[2] mkArr() => [1, 2]
        void nothing() { }

        void rvalues(i64 n, through ref Holder h) {
            var t = 0
            for x in mkDigits() { t = x }
            for x in mkCount() { t = x }
            for x in mkList() { t = x }
            for x in mkArr() { t = x }
            h.t.p.a += n
        }

        void main() { }
        """;

    private const string Typed = """
        struct Q {
            i64 a
            i64 b
        }

        void typed(List<i32> xs, at ref u8[4] a, by ReadOnlySpan<i16> sp, through ref (i64 a, i64 b) t, to ref Q q, for ref List<i64> ys, as i32 n, from i64 m) {
            for x in xs { let y = x }
            for x in a { let y = x }
            for x in sp { let y = x }
            for k in 0..n { let y = k }
            t.b += 1
            q.b += 2
            ys[^1] = 3
            ys[^(m + 1)] += 4
            a[^(m + 2)] = 5
        }

        void main() { }
        """;

    /// T4's lines in lowerstmt.byn: never operands, `??=`, pattern sections and lvalues of non-place receivers (Q870).
    private const string Exprs = """
        union error E {
            bad(i64 n)
        }

        union V {
            a(i64 n)
            b
        }

        struct H {
            (Ci a, i64 b) t
            Ci c
        }

        struct AH {
            Arena a
            i64 n
        }

        interface IS {
            i64 area()
        }

        struct Ci : IS {
            i64 r
            i64 area() => r
        }

        struct Digits : IIndexable {
            type Item = i64
            i64 count
            i64 get(i64 index) => index * 10
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

        i64 g() => 1
        i64 h() fails E { return 1 }
        void nothing() { }
        void v2() => nothing()
        H mkH() => H { t: (Ci { r: 1 }, 2), c: Ci { r: 3 } }
        Ci mkC() => Ci { r: 2 }
        Ci[2] mkCs() => [Ci { r: 1 }, Ci { r: 2 }]
        i64? opt() => 3

        void nevers(i64 n, through ref List<i64> xs, by Arena arena, to ref i64 o, at Digits[2] ds) fails E {
            if n == 1 { fail .bad(1 + panic("f")) }
            if n == 2 {
                provide allocator: (AH { a: arena, n: 1 + panic("a") }.a) {
                    o = 1
                }
            }
            if n == 3 { xs[1 + panic("i")] = 1 }
            if n == 4 { o += 1 + panic("c") }
            if n == 5 { if (1 + panic("if")) == 2 { o = 2 } }
            if n == 6 { while (1 + panic("w")) == 2 { o = 3 } }
            if n == 7 { for k in 0..(1 + panic("r")) { o = k } }
            if n == 8 { switch 1 + panic("s") { default: o = 4 } }
            if n == 9 { for x in [1, panic("l")] { o = x } }
            if n == 10 { for x in (Digits { count: 1 + panic("u") }) { o = x } }
            if n == 11 { for x in ds[1 + panic("v")] { o = x } }
            if n == 12 { for x in (Countdown { left: 1 + panic("e") }) { o = x } }
            o += g()
        }

        i64 rets(i64 n) {
            defer print("d")
            if n == 1 { return 1 + panic("r") }
            let p = opt()
            if p is not null { return p }
            return n
        }

        void coalesce(to ref i64? o, by i64 n) {
            o ??= n
            o ??= 1 + panic("q")
        }

        void pats(V v, by i64 n, to i64? p) {
            var r = 0
            switch v {
                case .a(let k): r = k
                case .b: r = 2
            }
            switch v {
                case .a(let k) when k > n: r = 1
                case .a(let k) when k > (p ?? g()): r = 3
                case .a(let k) when k > (h() catch 0): r = 5
                default: r = 4
            }
        }

        void places(i64 k, through ref List<Ci> cs) {
            let d1: dyn ref readonly IS = mkH().c
            let d2: dyn ref readonly IS = mkH().t.a
            let d3: dyn ref readonly IS = mkC()
            let d4: dyn ref readonly IS = mkCs()[k]
            let d5: dyn ref readonly IS = cs.removeLast()
        }

        void main() { }
        """;

    [Theory]
    [InlineData(nameof(Exprs), Exprs)]
    [InlineData(nameof(Typed), Typed)]
    [InlineData(nameof(Users), Users)]
    [InlineData(nameof(Loops), Loops)]
    [InlineData(nameof(Iterables), Iterables)]
    [InlineData(nameof(Branches), Branches)]
    [InlineData(nameof(Cleanups), Cleanups)]
    [InlineData(nameof(Places), Places)]
    [InlineData(nameof(More), More)]
    [InlineData(nameof(Rvalues), Rvalues)]
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
