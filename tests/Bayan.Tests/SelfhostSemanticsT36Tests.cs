using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T36: <c>selfhost/semantics/binder3.byn</c> against the C# <c>Binder</c> (Binder.cs 987-1467: for bodies, return/fail,
/// break/continue, defers, <c>Panic.handler</c>, <c>context</c>, the switch statement). Each case is a golden diag input
/// or an inline source, compiled whole by both sides: the C# via <c>Compilation.Create</c>, the Bayan via
/// <c>loadModule</c> + <c>declare</c>, conformances checked and every body bound. Both print the diagnostics with the
/// codes only T36 reports (plus the case's <see cref="Shared"/> codes), then each function's bound body; every expected
/// line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT36Tests
{
    private const string Dir = "tmp/t36-cases";

    /// <summary>
    /// Golden diag inputs. Dropped (Q680): the T35 <c>propertyType</c> stub (express4.byn:42) types every builtin property
    /// -1, which panics in <c>typeDisplay</c>; T34's <c>bindTry</c> and T35's <c>bindInterpolated</c> and <c>bindLambda</c>
    /// stubs change the rest (Q681). ambiguous-variant is dropped because the script <c>main</c> has return type -1
    /// (compile.byn:965 sets none; C# defaults to <c>void</c>), which panics in <c>bindReturn</c> (Q681).
    /// The <see cref="Inline"/> cases cover their T36 branches without those.
    /// </summary>
    private static readonly string[] Golden =
    [
        "not-iterable", "jump-out-of-defer", "fail-in-defer", "missing-return-value", "return-value-in-void", "continue-switch",
        "unknown-label", "jump-outside-loop", "errdefer-without-fails", "defer-failing-without-fails", "unknown-handler",
        "duplicate-default", "unknown-variant", "coalesce-break-outside-loop",
    ];

    /// <summary>Inputs written for T36: each reaches the branches named, with no builtin property, `try` or interpolation.</summary>
    private static readonly Dictionary<string, string> Inline = new()
    {
        // bindReturn: returnErrorInput (target member and `Set.x`), a case of the return type, missingReturnValue,
        // returnValueInVoid in a function and in `main`, a never value in a void function.
        ["x-return"] = """
            union error ParseError {
                empty
                tooLong
            }

            enum Outcome {
                empty
                full
            }

            i64 parse(bool e, bool l) fails ParseError {
                if e { return .empty }
                if l { return ParseError.tooLong }
                return 3
            }

            Outcome classify(bool l) fails ParseError {
                if l { fail .tooLong }
                return .empty
            }

            i64 none() {
                return
            }

            void v(bool b) {
                if b { return panic("x") }
                return 5
            }

            void main() {
                return 1
            }
            """,
        // bindFail: propagateFromInfallible (member and value), undeclared case, ambiguous case, undeclared set,
        // a value that is not an error, an inferred set, `fail` in `main`, an excluded case.
        ["x-fail"] = """
            union error ParseError {
                empty
            }

            union error Other {
                empty
                gone
            }

            union error MoneyError {
                invalid
            }

            union error Narrow = Other except .gone

            i64 f(bool e) {
                if e { fail ParseError.empty }
                return 1
            }

            i64 g(bool e) {
                if e { fail .empty }
                return 1
            }

            i64 h(bool e) fails ParseError {
                if e { fail .missing }
                return 1
            }

            i64 k(bool e) fails ParseError, Other {
                if e { fail .empty }
                return 1
            }

            i64 m(bool e) fails ParseError {
                if e { fail MoneyError.invalid }
                return 1
            }

            i64 n(bool e) fails ParseError {
                if e { fail 5 }
                return 1
            }

            i64 q(bool e) fails {
                if e { fail .empty }
                return 1
            }

            i64 r(bool e) fails Narrow {
                if e { fail .gone }
                return 1
            }

            i64 s(bool e) fails ParseError {
                if e { fail ParseError.empty }
                return 1
            }

            void main() {
                fail .empty
            }
            """,
        // findTarget, bindBreak, bindContinue: labels, `continue` to a switch, unknown label, outside a loop, out of a defer.
        ["x-jump"] = """
            void main() {
                outer: for i in 0..3 {
                    for j in 0..3 {
                        if j == 1 { continue outer }
                        if j == 2 { break outer }
                        break
                    }
                    defer {
                        break
                    }
                    continue
                }
                pick: switch 2 {
                    default:
                        if true { break pick }
                        continue pick
                }
                while true {
                    break nowhere
                }
                continue
            }
            """,
        // bindErrdefer, bindPanicDefer, inPanicHandler, bindOnPanic.
        ["x-defer"] = """
            union error E {
                bad
            }

            i64 work(bool b) fails E {
                defer failing (e) {
                    print("failed")
                }
                defer panicking (p) {
                    print("panicking")
                    return 0
                }
                for i in 0..3 {
                    defer panicking {
                        break
                    }
                }
                if b { fail .bad }
                return 1
            }

            void plain() {
                defer failing {
                    print("never")
                }
                defer panicking {
                    fail .bad
                }
            }

            void onCrash(Panic p) {
                print("crash")
            }

            void twoArgs(Panic p, i64 n) {
            }

            void setUp() {
                Panic.handler = onCrash
            }

            void main() {
                Panic.onPanic = onCrash
                Panic.handler = onCrash
                Panic.handler = twoArgs
                setUp()
            }
            """,
        // bindOnPanic not first, a handler that is not a function.
        ["x-onpanic"] = """
            void onCrash(Panic p) {
                print("crash")
            }

            void main() {
                print("starting")
                Panic.handler = 5
            }
            """,
        // bindContext: an unsupported handler, `allocator`, no handler at all is not writable, so only names and values.
        ["x-context"] = """
            void main() {
                let arena = Arena()
                provide net: arena {
                    print("a")
                }
                provide allocator: arena {
                    print("b")
                }
                provide alloc: 5 {
                    print("c")
                }
            }
            """,
        // userElement (IIndexable, IEnumerator from a place and from a temporary), declaredElement, notIterable with a
        // failing `next()`, bindForTry over a value with no failing `next()`, bindForBody's tuple names.
        ["x-for"] = """
            union error ReadError {
                broken
            }

            struct Rows : IIndexable {
                type Item = i64

                i64 count => 2

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

            struct Lines {
                i64 at

                i64? next(ref this) fails ReadError {
                    at += 1
                    return null
                }
            }

            Countdown from(i64 n) => Countdown { left: n }

            void take(i64 x) {
            }

            void takeMaybe(i64? x) {
            }

            void main2(List<i64> xs) fails ReadError {
                let rows = Rows {}
                for r in rows {
                    take(r)
                }
                var steps = Countdown { left: 3 }
                for n in steps {
                    take(n)
                }
                for n: i64? in from(2) {
                    takeMaybe(n)
                }
                for n: bool in rows {
                    take(n)
                }
                let lines = Lines { at: 0 }
                for line in lines {
                    print("l")
                }
                for try x in xs {
                    print("x")
                }
                for (a, b) in xs {
                    take(a)
                }
            }
            """,
        // bindForBody's tuple branch (a prelude of element bindings in a block) and its count mismatch.
        ["x-tuple"] = """
            void take(i64 x) {
            }

            void takeBool(bool x) {
            }

            void walk(List<(i64, bool)> pairs) {
                for (a, b) in pairs {
                    take(a)
                    takeBool(b)
                }
                for (a, b, d) in pairs {
                    take(a)
                }
            }
            """,
        // userElement: a unique `Item` (BYN2022).
        ["x-unique"] = """
            struct Rows : IIndexable {
                type Item = List<i64>

                i64 count => 1

                List<i64> get(i64 index) => List<i64>()
            }

            void main() {
                let rows = Rows {}
                for row in rows {
                    print("row")
                }
            }
            """,
        // userElement's type-parameter branch: an `IIndexable` and an `IEnumerator` constraint, and no iterable one.
        ["x-generic"] = """
            struct Rows : IIndexable {
                type Item = i64

                i64 count => 2

                i64 get(i64 index) => index
            }

            i64 walk<T>(T items) where T : IIndexable {
                for x in items {
                    let y = x
                }
                return 0
            }

            i64 step<T>(T items) where T : IEnumerator {
                var it = items
                for x in it {
                    let y = x
                }
                return 0
            }

            void none<T>(T items) {
                for x in items {
                    print("x")
                }
            }

            i64 run() => walk(Rows {})
            """,
        // bindFail with a payload call: one match, no match, an infallible function, a qualified variant.
        ["x-payload"] = """
            union error CarrierError {
                timeout(i64 after)
                closed
            }

            union error Other {
                gone(i64 code)
            }

            void ok() fails CarrierError {
                fail .timeout(after: 5)
            }

            void unknown() fails CarrierError {
                fail .missing(nope)
            }

            void infallible() {
                fail .timeout(after: 1)
            }

            void qualified() fails CarrierError {
                fail Other.gone(1)
            }
            """,
        // bindSwitchStatement: default arms only, a duplicate default, a guard-free default with statements.
        ["x-switch"] = """
            void main() {
                let x = 1
                switch x {
                    default:
                        print("a")
                    default:
                        print("b")
                }
            }
            """,
    };

    private static readonly string[] Cases = [.. Golden, .. Inline.Keys];

    /// <summary>
    /// Codes other functions also report, compared only in cases where every report of them comes from T36 code:
    /// <c>declaredElement</c> and <c>bindFail</c> (BYN2202), <c>bindForBody</c> (BYN2411), <c>userElement</c> and
    /// <c>bindContext</c> (BYN2022), <c>bindContext</c> (BYN2301), <c>bindOnPanic</c> (BYN2202); BYN2001 from the arguments
    /// <c>bindFail</c> binds for recovery.
    /// </summary>
    private static readonly Dictionary<string, DiagnosticDescriptor[]> Shared = new()
    {
        ["x-for"] = [DiagnosticCodes.TypeMismatch, DiagnosticCodes.PatternMismatch],
        ["x-tuple"] = [DiagnosticCodes.PatternMismatch],
        ["x-unique"] = [DiagnosticCodes.Unsupported],
        ["x-fail"] = [DiagnosticCodes.TypeMismatch],
        ["x-defer"] = [DiagnosticCodes.TypeMismatch],
        ["x-onpanic"] = [DiagnosticCodes.TypeMismatch],
        ["x-context"] = [DiagnosticCodes.Unsupported, DiagnosticCodes.MissingArgument],
        ["x-payload"] = [DiagnosticCodes.UndefinedName],
    };

    private static string[] CodesOf(string name) => [.. Codes, .. Shared.GetValueOrDefault(name, []).Select(d => d.Code)];

    /// <summary>The codes only the T36 functions report (BYN2022, BYN2202, BYN2301, BYN2411 have other reporters).</summary>
    private static readonly string[] Codes =
    [
        .. new[]
        {
            DiagnosticCodes.NotIterable, DiagnosticCodes.JumpOutOfDefer, DiagnosticCodes.MissingReturnValue, DiagnosticCodes.ReturnErrorInput,
            DiagnosticCodes.ReturnValueInVoid, DiagnosticCodes.AmbiguousVariant, DiagnosticCodes.UnknownVariant,
            DiagnosticCodes.PropagateFromInfallible, DiagnosticCodes.UndeclaredError, DiagnosticCodes.ContinueSwitch,
            DiagnosticCodes.UnknownLabel, DiagnosticCodes.JumpOutsideLoop, DiagnosticCodes.ErrdeferWithoutFails,
            DiagnosticCodes.RenamedMember, DiagnosticCodes.OnPanicPlacement, DiagnosticCodes.PanicHandlerRestricted,
            DiagnosticCodes.UnknownHandler, DiagnosticCodes.DuplicateDefault,
        }.Select(d => d.Code),
    ];

    private static string CaseSource(string name) =>
        Inline.TryGetValue(name, out var text) ? text + "\n"
            : File.ReadAllText(Path.Combine(Bayan.Tests.Golden.CasesRoot, "diag", name + ".byn")).Replace("\r\n", "\n");

    private static Compilation Compile(string name)
    {
        Directory.CreateDirectory(Dir);
        return Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), CaseSource(name))], packageRoot: Dir);
    }

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static readonly Dictionary<string, string> KindRenames = new()
    {
        ["this"] = "thisRef", ["return"] = "returnStmt", ["fail"] = "failStmt", ["break"] = "breakStmt", ["continue"] = "continueStmt",
        ["if"] = "ifStmt", ["while"] = "whileStmt", ["for"] = "forStmt", ["switch"] = "switchStmt", ["defer"] = "deferStmt",
        ["assert"] = "assertStmt", ["errdefer"] = "errdeferStmt", ["context"] = "contextStmt", ["default"] = "defaultValue",
    };

    private static string KindName(System.Type t)
    {
        var n = char.ToLowerInvariant(t.Name["Bound".Length]) + t.Name["Bound".Length..][1..];
        return KindRenames.GetValueOrDefault(n, n);
    }

    /// <summary>The bound tree, one node a line: kind and span, an expression's type.</summary>
    private static void Dump(List<string> lines, BoundNode? n, int depth = 0)
    {
        if (n is null)
        {
            lines.Add(new string(' ', depth * 2) + "none");
            return;
        }
        var sb = new StringBuilder(new string(' ', depth * 2));
        sb.Append($"{KindName(n.GetType())} {n.Syntax.Span.Start}..{n.Syntax.Span.End}");
        if (n is BoundExpr e) sb.Append($" : {e.Type.DisplayName}");
        if (n is BoundFor { UserIteration: { } u })
            sb.Append($" user {(u.Kind == IterationKind.Indexable ? "indexable" : "enumerator")} {u.Advance.Name} {u.Item.Name} {u.Spill?.Name ?? "-"}");
        lines.Add(sb.ToString());
        foreach (var ch in BoundTree.Children(n)) Dump(lines, ch, depth + 1);
    }

    /// <summary>The functions whose bodies are dumped: one of their name, declared, not generic, by name.</summary>
    private static List<string> Dumped(Compilation c) =>
        [.. c.EntryModule.Functions.Where(kv => kv.Value.Count == 1 && kv.Value[0].Body is not null)
            .Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    private static List<string> Expected(string name)
    {
        var c = Compile(name);
        var lines = new List<string>();
        foreach (var d in c.Diagnostics.Items.Where(d => CodesOf(name).Contains(d.Code))) lines.AddRange(Diag(d).Split('\n'));
        foreach (var f in Dumped(c))
        {
            lines.Add($"fn {f}");
            Dump(lines, c.EntryModule.Functions[f][0].Body);
        }
        return lines;
    }

    /// <summary>One program run per case, so a panic in one case fails only that case.</summary>
    private static readonly Lazy<string> Main = new(MainSource);

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add(k);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(SelfhostSemanticsHarness.RunSections(Main.Value, name), name, Expected(name));

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource() => """
        import lexer
        import parser

        void tcDump(ref Ctx c, for BNode n, at i64 depth) {
            var sb = StrBuf()
            for i in 0..depth { sb.append("  ") }
            if n == -1 {
                sb.append("none")
                print($"{sb.finish()}")
                return
            }
            let k = boundKind(n)
            let span = nodeSpan(in c, for: n)
            sb.append($"{k} {span.start}..{span.end}")
            if isExprKind(k) { sb.append($" : {typeDisplay(ref c, by: exprType(in c, for: n))}") }
            if k == .forStmt {
                if (c.bFors[boundIndex(n)].userIteration is { } u) {
                    let advance = u.advance != -1 ? c.functions[u.advance].name : c.fields[u.advanceField].name
                    let item = u.item != -1 ? c.functions[u.item].name : c.fields[u.itemField].name
                    sb.append($" user {u.kind == .indexable ? "indexable" : "enumerator"} {advance} {item} {u.spill == -1 ? "-" : c.locals[u.spill].name}")
                }
            }
            print($"{sb.finish()}")
            var kids = List<BNode>()
            for ch in children(in c, for: n) { kids.append(ch) }
            for ch in kids { tcDump(ref c, for: ch, at: depth + 1) }
        }

        /// The `k`th function named `name` of the entry module, in declaration order.
        FuncId tcNth(in Ctx c, by string name, at i64 k) {
            var seen: i64 = 0
            for f in 0..c.functions.length {
                if c.functions[f].name == name && c.functions[f].module == c.entryModule {
                    if seen == k { return f }
                    seen += 1
                }
            }
            return -1
        }

        /// Every function of the entry module bound, as the C# `BindAll` does (generics.byn:163).
        void tcBindAll(ref Ctx c) {
            // Compilation.cs BindAll checks conformances first; `userElement` reads their witnesses.
            checkConformances(ref c)
            let fns = copyIds(in c.modules[c.entryModule].allFunctions)
            for fn in fns {
                if !functionIsInstance(ref c, by: fn) { bindFunction(ref c, for: fn) }
            }
        }

        void tcT36Diags(ref Ctx c, for string name) {
            var keep = Map<string, bool>()
        KEEP
            tcShared(name, to: ref keep)
            for i in 0..c.diagnostics.length {
                if !keep.contains(c.diagnostics[i].code) { continue }
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        void tcBodies(ref Ctx c, for in List<string> names) {
            for name in names {
                print($"fn {name}")
                tcDump(ref c, for: c.functions[tcNth(in c, by: name, at: 0)].body, at: 0)
            }
        }

        CASES

        i32 main(ReadOnlySpan<string> args) {
            for name in args {
                var bytes = List<u8>()
                for x in tcSource(name).bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t36-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    packageRoot: "tmp/t36-cases",
                    displayRoot: "tmp/t36-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t36-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                print($"== {name}")
                tcBindAll(ref c)
                tcT36Diags(ref c, for: name)
                tcBodies(ref c, for: in tcDumped(name))
            }
            return 0
        }
        """.Replace("KEEP", string.Concat(Codes.Select(k => $"    keep[\"{k}\"] = true\n"))).Replace("CASES", CaseFunctions());

    /// <summary>Per case: its text, its signatures, and the functions whose bodies are dumped.</summary>
    private static string CaseFunctions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("string tcSource(string name) {");
        foreach (var d in Cases) sb.AppendLine($"    if name == \"{d}\" {{ return \"{Escape(CaseSource(d))}\" }}");
        sb.AppendLine("    return \"\"");
        sb.AppendLine("}");
        sb.AppendLine("void tcShared(string name, to ref Map<string, bool> keep) {");
        foreach (var (d, codes) in Shared)
            foreach (var k in codes) sb.AppendLine($"    if name == \"{d}\" {{ keep[\"{k.Code}\"] = true }}");
        sb.AppendLine("}");
        sb.AppendLine("List<string> tcDumped(string name) {");
        sb.AppendLine("    var names = List<string>()");
        foreach (var d in Cases)
            foreach (var f in Dumped(Compile(d))) sb.AppendLine($"    if name == \"{d}\" {{ names.append(\"{f}\") }}");
        sb.AppendLine("    return names");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
