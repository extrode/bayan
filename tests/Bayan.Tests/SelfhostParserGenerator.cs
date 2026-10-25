using System.Collections;
using System.Reflection;
using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Deterministic program generator for the self-hosted parser (spec 009 R7, R8 input). A valid corpus is
/// assembled from one template per <c>Nodes.cs</c> record type plus, for each seed, a program whose blocks and
/// binary expressions nest up to <see cref="NestingCap"/>. The corrupted corpus drops, duplicates or swaps a
/// token of a valid program using the C# lexer's spans. Four node kinds are only reachable through a recovery
/// or input-form diagnostic — <c>CastExpr</c> (<c>(i64)x</c>), <c>EmptyStmt</c> (<c>;</c>), <c>ErrorStmt</c>
/// (a stray <c>else</c>) and <c>ErrorExpr</c> (<c>()</c>) — and their templates still parse to a complete tree.
/// </summary>
public static class SelfhostParserGenerator
{
    /// <summary>Maximum nesting depth of the blocks and expressions in an assembled program.</summary>
    public const int NestingCap = 8;

    /// <summary>The fixed seeds every run of the corpora is built from (R7).</summary>
    public static readonly ulong[] Seeds =
    [
        0x9E3779B97F4A7C15, 0x0123456789ABCDEF, 0xFEDCBA9876543210, 0xDEADBEEFCAFEF00D,
        0x3141592653589793, 0x2718281828459045, 0x0BADF00D0BADF00D, 0x5EED5EED5EED5EED,
    ];

    /// <summary>Every concrete <c>Node</c> record type in <c>Bayan.Syntax.Ast</c>, by name.</summary>
    public static IReadOnlyList<Type> RecordTypes { get; } = typeof(Node).Assembly.GetTypes()
        .Where(t => t.Namespace == "Bayan.Syntax.Ast" && typeof(Node).IsAssignableFrom(t) && !t.IsAbstract)
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The one valid program per record type; the corpus instantiates every node kind through it.</summary>
    public static string TemplateFor(Type type) => type.Name switch
    {
        // Names, compilation unit and declaration support.
        nameof(Name) => "let name = 1\n",
        nameof(CompilationUnit) => "",
        nameof(AttributeSyntax) => "@experimental\nvoid run() { }\n",
        nameof(BecauseClause) => "@allow(unusedValue) because \"x\"\nvoid warm() { }\n",
        nameof(RequiresRule) => "struct SignupForm {\n    string username\n    string password\n    requires password != username at .password\n}\n",
        nameof(ImportDecl) => "import money\n",
        nameof(GlobalStatement) => "print(\"hi\")\n",
        nameof(Param) => "void f(i64 a) { }\n",
        nameof(FailsClause) => "void f() fails IoError { }\n",
        nameof(UsesClause) => "void f() uses io { }\n",
        nameof(FunctionDecl) => "void f() { }\n",
        nameof(WhereClause) => "T max<T>(T a, T b) where T : IOrder<T> => a\n",
        nameof(StructDecl) => "struct S { }\n",
        nameof(InterfaceDecl) => "interface I { }\n",
        nameof(GetterDecl) => "interface I { string name }\n",
        nameof(AssociatedTypeDecl) => "interface I { type Item }\n",
        nameof(ExtensionDecl) => "extension Cents : IShape\n",
        nameof(FieldDecl) => "struct S { i64 x }\n",
        nameof(PropertyDecl) => "struct S {\n    i64 total\n    i64 doubled => total * 2\n}\n",
        nameof(ConstructorDecl) => "struct Money {\n    i64 cents\n    Money(i64 cents) { this.cents = cents }\n}\n",
        nameof(ErrorVariant) => "union error CarrierError { timeout(Millis after) }\n",
        nameof(ErrorEnumDecl) => "union error CarrierError { timeout(Millis after) }\n",
        nameof(ErrorSetDecl) => "union error SignupError = EmailError, DbError\n",
        nameof(EnumMember) => "enum Color { red }\n",
        nameof(EnumDecl) => "enum Color { red }\n",
        nameof(UnionField) => "union Shape { circle(f32 r) }\n",
        nameof(UnionVariant) => "union Shape { circle(f32 r) }\n",
        nameof(UnionDecl) => "union Shape { circle(f32 r) }\n",
        nameof(TypeAliasDecl) => "type Cents = i64\n",
        nameof(EffectDecl) => "effect db = net, fs\n",
        nameof(TestDecl) => "test \"plain\" { }\n",

        // Types.
        nameof(NamedType) => "type Cents = i64\n",
        nameof(OptionalType) => "struct S {\n    i64? note\n}\n",
        nameof(ArrayType) => "struct S { i64[4] history }\n",
        nameof(TupleTypeElement) => "void f((i64, string) pair) { }\n",
        nameof(TupleType) => "void f((i64, string) pair) { }\n",
        nameof(FunctionTypeSyntax) => "type Pred = i32 -> bool\n",
        nameof(DynType) => "void show(dyn ref IShape s) { }\n",
        nameof(RefinedType) => "struct Order {\n    i64 id\n    states { paid }\n}\ntype Done = Order is .paid\n",
        nameof(PointerType) => "extern void f(i64* p) { }\n",

        // Statements.
        nameof(BlockStmt) => "void f() { }\n",
        nameof(LocalDecl) => "let x = 1\n",
        nameof(ExprStmt) => "print(\"hi\")\n",
        nameof(AssignStmt) => "void f() {\n    x = 1\n}\n",
        nameof(IncDecStmt) => "void f() {\n    x++\n}\n",
        nameof(ReturnStmt) => "i64 f() { return 0 }\n",
        nameof(FailStmt) => "union error E { bad }\nvoid f() fails E { fail .bad }\n",
        nameof(BreakStmt) => "void f() {\n    while true { break }\n}\n",
        nameof(ContinueStmt) => "void f() {\n    for x in xs { continue }\n}\n",
        nameof(BecomeStmt) => "void f() {\n    become .done\n}\n",
        nameof(IfStmt) => "void f(bool a) {\n    if a { print(\"x\") }\n}\n",
        nameof(UnlessStmt) => "void f(bool ok) {\n    unless ok { print(\"x\") }\n}\n",
        nameof(GuardedStmt) => "void f(bool b) {\n    print(\"x\") if b\n}\n",
        nameof(WhileStmt) => "void f(bool b) {\n    while b { break }\n}\n",
        nameof(DoWhileStmt) => "void f() {\n    var x = 0\n    do { x++ } while x < 10\n}\n",
        nameof(WhileLetStmt) => "void f() {\n    while let x = next() { use(x) }\n}\n",
        nameof(ForStmt) => "void f() {\n    for x in xs { use(x) }\n}\n",
        nameof(LabeledStmt) => "void f() {\n    outer: for row in grid { break outer }\n}\n",
        nameof(SwitchLabel) => "void f(i64 n) {\n    switch n {\n        case 1: print(\"x\")\n    }\n}\n",
        nameof(SwitchSection) => "void f(i64 n) {\n    switch n {\n        case 1: print(\"x\")\n    }\n}\n",
        nameof(SwitchStmt) => "void f(i64 n) {\n    switch n {\n        case 1: print(\"x\")\n        default: print(\"y\")\n    }\n}\n",
        nameof(DeferStmt) => "void f() {\n    defer close()\n}\n",
        nameof(AssertStmt) => "void f(bool a) {\n    assert a\n}\n",
        nameof(NamedArg) => "void f(Arena a) {\n    provide allocator: a {\n        print(\"x\")\n    }\n}\n",
        nameof(ContextStmt) => "void f(Arena a) {\n    provide allocator: a {\n        print(\"x\")\n    }\n}\n",
        nameof(EmptyStmt) => "void f() {\n    ;\n}\n",
        nameof(ErrorStmt) => "void f() {\n    else\n}\n",

        // Expressions.
        nameof(LiteralExpr) => "let x = 1\n",
        nameof(InterpolationText) => "let s = $\"a {x}\"\n",
        nameof(InterpolationHole) => "let s = $\"a {x}\"\n",
        nameof(InterpolatedExpr) => "let s = $\"a {x}\"\n",
        nameof(NameExpr) => "let y = x\n",
        nameof(ThisExpr) => "void f() {\n    let me = this\n}\n",
        nameof(MemberExpr) => "let y = a.b\n",
        nameof(TargetMemberExpr) => "void f() {\n    return .notFound\n}\n",
        nameof(Argument) => "let y = f(x)\n",
        nameof(CallExpr) => "let y = f(x)\n",
        nameof(IndexExpr) => "let y = xs[0]\n",
        nameof(UnaryExpr) => "let y = -x\n",
        nameof(BinaryExpr) => "let y = a + b\n",
        nameof(RangeExpr) => "let r = 0..n\n",
        nameof(ConditionalExpr) => "let y = a ? b : c\n",
        nameof(CastExpr) => "let y = (i64)x\n",
        nameof(IsExpr) => "let b = x is not null\n",
        nameof(ExceptExpr) => "let rest = code except .a\n",
        nameof(InExpr) => "let b = x in 0..=10\n",
        nameof(SwitchArm) => "let s = n switch { 0 => \"none\", _ => \"many\" }\n",
        nameof(SwitchExpr) => "let s = n switch { 0 => \"none\", _ => \"many\" }\n",
        nameof(TryExpr) => "let y = try parse(s)\n",
        nameof(MoveExpr) => "let y = move x\n",
        nameof(CatchExpr) => "let y = parse(s) catch 0\n",
        nameof(CaughtErrorExpr) => "let y = parse(s) catch switch { .bad => 1 }\n",
        nameof(JumpExpr) => "let y = db.find(id) ?? return\n",
        nameof(BlockExpr) => "let n = parse(s) catch { return -1 }\n",
        nameof(FieldInit) => "let p = Money { cents: 250 }\n",
        nameof(StructLiteralExpr) => "let p = Money { cents: 250 }\n",
        nameof(WithExpr) => "let more = item with { name: \"coffee\" }\n",
        nameof(SpreadExpr) => "let spread = [..a, ..b]\n",
        nameof(MapEntryExpr) => "let stock = [\"A\": 12]\n",
        nameof(CollectionExpr) => "let fixed = [1, 2, 3]\n",
        nameof(TupleElement) => "let t = (x: 1, y: 2)\n",
        nameof(TupleExpr) => "let t = (1, \"a\")\n",
        nameof(ParenExpr) => "let y = (a)\n",
        nameof(LambdaExpr) => "let inc = x => x + 1\n",
        nameof(StackallocExpr) => "let buf = stackalloc u8[64]\n",
        nameof(PatternExpr) => "const p = pattern Order { total: 0 }\n",
        nameof(ErrorExpr) => "let x = ()\n",

        // Patterns.
        nameof(ConstantPattern) => "let b = x is 1\n",
        nameof(RelationalPattern) => "let b = x is > 0\n",
        nameof(VarPattern) => "let b = x is var y\n",
        nameof(DiscardPattern) => "let b = x is _\n",
        nameof(DeclarationPattern) => "let b = x is DbError e\n",
        nameof(NotPattern) => "let b = x is not null\n",
        nameof(BinaryPattern) => "let b = x is > 0 and <= 10\n",
        nameof(PropertySubpattern) => "let b = x is { cents: > 0 }\n",
        nameof(PropertyPattern) => "let b = x is { cents: > 0 }\n",
        nameof(PositionalPattern) => "let b = x is (0, var y)\n",
        nameof(VariantPattern) => "let a = s switch {\n    .circle(let r) => r * r,\n    .none => 0\n}\n",
        nameof(SlicePattern) => "let b = xs is [var first, .. var rest]\n",
        nameof(ListPattern) => "let b = xs is [1, .., 9]\n",
        _ => throw new InvalidOperationException($"no template for {type.Name}"),
    };

    /// <summary>Every node record type present in <paramref name="tree"/>, found by walking public properties.</summary>
    public static HashSet<Type> InstantiatedTypes(SyntaxTree tree)
    {
        var set = new HashSet<Type>();
        Walk(tree.Root, set);
        return set;
    }

    /// <summary>The valid corpus: one program per record type, then one assembled program per seed.</summary>
    public static IReadOnlyList<(string Name, string Source)> GenerateValid(IReadOnlyList<ulong> seeds)
    {
        var programs = new List<(string, string)>(RecordTypes.Count + seeds.Count);
        foreach (var type in RecordTypes) programs.Add(($"valid-{type.Name}.byn", TemplateFor(type)));
        for (var i = 0; i < seeds.Count; i++) programs.Add(($"valid-seed-{i:D3}.byn", Assemble(new XorShift(seeds[i]))));
        return programs;
    }

    /// <summary>The corrupted corpus: one corrupted version of every valid program, so every recovery point is exercised.</summary>
    public static IReadOnlyList<(string Name, string Source)> GenerateCorrupt(IReadOnlyList<ulong> seeds)
    {
        var valid = GenerateValid(seeds);
        var programs = new List<(string, string)>(valid.Count);
        var rng = new XorShift(seeds[0]);
        foreach (var (name, source) in valid)
            programs.Add(($"corrupt-{name}", Corrupt(source, rng)));
        return programs;
    }

    /// <summary>About 1 MB of ordinary, valid Bayan code: a repeated declaration unit padded with spaces (R8).</summary>
    public static SourceText OneMegabyte()
    {
        var unit = """
            /// A ledger line.
            struct Line {
                i64 price = 0
                string name
            }
            i64 total(List<Line> lines) {
                var sum = 0
                for line in lines { sum += line.price * 2 + 0xFF }
                print($"total {sum} for {lines.length} lines")
                return sum
            }

            """u8.ToArray();
        var bytes = new byte[1024 * 1024];
        for (var i = 0; i + unit.Length <= bytes.Length; i += unit.Length) unit.CopyTo(bytes, i);
        for (var i = bytes.Length - bytes.Length % unit.Length; i < bytes.Length; i++) bytes[i] = (byte)' ';
        return new SourceText("big.byn", bytes);
    }

    /// <summary>Declaration templates combined at the top level of an assembled program.</summary>
    private static readonly Type[] AssemblyDeclarations =
    [
        typeof(ImportDecl), typeof(StructDecl), typeof(FunctionDecl), typeof(InterfaceDecl),
        typeof(ExtensionDecl), typeof(FieldDecl), typeof(PropertyDecl), typeof(ConstructorDecl),
        typeof(ErrorEnumDecl), typeof(ErrorSetDecl), typeof(EnumDecl), typeof(UnionDecl),
        typeof(TypeAliasDecl), typeof(EffectDecl), typeof(TestDecl),
    ];

    /// <summary>Statements nested inside the assembled <c>main</c>, chosen by the RNG.</summary>
    private static readonly string[] AssemblyStatements =
    [
        "let a = 1",
        "var b = a + 2",
        "a = b",
        "a++",
        "if a > 0 { b = 1 }",
        "unless b > 0 { a = 1 }",
        "while a < 10 { a++ }",
        "for x in xs { use(x) }",
        "switch a { case 1: b = 1 }",
        "defer close()",
        "assert a > 0",
        "print(\"x\")",
        "return",
    ];

    /// <summary>Expressions assigned to a local in the innermost block, chosen by the RNG.</summary>
    private static readonly string[] AssemblyExpressions =
    [
        "a + b", "a * b + c", "a ? b : c", "a && b", "f(a, b)", "xs[0]",
        "a == b", "a..b", "-a", "!a", "this", "a.b", "move a", "try f(a)",
    ];

    /// <summary>A valid program that combines declaration templates and nests statements up to <see cref="NestingCap"/>.</summary>
    private static string Assemble(XorShift rng)
    {
        var sb = new StringBuilder();
        foreach (var decl in AssemblyDeclarations)
            if (rng.Below(2) == 0)
                sb.Append(TemplateFor(decl));
        var depth = 1 + rng.Below(NestingCap);
        sb.Append("void main() {\n");
        sb.Append("    var total = 0\n");
        for (var d = 0; d < depth; d++)
        {
            sb.Append(new string(' ', 4 * (d + 1))).Append("if total >= 0 {\n");
            sb.Append(new string(' ', 4 * (d + 2))).Append(rng.Pick(AssemblyStatements)).Append('\n');
        }
        sb.Append(new string(' ', 4 * (depth + 1))).Append("total = ").Append(rng.Pick(AssemblyExpressions)).Append('\n');
        for (var d = depth - 1; d >= 0; d--)
            sb.Append(new string(' ', 4 * (d + 1))).Append("}\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Drops, duplicates or swaps one token of <paramref name="source"/> using the C# lexer's spans.</summary>
    public static string Corrupt(string source, ulong seed) => Corrupt(source, new XorShift(seed));

    private static string Corrupt(string source, XorShift rng)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var tokens = Lexer.Lex(new SourceText("corrupt.byn", bytes)).Tokens.ToArray()
            .Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        if (tokens.Length == 0) return source;
        return rng.Below(3) switch
        {
            0 => Slice(bytes, rng.Below(tokens.Length), tokens, 0, drop: true),
            1 => Slice(bytes, rng.Below(tokens.Length), tokens, 0, drop: false),
            _ => Slice(bytes, rng.Below(tokens.Length), tokens, rng.Below(tokens.Length), drop: false),
        };
    }

    /// <summary>Reassembles the source with the token at <paramref name="first"/> dropped, duplicated or swapped with <paramref name="second"/>.</summary>
    private static string Slice(byte[] bytes, int first, Token[] tokens, int second, bool drop)
    {
        using var ms = new MemoryStream(bytes.Length + 8);
        if (drop)
        {
            var t = tokens[first];
            ms.Write(bytes.AsSpan(0, t.Start));
            ms.Write(bytes.AsSpan(t.End));
        }
        else if (first == second)
        {
            var t = tokens[first];
            ms.Write(bytes.AsSpan(0, t.End));
            ms.Write(bytes.AsSpan(t.Start, t.Length));
            ms.Write(bytes.AsSpan(t.End));
        }
        else
        {
            var a = tokens[first];
            var b = tokens[second];
            if (a.Start > b.Start) (a, b) = (b, a);
            ms.Write(bytes.AsSpan(0, a.Start));
            ms.Write(bytes.AsSpan(b.Start, b.Length));
            ms.Write(bytes.AsSpan(a.End, b.Start - a.End));
            ms.Write(bytes.AsSpan(a.Start, a.Length));
            ms.Write(bytes.AsSpan(b.End));
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Walk(Node node, HashSet<Type> set)
    {
        set.Add(node.GetType());
        foreach (var prop in node.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
            Collect(prop.GetValue(node), set);
        }
    }

    private static void Collect(object? value, HashSet<Type> set)
    {
        if (value is null) return;
        if (value is Node node) { Walk(node, set); return; }
        if (value is IEnumerable sequence and not string)
            foreach (var item in sequence) Collect(item, set);
    }

    /// <summary>xorshift64 with a fixed seed, so every run writes the same corpus.</summary>
    private sealed class XorShift(ulong state)
    {
        public ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public int Below(int n) => (int)(Next() % (ulong)n);

        public T Pick<T>(T[] items) => items[Below(items.Length)];
    }
}
