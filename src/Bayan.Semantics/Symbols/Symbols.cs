using Bayan.Semantics.Binding;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Symbols;

public enum Accessibility
{
    /// <summary><c>private</c>: the declaring type only.</summary>
    Private,
    /// <summary>No modifier: the declaring module (directory).</summary>
    Module,
    /// <summary><c>public</c>: every importer.</summary>
    Public,
}

/// <summary>Where a declaration was written, for diagnostics that point back at it.</summary>
public readonly record struct Location(SourceText Source, TextSpan Span)
{
    public int Line => Source.GetLinePosition(Span.Start).Line;
}

public abstract class Symbol(string name, Location? location)
{
    public string Name { get; } = name;

    public Location? Location { get; } = location;

    public override string ToString() => Name;
}

/// <summary>A directory of <c>.byn</c> files. Its name is the import path (<c>money</c>, <c>app.orders</c>).</summary>
public sealed class ModuleSymbol(string name, string directory) : Symbol(name, null)
{
    public string Directory { get; } = directory;

    /// <summary>Name used in emitted C identifiers.</summary>
    public string CName { get; set; } = "";

    public List<FileContext> Files { get; } = new();

    public Dictionary<string, TypeSymbol> Types { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<FunctionSymbol>> Functions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ConstSymbol> Constants { get; } = new(StringComparer.Ordinal);

    public List<StructSymbol> Structs { get; } = new();

    public List<ErrorSetSymbol> ErrorSets { get; } = new();

    public List<EnumSymbol> Enums { get; } = new();

    public List<UnionSymbol> Unions { get; } = new();

    public List<FunctionSymbol> AllFunctions { get; } = new();

    public List<TestSymbol> Tests { get; } = new();

    public List<ConstSymbol> AllConstants { get; } = new();

    /// <summary>The implicit <c>main</c> built from top-level statements, or an explicit <c>main</c>.</summary>
    public FunctionSymbol? Main { get; set; }
}

/// <summary>Per-file state: its tree and the imports visible in it.</summary>
public sealed class FileContext(ModuleSymbol module, Syntax.Parsing.SyntaxTree tree)
{
    public ModuleSymbol Module { get; } = module;

    public Syntax.Parsing.SyntaxTree Tree { get; } = tree;

    public SourceText Source => Tree.Source;

    public Dictionary<string, ModuleSymbol> Imports { get; } = new(StringComparer.Ordinal);

    public bool IsTestFile => Source.Path.EndsWith(".test.byn", StringComparison.Ordinal);
}

public sealed class StructSymbol(string name, Location location, ModuleSymbol module, Accessibility access, StructDecl decl, FileContext file)
    : TypeSymbol
{
    public string Name { get; } = name;

    public Location Location { get; } = location;

    public ModuleSymbol Module { get; } = module;

    public Accessibility Access { get; } = access;

    public StructDecl Decl { get; } = decl;

    public FileContext File { get; } = file;

    public string? Doc => Decl.Doc;

    public List<FieldSymbol> Fields { get; } = new();

    public List<PropertySymbol> Properties { get; } = new();

    public List<FunctionSymbol> Methods { get; } = new();

    public List<FunctionSymbol> Constructors { get; } = new();

    /// <summary>The <c>&lt;A, B&gt;</c> of a generic template; empty otherwise.</summary>
    public List<TypeParameterType> TypeParameters { get; } = new();

    /// <summary>For an instantiation: the template it was re-declared from.</summary>
    public StructSymbol? Template { get; init; }

    public IReadOnlyList<TypeSymbol> TypeArguments { get; init; } = [];

    /// <summary>What each type parameter name means here: itself in a template, the argument in an instantiation.</summary>
    public IReadOnlyDictionary<string, TypeSymbol>? TypeMap { get; set; }

    /// <summary>A template, or an instantiation with a type parameter among its arguments: checked, never emitted.</summary>
    public bool IsGeneric => TypeParameters.Count > 0 || TypeArguments.Any(TypeParameterType.Occurs);

    public override string DisplayName =>
        TypeArguments.Count > 0 ? $"{Name}<{string.Join(", ", TypeArguments.Select(a => a.DisplayName))}>" :
        TypeParameters.Count > 0 ? $"{Name}<{string.Join(", ", TypeParameters.Select(a => a.Name))}>" : Name;

    public FieldSymbol? FindField(string name) => Fields.Find(f => f.Name == name);

    public PropertySymbol? FindProperty(string name) => Properties.Find(p => p.Name == name);

    public IEnumerable<FunctionSymbol> FindMethods(string name) => Methods.Where(m => m.Name == name);

    /// <summary>Every field has a default, so <c>T()</c> and <c>{ }</c> are valid.</summary>
    public bool IsDefaultable => Fields.All(f => f.HasDefault && !f.IsRequired);
}

public sealed class FieldSymbol(string name, Location location, StructSymbol owner, Accessibility access, FieldDecl decl)
    : Symbol(name, location)
{
    public StructSymbol Owner { get; } = owner;

    public Accessibility Access { get; } = access;

    public FieldDecl Decl { get; } = decl;

    public TypeSymbol Type { get; set; } = ErrorType.Instance;

    public bool IsRequired => Decl.Modifiers.HasFlag(Modifiers.Required);

    public bool IsReadonly => Decl.Modifiers.HasFlag(Modifiers.Readonly);

    public bool HasDefault => Decl.Default is not null;

    /// <summary>The bound default value, filled in when bodies are bound.</summary>
    public BoundExpr? Default { get; set; }

    public int Index => Owner.Fields.IndexOf(this);
}

public sealed class PropertySymbol(string name, Location location, StructSymbol owner, Accessibility access, PropertyDecl decl)
    : Symbol(name, location)
{
    public StructSymbol Owner { get; } = owner;

    public Accessibility Access { get; } = access;

    public PropertyDecl Decl { get; } = decl;

    public TypeSymbol Type { get; set; } = ErrorType.Instance;

    /// <summary>The getter, compiled as a method taking <c>this</c>.</summary>
    public FunctionSymbol Getter { get; set; } = null!;
}

public enum FunctionKind { Function, Method, Constructor, Getter, Main, Test, Builtin }

public sealed class FunctionSymbol(string name, Location? location, ModuleSymbol module, FunctionKind kind) : Symbol(name, location)
{
    public ModuleSymbol Module { get; } = module;

    public FunctionKind Kind { get; } = kind;

    public Accessibility Access { get; set; } = Accessibility.Module;

    public FileContext? File { get; set; }

    public StructSymbol? Owner { get; set; }

    public bool IsStatic { get; set; }

    public List<ParameterSymbol> Parameters { get; } = new();

    public TypeSymbol ReturnType { get; set; } = PrimitiveType.Void;

    /// <summary>Declared or inferred error sets; empty and <see cref="Fails"/> false for infallible functions.</summary>
    public List<ErrorSetSymbol> ErrorSets { get; } = new();

    /// <summary>The function has a <c>fails</c> clause (possibly inferred).</summary>
    public bool Fails { get; set; }

    /// <summary><c>fails</c> without a list: the set is inferred from the body.</summary>
    public bool InfersErrors { get; set; }

    public FunctionDecl? Decl { get; set; }

    public BuiltinFunction Builtin { get; set; }

    public BoundBlock? Body { get; set; }

    /// <summary>The bound <c>requires</c> clauses, set with <see cref="Body"/>; call sites check constant arguments against them.</summary>
    public BoundExpr[] Requires { get; set; } = [];

    /// <summary>Every local and parameter, for the C emitter's declarations.</summary>
    public List<LocalSymbol> Locals { get; } = new();

    /// <summary>The method writes to <c>this</c>; callers need a mutable receiver.</summary>
    public bool IsMutating { get; set; }

    /// <summary>The <c>&lt;T&gt;</c> of a generic template; empty otherwise.</summary>
    public List<TypeParameterType> TypeParameters { get; } = new();

    /// <summary>For an instantiated function: the template it was re-bound from.</summary>
    public FunctionSymbol? Template { get; init; }

    public IReadOnlyList<TypeSymbol> TypeArguments { get; init; } = [];

    /// <summary>What each type parameter name means in the signature and body (the owner's included).</summary>
    public IReadOnlyDictionary<string, TypeSymbol>? TypeMap { get; set; }

    /// <summary>Has a type parameter left (a template, or a member of one): bound only to check it, never analysed, lowered or emitted.</summary>
    public bool IsGeneric => TypeParameters.Count > 0 || TypeArguments.Any(TypeParameterType.Occurs) || Owner is { IsGeneric: true };

    /// <summary>Re-bound from a template with concrete types (directly, or as a member of an instantiated struct).</summary>
    public bool IsInstance => Template is not null || Owner?.Template is not null;

    public bool HasThis => Owner is not null && !IsStatic && Kind is FunctionKind.Method or FunctionKind.Getter or FunctionKind.Constructor;

    public string DisplaySignature =>
        (Owner is null ? "" : Owner.Name + ".") + Name + "(" + string.Join(", ", Parameters.Select(p => p.Type.DisplayName + " " + p.Name)) + ")";
}

/// <summary>Functions implemented by the compiler and runtime.</summary>
public enum BuiltinFunction
{
    None,
    Print,
    Panic,
    Todo,
    Assert,
    ListNew,
    ArenaNew,
    StrBufNew,
    StrBufGrow,
    OwnedNew,
    UntrustedNew,
    Trust,
    IoReadFile,
    IoReadText,
    IoWriteFile,
    IoListDir,
    IoRun,
    IoRunBatch,
    IoMonotonicNanos,
    IoStdout,
    IoStderr,
    IoStdin,
    StringFrom,
    StringTryFrom,
    StringInvalidUtf8At,
    EnumFromOrdinal,
    EnumParse,
}

public enum LocalKind { Let, Var, Const, Parameter, ForVariable, PatternVariable, Temp }

public class LocalSymbol(string name, Location? location, TypeSymbol type, LocalKind kind) : Symbol(name, location)
{
    public TypeSymbol Type { get; set; } = type;

    public LocalKind Kind { get; } = kind;

    public bool IsMutable => Kind is LocalKind.Var or LocalKind.Temp;

    /// <summary>Declared with <c>using</c>: disposed at scope end.</summary>
    public bool IsUsing { get; set; }

    /// <summary>Unique within the function; used for C names.</summary>
    public int Id { get; set; }

    /// <summary>Compile-time value of a <c>const</c>.</summary>
    public ConstantValue? Constant { get; set; }

    /// <summary>A <c>for</c> variable or pattern binding: of a unique type it borrows from the subject (spec §3.11).</summary>
    public bool BorrowsSubject { get; set; }

    /// <summary>The error bound by <c>catch |e|</c> or <c>errdefer |e|</c>.</summary>
    public bool IsCatch { get; set; }

    /// <summary>The <c>let</c> or <c>var</c> keyword, for the fix that turns one into the other.</summary>
    public Bayan.Syntax.Text.TextSpan? KeywordSpan { get; set; }

    /// <summary>The <c>when</c> guard of the <c>switch move …</c> arm that binds it; inside it the binding is read-only.</summary>
    public Bayan.Syntax.Text.TextSpan? GuardSpan { get; set; }

    /// <summary>Assigned or mutated through after its declaration; a <c>var</c> that never is should be a <c>let</c>.</summary>
    public bool Written { get; set; }
}

public sealed class ParameterSymbol(string name, Location? location, TypeSymbol type, ParamModifier modifier, int ordinal)
    : LocalSymbol(name, location, type, LocalKind.Parameter)
{
    public ParamModifier Modifier { get; } = modifier;

    public int Ordinal { get; } = ordinal;

    public Param? Syntax { get; init; }

    public BoundExpr? Default { get; set; }

    /// <summary><c>ref</c> and <c>out</c> parameters are passed by pointer and may be written.</summary>
    public bool IsByRef => Modifier is ParamModifier.Ref or ParamModifier.Out;
}

/// <summary>A module-level <c>const</c>.</summary>
public sealed class ConstSymbol(string name, Location location, ModuleSymbol module, LocalDecl decl, FileContext file) : Symbol(name, location)
{
    public ModuleSymbol Module { get; } = module;

    public LocalDecl Decl { get; } = decl;

    public FileContext File { get; } = file;

    public TypeSymbol Type { get; set; } = ErrorType.Instance;

    public ConstantValue? Value { get; set; }

    public BoundExpr? Bound { get; set; }

    /// <summary>Binding state, to detect constants defined in terms of themselves.</summary>
    public int State { get; set; }

    /// <summary>A <c>const T[N]</c> of literal scalars, emitted once as static data instead of inlined.</summary>
    public bool IsStaticArray { get; set; }

    /// <summary>The element values of a static array.</summary>
    public ConstantValue[] Elements { get; set; } = [];
}

/// <summary>An <c>error enum</c>: a closed set of failure variants.</summary>
public sealed class ErrorSetSymbol(string name, Location location, ModuleSymbol module, Accessibility access, ErrorEnumDecl decl)
    : TypeSymbol
{
    public string Name { get; } = name;

    public Location Location { get; } = location;

    public ModuleSymbol Module { get; } = module;

    public Accessibility Access { get; } = access;

    public ErrorEnumDecl Decl { get; } = decl;

    public List<ErrorVariantSymbol> Variants { get; } = new();

    public override string DisplayName => Name;

    public ErrorVariantSymbol? Find(string name) => Variants.Find(v => v.Name == name);
}

public sealed class ErrorVariantSymbol(string name, Location location, ErrorSetSymbol set, uint code, string? doc) : Symbol(name, location)
{
    public ErrorSetSymbol Set { get; } = set;

    /// <summary>Program-wide unique number (spec §3.8); 0 means "no error".</summary>
    public uint Code { get; } = code;

    public string? Doc { get; } = doc;

    /// <summary>The process exit code when <c>main</c> fails with this variant: <c>[ExitCode(n)]</c>, else 1.</summary>
    public int ExitCode { get; set; } = 1;

    /// <summary>First line of the doc comment, or the variant name (spec §6.6).</summary>
    public string Summary => Doc is { Length: > 0 } d ? d.Split('\n')[0].Trim() : Name;
}

public sealed class TestSymbol(string title, Location location, FunctionSymbol function) : Symbol(title, location)
{
    public FunctionSymbol Function { get; } = function;
}

/// <summary>A plain <c>enum</c>: named <c>i32</c> ordinals in declaration order.</summary>
public sealed class EnumSymbol(string name, Location location, ModuleSymbol module, Accessibility access, EnumDecl decl, FileContext file)
    : TypeSymbol
{
    public string Name { get; } = name;

    public Location Location { get; } = location;

    public ModuleSymbol Module { get; } = module;

    public Accessibility Access { get; } = access;

    public EnumDecl Decl { get; } = decl;

    public FileContext File { get; } = file;

    public List<EnumMemberSymbol> Members { get; } = new();

    /// <summary>The associated data of <c>enum Name(T x, …)</c>, in declaration order; empty for a plain enum.</summary>
    public List<EnumFieldSymbol> Fields { get; } = new();

    /// <summary><c>Name.fromOrdinal(i64)</c> and <c>Name.parse(string)</c>.</summary>
    public FunctionSymbol FromOrdinal { get; set; } = null!;

    public FunctionSymbol Parse { get; set; } = null!;

    public override string DisplayName => Name;

    public EnumMemberSymbol? Find(string name) => Members.Find(m => m.Name == name);

    public EnumFieldSymbol? FindField(string name) => Fields.Find(f => f.Name == name);
}

/// <summary>A parameter of a data enum, read as <c>e.name</c>; one static table per field, indexed by ordinal.</summary>
public sealed class EnumFieldSymbol(string name, Location location, EnumSymbol @enum, TypeSymbol type, int index) : Symbol(name, location)
{
    public EnumSymbol Enum { get; } = @enum;

    public TypeSymbol Type { get; } = type;

    public int Index { get; } = index;
}

public sealed class EnumMemberSymbol(string name, Location location, EnumSymbol @enum, int ordinal, string? doc) : Symbol(name, location)
{
    public EnumSymbol Enum { get; } = @enum;

    public int Ordinal { get; } = ordinal;

    public string? Doc { get; } = doc;

    /// <summary>The constant value of each field of a data enum, by field index (null after an error).</summary>
    public ConstantValue?[] Values { get; set; } = [];
}

/// <summary>A tagged <c>union</c>: variants in declaration order, each with positional payload fields.</summary>
public sealed class UnionSymbol(string name, Location location, ModuleSymbol module, Accessibility access, UnionDecl decl, FileContext file)
    : TypeSymbol
{
    public string Name { get; } = name;

    public Location Location { get; } = location;

    public ModuleSymbol Module { get; } = module;

    public Accessibility Access { get; } = access;

    public UnionDecl Decl { get; } = decl;

    public FileContext File { get; } = file;

    public List<UnionVariantSymbol> Variants { get; } = new();

    /// <summary>The <c>&lt;A, B&gt;</c> of a generic template; empty otherwise.</summary>
    public List<TypeParameterType> TypeParameters { get; } = new();

    /// <summary>For an instantiation: the template it was re-declared from.</summary>
    public UnionSymbol? Template { get; init; }

    public IReadOnlyList<TypeSymbol> TypeArguments { get; init; } = [];

    /// <summary>What each type parameter name means here: itself in a template, the argument in an instantiation.</summary>
    public IReadOnlyDictionary<string, TypeSymbol>? TypeMap { get; set; }

    /// <summary>A template, or an instantiation with a type parameter among its arguments: checked, never emitted.</summary>
    public bool IsGeneric => TypeParameters.Count > 0 || TypeArguments.Any(TypeParameterType.Occurs);

    public override string DisplayName =>
        TypeArguments.Count > 0 ? $"{Name}<{string.Join(", ", TypeArguments.Select(a => a.DisplayName))}>" :
        TypeParameters.Count > 0 ? $"{Name}<{string.Join(", ", TypeParameters.Select(a => a.Name))}>" : Name;

    public UnionVariantSymbol? Find(string name) => Variants.Find(v => v.Name == name);
}

/// <summary>A union variant; its tag is its declared value, else one more than the previous tag (0 first). Unit variants have no fields.</summary>
public sealed class UnionVariantSymbol(string name, Location location, UnionSymbol union, int tag, string? doc) : Symbol(name, location)
{
    public UnionSymbol Union { get; } = union;

    public int Tag { get; } = tag;

    public string? Doc { get; } = doc;

    public List<ParameterSymbol> Fields { get; } = new();
}
