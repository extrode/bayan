using System.Globalization;
using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using ArrayType = Bayan.Semantics.Symbols.ArrayType;
using FunctionType = Bayan.Semantics.Symbols.FunctionType;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;
using TupleType = Bayan.Semantics.Symbols.TupleType;

namespace Bayan.Tests;

/// <summary>
/// T6: <c>selfhost/semantics/conversions.byn</c> (<c>Conversions.cs</c>, <c>TypeFacts.cs</c>) and
/// <c>fold.byn</c> (<c>ConstantFolding.cs</c>) against the C# classes. One generated <c>main.byn</c>
/// rebuilds the oracle's symbols and types by hand and prints a section per function family.
/// </summary>
public sealed class SelfhostSemanticsT6Tests
{
    private sealed record Generated(string Source, Dictionary<string, List<string>> Expected);

    private static readonly Lazy<Generated> Cases = new(Generate);

    private static readonly Lazy<Dictionary<string, List<string>>> Actual = new(() => SelfhostSemanticsHarness.RunSections(Cases.Value.Source));

    [Fact]
    public void PrimitiveConversionsMatchCSharp() => Check("prims");

    [Fact]
    public void ConversionsMatchCSharp() => Check("pairs");

    [Fact]
    public void FunctionConversionsMatchCSharp() => Check("functions");

    [Fact]
    public void UniquenessMatchesCSharp() => Check("unique");

    [Fact]
    public void UnaryFoldingMatchesCSharp() => Check("unary");

    [Fact]
    public void BinaryFoldingMatchesCSharp() => Check("binary");

    [Fact]
    public void CastFoldingMatchesCSharp() => Check("cast");

    [Fact]
    public void EvaluateMatchesCSharp() => Check("evaluate");

    private static void Check(string section) => SelfhostSemanticsHarness.AssertSection(Actual.Value, section, Cases.Value.Expected[section]);

    // ── The oracle's helpers ─────────────────────────────────────────────────────

    private static readonly System.Type Folding =
        typeof(Conversions).Assembly.GetType("Bayan.Semantics.Binding.ConstantFolding") ?? throw new InvalidOperationException("ConstantFolding");

    private static ConstantValue? Fold(string name, params object?[] args) =>
        (ConstantValue?)Folding.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);

    private static string B(bool value) => value ? "true" : "false";

    private static string K(ConversionKind? k) => k is { } v ? ((int)v).ToString(CultureInfo.InvariantCulture) : "-";

    private static string T(TypeSymbol? t) => t is null ? "-" : t.DisplayName;

    private static string C(ConstantValue? c) => c is null ? "-" : c.ToString();

    private static string BigLit(Int128 n) => $"Big {{ lo: {(ulong)n}, hi: {(ulong)(n >> 64)} }}";

    private static string RealLit(double d)
    {
        if (double.IsNaN(d)) return "zero / zero";
        if (double.IsPositiveInfinity(d)) return "1.0 / zero";
        if (double.IsNegativeInfinity(d)) return "-1.0 / zero";
        var s = d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
        if (!s.Contains('.') && !s.Contains('e')) s += ".0";
        if (s.StartsWith('-')) s = $"0.0 - {s[1..]}";
        if (d == 0 && double.IsNegative(d)) s = "-zero";
        return s;
    }

    private static string ConstLit(ConstantValue v) => v.Value switch
    {
        Int128 n => $"Constant {{ kind: .integer, integer: {BigLit(n)} }}",
        double d => $"Constant {{ kind: .real, real: {RealLit(d)} }}",
        bool b => $"Constant {{ kind: .flag, flag: {B(b)} }}",
        Rune r => $"Constant {{ kind: .rune, rune: {r.Value} }}",
        string s => $"Constant {{ kind: .text, text: \"{s}\" }}",
        _ => throw new InvalidOperationException(),
    };

    private static ModuleSymbol Mod(string name) => new(name, "");

    private static StructSymbol Struct(string name, ModuleSymbol mod, bool unique, params (string Name, TypeSymbol Type)[] fields)
    {
        var lines = string.Join("\n", fields.Select(f => $"    i32 {f.Name}"));
        var src = SourceText.FromString("<t>", $"{(unique ? "unique " : "")}struct {name} {{\n{lines}\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (StructDecl)tree.Root.Members[0];
        var s = new StructSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl, new FileContext(mod, tree));
        for (var i = 0; i < fields.Length; i++)
            s.Fields.Add(new FieldSymbol(fields[i].Name, new Location(src, ((FieldDecl)decl.Members[i]).Name.Span), s, Accessibility.Private, (FieldDecl)decl.Members[i]) { Type = fields[i].Type });
        return s;
    }

    private static UnionSymbol Union(string name, ModuleSymbol mod, params string[] variants)
    {
        var lines = string.Join("\n", variants.Select(v => $"    {v}"));
        var src = SourceText.FromString("<t>", $"union {name} {{\n{lines}\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (UnionDecl)tree.Root.Members[0];
        var u = new UnionSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl, new FileContext(mod, tree));
        foreach (var v in decl.Variants)
            u.Variants.Add(new UnionVariantSymbol(v.Name.Text, new Location(src, v.Name.Span), u, u.Variants.Count, null));
        return u;
    }

    private static ErrorSetSymbol ErrorSet(string name, ModuleSymbol mod, params string[] variants)
    {
        var lines = string.Join(",\n", variants.Select(v => $"    {v}"));
        var src = SourceText.FromString("<t>", $"union error {name} {{\n{lines},\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (ErrorEnumDecl)tree.Root.Members[0];
        var es = new ErrorSetSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl);
        foreach (var v in decl.Variants)
            es.Variants.Add(new ErrorVariantSymbol(v.Name.Text, new Location(src, v.Name.Span), es, (uint)(es.Variants.Count + 1), null));
        return es;
    }

    private static InterfaceSymbol Interface(string name, ModuleSymbol mod)
    {
        var src = SourceText.FromString("<t>", $"interface {name} {{\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (InterfaceDecl)tree.Root.Members[0];
        return new InterfaceSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, new FileContext(mod, tree)) { Decl = decl };
    }

    // ── The generated program and its oracle ─────────────────────────────────────

    private static Generated Generate()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return GenerateNow(); }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static Generated GenerateNow()
    {
        var src = new List<string>();
        var expected = new Dictionary<string, List<string>>();
        var temps = 0;
        string Let(string expr)
        {
            var name = $"x{temps++}";
            src.Add($"    let {name} = {expr}");
            return name;
        }

        // The oracle's symbols; the generated `main` rebuilds the same ones by hand.
        var mod = Mod("a");
        var shape = Interface("Shape", mod);
        var tpT = new TypeParameterType("T") { Constraints = [shape] };
        var tpU = new TypeParameterType("U");
        var plain = Struct("Plain", mod, false, ("x", PrimitiveType.I32));
        var holder = Struct("Holder", mod, false, ("x", PrimitiveType.I32), ("items", new ListType(PrimitiveType.I32)));
        var handle = Struct("Handle", mod, true, ("fd", PrimitiveType.I32));
        var nest = Struct("Nest", mod, false, ("p", plain), ("h", holder));
        var node = Struct("Node", mod, false, ("v", PrimitiveType.I32));
        node.Fields.Add(new FieldSymbol("next", node.Location!, node, Accessibility.Private, node.Fields[0].Decl) { Type = new OptionalType(node) });
        var circle = Struct("Circle", mod, false, ("r", PrimitiveType.I32));
        circle.Conformances.Add(new Conformance(circle, shape));
        var flag = Union("Flag", mod, "on", "off");
        var msg = Union("Msg", mod, "ping", "data(List<u8> bytes)");
        msg.Variants[1].Fields.Add(new ParameterSymbol("bytes", null, new ListType(PrimitiveType.U8), ParamModifier.None, 0));
        var err = ErrorSet("Err", mod, "closed");
        var err2 = ErrorSet("Err2", mod, "busy");

        src.Add("// generated by SelfhostSemanticsT6Tests");
        src.Add("import parser");
        src.Add("string kt(ConversionKind? k) {");
        src.Add("    if (k is { } v) { return $\"{v.ordinal}\" }");
        src.Add("    return \"-\"");
        src.Add("}");
        src.Add("string tt(ref Ctx c, by Type t) => t == -1 ? \"-\" : typeDisplay(ref c, by: t)");
        src.Add("string ct(Constant? k) {");
        src.Add("    if (k is { } v) { return constantText(in v) }");
        src.Add("    return \"-\"");
        src.Add("}");
        src.Add("string st(string? s) {");
        src.Add("    if (s is { } v) { return v }");
        src.Add("    return \"-\"");
        src.Add("}");
        src.Add("i32 main() {");
        src.Add("    var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }");
        src.Add("    c.modules.append(Mod { name: \"a\", files: List<FileId>() })");
        src.Add("    let zero: f64 = 0.0");
        src.Add("    let span0 = TextSpan { start: 0, length: 0 }");
        src.Add("    let i0 = newInterface(ref c, by: InterfaceSymbol { name: \"Shape\", module: 0 })");
        src.Add("    let tp0 = newTypeParam(ref c, by: TypeParam { name: \"T\" })");
        src.Add("    c.typeParams[tp0].constraints.append(i0)");
        src.Add("    let tp1 = newTypeParam(ref c, by: TypeParam { name: \"U\" })");
        void BynStruct(string id, string name, bool unique, params (string Name, string Type)[] fields)
        {
            src.Add($"    let {id} = newStruct(ref c, by: StructSymbol {{ name: \"{name}\", module: 0, modifiers: {(unique ? 64 : 0)} }})");
            foreach (var f in fields)
            {
                var ft = Let(f.Type);
                var fid = Let($"newField(ref c, by: FieldSymbol {{ name: \"{f.Name}\", owner: {id}, type: {ft} }})");
                src.Add($"    c.structs[{id}].fields.append({fid})");
            }
        }
        BynStruct("s0", "Plain", false, ("x", "prim(.i32)"));
        BynStruct("s1", "Holder", false, ("x", "prim(.i32)"), ("items", "internList(ref c, by: prim(.i32))"));
        BynStruct("s2", "Handle", true, ("fd", "prim(.i32)"));
        BynStruct("s3", "Nest", false, ("p", "structTypeOf(s0)"), ("h", "structTypeOf(s1)"));
        BynStruct("s4", "Node", false, ("v", "prim(.i32)"), ("next", "internOptional(ref c, by: structTypeOf(4))"));
        BynStruct("s5", "Circle", false, ("r", "prim(.i32)"));
        src.Add("    let cf0 = newConformance(ref c, by: Conformance { type: s5, iface: i0 })");
        src.Add("    c.structs[s5].conformances.append(cf0)");
        src.Add("    let u0 = newUnion(ref c, by: UnionSymbol { name: \"Flag\", module: 0 })");
        src.Add("    let uv0 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"on\", owner: u0, tag: 0 })");
        src.Add("    c.unions[u0].variants.append(uv0)");
        src.Add("    let uv1 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"off\", owner: u0, tag: 1 })");
        src.Add("    c.unions[u0].variants.append(uv1)");
        src.Add("    let u1 = newUnion(ref c, by: UnionSymbol { name: \"Msg\", module: 0 })");
        src.Add("    let uv2 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"ping\", owner: u1, tag: 0 })");
        src.Add("    c.unions[u1].variants.append(uv2)");
        src.Add("    let uv3 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"data\", owner: u1, tag: 1 })");
        src.Add("    c.unions[u1].variants.append(uv3)");
        src.Add("    let lu8 = internList(ref c, by: prim(.u8))");
        src.Add("    let vf0 = newLocal(ref c, by: LocalSymbol { name: \"bytes\", type: lu8, isParameter: true })");
        src.Add("    c.unionVariants[uv3].fields.append(vf0)");
        src.Add("    let es0 = newErrorSet(ref c, by: ErrorSetSymbol { name: \"Err\", module: 0 })");
        src.Add("    let ev0 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"closed\", set: es0, code: 1 })");
        src.Add("    c.errorSets[es0].variants.append(ev0)");
        src.Add("    let es1 = newErrorSet(ref c, by: ErrorSetSymbol { name: \"Err2\", module: 0 })");
        src.Add("    let ev1 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"busy\", set: es1, code: 1 })");
        src.Add("    c.errorSets[es1].variants.append(ev1)");

        // The type list: primitives first (the `prims` section reads the first 17).
        src.Add("    var types = List<Type>()");
        var types = new List<TypeSymbol>();
        string Add(TypeSymbol cs, string byn)
        {
            var name = Let(byn);
            src.Add($"    types.append({name})");
            types.Add(cs);
            return name;
        }
        string Ids(string elementType, params string[] ids)
        {
            var name = Let($"List<{elementType}>()");
            if (ids.Length > 0) src[^1] = $"    var {name} = List<{elementType}>()";
            foreach (var id in ids) src.Add($"    {name}.append({id})");
            return name;
        }
        var prims = new[] { PrimitiveType.I8, PrimitiveType.I16, PrimitiveType.I32, PrimitiveType.I64, PrimitiveType.U8, PrimitiveType.U16, PrimitiveType.U32, PrimitiveType.U64, PrimitiveType.Isize, PrimitiveType.Usize, PrimitiveType.F32, PrimitiveType.F64, PrimitiveType.Bool, PrimitiveType.Char, PrimitiveType.String, PrimitiveType.Void, PrimitiveType.Never };
        var primNames = new[] { "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64", "isize", "usize", "f32", "f64", "bool", "char", "string", "void", "neverKind" };
        var p = new string[prims.Length];
        for (var i = 0; i < prims.Length; i++) p[i] = Add(prims[i], $"prim(.{primNames[i]})");
        var tI32 = p[2];
        Add(ErrorType.Instance, "errorType()");
        Add(NullType.Instance, "nullType()");
        Add(new OptionalType(PrimitiveType.I32), $"internOptional(ref c, by: {tI32})");
        Add(new OptionalType(PrimitiveType.I64), $"internOptional(ref c, by: {p[3]})");
        Add(new OptionalType(PrimitiveType.U8), $"internOptional(ref c, by: {p[4]})");
        var tListI32 = Add(new ListType(PrimitiveType.I32), $"internList(ref c, by: {tI32})");
        Add(new ListType(PrimitiveType.U8), $"internList(ref c, by: {p[4]})");
        Add(new SpanType(PrimitiveType.I32), $"internSpan(ref c, by: {tI32}, readOnly: false)");
        Add(new SpanType(PrimitiveType.I32, readOnly: true), $"internSpan(ref c, by: {tI32}, readOnly: true)");
        Add(new SpanType(PrimitiveType.U8), $"internSpan(ref c, by: {p[4]}, readOnly: false)");
        Add(new SpanType(PrimitiveType.U8, readOnly: true), $"internSpan(ref c, by: {p[4]}, readOnly: true)");
        Add(new ArrayType(PrimitiveType.I32, 4), $"internArray(ref c, by: {tI32}, at: 4)");
        Add(new ArrayType(PrimitiveType.U8, 4), $"internArray(ref c, by: {p[4]}, at: 4)");
        Add(new ResultType(PrimitiveType.I32, err), $"internResult(ref c, by: {tI32}, to: errorSetTypeOf(es0))");
        Add(new ResultType(PrimitiveType.I64, err), $"internResult(ref c, by: {p[3]}, to: errorSetTypeOf(es0))");
        Add(new ResultType(new ListType(PrimitiveType.I32), err), $"internResult(ref c, by: {tListI32}, to: errorSetTypeOf(es0))");
        Add(err, "errorSetTypeOf(es0)");
        Add(err2, "errorSetTypeOf(es1)");
        var sets2 = Ids("ErrorSetId", "es0", "es1");
        var sets0 = Ids("ErrorSetId");
        var sets1 = Ids("ErrorSetId", "es1");
        var noExcluded = Ids("VariantId");
        Add(new ErrorUnionType([err, err2]), $"errorUnionOf(ref c, for: in {sets2}, as: in {noExcluded})");
        Add(new ErrorUnionType([]), $"errorUnionOf(ref c, for: in {sets0}, as: in {noExcluded})");
        Add(new MapType(PrimitiveType.I32, PrimitiveType.String), $"internMap(ref c, by: {tI32}, to: {p[14]})");
        Add(new OwnedType(PrimitiveType.I32), $"internOwned(ref c, by: {tI32})");
        Add(new UntrustedType(new ListType(PrimitiveType.I32)), $"internUntrusted(ref c, by: {tListI32})");
        Add(new UntrustedType(PrimitiveType.I32), $"internUntrusted(ref c, by: {tI32})");
        var tf0 = Ids("TupleField");
        src[^1] = src[^1].Replace("    let ", "    var ");
        src.Add($"    {tf0}.append(TupleField {{ name: null, type: {tI32} }})");
        src.Add($"    {tf0}.append(TupleField {{ name: null, type: {tListI32} }})");
        Add(new TupleType([new TupleField(null, PrimitiveType.I32), new TupleField(null, new ListType(PrimitiveType.I32))]), $"internTuple(ref c, for: in {tf0})");
        var tf1 = Ids("TupleField");
        src[^1] = src[^1].Replace("    let ", "    var ");
        src.Add($"    {tf1}.append(TupleField {{ name: \"a\", type: {tI32} }})");
        src.Add($"    {tf1}.append(TupleField {{ name: \"b\", type: {p[3]} }})");
        Add(new TupleType([new TupleField("a", PrimitiveType.I32), new TupleField("b", PrimitiveType.I64)]), $"internTuple(ref c, for: in {tf1})");
        Add(BuiltinType.StrBuf, "builtinType(.strBuf)");
        Add(BuiltinType.Arena, "builtinType(.arena)");
        Add(plain, "structTypeOf(s0)");
        Add(holder, "structTypeOf(s1)");
        var tHandle = Add(handle, "structTypeOf(s2)");
        Add(nest, "structTypeOf(s3)");
        Add(node, "structTypeOf(s4)");
        Add(circle, "structTypeOf(s5)");
        Add(flag, "unionTypeOf(u0)");
        Add(msg, "unionTypeOf(u1)");
        var on = Ids("VariantId", "uv0");
        var onOff = Ids("VariantId", "uv0", "uv1");
        var off = Ids("VariantId", "uv1");
        var tPlainOn = Add(new StateType(plain, [flag.Variants[0]]), $"internState(ref c, by: s0, for: in {on})");
        Add(new StateType(plain, flag.Variants.ToList()), $"internState(ref c, by: s0, for: in {onOff})");
        Add(new StateType(holder, [flag.Variants[0]]), $"internState(ref c, by: s1, for: in {on})");
        Add(new StateType(plain, [flag.Variants[1]]), $"internState(ref c, by: s0, for: in {off})");
        Add(new StateType(nest, [flag.Variants[0]]), $"internState(ref c, by: s3, for: in {on})");
        Add(shape, "interfaceTypeOf(i0)");
        Add(new DynRefType(shape), "internDynRef(ref c, by: i0, readOnly: false)");
        Add(new DynRefType(shape, readOnly: true), "internDynRef(ref c, by: i0, readOnly: true)");
        Add(tpT, "typeParamTypeOf(tp0)");
        Add(tpU, "typeParamTypeOf(tp1)");
        Add(new OptionalType(plain), "internOptional(ref c, by: structTypeOf(s0))");
        Add(new OptionalType(new StateType(plain, [flag.Variants[0]])), $"internOptional(ref c, by: {tPlainOn})");
        Add(new OptionalType(new ListType(PrimitiveType.I32)), $"internOptional(ref c, by: {tListI32})");
        Add(new OwnedType(handle), $"internOwned(ref c, by: {tHandle})");
        Add(new ArrayType(handle, 2), $"internArray(ref c, by: {tHandle}, at: 2)");
        Add(new RangeType(PrimitiveType.I32), $"internRange(ref c, by: {tI32})");
        var noEffects = Ids("string");
        var fp1 = Ids("Type", tI32);
        Add(new FunctionType([PrimitiveType.I32], PrimitiveType.I32, [], []), $"internFunction(ref c, for: in {fp1}, to: {tI32}, by: in {sets0}, as: in {noEffects})");

        // prims: isWidening, implicit, explicit and common over every pair of primitives.
        var prim = expected["prims"] = [];
        src.Add("    print(\"== prims\")");
        src.Add($"    for i in 0..{prims.Length} {{");
        src.Add($"        for j in 0..{prims.Length} {{");
        src.Add("            let a = types[i]");
        src.Add("            let b = types[j]");
        src.Add("            let w = isWidening(a, to: b)");
        src.Add("            let k = kt(implicit(ref c, from: a, to: b))");
        src.Add("            let e = kt(explicit(ref c, from: a, to: b))");
        src.Add("            let m = tt(ref c, by: common(ref c, for: a, as: b))");
        src.Add("            print($\"{i} {j} {w} {k} {e} {m}\")");
        src.Add("        }");
        src.Add("    }");
        for (var i = 0; i < prims.Length; i++)
            for (var j = 0; j < prims.Length; j++)
                prim.Add($"{i} {j} {B(Conversions.IsWidening(prims[i], prims[j]))} {K(Conversions.Implicit(prims[i], prims[j]))} {K(Conversions.Explicit(prims[i], prims[j]))} {T(Conversions.Common(prims[i], prims[j]))}");

        // pairs: implicit (plain and as an argument), explicit and common over every pair of types.
        var pairs = expected["pairs"] = [];
        src.Add("    print(\"== pairs\")");
        src.Add("    for i in 0..types.length {");
        src.Add("        for j in 0..types.length {");
        src.Add("            let a = types[i]");
        src.Add("            let b = types[j]");
        src.Add("            let k = kt(implicit(ref c, from: a, to: b))");
        src.Add("            let g = kt(implicit(ref c, from: a, to: b, argument: true))");
        src.Add("            let e = kt(explicit(ref c, from: a, to: b))");
        src.Add("            let m = tt(ref c, by: common(ref c, for: a, as: b))");
        src.Add("            print($\"{i} {j} {k} {g} {e} {m}\")");
        src.Add("        }");
        src.Add("    }");
        for (var i = 0; i < types.Count; i++)
            for (var j = 0; j < types.Count; j++)
                pairs.Add($"{i} {j} {K(Conversions.Implicit(types[i], types[j]))} {K(Conversions.Implicit(types[i], types[j], argument: true))} {K(Conversions.Explicit(types[i], types[j]))} {T(Conversions.Common(types[i], types[j]))}");

        // unique: isUnique, uniquePart and whyUnique over every type.
        var unique = expected["unique"] = [];
        src.Add("    print(\"== unique\")");
        src.Add("    for i in 0..types.length {");
        src.Add("        let u = isUnique(ref c, for: types[i])");
        src.Add("        let part = tt(ref c, by: uniquePart(ref c, for: types[i]))");
        src.Add("        let why = st(whyUnique(ref c, for: types[i]))");
        src.Add("        print($\"{i} {u} {part} {why}\")");
        src.Add("    }");
        for (var i = 0; i < types.Count; i++)
            unique.Add($"{i} {B(TypeFacts.IsUnique(types[i]))} {T(TypeFacts.UniquePart(types[i]))} {TypeFacts.WhyUnique(types[i]) ?? "-"}");

        GenerateFunctions(src, expected, Let, Ids, mod, err, err2, tI32, p[3]);
        GenerateFolding(src, expected, types, prims.Length);
        GenerateEvaluate(src, expected, Let, plain, holder, p);

        src.Add("    return 0");
        src.Add("}");
        return new Generated(string.Join("\n", src) + "\n", expected);
    }

    /// <summary>functions: <c>implicitFunction</c> for each function against each function type.</summary>
    private static void GenerateFunctions(List<string> src, Dictionary<string, List<string>> expected, Func<string, string> let,
        Func<string, string[], string> ids, ModuleSymbol mod, ErrorSetSymbol err, ErrorSetSymbol err2, string tI32, string tI64)
    {
        var i32 = PrimitiveType.I32;
        var i64 = PrimitiveType.I64;
        var fnTypes = new List<FunctionType>();
        src.Add("    var fnTypes = List<Type>()");
        void FnType(TypeSymbol[] ps, TypeSymbol ret, ErrorSetSymbol[] fails)
        {
            fnTypes.Add(new FunctionType(ps, ret, fails, []));
            var pl = ids("Type", ps.Select(t => t == i32 ? tI32 : tI64).ToArray());
            var fl = ids("ErrorSetId", fails.Select(f => f == err ? "es0" : "es1").ToArray());
            var ul = ids("string", []);
            src.Add($"    fnTypes.append(internFunction(ref c, for: in {pl}, to: {(ret == i32 ? tI32 : tI64)}, by: in {fl}, as: in {ul}))");
        }
        FnType([i32], i32, []);
        FnType([i64], i32, []);
        FnType([i32], i32, [err]);
        FnType([i32], i64, []);
        FnType([i32, i32], i32, []);
        FnType([i32], i32, [err2]);
        FnType([i32], i32, [err, err2]);

        var fns = new List<FunctionSymbol>();
        src.Add("    var fns = List<FuncId>()");
        void Fn(ParamModifier[] mods, bool fails, ErrorSetSymbol[] sets, bool excluded)
        {
            var f = new FunctionSymbol($"f{fns.Count}", null, mod, FunctionKind.Function) { ReturnType = i32, Fails = fails };
            var fid = let($"newFunction(ref c, by: FunctionSymbol {{ name: \"f{fns.Count}\", returnType: {tI32}, hasFails: {B(fails)} }})");
            for (var i = 0; i < mods.Length; i++)
            {
                f.Parameters.Add(new ParameterSymbol($"p{i}", null, i32, mods[i], i));
                var m = mods[i] == ParamModifier.Ref ? ".refKeyword" : ".none";
                var pid = let($"newLocal(ref c, by: LocalSymbol {{ name: \"p{i}\", type: {tI32}, isParameter: true, modifier: {m}, ordinal: {i} }})");
                src.Add($"    c.functions[{fid}].parameters.append({pid})");
            }
            foreach (var s in sets)
            {
                f.ErrorSets.Add(s);
                src.Add($"    c.functions[{fid}].errorSets.append({(s == err ? "es0" : "es1")})");
            }
            if (excluded)
            {
                f.ExcludedErrors.Add(err.Variants[0]);
                src.Add($"    c.functions[{fid}].excludedErrors.append(ev0)");
            }
            fns.Add(f);
            src.Add($"    fns.append({fid})");
        }
        var none = ParamModifier.None;
        Fn([none], false, [], false);
        Fn([ParamModifier.Ref], false, [], false);
        Fn([none], true, [err], false);
        Fn([none], true, [err], true);
        Fn([none], true, [err2], false);
        Fn([none, none], false, [], false);
        Fn([none], true, [err2, err], false);
        Fn([none], true, [], false);
        Fn([none], false, [err], false);

        var lines = expected["functions"] = [];
        src.Add("    print(\"== functions\")");
        src.Add("    for i in 0..fns.length {");
        src.Add("        for j in 0..fnTypes.length {");
        src.Add("            print($\"{i} {j} {kt(implicitFunction(ref c, from: fns[i], to: fnTypes[j]))}\")");
        src.Add("        }");
        src.Add($"        print($\"{{i}} - {{kt(implicitFunction(ref c, from: fns[i], to: {tI32}))}}\")");
        src.Add("    }");
        for (var i = 0; i < fns.Count; i++)
        {
            for (var j = 0; j < fnTypes.Count; j++) lines.Add($"{i} {j} {K(Conversions.Implicit(fns[i], fnTypes[j]))}");
            lines.Add($"{i} - -");
        }
    }

    /// <summary>unary, binary, cast: seeded constants through <c>foldUnary</c>, <c>foldBinary</c> and <c>foldCast</c>.</summary>
    private static void GenerateFolding(List<string> src, Dictionary<string, List<string>> expected, List<TypeSymbol> types, int primCount)
    {
        var rng = new Random(6);
        var ints = new List<Int128> { 0, 1, -1, 2, 3, 7, -8, 31, 63, 64, 127, 128, -129, 255, 65535, -32768, 0x10FFFF, 0xD800, 2147483647,
            -2147483648L, 4294967296L, long.MaxValue, long.MinValue, ulong.MaxValue };
        for (var i = 0; i < 4; i++) ints.Add(rng.NextInt64(long.MinValue, long.MaxValue));
        var values = new List<ConstantValue>();
        values.AddRange(ints.Select(n => new ConstantValue(n)));
        foreach (var d in new[] { 0.0, -0.0, 1.5, -1.5, 2.5, 7.0, 1e20, -1e20, 3e9, 255.9, -128.7, 1e300, 0.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            values.Add(new ConstantValue(d));
        values.Add(new ConstantValue(rng.NextDouble() * 1e6));
        values.Add(new ConstantValue(true));
        values.Add(new ConstantValue(false));
        foreach (var r in new[] { 97, 98, 0x1F600 }) values.Add(new ConstantValue(new Rune(r)));
        foreach (var s in new[] { "a", "b", "" }) values.Add(new ConstantValue(s));

        src.Add("    var vals = List<Constant>()");
        foreach (var v in values) src.Add($"    vals.append({ConstLit(v)})");

        // unary: every operator over every value, with a type that cycles through the primitives.
        var unary = expected["unary"] = [];
        src.Add("    print(\"== unary\")");
        src.Add("    for op in 0..5 {");
        src.Add("        for i in 0..vals.length {");
        src.Add($"            let t = types[(op + i) % {primCount}]");
        src.Add("            let r = foldUnary(ref c, for: parser.UnaryOp.fromOrdinal(op) ?? .plus, by: in vals[i], as: t)");
        src.Add("            print($\"{op} {i} {ct(r)}\")");
        src.Add("        }");
        src.Add("    }");
        for (var op = 0; op < 5; op++)
            for (var i = 0; i < values.Count; i++)
                unary.Add($"{op} {i} {C(Fold("Unary", (UnaryOp)op, values[i], types[(op + i) % primCount]))}");

        // binary: every operator over same-kind pairs and every seventh mixed pair.
        var binary = expected["binary"] = [];
        var kinds = values.Select(v => v.Value.GetType()).ToList();
        src.Add("    print(\"== binary\")");
        src.Add("    for op in 0..25 {");
        src.Add("        for i in 0..vals.length {");
        src.Add("            for j in 0..vals.length {");
        src.Add("                if vals[i].kind != vals[j].kind && (i + j) % 7 != 0 { continue }");
        src.Add($"                let t = types[(op + i + j) % {primCount}]");
        src.Add("                let r = foldBinary(ref c, for: parser.BinaryOp.fromOrdinal(op) ?? .coalesce, from: in vals[i], by: in vals[j], as: t)");
        src.Add("                print($\"{op} {i} {j} {ct(r)}\")");
        src.Add("            }");
        src.Add("        }");
        src.Add("    }");
        for (var op = 0; op < 25; op++)
            for (var i = 0; i < values.Count; i++)
                for (var j = 0; j < values.Count; j++)
                {
                    if (kinds[i] != kinds[j] && (i + j) % 7 != 0) continue;
                    binary.Add($"{op} {i} {j} {C(Fold("Binary", (BinaryOp)op, values[i], values[j], types[(op + i + j) % primCount]))}");
                }

        // cast: every value to every type, from the target itself or from another type.
        var cast = expected["cast"] = [];
        src.Add("    print(\"== cast\")");
        src.Add("    for i in 0..vals.length {");
        src.Add("        for j in 0..types.length {");
        src.Add("            let from = (i + j) % 3 == 0 ? types[j] : types[(i * 7 + j) % types.length]");
        src.Add("            let r = foldCast(ref c, for: in vals[i], from: from, to: types[j])");
        src.Add("            print($\"{i} {j} {ct(r)}\")");
        src.Add("        }");
        src.Add("    }");
        for (var i = 0; i < values.Count; i++)
            for (var j = 0; j < types.Count; j++)
            {
                var from = (i + j) % 3 == 0 ? types[j] : types[(i * 7 + j) % types.Count];
                cast.Add($"{i} {j} {C(Fold("Cast", values[i], from, types[j]))}");
            }
    }

    /// <summary>One expression for <c>evaluate</c>: the C# node and the Bayan handle's name.</summary>
    private sealed record E(BoundExpr Cs, string Byn);

    /// <summary>evaluate: hand-built bound trees over known and unknown locals and fields.</summary>
    private static void GenerateEvaluate(List<string> src, Dictionary<string, List<string>> expected, Func<string, string> let,
        StructSymbol plain, StructSymbol holder, string[] p)
    {
        var i32 = PrimitiveType.I32;
        string TypeName(TypeSymbol t) => t switch
        {
            _ when t == PrimitiveType.I32 => p[2],
            _ when t == PrimitiveType.I64 => p[3],
            _ when t == PrimitiveType.U8 => p[4],
            _ when t == PrimitiveType.F64 => p[11],
            _ when t == PrimitiveType.Bool => p[12],
            _ when t == plain => "structTypeOf(s0)",
            _ => throw new InvalidOperationException(t.DisplayName),
        };
        var localNames = new Dictionary<LocalSymbol, string>();
        LocalSymbol Local(string name, TypeSymbol type)
        {
            var l = new LocalSymbol(name, null, type, LocalKind.Let);
            localNames[l] = let($"newLocal(ref c, by: LocalSymbol {{ name: \"{name}\", type: {TypeName(type)} }})");
            return l;
        }
        var five = Local("five", i32);
        var big = Local("big", i32);
        var unknown = Local("unknown", i32);
        var unknownFlag = Local("unknownFlag", PrimitiveType.Bool);
        string LocalName(LocalSymbol l) => localNames[l];
        var locals = new Dictionary<LocalSymbol, ConstantValue> { [five] = new((Int128)5), [big] = new((Int128)300) };
        src.Add("    var locals: Map<LocalId, Constant> = []");
        src.Add($"    locals[{LocalName(five)}] = {ConstLit(locals[five])}");
        src.Add($"    locals[{LocalName(big)}] = {ConstLit(locals[big])}");
        var fields = new Dictionary<FieldSymbol, ConstantValue> { [plain.Fields[0]] = new((Int128)7) };
        src.Add("    var fields: Map<FieldId, Constant> = []");
        src.Add($"    fields[c.structs[s0].fields[0]] = {ConstLit(fields[plain.Fields[0]])}");

        E Lit(ConstantValue v, TypeSymbol t)
        {
            var h = let($"addLiteral(ref c, from: BLiteral {{ span: span0, type: {TypeName(t)}, value: {ConstLit(v)} }})");
            src.Add($"    setConstant(ref c, for: {h}, to: {ConstLit(v)})");
            return new(new BoundLiteral(null!, t, v) { Constant = v }, h);
        }
        E Int(int n) => Lit(new ConstantValue((Int128)n), i32);
        E Flag(bool b) => Lit(new ConstantValue(b), PrimitiveType.Bool);
        E Loc(LocalSymbol l) => new(new BoundLocal(null!, l), let($"addLocal(ref c, from: BLocal {{ span: span0, local: {LocalName(l)} }})"));
        E This() => new(new BoundThis(null!, plain), let("addThis(ref c, from: BThis { span: span0, type: structTypeOf(s0) })"));
        E Field(E receiver, StructSymbol s, int index) =>
            new(new BoundField(null!, receiver.Cs, s.Fields[index]),
                let($"addField(ref c, from: BField {{ span: span0, receiver: {receiver.Byn}, field: c.structs[{(s == plain ? "s0" : "s1")}].fields[{index}], type: {TypeName(s.Fields[index].Type)} }})"));
        E Conv(E operand, TypeSymbol t, ConversionKind k, string bynKind) =>
            new(new BoundConversion(null!, operand.Cs, t, k),
                let($"addConversion(ref c, from: BConversion {{ span: span0, operand: {operand.Byn}, type: {TypeName(t)}, kind: .{bynKind} }})"));
        E Un(UnaryOp op, string bynOp, E operand, TypeSymbol t) =>
            new(new BoundUnary(null!, op, operand.Cs, t),
                let($"addUnary(ref c, from: BUnary {{ span: span0, op: .{bynOp}, operand: {operand.Byn}, type: {TypeName(t)} }})"));
        E Bin(BinaryOp op, string bynOp, E l, E r, TypeSymbol t, ConstantValue? constant = null)
        {
            var h = let($"addBinary(ref c, from: BBinary {{ span: span0, op: .{bynOp}, left: {l.Byn}, right: {r.Byn}, type: {TypeName(t)} }})");
            if (constant is not null) src.Add($"    setConstant(ref c, for: {h}, to: {ConstLit(constant)})");
            return new(new BoundBinary(null!, op, l.Cs, r.Cs, t) { Constant = constant }, h);
        }
        E IsRange(E value, E start, E end, bool inclusive)
        {
            var pat = let($"addRangePattern(ref c, from: BRangePattern {{ span: span0, inputType: {TypeName(i32)}, start: {start.Byn}, end: {end.Byn}, inclusive: {B(inclusive)} }})");
            return new(new BoundIs(null!, value.Cs, new BoundRangePattern(null!, i32, start.Cs, end.Cs, inclusive)),
                let($"addIs(ref c, from: BIs {{ span: span0, value: {value.Byn}, pattern: {pat} }})"));
        }

        var cases = new List<E>
        {
            Int(3),
            Loc(five),
            Loc(unknown),
            Bin(BinaryOp.Add, "add", Loc(five), Int(4), i32),
            Bin(BinaryOp.Mul, "mul", Loc(unknown), Int(1), i32),
            Bin(BinaryOp.Mul, "mul", Int(1), Loc(unknown), i32),
            Bin(BinaryOp.Lt, "lt", Loc(five), Loc(big), PrimitiveType.Bool),
            Bin(BinaryOp.Div, "div", Loc(big), Int(0), i32),
            Bin(BinaryOp.LogicalOr, "logicalOr", Flag(true), Loc(unknownFlag), PrimitiveType.Bool),
            Bin(BinaryOp.LogicalAnd, "logicalAnd", Flag(false), Loc(unknownFlag), PrimitiveType.Bool),
            Bin(BinaryOp.LogicalAnd, "logicalAnd", Flag(true), Flag(false), PrimitiveType.Bool),
            Bin(BinaryOp.LogicalOr, "logicalOr", Flag(false), Flag(true), PrimitiveType.Bool),
            Bin(BinaryOp.LogicalAnd, "logicalAnd", Loc(unknownFlag), Flag(true), PrimitiveType.Bool),
            Bin(BinaryOp.LogicalOr, "logicalOr", Int(1), Flag(true), PrimitiveType.Bool),
            Bin(BinaryOp.Add, "add", Loc(unknown), Loc(unknown), i32, new ConstantValue((Int128)42)),
            Field(This(), plain, 0),
            Field(This(), holder, 0),
            Field(Loc(five), plain, 0),
            Conv(Loc(big), PrimitiveType.U8, ConversionKind.NumericCast, "numericCast"),
            Conv(Loc(five), PrimitiveType.I64, ConversionKind.Widen, "widen"),
            Conv(Loc(five), PrimitiveType.F64, ConversionKind.NumericCast, "numericCast"),
            Conv(Loc(five), i32, ConversionKind.Identity, "identity"),
            Conv(Loc(five), PrimitiveType.I64, ConversionKind.ToOptional, "toOptional"),
            Conv(Loc(unknown), PrimitiveType.I64, ConversionKind.Widen, "widen"),
            Un(UnaryOp.Negate, "negate", Loc(five), i32),
            Un(UnaryOp.Complement, "complement", Loc(five), PrimitiveType.U8),
            Un(UnaryOp.Not, "notKeyword", Flag(false), PrimitiveType.Bool),
            Un(UnaryOp.Negate, "negate", Loc(unknown), i32),
            IsRange(Loc(five), Int(1), Int(5), true),
            IsRange(Loc(five), Int(1), Int(5), false),
            IsRange(Loc(five), Int(6), Int(9), true),
            IsRange(Loc(unknown), Int(1), Int(5), true),
            IsRange(Loc(five), Loc(unknown), Int(5), true),
            IsRange(Loc(five), Int(1), Loc(unknown), true),
            IsRange(Flag(true), Int(1), Int(5), true),
            This(),
        };
        var lines = expected["evaluate"] = [];
        src.Add("    print(\"== evaluate\")");
        for (var i = 0; i < cases.Count; i++)
        {
            src.Add($"    print($\"{i} {{ct(evaluate(ref c, for: {cases[i].Byn}, from: in locals, by: in fields))}}\")");
            lines.Add($"{i} {C(Fold("Evaluate", cases[i].Cs, locals, fields))}");
        }
    }
}
