using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Semantics.Binding;

/// <summary>Spec 013 R9: counter <see cref="Id"/> counts each time the statement it belongs to starts.</summary>
public sealed record BoundProbe(Node Syntax, int Id);

/// <summary>Spec 013 R9: a condition's outcomes, counter <see cref="WhenTrue"/> when it is true and <see cref="WhenFalse"/> when false.</summary>
public sealed record BoundBranchProbe(Node Syntax, int WhenTrue, int WhenFalse);

/// <summary>
/// Spec 013 R9: on the error-return edge of a <c>fail</c>, <c>try</c> or <c>catch .v</c> (the origin), the counter of
/// the variant the function returns.
/// </summary>
public sealed record BoundErrorProbe(Node Syntax, IReadOnlyList<(ErrorVariantSymbol Variant, int Id)> Variants);

/// <summary>
/// The probes of a <c>--coverage</c> build and its counter count. Like <see cref="BoundMutantSwitch"/> they sit beside
/// the bound tree, keyed by node reference (statements, conditions, error origins): the lowerer looks each node up.
/// </summary>
public sealed record CoverageProbes(int Count, IReadOnlyDictionary<BoundNode, BoundProbe> Statements,
    IReadOnlyDictionary<BoundNode, BoundBranchProbe> Branches, IReadOnlyDictionary<BoundNode, BoundErrorProbe> Errors);
