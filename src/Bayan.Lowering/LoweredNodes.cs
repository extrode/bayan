using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Lowering;

// The lowered tree reuses the simple bound nodes (literals, locals, fields, calls, unary and
// binary operators, conversions, struct/tuple/array literals, indexing, slicing, blocks, `if`,
// assignments, expression statements) and adds the nodes below. Everything with control flow
// inside an expression (`try`, `catch`, `??`, `switch`, patterns, `with`, interpolation) and
// every loop, `switch` statement and cleanup is rewritten into these forms, so the C emitter
// needs no knowledge of evaluation order, cleanup or failure.

/// <summary>A program ready for code generation.</summary>
public sealed record LoweredProgram(
    Bayan.Semantics.Compilation Compilation,
    IReadOnlyList<LoweredFunction> Functions,
    FunctionSymbol? Main,
    IReadOnlyList<(TestSymbol Test, LoweredFunction Function)> Tests);

/// <summary>A function body with every local (not parameter) it uses, in first-use order.</summary>
public sealed record LoweredFunction(FunctionSymbol Symbol, IReadOnlyList<LocalSymbol> Locals, int ContextSlots, int PanicSlots, BoundBlock Body);

/// <summary>A jump target; emitted as <c>name:;</c>.</summary>
public sealed record LLabel(Node Syntax, string Name) : BoundStmt(Syntax);

public sealed record LGoto(Node Syntax, string Label) : BoundStmt(Syntax);

/// <summary><c>for (;;) { body }</c>; leave it only through <see cref="LGoto"/>.</summary>
public sealed record LLoop(Node Syntax, BoundBlock Body) : BoundStmt(Syntax);

/// <summary>
/// Returns normally. For a failing function the value goes to the result pointer and the error
/// code is 0; a constructor returns its <c>this</c>.
/// </summary>
public sealed record LReturn(Node Syntax, BoundExpr? Value) : BoundStmt(Syntax);

/// <summary>Returns the error code in <see cref="Code"/> (a local of an error type) from a failing function.</summary>
public sealed record LReturnError(Node Syntax, BoundExpr Code) : BoundStmt(Syntax);

/// <summary><c>error = call(…, &amp;result)</c> for a function that can fail.</summary>
public sealed record LFailingCall(Node Syntax, LocalSymbol Error, BoundCall Call, LocalSymbol? Result) : BoundStmt(Syntax);

public enum WriteSink { Print, Panic, StrBuf }

/// <summary>
/// Formats <see cref="Parts"/> into a stack buffer and sends it to the sink: printed with a
/// newline, raised as a panic message, or appended to the <c>StrBuf</c> <see cref="Target"/>.
/// </summary>
public sealed record LWrite(Node Syntax, WriteSink Sink, BoundExpr? Target, BoundInterpolationPart[] Parts) : BoundStmt(Syntax);

/// <summary>Panics with a fixed message (failed <c>assert</c>, unmatched <c>switch</c>).</summary>
public sealed record LPanic(Node Syntax, string Message) : BoundStmt(Syntax);

/// <summary>
/// Sets <c>context.allocator</c> to <see cref="Arena"/>, saving the previous one in slot
/// <see cref="Slot"/>; with no arena, restores the saved allocator.
/// </summary>
public sealed record LSetContext(Node Syntax, BoundExpr? Arena, int Slot) : BoundStmt(Syntax);

/// <summary>Frees an arena declared with <c>using</c>.</summary>
public sealed record LFreeArena(Node Syntax, BoundExpr Arena) : BoundStmt(Syntax);

/// <summary>
/// Registers the <c>defer panicking</c> handler in slot <see cref="Slot"/>: on a panic the runtime jumps back here, sets
/// <see cref="Panic"/> and runs <see cref="Body"/>, which ends by handing the panic to the next handler.
/// </summary>
public sealed record LPushPanicHandler(Node Syntax, int Slot, LocalSymbol? Panic, BoundBlock Body) : BoundStmt(Syntax);

/// <summary>Unregisters the handler in slot <see cref="Slot"/> as its scope ends.</summary>
public sealed record LPopPanicHandler(Node Syntax, int Slot) : BoundStmt(Syntax);

/// <summary>Debug builds: a <c>switch (move x)</c> binding owns its storage once its arm is selected; nothing otherwise.</summary>
public sealed record LClaim(Node Syntax, LocalSymbol Local) : BoundStmt(Syntax);

// ── Expressions ──────────────────────────────────────────────────────────────────

/// <summary>Whether an optional holds a value.</summary>
public sealed record LOptionalHas(Node Syntax, BoundExpr Optional) : BoundExpr(Syntax, PrimitiveType.Bool);

/// <summary>The value inside an optional already known to hold one.</summary>
public sealed record LOptionalValue(Node Syntax, BoundExpr Optional, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>A union's tag: its variant's declaration index.</summary>
public sealed record LUnionTag(Node Syntax, BoundExpr Union) : BoundExpr(Syntax, PrimitiveType.I32);

/// <summary>A payload field of a union already known to hold <see cref="Variant"/>.</summary>
public sealed record LUnionField(Node Syntax, BoundExpr Union, UnionVariantSymbol Variant, int Index)
    : BoundExpr(Syntax, Variant.Fields[Index].Type);

/// <summary><c>(target = value)</c> as an expression; used for pattern bindings.</summary>
public sealed record LAssignExpr(Node Syntax, BoundExpr Target, BoundExpr Value) : BoundExpr(Syntax, Target.Type);

/// <summary>C's comma operator: evaluates <see cref="Effects"/>, then <see cref="Value"/>.</summary>
public sealed record LSequence(Node Syntax, BoundExpr[] Effects, BoundExpr Value) : BoundExpr(Syntax, Value.Type);

/// <summary>An element read whose index the compiler proved in range (loop over a sequence).</summary>
public sealed record LIndexUnchecked(Node Syntax, BoundExpr Target, BoundExpr Index, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>The next char of <see cref="Text"/> at byte offset <see cref="Position"/> (a local), advancing it.</summary>
public sealed record LNextChar(Node Syntax, BoundExpr Text, LocalSymbol Position) : BoundExpr(Syntax, PrimitiveType.Char);

/// <summary>The value of an expression that never completes (it jumped, failed or panicked).</summary>
public sealed record LNever(Node Syntax, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>The value of a <c>void</c> expression whose effects were already emitted.</summary>
public sealed record LVoid(Node Syntax) : BoundExpr(Syntax, PrimitiveType.Void);
