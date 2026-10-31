using System.Globalization;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;

namespace Bayan.Testing;

/// <summary>
/// One counter of a <c>--coverage</c> build: <see cref="Kind"/> <c>line</c> (a statement started), <c>branch</c> (a
/// condition had <see cref="Outcome"/>) or <c>error</c> (<see cref="Function"/> returned <see cref="Variant"/> from the
/// origin at the location, a <c>fail</c>, a <c>try</c> or a <c>catch</c> passing other variants on).
/// </summary>
public sealed record CoverageCounter(int Id, string Kind, string File, int Line, int Column,
    string? Function = null, string? Variant = null, string? Origin = null, bool? Outcome = null);

/// <summary>The counters of a <c>--coverage</c> build, by id, and the probes the lowerer puts in for them.</summary>
public sealed record CoverageMap(IReadOnlyList<CoverageCounter> Counters, CoverageProbes Probes);

/// <summary>
/// Spec 013 R9: counters for statements, condition outcomes and error paths, placed on the bound tree before lowering,
/// so code the lowerer makes (loop counters, desugared <c>try</c>, pattern temporaries) has none. Test bodies, generic
/// templates, harness <c>main.byn</c> files and nodes the binder made rather than parsed get none; the instances of a
/// generic share their template's counters.
/// </summary>
public static class CoverageInstrumenter
{
    public const string Line = "line";
    public const string Branch = "branch";
    public const string ErrorPath = "error";

    public static CoverageMap Instrument(Compilation c, IReadOnlySet<string>? skipFiles = null)
    {
        var walker = new Walker();
        foreach (var module in c.Modules)
            foreach (var fn in module.AllFunctions)
            {
                if (fn.Body is null || fn.IsGeneric || InTest(fn)) continue;
                if (fn.Kind is not (FunctionKind.Function or FunctionKind.Method or FunctionKind.Constructor or FunctionKind.Getter
                    or FunctionKind.Main or FunctionKind.Lambda)) continue;
                if (fn.File is not { } file || skipFiles?.Contains(file.Source.Path) == true) continue;
                walker.Function(fn, file.Source);
            }
        return new CoverageMap(walker.Counters,
            new CoverageProbes(walker.Counters.Count, walker.Statements, walker.Branches, walker.Errors));
    }

    private static bool InTest(FunctionSymbol fn)
    {
        for (FunctionSymbol? f = fn; f is not null; f = f.Enclosing)
            if (f.Kind == FunctionKind.Test) return true;
        return false;
    }

    /// <summary>
    /// Deletes every <c>&lt;prefix&gt;.*</c> file, so a run never merges counters left by an earlier one. A process writes
    /// <c>&lt;BAYAN_COVERAGE&gt;.&lt;test index&gt;</c> under <c>--test</c>, else <c>&lt;BAYAN_COVERAGE&gt;.&lt;pid&gt;</c>.
    /// </summary>
    public static void DeleteStale(string prefix)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(prefix))!;
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, Path.GetFileName(prefix) + ".*"))
            File.Delete(file);
    }

    /// <summary>
    /// Reads a counter file: the line <c>bayan-coverage 1</c>, then <c>&lt;counter id&gt; &lt;hits&gt;</c> per counter hit
    /// at least once (a counter not listed was not hit).
    /// </summary>
    public static Dictionary<int, ulong> Read(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != "bayan-coverage 1") throw new InvalidDataException($"{path} is not a Bayan coverage file");
        var counts = new Dictionary<int, ulong>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(' ');
            counts[int.Parse(parts[0], CultureInfo.InvariantCulture)] = ulong.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return counts;
    }

    private sealed class Walker
    {
        public List<CoverageCounter> Counters { get; } = new();

        public Dictionary<BoundNode, BoundProbe> Statements { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundNode, BoundBranchProbe> Branches { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundNode, BoundErrorProbe> Errors { get; } = new(ReferenceEqualityComparer.Instance);

        // By syntax, so the instances of a generic count into their template's counters.
        private readonly Dictionary<Node, BoundProbe> _statements = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Node, BoundBranchProbe> _branches = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(Node, uint), int> _errors = new();

        private FunctionSymbol _fn = null!;
        private SourceText _source = null!;
        private BoundNode _root = null!;
        private Node? _guarded;

        public void Function(FunctionSymbol fn, SourceText source)
        {
            (_fn, _source, _root) = (fn, source, fn.Body!);
            Visit(fn.Body!);
        }

        private void Visit(BoundNode node)
        {
            switch (node)
            {
                case BoundBlock: break;
                // `s if c` counts as one statement, the condition's outcomes saying whether `s` ran.
                case BoundStmt { Syntax: Stmt } s when !ReferenceEquals(s.Syntax, _guarded): Statement(s); break;
            }
            if (node is BoundIf { Syntax: GuardedStmt guarded }) _guarded = guarded.Statement;
            switch (node)
            {
                case BoundIf i: Condition(i.Condition); break;
                case BoundWhile w: Condition(w.Condition); break;
                case BoundDoWhile d: Condition(d.Condition); break;
                case BoundConditional c: Condition(c.Condition); break;
                case BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr, Syntax: BinaryExpr } b:
                    // An operand that is itself `&&` or `||` gets its own operands' counters instead.
                    if (b.Left is not BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr }) Condition(b.Left);
                    if (b.Right is not BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr }) Condition(b.Right);
                    break;
                case BoundSwitch s:
                    foreach (var section in s.Sections)
                        if (section.Guard is { } g) Condition(g);
                    break;
                case BoundSwitchExpr s:
                    foreach (var arm in s.Arms)
                        if (arm.Guard is { } g) Condition(g);
                    break;
                case BoundFail f when _fn.Fails:
                    ErrorOrigin(f, "fail", f.Error is BoundErrorVariant v ? [v.Variant] : Variants(_fn.ErrorSets, _fn.ExcludedErrors));
                    break;
                case BoundTry { Operand: BoundCall call } t when _fn.Fails:
                    ErrorOrigin(t, "try", Variants(call.Function.ErrorSets, call.Function.ExcludedErrors));
                    break;
                case BoundCatch { Operand: BoundCall call, Variant: { } caught } c when _fn.Fails:
                    ErrorOrigin(c, "catch", Variants(call.Function.ErrorSets, [.. call.Function.ExcludedErrors, caught]));
                    break;
            }
            foreach (var child in BoundTree.Children(node))
            {
                // A child outside its parent's span was made by the binder (a default argument, an inlined constant).
                // The main program's body has the span of its first statement.
                if (!ReferenceEquals(node, _root) && (child.Syntax.Span.Start < node.Syntax.Span.Start || child.Syntax.Span.End > node.Syntax.Span.End)) continue;
                Visit(child);
            }
        }

        private static List<ErrorVariantSymbol> Variants(IEnumerable<ErrorSetSymbol> sets, IReadOnlyCollection<ErrorVariantSymbol> excluded) =>
            sets.SelectMany(s => s.Variants).Where(v => !excluded.Contains(v)).Distinct().ToList();

        private void Statement(BoundStmt s)
        {
            if (!_statements.TryGetValue(s.Syntax, out var probe))
                _statements[s.Syntax] = probe = new BoundProbe(s.Syntax, Add(Line, s.Syntax));
            Statements[s] = probe;
        }

        private void Condition(BoundExpr cond)
        {
            // A constant condition (`while true`) has one outcome, and code the binder made has no source.
            if (cond.Constant is not null || cond.Syntax is not Expr) return;
            if (!_branches.TryGetValue(cond.Syntax, out var probe))
            {
                var whenTrue = Add(Branch, cond.Syntax, outcome: true);
                _branches[cond.Syntax] = probe = new BoundBranchProbe(cond.Syntax, whenTrue, Add(Branch, cond.Syntax, outcome: false));
            }
            Branches[cond] = probe;
        }

        private void ErrorOrigin(BoundNode origin, string kind, List<ErrorVariantSymbol> variants)
        {
            var name = (_fn.Template ?? _fn).Name;
            if (_fn.Owner is { } owner) name = (owner.Template ?? owner).Name + "." + name;
            var ids = new List<(ErrorVariantSymbol, int)>();
            foreach (var v in variants)
            {
                if (!_errors.TryGetValue((origin.Syntax, v.Code), out var id))
                    _errors[(origin.Syntax, v.Code)] = id = Add(ErrorPath, origin.Syntax, name, v.Set.Name + "." + v.Name, kind);
                ids.Add((v, id));
            }
            if (ids.Count > 0) Errors[origin] = new BoundErrorProbe(origin.Syntax, ids);
        }

        private int Add(string kind, Node at, string? function = null, string? variant = null, string? origin = null, bool? outcome = null)
        {
            var (line, column) = _source.GetLinePosition(at.Span.Start);
            Counters.Add(new CoverageCounter(Counters.Count, kind, _source.Path, line, column, function, variant, origin, outcome));
            return Counters.Count - 1;
        }
    }
}
