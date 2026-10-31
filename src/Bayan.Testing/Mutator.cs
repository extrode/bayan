using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;
using ArrayType = Bayan.Semantics.Symbols.ArrayType;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;

namespace Bayan.Testing;

/// <summary>
/// One mutant: where it is, what changes, and the bound node that replaces <see cref="OriginalNode"/> in the mutant.
/// <see cref="Original"/> is the source text of the changed span and <see cref="Mutated"/> the text that replaces it.
/// </summary>
public sealed record MutationSite(
    string File, int Line, int Column, string Operator, string Original, string Mutated,
    FunctionSymbol Function, BoundNode OriginalNode, BoundNode MutatedNode)
{
    /// <summary>What names the mutant across builds; numeric ids are per binary.</summary>
    public (string File, int Line, int Column, string Operator, string Mutated) Identity => (File, Line, Column, Operator, Mutated);
}

/// <summary>The mutants of a compilation, and per operator the candidates not emitted because they would not type-check (R4).</summary>
public sealed record Mutations(IReadOnlyList<MutationSite> Sites, IReadOnlyDictionary<string, int> NotEmitted);

/// <summary>
/// Finds every v1 mutation site on the bound tree (revision 0014 §3, spec 013 R4–R6). Each operator is chosen by the
/// operand type so every emitted mutant type-checks. Test bodies, generic templates and nodes the binder made rather
/// than parsed (their syntax is not the construct, or lies outside the parent's span) are never mutated.
/// </summary>
public static class Mutator
{
    public const string Boundary = "boundary";
    public const string Relational = "relational";
    public const string Logical = "logical";
    public const string Condition = "condition";
    public const string Bool = "bool";
    public const string Arithmetic = "arithmetic";
    public const string Literal = "literal";
    public const string Negation = "negation";
    public const string Statement = "statement";
    public const string EmptyReturn = "emptyReturn";
    public const string Variant = "variant";

    /// <summary>The operators, in the order of the table in SPEC §20.</summary>
    public static readonly string[] Operators = [Boundary, Relational, Logical, Condition, Bool, Arithmetic, Literal, Negation, Statement, EmptyReturn, Variant];

    /// <summary>The sites of every function in <paramref name="c"/>, a generic template's once (from its first instance), skipping files whose path is in <paramref name="skipFiles"/> (a harness-generated <c>main.byn</c>).</summary>
    public static Mutations Find(Compilation c, IReadOnlySet<string>? skipFiles = null)
    {
        var walker = new Walker();
        var walked = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var module in c.Modules)
            foreach (var fn in module.AllFunctions)
            {
                if (fn.Body is null || fn.IsGeneric || InTest(fn)) continue;
                if (fn.Kind is not (FunctionKind.Function or FunctionKind.Method or FunctionKind.Constructor or FunctionKind.Getter
                    or FunctionKind.Main or FunctionKind.Lambda)) continue;
                if (fn.File is not { } file || skipFiles?.Contains(file.Source.Path) == true) continue;
                // Instances of one template share its body syntax, and so its sites.
                if (!walked.Add(fn.Body.Syntax)) continue;
                walker.Function(fn, file.Source);
            }
        return new Mutations(walker.Sites, walker.NotEmitted);
    }

    private static bool InTest(FunctionSymbol fn)
    {
        for (FunctionSymbol? f = fn; f is not null; f = f.Enclosing)
            if (f.Kind == FunctionKind.Test) return true;
        return false;
    }

    private sealed class Walker
    {
        public List<MutationSite> Sites { get; } = new();

        public Dictionary<string, int> NotEmitted { get; } = Operators.ToDictionary(o => o, _ => 0);

        private FunctionSymbol _fn = null!;
        private SourceText _source = null!;
        /// <summary>Integer literals that index a fixed array, with the array type and whether they count from the end.</summary>
        private readonly Dictionary<BoundLiteral, (ArrayType Array, bool FromEnd)> _arrayIndexes = new(ReferenceEqualityComparer.Instance);
        /// <summary>Locals declared without an initializer and <c>out</c> parameters: removing an assignment to one leaves it unassigned.</summary>
        private readonly HashSet<LocalSymbol> _uninitialized = new();

        public void Function(FunctionSymbol fn, SourceText source)
        {
            (_fn, _source) = (fn, source);
            _uninitialized.Clear();
            _arrayIndexes.Clear();
            foreach (var d in BoundTree.Descendants(fn.Body!).OfType<BoundLocalDecl>())
                if (d.Initializer is null) _uninitialized.Add(d.Local);
            // An `out` parameter must be assigned before every return (BYN2103).
            _uninitialized.UnionWith(fn.Parameters.Where(p => p.Modifier == ParamModifier.Out));
            Visit(fn.Body!, null);
        }

        private void Visit(BoundNode node, BoundNode? parent)
        {
            // Patterns hold constants the switch checks for overlap and exhaustiveness; they are not mutated.
            if (node is BoundPattern) return;
            switch (node)
            {
                case BoundBinary b when b.Syntax is BinaryExpr be && be.Op == b.Op: OnBinary(b, be); break;
                case BoundUnary { Op: UnaryOp.Negate } u when u.Syntax is UnaryExpr { Op: UnaryOp.Negate } ue:
                    Add(Negation, ue.Span, Text(ue.Operand.Span), u, u.Operand);
                    break;
                case BoundLiteral l: OnLiteral(l, parent); break;
                case BoundIndex { Target.Type: ArrayType array } i when (i.Index as BoundLiteral ?? (i.Index as BoundConversion)?.Operand as BoundLiteral) is { } index:
                    _arrayIndexes[index] = (array, i.FromEnd);
                    break;
                case BoundExprStmt { Syntax: ExprStmt, Expr: BoundCall or BoundBuiltinCall } s when s.Expr.Type == PrimitiveType.Void:
                    Remove(s);
                    break;
                case BoundAssign a when a.Syntax is AssignStmt or IncDecStmt:
                    OnAssign(a);
                    break;
                case BoundReturn { Syntax: ReturnStmt { Value: { } valueSyntax }, Value: { } value }: OnReturn(value, valueSyntax); break;
                case BoundFail { Syntax: FailStmt } f: OnFail(f); break;
                case BoundIf { Syntax: IfStmt or UnlessStmt or GuardedStmt } i: OnCondition(i.Condition, false); break;
                case BoundWhile { Syntax: WhileStmt } w: OnCondition(w.Condition, true); break;
                case BoundDoWhile { Syntax: DoWhileStmt } d: OnCondition(d.Condition, true); break;
                case BoundSwitch s:
                    foreach (var section in s.Sections)
                        if (section.Guard is { } g) OnCondition(g, false);
                    break;
                case BoundSwitchExpr s:
                    foreach (var arm in s.Arms)
                        if (arm.Guard is { } g) OnCondition(g, false);
                    break;
            }
            foreach (var child in BoundTree.Children(node))
            {
                // A child outside its parent's span was made by the binder (a default argument, an inlined constant).
                if (child.Syntax.Span.Start < node.Syntax.Span.Start || child.Syntax.Span.End > node.Syntax.Span.End) continue;
                Visit(child, node);
            }
        }

        private void OnBinary(BoundBinary b, BinaryExpr be)
        {
            switch (b.Op)
            {
                case BinaryOp.Lt or BinaryOp.Le or BinaryOp.Gt or BinaryOp.Ge:
                    ChangeOp(Boundary, b, be, b.Op switch { BinaryOp.Lt => BinaryOp.Le, BinaryOp.Le => BinaryOp.Lt, BinaryOp.Gt => BinaryOp.Ge, _ => BinaryOp.Gt });
                    ChangeOp(Relational, b, be, b.Op switch { BinaryOp.Lt => BinaryOp.Ge, BinaryOp.Ge => BinaryOp.Lt, BinaryOp.Gt => BinaryOp.Le, _ => BinaryOp.Gt });
                    break;
                case BinaryOp.Eq or BinaryOp.Ne:
                    ChangeOp(Relational, b, be, b.Op == BinaryOp.Eq ? BinaryOp.Ne : BinaryOp.Eq);
                    break;
                case BinaryOp.LogicalAnd or BinaryOp.LogicalOr:
                    // `x is T t && t.f` reads what the left side proved; with `||` the right side would not bind.
                    if (BoundTree.Descendants(b.Left).Any(n => n is BoundIs)) NotEmitted[Logical]++;
                    else ChangeOp(Logical, b, be, b.Op == BinaryOp.LogicalAnd ? BinaryOp.LogicalOr : BinaryOp.LogicalAnd);
                    break;
                case BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Rem:
                    var to = b.Op switch { BinaryOp.Add => BinaryOp.Sub, BinaryOp.Sub => BinaryOp.Add, BinaryOp.Mul => BinaryOp.Div, _ => BinaryOp.Mul };
                    // Two constant operands fold at compile time, where overflow and division by zero are errors.
                    if (b.Left.Constant is { Value: Int128 l } && b.Right.Constant is { Value: Int128 r } && b.Type is PrimitiveType p && !Folds(to, l, r, p))
                        NotEmitted[Arithmetic]++;
                    else ChangeOp(Arithmetic, b, be, to);
                    break;
            }
        }

        /// <summary>Replaces the operator token between the operands, keeping the source around it.</summary>
        private void ChangeOp(string op, BoundBinary b, BinaryExpr be, BinaryOp to)
        {
            var between = TextSpan.FromBounds(be.Left.Span.End, be.Right.Span.Start);
            var at = be.Left.Span.End + Text(between).IndexOf(OpText(b.Op), StringComparison.Ordinal);
            var mutated = Text(TextSpan.FromBounds(be.Span.Start, at)) + OpText(to) + Text(TextSpan.FromBounds(at + OpText(b.Op).Length, be.Span.End));
            Add(op, be.Span, mutated, b, b with { Op = to, Constant = null });
        }

        private void OnLiteral(BoundLiteral l, BoundNode? parent)
        {
            if (l.Value.Value is bool flag && l.Syntax is LiteralExpr { Kind: LiteralKind.True or LiteralKind.False } le
                && le.Kind == LiteralKind.True == flag)
            {
                // `while true` is how a loop says it ends only by `break` or `return`; `while false` leaves code after it reachable.
                if (flag && parent is BoundWhile or BoundDoWhile) NotEmitted[Bool]++;
                else Add(Bool, le.Span, flag ? "false" : "true", l, Constant(l.Syntax, PrimitiveType.Bool, !flag));
                return;
            }
            if (l.Type is not PrimitiveType { IsInteger: true } type || l.Value.Value is not Int128 n) return;
            var literal = l.Syntax switch
            {
                LiteralExpr { Kind: LiteralKind.Integer } e => e,
                UnaryExpr { Op: UnaryOp.Negate, Operand: LiteralExpr { Kind: LiteralKind.Integer } e } => e,
                _ => null,
            };
            if (literal is null) return;
            foreach (var m in new[] { n + 1, n - 1 })
            {
                if (!Fits(m, type) || !FoldsInParent(l, m, parent, type) || !InBounds(l, m))
                {
                    NotEmitted[Literal]++;
                    continue;
                }
                Add(Literal, l.Syntax.Span, m + (literal.Suffix?.Text ?? ""), l, Constant(l.Syntax, type, m));
            }
        }

        /// <summary>A literal operand of a constant arithmetic expression changes the folded value, which must still fold.</summary>
        private static bool FoldsInParent(BoundLiteral l, Int128 value, BoundNode? parent, PrimitiveType type)
        {
            if (parent is not BoundBinary { Op: BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Rem } b) return true;
            var other = b.Left == l ? b.Right : b.Left;
            if (other.Constant is not { Value: Int128 o }) return true;
            return b.Left == l ? Folds(b.Op, value, o, type) : Folds(b.Op, o, value, type);
        }

        /// <summary>A constant index into a fixed array must stay inside it (BYN IndexOutOfRange).</summary>
        private bool InBounds(BoundLiteral l, Int128 value)
        {
            if (!_arrayIndexes.TryGetValue(l, out var a)) return true;
            var effective = a.FromEnd ? a.Array.Length - value : value;
            return effective >= 0 && effective < a.Array.Length && !(a.FromEnd && value == 0);
        }

        private void OnAssign(BoundAssign a)
        {
            if (a.Target is BoundLocal { Local: var local } && _uninitialized.Contains(local) && a.Op is null) NotEmitted[Statement]++;
            else Remove(a);
            if (a.Syntax is not AssignStmt { Op: AssignOp.Add or AssignOp.Sub or AssignOp.Mul or AssignOp.Div or AssignOp.Rem } s) return;
            var to = a.Op switch { BinaryOp.Add => BinaryOp.Sub, BinaryOp.Sub => BinaryOp.Add, BinaryOp.Mul => BinaryOp.Div, _ => BinaryOp.Mul };
            var between = TextSpan.FromBounds(s.Target.Span.End, s.Value.Span.Start);
            var opText = OpText(a.Op!.Value) + "=";
            var at = s.Target.Span.End + Text(between).IndexOf(opText, StringComparison.Ordinal);
            var mutated = Text(TextSpan.FromBounds(s.Span.Start, at)) + OpText(to) + "=" + Text(TextSpan.FromBounds(at + opText.Length, s.Span.End));
            Add(Arithmetic, s.Span, mutated, a, a with { Op = to });
        }

        private void Remove(BoundStmt s) => Add(Statement, s.Syntax.Span, "", s, new BoundBlock(s.Syntax, []));

        private void OnReturn(BoundExpr value, Expr syntax)
        {
            var empty = EmptyText(value.Type);
            if (empty is null)
            {
                NotEmitted[EmptyReturn]++;
                return;
            }
            // Already the empty value: the mutant would be the program itself.
            if (value is BoundDefault || Text(syntax.Span) == empty) return;
            Add(EmptyReturn, syntax.Span, empty, value, new BoundDefault(value.Syntax, value.Type));
        }

        /// <summary>The empty value of <paramref name="t"/> as source, or null when the type has none.</summary>
        private static string? EmptyText(TypeSymbol t) => t switch
        {
            PrimitiveType { IsInteger: true } => "0",
            PrimitiveType { IsFloat: true } => "0.0",
            PrimitiveType { Kind: PrimitiveKind.Bool } => "false",
            PrimitiveType { Kind: PrimitiveKind.String } => "\"\"",
            OptionalType => "null",
            _ => null,
        };

        private void OnFail(BoundFail f)
        {
            var error = f.Error is BoundConversion conv ? conv.Operand : f.Error;
            if (error is not BoundErrorVariant { Variant: var v } ev) return;
            var text = Text(ev.Syntax.Span);
            var paren = text.IndexOf('(');
            var name = text.LastIndexOf("." + v.Name, paren < 0 ? text.Length - 1 : paren - 1, StringComparison.Ordinal) + 1;
            foreach (var other in v.Set.Variants)
            {
                if (other == v) continue;
                if (other.Fields.Count != v.Fields.Count || other.Fields.Zip(v.Fields).Any(p => p.First.Type != p.Second.Type)
                    || _fn.ExcludedErrors.Contains(other))
                {
                    NotEmitted[Variant]++;
                    continue;
                }
                var mutated = text[..name] + other.Name + text[(name + v.Name.Length)..];
                Add(Variant, ev.Syntax.Span, mutated, ev,
                    new BoundErrorVariant(ev.Syntax, other) { Payload = ev.Payload });
            }
        }

        private void OnCondition(BoundExpr cond, bool loop)
        {
            // Negating a pattern test would leave what it binds or narrows unproved where it is read.
            if (BoundTree.Descendants(cond).Any(n => n is BoundIs) || loop && cond.Constant is { Value: true })
            {
                NotEmitted[Condition]++;
                return;
            }
            var span = cond.Syntax.Span;
            Add(Condition, span, "!(" + Text(span) + ")", cond, new BoundUnary(cond.Syntax, UnaryOp.Not, cond, PrimitiveType.Bool));
        }

        /// <summary>A site at the start of <paramref name="span"/>, the expression or statement the mutant replaces.</summary>
        private void Add(string op, TextSpan span, string mutated, BoundNode original, BoundNode replacement)
        {
            var (line, column) = _source.GetLinePosition(span.Start);
            Sites.Add(new MutationSite(_source.Path, line, column, op, Text(span), mutated, _fn, original, replacement));
        }

        private string Text(TextSpan span) => _source.GetText(span);
    }

    private static BoundLiteral Constant(Node syntax, TypeSymbol type, object value)
    {
        var c = new ConstantValue(value);
        return new BoundLiteral(syntax, type, c) { Constant = c };
    }

    private static string OpText(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+", BinaryOp.Sub => "-", BinaryOp.Mul => "*", BinaryOp.Div => "/", BinaryOp.Rem => "%",
        BinaryOp.Lt => "<", BinaryOp.Gt => ">", BinaryOp.Le => "<=", BinaryOp.Ge => ">=", BinaryOp.Eq => "==", BinaryOp.Ne => "!=",
        BinaryOp.LogicalAnd => "&&", BinaryOp.LogicalOr => "||",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static bool Fits(Int128 n, PrimitiveType t) => t.IsSignedInteger
        ? n >= -(Int128.One << (t.Bits - 1)) && n < Int128.One << (t.Bits - 1)
        : n >= 0 && n < Int128.One << t.Bits;

    /// <summary>Whether <c>l op r</c> folds without the binder's overflow or division-by-zero error.</summary>
    private static bool Folds(BinaryOp op, Int128 l, Int128 r, PrimitiveType t)
    {
        if (op is BinaryOp.Div or BinaryOp.Rem && r == 0) return false;
        Int128 result = op switch
        {
            BinaryOp.Add => l + r,
            BinaryOp.Sub => l - r,
            BinaryOp.Mul => l * r,
            BinaryOp.Div => l / r,
            BinaryOp.Rem => l % r,
            _ => 0,
        };
        return Fits(result, t);
    }
}
