using Bayan.Syntax.Ast;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Spec 013 R2: <see cref="Original"/> (an expression or a statement) runs unless the process-wide selector
/// <c>byn_mutant</c> names one of <see cref="Mutants"/>, which runs instead. Only a <c>--mutate</c> build makes one,
/// beside the bound tree rather than in it: the lowerer looks each node up. Neither a <see cref="BoundNode"/> nor in
/// BoundNodes.cs, so the self-hosted bound tree, which it never enters, needs no mirror of it.
/// </summary>
public sealed record BoundMutantSwitch(Node Syntax, BoundNode Original, BoundMutant[] Mutants);

/// <summary>One arm of a <see cref="BoundMutantSwitch"/>: the node that replaces the original when <c>byn_mutant</c> is <see cref="Id"/>.</summary>
public sealed record BoundMutant(int Id, BoundNode Node);
