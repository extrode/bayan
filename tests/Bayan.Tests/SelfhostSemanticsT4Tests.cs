using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using TupleType = Bayan.Semantics.Symbols.TupleType;

namespace Bayan.Tests;

/// <summary>
/// T4: the self-hosted bound tree (<c>selfhost/semantics/bound.byn</c>) against the C# records of
/// <c>BoundNodes.cs</c> and <c>BoundTree</c>. One generated <c>main.byn</c> builds the same nodes the C#
/// oracle builds and prints labelled sections; each test compares one.
/// </summary>
public sealed class SelfhostSemanticsT4Tests
{
    /// <summary>Q535: kind names that are Bayan keywords.</summary>
    private static readonly Dictionary<string, string> KindRenames = new()
    {
        ["default"] = "defaultValue", ["this"] = "thisRef", ["with"] = "withExpr", ["is"] = "isExpr", ["move"] = "moveExpr",
        ["try"] = "tryExpr", ["catch"] = "catchExpr", ["return"] = "returnStmt", ["fail"] = "failStmt", ["become"] = "becomeStmt",
        ["break"] = "breakStmt", ["continue"] = "continueStmt", ["if"] = "ifStmt", ["while"] = "whileStmt", ["for"] = "forStmt",
        ["switch"] = "switchStmt", ["defer"] = "deferStmt", ["assert"] = "assertStmt", ["errdefer"] = "errdeferStmt",
        ["context"] = "contextStmt",
    };

    /// <summary>Q535/Q537: field renames (C# keyword names) and fields added on the Bayan side, by record.</summary>
    private static readonly Dictionary<string, (Dictionary<string, string> Renames, string[] Added)> FieldChanges = new()
    {
        ["BoundStructLiteral"] = (new() { ["struct"] = "structId" }, ["type"]),
        ["BoundEnumValues"] = (new() { ["enum"] = "enumId" }, []),
        ["BoundStaticArray"] = (new() { ["const"] = "constId" }, ["type"]),
        ["BoundIf"] = (new() { ["else"] = "elseBranch", ["never"] = "isNever" }, ["guarded"]),
        ["BoundMemberStep"] = (new() { ["property"] = "propertyId" }, []),
        ["BoundLocal"] = (new(), ["type"]),
        ["BoundField"] = (new(), ["type"]),
        ["BoundWith"] = (new(), ["type"]),
        ["BoundMove"] = (new(), ["type"]),
        ["BoundErrorVariant"] = (new(), ["type"]),
        ["BoundEnumMember"] = (new(), ["type"]),
        ["BoundUnionNew"] = (new(), ["type"]),
        ["BoundUnionField"] = (new(), ["type"]),
        ["BoundErrorField"] = (new(), ["type"]),
        ["UserIteration"] = (new(), ["advanceField", "itemField"]),
        ["StructRule"] = (new(), ["reads"]),
    };

    private static readonly string[] Groups = ["BoundMethodGroup", "BoundVariantGroup", "BoundBuiltinGroup"];

    private static string BindingDir => Path.Combine(Golden.RepoRoot, "src", "Bayan.Semantics", "Binding");

    private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static string KindName(System.Type t)
    {
        var n = Lower(t.Name["Bound".Length..]);
        return KindRenames.GetValueOrDefault(n, n);
    }

    private static System.Type BindingType(string name) =>
        typeof(BoundNode).Assembly.GetType("Bayan.Semantics.Binding." + name) ?? throw new InvalidOperationException(name);

    /// <summary>Every concrete node record in file order: <c>BoundNodes.cs</c>, then the three groups.</summary>
    private static List<System.Type> NodeTypes()
    {
        var names = Regex.Matches(File.ReadAllText(Path.Combine(BindingDir, "BoundNodes.cs")), @"sealed record (Bound\w+)\(")
            .Select(m => m.Groups[1].Value).Concat(Groups);
        return names.Select(BindingType).Where(t => typeof(BoundNode).IsAssignableFrom(t) && !t.IsAbstract).ToList();
    }

    private static T Uninit<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // ── The C# oracle's symbols and leaves (mirrored by the generated main's `leaf…` helpers) ──

    private static readonly PrimitiveType I64 = PrimitiveType.I64;
    private static readonly LocalSymbol La = new("a", null, PrimitiveType.I64, LocalKind.Let);
    private static readonly LocalSymbol Lb = new("b", null, PrimitiveType.I64, LocalKind.Let);
    private static readonly FunctionSymbol Fn = new("f", null, new ModuleSymbol("m", ""), FunctionKind.Function);
    private static readonly JumpTarget Jt = new(null, true);

    private static readonly FieldSymbol Fs = MakeField();
    private static readonly UnionVariantSymbol Uv = MakeUnionVariant();
    private static readonly ErrorVariantSymbol Ev = MakeErrorVariant();
    private static readonly ConstSymbol Cs = MakeConst();
    private static readonly EnumMemberSymbol Em = new("m", default, Uninit<EnumSymbol>(), 0, null);

    private static FieldSymbol MakeField()
    {
        var f = Uninit<FieldSymbol>();
        f.Type = PrimitiveType.I32;
        return f;
    }

    private static ConstSymbol MakeConst()
    {
        var k = Uninit<ConstSymbol>();
        k.Type = PrimitiveType.U8;
        return k;
    }

    private static UnionVariantSymbol MakeUnionVariant()
    {
        var v = new UnionVariantSymbol("v", default, Uninit<UnionSymbol>(), 0, null);
        v.Fields.Add(new ParameterSymbol("p", null, PrimitiveType.I16, ParamModifier.None, 0));
        return v;
    }

    private static ErrorVariantSymbol MakeErrorVariant()
    {
        var v = new ErrorVariantSymbol("e", default, Uninit<ErrorSetSymbol>(), 0, null);
        v.Fields.Add(new ParameterSymbol("p", null, PrimitiveType.I16, ParamModifier.None, 0));
        return v;
    }

    private static BoundExpr E(int i) => i switch
    {
        0 => new BoundLiteral(null!, I64, new ConstantValue((Int128)0)),
        1 => new BoundDefault(null!, I64),
        2 => new BoundThis(null!, I64),
        3 => new BoundErrorExpr(null!),
        4 => new BoundTypeRef(null!, I64),
        _ => new BoundSymbolLiteral(null!, "s"),
    };

    private static BoundStmt S(int i) => i switch
    {
        0 => new BoundErrorStmt(null!),
        1 => new BoundBreak(null!, Jt),
        2 => new BoundContinue(null!, Jt),
        _ => new BoundSetOnPanic(null!, Fn),
    };

    private static BoundPattern P(int i) => i switch
    {
        0 => new BoundBindPattern(null!, I64, null),
        1 => new BoundUnionPattern(null!, I64, Uv, []),
        2 => new BoundErrorPattern(null!, I64, Ev, []),
        _ => new BoundPropertyPattern(null!, I64, I64, [], null),
    };

    private static BoundBlock Blk() => new(null!, [S(0)]);

    private static BoundLocal Loc() => new(null!, La);

    private static BoundExpr Group(string name, params object?[] args) =>
        (BoundExpr)Activator.CreateInstance(BindingType(name), args)!;

    /// <summary>
    /// One node of every kind (twice where a child is optional): the C# node, and the Bayan expression
    /// building it. <c>$E0</c>…<c>$E5</c>, <c>$S0</c>…<c>$S3</c>, <c>$P0</c>…<c>$P3</c>, <c>$BLK</c> and <c>$LOC</c>
    /// are the leaves above; <c>$SP</c> is the case's span.
    /// </summary>
    private static readonly (string Label, Func<BoundNode> CSharp, string Bayan)[] Cases =
    [
        ("literal", () => new BoundLiteral(null!, PrimitiveType.I8, new ConstantValue((Int128)1)),
            "addLiteral(ref c, from: BLiteral { span: $SP, type: prim(.i8), value: Constant { kind: .integer } })"),
        ("symbolLiteral", () => new BoundSymbolLiteral(null!, "s"), "addSymbolLiteral(ref c, from: BSymbolLiteral { span: $SP, text: \"s\" })"),
        ("default", () => new BoundDefault(null!, PrimitiveType.U16), "addDefault(ref c, from: BDefault { span: $SP, type: prim(.u16) })"),
        ("local", () => new BoundLocal(null!, La), "addLocal(ref c, from: BLocal { span: $SP, local: 0 })"),
        ("this", () => new BoundThis(null!, PrimitiveType.U32), "addThis(ref c, from: BThis { span: $SP, type: prim(.u32) })"),
        ("field", () => new BoundField(null!, E(0), Fs), "addField(ref c, from: BField { span: $SP, receiver: $E0, field: 0 })"),
        ("tupleElement", () => new BoundTupleElement(null!, E(1), 0, PrimitiveType.F32),
            "addTupleElement(ref c, from: BTupleElement { span: $SP, receiver: $E1, index: 0, type: prim(.f32) })"),
        ("call", () => new BoundCall(null!, Fn, E(0), [E(1), E(2)], I64),
            "addCall(ref c, from: BCall { span: $SP, function: 0, receiver: $E0, arguments: [$E1, $E2], type: prim(.i64) })"),
        ("call-", () => new BoundCall(null!, Fn, null, [E(1)], I64),
            "addCall(ref c, from: BCall { span: $SP, function: 0, arguments: [$E1], type: prim(.i64) })"),
        ("builtinCall", () => new BoundBuiltinCall(null!, BuiltinMember.ListAdd, E(0), [E(1), E(2)], PrimitiveType.Void),
            "addBuiltinCall(ref c, from: BBuiltinCall { span: $SP, member: .listAdd, receiver: $E0, arguments: [$E1, $E2], type: prim(.void) })"),
        ("unary", () => new BoundUnary(null!, UnaryOp.Negate, E(3), I64),
            "addUnary(ref c, from: BUnary { span: $SP, op: .negate, operand: $E3, type: prim(.i64) })"),
        ("binary", () => new BoundBinary(null!, BinaryOp.Add, E(0), E(1), I64),
            "addBinary(ref c, from: BBinary { span: $SP, op: .add, left: $E0, right: $E1, type: prim(.i64) })"),
        ("conversion", () => new BoundConversion(null!, E(4), PrimitiveType.F64, ConversionKind.Widen),
            "addConversion(ref c, from: BConversion { span: $SP, operand: $E4, type: prim(.f64), kind: .widen })"),
        ("tryFrom", () => new BoundTryFrom(null!, E(0), PrimitiveType.U8, I64),
            "addTryFrom(ref c, from: BTryFrom { span: $SP, operand: $E0, target: prim(.u8), type: prim(.i64) })"),
        ("conditional", () => new BoundConditional(null!, E(0), E(1), E(2), I64),
            "addConditional(ref c, from: BConditional { span: $SP, condition: $E0, whenTrue: $E1, whenFalse: $E2, type: prim(.i64) })"),
        ("coalesce", () => new BoundCoalesce(null!, E(2), E(1), I64),
            "addCoalesce(ref c, from: BCoalesce { span: $SP, left: $E2, right: $E1, type: prim(.i64) })"),
        ("structLiteral", () => new BoundStructLiteral(null!, Uninit<StructSymbol>(), [E(0), E(1)]) { Payload = [E(2)] },
            "addStructLiteral(ref c, from: BStructLiteral { span: $SP, structId: 0, fields: [$E0, $E1], payload: [$E2] })"),
        ("structLiteral-", () => new BoundStructLiteral(null!, Uninit<StructSymbol>(), []),
            "addStructLiteral(ref c, from: BStructLiteral { span: $SP, structId: 0 })"),
        ("with", () => new BoundWith(null!, E(0), [(Fs, E(1)), (Fs, E(2))]),
            "addWith(ref c, from: BWith { span: $SP, target: $E0, updates: [BWithUpdate { field: 0, value: $E1 }, BWithUpdate { field: 0, value: $E2 }] })"),
        ("tuple", () => new BoundTuple(null!, Uninit<TupleType>(), [E(0), E(1)]),
            "addTuple(ref c, from: BTuple { span: $SP, tupleType: makeType(.tuple, at: 0), elements: [$E0, $E1] })"),
        ("collection", () => new BoundCollection(null!, I64, [E(0), E(1), E(2)]),
            "addCollection(ref c, from: BCollection { span: $SP, type: prim(.i64), elements: [$E0, $E1, $E2] })"),
        ("spread", () => new BoundSpread(null!, E(5), I64), "addSpread(ref c, from: BSpread { span: $SP, operand: $E5, type: prim(.i64) })"),
        ("index", () => new BoundIndex(null!, E(0), E(1), true, I64),
            "addIndex(ref c, from: BIndex { span: $SP, target: $E0, index: $E1, fromEnd: true, type: prim(.i64) })"),
        ("slice", () => new BoundSlice(null!, E(0), E(1), false, E(2), false, false, I64),
            "addSlice(ref c, from: BSlice { span: $SP, target: $E0, start: $E1, end: $E2, type: prim(.i64) })"),
        ("slice-", () => new BoundSlice(null!, E(0), null, false, null, false, false, I64),
            "addSlice(ref c, from: BSlice { span: $SP, target: $E0, type: prim(.i64) })"),
        ("sliceEnd", () => new BoundSlice(null!, E(0), null, false, E(2), false, false, I64),
            "addSlice(ref c, from: BSlice { span: $SP, target: $E0, end: $E2, type: prim(.i64) })"),
        ("range", () => new BoundRange(null!, E(0), E(1), false, I64),
            "addRange(ref c, from: BRange { span: $SP, start: $E0, end: $E1, type: prim(.i64) })"),
        ("is", () => new BoundIs(null!, E(0), P(0)), "addIs(ref c, from: BIs { span: $SP, value: $E0, pattern: $P0 })"),
        ("switchExpr", () => new BoundSwitchExpr(null!, E(0), [new BoundSwitchArm(null!, P(0), E(1), E(2)), new BoundSwitchArm(null!, P(1), null, E(3))], I64),
            "addSwitchExpr(ref c, from: BSwitchExpr { span: $SP, value: $E0, arms: [BSwitchArm { span: sp, pattern: $P0, guard: $E1, value: $E2 }, BSwitchArm { span: sp, pattern: $P1, value: $E3 }], type: prim(.i64) })"),
        ("interpolated", () => new BoundInterpolated(null!, [new("a", null), new(null, E(0)), new("b", null), new(null, E(1))]),
            "addInterpolated(ref c, from: BInterpolated { span: $SP, parts: [BInterpolationPart { text: \"a\" }, BInterpolationPart { value: $E0 }, BInterpolationPart { text: \"b\" }, BInterpolationPart { value: $E1 }] })"),
        ("move", () => new BoundMove(null!, Loc()), "addMove(ref c, from: BMove { span: $SP, operand: $LOC })"),
        ("try", () => new BoundTry(null!, E(0), I64), "addTry(ref c, from: BTry { span: $SP, operand: $E0, type: prim(.i64) })"),
        ("catch", () => new BoundCatch(null!, E(0), null, null, E(1), I64),
            "addCatch(ref c, from: BCatch { span: $SP, operand: $E0, handler: $E1, type: prim(.i64) })"),
        ("jump", () => new BoundJump(null!, S(1)), "addJump(ref c, from: BJump { span: $SP, jump: $S1 })"),
        ("blockExpr", () => new BoundBlockExpr(null!, Blk(), PrimitiveType.Void),
            "addBlockExpr(ref c, from: BBlockExpr { span: $SP, block: $BLK, type: prim(.void) })"),
        ("errorVariant", () => new BoundErrorVariant(null!, Ev) { Payload = [E(0), E(1)] },
            "addErrorVariant(ref c, from: BErrorVariant { span: $SP, variant: 0, payload: [$E0, $E1] })"),
        ("enumMember", () => new BoundEnumMember(null!, Em), "addEnumMember(ref c, from: BEnumMember { span: $SP, member: 0 })"),
        ("unionNew", () => new BoundUnionNew(null!, Uv, [E(0), E(1)]), "addUnionNew(ref c, from: BUnionNew { span: $SP, variant: 0, payload: [$E0, $E1] })"),
        ("enumValues", () => new BoundEnumValues(null!, Uninit<EnumSymbol>(), PrimitiveType.Bool),
            "addEnumValues(ref c, from: BEnumValues { span: $SP, enumId: 0, type: prim(.bool) })"),
        ("staticArray", () => new BoundStaticArray(null!, Cs), "addStaticArray(ref c, from: BStaticArray { span: $SP, constId: 0 })"),
        ("stackalloc", () => new BoundStackalloc(null!, I64, 4, PrimitiveType.String),
            "addStackalloc(ref c, from: BStackalloc { span: $SP, element: prim(.i64), count: 4, type: prim(.string) })"),
        ("errorExpr", () => new BoundErrorExpr(null!), "addErrorExpr(ref c, from: BErrorExpr { span: $SP })"),
        ("typeRef", () => new BoundTypeRef(null!, I64), "addTypeRef(ref c, from: BTypeRef { span: $SP, referenced: prim(.i64) })"),
        ("moduleRef", () => new BoundModuleRef(null!, new ModuleSymbol("m", "")), "addModuleRef(ref c, from: BModuleRef { span: $SP, module: 0 })"),
        ("lambda", () => new BoundLambda(null!, Fn, [Loc(), Loc()], Uninit<FunctionType>()),
            "addLambda(ref c, from: BLambda { span: $SP, lambda: 0, captures: [$LOC, $LOC], functionType: makeType(.function, at: 0) })"),
        ("functionRef", () => new BoundFunctionRef(null!, Fn, Uninit<FunctionType>()),
            "addFunctionRef(ref c, from: BFunctionRef { span: $SP, function: 0, functionType: makeType(.function, at: 0) })"),
        ("block", () => new BoundBlock(null!, [S(0), S(1), S(2)]), "addBlock(ref c, from: BBlock { span: $SP, statements: [$S0, $S1, $S2] })"),
        ("localDecl", () => new BoundLocalDecl(null!, La, E(0)), "addLocalDecl(ref c, from: BLocalDecl { span: $SP, local: 0, initializer: $E0 })"),
        ("localDecl-", () => new BoundLocalDecl(null!, La, null), "addLocalDecl(ref c, from: BLocalDecl { span: $SP, local: 0 })"),
        ("exprStmt", () => new BoundExprStmt(null!, E(2)), "addExprStmt(ref c, from: BExprStmt { span: $SP, expr: $E2 })"),
        ("assign", () => new BoundAssign(null!, E(0), E(1), BinaryOp.Add), "addAssign(ref c, from: BAssign { span: $SP, target: $E0, value: $E1, op: .add })"),
        ("return", () => new BoundReturn(null!, E(0)), "addReturn(ref c, from: BReturn { span: $SP, value: $E0 })"),
        ("return-", () => new BoundReturn(null!, null), "addReturn(ref c, from: BReturn { span: $SP })"),
        ("fail", () => new BoundFail(null!, E(3)), "addFail(ref c, from: BFail { span: $SP, error: $E3 })"),
        ("become", () => new BoundBecome(null!, Uv, [E(0), E(1)]), "addBecome(ref c, from: BBecome { span: $SP, state: 0, payload: [$E0, $E1] })"),
        ("break", () => new BoundBreak(null!, Jt), "addBreak(ref c, from: BBreak { span: $SP, target: 0 })"),
        ("continue", () => new BoundContinue(null!, Jt), "addContinue(ref c, from: BContinue { span: $SP, target: 0 })"),
        ("if", () => new BoundIf(null!, E(0), S(0), S(1)), "addIf(ref c, from: BIf { span: $SP, condition: $E0, then: $S0, elseBranch: $S1 })"),
        ("if-", () => new BoundIf(null!, E(0), S(0), null), "addIf(ref c, from: BIf { span: $SP, condition: $E0, then: $S0 })"),
        ("while", () => new BoundWhile(null!, E(0), S(0), Jt), "addWhile(ref c, from: BWhile { span: $SP, condition: $E0, body: $S0, target: 0 })"),
        ("doWhile", () => new BoundDoWhile(null!, S(0), E(0), Jt), "addDoWhile(ref c, from: BDoWhile { span: $SP, body: $S0, condition: $E0, target: 0 })"),
        ("for", () => new BoundFor(null!, La, E(0), S(0), Jt), "addFor(ref c, from: BFor { span: $SP, variable: 0, iterable: $E0, body: $S0, target: 0 })"),
        ("switch", () => new BoundSwitch(null!, E(0), [new BoundSwitchSection(null!, P(0), E(1), [S(0), S(1)]), new BoundSwitchSection(null!, null, null, [S(2)])], Jt),
            "addSwitch(ref c, from: BSwitch { span: $SP, value: $E0, sections: [BSwitchSection { span: sp, pattern: $P0, guard: $E1, body: [$S0, $S1] }, BSwitchSection { span: sp, body: [$S2] }], target: 0 })"),
        ("defer", () => new BoundDefer(null!, S(0)), "addDefer(ref c, from: BDefer { span: $SP, body: $S0 })"),
        ("assert", () => new BoundAssert(null!, E(0)), "addAssert(ref c, from: BAssert { span: $SP, condition: $E0 })"),
        ("errdefer", () => new BoundErrdefer(null!, null, S(1)), "addErrdefer(ref c, from: BErrdefer { span: $SP, body: $S1 })"),
        ("panicDefer", () => new BoundPanicDefer(null!, null, S(2)), "addPanicDefer(ref c, from: BPanicDefer { span: $SP, body: $S2 })"),
        ("setOnPanic", () => new BoundSetOnPanic(null!, Fn), "addSetOnPanic(ref c, from: BSetOnPanic { span: $SP, handler: 0 })"),
        ("context", () => new BoundContext(null!, E(0), Blk()), "addContext(ref c, from: BContext { span: $SP, allocator: $E0, body: $BLK })"),
        ("errorStmt", () => new BoundErrorStmt(null!), "addErrorStmt(ref c, from: BErrorStmt { span: $SP })"),
        ("constantPattern", () => new BoundConstantPattern(null!, PrimitiveType.I8, E(0)),
            "addConstantPattern(ref c, from: BConstantPattern { span: $SP, inputType: prim(.i8), value: $E0 })"),
        ("relationalPattern", () => new BoundRelationalPattern(null!, PrimitiveType.I16, BinaryOp.Lt, E(1)),
            "addRelationalPattern(ref c, from: BRelationalPattern { span: $SP, inputType: prim(.i16), op: .lt, value: $E1 })"),
        ("rangePattern", () => new BoundRangePattern(null!, PrimitiveType.I32, E(0), E(1), true),
            "addRangePattern(ref c, from: BRangePattern { span: $SP, inputType: prim(.i32), start: $E0, end: $E1, inclusive: true })"),
        ("bindPattern", () => new BoundBindPattern(null!, PrimitiveType.U8, La), "addBindPattern(ref c, from: BBindPattern { span: $SP, inputType: prim(.u8), local: 0 })"),
        ("notPattern", () => new BoundNotPattern(null!, PrimitiveType.U16, P(1)), "addNotPattern(ref c, from: BNotPattern { span: $SP, inputType: prim(.u16), operand: $P1 })"),
        ("binaryPattern", () => new BoundBinaryPattern(null!, PrimitiveType.U32, true, P(0), P(1)),
            "addBinaryPattern(ref c, from: BBinaryPattern { span: $SP, inputType: prim(.u32), isAnd: true, left: $P0, right: $P1 })"),
        ("propertyPattern", () => new BoundPropertyPattern(null!, PrimitiveType.U64, I64, [new BoundSubpattern([], P(0)), new BoundSubpattern([], P(1))], null),
            "addPropertyPattern(ref c, from: BPropertyPattern { span: $SP, inputType: prim(.u64), valueType: prim(.i64), subpatterns: [BSubpattern { pattern: $P0 }, BSubpattern { pattern: $P1 }] })"),
        ("unionField", () => new BoundUnionField(null!, E(0), Uv, 0), "addUnionField(ref c, from: BUnionField { span: $SP, receiver: $E0, variant: 0, index: 0 })"),
        ("unionPattern", () => new BoundUnionPattern(null!, PrimitiveType.F32, Uv, [P(0), P(1)]),
            "addUnionPattern(ref c, from: BUnionPattern { span: $SP, inputType: prim(.f32), variant: 0, subpatterns: [$P0, $P1] })"),
        ("errorField", () => new BoundErrorField(null!, E(1), Ev, 0), "addErrorField(ref c, from: BErrorField { span: $SP, receiver: $E1, variant: 0, index: 0 })"),
        ("errorPattern", () => new BoundErrorPattern(null!, PrimitiveType.F64, Ev, [P(0), P(2)]),
            "addErrorPattern(ref c, from: BErrorPattern { span: $SP, inputType: prim(.f64), variant: 0, subpatterns: [$P0, $P2] })"),
        ("methodGroup", () => Group("BoundMethodGroup", null!, E(0), Fn), "addMethodGroup(ref c, from: BMethodGroup { span: $SP, receiver: $E0, function: 0 })"),
        ("variantGroup", () => Group("BoundVariantGroup", null!, Uv), "addVariantGroup(ref c, from: BVariantGroup { span: $SP, variant: 0 })"),
        ("builtinGroup", () => Group("BoundBuiltinGroup", null!, E(0), "length"),
            "addBuiltinGroup(ref c, from: BBuiltinGroup { span: $SP, receiver: $E0, name: \"length\" })"),
    ];

    private static readonly Int128 Two100 = (Int128)1 << 100;

    /// <summary>The constants of <c>ConstantTextMatchesCSharp</c>: the C# value and the Bayan <c>Constant</c> literal.</summary>
    private static readonly (object Value, string Bayan)[] Constants =
    [
        ((Int128)0, "Constant { kind: .integer, integer: bigFromI64(0) }"),
        ((Int128)(-1), "Constant { kind: .integer, integer: bigFromI64(-1) }"),
        ((Int128)42, "Constant { kind: .integer, integer: bigFromI64(42) }"),
        ((Int128)long.MaxValue, "Constant { kind: .integer, integer: bigFromI64(9223372036854775807) }"),
        ((Int128)long.MinValue, "Constant { kind: .integer, integer: bigMin(64, signed: true) }"),
        ((Int128)ulong.MaxValue, "Constant { kind: .integer, integer: bigFromU64(18446744073709551615) }"),
        (Two100, "Constant { kind: .integer, integer: bigShl(bigFromI64(1), by: 100) }"),
        (-Two100, "Constant { kind: .integer, integer: bigNeg(bigShl(bigFromI64(1), by: 100)) }"),
        (Int128.MaxValue, "Constant { kind: .integer, integer: bigMax(128, signed: true) }"),
        (Int128.MinValue, "Constant { kind: .integer, integer: bigMin(128, signed: true) }"),
        (true, "Constant { kind: .flag, flag: true }"),
        (false, "Constant { kind: .flag, flag: false }"),
        (false, "Constant { kind: .flag }"),
        ("say \"hi\"", "Constant { kind: .text, text: \"say \\\"hi\\\"\" }"),
        ("", "Constant { kind: .text, text: \"\" }"),
        (new Rune('a'), "Constant { kind: .rune, rune: 97 }"),
        (new Rune(0x1F600), "Constant { kind: .rune, rune: 128512 }"),
        (1.5, "Constant { kind: .real, real: 1.5 }"),
        (0.1, "Constant { kind: .real, real: 0.1 }"),
        (100.0, "Constant { kind: .real, real: 100.0 }"),
        (0.0, "Constant { kind: .real, real: 0.0 }"),
        (-0.0, "Constant { kind: .real, real: negZero() }"),
        (-2.5, "Constant { kind: .real, real: -2.5 }"),
        (1e20, "Constant { kind: .real, real: 1e20 }"),
        (1e15, "Constant { kind: .real, real: 1e15 }"),
        (1e14, "Constant { kind: .real, real: 1e14 }"),
        (1e16, "Constant { kind: .real, real: 1e16 }"),
        (1e17, "Constant { kind: .real, real: 1e17 }"),
        (1.5e16, "Constant { kind: .real, real: 1.5e16 }"),
        (12345678901234567.0, "Constant { kind: .real, real: 12345678901234567.0 }"),
        (123456789012345678.0, "Constant { kind: .real, real: 123456789012345678.0 }"),
        (1.5e-5, "Constant { kind: .real, real: 1.5e-5 }"),
        (0.00012345, "Constant { kind: .real, real: 0.00012345 }"),
        (123456789012345.0, "Constant { kind: .real, real: 123456789012345.0 }"),
        (1234567890123456.0, "Constant { kind: .real, real: 1234567890123456.0 }"),
        (0.0001, "Constant { kind: .real, real: 0.0001 }"),
        (0.00001, "Constant { kind: .real, real: 0.00001 }"),
        (1.25e-7, "Constant { kind: .real, real: 1.25e-7 }"),
        (-2.5e-300, "Constant { kind: .real, real: -2.5e-300 }"),
        (1.7976931348623157e308, "Constant { kind: .real, real: 1.7976931348623157e308 }"),
        (5e-324, "Constant { kind: .real, real: 5e-324 }"),
        (3.0e100, "Constant { kind: .real, real: 3e100 }"),
        (double.PositiveInfinity, "Constant { kind: .real, real: 1.0 / zero() }"),
        (double.NegativeInfinity, "Constant { kind: .real, real: -1.0 / zero() }"),
        (double.NaN, "Constant { kind: .real, real: zero() / zero() }"),
    ];

    private static readonly Lazy<string> Main = new(MainSource);

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(Main.Value));

    private static string MainSource()
    {
        var s = new StringBuilder();
        void L(string line) => s.Append(line).Append('\n');
        L("f64 zero() => 0.0");
        L("f64 negZero() => -zero()");
        L("");
        L("string kindsText(in List<BNode> ns) {");
        L("    var sb = StrBuf()");
        L("    for n in ns {");
        L("        sb.append($\" {boundKind(n)}\")");
        L("    }");
        L("    return $\"{sb.finish()}\"");
        L("}");
        L("");
        L("string typeText(in Ctx c, for BNode n) {");
        L("    let k = boundKind(n)");
        L("    var t: Type = -1");
        L("    if isExprKind(k) {");
        L("        t = exprType(in c, for: n)");
        L("    } else if isPatternKind(k) {");
        L("        t = patternInput(in c, for: n)");
        L("    } else {");
        L("        return \"-\"");
        L("    }");
        L("    if kindOf(t) == .primitive {");
        L("        return $\"{primKindOf(typeIndex(t))}\"");
        L("    }");
        L("    return $\"{kindOf(t)}\"");
        L("}");
        L("");
        L("BExpr leafE(ref Ctx c, at i64 i) {");
        L("    let sp = TextSpan { start: 0, length: 0 }");
        L("    switch i {");
        L("        case 0: return addLiteral(ref c, from: BLiteral { span: sp, type: prim(.i64), value: Constant { kind: .integer } })");
        L("        case 1: return addDefault(ref c, from: BDefault { span: sp, type: prim(.i64) })");
        L("        case 2: return addThis(ref c, from: BThis { span: sp, type: prim(.i64) })");
        L("        case 3: return addErrorExpr(ref c, from: BErrorExpr { span: sp })");
        L("        case 4: return addTypeRef(ref c, from: BTypeRef { span: sp, referenced: prim(.i64) })");
        L("        default: return addSymbolLiteral(ref c, from: BSymbolLiteral { span: sp, text: \"s\" })");
        L("    }");
        L("}");
        L("");
        L("BStmt leafS(ref Ctx c, at i64 i) {");
        L("    let sp = TextSpan { start: 0, length: 0 }");
        L("    switch i {");
        L("        case 0: return addErrorStmt(ref c, from: BErrorStmt { span: sp })");
        L("        case 1: return addBreak(ref c, from: BBreak { span: sp, target: 0 })");
        L("        case 2: return addContinue(ref c, from: BContinue { span: sp, target: 0 })");
        L("        default: return addSetOnPanic(ref c, from: BSetOnPanic { span: sp, handler: 0 })");
        L("    }");
        L("}");
        L("");
        L("BPattern leafP(ref Ctx c, at i64 i) {");
        L("    let sp = TextSpan { start: 0, length: 0 }");
        L("    switch i {");
        L("        case 0: return addBindPattern(ref c, from: BBindPattern { span: sp, inputType: prim(.i64) })");
        L("        case 1: return addUnionPattern(ref c, from: BUnionPattern { span: sp, inputType: prim(.i64), variant: 0 })");
        L("        case 2: return addErrorPattern(ref c, from: BErrorPattern { span: sp, inputType: prim(.i64), variant: 0 })");
        L("        default: return addPropertyPattern(ref c, from: BPropertyPattern { span: sp, inputType: prim(.i64), valueType: prim(.i64) })");
        L("    }");
        L("}");
        L("");
        L("BStmt leafBlock(ref Ctx c) {");
        L("    let s = leafS(ref c, at: 0)");
        L("    return addBlock(ref c, from: BBlock { span: TextSpan { start: 0, length: 0 }, statements: [s] })");
        L("}");
        L("");
        L("BExpr leafLocal(ref Ctx c) => addLocal(ref c, from: BLocal { span: TextSpan { start: 0, length: 0 }, local: 0 })");
        L("");
        L("void printNode(in Ctx c, for BNode n, as string label) {");
        L("    let sp = nodeSpan(in c, for: n)");
        L("    let ch = children(in c, for: n)");
        L("    let d = descendants(in c, for: n)");
        L("    print($\"{label} {boundKind(n)} {sp.start}+{sp.length} {typeText(in c, for: n)}:{kindsText(in ch)} |{kindsText(in d)}\")");
        L("}");
        L("");
        L("i32 main() {");
        L("    var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }");
        L("    let sp = TextSpan { start: 0, length: 0 }");
        L("    c.locals.append(LocalSymbol { name: \"a\", type: prim(.i64) })");
        L("    c.locals.append(LocalSymbol { name: \"b\", type: prim(.i64) })");
        L("    c.locals.append(LocalSymbol { name: \"p\", type: prim(.i16) })");
        L("    c.functions.append(FunctionSymbol { name: \"f\" })");
        L("    c.fields.append(FieldSymbol { name: \"x\", type: prim(.i32) })");
        L("    c.consts.append(ConstSymbol { name: \"k\", type: prim(.u8) })");
        L("    c.enumMembers.append(EnumMemberSymbol { name: \"m\", owner: 0 })");
        L("    var uv = UnionVariantSymbol { name: \"v\", owner: 0 }");
        L("    uv.fields.append(2)");
        L("    c.unionVariants.append(move uv)");
        L("    var ev = ErrorVariantSymbol { name: \"e\", set: 0 }");
        L("    ev.fields.append(2)");
        L("    c.errorVariants.append(move ev)");
        L("    c.jumpTargets.append(JumpTarget { isLoop: true })");

        // tiny tree
        L("    print(\"== tiny\")");
        L("    let ta = addLocal(ref c, from: BLocal { span: TextSpan { start: 10, length: 1 }, local: 0 })");
        L("    let tb = addLocal(ref c, from: BLocal { span: TextSpan { start: 14, length: 1 }, local: 1 })");
        L("    let tcond = addBinary(ref c, from: BBinary { span: TextSpan { start: 10, length: 5 }, op: .lt, left: ta, right: tb, type: prim(.bool) })");
        L("    let tx = addLocal(ref c, from: BLocal { span: TextSpan { start: 30, length: 1 }, local: 0 })");
        L("    let ty = addLiteral(ref c, from: BLiteral { span: TextSpan { start: 33, length: 1 }, type: prim(.i64), value: Constant { kind: .integer, integer: bigFromI64(1) } })");
        L("    let tcall = addCall(ref c, from: BCall { span: TextSpan { start: 28, length: 7 }, function: 0, arguments: [tx, ty], type: prim(.i64) })");
        L("    let tret = addReturn(ref c, from: BReturn { span: TextSpan { start: 21, length: 15 }, value: tcall })");
        L("    let tif = addIf(ref c, from: BIf { span: TextSpan { start: 6, length: 31 }, condition: tcond, then: tret })");
        L("    let troot = addBlock(ref c, from: BBlock { span: TextSpan { start: 0, length: 40 }, statements: [tif] })");
        L("    let tall = descendants(in c, for: troot)");
        L("    for n in tall {");
        L("        printNode(in c, for: n, as: \"node\")");
        L("    }");

        L("    print(\"== children\")");
        var v = 0;
        for (var i = 0; i < Cases.Length; i++)
        {
            var expr = Cases[i].Bayan.Replace("$SP", $"TextSpan {{ start: {1000 + i}, length: 3 }}");
            expr = Regex.Replace(expr, @"\$(E\d|S\d|P\d|BLK|LOC)", m =>
            {
                var name = $"v{v++}";
                var p = m.Groups[1].Value;
                var call = p switch
                {
                    "BLK" => "leafBlock(ref c)",
                    "LOC" => "leafLocal(ref c)",
                    _ => $"leaf{p[0]}(ref c, at: {p[1]})",
                };
                L($"    let {name} = {call}");
                return name;
            });
            L($"    let n{i} = {expr}");
            L($"    printNode(in c, for: n{i}, as: \"{Cases[i].Label}\")");
        }

        L("    print(\"== constants\")");
        for (var i = 0; i < Constants.Length; i++)
        {
            L($"    let k{i} = {Constants[i].Bayan}");
            L($"    print(constantText(in k{i}))");
        }

        var kinds = NodeTypes().Count;
        L("    print(\"== handles\")");
        L("    print($\"count {BoundKind.values.length}\")");
        L($"    for i in 0..{kinds} {{");
        L("        let k = BoundKind.fromOrdinal(i) ?? .errorExpr");
        L("        let h0 = makeBound(k, at: 0)");
        L("        let h1 = makeBound(k, at: 2147483647)");
        L("        let h2 = makeBound(k, at: 12345)");
        L("        print($\"{i} {k} {isExprKind(k)} {isStmtKind(k)} {isPatternKind(k)} {boundKind(h0) == k} {boundIndex(h0)} {boundKind(h1) == k} {boundIndex(h1)} {boundKind(h2)} {boundIndex(h2)}\")");
        L("    }");
        L("    return 0");
        L("}");
        return s.ToString();
    }

    // ── The C# side ──

    private static string KindsText(IEnumerable<BoundNode> ns) => string.Concat(ns.Select(n => " " + KindName(n.GetType())));

    private static string TypeText(BoundNode n)
    {
        var t = n switch
        {
            BoundExpr e => e.Type,
            BoundPattern p => p.InputType,
            _ => null,
        };
        return t switch
        {
            null => "-",
            PrimitiveType { Kind: PrimitiveKind.Never } => "neverKind",
            PrimitiveType p => Lower(p.Kind.ToString()),
            StructSymbol => "structType",
            UnionSymbol => "unionType",
            EnumSymbol => "enumType",
            ErrorSetSymbol => "errorSet",
            ErrorType => "error",
            TupleType => "tuple",
            FunctionType => "function",
            BuiltinType => "builtin",
            _ => t.GetType().Name,
        };
    }

    private static string NodeLine(BoundNode n, string label, string span) =>
        $"{label} {KindName(n.GetType())} {span} {TypeText(n)}:{KindsText(BoundTree.Children(n))} |{KindsText(BoundTree.Descendants(n))}";

    [Fact]
    public void BuildsAndWalksATinyTree()
    {
        var a = new BoundLocal(null!, La);
        var b = new BoundLocal(null!, Lb);
        var cond = new BoundBinary(null!, BinaryOp.Lt, a, b, PrimitiveType.Bool);
        var x = new BoundLocal(null!, La);
        var y = new BoundLiteral(null!, I64, new ConstantValue((Int128)1));
        var call = new BoundCall(null!, Fn, null, [x, y], I64);
        var ret = new BoundReturn(null!, call);
        var ifs = new BoundIf(null!, cond, ret, null);
        var root = new BoundBlock(null!, [ifs]);
        var spans = new Dictionary<BoundNode, string>(ReferenceEqualityComparer.Instance)
        {
            [a] = "10+1", [b] = "14+1", [cond] = "10+5", [x] = "30+1", [y] = "33+1", [call] = "28+7", [ret] = "21+15", [ifs] = "6+31", [root] = "0+40",
        };
        var expected = BoundTree.Descendants(root).Select(n => NodeLine(n, "node", spans[n])).ToList();
        Assert.Equal(9, expected.Count);
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "tiny", expected);
    }

    [Fact]
    public void ChildrenOrderMatchesBoundTreeForEveryKind()
    {
        var expected = Cases.Select((k, i) => NodeLine(k.CSharp(), k.Label, $"{1000 + i}+3")).ToList();
        var covered = Cases.Select(k => k.CSharp().GetType()).ToHashSet();
        Assert.Empty(NodeTypes().Where(t => !covered.Contains(t)).Select(t => t.Name));
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "children", expected);
    }

    [Fact]
    public void ConstantTextMatchesCSharp()
    {
        // ConstantValue.ToString() follows the current culture; the port mirrors the invariant one (Q536).
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        List<string> expected;
        try
        {
            expected = Constants.Select(k => new ConstantValue(k.Value).ToString()).ToList();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "constants", expected);
    }

    [Fact]
    public void HandlesRoundTrip()
    {
        var types = NodeTypes();
        var expected = new List<string> { $"count {types.Count}" };
        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            var k = KindName(t);
            static string B(bool v) => v ? "true" : "false";
            expected.Add($"{i} {k} {B(typeof(BoundExpr).IsAssignableFrom(t))} {B(typeof(BoundStmt).IsAssignableFrom(t))} {B(typeof(BoundPattern).IsAssignableFrom(t))} true 0 true 2147483647 {k} 12345");
        }
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "handles", expected);
    }

    [Fact]
    public void EveryRecordHasAStruct()
    {
        // The C# records: every record of BoundNodes.cs but ConstantValue (Constant, Q536) and the abstract bases, then the groups.
        var names = Regex.Matches(File.ReadAllText(Path.Combine(BindingDir, "BoundNodes.cs")), @"sealed record (\w+)\(")
            .Select(m => m.Groups[1].Value).Where(n => n != "ConstantValue").Concat(Groups).ToList();
        var expected = new List<string>();
        foreach (var name in names)
        {
            var t = BindingType(name);
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var fields = ctor.GetParameters().Select(p => Lower(p.Name!)).ToList();
            fields.AddRange(t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is not null && !fields.Contains(Lower(p.Name))).Select(p => Lower(p.Name)));
            var (renames, added) = FieldChanges.GetValueOrDefault(name, (new(), []));
            var mapped = fields.Select(f => f == "syntax" ? "span" : renames.GetValueOrDefault(f, f)).Concat(added);
            var bayan = name.StartsWith("Bound", StringComparison.Ordinal) ? "B" + name["Bound".Length..] : name;
            expected.Add($"{bayan}: {string.Join(' ', mapped)}");
        }

        // The Bayan structs, read from bound.byn's declarations.
        var source = File.ReadAllText(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, "bound.byn"));
        var structs = Regex.Matches(source, @"^(?:public )?struct (\w+) \{\n((?:    .*\n)*?)\}", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => string.Join(' ', m.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal))
                .Select(l => l.Split(" = ")[0].Trim().Split(' ')[^1])));
        var actual = expected.Select(e => e[..e.IndexOf(':')]).Select(n => structs.TryGetValue(n, out var f) ? $"{n}: {f}" : $"{n}: <missing>").ToList();
        SelfhostSemanticsHarness.AssertSection(new() { ["records"] = actual }, "records", expected);
    }
}
