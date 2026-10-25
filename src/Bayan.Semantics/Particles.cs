using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <summary>
/// The particle rule at a declaration (spec §10): one unlabeled required parameter, the object; every other
/// takes a particle (BYN5004, BYN5006); a particle appears once (BYN5013). The receiver is not a parameter here,
/// and a <c>bool</c> parameter is passed by name, so neither counts. Calls are checked in <c>BindArguments</c> (BYN5005).
/// </summary>
internal static class Particles
{
    /// <summary>Exempt: <c>extern</c> declarations (C signatures) and a <c>rest</c> tail, which takes no label (J39).</summary>
    internal static void Check(Compilation c, SourceText source, FunctionSymbol fn)
    {
        if (fn.Template is not null || fn.Decl?.Modifiers.HasFlag(Modifiers.Extern) == true) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in fn.Parameters)
            if (p.Syntax?.Particle is { } particle && !seen.Add(particle.Text))
                c.Report(DiagnosticCodes.RepeatedParticle, source, particle.Span, [fn.Name, particle.Text], label: "used again",
                    help: "group the values in a struct, or choose another particle");
        var unlabeled = fn.Parameters.Where(p => p.Syntax is { Particle: null, Default: null, Rest: false } && !p.Type.IsBool).ToList();
        if (unlabeled.Count < 2) return;
        var name = fn.Kind == FunctionKind.Constructor ? fn.Owner!.Name : fn.Name;
        var at = fn.Decl?.Name.Span ?? fn.Location!.Value.Span;
        c.Report(DiagnosticCodes.ParticleRequired, source, at, [name, unlabeled.Count], label: $"{unlabeled.Count} unlabeled parameters",
            help: "keep one unlabeled parameter, the object");
        foreach (var p in unlabeled.Skip(1))
            c.Report(DiagnosticCodes.ParameterNeedsParticle, source, p.Syntax!.Name.Span, [p.Name], label: "needs a particle",
                help: "label it with `from`, `to`, `until`, `through`, `at`, `by`, `for` or `as`");
    }
}
