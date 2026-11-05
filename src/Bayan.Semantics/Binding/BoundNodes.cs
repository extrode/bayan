using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Semantics.Binding;

/// <summary>A compile-time value: <see cref="Int128"/> for integers, <c>double</c>, <c>bool</c>, <c>string</c>, or <see cref="System.Text.Rune"/> for chars.</summary>
public sealed record ConstantValue(object Value)
{
    public Int128 Integer => (Int128)Value;

    public override string ToString() => Value switch
    {
        bool b => b ? "true" : "false",
        string s => "\"" + s + "\"",
        _ => Value.ToString() ?? "",
    };
}

/// <summary>Base of the typed tree the binder produces and lowering rewrites.</summary>
public abstract record BoundNode(Node Syntax);

// ── Expressions ──────────────────────────────────────────────────────────────────

public abstract record BoundExpr(Node Syntax, TypeSymbol Type) : BoundNode(Syntax)
{
    /// <summary>Set when the expression is a compile-time constant.</summary>
    public ConstantValue? Constant { get; init; }
}

public sealed record BoundLiteral(Node Syntax, TypeSymbol Type, ConstantValue Value) : BoundExpr(Syntax, Type);

/// <summary><c>Target.os</c> (or <c>Target.arch</c>) of a build with no <c>--target</c>: not a constant, the C preprocessor resolves it so the emitted C is host-neutral.</summary>
public sealed record BoundHostTarget(Node Syntax, bool IsOs) : BoundExpr(Syntax, PrimitiveType.String);

/// <summary>A string literal where a <c>Symbol</c> is expected: an entry of the program's literal table, fixed at compile time (spec 008 R2).</summary>
public sealed record BoundSymbolLiteral(Node Syntax, string Text) : BoundExpr(Syntax, BuiltinType.Symbol);

/// <summary>The default (zero) value of a type, or <c>null</c> of an optional.</summary>
public sealed record BoundDefault(Node Syntax, TypeSymbol Type) : BoundExpr(Syntax, Type);

public sealed record BoundLocal(Node Syntax, LocalSymbol Local) : BoundExpr(Syntax, Local.Type);

/// <summary><c>this</c>: a struct, or the synthetic <c>Self</c> type parameter in an interface's default-method template.</summary>
public sealed record BoundThis(Node Syntax, TypeSymbol Type) : BoundExpr(Syntax, Type)
{
    /// <summary>The struct <c>this</c> points to; only templates (never lowered or emitted) have a <c>Self</c> type here.</summary>
    public StructSymbol Struct => (StructSymbol)Type;
}

public sealed record BoundField(Node Syntax, BoundExpr Receiver, FieldSymbol Field) : BoundExpr(Syntax, Field.Type);

public sealed record BoundTupleElement(Node Syntax, BoundExpr Receiver, int Index, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>A call. <see cref="Arguments"/> are in parameter order with defaults filled in.</summary>
public sealed record BoundCall(Node Syntax, FunctionSymbol Function, BoundExpr? Receiver, BoundExpr[] Arguments, TypeSymbol Type)
    : BoundExpr(Syntax, Type);

/// <summary>Members of built-in types (<c>List.length</c>, <c>List.append</c>, …).</summary>
public enum BuiltinMember
{
    StringLength, StringBytes, StringIsEmpty, StringRuneCount, StringIsAscii, StringCopy, StringStartsWith, StringEndsWith, StringContains,
    StringPosition, StringLastPosition, StringCount, StringTrimmed, StringTrimmedBy, StringLowercased, StringUppercased, StringEquals,
    StringReplaced, StringRepeated, StringJoined, StringPadded,
    /// <summary><c>string(bytes)</c> on a byte span, and <c>T.tryParse(text, as:)</c> on an integer type (the receiver is the text).</summary>
    BytesToString, IntTryParse,
    /// <summary>The views <c>runes</c>, <c>utf16</c>, <c>lines</c> and <c>parts(by:)</c> (a <c>rune</c> separator is <c>StringPartsRune</c>), and the view members <c>runes.take(n)</c>, <c>skip</c>, <c>takeLast</c> and <c>utf16.copy()</c>.</summary>
    StringRunes, StringUtf16, StringLines, StringParts, StringPartsRune, RunesTake, RunesSkip, RunesTakeLast, Utf16Copy,
    /// <summary>Lowering of <c>for</c> over a text view: whether it has another item at the cursor, and the item (advancing the cursor).</summary>
    TextViewMore, TextViewNext,
    SpanLength, SpanIsEmpty, SpanCopy, ListCopy, MapCopy, StrBufCopy,
    ListLength, ListIsEmpty, ListAdd, ListAsSpan, ListMutableSpan, ArrayMutableSpan, ListClear, ListRemoveLast, ListRemoveRange, ListRemoveThrough, ListRemoveAt, ListRemoveAtRange, ListReplace, ListSort, ListTryGet,
    MapLength, MapTryGet, MapContains, MapRemove, MapClear,
    /// <summary><c>map[k]</c> read (panics when missing), <c>map[k]</c> as a write target (inserts when missing), and <c>map.slot(k)</c> (inserts the default).</summary>
    MapIndex, MapIndexSet, MapSlot,
    /// <summary>Lowering of <c>for</c> over a map: entry slots (live or removed), whether slot i is live, and its entry.</summary>
    MapSlotCount, MapSlotLive, MapSlotEntry,
    ArrayLength, ArrayAsSpan,
    ArenaReset,
    StrBufAppend, StrBufAppendRepeated, StrBufInsert, StrBufLength, StrBufAsString, StrBufClear,
    /// <summary><c>sym.text</c>: the interned text, which lives until exit.</summary>
    SymbolText,
    /// <summary><c>x.take()</c>: moves the value out of a place and leaves the type's empty value behind.</summary>
    ListTake, MapTake, StrBufTake, OptionalTake,
    CharValue, IntToString,
    ResultIsOk, ResultValue, ResultError,
    /// <summary><c>o.value</c> of an <c>Owned&lt;T&gt;</c>, also inserted for member access through it.</summary>
    OwnedValue,
    IoWrite,
    IoReadAll,
    EnumName, EnumOrdinal,
    /// <summary>A data-enum field; the argument is the field index.</summary>
    EnumField,
    ProcessExitCode, ProcessStdout, ProcessStderr,
    PanicMessage,
    FormatSpecRadix, FormatSpecDebug, FormatSpecWidth, FormatSpecFill, FormatSpecAlign, FormatSpecDigits,
    /// <summary><c>f64.tryParse(text)</c> / <c>f32.tryParse(text)</c> (the receiver is the text; revision 0015).</summary>
    FloatTryParse,
}

public sealed record BoundBuiltinCall(Node Syntax, BuiltinMember Member, BoundExpr Receiver, BoundExpr[] Arguments, TypeSymbol Type)
    : BoundExpr(Syntax, Type);

public sealed record BoundUnary(Node Syntax, UnaryOp Op, BoundExpr Operand, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>Both operands already have the operand type (after widening); comparisons have type <c>bool</c>.</summary>
public sealed record BoundBinary(Node Syntax, BinaryOp Op, BoundExpr Left, BoundExpr Right, TypeSymbol Type) : BoundExpr(Syntax, Type);

public enum ConversionKind
{
    Identity,
    /// <summary>Lossless numeric widening.</summary>
    Widen,
    /// <summary>Explicit numeric cast; narrowing traps in checked builds, float→int saturates.</summary>
    NumericCast,
    CharToInteger,
    /// <summary>Checked: the value must be a Unicode scalar.</summary>
    IntegerToChar,
    ToOptional,
    NullToOptional,
    ListToSpan,
    ArrayToSpan,
    /// <summary>A <c>string</c> argument viewed as its UTF-8 bytes (<c>ReadOnlySpan&lt;u8&gt;</c>).</summary>
    StringToBytes,
    /// <summary>A value becomes a successful <c>Result</c>.</summary>
    ToResult,
    /// <summary>An error becomes a failed <c>Result</c>.</summary>
    ErrorToResult,
    /// <summary>A <c>never</c> expression in a position of another type.</summary>
    FromNever,
    /// <summary>A <c>T?</c> let local or parameter read where a null check proved it present (spec §7.2).</summary>
    Narrow,
    /// <summary>A module function named where a <see cref="FunctionType"/> of its exact signature is expected.</summary>
    ToFunction,
    /// <summary>A conforming value or borrow viewed as <c>dyn ref I</c>: a borrow of the operand plus its vtable, never a move.</summary>
    ToDynRef,
}

public sealed record BoundConversion(Node Syntax, BoundExpr Operand, TypeSymbol Type, ConversionKind Kind) : BoundExpr(Syntax, Type);

/// <summary><c>u8.tryFrom(x)</c>: <see cref="Operand"/> converted to <see cref="Target"/>, or <c>null</c> when it does not fit.</summary>
public sealed record BoundTryFrom(Node Syntax, BoundExpr Operand, PrimitiveType Target, TypeSymbol Type) : BoundExpr(Syntax, Type);

public sealed record BoundConditional(Node Syntax, BoundExpr Condition, BoundExpr WhenTrue, BoundExpr WhenFalse, TypeSymbol Type)
    : BoundExpr(Syntax, Type);

/// <summary><c>a ?? b</c>: <see cref="Left"/> is optional; <see cref="Right"/> has the result type or is <c>never</c>.</summary>
public sealed record BoundCoalesce(Node Syntax, BoundExpr Left, BoundExpr Right, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>A struct value with every field given, in declaration order.</summary>
public sealed record BoundStructLiteral(Node Syntax, StructSymbol Struct, BoundExpr[] Fields) : BoundExpr(Syntax, Struct)
{
    /// <summary>The state of a state literal <c>T.s { … }</c>, whose type is then <c>T.s</c>; null for a struct without states.</summary>
    public UnionVariantSymbol? State { get; init; }

    /// <summary>The payload of <see cref="State"/>, by its field order.</summary>
    public BoundExpr[] Payload { get; init; } = [];
}

public sealed record BoundWith(Node Syntax, BoundExpr Target, (FieldSymbol Field, BoundExpr Value)[] Updates) : BoundExpr(Syntax, Target.Type);

public sealed record BoundTuple(Node Syntax, TupleType TupleType, BoundExpr[] Elements) : BoundExpr(Syntax, TupleType);

/// <summary><c>[a, b]</c> as a <c>List</c>, <c>Span</c> (stack storage) or fixed array; for a <c>Map</c>, <see cref="Elements"/> alternate key and value.</summary>
public sealed record BoundCollection(Node Syntax, TypeSymbol Type, BoundExpr[] Elements) : BoundExpr(Syntax, Type);

/// <summary><c>..xs</c> inside a <see cref="BoundCollection"/>: every element of a list, span or fixed array; <see cref="BoundExpr.Type"/> is the element type.</summary>
public sealed record BoundSpread(Node Syntax, BoundExpr Operand, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary><c>xs[i]</c> or <c>xs[^i]</c> on spans, lists and arrays; always bounds-checked.</summary>
public sealed record BoundIndex(Node Syntax, BoundExpr Target, BoundExpr Index, bool FromEnd, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary><c>xs[a..b]</c>: a span or (for strings) a string. Ends may be omitted or count from the end.</summary>
public sealed record BoundSlice(
    Node Syntax, BoundExpr Target, BoundExpr? Start, bool StartFromEnd, BoundExpr? End, bool EndFromEnd, bool Inclusive, TypeSymbol Type)
    : BoundExpr(Syntax, Type);

public sealed record BoundRange(Node Syntax, BoundExpr Start, BoundExpr End, bool Inclusive, TypeSymbol Type) : BoundExpr(Syntax, Type);

public sealed record BoundIs(Node Syntax, BoundExpr Value, BoundPattern Pattern) : BoundExpr(Syntax, PrimitiveType.Bool);

public sealed record BoundSwitchArm(Node Syntax, BoundPattern Pattern, BoundExpr? Guard, BoundExpr Value);

public sealed record BoundSwitchExpr(Node Syntax, BoundExpr Value, BoundSwitchArm[] Arms, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>One piece of <c>$"…"</c>: literal text or a value to format.</summary>
public sealed record BoundInterpolationPart(string? Text, BoundExpr? Value)
{
    /// <summary>The hole's checked specs (<c>{n, as: .hex, width: 6}</c>, spec §16.4); null without any.</summary>
    public BoundFormatSpec? Spec { get; init; }
}

/// <summary>
/// Constant format specs: <see cref="Radix"/> 0 (none), 2, 8, 10 or 16; <see cref="Width"/> in runes, 0 for none;
/// <see cref="Align"/> -1 (numbers to the end, the rest to the start), 0 start, 1 end, 2 center; <see cref="Digits"/> -1 for none.
/// </summary>
public sealed record BoundFormatSpec(int Radix, bool Debug, long Width, uint Fill, int Align, int Digits);

/// <summary><c>$"…"</c>. Written straight into an output sink (<c>print</c>, <c>panic</c>, <c>StrBuf.append</c>); elsewhere a string from <c>context.allocator</c>.</summary>
public sealed record BoundInterpolated(Node Syntax, BoundInterpolationPart[] Parts) : BoundExpr(Syntax, PrimitiveType.String);

/// <summary><c>try f()</c>: on failure, runs <c>errdefer</c>s and fails the enclosing function with the same error.</summary>
/// <summary><c>move x</c> of a local or <c>move</c> parameter: the value is handed over and <c>x</c> may not be read until reassigned.</summary>
public sealed record BoundMove(Node Syntax, BoundExpr Operand) : BoundExpr(Syntax, Operand.Type);

public sealed record BoundTry(Node Syntax, BoundExpr Operand, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>
/// <c>f() catch h</c>. <see cref="Binding"/> receives the error; with <see cref="Variant"/> only that
/// variant is handled and the rest propagate.
/// </summary>
public sealed record BoundCatch(Node Syntax, BoundExpr Operand, LocalSymbol? Binding, ErrorVariantSymbol? Variant, BoundExpr Handler, TypeSymbol Type)
    : BoundExpr(Syntax, Type);

/// <summary><c>return</c>, <c>break</c> or <c>continue</c> in expression position; type <c>never</c>.</summary>
public sealed record BoundJump(Node Syntax, BoundStmt Jump) : BoundExpr(Syntax, PrimitiveType.Never);

/// <summary><c>catch { … }</c> handler; type <c>void</c> for a <c>void</c> operand, else <c>never</c> (flow analysis checks the claim).</summary>
public sealed record BoundBlockExpr(Node Syntax, BoundBlock Block, TypeSymbol Type) : BoundExpr(Syntax, Type);

public sealed record BoundErrorVariant(Node Syntax, ErrorVariantSymbol Variant) : BoundExpr(Syntax, Variant.Set)
{
    /// <summary>The payload of <c>.timeout(after)</c>, one value per field of <see cref="Variant"/>; empty for a bare case.</summary>
    public BoundExpr[] Payload { get; init; } = [];
}

/// <summary><c>Color.red</c> or a target-typed <c>.red</c>.</summary>
public sealed record BoundEnumMember(Node Syntax, EnumMemberSymbol Member) : BoundExpr(Syntax, Member.Enum);

/// <summary><c>Shape.Circle(1)</c>, <c>Shape.None</c> or a target-typed <c>.Circle(1)</c>.</summary>
public sealed record BoundUnionNew(Node Syntax, UnionVariantSymbol Variant, BoundExpr[] Payload) : BoundExpr(Syntax, Variant.Union);

/// <summary><c>Color.values</c>: a <c>ReadOnlySpan</c> over the enum's static table of members.</summary>
public sealed record BoundEnumValues(Node Syntax, EnumSymbol Enum, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>A module-level <c>const T[N]</c> of literal scalars, read in place from static data.</summary>
public sealed record BoundStaticArray(Node Syntax, ConstSymbol Const) : BoundExpr(Syntax, Const.Type);

/// <summary><c>stackalloc T[n]</c> with a constant <c>n</c>: a span over stack storage.</summary>
public sealed record BoundStackalloc(Node Syntax, TypeSymbol Element, long Count, TypeSymbol Type) : BoundExpr(Syntax, Type);

/// <summary>An expression that failed to bind; its diagnostic was already reported.</summary>
public sealed record BoundErrorExpr(Node Syntax) : BoundExpr(Syntax, ErrorType.Instance);

/// <summary>A type or module used where an expression appears (<c>Point.origin</c>, <c>money.parse</c>); never survives binding.</summary>
public sealed record BoundTypeRef(Node Syntax, TypeSymbol Referenced) : BoundExpr(Syntax, ErrorType.Instance);

public sealed record BoundModuleRef(Node Syntax, ModuleSymbol Module) : BoundExpr(Syntax, ErrorType.Instance);

/// <summary>A lambda: <see cref="Lambda"/> is the lifted function; <see cref="Captures"/> read each captured local as the value is made.</summary>
public sealed record BoundLambda(Node Syntax, FunctionSymbol Lambda, BoundLocal[] Captures, FunctionType FunctionType) : BoundExpr(Syntax, FunctionType);

/// <summary>A module function used as a value of a <see cref="FunctionType"/>.</summary>
public sealed record BoundFunctionRef(Node Syntax, FunctionSymbol Function, FunctionType FunctionType) : BoundExpr(Syntax, FunctionType);

// ── Statements ───────────────────────────────────────────────────────────────────

public abstract record BoundStmt(Node Syntax) : BoundNode(Syntax);

public sealed record BoundBlock(Node Syntax, BoundStmt[] Statements) : BoundStmt(Syntax);

public sealed record BoundLocalDecl(Node Syntax, LocalSymbol Local, BoundExpr? Initializer) : BoundStmt(Syntax);

public sealed record BoundExprStmt(Node Syntax, BoundExpr Expr) : BoundStmt(Syntax);

/// <summary><c>t = v</c>, or <c>t op= v</c> when <see cref="Op"/> is set (the target is evaluated once).</summary>
public sealed record BoundAssign(Node Syntax, BoundExpr Target, BoundExpr Value, BinaryOp? Op) : BoundStmt(Syntax);

public sealed record BoundReturn(Node Syntax, BoundExpr? Value) : BoundStmt(Syntax);

/// <summary>Fails the enclosing function with <see cref="Error"/> (a value of an error-set type).</summary>
public sealed record BoundFail(Node Syntax, BoundExpr Error) : BoundStmt(Syntax);

/// <summary><c>become .s(…)</c>: <c>this</c> moves to <see cref="State"/>, a case of its struct's states union, with its payload.</summary>
public sealed record BoundBecome(Node Syntax, UnionVariantSymbol State, BoundExpr[] Payload) : BoundStmt(Syntax);

/// <summary>Identity of a loop or switch for <c>break</c>/<c>continue</c>.</summary>
public sealed class JumpTarget(string? label, bool isLoop)
{
    public string? Label { get; } = label;

    public bool IsLoop { get; } = isLoop;

    public int Id { get; set; }
}

public sealed record BoundBreak(Node Syntax, JumpTarget Target) : BoundStmt(Syntax);

public sealed record BoundContinue(Node Syntax, JumpTarget Target) : BoundStmt(Syntax);

public sealed record BoundIf(Node Syntax, BoundExpr Condition, BoundStmt Then, BoundStmt? Else) : BoundStmt(Syntax)
{
    /// <summary><c>if never</c>: after <see cref="Then"/>, panics in every build mode.</summary>
    public bool Never { get; init; }
}

public sealed record BoundWhile(Node Syntax, BoundExpr Condition, BoundStmt Body, JumpTarget Target) : BoundStmt(Syntax);

public sealed record BoundDoWhile(Node Syntax, BoundStmt Body, BoundExpr Condition, JumpTarget Target) : BoundStmt(Syntax);

/// <summary><c>for (let x in e)</c> over a range, span, list, array or string (by char).</summary>
public sealed record BoundFor(Node Syntax, LocalSymbol Variable, BoundExpr Iterable, BoundStmt Body, JumpTarget Target) : BoundStmt(Syntax)
{
    /// <summary>Set when <see cref="Iterable"/> is a user type iterated through <c>IIndexable</c> or <c>IEnumerator</c>.</summary>
    public UserIteration? UserIteration { get; init; }
}

public enum IterationKind { Indexable, Enumerator }

/// <summary>
/// How a <c>for</c> loop walks a user type: <see cref="Advance"/> is the <c>count</c> getter or <c>moveNext</c>,
/// <see cref="Item"/> is <c>get</c> or the <c>current</c> getter (either getter may be a field), and <see cref="Spill"/> holds an rvalue enumerator.
/// </summary>
public sealed record UserIteration(IterationKind Kind, Symbol Advance, Symbol Item, LocalSymbol? Spill);

/// <summary>A <c>case</c>; a null <see cref="Pattern"/> is <c>default</c>.</summary>
public sealed record BoundSwitchSection(Node Syntax, BoundPattern? Pattern, BoundExpr? Guard, BoundStmt[] Body);

public sealed record BoundSwitch(Node Syntax, BoundExpr Value, BoundSwitchSection[] Sections, JumpTarget Target) : BoundStmt(Syntax)
{
    /// <summary>Some section always matches (a <c>default</c>, or every value of a finite type is covered).</summary>
    public bool Exhaustive { get; init; }
}

public sealed record BoundDefer(Node Syntax, BoundStmt Body) : BoundStmt(Syntax);

/// <summary><c>assert condition;</c>; <see cref="BoundNode.Syntax"/> is the <c>AssertStmt</c>.</summary>
public sealed record BoundAssert(Node Syntax, BoundExpr Condition) : BoundStmt(Syntax)
{
    /// <summary><c>assert f() fails E[.x]</c>: <see cref="Condition"/> is the call, which must fail with a case of this set.</summary>
    public ErrorSetSymbol? FailsWith { get; init; }

    /// <summary>The one case <c>assert f() fails E.x</c> expects; null when any case of <see cref="FailsWith"/> will do.</summary>
    public ErrorVariantSymbol? FailsCase { get; init; }
}

public sealed record BoundErrdefer(Node Syntax, LocalSymbol? Error, BoundStmt Body) : BoundStmt(Syntax);

/// <summary><c>defer panicking [(p)] body</c>: runs only while a panic is in progress; <see cref="Panic"/> receives it.</summary>
public sealed record BoundPanicDefer(Node Syntax, LocalSymbol? Panic, BoundStmt Body) : BoundStmt(Syntax);

/// <summary><c>Panic.onPanic = f;</c>, the first statement of <c>main</c>.</summary>
public sealed record BoundSetOnPanic(Node Syntax, FunctionSymbol Handler) : BoundStmt(Syntax);

/// <summary><c>context (allocator: a) { … }</c>.</summary>
public sealed record BoundContext(Node Syntax, BoundExpr Allocator, BoundBlock Body) : BoundStmt(Syntax);

/// <summary>A statement that failed to bind.</summary>
public sealed record BoundErrorStmt(Node Syntax) : BoundStmt(Syntax);

// ── Patterns ─────────────────────────────────────────────────────────────────────

public abstract record BoundPattern(Node Syntax, TypeSymbol InputType) : BoundNode(Syntax);

/// <summary>Equality with a constant (or error variant) of the input type.</summary>
public sealed record BoundConstantPattern(Node Syntax, TypeSymbol InputType, BoundExpr Value) : BoundPattern(Syntax, InputType);

public sealed record BoundRelationalPattern(Node Syntax, TypeSymbol InputType, BinaryOp Op, BoundExpr Value) : BoundPattern(Syntax, InputType);

public sealed record BoundRangePattern(Node Syntax, TypeSymbol InputType, BoundExpr Start, BoundExpr End, bool Inclusive) : BoundPattern(Syntax, InputType);

/// <summary><c>var x</c> (binds) or <c>_</c> (<see cref="Local"/> null); always matches.</summary>
public sealed record BoundBindPattern(Node Syntax, TypeSymbol InputType, LocalSymbol? Local) : BoundPattern(Syntax, InputType);

public sealed record BoundNotPattern(Node Syntax, TypeSymbol InputType, BoundPattern Operand) : BoundPattern(Syntax, InputType);

public sealed record BoundBinaryPattern(Node Syntax, TypeSymbol InputType, bool IsAnd, BoundPattern Left, BoundPattern Right)
    : BoundPattern(Syntax, InputType);

/// <summary>One <c>member: pattern</c> of a property pattern; <see cref="Path"/> are field/property/tuple accesses.</summary>
public sealed record BoundSubpattern(BoundMemberStep[] Path, BoundPattern Pattern);

public sealed record BoundMemberStep(FieldSymbol? Field, PropertySymbol? Property, int TupleIndex, BuiltinMember? Builtin, TypeSymbol Type);

/// <summary>
/// <c>{ a: p } v</c>. On an optional input the value must be present, and subpatterns and
/// <see cref="Designation"/> see the unwrapped value (<see cref="ValueType"/>).
/// </summary>
public sealed record BoundPropertyPattern(Node Syntax, TypeSymbol InputType, TypeSymbol ValueType, BoundSubpattern[] Subpatterns, LocalSymbol? Designation)
    : BoundPattern(Syntax, InputType);

/// <summary><c>p.rate</c> where <c>p</c> was bound by <c>.percentOff p</c>: a payload field of a value known to hold <see cref="Variant"/>.</summary>
public sealed record BoundUnionField(Node Syntax, BoundExpr Receiver, UnionVariantSymbol Variant, int Index)
    : BoundExpr(Syntax, Variant.Fields[Index].Type);

/// <summary>
/// <c>.Circle(let r)</c> or <c>.None</c>: a tag test, then each subpattern on its payload field.
/// On an optional input the value must be present. <c>.Circle c</c> binds the whole value to <see cref="Designation"/>.
/// </summary>
public sealed record BoundUnionPattern(Node Syntax, TypeSymbol InputType, UnionVariantSymbol Variant, BoundPattern[] Subpatterns, LocalSymbol? Designation = null)
    : BoundPattern(Syntax, InputType);

/// <summary><c>e.after</c> where <c>e</c> was bound by <c>.timeout e</c>: a payload field of an error known to be <see cref="Variant"/>.</summary>
public sealed record BoundErrorField(Node Syntax, BoundExpr Receiver, ErrorVariantSymbol Variant, int Index)
    : BoundExpr(Syntax, Variant.Fields[Index].Type);

/// <summary><c>.timeout(let after)</c> on an error: a test of its code, then each subpattern on its payload field.</summary>
public sealed record BoundErrorPattern(Node Syntax, TypeSymbol InputType, ErrorVariantSymbol Variant, BoundPattern[] Subpatterns)
    : BoundPattern(Syntax, InputType);

// ── `requires` checks ────────────────────────────────────────────────────────────

/// <summary>A struct's <c>requires</c> check: the hidden method run on each built value, and its rules in run order.</summary>
public sealed record StructChecks(FunctionSymbol Check, List<StructRule> Rules);

/// <summary>One rule: a field's own rule (<c>it</c> is <paramref name="Field"/>), or a struct-level one when the field is null.</summary>
public sealed record StructRule(RequiresRule Syntax, FieldSymbol? Field, BoundExpr Condition)
{
    /// <summary>The fields of <c>this</c> the condition reads.</summary>
    public FieldSymbol[] Reads { get; } = BoundTree.Descendants(Condition).OfType<BoundField>()
        .Where(f => f.Receiver is BoundThis).Select(f => f.Field).Distinct().ToArray();
}
