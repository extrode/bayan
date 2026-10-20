using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics.Binding;

/// <summary>
/// BYN2311: a <c>var</c> local or pattern binding that is never assigned or mutated through should be a <c>let</c>
/// (spec §7.1, §6.5). Writes are recorded by <c>Binder.CheckWritable</c>, which every assignment, increment,
/// <c>ref</c>/<c>out</c> argument and mutating call goes through.
/// </summary>
internal static class NeverReassignedVarLint
{
    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        foreach (var node in BoundTree.Descendants(fn.Body))
        {
            var local = node switch
            {
                BoundLocalDecl { Initializer: not null } d => d.Local,
                BoundBindPattern p => p.Local,
                _ => null,
            };
            if (local is not { Kind: LocalKind.Var, Written: false, KeywordSpan: { } keyword, Location: { } location }) continue;
            c.Report(DiagnosticCodes.NeverReassignedVar, fn.File.Source, location.Span, [local.Name],
                help: "a `let` tells the reader the value never changes", fix: new Fix(keyword, "let"));
        }
    }
}
