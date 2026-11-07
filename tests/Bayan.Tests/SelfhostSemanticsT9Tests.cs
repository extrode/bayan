using System.Text;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Semantics.Symbols;
using ArrayType = Bayan.Semantics.Symbols.ArrayType;

namespace Bayan.Tests;

/// <summary>
/// T9: the builtin functions, types and member tables of <c>selfhost/semantics/builtins.byn</c> against
/// the C# <c>Builtins</c> and <c>BuiltinMembers</c>. One generated <c>main.byn</c> prints labelled
/// sections; each test builds the same lines from the C# oracle and compares one section.
/// </summary>
public sealed class SelfhostSemanticsT9Tests
{
    private static readonly Builtins B = new();

    /// <summary>A lookup the generated main makes (a Bayan expression) and the C# symbol it must match.</summary>
    private static readonly (string Label, string Bayan, FunctionSymbol? Oracle)[] Functions =
    [
        ("find print", "find(ref c, for: \"print\") ?? -1", B.Find("print")),
        ("find log", "find(ref c, for: \"log\") ?? -1", B.Find("log")),
        ("find panic", "find(ref c, for: \"panic\") ?? -1", B.Find("panic")),
        ("find todo", "find(ref c, for: \"todo\") ?? -1", B.Find("todo")),
        ("find Owned", "find(ref c, for: \"Owned\") ?? -1", B.Find("Owned")),
        ("find Untrusted", "find(ref c, for: \"Untrusted\") ?? -1", B.Find("Untrusted")),
        ("find trust", "find(ref c, for: \"trust\") ?? -1", B.Find("trust")),
        ("find Arena", "find(ref c, for: \"Arena\") ?? -1", B.Find("Arena")),
        ("find nope", "find(ref c, for: \"nope\") ?? -1", B.Find("nope")),
        ("io readFile", "findIo(ref c, for: \"readFile\")", B.FindIo("readFile")),
        ("io readText", "findIo(ref c, for: \"readText\")", B.FindIo("readText")),
        ("io writeFile", "findIo(ref c, for: \"writeFile\")", B.FindIo("writeFile")),
        ("io remove", "findIo(ref c, for: \"remove\")", B.FindIo("remove")),
        ("io createDirectory", "findIo(ref c, for: \"createDirectory\")", B.FindIo("createDirectory")),
        ("io removeDirectory", "findIo(ref c, for: \"removeDirectory\")", B.FindIo("removeDirectory")),
        ("io listDir", "findIo(ref c, for: \"listDir\")", B.FindIo("listDir")),
        ("io run", "findIo(ref c, for: \"run\")", B.FindIo("run")),
        ("io runBatch", "findIo(ref c, for: \"runBatch\")", B.FindIo("runBatch")),
        ("io monotonicNanos", "findIo(ref c, for: \"monotonicNanos\")", B.FindIo("monotonicNanos")),
        ("io stdout", "findIo(ref c, for: \"stdout\")", B.FindIo("stdout")),
        ("io stderr", "findIo(ref c, for: \"stderr\")", B.FindIo("stderr")),
        ("io stdin", "findIo(ref c, for: \"stdin\")", B.FindIo("stdin")),
        ("io print", "findIo(ref c, for: \"print\")", B.FindIo("print")),
        ("string invalidUtf8At", "findString(ref c, for: \"invalidUtf8At\")", B.FindString("invalidUtf8At")),
        ("string decode", "findString(ref c, for: \"decode\")", B.FindString("decode")),
        ("utf8 decode", "findUtf8(ref c, for: \"decode\")", B.FindUtf8("decode")),
        ("utf8 tryDecode", "findUtf8(ref c, for: \"tryDecode\")", B.FindUtf8("tryDecode")),
        ("utf8 encode", "findUtf8(ref c, for: \"encode\")", B.FindUtf8("encode")),
        ("parse i32", "intParse(ref c, for: .i32)", B.IntParse(PrimitiveType.I32)),
        ("parse u8", "intParse(ref c, for: .u8)", B.IntParse(PrimitiveType.U8)),
        ("Arena", "t9Function(ref c, for: \"Arena\")", B.ArenaNew),
        ("StrBuf(buffer)", "t9Function(ref c, for: \"StrBuf(buffer)\")", B.StrBufNew),
        ("StrBuf", "t9Function(ref c, for: \"StrBuf\")", B.StrBufGrow),
        ("StrBuf.over", "t9Function(ref c, for: \"StrBuf.over\")", B.StrBufOver),
        ("Utf8.decode(invalid)", "t9Function(ref c, for: \"Utf8.decode(invalid)\")", B.Utf8DecodeReplace),
        ("Hex.encode", "t9Function(ref c, for: \"Hex.encode\")", B.HexEncode),
        ("Hex.decode", "t9Function(ref c, for: \"Hex.decode\")", B.HexDecode),
        ("path isRooted", "findPath(ref c, for: \"isRooted\")", B.FindPath("isRooted")),
        ("path relative", "findPath(ref c, for: \"relative\")", B.FindPath("relative")),
        ("path fileName", "findPath(ref c, for: \"fileName\")", B.FindPath("fileName")),
        ("path directoryName", "findPath(ref c, for: \"directoryName\")", B.FindPath("directoryName")),
        ("path combine", "findPath(ref c, for: \"combine\")", B.FindPath("combine")),
        ("path fullPath", "findPath(ref c, for: \"fullPath\")", B.FindPath("fullPath")),
        ("Path.combine(paths)", "t9Function(ref c, for: \"Path.combine(paths)\")", B.PathCombineSpan),
        ("Symbol.intern", "t9Function(ref c, for: \"Symbol.intern\")", B.SymbolIntern),
        ("env get", "findEnv(ref c, for: \"get\")", B.FindEnv("get")),
        ("env nope", "findEnv(ref c, for: \"nope\")", B.FindEnv("nope")),
        ("fs fileExists", "findFs(ref c, for: \"fileExists\")", B.FindFs("fileExists")),
        ("fs nope", "findFs(ref c, for: \"nope\")", B.FindFs("nope")),
        ("Env.get", "t9Function(ref c, for: \"Env.get\")", B.EnvGet),
        ("Fs.fileExists", "t9Function(ref c, for: \"Fs.fileExists\")", B.FsFileExists),
    ];

    /// <summary>The types the member tables are asked about: label, Bayan expression, C# type.</summary>
    private static readonly (string Label, string Bayan, TypeSymbol Oracle)[] Types =
    [
        ("string", "prim(.string)", PrimitiveType.String),
        ("i64", "prim(.i64)", PrimitiveType.I64),
        ("rune", "prim(.char)", PrimitiveType.Char),
        ("roBytes", "internSpan(ref c, by: prim(.u8), readOnly: true)", new SpanType(PrimitiveType.U8, readOnly: true)),
        ("spanStr", "internSpan(ref c, by: prim(.string), readOnly: false)", new SpanType(PrimitiveType.String)),
        ("listI64", "internList(ref c, by: prim(.i64))", new ListType(PrimitiveType.I64)),
        ("listStr", "internList(ref c, by: prim(.string))", new ListType(PrimitiveType.String)),
        ("listBool", "internList(ref c, by: prim(.bool))", new ListType(PrimitiveType.Bool)),
        ("listRune", "internList(ref c, by: prim(.char))", new ListType(PrimitiveType.Char)),
        ("map", "internMap(ref c, by: prim(.string), to: prim(.i64))", new MapType(PrimitiveType.String, PrimitiveType.I64)),
        ("array", "internArray(ref c, by: prim(.i32), at: 3)", new ArrayType(PrimitiveType.I32, 3)),
        ("arena", "builtinType(.arena)", BuiltinType.Arena),
        ("strBuf", "builtinType(.strBuf)", BuiltinType.StrBuf),
        ("symbol", "builtinType(.symbol)", BuiltinType.Symbol),
        ("processResult", "builtinType(.processResult)", BuiltinType.ProcessResult),
        ("panic", "builtinType(.panic)", BuiltinType.Panic),
        ("formatSpec", "builtinType(.formatSpec)", BuiltinType.FormatSpec),
        ("ioStream", "builtinType(.ioStream)", BuiltinType.IoStream),
        ("ioInput", "builtinType(.ioInput)", BuiltinType.IoInput),
        ("io", "builtinType(.io)", BuiltinType.Io),
        ("result", "internResult(ref c, by: prim(.i64), to: t9Type(ref c, for: \"IoError\"))", new ResultType(PrimitiveType.I64, B.IoError)),
        ("owned", "internOwned(ref c, by: prim(.i64))", new OwnedType(PrimitiveType.I64)),
        ("optional", "internOptional(ref c, by: prim(.i64))", new OptionalType(PrimitiveType.I64)),
        ("listList", "internList(ref c, by: internList(ref c, by: prim(.i64)))", new ListType(new ListType(PrimitiveType.I64))),
        ("listStrBuf", "internList(ref c, by: builtinType(.strBuf))", new ListType(BuiltinType.StrBuf)),
        ("optStrBuf", "internOptional(ref c, by: builtinType(.strBuf))", new OptionalType(BuiltinType.StrBuf)),
        ("optList", "internOptional(ref c, by: internList(ref c, by: prim(.i64)))", new OptionalType(new ListType(PrimitiveType.I64))),
        ("enum", "t9Type(ref c, for: \"Stream\")", B.Stream),
        ("runes", "textViewType(.runes)", TextViewType.Runes),
        ("utf16", "textViewType(.utf16)", TextViewType.Utf16),
        ("lines", "textViewType(.lines)", TextViewType.Lines),
        ("parts", "textViewType(.parts)", TextViewType.Parts),
    ];

    /// <summary>The C# <c>Candidates</c> plus names no table has.</summary>
    private static readonly string[] MemberNames =
        ["length", "bytes", "isEmpty", "runeCount", "isAscii", "runes", "utf16", "lines", "value", "copy", "startsWith", "endsWith", "contains",
         "position", "lastPosition", "count", "trimmed", "equals", "lowercased", "uppercased", "replaced", "repeated", "padded", "parts", "skip", "takeLast", "joined", "span", "mutableSpan", "clear", "removeLast", "replace", "sort", "tryGet", "slot", "remove", "reset", "append", "appendRepeated", "insert", "finish", "view", "take", "write", "readAll", "readAllBytes", "name", "ordinal", "exitCode", "stdout", "stderr", "message", "radix", "debug", "width", "fill", "align", "digits",
         "isOk", "err", "nope"];

    private static readonly string[] NameTypes =
        ["string", "i64", "rune", "roBytes", "spanStr", "listI64", "listStr", "listBool", "listRune", "listList", "listStrBuf", "map", "array", "strBuf", "symbol",
         "processResult", "formatSpec", "result", "owned", "optional", "optStrBuf", "optList", "enum", "runes", "utf16", "ioInput"];

    /// <summary>The builtin kinds the type table names: Bayan expression and C# type, compared by ordinal and display name.</summary>
    private static readonly (string Bayan, BuiltinType Oracle)[] Kinds =
    [
        ("builtinType(.path)", BuiltinType.Path),
        ("builtinType(.target)", BuiltinType.Target),
        ("builtinType(.env)", BuiltinType.Env),
        ("builtinType(.fs)", BuiltinType.Fs),
    ];

    /// <summary>Build targets for <c>Target.os</c>/<c>Target.arch</c>; null is the compiling host.</summary>
    private static readonly string?[] Targets = ["linux-x64", "macos-arm64", "windows-x64", "wasm32", "nodash", "", null];

    private const string TargetSource = "void main() {\n    let a = Target.os\n    let b = Target.arch\n}\n";

    private static string TargetLabel(string? t) => t is null ? "host" : $"[{t}]";

    private static readonly string[] Enums = ["Stream", "Radix", "Edge", "Align", "Comparison", "LogLevel", "Utf8Invalid"];

    private static readonly string[] ErrorSets = ["IoError", "Utf8Error", "ValidationError", "ParseError"];

    /// <summary>
    /// T21 (Q1017): programs compiled by both checkers, each alone with <c>Root</c> (under the repo) as package root:
    /// the module order, the diagnostics, the error and struct-literal nodes, and the display name of <c>TypeName</c>
    /// when it is an unexpanded alias in the entry module's types (the C# <c>AliasPlaceholder</c>, <c>TypeKind.alias</c>).
    /// </summary>
    private static readonly (string Label, string Root, string Source, string TypeName)[] AliasCases =
    [
        ("static-call", "tmp/t21-alias", "struct L {\n    i64 a\n    static L make(i64 a) => L { a: a }\n}\ntype M = L\nvoid main() {\n    let m = M.make(2)\n    print($\"{m.a}\")\n}\n", "M"),
        ("field-default-import", "tests/cases/run/alias-field-default-import", File.ReadAllText(Path.Combine(Golden.CasesRoot, "run", "alias-field-default-import", "main.byn")), "LTree"),
        // Not a call: the C# `BindCall` drops an error callee for a fresh `BoundErrorExpr` (BinderCalls.cs:92-94), which
        // `Descendants` never sees, while the selfhost arena keeps both.
        ("unknown-member", "tmp/t21-alias", "struct L {\n    i64 a\n}\ntype M = L\nvoid main() {\n    let m = M.nope\n}\n", "M"),
        ("struct-itself", "tmp/t21-alias", "struct L {\n    i64 a\n    static L make(i64 a) => L { a: a }\n}\nvoid main() {\n    let m = L.make(2)\n    print($\"{m.a}\")\n}\n", "L"),
    ];

    private static string BayanString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static readonly Lazy<Dictionary<string, List<string>>> Output = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    /// <summary>The internal <c>BuiltinMembers</c>, reached by reflection.</summary>
    private static readonly System.Type Members = typeof(Builtins).Assembly.GetType("Bayan.Semantics.Binding.BuiltinMembers")!;

    private static object? Call(string method, TypeSymbol type, string name) => Members.GetMethod(method)!.Invoke(null, [type, name]);

    private static object Field(object o, string name) =>
        o.GetType().GetField(name) is { } f ? f.GetValue(o)! : o.GetType().GetProperty(name)!.GetValue(o)!;

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Span(Location? l) => l is { } x ? $"{x.Span.Start}:{x.Span.Length}" : "-";

    private static string MainSource()
    {
        var s = new StringBuilder();
        s.AppendLine("""
            import lexer
            import parser

            string b(bool v) => v ? "true" : "false"

            string sp(TextSpan s) => $"{s.start}:{s.length}"

            string fnLine(ref Ctx c, for string label, at FuncId f) {
                if f < 0 { return $"{label} null" }
                var sb = StrBuf()
                let ret = typeDisplay(ref c, by: c.functions[f].returnType)
                sb.append($"{label} {c.functions[f].name} {c.functions[f].builtin.ordinal} {ret} (")
                for i in 0..c.functions[f].parameters.length {
                    let l = c.functions[f].parameters[i]
                    let t = typeDisplay(ref c, by: c.locals[l].type)
                    if i > 0 { sb.append(", ") }
                    sb.append($"{c.locals[l].name}: {t}")
                }
                sb.append($") fails={b(v: c.functions[f].hasFails)} sets=")
                for i in 0..c.functions[f].errorSets.length {
                    if i > 0 { sb.append(",") }
                    sb.append(c.errorSets[c.functions[f].errorSets[i]].name)
                }
                return $"{sb.finish()}"
            }

            void members(ref Ctx c, for string label, at Type t, by in List<string> all) {
                for n in all {
                    if (builtinProperty(ref c, for: t, by: n) is { } p) {
                        let d = typeDisplay(ref c, by: p.type)
                        print($"{label}.{n} property {p.member.ordinal} {d}")
                    }
                    let found = method(ref c, for: t, by: n)
                    if (found is { } m) {
                        var sb = StrBuf()
                        sb.append($"{label}.{n} method {m.member.ordinal} (")
                        for i in 0..m.parameters.length {
                            let d = typeDisplay(ref c, by: m.parameters[i].type)
                            if i > 0 { sb.append(", ") }
                            sb.append($"{m.parameters[i].name}: {d}")
                        }
                        let r = typeDisplay(ref c, by: m.returns)
                        sb.append($") {r} mutating={b(v: m.mutating)} optional={m.optional}")
                        print(sb.finish())
                    }
                    if hasMethod(ref c, for: t, by: n) { print($"{label}.{n} has") }
                }
            }

            void namesLine(ref Ctx c, for string label, at Type t) {
                var sb = StrBuf()
                sb.append($"{label}:")
                for n in names(ref c, for: t) { sb.append($" {n}") }
                print(sb.finish())
            }

            void enumLine(ref Ctx c, for string name) {
                let e = typeIndex(t9Type(ref c, for: name))
                var sb = StrBuf()
                sb.append($"{c.enums[e].name} {sp(c.enums[e].span)} decl={c.enums[e].declIndex}")
                for m in c.enums[e].members {
                    sb.append($" {c.enumMembers[m].name}@{sp(c.enumMembers[m].span)}#{c.enumMembers[m].ordinal}")
                }
                print(sb.finish())
            }

            void errorSetLine(ref Ctx c, for string name) {
                let e = typeIndex(t9Type(ref c, for: name))
                let decl = t9Text(ref c, for: c.errorSets[e].file, at: t9DeclName(ref c, for: c.errorSets[e].file, at: 0))
                print($"{c.errorSets[e].name} {sp(c.errorSets[e].span)} decl={decl}")
            }

            i32 main() {
                var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
                print("== functions")
            """);
        for (var i = 0; i < Functions.Length; i++)
        {
            s.AppendLine($"    let f{i} = {Functions[i].Bayan}");
            s.AppendLine($"    print(fnLine(ref c, for: \"{Functions[i].Label}\", at: f{i}))");
        }
        s.AppendLine("    let again = intParse(ref c, for: .i32)");
        s.AppendLine($"    print($\"parse i32 again {{b(v: again == f{Array.FindIndex(Functions, f => f.Label == "parse i32")})}}\")");
        s.AppendLine("    print(\"== types\")");
        foreach (var e in Enums) s.AppendLine($"    enumLine(ref c, for: \"{e}\")");
        foreach (var e in ErrorSets) s.AppendLine($"    errorSetLine(ref c, for: \"{e}\")");
        s.AppendLine("""
                let rule = typeIndex(t9Type(ref c, for: "Rule"))
                let ruleFile = c.unions[rule].file
                let violation = t9DeclName(ref c, for: ruleFile, at: 1)
                print($"{c.unions[rule].name} {sp(c.unions[rule].span)} violation={t9Text(ref c, for: ruleFile, at: violation)}@{sp(violation)}")
                var variants = List<TextSpan>()
                switch c.files[ruleFile].unit.members[0] {
                    case .unionDecl(let d):
                        for v in d.value.variants { variants.append(v.name) }
                    default: print("no union")
                }
                for v in variants { print($"Rule.{t9Text(ref c, for: ruleFile, at: v)} {sp(v)}") }
                let ifaces: List<string> = ["IEnumerator", "IIndexable", "nope"]
                for name in ifaces {
                    let i = findInterface(ref c, for: name)
                    if i < 0 { print($"{name} null") } else { print($"{c.interfaces[i].name} {sp(c.interfaces[i].span)} decl={c.interfaces[i].declIndex}") }
                }
                print($"modules {c.modules.length}")
                let all: List<string> = [
            """);
        s.AppendLine(string.Join(",\n", MemberNames.Select(n => $"        \"{n}\"")));
        s.AppendLine("    ]");
        for (var i = 0; i < Types.Length; i++) s.AppendLine($"    let t{i} = {Types[i].Bayan}");
        s.AppendLine("    print(\"== members\")");
        for (var i = 0; i < Types.Length; i++) s.AppendLine($"    members(ref c, for: \"{Types[i].Label}\", at: t{i}, by: all)");
        s.AppendLine("    print(\"== kinds\")");
        foreach (var (bayan, _) in Kinds)
            s.AppendLine($"    print($\"{{builtinKindOf(typeIndex({bayan})).ordinal}} {{typeDisplay(ref c, by: {bayan})}}\")");
        s.AppendLine("    print(\"== target\")");
        foreach (var target in Targets)
        {
            var option = target is null ? "" : $", target: \"{target}\"";
            s.AppendLine($$"""
                    {
                        var files = List<lexer.SourceText>()
                        var bytes = List<u8>()
                        for ch in "{{TargetSource.Replace("\n", "\\n")}}".bytes { bytes.append(ch) }
                        files.append(lexer.SourceText.create("main.byn", from: move bytes))
                        let compiled = create(from: move files, at: "tmp/t18-target", by: in CompilationOptions { includeTests: false{{option}} })
                        var sb = StrBuf()
                        sb.append($"{{TargetLabel(target)}} diagnostics={compiled.diagnostics.length} host={compiled.bHostTargets.length} literals=")
                        for lit in compiled.bLiterals { sb.append($"[{lit.value.text}]") }
                        print(sb.finish())
                    }
                """);
        }
        s.AppendLine("    print(\"== alias\")");
        foreach (var (label, root, source, typeName) in AliasCases)
        {
            s.AppendLine($$"""
                    {
                        var files = List<lexer.SourceText>()
                        var bytes = List<u8>()
                        for ch in "{{BayanString(source)}}".bytes { bytes.append(ch) }
                        files.append(lexer.SourceText.create("main.byn", from: move bytes))
                        var compiled = create(from: move files, at: "{{BayanString(Path.Combine(Golden.RepoRoot, root))}}", by: in CompilationOptions { includeTests: false })
                        var sb = StrBuf()
                        sb.append("{{label}} modules=")
                        for m in compiled.moduleOrder { sb.append($"{compiled.modules[m].name}/") }
                        let entry = compiled.entryModule
                        let aliased = compiled.modules[entry].types.contains("{{typeName}}") && kindOf(compiled.modules[entry].types["{{typeName}}"]) == .alias
                        let shown = aliased ? typeDisplay(ref compiled, by: compiled.modules[entry].types["{{typeName}}"]) : "-"
                        sb.append($" diagnostics={compiled.diagnostics.length} errors={compiled.bErrorExprs.length} structs={compiled.bStructLiterals.length} alias={shown}")
                        print(sb.finish())
                        for d in compiled.diagnostics { print($"  {d.code} {sp(d.span)} {d.message}") }
                    }
                """);
        }
        s.AppendLine("    print(\"== names\")");
        foreach (var n in NameTypes)
            s.AppendLine($"    namesLine(ref c, for: \"{n}\", at: t{Array.FindIndex(Types, t => t.Label == n)})");
        s.AppendLine("    return 0\n}");
        return s.ToString();
    }

    private static string FnLine(string label, FunctionSymbol? f)
    {
        if (f is null) return $"{label} null";
        var ps = string.Join(", ", f.Parameters.Select(p => $"{p.Name}: {p.Type.DisplayName}"));
        return $"{label} {f.Name} {(int)f.Builtin} {f.ReturnType.DisplayName} ({ps}) fails={Bool(f.Fails)} sets={string.Join(",", f.ErrorSets.Select(e => e.Name))}";
    }

    [Fact]
    public void FunctionsMatchTheCSharpBuiltins()
    {
        var expected = Functions.Select(f => FnLine(f.Label, f.Oracle)).ToList();
        expected.Add($"parse i32 again {Bool(ReferenceEquals(B.IntParse(PrimitiveType.I32), B.IntParse(PrimitiveType.I32)))}");
        SelfhostSemanticsHarness.AssertSection(Output.Value, "functions", expected);
    }

    [Fact]
    public void TypesAreParsedFromTheBuiltinSources()
    {
        var expected = new List<string>();
        foreach (var name in Enums)
        {
            var e = (EnumSymbol)typeof(Builtins).GetProperty(name)!.GetValue(B)!;
            expected.Add($"{e.Name} {Span(e.Location)} decl=0" + string.Concat(e.Members.Select(m => $" {m.Name}@{Span(m.Location)}#{m.Ordinal}")));
        }
        foreach (var name in ErrorSets)
        {
            var e = (ErrorSetSymbol)typeof(Builtins).GetProperty(name)!.GetValue(B)!;
            expected.Add($"{e.Name} {Span(e.Location)} decl={e.Decl.Name.Text}");
        }
        expected.Add($"{B.Rule.Name} {Span(B.Rule.Location)} violation={B.ViolationDecl.Name.Text}@{B.ViolationDecl.Name.Span.Start}:{B.ViolationDecl.Name.Span.Length}");
        foreach (var v in B.Rule.Decl.Variants) expected.Add($"Rule.{v.Name.Text} {v.Name.Span.Start}:{v.Name.Span.Length}");
        string[] ifaces = ["IEnumerator", "IIndexable", "nope"];
        for (var k = 0; k < ifaces.Length; k++)
            expected.Add(B.FindInterface(ifaces[k]) is { } i ? $"{i.Name} {Span(i.Location)} decl={k}" : $"{ifaces[k]} null");
        expected.Add("modules 1");
        SelfhostSemanticsHarness.AssertSection(Output.Value, "types", expected);
    }

    [Fact]
    public void MemberTablesMatchTheCSharp()
    {
        var expected = new List<string>();
        foreach (var (label, _, type) in Types)
        foreach (var n in MemberNames)
        {
            if (Call("Property", type, n) is { } p)
                expected.Add($"{label}.{n} property {Convert.ToInt32(Field(p, "Item1"))} {((TypeSymbol)Field(p, "Item2")).DisplayName}");
            if (Call("Method", type, n) is { } m)
            {
                var ps = string.Join(", ", ((System.Collections.IEnumerable)Field(m, "Parameters")).Cast<object>()
                    .Select(x => $"{Field(x, "Item1")}: {((TypeSymbol)Field(x, "Item2")).DisplayName}"));
                expected.Add($"{label}.{n} method {Convert.ToInt32(Field(m, "Member"))} ({ps}) {((TypeSymbol)Field(m, "Returns")).DisplayName} " +
                    $"mutating={Bool((bool)Field(m, "Mutating"))} optional={Field(m, "Optional")}");
            }
            if ((bool)Call("HasMethod", type, n)!) expected.Add($"{label}.{n} has");
        }
        SelfhostSemanticsHarness.AssertSection(Output.Value, "members", expected);
    }

    [Fact]
    public void BuiltinKindsMatchTheCSharp()
    {
        var expected = Kinds.Select(k => $"{(int)k.Oracle.Kind} {k.Oracle.DisplayName}").ToList();
        SelfhostSemanticsHarness.AssertSection(Output.Value, "kinds", expected);
    }

    [Fact]
    public void TargetMembersMatchTheCSharp()
    {
        var expected = new List<string>();
        foreach (var target in Targets)
        {
            var source = SourceText.FromString("main.byn", TargetSource);
            var compilation = Compilation.Create([source], Path.Combine(Golden.RepoRoot, "tmp", "t18-target"), new CompilationOptions(Target: target));
            var bound = Bayan.Semantics.Binding.BoundTree.Descendants(compilation.EntryModule.Functions["main"][0].Body!).ToList();
            var host = bound.OfType<Bayan.Semantics.Binding.BoundHostTarget>().Count();
            var literals = string.Concat(bound.OfType<Bayan.Semantics.Binding.BoundLiteral>().Select(l => $"[{l.Value.Value}]"));
            expected.Add($"{TargetLabel(target)} diagnostics={compilation.Diagnostics.Count} host={host} literals={literals}");
        }
        SelfhostSemanticsHarness.AssertSection(Output.Value, "target", expected);
    }

    /// <summary>
    /// T21: a static call and a field default through a type alias bind without an error node, as the C#
    /// (Q1017); the module order is the C# <c>Modules</c> order (Compilation.cs:180, an import before its importer).
    /// </summary>
    [Fact]
    public void AliasesBindAsTheCSharp()
    {
        var expected = new List<string>();
        foreach (var (label, root, source, typeName) in AliasCases)
        {
            var compilation = Compilation.Create([SourceText.FromString("main.byn", source)], Path.Combine(Golden.RepoRoot, root), new CompilationOptions());
            var modules = string.Concat(compilation.Modules.Select(m => m.Name + "/"));
            var nodes = compilation.Modules.SelectMany(m => m.AllFunctions).Where(f => f.Body is not null)
                .SelectMany(f => Bayan.Semantics.Binding.BoundTree.Descendants(f.Body!)).ToList();
            var aliased = compilation.EntryModule.Types.TryGetValue(typeName, out var t) && t.GetType().Name == "AliasPlaceholder" ? t.DisplayName : "-";
            expected.Add($"{label} modules={modules} diagnostics={compilation.Diagnostics.Count} errors={nodes.OfType<Bayan.Semantics.Binding.BoundErrorExpr>().Count()} " +
                $"structs={nodes.OfType<Bayan.Semantics.Binding.BoundStructLiteral>().Count()} alias={aliased}");
            foreach (var d in compilation.Diagnostics.Items) expected.Add($"  {d.Code} {d.Span.Start}:{d.Span.Length} {d.Message}");
        }
        SelfhostSemanticsHarness.AssertSection(Output.Value, "alias", expected);
    }

    [Fact]
    public void NamesMatchTheCSharp()
    {
        var expected = NameTypes
            .Select(n => $"{n}:" + string.Concat(((IEnumerable<string>)Members.GetMethod("Names")!.Invoke(null, [Types.First(t => t.Label == n).Oracle])!).Select(x => $" {x}")))
            .ToList();
        SelfhostSemanticsHarness.AssertSection(Output.Value, "names", expected);
    }
}
