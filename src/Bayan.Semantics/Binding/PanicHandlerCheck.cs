using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Panic handlers can't recover (spec §7.6): a <c>defer panicking</c> body and the function <c>Panic.onPanic</c> names
/// may not assign or allocate (BYN2612) and read only <c>let</c> bindings and parameters (BYN2613). The function is also
/// checked for <c>return</c>, <c>break</c>, <c>continue</c> and <c>try</c>, which the binder reports inside a <c>defer panicking</c>.
/// </summary>
internal static class PanicHandlerCheck
{
    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        if (fn == c.OnPanic) Check(c, fn, fn.Body, whole: true);
        else FindHandlers(c, fn, fn.Body);
    }

    /// <summary>Checks each outermost <c>defer panicking</c> body; its nested handlers are checked with it.</summary>
    private static void FindHandlers(Compilation c, FunctionSymbol fn, BoundNode node)
    {
        if (node is BoundPanicDefer d)
        {
            Check(c, fn, d.Body, whole: false);
            return;
        }
        foreach (var child in BoundTree.Children(node)) FindHandlers(c, fn, child);
    }

    private static void Check(Compilation c, FunctionSymbol fn, BoundNode body, bool whole)
    {
        var own = new HashSet<LocalSymbol>();
        var sinks = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
        var targets = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in BoundTree.Descendants(body))
        {
            string? restricted = null;
            switch (node)
            {
                case BoundLocalDecl d: own.Add(d.Local); break;
                case BoundBindPattern { Local: { } l }: own.Add(l); break;
                case BoundPropertyPattern { Designation: { } l }: own.Add(l); break;
                case BoundUnionPattern { Designation: { } l }: own.Add(l); break;
                case BoundFor f: own.Add(f.Variable); break;
                case BoundCatch { Binding: { } l }: own.Add(l); break;
                case BoundErrdefer { Error: { } l }: own.Add(l); break;
                case BoundPanicDefer { Panic: { } l }: own.Add(l); break;
                case BoundReturn when whole: restricted = "`return`"; break;
                case BoundBreak when whole: restricted = "`break`"; break;
                case BoundContinue when whole: restricted = "`continue`"; break;
                case BoundTry when whole: restricted = "`try`"; break;
                case BoundAssign a:
                    restricted = "assignment";
                    targets.Add(a.Target);
                    break;
                // Interpolation written straight into `print`, `panic` or `StrBuf.append` is formatted on the stack.
                case BoundCall { Function.Builtin: BuiltinFunction.Print or BuiltinFunction.Panic, Arguments: [BoundInterpolated i, ..] }:
                    sinks.Add(i);
                    break;
                case BoundBuiltinCall { Member: BuiltinMember.StrBufAppend, Arguments: [BoundInterpolated i] }:
                    sinks.Add(i);
                    break;
                case BoundInterpolated when !sinks.Contains(node):
                case BoundCollection { Type: ListType or MapType }:
                case BoundCall { Function.Builtin: BuiltinFunction.ListNew or BuiltinFunction.OwnedNew or BuiltinFunction.StrBufGrow or BuiltinFunction.StrBufOver or BuiltinFunction.ArenaNew
                    or BuiltinFunction.Utf8Decode or BuiltinFunction.Utf8TryDecode or BuiltinFunction.Utf8DecodeReplace or BuiltinFunction.HexEncode or BuiltinFunction.HexDecode or BuiltinFunction.SymbolIntern or BuiltinFunction.IoReadFile or BuiltinFunction.IoReadText or BuiltinFunction.IoListDir
                    or BuiltinFunction.IoRun or BuiltinFunction.IoRunBatch or BuiltinFunction.PathRelative or BuiltinFunction.PathFileName }:
                case BoundBuiltinCall { Member: BuiltinMember.SpanCopy or BuiltinMember.StringCopy or BuiltinMember.ListCopy
                    or BuiltinMember.MapCopy or BuiltinMember.StrBufCopy or BuiltinMember.IoReadAll or BuiltinMember.IntToString
                    or BuiltinMember.ListAdd or BuiltinMember.ListReplace or BuiltinMember.MapSlot or BuiltinMember.MapIndexSet
                    or BuiltinMember.StringLowercased or BuiltinMember.StringUppercased or BuiltinMember.StringReplaced or BuiltinMember.StringRepeated or BuiltinMember.StringPadded or BuiltinMember.BytesToString
                    or BuiltinMember.StringJoined or BuiltinMember.Utf16Copy }:
                    restricted = "allocation";
                    break;
                case BoundLocal { Local: var l } when !targets.Contains(node) && !own.Contains(l) && l.IsMutable:
                    c.Report(DiagnosticCodes.PanicHandlerReads, fn.File!.Source, node.Syntax.Span, [],
                        label: $"`{l.Name}` is not a `let` or a parameter", help: "copy the value into a `let` before the handler and read that");
                    break;
            }
            if (restricted is not null)
                c.Report(DiagnosticCodes.PanicHandlerRestricted, fn.File!.Source, node.Syntax.Span, [restricted],
                    help: "remove it; panic handlers can't recover");
        }
    }
}
