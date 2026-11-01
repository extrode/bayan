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
/// compiled whole by both sides: the C# via <c>Compilation.Create</c>, the Bayan via <c>loadModule</c> + <c>declare</c>,
/// hand-set signatures (T38's <c>declareSignature</c> is a stub) and every body bound. Both print the diagnostics with
/// the codes only T36 reports, then each function's bound body; every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT36Tests
{
    private const string Dir = "tmp/t36-cases";

    private static readonly string[] Cases =
    [
        "not-iterable", "for-try-iterable", "jump-out-of-defer", "fail-in-defer", "missing-return-value", "return-error-input",
        "return-value-in-void", "continue-switch", "unknown-label", "jump-outside-loop", "errdefer-without-fails",
        "defer-failing-without-fails", "panic-on-panic-renamed", "on-panic-not-entry", "on-panic-not-first", "on-panic-twice",
        "panic-handler-restricted", "unknown-handler", "duplicate-default", "ambiguous-variant", "unknown-variant",
        "fail-from-infallible", "propagate-from-infallible", "fail-undeclared-case", "undeclared-error",
    ];

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
        File.ReadAllText(Path.Combine(Golden.CasesRoot, "diag", name + ".byn")).Replace("\r\n", "\n");

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
        ["assert"] = "assertStmt", ["errdefer"] = "errdeferStmt", ["context"] = "contextStmt",
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
        lines.Add(sb.ToString());
        foreach (var ch in BoundTree.Children(n)) Dump(lines, ch, depth + 1);
    }

    /// <summary>The functions whose bodies are dumped: one of their name, declared, not generic, by name.</summary>
    private static List<string> Dumped(Compilation c) =>
        [.. c.EntryModule.Functions.Where(kv => kv.Value.Count == 1 && kv.Value[0].TypeParameters.Count == 0 && kv.Value[0].Body is not null)
            .Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    private static List<string> Expected(string name)
    {
        var c = Compile(name);
        var lines = new List<string>();
        foreach (var d in c.Diagnostics.Items.Where(d => Codes.Contains(d.Code))) lines.AddRange(Diag(d).Split('\n'));
        foreach (var f in Dumped(c))
        {
            lines.Add($"fn {f}");
            Dump(lines, c.EntryModule.Functions[f][0].Body);
        }
        return lines;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases]));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add(k);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, name, Expected(name));

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
            print($"{sb.finish()}")
            var kids = List<BNode>()
            for ch in children(in c, for: n) { kids.append(ch) }
            for ch in kids { tcDump(ref c, for: ch, at: depth + 1) }
        }

        void tcParam(ref Ctx c, for FuncId f, by string name, at TextSpan span, as Type type, to parser.ParamModifier modifier) {
            let ordinal = c.functions[f].parameters.length
            let p = newLocal(ref c, by: LocalSymbol { name: name, nameSpan: span, span: span, type: type, kind: .parameter, isParameter: true, modifier: modifier, ordinal: ordinal })
            c.functions[f].parameters.append(p)
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
            let fns = copyIds(in c.modules[c.entryModule].allFunctions)
            for fn in fns {
                if !functionIsInstance(ref c, by: fn) { bindFunction(ref c, for: fn) }
            }
        }

        void tcT36Diags(ref Ctx c) {
            var keep = Map<string, bool>()
        KEEP
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
                tcSignatures(ref c, by: name)
                tcBindAll(ref c)
                tcT36Diags(ref c)
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
        sb.AppendLine("List<string> tcDumped(string name) {");
        sb.AppendLine("    var names = List<string>()");
        foreach (var d in Cases)
            foreach (var f in Dumped(Compile(d))) sb.AppendLine($"    if name == \"{d}\" {{ names.append(\"{f}\") }}");
        sb.AppendLine("    return names");
        sb.AppendLine("}");
        sb.AppendLine("void tcSignatures(ref Ctx c, by string name) {");
        for (var i = 0; i < Cases.Length; i++) sb.AppendLine($"    if name == \"{Cases[i]}\" {{ tcSigs{i}(ref c) }}");
        sb.AppendLine("}");
        for (var i = 0; i < Cases.Length; i++)
        {
            sb.AppendLine($"void tcSigs{i}(ref Ctx c) {{");
            sb.Append(Signatures(Compile(Cases[i])));
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private static string Lower(bool b) => b ? "true" : "false";

    /// <summary>The Bayan expression for type <paramref name="t"/>.</summary>
    private static string BayanType(TypeSymbol t) => t switch
    {
        PrimitiveType p => $"prim(.{(p.DisplayName == "never" ? "neverKind" : p.DisplayName)})",
        BuiltinType { Kind: BuiltinKind.Panic } => "builtinType(.panic)",
        Bayan.Semantics.Symbols.OptionalType o => $"internOptional(ref c, by: {BayanType(o.Inner)})",
        ListType l => $"internList(ref c, by: {BayanType(l.Element)})",
        SpanType sp => $"internSpan(ref c, by: {BayanType(sp.Element)}, readOnly: {Lower(sp.ReadOnly)})",
        ErrorSetSymbol or StructSymbol or EnumSymbol or UnionSymbol => $"c.modules[c.entryModule].types[\"{t.DisplayName}\"]",
        _ => throw new InvalidOperationException(t.DisplayName),
    };

    /// <summary>
    /// What T38's stubbed <c>declareSignature</c>/<c>declareParameters</c> (declare2.byn:15, :18) would set on each
    /// declared function, generated from the C# symbols as SelfhostSemanticsT20Tests.Signatures does (Q618).
    /// </summary>
    private static string Signatures(Compilation c)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var (name, fns) in c.EntryModule.Functions)
        {
            if (fns.All(f => f.Decl is null)) continue;
            for (var i = 0; i < fns.Count; i++)
            {
                var fn = fns[i];
                var v = $"f{n++}";
                sb.AppendLine($"    let {v} = tcNth(in c, by: \"{name}\", at: {i})");
                sb.AppendLine($"    c.functions[{v}].returnType = {BayanType(fn.ReturnType)}");
                sb.AppendLine($"    c.functions[{v}].hasFails = {Lower(fn.Fails)}");
                sb.AppendLine($"    c.functions[{v}].infersErrors = {Lower(fn.InfersErrors)}");
                foreach (var s in fn.ErrorSets)
                    sb.AppendLine($"    c.functions[{v}].errorSets.append(typeIndex(c.modules[c.entryModule].types[\"{s.Name}\"]))");
                foreach (var x in fn.ExcludedErrors)
                    sb.AppendLine($"    c.functions[{v}].excludedErrors.append(findErrorVariant(ref c, by: typeIndex(c.modules[c.entryModule].types[\"{x.Set.Name}\"]), for: \"{x.Name}\"))");
                foreach (var p in fn.Parameters)
                {
                    var span = p.Location!.Value.Span;
                    var modifier = p.Modifier switch
                    {
                        ParamModifier.None => "none", ParamModifier.Ref => "refKeyword", ParamModifier.Move => "moveKeyword",
                        ParamModifier.Out => "outKeyword", _ => "inKeyword",
                    };
                    sb.AppendLine($"    tcParam(ref c, for: {v}, by: \"{p.Name}\", at: TextSpan {{ start: {span.Start}, length: {span.Length} }}, as: {BayanType(p.Type)}, to: .{modifier})");
                }
            }
        }
        return sb.ToString();
    }
}
