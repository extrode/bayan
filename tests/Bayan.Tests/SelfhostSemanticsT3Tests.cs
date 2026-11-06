using System.Globalization;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;
using ArrayType = Bayan.Semantics.Symbols.ArrayType;
using TupleType = Bayan.Semantics.Symbols.TupleType;

namespace Bayan.Tests;

/// <summary>
/// T3: the self-hosted symbols and types (<c>selfhost/semantics/symbols.byn</c> and
/// <c>types.byn</c>) against a C# oracle over the same symbols and types. One generated
/// <c>main.byn</c> reconstructs the symbols by hand and prints labelled sections; the C# oracle
/// builds the same symbols and types and emits the same sections, and each test compares one.
/// </summary>
public sealed class SelfhostSemanticsT3Tests
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string SemanticsDir => Path.Combine(SelfhostDir, "semantics");

    /// <summary>The generated entry module, its expected lines, and each section's boundaries.</summary>
    private sealed record Generated(
        string Source, List<string> Expected, int DisplayStart, int DisplayCount, int InternStart, int InternCount,
        int RangeStart, int RangeCount, int KeyStart, int KeyCount, int LookupStart, int LookupCount);

    private static readonly Lazy<Generated> Cases = new(Generate);

    /// <summary>The built program's output lines, or the compiler's complaint (T5's build shape).</summary>
    private static readonly Lazy<(string[] Lines, string Error)> Executed = new(() =>
    {
        var sources = Directory.GetFiles(SemanticsDir, "*.byn")
            .Where(path => Path.GetFileName(path) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(SemanticsDir, "main.byn"), Cases.Value.Source));
        var (program, error) = Build(sources);
        if (program is null) return ([], error);
        var (exit, output, stderr) = SelfhostParserTests.Run(program);
        if (exit != 0 || stderr != "") return ([], $"exit {exit}: {stderr}");
        return (output.Split('\n', StringSplitOptions.RemoveEmptyEntries), "");
    });

    /// <summary>The whole <c>selfhost/semantics</c> module with a generated <c>main</c>, built once.</summary>
    private static (string? Path, string Error) Build(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    [Fact]
    public void DisplayNamesMatchCSharp()
    {
        Assert.True(Cases.Value.Expected.Count >= 600, $"only {Cases.Value.Expected.Count} printed lines");
        Compare("display", Cases.Value.DisplayStart, Cases.Value.DisplayCount);
    }

    [Fact]
    public void InterningIsCanonical() => Compare("interning", Cases.Value.InternStart, Cases.Value.InternCount);

    [Fact]
    public void PrimitiveRangesMatchCSharp() => Compare("ranges", Cases.Value.RangeStart, Cases.Value.RangeCount);

    [Fact]
    public void KeysAndOccursMatchCSharp() => Compare("keys", Cases.Value.KeyStart, Cases.Value.KeyCount);

    [Fact]
    public void SymbolLookupsMatchCSharp() => Compare("lookups", Cases.Value.LookupStart, Cases.Value.LookupCount);

    private static void Compare(string section, int start, int count)
    {
        var cases = Cases.Value;
        var (lines, error) = Executed.Value;
        Assert.True(error.Length == 0, error);
        Assert.True(lines.Length == cases.Expected.Count, $"{section}: printed {lines.Length} lines, expected {cases.Expected.Count}");
        for (var i = 0; i < count; i++)
        {
            var at = start + i;
            if (lines[at] != cases.Expected[at])
                Assert.Fail($"{section}: case {i} (line {at + 1}):\n expected: {cases.Expected[at]}\n   actual: {lines[at]}");
        }
    }

    // ── The oracle's helpers ─────────────────────────────────────────────────────

    private static string B(bool value) => value ? "true" : "false";

    private static string Display(TypeSymbol t, bool qualify)
    {
        var field = typeof(TypeSymbol).GetField("QualifyModules",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var old = (bool)field.GetValue(null)!;
        field.SetValue(null, qualify);
        try { return t.DisplayName; }
        finally { field.SetValue(null, old); }
    }

    private static string IpText(TypeSymbol t)
    {
        var p = MapType.InvalidKeyPart(t);
        return p is null ? "-" : $"{p.Value.Part} {p.Value.Type.DisplayName}";
    }

    private static ModuleSymbol Mod(string name) => new(name, "");

    private static StructSymbol Struct(string name, ModuleSymbol mod, params (string Name, TypeSymbol Type)[] fields)
    {
        var lines = string.Join("\n", fields.Select(f => $"    i32 {f.Name}"));
        var src = SourceText.FromString("<t>", $"struct {name} {{\n{lines}\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (StructDecl)tree.Root.Members[0];
        var s = new StructSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl, new FileContext(mod, tree));
        for (var i = 0; i < fields.Length; i++)
            s.Fields.Add(new FieldSymbol(fields[i].Name, new Location(src, ((FieldDecl)decl.Members[i]).Name.Span), s, Accessibility.Private, (FieldDecl)decl.Members[i]) { Type = fields[i].Type });
        return s;
    }

    private static StructSymbol NoEqualityStruct(string name, ModuleSymbol mod, string field, TypeSymbol type)
    {
        var src = SourceText.FromString("<t>", $"@noEquality\nstruct {name} {{\n    i32 {field}\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (StructDecl)tree.Root.Members[0];
        var s = new StructSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl, new FileContext(mod, tree));
        s.Fields.Add(new FieldSymbol(field, new Location(src, ((FieldDecl)decl.Members[0]).Name.Span), s, Accessibility.Private, (FieldDecl)decl.Members[0]) { Type = type });
        return s;
    }

    private static EnumSymbol Enum(string name, ModuleSymbol mod, params string[] members)
    {
        var lines = string.Join(",\n", members.Select(m => $"    {m}"));
        var src = SourceText.FromString("<t>", $"enum {name} {{\n{lines},\n}}\n");
        var tree = SyntaxTree.Parse(src);
        var decl = (EnumDecl)tree.Root.Members[0];
        var e = new EnumSymbol(name, new Location(src, decl.Name.Span), mod, Accessibility.Public, decl, new FileContext(mod, tree));
        foreach (var m in decl.Members)
            e.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(src, m.Name.Span), e, e.Members.Count, null));
        return e;
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
        var source = new List<string>();
        var expected = new List<string>();

        // The oracle's symbols; the generated `main` rebuilds the same ones by hand.
        var modA = Mod("a");
        var modB = Mod("b");
        var tp = new TypeParameterType("T");
        var pointA = Struct("Point", modA, ("x", PrimitiveType.I32), ("y", PrimitiveType.I32));
        pointA.Methods.Add(new FunctionSymbol("area", null, modA, FunctionKind.Method) { Owner = pointA });
        var box = Struct("Box", modA, ("value", tp));
        box.TypeParameters.Add(tp);
        var pointB = Struct("Point", modB, ("z", PrimitiveType.I64));
        var holder = Struct("Holder", modA, ("data", new OwnedType(PrimitiveType.I32)));
        var noEq = NoEqualityStruct("NoEq", modA, "x", PrimitiveType.I32);
        var show = Interface("Show", modA);
        show.TypeParameters.Add(tp);
        var color = Enum("Color", modA, "red", "green", "blue");
        var shape = Union("Shape", modA, "circle(f32 r)", "square(i64 s)");
        shape.TypeParameters.Add(tp);
        var errA = ErrorSet("Err", modA, "overflow");
        var errB = ErrorSet("Err", modB, "timeout");
        var mathError = ErrorSet("MathError", modA, "overflow", "underflow");

        // Bayan source: helpers first, then main.
        source.Add("// generated by SelfhostSemanticsT3Tests");
        source.Add("string ipText(ref Ctx c, by Type t) {");
        source.Add("    let p = invalidKeyPart(ref c, by: t)");
        source.Add("    if (p is { } ip) { return $\"{ip.part} {typeDisplay(ref c, by: ip.type)}\" }");
        source.Add("    return \"-\"");
        source.Add("}");
        source.Add("string byNameText(ref Ctx c, by Type? t) {");
        source.Add("    if (t is { } x) { return typeDisplay(ref c, by: x) }");
        source.Add("    return \"null\"");
        source.Add("}");
        source.Add("i32 main() {");
        source.Add("    var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }");
        source.Add("    c.modules.append(Mod { name: \"a\", files: List<FileId>() })");
        source.Add("    c.modules.append(Mod { name: \"b\", files: List<FileId>() })");

        // Symbols.
        source.Add("    let s0 = newStruct(ref c, by: StructSymbol { name: \"Point\", module: 0 })");
        source.Add("    let f0 = newField(ref c, by: FieldSymbol { name: \"x\", owner: s0, type: prim(.i32) })");
        source.Add("    c.structs[s0].fields.append(f0)");
        source.Add("    let f1 = newField(ref c, by: FieldSymbol { name: \"y\", owner: s0, type: prim(.i32) })");
        source.Add("    c.structs[s0].fields.append(f1)");
        source.Add("    let m0 = newFunction(ref c, by: FunctionSymbol { name: \"area\", owner: s0, kind: .method })");
        source.Add("    c.structs[s0].methods.append(m0)");
        source.Add("    let tp0 = newTypeParam(ref c, by: TypeParam { name: \"T\" })");
        source.Add("    let s1 = newStruct(ref c, by: StructSymbol { name: \"Box\", module: 0 })");
        source.Add("    c.structs[s1].typeParams.append(tp0)");
        source.Add("    let f2 = newField(ref c, by: FieldSymbol { name: \"value\", owner: s1, type: typeParamTypeOf(tp0) })");
        source.Add("    c.structs[s1].fields.append(f2)");
        source.Add("    let s2 = newStruct(ref c, by: StructSymbol { name: \"Point\", module: 1 })");
        source.Add("    let f3 = newField(ref c, by: FieldSymbol { name: \"z\", owner: s2, type: prim(.i64) })");
        source.Add("    c.structs[s2].fields.append(f3)");
        source.Add("    let s3 = newStruct(ref c, by: StructSymbol { name: \"Holder\", module: 0 })");
        source.Add("    let f4 = newField(ref c, by: FieldSymbol { name: \"data\", owner: s3, type: internOwned(ref c, by: prim(.i32)) })");
        source.Add("    c.structs[s3].fields.append(f4)");
        source.Add("    let s4 = newStruct(ref c, by: StructSymbol { name: \"NoEq\", module: 0 })");
        source.Add("    c.structs[s4].attributes.append(\"noEquality\")");
        source.Add("    let f5 = newField(ref c, by: FieldSymbol { name: \"x\", owner: s4, type: prim(.i32) })");
        source.Add("    c.structs[s4].fields.append(f5)");
        source.Add("    let i0 = newInterface(ref c, by: InterfaceSymbol { name: \"Show\", module: 0 })");
        source.Add("    c.interfaces[i0].typeParams.append(tp0)");
        source.Add("    let e0 = newEnum(ref c, by: EnumSymbol { name: \"Color\", module: 0 })");
        source.Add("    let em0 = newEnumMember(ref c, by: EnumMemberSymbol { name: \"red\", owner: e0, ordinal: 0 })");
        source.Add("    c.enums[e0].members.append(em0)");
        source.Add("    let em1 = newEnumMember(ref c, by: EnumMemberSymbol { name: \"green\", owner: e0, ordinal: 1 })");
        source.Add("    c.enums[e0].members.append(em1)");
        source.Add("    let em2 = newEnumMember(ref c, by: EnumMemberSymbol { name: \"blue\", owner: e0, ordinal: 2 })");
        source.Add("    c.enums[e0].members.append(em2)");
        source.Add("    let u0 = newUnion(ref c, by: UnionSymbol { name: \"Shape\", module: 0 })");
        source.Add("    c.unions[u0].typeParams.append(tp0)");
        source.Add("    let uv0 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"circle\", owner: u0, tag: 0 })");
        source.Add("    c.unions[u0].variants.append(uv0)");
        source.Add("    let uv1 = newUnionVariant(ref c, by: UnionVariantSymbol { name: \"square\", owner: u0, tag: 1 })");
        source.Add("    c.unions[u0].variants.append(uv1)");
        source.Add("    let es0 = newErrorSet(ref c, by: ErrorSetSymbol { name: \"Err\", module: 0 })");
        source.Add("    let ev0 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"overflow\", set: es0, code: 1 })");
        source.Add("    c.errorSets[es0].variants.append(ev0)");
        source.Add("    let es1 = newErrorSet(ref c, by: ErrorSetSymbol { name: \"Err\", module: 1 })");
        source.Add("    let ev1 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"timeout\", set: es1, code: 1 })");
        source.Add("    c.errorSets[es1].variants.append(ev1)");
        source.Add("    let es2 = newErrorSet(ref c, by: ErrorSetSymbol { name: \"MathError\", module: 0 })");
        source.Add("    let ev2 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"overflow\", set: es2, code: 1 })");
        source.Add("    c.errorSets[es2].variants.append(ev2)");
        source.Add("    let ev3 = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"underflow\", set: es2, code: 2 })");
        source.Add("    c.errorSets[es2].variants.append(ev3)");

        // The type list, shared by display and keys.
        var types = new List<(TypeSymbol Cs, string Byn)>();
        var prims = new[] { PrimitiveType.I8, PrimitiveType.I16, PrimitiveType.I32, PrimitiveType.I64, PrimitiveType.U8, PrimitiveType.U16, PrimitiveType.U32, PrimitiveType.U64, PrimitiveType.Isize, PrimitiveType.Usize, PrimitiveType.F32, PrimitiveType.F64, PrimitiveType.Bool, PrimitiveType.Char, PrimitiveType.String, PrimitiveType.Void, PrimitiveType.Never };
        var primNames = new[] { "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64", "isize", "usize", "f32", "f64", "bool", "char", "string", "void", "neverKind" };
        for (var i = 0; i < prims.Length; i++) types.Add((prims[i], $"prim(.{primNames[i]})"));
        types.Add((ErrorType.Instance, "errorType()"));
        types.Add((NullType.Instance, "nullType()"));
        var textViews = new[] { TextViewType.Runes, TextViewType.Utf16, TextViewType.Lines, TextViewType.Parts };
        var textViewNames = new[] { "runes", "utf16", "lines", "parts" };
        for (var i = 0; i < textViews.Length; i++) types.Add((textViews[i], $"textViewType(.{textViewNames[i]})"));
        var builtins = new[] { BuiltinType.Arena, BuiltinType.StrBuf, BuiltinType.Io, BuiltinType.Hex, BuiltinType.Utf8, BuiltinType.IoStream, BuiltinType.IoInput, BuiltinType.ProcessResult, BuiltinType.Panic, BuiltinType.FormatSpec, BuiltinType.Symbol, BuiltinType.Path };
        var builtinNames = new[] { "arena", "strBuf", "io", "hex", "utf8", "ioStream", "ioInput", "processResult", "panic", "formatSpec", "symbol", "path" };
        for (var i = 0; i < builtins.Length; i++) types.Add((builtins[i], $"builtinType(.{builtinNames[i]})"));
        types.Add((pointA, "structTypeOf(s0)"));
        types.Add((box, "structTypeOf(s1)"));
        types.Add((pointB, "structTypeOf(s2)"));
        types.Add((holder, "structTypeOf(s3)"));
        types.Add((noEq, "structTypeOf(s4)"));
        types.Add((show, "interfaceTypeOf(i0)"));
        types.Add((color, "enumTypeOf(e0)"));
        types.Add((shape, "unionTypeOf(u0)"));
        types.Add((errA, "errorSetTypeOf(es0)"));
        types.Add((errB, "errorSetTypeOf(es1)"));
        types.Add((mathError, "errorSetTypeOf(es2)"));
        types.Add((tp, "typeParamTypeOf(tp0)"));

        // List locals for list-taking interns.
        source.Add("    var lstStates0 = List<VariantId>()");
        source.Add("    lstStates0.append(uv0)");
        source.Add("    var lstStates1 = List<VariantId>()");
        source.Add("    lstStates1.append(uv0)");
        source.Add("    lstStates1.append(uv1)");
        source.Add("    var lstSets01 = List<ErrorSetId>()");
        source.Add("    lstSets01.append(es0)");
        source.Add("    lstSets01.append(es1)");
        source.Add("    var lstSets02 = List<ErrorSetId>()");
        source.Add("    lstSets02.append(es0)");
        source.Add("    lstSets02.append(es2)");
        source.Add("    var lstSets20 = List<ErrorSetId>()");
        source.Add("    lstSets20.append(es2)");
        source.Add("    lstSets20.append(es0)");
        source.Add("    var lstExcl = List<VariantId>()");
        source.Add("    lstExcl.append(ev3)");
        source.Add("    let lstEmpty = List<VariantId>()");
        source.Add("    let lstEmptyUses = List<string>()");
        source.Add("    var lstParams0 = List<Type>()");
        source.Add("    lstParams0.append(prim(.i32))");
        source.Add("    lstParams0.append(prim(.i32))");
        source.Add("    var lstParams1 = List<Type>()");
        source.Add("    lstParams1.append(prim(.i32))");
        source.Add("    var lstFails0 = List<ErrorSetId>()");
        source.Add("    lstFails0.append(es2)");
        source.Add("    var lstUses0 = List<string>()");
        source.Add("    lstUses0.append(\"net\")");
        source.Add("    var lstTuple0 = List<TupleField>()");
        source.Add("    lstTuple0.append(TupleField { name: null, type: prim(.i32) })");
        source.Add("    lstTuple0.append(TupleField { name: null, type: prim(.string) })");
        source.Add("    var lstTuple1 = List<TupleField>()");
        source.Add("    lstTuple1.append(TupleField { name: \"key\", type: prim(.string) })");
        source.Add("    lstTuple1.append(TupleField { name: \"value\", type: prim(.i32) })");
        source.Add("    var lstTuple2 = List<TupleField>()");
        source.Add("    lstTuple2.append(TupleField { name: \"value\", type: prim(.i32) })");
        source.Add("    lstTuple2.append(TupleField { name: \"key\", type: prim(.string) })");
        source.Add("    var lstTuple3 = List<TupleField>()");
        source.Add("    lstTuple3.append(TupleField { name: null, type: typeParamTypeOf(tp0) })");
        source.Add("    lstTuple3.append(TupleField { name: \"n\", type: prim(.i32) })");
        source.Add("    var lstParamsT = List<Type>()");
        source.Add("    lstParamsT.append(typeParamTypeOf(tp0))");

        // Constructed types: 1-level over every primitive, then the mixed/nested ones.
        for (var i = 0; i < prims.Length; i++)
        {
            var p = $"prim(.{primNames[i]})";
            types.Add((new OptionalType(prims[i]), $"internOptional(ref c, by: {p})"));
            types.Add((new ListType(prims[i]), $"internList(ref c, by: {p})"));
            types.Add((new SpanType(prims[i]), $"internSpan(ref c, by: {p}, readOnly: false)"));
            types.Add((new ArrayType(prims[i], 3), $"internArray(ref c, by: {p}, at: 3)"));
        }
        types.Add((new SpanType(PrimitiveType.U8, readOnly: true), "internSpan(ref c, by: prim(.u8), readOnly: true)"));
        types.Add((new DynRefType(show), "internDynRef(ref c, by: i0, readOnly: false)"));
        types.Add((new DynRefType(show, readOnly: true), "internDynRef(ref c, by: i0, readOnly: true)"));
        types.Add((new MapType(PrimitiveType.String, PrimitiveType.I32), "internMap(ref c, by: prim(.string), to: prim(.i32))"));
        types.Add((new OwnedType(PrimitiveType.I32), "internOwned(ref c, by: prim(.i32))"));
        types.Add((new UntrustedType(PrimitiveType.U8), "internUntrusted(ref c, by: prim(.u8))"));
        types.Add((new RangeType(PrimitiveType.I64), "internRange(ref c, by: prim(.i64))"));
        types.Add((new ResultType(PrimitiveType.I32, mathError), "internResult(ref c, by: prim(.i32), to: errorSetTypeOf(es2))"));
        types.Add((new StateType(pointA, [shape.Variants[0]]), "internState(ref c, by: s0, for: in lstStates0)"));
        types.Add((new StateType(pointA, shape.Variants.ToList()), "internState(ref c, by: s0, for: in lstStates1)"));
        types.Add((ErrorUnionType.Of([errA, mathError]), "errorUnionOf(ref c, for: in lstSets02, as: in lstEmpty)"));
        types.Add((ErrorUnionType.Of([errA, mathError], [mathError.Variants[1]]), "errorUnionOf(ref c, for: in lstSets02, as: in lstExcl)"));
        types.Add((ErrorUnionType.Of([errA, errB]), "errorUnionOf(ref c, for: in lstSets01, as: in lstEmpty)"));
        types.Add((new TupleType([new TupleField(null, PrimitiveType.I32), new TupleField(null, PrimitiveType.String)]), "internTuple(ref c, for: in lstTuple0)"));
        types.Add((new TupleType([new TupleField("key", PrimitiveType.String), new TupleField("value", PrimitiveType.I32)]), "internTuple(ref c, for: in lstTuple1)"));
        types.Add((new FunctionType([PrimitiveType.I32, PrimitiveType.I32], PrimitiveType.Bool, [], []), "internFunction(ref c, for: in lstParams0, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)"));
        types.Add((new FunctionType([PrimitiveType.I32], PrimitiveType.Void, [mathError], ["net"]), "internFunction(ref c, for: in lstParams1, to: prim(.void), by: in lstFails0, as: in lstUses0)"));
        types.Add((new OptionalType(new ListType(PrimitiveType.I32)), "internOptional(ref c, by: internList(ref c, by: prim(.i32)))"));
        types.Add((new ListType(new OptionalType(PrimitiveType.I32)), "internList(ref c, by: internOptional(ref c, by: prim(.i32)))"));
        types.Add((new MapType(PrimitiveType.String, new ListType(PrimitiveType.I32)), "internMap(ref c, by: prim(.string), to: internList(ref c, by: prim(.i32)))"));
        types.Add((new SpanType(new OptionalType(PrimitiveType.U8), readOnly: true), "internSpan(ref c, by: internOptional(ref c, by: prim(.u8)), readOnly: true)"));
        types.Add((new ArrayType(new ListType(PrimitiveType.I8), 4), "internArray(ref c, by: internList(ref c, by: prim(.i8)), at: 4)"));
        types.Add((new OwnedType(new SpanType(PrimitiveType.U8)), "internOwned(ref c, by: internSpan(ref c, by: prim(.u8), readOnly: false))"));
        types.Add((new RangeType(new OptionalType(PrimitiveType.I64)), "internRange(ref c, by: internOptional(ref c, by: prim(.i64)))"));
        types.Add((new OptionalType(new OptionalType(PrimitiveType.I32)), "internOptional(ref c, by: internOptional(ref c, by: prim(.i32)))"));
        types.Add((new MapType(pointA, PrimitiveType.I32), "internMap(ref c, by: structTypeOf(s0), to: prim(.i32))"));
        types.Add((new ListType(pointA), "internList(ref c, by: structTypeOf(s0))"));

        // More nested types (to reach 600+ printed lines in total).
        var nestedAtoms = new[] { PrimitiveType.I8, PrimitiveType.I16, PrimitiveType.I32, PrimitiveType.I64, PrimitiveType.U8, PrimitiveType.F64, PrimitiveType.String };
        var nestedNames = new[] { "i8", "i16", "i32", "i64", "u8", "f64", "string" };
        for (var i = 0; i < nestedAtoms.Length; i++)
        {
            var p = $"prim(.{nestedNames[i]})";
            var q = nestedAtoms[i];
            types.Add((new OptionalType(new ListType(q)), $"internOptional(ref c, by: internList(ref c, by: {p}))"));
            types.Add((new ListType(new OptionalType(q)), $"internList(ref c, by: internOptional(ref c, by: {p}))"));
            types.Add((new MapType(PrimitiveType.String, new OptionalType(q)), $"internMap(ref c, by: prim(.string), to: internOptional(ref c, by: {p}))"));
            types.Add((new SpanType(new ListType(q)), $"internSpan(ref c, by: internList(ref c, by: {p}), readOnly: false)"));
            types.Add((new ArrayType(new OptionalType(q), 2), $"internArray(ref c, by: internOptional(ref c, by: {p}), at: 2)"));
        }

        types.Add((new OptionalType(tp), "internOptional(ref c, by: typeParamTypeOf(tp0))"));
        types.Add((new SpanType(tp), "internSpan(ref c, by: typeParamTypeOf(tp0), readOnly: false)"));
        types.Add((new ListType(tp), "internList(ref c, by: typeParamTypeOf(tp0))"));
        types.Add((new MapType(tp, PrimitiveType.I32), "internMap(ref c, by: typeParamTypeOf(tp0), to: prim(.i32))"));
        types.Add((new ArrayType(tp, 2), "internArray(ref c, by: typeParamTypeOf(tp0), at: 2)"));
        types.Add((new ResultType(tp, errA), "internResult(ref c, by: typeParamTypeOf(tp0), to: errorSetTypeOf(es0))"));
        types.Add((new StateType(box, [shape.Variants[0]]), "internState(ref c, by: s1, for: in lstStates0)"));
        types.Add((new TupleType([new TupleField(null, tp), new TupleField("n", PrimitiveType.I32)]), "internTuple(ref c, for: in lstTuple3)"));
        types.Add((new FunctionType([tp], PrimitiveType.Bool, [], []), "internFunction(ref c, for: in lstParamsT, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)"));

        // Assign each type to a variable so the sections can share it.
        for (var i = 0; i < types.Count; i++)
            source.Add($"    let t{i} = {types[i].Byn}");

        // Section 1: display names, both qualify modes.
        var displayStart = expected.Count;
        for (var i = 0; i < types.Count; i++)
        {
            source.Add($"    print($\"d{i} {{typeDisplay(ref c, by: t{i})}}\")");
            expected.Add($"d{i} {Display(types[i].Cs, false)}");
        }
        source.Add("    c.qualifyModules = true");
        for (var i = 0; i < types.Count; i++)
        {
            source.Add($"    print($\"q{i} {{typeDisplay(ref c, by: t{i})}}\")");
            expected.Add($"q{i} {Display(types[i].Cs, true)}");
        }
        source.Add("    c.qualifyModules = false");
        var displayCount = expected.Count - displayStart;

        // Section 2: interning is canonical (equal parts ==, differing parts !=).
        var internStart = expected.Count;
        var pairs = new List<(TypeSymbol A, TypeSymbol B, string Ba, string Bb)>
        {
            (new OptionalType(PrimitiveType.I32), new OptionalType(PrimitiveType.I32), "internOptional(ref c, by: prim(.i32))", "internOptional(ref c, by: prim(.i32))"),
            (new OptionalType(PrimitiveType.I32), new OptionalType(PrimitiveType.I64), "internOptional(ref c, by: prim(.i32))", "internOptional(ref c, by: prim(.i64))"),
            (new SpanType(PrimitiveType.U8), new SpanType(PrimitiveType.U8), "internSpan(ref c, by: prim(.u8), readOnly: false)", "internSpan(ref c, by: prim(.u8), readOnly: false)"),
            (new SpanType(PrimitiveType.U8), new SpanType(PrimitiveType.U8, readOnly: true), "internSpan(ref c, by: prim(.u8), readOnly: false)", "internSpan(ref c, by: prim(.u8), readOnly: true)"),
            (new DynRefType(show), new DynRefType(show), "internDynRef(ref c, by: i0, readOnly: false)", "internDynRef(ref c, by: i0, readOnly: false)"),
            (new DynRefType(show), new DynRefType(show, readOnly: true), "internDynRef(ref c, by: i0, readOnly: false)", "internDynRef(ref c, by: i0, readOnly: true)"),
            (new ListType(PrimitiveType.I32), new ListType(PrimitiveType.I32), "internList(ref c, by: prim(.i32))", "internList(ref c, by: prim(.i32))"),
            (new ListType(PrimitiveType.I32), new ListType(PrimitiveType.I64), "internList(ref c, by: prim(.i32))", "internList(ref c, by: prim(.i64))"),
            (new MapType(PrimitiveType.String, PrimitiveType.I32), new MapType(PrimitiveType.String, PrimitiveType.I32), "internMap(ref c, by: prim(.string), to: prim(.i32))", "internMap(ref c, by: prim(.string), to: prim(.i32))"),
            (new MapType(PrimitiveType.String, PrimitiveType.I32), new MapType(PrimitiveType.String, PrimitiveType.I64), "internMap(ref c, by: prim(.string), to: prim(.i32))", "internMap(ref c, by: prim(.string), to: prim(.i64))"),
            (new OwnedType(PrimitiveType.I32), new OwnedType(PrimitiveType.I32), "internOwned(ref c, by: prim(.i32))", "internOwned(ref c, by: prim(.i32))"),
            (new OwnedType(PrimitiveType.I32), new OwnedType(PrimitiveType.I64), "internOwned(ref c, by: prim(.i32))", "internOwned(ref c, by: prim(.i64))"),
            (new UntrustedType(PrimitiveType.U8), new UntrustedType(PrimitiveType.U8), "internUntrusted(ref c, by: prim(.u8))", "internUntrusted(ref c, by: prim(.u8))"),
            (new UntrustedType(PrimitiveType.U8), new UntrustedType(PrimitiveType.I8), "internUntrusted(ref c, by: prim(.u8))", "internUntrusted(ref c, by: prim(.i8))"),
            (new ArrayType(PrimitiveType.I32, 3), new ArrayType(PrimitiveType.I32, 3), "internArray(ref c, by: prim(.i32), at: 3)", "internArray(ref c, by: prim(.i32), at: 3)"),
            (new ArrayType(PrimitiveType.I32, 3), new ArrayType(PrimitiveType.I32, 4), "internArray(ref c, by: prim(.i32), at: 3)", "internArray(ref c, by: prim(.i32), at: 4)"),
            (new RangeType(PrimitiveType.I64), new RangeType(PrimitiveType.I64), "internRange(ref c, by: prim(.i64))", "internRange(ref c, by: prim(.i64))"),
            (new RangeType(PrimitiveType.I64), new RangeType(PrimitiveType.I32), "internRange(ref c, by: prim(.i64))", "internRange(ref c, by: prim(.i32))"),
            (new ResultType(PrimitiveType.I32, mathError), new ResultType(PrimitiveType.I32, mathError), "internResult(ref c, by: prim(.i32), to: errorSetTypeOf(es2))", "internResult(ref c, by: prim(.i32), to: errorSetTypeOf(es2))"),
            (new ResultType(PrimitiveType.I32, mathError), new ResultType(PrimitiveType.I64, mathError), "internResult(ref c, by: prim(.i32), to: errorSetTypeOf(es2))", "internResult(ref c, by: prim(.i64), to: errorSetTypeOf(es2))"),
            (new StateType(pointA, [shape.Variants[0]]), new StateType(pointA, [shape.Variants[0]]), "internState(ref c, by: s0, for: in lstStates0)", "internState(ref c, by: s0, for: in lstStates0)"),
            (new StateType(pointA, [shape.Variants[0]]), new StateType(pointA, [shape.Variants[1]]), "internState(ref c, by: s0, for: in lstStates0)", "internState(ref c, by: s0, for: in lstStates1)"),
            (new TupleType([new TupleField(null, PrimitiveType.I32), new TupleField(null, PrimitiveType.String)]), new TupleType([new TupleField(null, PrimitiveType.I32), new TupleField(null, PrimitiveType.String)]), "internTuple(ref c, for: in lstTuple0)", "internTuple(ref c, for: in lstTuple0)"),
            (new TupleType([new TupleField("key", PrimitiveType.String), new TupleField("value", PrimitiveType.I32)]), new TupleType([new TupleField("value", PrimitiveType.I32), new TupleField("key", PrimitiveType.String)]), "internTuple(ref c, for: in lstTuple1)", "internTuple(ref c, for: in lstTuple2)"),
            (new FunctionType([PrimitiveType.I32, PrimitiveType.I32], PrimitiveType.Bool, [], []), new FunctionType([PrimitiveType.I32, PrimitiveType.I32], PrimitiveType.Bool, [], []), "internFunction(ref c, for: in lstParams0, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)", "internFunction(ref c, for: in lstParams0, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)"),
            (new FunctionType([PrimitiveType.I32, PrimitiveType.I32], PrimitiveType.Bool, [], []), new FunctionType([PrimitiveType.I32], PrimitiveType.Bool, [], []), "internFunction(ref c, for: in lstParams0, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)", "internFunction(ref c, for: in lstParams1, to: prim(.bool), by: in lstEmpty, as: in lstEmptyUses)"),
            (new FunctionType([PrimitiveType.I32], PrimitiveType.Void, [mathError], ["net"]), new FunctionType([PrimitiveType.I32], PrimitiveType.Void, [mathError], ["net"]), "internFunction(ref c, for: in lstParams1, to: prim(.void), by: in lstFails0, as: in lstUses0)", "internFunction(ref c, for: in lstParams1, to: prim(.void), by: in lstFails0, as: in lstUses0)"),
            (ErrorUnionType.Of([errA, mathError]), ErrorUnionType.Of([mathError, errA]), "errorUnionOf(ref c, for: in lstSets02, as: in lstEmpty)", "errorUnionOf(ref c, for: in lstSets20, as: in lstEmpty)"),
        };
        for (var i = 0; i < pairs.Count; i++)
        {
            source.Add($"    let ia{i} = {pairs[i].Ba}");
            source.Add($"    let ib{i} = {pairs[i].Bb}");
            source.Add($"    print($\"i{i} {{ia{i} == ib{i}}}\")");
            expected.Add($"i{i} {B(pairs[i].A.Equals(pairs[i].B))}");
        }
        var internCount = expected.Count - internStart;

        // Section 3: primitive ranges, names, bits and predicates.
        var rangeStart = expected.Count;
        var intKinds = new[] { PrimitiveType.I8, PrimitiveType.I16, PrimitiveType.I32, PrimitiveType.I64, PrimitiveType.Isize, PrimitiveType.U8, PrimitiveType.U16, PrimitiveType.U32, PrimitiveType.U64, PrimitiveType.Usize };
        var intNames = new[] { "i8", "i16", "i32", "i64", "isize", "u8", "u16", "u32", "u64", "usize" };
        for (var i = 0; i < intKinds.Length; i++)
        {
            source.Add($"    print($\"r{intNames[i]} {{bigText(range(.{intNames[i]}).min)}} {{bigText(range(.{intNames[i]}).max)}}\")");
            expected.Add($"r{intNames[i]} {intKinds[i].Range.Min.ToString(CultureInfo.InvariantCulture)} {intKinds[i].Range.Max.ToString(CultureInfo.InvariantCulture)}");
        }
        foreach (var key in PrimitiveType.ByName.Keys)
        {
            source.Add($"    print($\"n{key} {{byNameText(ref c, by: primitiveByName(\"{key}\"))}}\")");
            expected.Add($"n{key} {(PrimitiveType.ByName[key].DisplayName)}");
        }
        source.Add($"    print($\"nzz {{byNameText(ref c, by: primitiveByName(\"nope\"))}}\")");
        expected.Add("nzz null");
        for (var i = 0; i < prims.Length; i++)
        {
            source.Add($"    print($\"b{primNames[i]} {{primitiveBits(.{primNames[i]})}}\")");
            expected.Add($"b{primNames[i]} {prims[i].Bits}");
            source.Add($"    print($\"p{primNames[i]} {{isInteger(prim(.{primNames[i]}))}} {{isSignedInteger(prim(.{primNames[i]}))}} {{isUnsignedInteger(prim(.{primNames[i]}))}} {{isFloat(prim(.{primNames[i]}))}} {{isNumeric(prim(.{primNames[i]}))}}\")");
            expected.Add($"p{primNames[i]} {B(prims[i].IsInteger)} {B(prims[i].IsSignedInteger)} {B(prims[i].IsUnsignedInteger)} {B(prims[i].IsFloat)} {B(prims[i].IsNumeric)}");
        }
        var rangeCount = expected.Count - rangeStart;

        // Section 4: keys, occurs, isBorrow and invalidKeyPart over the whole type list, plus tupleIndexOf.
        var keyStart = expected.Count;
        for (var i = 0; i < types.Count; i++)
        {
            source.Add($"    print($\"k{i} {{isValidKey(ref c, by: t{i})}} {{occurs(ref c, by: t{i})}} {{isBorrow(ref c, by: t{i})}} {{ipText(ref c, by: t{i})}}\")");
            expected.Add($"k{i} {B(MapType.IsValidKey(types[i].Cs))} {B(TypeParameterType.Occurs(types[i].Cs))} {B(types[i].Cs.IsBorrow)} {IpText(types[i].Cs)}");
        }
        // tupleIndexOf for the two tuple types.
        var namedTuple = new TupleType([new TupleField("key", PrimitiveType.String), new TupleField("value", PrimitiveType.I32)]);
        source.Add("    print($\"x0 {tupleIndexOf(ref c, by: internTuple(ref c, for: in lstTuple1), for: \"key\")}\")");
        expected.Add($"x0 {namedTuple.IndexOf("key")}");
        source.Add("    print($\"x1 {tupleIndexOf(ref c, by: internTuple(ref c, for: in lstTuple1), for: \"item2\")}\")");
        expected.Add($"x1 {namedTuple.IndexOf("item2")}");
        source.Add("    print($\"x2 {tupleIndexOf(ref c, by: internTuple(ref c, for: in lstTuple1), for: \"nope\")}\")");
        expected.Add($"x2 {namedTuple.IndexOf("nope")}");
        var keyCount = expected.Count - keyStart;

        // Section 5: symbol lookups.
        var lookupStart = expected.Count;
        source.Add("    print($\"l0 {c.fields[findField(ref c, by: s0, for: \"x\")].name}\")");
        expected.Add("l0 x");
        source.Add("    print($\"l1 {findField(ref c, by: s0, for: \"nope\")}\")");
        expected.Add("l1 -1");
        source.Add("    print($\"l2 {c.fields[findField(ref c, by: s3, for: \"data\")].name}\")");
        expected.Add("l2 data");
        source.Add("    print($\"l3 {findMethods(ref c, by: s0, for: \"area\").length}\")");
        expected.Add("l3 1");
        source.Add("    print($\"l3b {findMethods(ref c, by: s0, for: \"nope\").length}\")");
        expected.Add("l3b 0");
        source.Add("    print($\"l4 {displayNameAmong(ref c, by: es0, for: in lstSets01)}\")");
        expected.Add($"l4 {errA.DisplayNameAmong([errA, errB])}");
        source.Add("    print($\"l5 {displayNameAmong(ref c, by: es2, for: in lstFails0)}\")");
        expected.Add($"l5 {mathError.DisplayNameAmong([mathError])}");
        source.Add("    print($\"l6 {errorVariantSummary(ref c, by: ev0)}\")");
        expected.Add("l6 overflow");
        source.Add("    print($\"l7 {structIsGeneric(ref c, by: s0)} {structIsGeneric(ref c, by: s1)}\")");
        expected.Add($"l7 {B(pointA.IsGeneric)} {B(box.IsGeneric)}");
        // The intern-key helper is observable directly, so a broken separator is caught.
        source.Add("    print($\"h0 {joinIds(in lstSets02)}\")");
        expected.Add("h0 0,2");
        source.Add("    print($\"h1 {joinIds(in lstParams0)}\")");
        expected.Add("h1 2,2");
        source.Add("    print($\"h2 {joinIds(in lstEmpty)}\")");
        expected.Add("h2 ");
        var lookupCount = expected.Count - lookupStart;

        source.Add("    return 0");
        source.Add("}");
        return new Generated(string.Join("\n", source) + "\n", expected, displayStart, displayCount, internStart, internCount, rangeStart, rangeCount, keyStart, keyCount, lookupStart, lookupCount);
    }
}
