using Bayan.Syntax.Text;

namespace Bayan.Syntax.Ast;

/// <summary>Base of every syntax node. Spans are byte ranges in the node's source.</summary>
public abstract record Node(TextSpan Span);

/// <summary>An identifier with its span; <see cref="Text"/> is the decoded name.</summary>
public sealed record Name(TextSpan Span, string Text) : Node(Span)
{
    public override string ToString() => Text;
}

[Flags]
public enum Modifiers
{
    None = 0,
    Public = 1 << 0,
    Private = 1 << 1,
    Static = 1 << 2,
    Readonly = 1 << 3,
    Required = 1 << 4,
    Const = 1 << 5,
    Unique = 1 << 6,
    Extern = 1 << 7,
    Unsafe = 1 << 8,
}

// ── Compilation unit and declarations ────────────────────────────────────────────

public sealed record CompilationUnit(TextSpan Span, Decl[] Members) : Node(Span);

/// <summary>An attribute: <c>@name</c> or <c>@name(args)</c> before a declaration or an error variant.</summary>
public sealed record AttributeSyntax(TextSpan Span, Name Name, Argument[] Args) : Node(Span)
{
    public BecauseClause? Because { get; init; }
}

/// <summary><c>because "reason"</c>: why an attribute or a call bypasses a safety rule (J6).</summary>
public sealed record BecauseClause(TextSpan Span, string Reason) : Node(Span);

/// <summary><c>requires condition</c>, or a struct-level <c>requires condition at .field</c>; <c>it</c> is the field's value.</summary>
public sealed record RequiresRule(TextSpan Span, Expr Condition, Name? At) : Node(Span);

public abstract record Decl(TextSpan Span) : Node(Span)
{
    /// <summary>The <c>///</c> doc comment text (markers stripped), if any.</summary>
    public string? Doc { get; init; }

    public AttributeSyntax[] Attributes { get; init; } = [];
}

public sealed record ImportDecl(TextSpan Span, Name[] Path, Name? Alias) : Decl(Span);

/// <summary>A statement written at module level; together they form the implicit <c>main</c>.</summary>
public sealed record GlobalStatement(TextSpan Span, Stmt Statement) : Decl(Span);

public enum ParamModifier { None, Ref, Move, Out, In }

public sealed record Param(TextSpan Span, ParamModifier Modifier, TypeSyntax Type, Name Name, Expr? Default) : Node(Span);

/// <summary><c>fails</c> clause: <see cref="Errors"/> empty means the set is inferred.</summary>
public sealed record FailsClause(TextSpan Span, TypeSyntax[] Errors) : Node(Span);

/// <summary>A function or method. Exactly one of <see cref="Body"/> and <see cref="ExpressionBody"/> is set, or neither for a declaration.</summary>
public sealed record FunctionDecl(
    TextSpan Span, Modifiers Modifiers, TypeSyntax ReturnType, Name Name, Param[] Params,
    FailsClause? Fails, BlockStmt? Body, Expr? ExpressionBody) : Decl(Span)
{
    /// <summary>The <c>&lt;T, U&gt;</c> after the name; empty for a non-generic function.</summary>
    public Name[] TypeParams { get; init; } = [];

    /// <summary>The <c>ref this</c> written as parameter 0 of a mutating member; not in <see cref="Params"/>.</summary>
    public TextSpan? RefThis { get; init; }

    /// <summary>The state of a receiver <c>this is .s</c> or <c>ref this is .s</c>; parsed only, not yet checked.</summary>
    public Name? ReceiverState { get; init; }

    /// <summary>Where a <c>ref this</c> would go: just after the <c>(</c>.</summary>
    public int ParamsStart { get; init; }

    /// <summary>The <c>requires</c> clauses after the signature, checked on entry.</summary>
    public Expr[] Requires { get; init; } = [];

    /// <summary>The <c>where T : I</c> clauses after <c>fails</c>.</summary>
    public WhereClause[] Where { get; init; } = [];
}

/// <summary><c>where T : IA, IB</c>: the interfaces a type parameter must conform to.</summary>
public sealed record WhereClause(TextSpan Span, Name Param, NamedType[] Bounds) : Node(Span);

public sealed record StructDecl(TextSpan Span, Modifiers Modifiers, Name Name, Decl[] Members) : Decl(Span)
{
    public Name[] TypeParams { get; init; } = [];

    /// <summary>The interfaces after <c>:</c>.</summary>
    public NamedType[] Bases { get; init; } = [];

    public WhereClause[] Where { get; init; } = [];

    /// <summary>The struct-level <c>requires</c> rules: those that read more than <c>it</c> (O3).</summary>
    public RequiresRule[] Rules { get; init; } = [];
}

/// <summary><c>interface IName&lt;T&gt; { … }</c>: methods (abstract without a body), getters, associated types.</summary>
public sealed record InterfaceDecl(TextSpan Span, Modifiers Modifiers, Name Name, Decl[] Members) : Decl(Span)
{
    public Name[] TypeParams { get; init; } = [];

    public WhereClause[] Where { get; init; } = [];
}

/// <summary><c>T name { get; }</c>: an abstract read-only property of an interface.</summary>
public sealed record GetterDecl(TextSpan Span, Modifiers Modifiers, TypeSyntax Type, Name Name) : Decl(Span);

/// <summary><c>type Item;</c>: an interface's associated type, bound by each conformer.</summary>
public sealed record AssociatedTypeDecl(TextSpan Span, Name Name) : Decl(Span);

/// <summary><c>extension T : I { … }</c>: retroactive conformance (not supported yet).</summary>
public sealed record ExtensionDecl(TextSpan Span, Name Name, NamedType[] Bases, Decl[] Members) : Decl(Span);

public sealed record FieldDecl(TextSpan Span, Modifiers Modifiers, TypeSyntax Type, Name Name, Expr? Default) : Decl(Span)
{
    /// <summary>The <c>requires</c> rules that read only <c>it</c>, the field's value.</summary>
    public RequiresRule[] Requires { get; init; } = [];
}

/// <summary>A getter-only property: <c>T name => expr</c>, or <c>T name { … }</c> with a <see cref="Block"/> body (J46).</summary>
public sealed record PropertyDecl(TextSpan Span, Modifiers Modifiers, TypeSyntax Type, Name Name, Expr? Body) : Decl(Span)
{
    /// <summary>The block body; set exactly when <see cref="Body"/> is null.</summary>
    public BlockStmt? Block { get; init; }
}

public sealed record ConstructorDecl(TextSpan Span, Modifiers Modifiers, Name Name, Param[] Params, BlockStmt Body) : Decl(Span);

public sealed record ErrorVariant(TextSpan Span, Name Name, string? Doc) : Node(Span)
{
    public AttributeSyntax[] Attributes { get; init; } = [];
}

/// <summary><c>union error Name { a b }</c>, one case per line.</summary>
public sealed record ErrorEnumDecl(TextSpan Span, Modifiers Modifiers, Name Name, ErrorVariant[] Variants) : Decl(Span);

/// <summary><c>union error Name = A, B except .c</c>: a name for several error sets, flattened, less the <see cref="Except"/> cases.</summary>
public sealed record ErrorSetDecl(TextSpan Span, Modifiers Modifiers, Name Name, TypeSyntax[] Members) : Decl(Span)
{
    /// <summary>The two declaration keywords, as written.</summary>
    public TextSpan Head { get; init; }

    public Name[] Except { get; init; } = [];
}

/// <summary>A member; <see cref="Args"/> is <c>null</c> without parentheses.</summary>
public sealed record EnumMember(TextSpan Span, Name Name, string? Doc, Argument[]? Args) : Node(Span);

/// <summary><c>enum Name { a, b }</c>, or <c>enum Name(T x) { a(1), b(2); }</c> with associated data (<see cref="Params"/> non-null).</summary>
public sealed record EnumDecl(TextSpan Span, Modifiers Modifiers, Name Name, Param[]? Params, EnumMember[] Members) : Decl(Span)
{
    public NamedType[] Bases { get; init; } = [];

    public WhereClause[] Where { get; init; } = [];
}

/// <summary>A positional payload field of a union variant: <c>f32 r</c>.</summary>
public sealed record UnionField(TextSpan Span, TypeSyntax Type, Name Name) : Node(Span);

/// <summary><c>circle(f32 r)</c>, or <c>none</c> without a payload; <see cref="Value"/> is the <c>= 1</c> of <c>read = 1</c>.</summary>
public sealed record UnionVariant(TextSpan Span, Name Name, UnionField[] Fields, string? Doc, Expr? Value = null) : Node(Span);

/// <summary><c>union Name { circle(f32 r), none }</c>: a tagged union stored inline.</summary>
public sealed record UnionDecl(TextSpan Span, Modifiers Modifiers, Name Name, UnionVariant[] Variants) : Decl(Span)
{
    public Name[] TypeParams { get; init; } = [];

    public NamedType[] Bases { get; init; } = [];

    public WhereClause[] Where { get; init; } = [];
}

/// <summary><c>type Name = T</c> — a plain alias, or with <see cref="Distinct"/> a <c>type distinct</c>.</summary>
public sealed record TypeAliasDecl(TextSpan Span, Modifiers Modifiers, Name Name, TypeSyntax Target) : Decl(Span)
{
    public bool Distinct { get; init; }

    /// <summary>A <c>type distinct</c>'s <c>requires</c> rules, on <c>it</c>.</summary>
    public RequiresRule[] Requires { get; init; } = [];
}

/// <summary><c>test "name" { … }</c>, or <c>test "name" (T a, U b) { … }</c> run once per <c>[Cases]</c> value.</summary>
public sealed record TestDecl(TextSpan Span, string Title, Param[] Params, BlockStmt Body) : Decl(Span);

// ── Types ────────────────────────────────────────────────────────────────────────

public abstract record TypeSyntax(TextSpan Span) : Node(Span);

/// <summary><c>Name</c> or <c>Name&lt;A, B&gt;</c>, optionally qualified (<c>m.Name</c>).</summary>
public sealed record NamedType(TextSpan Span, Name[] Path, TypeSyntax[] Args) : TypeSyntax(Span);

public sealed record OptionalType(TextSpan Span, TypeSyntax Inner) : TypeSyntax(Span);

/// <summary><c>T[N]</c>.</summary>
public sealed record ArrayType(TextSpan Span, TypeSyntax Element, Expr Size) : TypeSyntax(Span);

public sealed record TupleTypeElement(TextSpan Span, TypeSyntax Type, Name? Name) : Node(Span);

public sealed record TupleType(TextSpan Span, TupleTypeElement[] Elements) : TypeSyntax(Span);

/// <summary><c>dyn ref I</c>, <c>dyn ref readonly I</c>, or <c>dyn I</c> (not supported yet).</summary>
public sealed record DynType(TextSpan Span, bool IsRef, bool IsReadOnly, NamedType Inner) : TypeSyntax(Span);

// ── Statements ───────────────────────────────────────────────────────────────────

public abstract record Stmt(TextSpan Span) : Node(Span);

public sealed record BlockStmt(TextSpan Span, Stmt[] Statements) : Stmt(Span);

public enum BindingKind { Let, Var, Const }

/// <summary><c>let x = e;</c>, <c>var T x = e;</c>, C#-style <c>T x = e;</c> (a <c>var</c>), optionally <c>using</c>.</summary>
public sealed record LocalDecl(TextSpan Span, BindingKind Kind, bool Using, TypeSyntax? Type, Name Name, Expr? Init) : Stmt(Span);

public sealed record ExprStmt(TextSpan Span, Expr Expr) : Stmt(Span);

public enum AssignOp { Assign, Add, Sub, Mul, Div, Rem, And, Or, Xor, Shl, Shr, Coalesce }

public sealed record AssignStmt(TextSpan Span, Expr Target, AssignOp Op, Expr Value) : Stmt(Span);

public sealed record IncDecStmt(TextSpan Span, Expr Target, bool Increment) : Stmt(Span);

public sealed record ReturnStmt(TextSpan Span, Expr? Value) : Stmt(Span);

public sealed record BreakStmt(TextSpan Span, Name? Label) : Stmt(Span);

public sealed record ContinueStmt(TextSpan Span, Name? Label) : Stmt(Span);

/// <summary>The word after <c>if</c>: <c>if unlikely</c> (a layout hint) or <c>if never</c> (panics when taken).</summary>
public enum Likelihood { None, Unlikely, Never }

public sealed record IfStmt(TextSpan Span, Expr Condition, Stmt Then, Stmt? Else) : Stmt(Span)
{
    public Likelihood Likelihood { get; init; }
}

public sealed record UnlessStmt(TextSpan Span, Expr Condition, Stmt Body) : Stmt(Span);

/// <summary>A simple statement with a postfix <c>if</c>/<c>unless</c> (spec §7.3); the condition is evaluated first.</summary>
public sealed record GuardedStmt(TextSpan Span, Stmt Statement, bool Unless, Expr Condition) : Stmt(Span)
{
    /// <summary><c>stmt if unlikely cond;</c></summary>
    public bool Unlikely { get; init; }
}

public sealed record WhileStmt(TextSpan Span, Expr Condition, Stmt Body) : Stmt(Span);

public sealed record DoWhileStmt(TextSpan Span, Stmt Body, Expr Condition) : Stmt(Span);

/// <summary><c>while let x = e { … }</c>: loops while <c>e</c> is non-null, binding <c>x</c> to its value.</summary>
public sealed record WhileLetStmt(TextSpan Span, Name Variable, Expr Value, Stmt Body) : Stmt(Span);

/// <summary><c>for x in e</c>, <c>for (k, v) in e</c>, <c>for try x in e</c>, or the input form <c>for (let x in e)</c>.</summary>
public sealed record ForStmt(TextSpan Span, BindingKind Kind, TypeSyntax? Type, Name Variable, Expr Iterable, Stmt Body) : Stmt(Span)
{
    /// <summary><c>for try</c>: every step may fail.</summary>
    public bool Try { get; init; }

    /// <summary>The names of <c>for (k, v) in e</c>, whose <see cref="Variable"/> is the unnamed element; empty otherwise.</summary>
    public Name[] Names { get; init; } = [];
}

public sealed record LabeledStmt(TextSpan Span, Name Label, Stmt Statement) : Stmt(Span);

/// <summary>One <c>case</c> label: a pattern and optional <c>when</c> guard. A null pattern is <c>default</c>.</summary>
public sealed record SwitchLabel(TextSpan Span, Pattern? Pattern, Expr? Guard) : Node(Span);

public sealed record SwitchSection(TextSpan Span, SwitchLabel Label, Stmt[] Statements) : Node(Span);

public sealed record SwitchStmt(TextSpan Span, Expr Value, SwitchSection[] Sections) : Stmt(Span);

/// <summary>When a <c>defer</c> body runs: on every exit, on exit by failure, or while a panic is in progress.</summary>
public enum DeferKind { Always, Failing, Panicking }

/// <summary>
/// <c>defer stmt;</c>, <c>defer failing [(e)] stmt</c> or <c>defer panicking [(p)] stmt</c>. <see cref="Binding"/> names the error
/// or the panic. <c>errdefer [(e)] stmt</c> parses as <c>defer failing</c>.
/// </summary>
public sealed record DeferStmt(TextSpan Span, DeferKind Kind, Name? Binding, Stmt Body) : Stmt(Span);

/// <summary><c>assert expr;</c>: panics with the value of each sub-expression when <see cref="Condition"/> is false.</summary>
public sealed record AssertStmt(TextSpan Span, Expr Condition) : Stmt(Span);

public sealed record NamedArg(TextSpan Span, Name Name, Expr Value) : Node(Span);

/// <summary><c>context (allocator: a) { … }</c>.</summary>
public sealed record ContextStmt(TextSpan Span, NamedArg[] Values, BlockStmt Body) : Stmt(Span);

public sealed record EmptyStmt(TextSpan Span) : Stmt(Span);

/// <summary>Placeholder after a parse error, so later phases see a complete tree.</summary>
public sealed record ErrorStmt(TextSpan Span) : Stmt(Span);

// ── Expressions ──────────────────────────────────────────────────────────────────

public abstract record Expr(TextSpan Span) : Node(Span);

public enum LiteralKind { Integer, Float, Char, String, RawString, CString, U16String, True, False, Null }

/// <summary>A literal; <see cref="Suffix"/> is the unit/width suffix of a number (<c>dec</c>, <c>u</c>, …).</summary>
public sealed record LiteralExpr(TextSpan Span, LiteralKind Kind, string Text, Name? Suffix) : Expr(Span);

public abstract record InterpolationPart(TextSpan Span) : Node(Span);

/// <summary>Literal text with escapes and doubled braces decoded.</summary>
public sealed record InterpolationText(TextSpan Span, string Text) : InterpolationPart(Span);

public sealed record InterpolationHole(TextSpan Span, Expr Expr) : InterpolationPart(Span);

public sealed record InterpolatedExpr(TextSpan Span, InterpolationPart[] Parts) : Expr(Span);

public sealed record NameExpr(TextSpan Span, Name Name, TypeSyntax[] TypeArgs) : Expr(Span);

public sealed record ThisExpr(TextSpan Span) : Expr(Span);

public sealed record MemberExpr(TextSpan Span, Expr Target, Name Member, bool NullConditional) : Expr(Span)
{
    /// <summary>Explicit type arguments of a generic method call, <c>x.f&lt;T&gt;()</c>.</summary>
    public TypeSyntax[] TypeArgs { get; init; } = [];
}

/// <summary><c>.member</c> resolved from the target type.</summary>
public sealed record TargetMemberExpr(TextSpan Span, Name Member) : Expr(Span);

public enum ArgModifier { None, Ref, Move, Out, In }

public sealed record Argument(TextSpan Span, Name? Name, ArgModifier Modifier, Expr Value) : Node(Span);

public sealed record CallExpr(TextSpan Span, Expr Callee, Argument[] Args) : Expr(Span)
{
    public BecauseClause? Because { get; init; }
}

public sealed record IndexExpr(TextSpan Span, Expr Target, Expr Index) : Expr(Span);

public enum UnaryOp { Plus, Negate, Not, Complement, FromEnd }

public sealed record UnaryExpr(TextSpan Span, UnaryOp Op, Expr Operand) : Expr(Span);

public enum BinaryOp
{
    Add, Sub, Mul, Div, Rem,
    AddWrap, SubWrap, MulWrap,
    AddSat, SubSat, MulSat,
    Shl, Shr,
    Lt, Gt, Le, Ge, Eq, Ne,
    BitAnd, BitOr, BitXor,
    LogicalAnd, LogicalOr,
    Coalesce,
}

public sealed record BinaryExpr(TextSpan Span, BinaryOp Op, Expr Left, Expr Right) : Expr(Span);

/// <summary><c>a..b</c> / <c>a..=b</c>; either end may be omitted (<c>..b</c>, <c>a..</c>).</summary>
public sealed record RangeExpr(TextSpan Span, Expr? Start, Expr? End, bool Inclusive) : Expr(Span);

public sealed record ConditionalExpr(TextSpan Span, Expr Condition, Expr WhenTrue, Expr WhenFalse) : Expr(Span);

public sealed record CastExpr(TextSpan Span, TypeSyntax Type, Expr Operand) : Expr(Span);

public sealed record IsExpr(TextSpan Span, Expr Value, Pattern Pattern) : Expr(Span);

/// <summary><c>x in a..=b</c>: whether a value lies in a range.</summary>
public sealed record InExpr(TextSpan Span, Expr Value, Expr Range) : Expr(Span);

public sealed record SwitchArm(TextSpan Span, Pattern Pattern, Expr? Guard, Expr Value) : Node(Span);

public sealed record SwitchExpr(TextSpan Span, Expr Value, SwitchArm[] Arms) : Expr(Span);

public sealed record TryExpr(TextSpan Span, Expr Operand) : Expr(Span);

/// <summary><c>move x</c>: hands the value of a unique local over, leaving the local unusable.</summary>
public sealed record MoveExpr(TextSpan Span, Expr Operand) : Expr(Span);

/// <summary>
/// <c>e catch h</c>, <c>e catch (x) h</c> (binds the error) or <c>e catch (Set.variant) h</c>
/// (handles one variant; <see cref="Variant"/> set).
/// </summary>
public sealed record CatchExpr(TextSpan Span, Expr Operand, Name? Binding, Expr? Variant, Expr Handler) : Expr(Span);

/// <summary>The subject of <c>catch switch { … }</c>: the caught error; <see cref="Span"/> is the <c>switch</c> keyword.</summary>
public sealed record CaughtErrorExpr(TextSpan Span) : Expr(Span);

/// <summary><c>return e</c>, <c>break</c>, <c>continue</c> used as a <c>never</c> expression after <c>??</c> or <c>catch</c>.</summary>
public sealed record JumpExpr(TextSpan Span, Stmt Jump) : Expr(Span);

/// <summary><c>{ … }</c> as a <c>catch</c> handler: runs the statements; it must not complete unless the operand is <c>void</c>.</summary>
public sealed record BlockExpr(TextSpan Span, BlockStmt Block) : Expr(Span);

public sealed record FieldInit(TextSpan Span, Name Name, Expr Value) : Node(Span);

/// <summary><c>T { f = v }</c> or target-typed <c>{ f = v }</c> (<see cref="Type"/> null).</summary>
public sealed record StructLiteralExpr(TextSpan Span, TypeSyntax? Type, FieldInit[] Fields) : Expr(Span);

/// <summary><c>e with { f = v }</c>.</summary>
public sealed record WithExpr(TextSpan Span, Expr Target, FieldInit[] Fields) : Expr(Span);

public sealed record SpreadExpr(TextSpan Span, Expr Operand) : Expr(Span);

/// <summary><c>key: value</c> inside a collection literal; a literal whose elements are all entries is a map literal.</summary>
public sealed record MapEntryExpr(TextSpan Span, Expr Key, Expr Value) : Expr(Span);

public sealed record CollectionExpr(TextSpan Span, Expr[] Elements) : Expr(Span);

public sealed record TupleElement(TextSpan Span, Name? Name, Expr Value) : Node(Span);

public sealed record TupleExpr(TextSpan Span, TupleElement[] Elements) : Expr(Span);

public sealed record ParenExpr(TextSpan Span, Expr Inner) : Expr(Span);

/// <summary><c>stackalloc T[n]</c>.</summary>
public sealed record StackallocExpr(TextSpan Span, TypeSyntax Element, Expr Count) : Expr(Span);

/// <summary><c>pattern T { … }</c>, a named pattern's value; valid only after <c>=</c> in a <c>const</c> (BYN1046).</summary>
public sealed record PatternExpr(TextSpan Span, Pattern Pattern) : Expr(Span);

/// <summary>Placeholder after a parse error.</summary>
public sealed record ErrorExpr(TextSpan Span) : Expr(Span);

// ── Patterns ─────────────────────────────────────────────────────────────────────

public abstract record Pattern(TextSpan Span) : Node(Span);

/// <summary>A constant: literal, <c>.member</c>, a qualified name or a range.</summary>
public sealed record ConstantPattern(TextSpan Span, Expr Value) : Pattern(Span);

public sealed record RelationalPattern(TextSpan Span, BinaryOp Op, Expr Value) : Pattern(Span);

/// <summary><c>var x</c> or <c>let x</c>; both bind an immutable pattern variable.</summary>
public sealed record VarPattern(TextSpan Span, Name Name, bool IsLet = false) : Pattern(Span);

public sealed record DiscardPattern(TextSpan Span) : Pattern(Span);

/// <summary><c>DbError e</c>: matches a value of <see cref="Type"/> and binds it.</summary>
public sealed record DeclarationPattern(TextSpan Span, TypeSyntax Type, Name Name) : Pattern(Span);

public sealed record NotPattern(TextSpan Span, Pattern Operand) : Pattern(Span);

public sealed record BinaryPattern(TextSpan Span, bool IsAnd, Pattern Left, Pattern Right) : Pattern(Span);

public sealed record PropertySubpattern(TextSpan Span, Name[] Path, Pattern Pattern) : Node(Span);

/// <summary><c>Type { a: p } name</c>; <c>{ } v</c> matches any non-null value.</summary>
public sealed record PropertyPattern(TextSpan Span, TypeSyntax? Type, PropertySubpattern[] Properties, Name? Designation) : Pattern(Span);

/// <summary><c>Name(p, q)</c> or <c>(p, q)</c>.</summary>
public sealed record PositionalPattern(TextSpan Span, TypeSyntax? Type, Pattern[] Elements) : Pattern(Span);

/// <summary><c>.Circle(let r)</c>: a target-typed union variant with payload patterns.</summary>
public sealed record VariantPattern(TextSpan Span, TargetMemberExpr Variant, Pattern[] Elements) : Pattern(Span);

/// <summary><c>..</c> or <c>.. var rest</c> inside a list pattern.</summary>
public sealed record SlicePattern(TextSpan Span, Pattern? Inner) : Pattern(Span);

public sealed record ListPattern(TextSpan Span, Pattern[] Elements) : Pattern(Span);
