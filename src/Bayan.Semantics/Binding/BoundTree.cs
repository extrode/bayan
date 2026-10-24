namespace Bayan.Semantics.Binding;

/// <summary>Generic traversal of the bound tree.</summary>
public static class BoundTree
{
    /// <summary>The direct children of a node, in evaluation order.</summary>
    public static IEnumerable<BoundNode> Children(BoundNode node)
    {
        switch (node)
        {
            case BoundField n: yield return n.Receiver; break;
            case BoundTupleElement n: yield return n.Receiver; break;
            case BoundUnionField n: yield return n.Receiver; break;
            case BoundErrorField n: yield return n.Receiver; break;
            case BoundCall n:
                if (n.Receiver is not null) yield return n.Receiver;
                foreach (var a in n.Arguments) yield return a;
                break;
            case BoundBuiltinCall n:
                yield return n.Receiver;
                foreach (var a in n.Arguments) yield return a;
                break;
            case BoundUnary n: yield return n.Operand; break;
            case BoundBinary n: yield return n.Left; yield return n.Right; break;
            case BoundConversion n: yield return n.Operand; break;
            case BoundTryFrom n: yield return n.Operand; break;
            case BoundConditional n: yield return n.Condition; yield return n.WhenTrue; yield return n.WhenFalse; break;
            case BoundCoalesce n: yield return n.Left; yield return n.Right; break;
            case BoundStructLiteral n: foreach (var f in n.Fields) yield return f; break;
            case BoundUnionNew n: foreach (var f in n.Payload) yield return f; break;
            case BoundErrorVariant n: foreach (var f in n.Payload) yield return f; break;
            case BoundWith n:
                yield return n.Target;
                foreach (var u in n.Updates) yield return u.Value;
                break;
            case BoundTuple n: foreach (var e in n.Elements) yield return e; break;
            case BoundCollection n: foreach (var e in n.Elements) yield return e; break;
            case BoundSpread n: yield return n.Operand; break;
            case BoundIndex n: yield return n.Target; yield return n.Index; break;
            case BoundSlice n:
                yield return n.Target;
                if (n.Start is not null) yield return n.Start;
                if (n.End is not null) yield return n.End;
                break;
            case BoundRange n: yield return n.Start; yield return n.End; break;
            case BoundIs n: yield return n.Value; yield return n.Pattern; break;
            case BoundSwitchExpr n:
                yield return n.Value;
                foreach (var a in n.Arms)
                {
                    yield return a.Pattern;
                    if (a.Guard is not null) yield return a.Guard;
                    yield return a.Value;
                }
                break;
            case BoundInterpolated n:
                foreach (var p in n.Parts)
                    if (p.Value is not null) yield return p.Value;
                break;
            case BoundTry n: yield return n.Operand; break;
            case BoundMove n: yield return n.Operand; break;
            case BoundCatch n: yield return n.Operand; yield return n.Handler; break;
            case BoundJump n: yield return n.Jump; break;
            case BoundBlockExpr n: yield return n.Block; break;

            case BoundBlock n: foreach (var s in n.Statements) yield return s; break;
            case BoundLocalDecl n: if (n.Initializer is not null) yield return n.Initializer; break;
            case BoundExprStmt n: yield return n.Expr; break;
            case BoundAssert n: yield return n.Condition; break;
            case BoundAssign n: yield return n.Target; yield return n.Value; break;
            case BoundReturn n: if (n.Value is not null) yield return n.Value; break;
            case BoundFail n: yield return n.Error; break;
            case BoundIf n:
                yield return n.Condition;
                yield return n.Then;
                if (n.Else is not null) yield return n.Else;
                break;
            case BoundWhile n: yield return n.Condition; yield return n.Body; break;
            case BoundDoWhile n: yield return n.Body; yield return n.Condition; break;
            case BoundFor n: yield return n.Iterable; yield return n.Body; break;
            case BoundSwitch n:
                yield return n.Value;
                foreach (var s in n.Sections)
                {
                    if (s.Pattern is not null) yield return s.Pattern;
                    if (s.Guard is not null) yield return s.Guard;
                    foreach (var b in s.Body) yield return b;
                }
                break;
            case BoundDefer n: yield return n.Body; break;
            case BoundErrdefer n: yield return n.Body; break;
            case BoundPanicDefer n: yield return n.Body; break;
            case BoundContext n: yield return n.Allocator; yield return n.Body; break;

            case BoundConstantPattern n: yield return n.Value; break;
            case BoundRelationalPattern n: yield return n.Value; break;
            case BoundRangePattern n: yield return n.Start; yield return n.End; break;
            case BoundNotPattern n: yield return n.Operand; break;
            case BoundBinaryPattern n: yield return n.Left; yield return n.Right; break;
            case BoundPropertyPattern n: foreach (var s in n.Subpatterns) yield return s.Pattern; break;
            case BoundUnionPattern n: foreach (var s in n.Subpatterns) yield return s; break;
            case BoundErrorPattern n: foreach (var s in n.Subpatterns) yield return s; break;
        }
    }

    /// <summary>The node and everything below it, pre-order.</summary>
    public static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        var stack = new Stack<BoundNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in Children(n).Reverse()) stack.Push(c);
        }
    }
}
