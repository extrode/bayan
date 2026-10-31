using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using TextSpan = Bayan.Syntax.Text.TextSpan;

namespace Bayan.Tests;

/// <summary>
/// T13: <c>selfhost/semantics/generics.byn</c> against the C# <c>Compilation</c> (Compilation.cs 1363–1952).
/// Each section compiles a source with the C# <c>Compilation.Create</c>, drives the C# method under test
/// through reflection, and prints the oracle's lines; the same scenario is emitted as Bayan code that
/// builds the matching symbols (from the C# ones) and calls the port, whose output is compared line by line.
/// Diagnostics print as <c>code line:col message | label | help</c> plus related spans.
/// </summary>
public sealed class SelfhostSemanticsT13Tests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static string Root
    {
        get
        {
            var root = Path.Combine(Golden.RepoRoot, "tmp", "t13-cases");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private static Compilation Compile(string text, CompilationOptions? options = null) =>
        Compilation.Create([SourceText.FromString(Path.Combine(Root, "t13.byn"), text)], Root, options ?? new CompilationOptions(Library: true));

    private static object? Call(Compilation c, string name, params object?[] args)
    {
        var m = typeof(Compilation).GetMethods(Any).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return m.Invoke(m.IsStatic ? null : c, args); }
        catch (TargetInvocationException e) { throw e.InnerException!; }
    }

    private static object? Get(Compilation c, string name) =>
        typeof(Compilation).GetField(name, Any) is { } f ? f.GetValue(c) : typeof(Compilation).GetProperty(name, Any)!.GetValue(c);

    private static void Set(Compilation c, string name, object? value) => typeof(Compilation).GetField(name, Any)!.SetValue(c, value);

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name, Any)!.GetValue(o);

    private static object? Item4(object tuple) => tuple.GetType().GetField("Item4")!.GetValue(tuple);

    private static void Report(Compilation c, DiagnosticDescriptor d, SourceText source, TextSpan span, params string[] args) =>
        Call(c, "Report", d, source, span, args.Cast<object?>().ToArray(), null, null, null);

    private static DiagnosticDescriptor Code(string code) => DiagnosticCodes.All.First(d => d.Code == code);

    private static string At(SourceText? s, TextSpan span)
    {
        if (s is null) return "-";
        var bytes = s.Bytes.Span;
        int line = 1, col = 1;
        for (var i = 0; i < span.Start; i++)
        {
            if (bytes[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return $"{line}:{col}";
    }

    private static string Line(Diagnostic d)
    {
        var sb = new StringBuilder($"{d.Code} {At(d.Source, d.Span)} {d.Message} | {d.Label} | {d.Help}");
        foreach (var r in d.Related ?? []) sb.Append($" | rel {At(d.Source, r.Span)} {r.Label}");
        return sb.ToString();
    }

    private static List<string> NewDiags(Compilation c, int from) => c.Diagnostics.Items.Skip(from).Select(Line).ToList();

    private static string Lit(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '\n') sb.Append("\\n");
            else if (ch == '\\' || ch == '"') sb.Append('\\').Append(ch);
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }

    private static string Span(TextSpan s) => $"TextSpan {{ start: {s.Start}, length: {s.Length} }}";

    private static int MemberIndex(Compilation c, Decl d) => Array.IndexOf(c.EntryModule.Files[0].Tree.Root.Members, d);

    private static string KindName(FunctionKind k) => k switch
    {
        FunctionKind.Function => ".function",
        FunctionKind.Method => ".method",
        FunctionKind.Constructor => ".constructor",
        FunctionKind.Getter => ".getter",
        FunctionKind.Main => ".main",
        FunctionKind.Test => ".testKeyword",
        FunctionKind.Builtin => ".builtin",
        FunctionKind.Lambda => ".lambda",
        _ => ".invoke",
    };

    /// <summary>Bayan code that builds the symbols of one C# compilation's entry module, by name.</summary>
    private sealed class World
    {
        public readonly Compilation C;
        public readonly StringBuilder Code = new();
        public readonly Dictionary<object, string> Vars = new(ReferenceEqualityComparer.Instance);
        private int _next;

        public World(Compilation c, string text)
        {
            C = c;
            Emit($"var c = newCtx()");
            Emit($"addSource(ref c, for: {Lit(text)}, as: {Lit(c.EntryModule.Name)})");
            Emit("c.entryModule = 0");
        }

        public void Emit(string line) => Code.Append("    ").Append(line).Append('\n');

        private string Fresh(string prefix) => $"{prefix}{_next++}";

        public string Type(TypeSymbol t) => t switch
        {
            ErrorType => "errorType()",
            PrimitiveType p => $"prim(PrimitiveKind.{p.DisplayName})",
            StructSymbol s => $"structTypeOf({Vars[s]})",
            InterfaceSymbol i => $"interfaceTypeOf({Vars[i]})",
            UnionSymbol u => $"unionTypeOf({Vars[u]})",
            ErrorSetSymbol e => $"errorSetTypeOf({Vars[e]})",
            TypeParameterType p => $"typeParamTypeOf({Vars[p]})",
            _ => throw new InvalidOperationException(t.DisplayName),
        };

        public string Ids(IEnumerable<object> xs, string type)
        {
            var v = Fresh("ids");
            Emit($"var {v} = List<{type}>()");
            foreach (var x in xs) Emit($"{v}.append({(type == "Type" ? Type((TypeSymbol)x) : Vars[x])})");
            return v;
        }

        public string TypeParams(IEnumerable<TypeParameterType> ps)
        {
            var list = ps.ToList();
            foreach (var p in list.Where(p => !Vars.ContainsKey(p)))
            {
                var cs = Ids(p.Constraints, "InterfaceId");
                var v = Fresh("tp");
                Emit($"let {v} = newTypeParam(ref c, by: TypeParam {{ name: {Lit(p.Name)}, constraints: move {cs} }})");
                Vars[p] = v;
            }
            return Ids(list, "TypeParamId");
        }

        public string Function(FunctionSymbol f, int decl, int member = -1, string owner = "-1", string iface = "-1", string extra = "")
        {
            var tps = TypeParams(f.TypeParameters);
            var v = Fresh("fn");
            var loc = f.Location is { } l ? l.Span : default;
            if (f.ReturnType is PrimitiveType ret) extra += $", returnType: {Type(ret)}";
            Emit($"let {v} = newFunction(ref c, by: FunctionSymbol {{ name: {Lit(f.Name)}, nameSpan: {Span(loc)}, span: {Span(loc)}, module: 0, kind: {KindName(f.Kind)}, file: 0, declIndex: {decl}, memberIndex: {member}, owner: {owner}, iface: {iface}, isAbstract: {(f.IsAbstract ? "true" : "false")}, typeParams: move {tps}{extra} }})");
            Vars[f] = v;
            return v;
        }

        /// <summary>Interfaces, structs, unions and generic functions of the entry module (templates only).</summary>
        public void Declarations()
        {
            var m = C.EntryModule;
            foreach (var i in m.Interfaces.Where(i => i.Template is null))
            {
                var v = Fresh("iface");
                Emit($"let {v} = newInterface(ref c, by: InterfaceSymbol {{ name: {Lit(i.Name)}, nameSpan: {Span(i.Location.Span)}, span: {Span(i.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(C, i.Decl)} }})");
                Emit($"c.modules[0].interfaces.append({v})");
                Emit($"c.modules[0].types[{Lit(i.Name)}] = interfaceTypeOf({v})");
                Vars[i] = v;
            }
            foreach (var i in m.Interfaces.Where(i => i.Template is null))
            {
                var tps = TypeParams(i.TypeParameters);
                Emit($"c.interfaces[{Vars[i]}].typeParams = move {tps}");
                var d = MemberIndex(C, i.Decl);
                foreach (var f in i.Methods) Emit($"c.interfaces[{Vars[i]}].methods.append({Function(f, d, Array.IndexOf(i.Decl.Members, f.Decl), iface: Vars[i])})");
                foreach (var f in i.Properties) Emit($"c.interfaces[{Vars[i]}].properties.append({Function(f, d, -1, iface: Vars[i])})");
            }
            foreach (var s in m.Structs.Where(s => s.Template is null))
            {
                var v = Fresh("st");
                Emit($"let {v} = newStruct(ref c, by: StructSymbol {{ name: {Lit(s.Name)}, nameSpan: {Span(s.Location.Span)}, span: {Span(s.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(C, s.Decl)} }})");
                Emit($"c.modules[0].structs.append({v})");
                Emit($"c.modules[0].types[{Lit(s.Name)}] = structTypeOf({v})");
                Vars[s] = v;
            }
            foreach (var s in m.Structs.Where(s => s.Template is null))
            {
                var v = Vars[s];
                var tps = TypeParams(s.TypeParameters);
                Emit($"c.structs[{v}].typeParams = move {tps}");
                var d = MemberIndex(C, s.Decl);
                foreach (var f in s.Methods) Emit($"c.structs[{v}].methods.append({Function(f, d, Array.IndexOf(s.Decl.Members, f.Decl), owner: v)})");
                foreach (var p in s.Properties)
                {
                    var g = Function(p.Getter!, d, Array.IndexOf(s.Decl.Members, p.Decl), owner: v);
                    Emit($"c.structs[{v}].properties.append(newProperty(ref c, by: PropertySymbol {{ name: {Lit(p.Name)}, owner: {v}, file: 0, declIndex: {d}, memberIndex: {Array.IndexOf(s.Decl.Members, p.Decl)}, getter: {g} }}))");
                }
                foreach (var k in s.Conformances)
                    Emit($"c.structs[{v}].conformances.append(newConformance(ref c, by: Conformance {{ type: {v}, iface: {Vars[k.Interface]} }}))");
            }
            foreach (var u in m.Unions.Where(u => u.Template is null))
            {
                var v = Fresh("un");
                var tps = TypeParams(u.TypeParameters);
                Emit($"let {v} = newUnion(ref c, by: UnionSymbol {{ name: {Lit(u.Name)}, nameSpan: {Span(u.Location.Span)}, span: {Span(u.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(C, u.Decl)}, typeParams: move {tps} }})");
                Emit($"c.modules[0].unions.append({v})");
                Emit($"c.modules[0].types[{Lit(u.Name)}] = unionTypeOf({v})");
                Vars[u] = v;
            }
            foreach (var f in m.AllFunctions.Where(f => f.Kind == FunctionKind.Function && !f.IsInstance))
                Emit($"c.modules[0].allFunctions.append({Function(f, MemberIndex(C, f.Decl!))})");
        }

        public FunctionSymbol Fn(string name) => C.EntryModule.Functions[name][0];

        public TypeSymbol T(string name) => C.EntryModule.Types[name];
    }

    // ── Sources ─────────────────────────────────────────────────────────────────

    private const string Generics = """
        interface IShape {
            i64 area()
            i64 side
        }

        interface IOther {
            i64 other() => 1
        }

        interface IEmpty {
        }

        struct Circle : IShape {
            i64 r
            i64 area() => r
            i64 side => r
        }

        struct Square {
            i64 s
            i64 side() => s
        }

        struct Both {
            i64 area() => 1
            i64 side => 1
        }

        struct Tri : IShape {
            i64 area() => 3
            i64 side => 3
        }

        void need<T>() where T : IShape {
        }

        void two<T>() where T : IOther, IShape {
        }

        void empty<T>() where T : IEmpty {
        }

        void none<T>() {
        }

        void notIface<T>() where T : Circle {
        }

        void loose<U>() {
        }

        struct Box<T> where T : IShape {
            T item
            i64 n = 1
            i64 get() => n
        }

        union Maybe<T> where T : IShape {
            some(T value)
            none
        }

        interface IGen<T> where T : IShape {
        }

        struct Free<T> {
            T item
        }

        interface IRev {
            i64 back
            i64 front()
        }

        void split<T>() where T : IShape where T : IEmpty {
        }

        void rev<T>() where T : IRev {
        }

        """;

    private static readonly TextSpan Site = new(30, 6);

    // ── Sections ────────────────────────────────────────────────────────────────

    private delegate (string Bayan, List<string> Expected) Section();

    private static readonly (string Name, Section Build)[] All =
    [
        ("instantiating", Instantiating),
        ("constraints", Constraints),
        ("instantiateFunction", InstantiateFunction),
        ("instantiateType", InstantiateType),
        ("templateOf", TemplateOf),
        ("invokeSymbol", InvokeSymbol),
        ("errorSets", ErrorSets),
        ("bindConstant", BindConstant),
        ("reportHeld", ReportHeld),
        ("defaults", Defaults),
        ("bindFunction", BindFunction),
        ("bindMainAndCases", BindMainAndCases),
        ("requires", Requires),
        ("noMain", NoMain),
        ("bindAllDrains", BindAllDrains),
        ("bindAllConstantsAndDefaults", BindAllConstantsAndDefaults),
        ("drainInstanceMethods", DrainInstanceMethods),
    ];

    /// <summary>Instantiating: which codes are forwarded to the use, which are held, depth and outermost site.</summary>
    private static (string, List<string>) Instantiating()
    {
        const string text = "void f() {\n}\n\nstruct P {\n    i64 x\n    i64 y\n    i64 z\n    i64 w\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        w.Emit("let f = newFunction(ref c, by: FunctionSymbol { name: \"f\", file: 0, module: 0 })");
        var site = c.EntryModule.Files[0].Source;
        var lines = new List<string>();
        var start = c.Diagnostics.Count;
        w.Emit("let start = c.diagnostics.length");
        var s1 = new TextSpan(0, 4);
        var s2 = new TextSpan(5, 1);
        var s3 = new TextSpan(14, 6);
        var s4 = new TextSpan(21, 1);
        var s5 = new TextSpan(29, 3);
        var s6 = new TextSpan(33, 1);
        void Rep(string code, string bayanCode, TextSpan span)
        {
            Report(c, Code(code), site, span, "a", "b", "c", "d");
        }
        string BRep(string bayanCode, TextSpan span) =>
            $"report(ref c, for: .{bayanCode}, from: 0, at: {Span(span)}, by: (\"a\", \"b\", \"c\", \"d\", \"\"))";
        (string Code, string Bayan, TextSpan Span)[] before = [("BYN2822", "functionTypeNotHere", s1), ("BYN2780", "implicitCopy", s2)];
        (string Code, string Bayan, TextSpan Span)[] inside =
        [
            ("BYN2760", "invalidMapKey", s3), ("BYN2831", "keyPartNotHashable", s3), ("BYN2208", "badBinaryOperands", s3),
            ("BYN2225", "notFormattable", s3), ("BYN2807", "constraintNotSatisfied", s3), ("BYN2822", "functionTypeNotHere", s1),
            ("BYN2822", "functionTypeNotHere", s4), ("BYN2022", "unsupported", s4), ("BYN2780", "implicitCopy", s2),
            ("BYN2780", "implicitCopy", s5), ("BYN7012", "assignmentChangesState", s5), ("BYN2006", "duplicateDeclaration", s6),
            ("BYN2761", "recursiveByValue", s6), ("BYN2201", "stringIndexer", s6),
        ];
        foreach (var (code, b, span) in before)
        {
            Rep(code, b, span);
            w.Emit(BRep(b, span));
        }
        string State() => $"depth {Get(c, "_instantiating")} site {(Get(c, "_instantiationSite") is { } o ? ((ValueTuple<SourceText, TextSpan>)o).Item2.Start : -1)}";
        const string bState = "print($\"depth {c.instantiating} site {c.hasInstantiationSite ? c.instantiationSiteSpan.start : -1}\")";
        Call(c, "Instantiating", site, Site, (Action)(() =>
        {
            lines.Add(State());
            foreach (var (code, b, span) in inside) Rep(code, b, span);
            Call(c, "Instantiating", site, new TextSpan(40, 2), (Action)(() =>
            {
                lines.Add(State());
                Rep("BYN2807", "", s3);
                Rep("BYN2006", "", s6);
            }), null);
            lines.Add(State());
        }), null);
        w.Emit($"let outer = instantiatingBegin(ref c, from: 0, at: {Span(Site)})");
        w.Emit(bState);
        foreach (var (_, b, span) in inside) w.Emit(BRep(b, span));
        w.Emit($"let inner = instantiatingBegin(ref c, from: 0, at: {Span(new TextSpan(40, 2))})");
        w.Emit(bState);
        w.Emit(BRep("constraintNotSatisfied", s3));
        w.Emit(BRep("duplicateDeclaration", s6));
        w.Emit("instantiatingEnd(ref c, by: inner)");
        w.Emit(bState);
        // C# has no `_quiet`; `target.Add` (Compilation.cs:1488) is ungated, so the forwarded errors arrive while Bayan's
        // `c.quiet` is set (Q580). The expected lines are C#'s, run without it.
        w.Emit("c.quiet = true");
        w.Emit("instantiatingEnd(ref c, by: outer)");
        w.Emit("c.quiet = false");
        lines.Add(State());
        w.Emit(bState);
        // A state change through a `ref` names the generic; related only when it is in the same source.
        var f = c.EntryModule.Functions["f"][0];
        Call(c, "Instantiating", site, new TextSpan(50, 1), (Action)(() => Rep("BYN7012", "", s4)), f);
        w.Emit($"let named = instantiatingBegin(ref c, from: 0, at: {Span(new TextSpan(50, 1))})");
        w.Emit(BRep("assignmentChangesState", s4));
        w.Emit("c.quiet = true");
        w.Emit("instantiatingEnd(ref c, by: named, instance: f)");
        w.Emit("c.quiet = false");
        // Unforwarded warnings vanish; a 7012 already reported stays held.
        Call(c, "Instantiating", site, new TextSpan(52, 1), (Action)(() =>
        {
            Report(c, Code("BYN2405"), site, s6, "a", "b", "c", "d");
            Rep("BYN7012", "", s4);
        }), f);
        w.Emit($"let warned = instantiatingBegin(ref c, from: 0, at: {Span(new TextSpan(52, 1))})");
        w.Emit(BRep("unreachableCode", s6));
        w.Emit(BRep("assignmentChangesState", s4));
        w.Emit("instantiatingEnd(ref c, by: warned, instance: f)");
        lines.AddRange(NewDiags(c, start));
        foreach (var d in (List<Diagnostic>)Get(c, "_instantiationErrors")!) lines.Add("held " + Line(d));
        w.Emit("showDiags(in c, from: start)");
        w.Emit("for d in c.instantiationErrors { print($\"held {diagLine(in c, for: in d)}\") }");
        return (w.Code.ToString(), lines);
    }

    /// <summary>CheckConstraints: each function's <c>where</c> bounds against one argument.</summary>
    private static (string, List<string>) Constraints()
    {
        var c = Compile(Generics);
        var w = new World(c, Generics);
        w.Declarations();
        var lines = new List<string>();
        (string Fn, string Arg)[] cases =
        [
            ("need", "Circle"), ("need", "Square"), ("need", "Both"), ("need", "i32"), ("need", "U"), ("need", "IShape"),
            ("need", "error"), ("two", "Square"), ("empty", "Square"), ("none", "Square"), ("notIface", "Square"), ("two", "Tri"),
            ("split", "Square"), ("split", "Circle"), ("rev", "Square"),
        ];
        var loose = w.Fn("loose");
        foreach (var (fn, arg) in cases)
        {
            var f = w.Fn(fn);
            TypeSymbol t = arg switch
            {
                "i32" => PrimitiveType.I32,
                "U" => loose.TypeParameters[0],
                "error" => ErrorType.Instance,
                _ => w.T(arg),
            };
            var start = c.Diagnostics.Count;
            var ok = (bool)Call(c, "CheckConstraints", f.TypeParameters, f.Decl!.Where, f.File, null, f.TypeMap, new[] { t })!;
            lines.Add($"{fn} {arg} {(ok ? "yes" : "no")}");
            lines.AddRange(NewDiags(c, start));
            w.Emit("{");
            w.Emit($"    let start = c.diagnostics.length");
            w.Emit($"    var args = List<Type>()");
            w.Emit($"    args.append({w.Type(t)})");
            w.Emit($"    let ps = copyIds(in c.functions[{w.Vars[f]}].typeParams)");
            w.Emit($"    var ok = false");
            w.Emit($"    var tm = Map<string, Type>()");
            foreach (var (k, v) in f.TypeMap ?? new Dictionary<string, TypeSymbol>()) w.Emit($"    tm[{Lit(k)}] = {w.Type(v)}");
            w.Emit($"    switch c.files[0].unit.members[{MemberIndex(c, f.Decl)}] {{");
            w.Emit($"        case .functionDecl(let n):");
            w.Emit($"            ok = checkConstraints(ref c, for: in ps, through: in n.value.where, from: 0, as: -1, by: in tm, to: in args)");
            w.Emit($"        default:");
            w.Emit($"            panic(\"not a function\")");
            w.Emit($"    }}");
            w.Emit($"    print($\"{fn} {arg} {{yn(b: ok)}}\")");
            w.Emit($"    showDiags(in c, from: start)");
            w.Emit("}");
        }
        return (w.Code.ToString(), lines);
    }

    private static string Display(IEnumerable<TypeSymbol> ts) => string.Join(",", ts.Select(t => t.DisplayName));

    private static string MapText(IReadOnlyDictionary<string, TypeSymbol>? m) =>
        m is null ? "" : string.Join(",", m.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.DisplayName}"));

    private static int WorkCount(Compilation c) => ((System.Collections.ICollection)Get(c, "_instantiationWork")!).Count;

    /// <summary>InstantiateFunction: the cache, the declared instance, its constraint check, the worklist, a first pass.</summary>
    private static (string, List<string>) InstantiateFunction()
    {
        var c = Compile(Generics);
        var w = new World(c, Generics);
        w.Declarations();
        var site = c.EntryModule.Files[0].Source;
        var lines = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void One(string label, string fn, string arg, Action? before = null, Action? after = null, string bayanBefore = "", string bayanAfter = "")
        {
            var template = w.Fn(fn);
            var t = arg == "U" ? w.Fn("loose").TypeParameters[0] : w.T(arg);
            before?.Invoke();
            var start = c.Diagnostics.Count;
            var fns = c.EntryModule.AllFunctions.Count;
            var work = WorkCount(c);
            var inst = (FunctionSymbol)Call(c, "InstantiateFunction", template, new[] { t }, site, Site)!;
            var queued = WorkCount(c) - work;
            var last = queued > 0 ? Item4(((System.Collections.IEnumerable)Get(c, "_instantiationWork")!).Cast<object>().Last()) : null;
            lines.Add($"{label} new {(seen.Add(inst) ? "yes" : "no")} {inst.Name} of {inst.Template?.Name} args {Display(inst.TypeArguments)} map {MapText(inst.TypeMap)} fns +{c.EntryModule.AllFunctions.Count - fns} work +{queued}{(queued > 0 ? $" self {(ReferenceEquals(last, inst) ? "yes" : "no")}" : "")}");
            lines.AddRange(NewDiags(c, start));
            after?.Invoke();
            w.Emit("{");
            w.Emit(bayanBefore);
            w.Emit("    let start = c.diagnostics.length");
            w.Emit("    let fns = c.modules[0].allFunctions.length");
            w.Emit("    let work = c.instantiationWork.length");
            w.Emit("    var args = List<Type>()");
            w.Emit($"    args.append({w.Type(t)})");
            w.Emit($"    let inst = instantiateFunction(ref c, for: {w.Vars[template]}, by: in args, from: 0, at: {Span(Site)})");
            w.Emit($"    showInstance(ref c, for: inst, by: \"{label}\", from: fns, at: work, to: ref seen)");
            w.Emit("    showDiags(in c, from: start)");
            w.Emit(bayanAfter);
            w.Emit("}");
        }
        w.Emit("var seen = List<FuncId>()");
        One("tri", "need", "Tri");
        One("again", "need", "Tri");
        One("square", "need", "Square");
        One("nested", "need", "Both", () => Set(c, "_instantiating", 1), () => Set(c, "_instantiating", 0),
            "    c.instantiating = 1", "    c.instantiating = 0");
        One("generic", "loose", "U");
        // A first pass keeps what it instantiates and what that reported.
        One("pass", "two", "Square",
            () => Set(c, "_firstPass", ((List<FunctionSymbol>)[], new DiagnosticBag(), new List<Diagnostic>())),
            () =>
            {
                var (instances, bag, errors) = ((List<FunctionSymbol>, DiagnosticBag, List<Diagnostic>))Get(c, "_firstPass")!;
                lines.Add($"pass instances {string.Join(",", instances.Select(i => $"{i.Name}<{Display(i.TypeArguments)}>"))} errors {errors.Count}");
                lines.AddRange(bag.Items.Select(d => "pass " + Line(d)));
                Set(c, "_firstPass", null);
            },
            "    c.bags.append(DiagBag { items: List<Diag>() })\n    c.firstPasses.append(FirstPass { instances: List<FuncId>(), bag: c.bags.length - 1, errors: List<Diag>() })\n    c.firstPass = c.firstPasses.length - 1",
            "    showPass(ref c)\n    c.firstPass = -1");
        return (w.Code.ToString(), lines);
    }

    /// <summary>InstantiateType before conformances are checked: the cache, deferred constraints, deferred interface members.</summary>
    private static (string, List<string>) InstantiateType()
    {
        var c = Compile(Generics);
        Set(c, "_conformancesChecked", false);
        var w = new World(c, Generics);
        w.Declarations();
        w.Emit("c.interfaceMembersDeclared = true");
        var site = c.EntryModule.Files[0].Source;
        var lines = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = (List<Action>)Get(c, "_pendingConstraints")!;
        var pendingInterfaces = (System.Collections.ICollection)Get(c, "_pendingInterfaces")!;
        w.Emit("var seen = List<Type>()");
        void One(string label, string generic, string arg, string bayanBefore = "", string bayanAfter = "", Action? before = null, Action? after = null)
        {
            var template = w.T(generic);
            var t = w.T(arg);
            before?.Invoke();
            var start = c.Diagnostics.Count;
            var p0 = pending.Count;
            var i0 = pendingInterfaces.Count;
            var work = WorkCount(c);
            var inst = (TypeSymbol)Call(c, "InstantiateType", template, new[] { t }, site, Site)!;
            var map = inst switch { StructSymbol s => s.TypeMap, UnionSymbol u => u.TypeMap, InterfaceSymbol i => i.TypeMap, _ => null };
            lines.Add($"{label} new {(seen.Add(inst) ? "yes" : "no")} {inst.DisplayName} map {MapText(map)} pending +{pending.Count - p0} interfaces +{pendingInterfaces.Count - i0}");
            lines.Add($"work +{WorkCount(c) - work}");
            lines.AddRange(NewDiags(c, start));
            after?.Invoke();
            w.Emit("{");
            w.Emit(bayanBefore);
            w.Emit("    let start = c.diagnostics.length");
            w.Emit("    let p0 = c.pendingConstraints.length");
            w.Emit("    let i0 = c.pendingInterfaces.length");
            w.Emit("    let work = c.instantiationWork.length");
            w.Emit("    var args = List<Type>()");
            w.Emit($"    args.append({w.Type(t)})");
            w.Emit($"    let inst = instantiateType(ref c, for: {w.Type(template)}, by: in args, from: 0, at: {Span(Site)})");
            w.Emit($"    showType(ref c, for: inst, by: \"{label}\", from: p0, at: i0, to: ref seen)");
            w.Emit("    print($\"work +{c.instantiationWork.length - work}\")");
            w.Emit("    showDiags(in c, from: start)");
            w.Emit(bayanAfter);
            w.Emit("}");
        }
        One("box", "Box", "Tri");
        One("again", "Box", "Tri");
        One("square", "Box", "Square");
        One("maybe", "Maybe", "Square");
        One("gen", "IGen", "Square");
        One("genLater", "IGen", "Tri", "    c.interfaceMembersDeclared = false", "    c.interfaceMembersDeclared = true",
            () => Set(c, "_interfaceMembersDeclared", false), () => Set(c, "_interfaceMembersDeclared", true));
        One("free", "Free", "Square");
        One("nested", "Box", "Both", "    c.instantiating = 1", "    c.instantiating = 0",
            () => Set(c, "_instantiating", 1), () => Set(c, "_instantiating", 0));
        // Once conformances are checked: constraints run at once and a struct instance conforms.
        One("checked", "Box", "Circle", "    c.conformancesChecked[0] = 1", before: () => Set(c, "_conformancesChecked", true));
        One("checkedBad", "Maybe", "Both");
        // The deferred checks, as CheckConformances runs them: each at its use, with the instantiation label.
        var startRun = c.Diagnostics.Count;
        foreach (var check in pending.ToList()) check();
        lines.Add("run");
        lines.AddRange(NewDiags(c, startRun));
        w.Emit("let startRun = c.diagnostics.length");
        w.Emit("for i in 0..c.pendingConstraints.length {");
        w.Emit("    let work = Work { kind: c.pendingConstraints[i].kind, subject: c.pendingConstraints[i].subject, args: List<Type>(), file: c.pendingConstraints[i].file, span: c.pendingConstraints[i].span }");
        w.Emit("    t13RunConstraint(ref c, for: in work)");
        w.Emit("}");
        w.Emit("print(\"run\")");
        w.Emit("showDiags(in c, from: startRun)");
        return (w.Code.ToString(), lines);
    }

    /// <summary>TemplateOf: a generic function's instance, a member of a generic struct's instance, and neither.</summary>
    private static (string, List<string>) TemplateOf()
    {
        var c = Compile(Generics);
        var w = new World(c, Generics);
        w.Declarations();
        var site = c.EntryModule.Files[0].Source;
        var need = w.Fn("need");
        var inst = (FunctionSymbol)Call(c, "InstantiateFunction", need, new[] { w.T("Tri") }, site, Site)!;
        var box = (StructSymbol)w.T("Box");
        var boxTri = (StructSymbol)Call(c, "InstantiateType", box, new[] { w.T("Tri") }, site, Site)!;
        var lines = new List<string>();
        string Of(FunctionSymbol f) => Call(c, "TemplateOf", f) is FunctionSymbol t ? $"{t.Name} owner {t.Owner?.DisplayName ?? "-"}" : "null";
        lines.Add($"instance {Of(inst)}");
        lines.Add($"member {Of(boxTri.Methods.Single(m => m.Name == "get"))}");
        lines.Add($"template {Of(need)}");
        lines.Add($"templateMember {Of(box.Methods.Single(m => m.Name == "get"))}");
        var get = box.Methods.Single(m => m.Name == "get");
        var getIndex = Array.IndexOf(box.Decl.Members, get.Decl);
        w.Emit("var args = List<Type>()");
        w.Emit($"args.append({w.Type(w.T("Tri"))})");
        w.Emit($"let inst = instantiateFunction(ref c, for: {w.Vars[need]}, by: in args, from: 0, at: {Span(Site)})");
        w.Emit($"let boxTri = newStruct(ref c, by: StructSymbol {{ name: \"Box\", module: 0, file: 0, declIndex: {MemberIndex(c, box.Decl)}, template: {w.Vars[box]} }})");
        w.Emit($"let instOther = newFunction(ref c, by: FunctionSymbol {{ name: \"other\", kind: .method, module: 0, file: 0, declIndex: {MemberIndex(c, box.Decl)}, memberIndex: {getIndex + 1}, owner: boxTri }})");
        w.Emit($"let instGet = newFunction(ref c, by: FunctionSymbol {{ name: \"get\", kind: .method, module: 0, file: 0, declIndex: {MemberIndex(c, box.Decl)}, memberIndex: {getIndex}, owner: boxTri }})");
        w.Emit("c.structs[boxTri].methods.append(instOther)");
        w.Emit("c.structs[boxTri].methods.append(instGet)");
        w.Emit("print($\"instance {templateText(ref c, for: inst)}\")");
        w.Emit("print($\"member {templateText(ref c, for: instGet)}\")");
        w.Emit($"print($\"template {{templateText(ref c, for: {w.Vars[need]})}}\")");
        w.Emit($"print($\"templateMember {{templateText(ref c, for: {w.Vars[get]})}}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>InvokeSymbol: one symbol per function type, with particle-less parameters.</summary>
    private static (string, List<string>) InvokeSymbol()
    {
        const string text = "union error E {\n    bad\n}\n\nvoid apply((i64, string) -> bool f, by (i64) -> i64 fails E g) {\n}\n";
        var c = Compile(text);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items));
        var w = new World(c, text);
        var e = c.EntryModule.ErrorSets.Single(s => s.Name == "E");
        w.Emit("let e = newErrorSet(ref c, by: ErrorSetSymbol { name: \"E\", module: 0, file: 0 })");
        w.Vars[e] = "e";
        var lines = new List<string>();
        var apply = c.EntryModule.Functions["apply"][0];
        foreach (var p in apply.Parameters)
        {
            var ft = (FunctionType)p.Type;
            var a = (FunctionSymbol)Call(c, "InvokeSymbol", ft)!;
            var b = (FunctionSymbol)Call(c, "InvokeSymbol", ft)!;
            lines.Add($"{a.Name} same {(ReferenceEquals(a, b) ? "yes" : "no")} kind {a.Kind} module {a.Module.Name} returns {a.ReturnType.DisplayName} fails {(a.Fails ? "yes" : "no")} sets {string.Join(",", a.ErrorSets.Select(s => s.Name))}");
            foreach (var q in a.Parameters) lines.Add($"param {q.Name} {q.Type.DisplayName} {q.Ordinal} {q.Modifier} {q.Kind}");
            var ps = w.Ids(ft.Parameters, "Type");
            var sets = w.Ids(ft.Fails, "ErrorSetId");
            w.Emit("{");
            w.Emit($"    c.functionTypes.append(FunctionData {{ parameters: move {ps}, ret: {w.Type(ft.Return)}, errorSets: move {sets}, effects: List<string>() }})");
            w.Emit("    let t = makeType(.function, at: c.functionTypes.length - 1)");
            w.Emit("    showInvoke(ref c, for: invokeSymbol(ref c, for: t), by: invokeSymbol(ref c, for: t))");
            w.Emit("}");
        }
        return (w.Code.ToString(), lines);
    }

    /// <summary>InferErrorSets and ErrorSetsOf: an instance takes its template's sets; <c>main</c> and tests fail when they propagate one.</summary>
    private static (string, List<string>) ErrorSets()
    {
        const string text = "union error E {\n    bad\n}\n\ni64 dec() fails E {\n    fail .bad\n}\n\ni64 inf() fails {\n    fail E.bad\n}\n\ni64 gen<T>(T v) fails {\n    fail E.bad\n}\n\nvoid quiet() {\n}\n\ntest \"t\" {\n    let _ = try inf()\n}\n\nlet x = try gen(1)\nprint($\"{x}\")\n";
        var c = Compile(text, new CompilationOptions(IncludeTests: true));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items));
        var w = new World(c, text);
        var e = c.EntryModule.ErrorSets.Single(s => s.Name == "E");
        w.Emit("let e = newErrorSet(ref c, by: ErrorSetSymbol { name: \"E\", module: 0, file: 0 })");
        w.Vars[e] = "e";
        var lines = new List<string>();
        foreach (var f in c.EntryModule.AllFunctions)
        {
            var sets = (IReadOnlyList<ErrorSetSymbol>)Call(c, "ErrorSetsOf", f)!;
            lines.Add($"{f.Name} {f.Kind} sets {string.Join(",", f.ErrorSets.Select(s => s.Name))} fails {(f.Fails ? "yes" : "no")} of {string.Join(",", sets.Select(s => s.Name))}");
            var main = f.Kind is FunctionKind.Main or FunctionKind.Test;
            var preset = w.Ids(f.IsInstance ? Array.Empty<object>() : f.ErrorSets.Cast<object>(), "ErrorSetId");
            var template = f.Template is { } t ? w.Vars[t] : "-1";
            var v = $"f{w.Vars.Count}";
            w.Emit($"let {v} = newFunction(ref c, by: FunctionSymbol {{ name: {Lit(f.Name)}, module: 0, file: 0, kind: {KindName(f.Kind)}, infersErrors: {(f.Kind == FunctionKind.Main ? "false" : (f.InfersErrors ? "true" : "false"))}, hasFails: {(!main && f.Fails ? "true" : "false")}, template: {template}, errorSets: move {preset} }})");
            w.Emit($"c.modules[0].allFunctions.append({v})");
            w.Vars[f] = v;
        }
        w.Emit("inferErrorSets(ref c)");
        w.Emit("for f in copyIds(in c.modules[0].allFunctions) { showSets(ref c, for: f) }");
        return (w.Code.ToString(), lines);
    }

    /// <summary>BindConstant: a constant met again while it is being bound is circular; a bound one is left alone.</summary>
    private static (string, List<string>) BindConstant()
    {
        const string text = "const a: i64 = 1\nconst b: i64 = 2\nconst d: i64 = 4\n";
        var c = Compile(text);
        var w = new World(c, text);
        var lines = new List<string>();
        var start = c.Diagnostics.Count;
        w.Emit("let start = c.diagnostics.length");
        foreach (var (name, state) in new[] { ("a", 1), ("b", 2), ("d", 0) })
        {
            var k = c.EntryModule.Constants[name];
            k.State = state;
            Call(c, "BindConstant", k);
            lines.Add($"{name} state {k.State}{(state == 1 ? $" type {k.Type.DisplayName}" : "")}");
            var index = Array.FindIndex(c.EntryModule.Files[0].Tree.Root.Members, m => m is GlobalStatement g && ReferenceEquals(g.Statement, k.Decl));
            w.Emit($"let k{name} = newConst(ref c, by: ConstSymbol {{ name: {Lit(name)}, nameSpan: {Span(k.Decl.Name.Span)}, span: {Span(k.Decl.Span)}, module: 0, file: 0, declIndex: {index}, state: {state} }})");
            w.Emit($"bindConstant(ref c, for: k{name})");
            var typeText = state == 1 ? $" type {{typeDisplay(ref c, by: c.consts[k{name}].type)}}" : "";
            w.Emit($"print($\"{name} state {{c.consts[k{name}].state}}{typeText}\")");
        }
        lines.AddRange(NewDiags(c, start));
        w.Emit("showDiags(in c, from: start)");
        return (w.Code.ToString(), lines);
    }

    /// <summary>ReportHeld: into Diagnostics, or into the bag a ReportInto made the sink.</summary>
    private static (string, List<string>) ReportHeld()
    {
        const string text = "void f() {\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        var site = c.EntryModule.Files[0].Source;
        Diagnostic D(string code, int at, string message) => new(Severity.Error, code, site, new TextSpan(at, 1), message, "held", null);
        string B(string code, int at, string message) =>
            $"held.append(Diag {{ code: {Lit(code)}, file: 0, span: {Span(new TextSpan(at, 1))}, message: {Lit(message)}, label: \"held\", fixSpan: {Span(default)}, related: List<Related>() }})";
        var lines = new List<string>();
        var start = c.Diagnostics.Count;
        Call(c, "ReportHeld", new List<Diagnostic> { D("BYN2006", 0, "one"), D("BYN2007", 5, "two") });
        var bag = new DiagnosticBag();
        var outer = Call(c, "ReportInto", bag);
        Call(c, "ReportHeld", new List<Diagnostic> { D("BYN2008", 9, "three") });
        Call(c, "ReportInto", outer);
        lines.AddRange(NewDiags(c, start));
        lines.Add("bag");
        lines.AddRange(bag.Items.Select(Line));
        w.Emit("let start = c.diagnostics.length");
        w.Emit("{");
        w.Emit("    var held = List<Diag>()");
        w.Emit("    " + B("BYN2006", 0, "one"));
        w.Emit("    " + B("BYN2007", 5, "two"));
        // C# has no `_quiet`; `AddRange` (Compilation.cs:1919) is ungated, so held diagnostics arrive while Bayan's
        // `c.quiet` is set (Q580). The expected lines are C#'s, run without it.
        w.Emit("    c.quiet = true");
        w.Emit("    reportHeld(ref c, for: in held)");
        w.Emit("    c.quiet = false");
        w.Emit("}");
        w.Emit("c.bags.append(DiagBag { items: List<Diag>() })");
        w.Emit("let bag = c.bags.length - 1");
        w.Emit("let outer = c.sink");
        w.Emit("c.sink = bag");
        w.Emit("{");
        w.Emit("    var held = List<Diag>()");
        w.Emit("    " + B("BYN2008", 9, "three"));
        w.Emit("    reportHeld(ref c, for: in held)");
        w.Emit("}");
        w.Emit("c.sink = outer");
        w.Emit("showDiags(in c, from: start)");
        w.Emit("print(\"bag\")");
        w.Emit("for d in c.bags[bag].items { print(diagLine(in c, for: in d)) }");
        return (w.Code.ToString(), lines);
    }

    /// <summary>FieldDefault and ParameterDefault: bound on first use, only where the syntax has a default.</summary>
    private static (string, List<string>) Defaults()
    {
        const string text = "/// A box.\nstruct Box<T> {\n    T item\n    i64 n = 1\n}\n\nstruct Plain {\n    i64 k = 5\n    i64 j\n}\n\ni64 pd(i64 a, by i64 b = 2) => a + b\n";
        var c = Compile(text);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items));
        var w = new World(c, text);
        w.Declarations();
        var site = c.EntryModule.Files[0].Source;
        var lines = new List<string>();
        // Only whether a default was bound: what it binds to is the binder's (T15) business.
        static string Const(BoundExpr? e) => e is null ? "null" : "bound";
        var box = (StructSymbol)w.T("Box");
        var inst = (StructSymbol)Call(c, "InstantiateType", box, new TypeSymbol[] { PrimitiveType.I64 }, site, Site)!;
        // C#'s `Doc => Decl.Doc` (Symbols.cs:100): an instance shares its template's doc.
        lines.Add($"instance doc {inst.Doc ?? "null"}");
        foreach (var f in inst.Fields) lines.Add($"instance {f.Name} {Const((BoundExpr?)Call(c, "FieldDefault", inst, f, site, Site))}");
        var plain = (StructSymbol)w.T("Plain");
        foreach (var f in plain.Fields) lines.Add($"plain {f.Name} {(Call(c, "FieldDefault", plain, f, site, Site) is null ? "null" : "set")}");
        var pd = w.Fn("pd");
        foreach (var p in pd.Parameters) lines.Add($"param {p.Name} {Const((BoundExpr?)Call(c, "ParameterDefault", pd, p))}");
        lines.Add($"again {(ReferenceEquals(Call(c, "ParameterDefault", pd, pd.Parameters[1]), Call(c, "ParameterDefault", pd, pd.Parameters[1])) ? "same" : "new")}");
        w.Emit("var args = List<Type>()");
        w.Emit("args.append(prim(PrimitiveKind.i64))");
        if (box.Doc is { } doc) w.Emit($"c.structs[{w.Vars[box]}].doc = {Lit(doc)}");
        w.Emit($"let inst = StructId(typeIndex(instantiateType(ref c, for: {w.Type(box)}, by: in args, from: 0, at: {Span(Site)})))");
        w.Emit("print($\"instance doc {c.structs[inst].doc ?? \"null\"}\")");
        w.Emit("for f in copyIds(in c.structs[inst].fields) { print($\"instance {c.fields[f].name} {constText(ref c, for: fieldDefault(ref c, for: inst, by: f, from: 0, at: TextSpan { start: 0, length: 0 }))}\") }");
        // Plain's own defaults were bound by BindAll before this runs; the template is returned as it stands.
        foreach (var f in plain.Fields)
        {
            var fv = $"field{f.Name}";
            w.Emit($"let {fv} = newField(ref c, by: FieldSymbol {{ name: {Lit(f.Name)}, owner: {w.Vars[plain]}, file: 0, hasDefault: {(f.HasDefault ? "true" : "false")}, defaultValue: {(f.Default is null ? "-1" : "addErrorExpr(ref c, from: BErrorExpr { span: TextSpan { start: 0, length: 0 } })")} }})");
            w.Emit($"print($\"plain {f.Name} {{fieldDefault(ref c, for: {w.Vars[plain]}, by: {fv}, from: 0, at: TextSpan {{ start: 0, length: 0 }}) == -1 ? \"null\" : \"set\"}}\")");
        }
        var fn = w.Vars[pd];
        foreach (var p in pd.Parameters)
        {
            var pv = $"param{p.Name}";
            w.Emit($"let {pv} = newLocal(ref c, by: LocalSymbol {{ name: {Lit(p.Name)}, type: prim(PrimitiveKind.i64), kind: .parameter, isParameter: true, ordinal: {p.Ordinal} }})");
            w.Emit($"c.functions[{fn}].parameters.append({pv})");
        }
        foreach (var p in pd.Parameters)
            w.Emit($"print($\"param {p.Name} {{constText(ref c, for: parameterDefault(ref c, for: {fn}, by: param{p.Name}) ?? -1)}}\")");
        w.Emit($"let first = parameterDefault(ref c, for: {fn}, by: param{pd.Parameters[1].Name}) ?? -1");
        w.Emit($"let second = parameterDefault(ref c, for: {fn}, by: param{pd.Parameters[1].Name}) ?? -1");
        w.Emit("print($\"again {first == second ? \"same\" : \"new\"}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>
    /// BindFunction: which body each kind of function binds through (a getter through its property's declaration),
    /// the locals it declares, and what the first pass held and then reported (no seeds).
    /// </summary>
    private static (string, List<string>) BindFunction()
    {
        const string text = "struct Q {\n    i64 v\n    i64 side => 1\n    i64 get() => 2\n}\n\ni64 top() => 3\n\nvoid bad() {\n    let z: Nope = 1\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        w.Declarations();
        var q = (StructSymbol)w.T("Q");
        return BindFunctions(c, w, q.Methods.Concat(q.Properties.Select(p => p.Getter!)).Append(w.Fn("top")).Append(w.Fn("bad")).ToList());
    }

    /// <summary>
    /// BindFunction on the synthesized main (its two top-level blocks, the second with a BYN2202) and on each case of a
    /// <c>@cases</c> test (the second case mismatched), through T15's <c>bindMain</c>/<c>bindTest</c> (Q718).
    /// </summary>
    private static (string, List<string>) BindMainAndCases()
    {
        const string text = "{\n    let a: i64 = 1\n}\n\n{\n    let q: bool = 3\n}\n\n@cases((1, true), (2, 3))\ntest \"t\"(i32 n, bool f) {\n    let y: i64 = 2\n}\n";
        var c = Compile(text, new CompilationOptions(IncludeTests: true));
        var w = new World(c, text);
        var members = c.EntryModule.Files[0].Tree.Root.Members;
        var main = c.EntryModule.AllFunctions.Single(f => f.Kind == FunctionKind.Main);
        w.Function(main, -1);
        w.Emit($"c.mainStatementsFn = {w.Vars[main]}");
        for (var i = 0; i < members.Length; i++)
            if (members[i] is GlobalStatement) w.Emit($"c.mainStatements.append(SyntaxRef {{ file: 0, declIndex: {i} }})");
        var bodies = (System.Collections.IDictionary)Get(c, "_testBodies")!;
        var tests = c.EntryModule.AllFunctions.Where(f => f.Kind == FunctionKind.Test).ToList();
        foreach (var t in tests)
        {
            var body = bodies[t]!;
            var decl = (TestDecl)body.GetType().GetField("Item1")!.GetValue(body)!;
            var values = (Expr[])body.GetType().GetField("Item2")!.GetValue(body)!;
            var d = Array.IndexOf(members, decl);
            var a = Array.FindIndex(decl.Attributes, x => x.Name.Text == "cases");
            var k = Array.FindIndex(decl.Attributes[a].Args, x => ReferenceEquals(((TupleExpr)x.Value).Elements[0].Value, values[0]));
            w.Function(t, d);
            w.Emit($"c.testBodies[{w.Vars[t]}] = TestBody {{ decl: SyntaxRef {{ file: 0, declIndex: {d} }}, hasValues: true, values: SyntaxRef {{ file: 0, declIndex: {d}, memberIndex: {a}, itemIndex: {k} }} }}");
        }
        return BindFunctions(c, w, tests.Prepend(main).ToList());
    }

    private static (string, List<string>) BindFunctions(Compilation c, World w, List<FunctionSymbol> fns)
    {
        var lines = new List<string>();
        var invoke = (FunctionSymbol)Call(c, "InvokeSymbol", new FunctionType([], PrimitiveType.I64, [], []))!;
        var errors = (System.Collections.ICollection)Get(c, "_instantiationErrors")!;
        var held = errors.Count;
        foreach (var f in fns.Append(invoke))
        {
            f.Body = null;
            var locals = f.Locals.Count;
            var start = c.Diagnostics.Count;
            Call(c, "BindFunction", f);
            lines.Add($"{f.Name} {f.Kind} body {(f.Body is null ? "no" : "yes")}");
            foreach (var l in f.Locals.Skip(locals)) lines.Add($"local {l.Name} {l.Type.DisplayName}");
            lines.AddRange(NewDiags(c, start));
        }
        lines.Add($"instantiationErrors +{errors.Count - held}");
        w.Emit("c.functionTypes.append(FunctionData { parameters: List<Type>(), ret: prim(PrimitiveKind.i64), errorSets: List<ErrorSetId>(), effects: List<string>() })");
        w.Emit("let invoke = invokeSymbol(ref c, for: makeType(.function, at: c.functionTypes.length - 1))");
        foreach (var f in fns) w.Emit($"showBody(ref c, for: {w.Vars[f]})");
        w.Emit("showBody(ref c, for: invoke)");
        w.Emit("print($\"instantiationErrors +{c.instantiationErrors.length}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>
    /// CheckRequires over the calls the C# binder recorded: the clause, the argument it blames, or the whole call.
    /// In <c>two</c> the default <c>0</c> (callee's file) has the span of the written <c>1</c> (caller's file), so only
    /// the file tells them apart and C# blames the call.
    /// </summary>
    private static (string, List<string>) Requires()
    {
        const string one = "i32 positive(i32 n) expects n > 0 && n < 1000 => n\n\ni32 pair(i32 a, by i32 b = 0) expects b > 0 => a\n\nlet x = positive(0)\nlet y = positive(5)\nlet z = pair(1)\nlet v = pair(x, by: 1)\nlet u = pair(2, by: 0)\nprint($\"{x}{y}{z}{v}{u}\")\n";
        const string callee = "i32 pair(i32 a, by i32 b = 0) expects b > 0 => a\n";
        const string caller = "let pd = 1234\nlet q = pair(1)\nprint($\"{pd}{q}\")\n";
        var sb = new StringBuilder();
        var lines = new List<string>();
        foreach (var (label, texts) in new[] { ("one", new[] { one }), ("two", new[] { caller, callee }) })
        {
            var sources = texts.Select((t, i) => SourceText.FromString(Path.Combine(Root, $"t13r{i}.byn"), t)).ToList();
            var c = Compilation.Create(sources, Root, new CompilationOptions());
            var files = c.EntryModule.Files.Select(f => f.Source).ToList();
            string Text(SourceText src) => texts[sources.FindIndex(x => ReferenceEquals(x, src))];
            var w = new World(c, Text(files[0]));
            for (var i = 1; i < files.Count; i++) w.Emit($"addSource(ref c, for: {Lit(Text(files[i]))}, as: {Lit(c.EntryModule.Name)})");
            int FileOf(SourceText src) => files.FindIndex(x => ReferenceEquals(x, src));
            lines.Add(label);
            lines.AddRange(c.Diagnostics.Items.Where(d => d.Code == "BYN2321").Select(Line));
            var checks = (System.Collections.IEnumerable)Get(c, "RequiresChecks")!;
            var n = 0;
            // An argument is read only for its constant and its span, so it is built as a literal carrying both.
            string Expr(BoundExpr e, bool argument = false)
            {
                var v = $"e{n++}";
                switch (argument ? null : e)
                {
                    case BoundBinary b:
                        var l = Expr(b.Left);
                        var r = Expr(b.Right);
                        var op = char.ToLowerInvariant(b.Op.ToString()[0]) + b.Op.ToString()[1..];
                        w.Emit($"let {v} = addBinary(ref c, from: BBinary {{ span: {Span(b.Syntax.Span)}, op: .{op}, left: {l}, right: {r}, type: prim(PrimitiveKind.bool) }})");
                        break;
                    case BoundLocal loc:
                        w.Emit($"let {v} = addLocal(ref c, from: BLocal {{ span: {Span(loc.Syntax.Span)}, local: {w.Vars[loc.Local]} }})");
                        break;
                    default:
                        w.Emit($"let {v} = addLiteral(ref c, from: BLiteral {{ span: {Span(e.Syntax.Span)}, type: prim(PrimitiveKind.i32), value: Constant {{ kind: .integer }} }})");
                        break;
                }
                if (e.Constant is { Value: Int128 i }) w.Emit($"setConstant(ref c, for: {v}, to: Constant {{ kind: .integer, integer: bigFromI64({(long)i}) }})");
                else if (e.Constant is { Value: bool f }) w.Emit($"setConstant(ref c, for: {v}, to: Constant {{ kind: .flag, flag: {(f ? "true" : "false")} }})");
                return v;
            }
            foreach (var check in checks)
            {
                var fn = (FunctionSymbol)Prop(check, "Callee")!;
                var calleeFile = FileOf(fn.File!.Source);
                if (!w.Vars.ContainsKey(fn))
                {
                    var ps = new List<string>();
                    foreach (var p in fn.Parameters)
                    {
                        var pv = $"p{w.Vars.Count}";
                        w.Emit($"let {pv} = newLocal(ref c, by: LocalSymbol {{ name: {Lit(p.Name)}, type: prim(PrimitiveKind.i32), kind: .parameter, isParameter: true, ordinal: {p.Ordinal} }})");
                        w.Vars[p] = pv;
                        ps.Add(pv);
                    }
                    var fv = $"callee{w.Vars.Count}";
                    w.Emit($"let {fv} = newFunction(ref c, by: FunctionSymbol {{ name: {Lit(fn.Name)}, module: 0, file: {calleeFile} }})");
                    foreach (var pv in ps) w.Emit($"c.functions[{fv}].parameters.append({pv})");
                    foreach (var clause in fn.Requires) w.Emit($"c.functions[{fv}].requirements.append({Expr(clause)})");
                    w.Vars[fn] = fv;
                }
                var call = (CallExpr)Prop(check, "Call")!;
                var args = (BoundExpr[])Prop(check, "Arguments")!;
                var source = FileOf((SourceText)Prop(check, "Source")!);
                w.Emit("{");
                w.Emit("    var callArgs = List<Location>()");
                foreach (var a in call.Args) w.Emit($"    callArgs.append(Location {{ file: {source}, span: {Span(a.Value.Span)} }})");
                w.Emit("    var arguments = List<BExpr>()");
                w.Emit("    var argumentFiles = List<FileId>()");
                foreach (var a in args)
                {
                    w.Emit($"    arguments.append({Expr(a, argument: true)})");
                    // An argument not written in the call is the parameter's default, bound in the callee's file (Compilation.cs:1904).
                    w.Emit($"    argumentFiles.append({(call.Args.Any(x => ReferenceEquals(x.Value, a.Syntax)) ? source : calleeFile)})");
                }
                w.Emit($"    c.requiresChecks.append(RequiresCheck {{ source: {source}, callSpan: {Span(call.Span)}, callArgs: move callArgs, callee: {w.Vars[fn]}, arguments: move arguments, argumentFiles: move argumentFiles }})");
                w.Emit("}");
            }
            w.Emit("checkRequires(ref c)");
            w.Emit($"print(\"{label}\")");
            w.Emit("showDiags(in c, from: 0)");
            sb.Append("    {\n").Append(w.Code).Append("    }\n");
        }
        return (sb.ToString(), lines);
    }

    /// <summary>BindAll's last step: BYN2014 only for an entry module with files, no main, no tests asked for, not a library, no errors.</summary>
    private static (string, List<string>) NoMain()
    {
        const string plain = "struct P {\n    i64 x\n}\n";
        const string twice = "struct P {\n    i64 x\n}\n\nstruct P {\n    i64 y\n}\n";
        var sb = new StringBuilder();
        var lines = new List<string>();
        foreach (var (label, text, options, flags) in new[]
        {
            ("app", plain, new CompilationOptions(), ""),
            ("library", plain, new CompilationOptions(Library: true), "c.library = true"),
            ("tests", plain, new CompilationOptions(IncludeTests: true), "c.includeTests = true"),
            ("errors", twice, new CompilationOptions(), ""),
        })
        {
            var c = Compile(text, options);
            lines.Add(label);
            var held = c.Diagnostics.Items.Where(d => d.Code != "BYN2014").ToList();
            lines.AddRange(c.Diagnostics.Items.Select(Line));
            var w = new World(c, text);
            w.Emit(flags);
            foreach (var d in held)
                w.Emit($"c.diagnostics.append(Diag {{ code: {Lit(d.Code)}, file: 0, span: {Span(d.Span)}, message: {Lit(d.Message)}, label: {Lit(d.Label ?? "")}, help: {Lit(d.Help ?? "")}, fixSpan: {Span(default)}, related: List<Related>() }})");
            w.Emit("bindAll(ref c)");
            w.Emit($"print(\"{label}\")");
            w.Emit("showDiags(in c, from: 0)");
            sb.Append("    {\n").Append(w.Code).Append("    }\n");
        }
        return (sb.ToString(), lines);
    }

    /// <summary>BindAll with exit codes and a test body: each is drained and bound. Main statements and `@cases` values are in <c>bindMainAndCases</c>.</summary>
    private static (string, List<string>) BindAllDrains()
    {
        const string text = "union error E {\n    @exitCode(42)\n    bad\n}\n\nvoid run() {\n}\n\ntest \"t\" {\n    let y = 2\n}\n";
        var c = Compile(text, new CompilationOptions(IncludeTests: true));
        var w = new World(c, text);
        var lines = new List<string>();
        var v = c.EntryModule.ErrorSets.SelectMany(s => s.Variants).Single(vr => vr.Name == "bad");
        lines.Add($"exitCode {v.ExitCode}");
        w.Emit("let es = newErrorSet(ref c, by: ErrorSetSymbol { name: \"E\", module: 0, file: 0, declIndex: 0 })");
        w.Emit("let ev = newErrorVariant(ref c, by: ErrorVariantSymbol { name: \"bad\", file: 0, declIndex: 0, set: es })");
        w.Emit("c.errorSets[es].variants.append(ev)");
        w.Emit("c.exitCodes.append(ExitCodeRef { variant: ev, attribute: SyntaxRef { file: 0, declIndex: 0, memberIndex: 0, itemIndex: 0 } })");
        var tFn = c.EntryModule.AllFunctions.Single(f => f.Kind == FunctionKind.Test);
        lines.Add($"test body {(tFn.Body is null ? "no" : "yes")}");
        w.Emit("let fnTest = newFunction(ref c, by: FunctionSymbol { name: \"test_t\", module: 0, file: 0, kind: .testKeyword })");
        w.Emit("c.modules[0].allFunctions.append(fnTest)");
        w.Emit("c.testBodies[fnTest] = TestBody { decl: SyntaxRef { file: 0, declIndex: 2 }, values: SyntaxRef {} }");
        w.Emit("bindAll(ref c)");
        w.Emit("print($\"exitCode {c.errorVariants[ev].exitCode}\")");
        w.Emit("print($\"test body {(c.functions[fnTest].body != -1 ? \"yes\" : \"no\")}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>
    /// BindAll's module-constant and field-default loops: every constant is bound (one with no value reports BYN2101),
    /// and only a field with a default gets one. What a default binds to is T15/T17's business, so only whether it is set.
    /// </summary>
    private static (string, List<string>) BindAllConstantsAndDefaults()
    {
        const string text = "const a: i64 = 1\nconst b: i64\n\nstruct P {\n    i64 x = 3\n    i64 y\n}\n";
        var c = Compile(text, new CompilationOptions(Library: true));
        var w = new World(c, text);
        var lines = new List<string>();
        var members = c.EntryModule.Files[0].Tree.Root.Members;
        w.Emit("c.library = true");
        foreach (var name in new[] { "a", "b" })
        {
            var k = c.EntryModule.Constants[name];
            lines.Add($"const {name} state {k.State} type {k.Type.DisplayName}");
            var index = Array.FindIndex(members, m => m is GlobalStatement g && ReferenceEquals(g.Statement, k.Decl));
            w.Emit($"let k{name} = newConst(ref c, by: ConstSymbol {{ name: {Lit(name)}, nameSpan: {Span(k.Decl.Name.Span)}, span: {Span(k.Decl.Span)}, module: 0, file: 0, declIndex: {index} }})");
            w.Emit($"c.modules[0].allConstants.append(k{name})");
        }
        var p = (StructSymbol)c.EntryModule.Types["P"];
        w.Emit($"let st = newStruct(ref c, by: StructSymbol {{ name: \"P\", nameSpan: {Span(p.Location.Span)}, span: {Span(p.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(c, p.Decl)} }})");
        w.Emit("c.modules[0].structs.append(st)");
        foreach (var f in p.Fields)
        {
            lines.Add($"field {f.Name} default {(f.Default is null ? "null" : "set")}");
            w.Emit($"c.structs[st].fields.append(newField(ref c, by: FieldSymbol {{ name: {Lit(f.Name)}, owner: st, file: 0, declIndex: {MemberIndex(c, p.Decl)}, memberIndex: {Array.IndexOf(p.Decl.Members, f.Decl)}, type: prim(PrimitiveKind.i64), hasDefault: {(f.HasDefault ? "true" : "false")} }}))");
        }
        lines.AddRange(NewDiags(c, 0));
        w.Emit("bindAll(ref c)");
        foreach (var name in new[] { "a", "b" })
            w.Emit($"print($\"const {name} state {{c.consts[k{name}].state}} type {{c.consts[k{name}].type == -1 ? \"unset\" : typeDisplay(ref c, by: c.consts[k{name}].type)}}\")");
        w.Emit("for f in copyIds(in c.structs[st].fields) { print($\"field {c.fields[f].name} default {c.fields[f].defaultValue == -1 ? \"null\" : \"set\"}\") }");
        w.Emit("showDiags(in c, from: 0)");
        return (w.Code.ToString(), lines);
    }

    /// <summary>
    /// BindAll's drain over the methods an instantiated struct queued (Compilation.cs:1637, no <c>Instance</c>): C# runs
    /// <c>Instantiating(work.Site, work.Span, work.Bind, work.Instance)</c> for each; Bayan's `bindAll` binds the Work's
    /// <c>instance</c>. No error: a held one makes `bindAll` panic (<c>HeldInstantiationErrorIsInternalError</c>).
    /// </summary>
    private static (string, List<string>) DrainInstanceMethods()
    {
        const string text = "struct Tri {\n    i64 n\n}\n\nstruct Box<T> {\n    T v\n    void get() {\n        let k: i64 = 1\n    }\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        w.Declarations();
        w.Emit("c.library = true");
        var site = c.EntryModule.Files[0].Source;
        var errors = (System.Collections.ICollection)Get(c, "_instantiationErrors")!;
        var held = errors.Count;
        var inst = (StructSymbol)Call(c, "InstantiateType", w.T("Box"), new[] { w.T("Tri") }, site, Site)!;
        var queue = Get(c, "_instantiationWork")!;
        var start = c.Diagnostics.Count;
        var lines = new List<string>();
        while (WorkCount(c) > 0)
        {
            var work = queue.GetType().GetMethod("Dequeue")!.Invoke(queue, null)!;
            var item = (string n) => work.GetType().GetField(n)!.GetValue(work);
            lines.Add($"instance {(item("Item4") is FunctionSymbol f ? f.Name : "null")}");
            Call(c, "Instantiating", item("Item1"), item("Item2"), item("Item3"), item("Item4"));
        }
        foreach (var m in inst.Methods)
        {
            lines.Add($"{m.Name} {m.Kind} body {(m.Body is null ? "no" : "yes")}");
            foreach (var l in m.Locals) lines.Add($"local {l.Name} {l.Type.DisplayName}");
        }
        lines.AddRange(NewDiags(c, start));
        lines.Add($"instantiationErrors +{errors.Count - held}");
        w.Emit("var args = List<Type>()");
        w.Emit($"args.append({w.Type(w.T("Tri"))})");
        w.Emit($"let inst = instantiateType(ref c, for: {w.Type(w.T("Box"))}, by: in args, from: 0, at: {Span(Site)})");
        w.Emit("for x in c.instantiationWork { print($\"instance {x.kind == .bindFunction && x.memberIndex != -1 ? c.functions[x.memberIndex].name : \"null\"}\") }");
        w.Emit("let start = c.diagnostics.length");
        w.Emit("bindAll(ref c)");
        w.Emit("let ms = copyIds(in c.structs[typeIndex(inst)].methods)");
        w.Emit("for m in ms {");
        w.Emit("    print($\"{c.functions[m].name} {kindText(c.functions[m].kind)} body {yn(b: c.functions[m].body != -1)}\")");
        w.Emit("    let ls = copyIds(in c.functions[m].locals)");
        w.Emit("    for l in ls { print($\"local {c.locals[l].name} {typeDisplay(ref c, by: c.locals[l].type)}\") }");
        w.Emit("}");
        w.Emit("showDiags(in c, from: start)");
        w.Emit("print($\"instantiationErrors +{c.instantiationErrors.length}\")");
        return (w.Code.ToString(), lines);
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private static readonly Lazy<(Dictionary<string, List<string>> Expected, Dictionary<string, List<string>> Actual)> Run = new(() =>
    {
        var expected = new Dictionary<string, List<string>>();
        var main = new StringBuilder(Prelude);
        foreach (var (name, build) in All)
        {
            var (bayan, lines) = build();
            expected[name] = lines;
            main.Append($"void section{char.ToUpperInvariant(name[0])}{name[1..]}() {{\n    print(\"== {name}\")\n{bayan}}}\n\n");
        }
        main.Append("i32 main() {\n");
        foreach (var (name, _) in All) main.Append($"    section{char.ToUpperInvariant(name[0])}{name[1..]}()\n");
        main.Append("    return 0\n}\n");
        File.WriteAllText(Path.Combine(Root, "main.byn"), main.ToString());
        return (expected, SelfhostSemanticsHarness.RunSections(main.ToString()));
    });

    private const string Prelude = """
        import lexer
        import parser

        string yn(bool b) => b ? "yes" : "no"

        Ctx newCtx() => Ctx {
            files: List<SourceFile>(),
            modules: List<Mod>(),
            diagnostics: List<Diag>(),
            bags: List<DiagBag>(),
        }

        void addSource(ref Ctx c, for string text, as string module) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            var copy = List<u8>()
            for b in text.bytes { copy.append(b) }
            let source = lexer.SourceText.create("t13.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            c.files.append(SourceFile { path: "t13.byn", displayPath: "t13.byn", text: move copy, unit: move unit, module: 0, isEntry: true, imports: Map<string, ModId>() })
            var files = List<FileId>()
            files.append(c.files.length - 1)
            c.modules.append(Mod { name: module, files: move files })
        }

        string lineCol(in Ctx c, for FileId f, at TextSpan s) {
            if f < 0 { return "-" }
            var line: i64 = 1
            var col: i64 = 1
            for i in 0..s.start {
                if c.files[f].text[i] == 10 {
                    line += 1
                    col = 1
                } else {
                    col += 1
                }
            }
            return $"{line}:{col}"
        }

        string diagLine(in Ctx c, for in Diag d) {
            var sb = StrBuf()
            sb.append($"{d.code} {lineCol(in c, for: d.file, at: d.span)} {d.message} | {d.label} | {d.help}")
            for r in d.related { sb.append($" | rel {lineCol(in c, for: d.file, at: r.span)} {r.label}") }
            return $"{sb.finish()}"
        }

        void showDiags(in Ctx c, from i64 start) {
            for i in start..c.diagnostics.length { print(diagLine(in c, for: in c.diagnostics[i])) }
        }

        string typesText(ref Ctx c, for in List<Type> ts) {
            var sb = StrBuf()
            for i in 0..ts.length {
                if i > 0 { sb.append(",") }
                sb.append(typeDisplay(ref c, by: ts[i]))
            }
            return $"{sb.finish()}"
        }

        string mapText(ref Ctx c, for in Map<string, Type> m) {
            var keys = List<string>()
            for (k, v) in m { keys.append(k) }
            var sb = StrBuf()
            for i in 0..keys.length {
                if i > 0 { sb.append(",") }
                sb.append($"{keys[i]}={typeDisplay(ref c, by: m[keys[i]])}")
            }
            return $"{sb.finish()}"
        }

        bool seenBefore(ref List<i64> seen, for i64 x) {
            for s in seen {
                if s == x { return true }
            }
            seen.append(x)
            return false
        }

        void showInstance(ref Ctx c, for FuncId inst, by string label, from i64 fns, at i64 work, to ref List<FuncId> seen) {
            let fresh = !seenBefore(ref seen, for: inst)
            let args = copyIds(in c.functions[inst].typeArgs)
            var map = Map<string, Type>()
            for (k, v) in c.functions[inst].typeMap { map[k] = v }
            let queued = c.instantiationWork.length - work
            var selfText = ""
            if queued > 0 {
                let last = c.instantiationWork[c.instantiationWork.length - 1].instance
                selfText = $" self {yn(b: last == inst)}"
            }
            let template = c.functions[c.functions[inst].template].name
            print($"{label} new {yn(b: fresh)} {c.functions[inst].name} of {template} args {typesText(ref c, for: in args)} map {mapText(ref c, for: in map)} fns +{c.modules[0].allFunctions.length - fns} work +{queued}{selfText}")
        }

        void showPass(ref Ctx c) {
            let pass = c.firstPass
            var sb = StrBuf()
            let instances = copyIds(in c.firstPasses[pass].instances)
            for i in 0..instances.length {
                if i > 0 { sb.append(",") }
                let args = copyIds(in c.functions[instances[i]].typeArgs)
                sb.append($"{c.functions[instances[i]].name}<{typesText(ref c, for: in args)}>")
            }
            print($"pass instances {sb.finish()} errors {c.firstPasses[pass].errors.length}")
            let bag = c.firstPasses[pass].bag
            for d in c.bags[bag].items { print($"pass {diagLine(in c, for: in d)}") }
        }

        void showType(ref Ctx c, for Type inst, by string label, from i64 p0, at i64 i0, to ref List<Type> seen) {
            let fresh = !seenBefore(ref seen, for: inst)
            var map = Map<string, Type>()
            let id = typeIndex(inst)
            switch kindOf(inst) {
                case .structType:
                    for (k, v) in c.structs[id].typeMap { map[k] = v }
                case .unionType:
                    for (k, v) in c.unions[id].typeMap { map[k] = v }
                case .interfaceType:
                    for (k, v) in c.interfaces[id].typeMap { map[k] = v }
                default:
                    panic("not an instance")
            }
            print($"{label} new {yn(b: fresh)} {typeDisplay(ref c, by: inst)} map {mapText(ref c, for: in map)} pending +{c.pendingConstraints.length - p0} interfaces +{c.pendingInterfaces.length - i0}")
        }

        string templateText(ref Ctx c, for FuncId f) {
            if (templateOf(ref c, for: f) is { } t) {
                let owner = c.functions[t].owner
                let shown = owner == -1 ? "-" : typeDisplay(ref c, by: structTypeOf(owner))
                return $"{c.functions[t].name} owner {shown}"
            }
            return "null"
        }

        string constText(ref Ctx c, for BExpr e) => e == -1 ? "null" : "bound"

        void showBody(ref Ctx c, for FuncId f) {
            let locals = c.functions[f].locals.length
            let start = c.diagnostics.length
            bindFunction(ref c, for: f)
            print($"{c.functions[f].name} {kindText(c.functions[f].kind)} body {yn(b: c.functions[f].body != -1)}")
            for i in locals..c.functions[f].locals.length {
                let l = c.functions[f].locals[i]
                print($"local {c.locals[l].name} {typeDisplay(ref c, by: c.locals[l].type)}")
            }
            showDiags(in c, from: start)
        }

        string kindText(FunctionKind k) => k switch {
            .function => "Function",
            .method => "Method",
            .constructor => "Constructor",
            .getter => "Getter",
            .main => "Main",
            .testKeyword => "Test",
            .builtin => "Builtin",
            .lambda => "Lambda",
            .invoke => "Invoke",
        }

        string setsText(ref Ctx c, for in List<ErrorSetId> sets) {
            var sb = StrBuf()
            for i in 0..sets.length {
                if i > 0 { sb.append(",") }
                sb.append(c.errorSets[sets[i]].name)
            }
            return $"{sb.finish()}"
        }

        void showInvoke(ref Ctx c, for FuncId a, by FuncId b) {
            let sets = copyIds(in c.functions[a].errorSets)
            let module = c.modules[c.functions[a].module].name
            print($"{c.functions[a].name} same {yn(b: a == b)} kind {kindText(c.functions[a].kind)} module {module} returns {typeDisplay(ref c, by: c.functions[a].returnType)} fails {yn(b: c.functions[a].hasFails)} sets {setsText(ref c, for: in sets)}")
            let ps = copyIds(in c.functions[a].parameters)
            for p in ps {
                let modifier = c.locals[p].modifier == .none ? "None" : "other"
                let kind = c.locals[p].kind == .parameter && c.locals[p].isParameter ? "Parameter" : "other"
                print($"param {c.locals[p].name} {typeDisplay(ref c, by: c.locals[p].type)} {c.locals[p].ordinal} {modifier} {kind}")
            }
        }

        void showSets(ref Ctx c, for FuncId f) {
            let sets = copyIds(in c.functions[f].errorSets)
            let of = errorSetsOf(ref c, for: f)
            print($"{c.functions[f].name} {kindText(c.functions[f].kind)} sets {setsText(ref c, for: in sets)} fails {yn(b: c.functions[f].hasFails)} of {setsText(ref c, for: in of)}")
        }


        """;

    /// <summary>
    /// BindAll's last check: an instantiation error held while nothing else failed is a compiler bug, so
    /// BindAll throws (C#) or panics (Bayan) with the same text. Its own build, as the program does not exit 0.
    /// </summary>
    [Fact]
    public void HeldInstantiationErrorIsInternalError()
    {
        const string text = "struct P {\n    i64 x\n}\n";
        var c = Compile(text);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items));
        var site = c.EntryModule.Files[0].Source;
        ((List<Diagnostic>)Get(c, "_instantiationErrors")!).Add(new Diagnostic(Severity.Error, "BYN2006", site, new TextSpan(7, 1), "held"));
        var expected = Assert.Throws<InvalidOperationException>(() => Call(c, "BindAll")).Message;
        var w = new World(c, text);
        w.Emit($"c.files[0].path = {Lit(site.Path)}");
        w.Emit("c.library = true");
        w.Emit($"c.instantiationErrors.append(Diag {{ code: \"BYN2006\", file: 0, span: {Span(new TextSpan(7, 1))}, message: \"held\", fixSpan: {Span(default)}, related: List<Related>() }})");
        w.Emit("bindAll(ref c)");
        var main = Prelude + "i32 main() {\n" + w.Code + "    return 0\n}\n";
        var (exit, _, err) = SelfhostParserTests.Run(SelfhostSemanticsHarness.Build(main));
        Assert.NotEqual(0, exit);
        Assert.Equal("panic: " + expected, err.Split('\n')[0]);
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in All) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void SectionMatchesCSharp(string name) =>
        SelfhostSemanticsHarness.AssertSection(Run.Value.Actual, name, Run.Value.Expected[name]);
}
