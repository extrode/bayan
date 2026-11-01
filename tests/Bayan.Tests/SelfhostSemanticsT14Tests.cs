using System.Collections;
using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using TextSpan = Bayan.Syntax.Text.TextSpan;

namespace Bayan.Tests;

/// <summary>
/// T14: <c>selfhost/semantics/conformance.byn</c> against the C# <c>Compilation</c> (Conformance.cs).
/// Each section compiles a source with the C# <c>Compilation.Create</c> and takes the oracle's lines from it,
/// directly or by driving the C# method under test through reflection; the same scenario is emitted as Bayan
/// code that builds the matching symbols (from the C# ones) and calls the port, compared line by line.
/// Diagnostics print as <c>code line:col message | label | help</c> plus the fix, when there is one.
/// </summary>
public sealed class SelfhostSemanticsT14Tests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>The codes Conform and its pending-constraint drain report.</summary>
    private static readonly string[] ConformCodes = ["BYN2780", "BYN2801", "BYN2802", "BYN2803", "BYN2807", "BYN2814"];

    private static string Root
    {
        get
        {
            var root = Path.Combine(Golden.RepoRoot, "tmp", "t14-cases");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private static Compilation Compile(string text) =>
        Compilation.Create([SourceText.FromString(Path.Combine(Root, "t14.byn"), text)], Root, new CompilationOptions(Library: true));

    private static object? Call(Compilation c, string name, params object?[] args)
    {
        var m = typeof(Compilation).GetMethods(Any).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return m.Invoke(m.IsStatic ? null : c, args); }
        catch (TargetInvocationException e) { throw e.InnerException!; }
    }

    private static object? Get(Compilation c, string name) => typeof(Compilation).GetField(name, Any)!.GetValue(c);

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

    private static string FixText(Fix? f) => f is null ? "null" : $"{f.Span.Start}:{f.Span.Length} {f.Replacement.Replace("\n", "\\n")}";

    private static string Line(Diagnostic d) =>
        $"{d.Code} {At(d.Source, d.Span)} {d.Message} | {d.Label} | {d.Help}" + (d.Fix is { } f ? $" | fix {FixText(f)}" : "");

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

    private static string Yn(bool b) => b ? "yes" : "no";

    private static string Bool(bool b) => b ? "true" : "false";

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

    private static string ModifierName(ParamModifier m) => m switch
    {
        ParamModifier.Ref => ".refKeyword",
        ParamModifier.Move => ".moveKeyword",
        ParamModifier.Out => ".outKeyword",
        ParamModifier.In => ".inKeyword",
        _ => ".none",
    };

    /// <summary>A default cloned onto a struct: its template is the interface's member.</summary>
    private static bool IsClone(FunctionSymbol f) => f.Owner is not null && f.Template is { Interface: not null };

    private static string Who(FunctionSymbol f) =>
        $"{f.Owner?.Name ?? "-"}.{f.Name} {f.Kind} {f.ReturnType.DisplayName} clone {Yn(f.Template is not null)}";

    /// <summary>Bayan code that builds the symbols of one C# compilation's entry module, before conformance.</summary>
    private sealed class World
    {
        public readonly Compilation C;
        public readonly string Text;
        public readonly StringBuilder Code = new();
        public readonly Dictionary<object, string> Vars = new(ReferenceEqualityComparer.Instance);
        private int _next;

        public World(Compilation c, string text)
        {
            C = c;
            Text = text;
            Emit("var c = newCtx()");
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
            ErrorSetSymbol e => $"errorSetTypeOf({Vars[e]})",
            TypeParameterType p => $"typeParamTypeOf({Vars[p]})",
            ListType l => $"internList(ref c, by: {Type(l.Element)})",
            _ => throw new InvalidOperationException(t.DisplayName),
        };

        public string Ids(IEnumerable<object> xs, string type)
        {
            var v = Fresh("ids");
            var all = xs.ToList();
            Emit($"{(all.Count == 0 ? "let" : "var")} {v} = List<{type}>()");
            foreach (var x in all) Emit($"{v}.append({(type == "Type" ? Type((TypeSymbol)x) : Vars[x])})");
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

        public string Function(FunctionSymbol f, int decl, int member, string owner = "-1", string iface = "-1")
        {
            var ps = Fresh("ps");
            Emit($"{(f.Parameters.Count == 0 ? "let" : "var")} {ps} = List<LocalId>()");
            foreach (var p in f.Parameters)
                Emit($"{ps}.append(newLocal(ref c, by: LocalSymbol {{ name: {Lit(p.Name)}, type: {Type(p.Type)}, kind: .parameter, isParameter: true, modifier: {ModifierName(p.Modifier)}, ordinal: {p.Ordinal} }}))");
            var sets = Ids(f.ErrorSets, "ErrorSetId");
            var excluded = Ids(f.ExcludedErrors, "VariantId");
            var v = Fresh("fn");
            var loc = f.Location!.Value.Span;
            Emit($"let {v} = newFunction(ref c, by: FunctionSymbol {{ name: {Lit(f.Name)}, nameSpan: {Span(loc)}, span: {Span(loc)}, module: 0, kind: {KindName(f.Kind)}, file: 0, declIndex: {decl}, memberIndex: {member}, owner: {owner}, iface: {iface}, isAbstract: {Bool(f.IsAbstract)}, isStatic: {Bool(f.IsStatic)}, isMutating: {Bool(f.IsMutating)}, hasFails: {Bool(f.Fails)}, infersErrors: {Bool(f.InfersErrors)}, parameters: move {ps}, returnType: {Type(f.ReturnType)}, errorSets: move {sets}, excludedErrors: move {excluded} }})");
            Vars[f] = v;
            return v;
        }

        /// <summary>
        /// Error sets, interfaces, structs (templates, then their instances) and the T12 state Conform reads
        /// (<c>_baseSpans</c>, <c>_associatedBindings</c>); cloned defaults and conformances are left out, as they are Conform's.
        /// </summary>
        public void Declarations()
        {
            var m = C.EntryModule;
            foreach (var e in m.ErrorSets)
            {
                var v = Fresh("es");
                Emit($"let {v} = newErrorSet(ref c, by: ErrorSetSymbol {{ name: {Lit(e.Name)}, module: 0, file: 0 }})");
                Vars[e] = v;
                foreach (var x in e.Variants)
                {
                    var xv = Fresh("ev");
                    Emit($"let {xv} = newErrorVariant(ref c, by: ErrorVariantSymbol {{ name: {Lit(x.Name)}, file: 0, set: {v} }})");
                    Emit($"c.errorSets[{v}].variants.append({xv})");
                    Vars[x] = xv;
                }
            }
            foreach (var i in m.Interfaces)
            {
                var v = Fresh("iface");
                Emit($"let {v} = newInterface(ref c, by: InterfaceSymbol {{ name: {Lit(i.Name)}, nameSpan: {Span(i.Location.Span)}, span: {Span(i.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(C, i.Decl)} }})");
                Emit($"c.modules[0].interfaces.append({v})");
                Emit($"c.modules[0].types[{Lit(i.Name)}] = interfaceTypeOf({v})");
                Vars[i] = v;
            }
            var getters = (IDictionary)Get(C, "_interfaceGetters")!;
            foreach (var i in m.Interfaces)
            {
                var v = Vars[i];
                var assoc = TypeParams(i.AssociatedTypes);
                Emit($"c.interfaces[{v}].associatedTypes = move {assoc}");
                var d = MemberIndex(C, i.Decl);
                foreach (var f in i.Methods) Emit($"c.interfaces[{v}].methods.append({Function(f, d, Array.IndexOf(i.Decl.Members, f.Decl), iface: v)})");
                foreach (var f in i.Properties) Emit($"c.interfaces[{v}].properties.append({Function(f, d, Array.IndexOf(i.Decl.Members, (Decl)getters[f]!), iface: v)})");
            }
            var structs = m.Structs.Where(s => s.Template is null).ToList();
            foreach (var s in structs)
            {
                var v = Fresh("st");
                Emit($"let {v} = newStruct(ref c, by: StructSymbol {{ name: {Lit(s.Name)}, nameSpan: {Span(s.Location.Span)}, span: {Span(s.Location.Span)}, module: 0, file: 0, declIndex: {MemberIndex(C, s.Decl)} }})");
                Emit($"c.modules[0].structs.append({v})");
                Emit($"c.modules[0].types[{Lit(s.Name)}] = structTypeOf({v})");
                Vars[s] = v;
            }
            foreach (var s in structs)
            {
                var tps = TypeParams(s.TypeParameters);
                Emit($"c.structs[{Vars[s]}].typeParams = move {tps}");
            }
            foreach (var inst in ((IDictionary)Get(C, "_instances")!).Values.OfType<StructSymbol>())
            {
                var args = Ids(inst.TypeArguments, "Type");
                var v = Fresh("inst");
                var site = new TextSpan(Text.IndexOf(inst.DisplayName, StringComparison.Ordinal), inst.DisplayName.Length);
                Emit($"let {v} = instantiateType(ref c, for: structTypeOf({Vars[inst.Template!]}), by: in {args}, from: 0, at: {Span(site)})");
                Vars[inst] = $"StructId(typeIndex({v}))";
            }
            foreach (var s in structs)
            {
                var v = Vars[s];
                var d = MemberIndex(C, s.Decl);
                foreach (var f in s.Fields)
                    Emit($"c.structs[{v}].fields.append(newField(ref c, by: FieldSymbol {{ name: {Lit(f.Name)}, nameSpan: {Span(f.Location!.Value.Span)}, span: {Span(f.Location!.Value.Span)}, owner: {v}, file: 0, declIndex: {d}, memberIndex: {Array.IndexOf(s.Decl.Members, f.Decl)}, type: {Type(f.Type)} }}))");
                foreach (var f in s.Methods.Where(f => !IsClone(f)))
                    Emit($"c.structs[{v}].methods.append({Function(f, d, Array.IndexOf(s.Decl.Members, f.Decl), owner: v)})");
                foreach (var p in s.Properties.Where(p => !IsClone(p.Getter)))
                {
                    var mi = Array.IndexOf(s.Decl.Members, p.Decl);
                    var g = Function(p.Getter, d, mi, owner: v);
                    Emit($"c.structs[{v}].properties.append(newProperty(ref c, by: PropertySymbol {{ name: {Lit(p.Name)}, nameSpan: {Span(p.Location!.Value.Span)}, span: {Span(p.Location!.Value.Span)}, owner: {v}, file: 0, declIndex: {d}, memberIndex: {mi}, type: {Type(p.Type)}, getter: {g} }}))");
                }
                foreach (var b in s.Bases) Emit($"c.structs[{v}].bases.append({Vars[b]})");
            }
            foreach (DictionaryEntry e in (IDictionary)Get(C, "_baseSpans")!)
            {
                var (s, i) = ((StructSymbol, InterfaceSymbol))e.Key;
                if (s.Template is null && Vars.TryGetValue(s, out var sv))
                    Emit($"c.baseSpans[$\"{{{sv}}} {{{Vars[i]}}}\"] = {Span((TextSpan)e.Value!)}");
            }
            foreach (DictionaryEntry e in (IDictionary)Get(C, "_associatedBindings")!)
            {
                var (s, name) = ((StructSymbol, string))e.Key;
                if (s.Template is null && Vars.TryGetValue(s, out var sv))
                    Emit($"c.associatedBindings[$\"{{{sv}}} {name}\"] = {Type((TypeSymbol)e.Value!)}");
            }
        }

        public StructSymbol S(string name) => (StructSymbol)C.EntryModule.Types[name];
    }

    // ── Sources ─────────────────────────────────────────────────────────────────

    private const string Conformances = """
        union error E {
            a
            b
        }

        interface ICount {
            i64 count
        }

        interface IDef {
            i64 size => 1
        }

        interface IDef2 {
            i64 size => 2
        }

        interface IAbs {
            i64 size
        }

        interface IBag {
            type Item
            Item first
        }

        interface IList {
            List<i64> items
        }

        interface IWide {
            i64 wide
            i64 tall
        }

        interface IMeth {
            i64 m() => 1
            i64 n()
        }

        struct FieldOk : ICount {
            i64 count
        }

        struct FieldBad : ICount {
            string count
        }

        struct Copy : IList {
            List<i64> items
        }

        struct GetOk : ICount {
            i64 count => 1
        }

        struct GetBad : ICount {
            bool count => true
        }

        struct Meth : ICount {
            i64 count(ref this, ref i64 a, string b) fails E {
                return 1
            }
        }

        struct Miss : IWide {
            i64 other
        }

        struct MissOne : ICount { i64 x }

        struct Cl : IDef {
        }

        struct Also : IDef, IDef2 {
        }

        struct Has : IDef, IAbs {
        }

        struct Later : IAbs, IDef {
        }

        struct Two : ICount, IDef {
            i64 count
        }

        struct Ints : IBag {
            type Item = i64
            i64 first
        }

        struct NoItem : IBag {
            i64 first
        }

        struct Plain {
            i64 v
        }

        struct Box<T> where T : ICount {
            T item
        }

        struct Holder {
            Box<Plain> b
        }

        """;

    private const string Shapes = """
        union error E {
            a
            b
        }

        union error F {
            c
        }

        union error G = E except .a

        struct Sh {
            i64 r1() => 1
            bool r2() => true
            static i64 s1() => 1
            Nope e1() => 1
            i64 p1(i64 a) => 1
            i64 p2(ref i64 a) => 1
            i64 p3(bool a) => 1
            i64 p4(move string a, in i64 b, out i64 d) {
                d = 1
                return 1
            }
            i64 f1() fails E => 1
            i64 f2() fails E => 1
            i64 f3() fails F => 1
            i64 f4() fails E, F => 1
            i64 f5() fails F, E => 1
            i64 f6() fails G => 1
            i64 f7() fails => 1
            i64 g => 1
            void m1(ref this) {
            }
            void m2(ref this, i64 x) {
            }
            void m3() {
            }
            void m4(i64 x) {
            }
        }

        """;

    // ── Sections ────────────────────────────────────────────────────────────────

    private delegate (string Bayan, List<string> Expected) Section();

    private static readonly (string Name, Section Build)[] All =
    [
        ("checkConformances", CheckConformances),
        ("baseSpanFallback", BaseSpanFallback),
        ("associatedBinding", AssociatedBinding),
        ("expected", Expected),
        ("sameShape", SameShape),
        ("signatureText", SignatureText),
        ("refThisFix", RefThisFix),
        ("quietly", Quietly),
    ];

    /// <summary>
    /// CheckConformances over every struct of the module: its diagnostics (Conform's, then the drained
    /// constraint check of <c>Box&lt;Plain&gt;</c>), each conformance, the defaults added to <c>AllFunctions</c>,
    /// the clones queued for binding, and the flag.
    /// </summary>
    private static (string, List<string>) CheckConformances()
    {
        var c = Compile(Conformances);
        var w = new World(c, Conformances);
        w.Declarations();
        var lines = c.Diagnostics.Items.Where(d => ConformCodes.Contains(d.Code)).Select(Line).ToList();
        var m = c.EntryModule;
        foreach (var s in m.Structs.Where(s => s.Template is null))
        {
            foreach (var k in s.Conformances)
            {
                var sb = new StringBuilder($"{s.Name} : {k.Interface.DisplayName}");
                foreach (var (member, own) in k.Witnesses) sb.Append($" w {member.Name}={Who(own)}");
                foreach (var (member, field) in k.FieldWitnesses) sb.Append($" f {member.Name}={field.Name}");
                foreach (var (name, t) in k.AssociatedTypes) sb.Append($" a {name}={t.DisplayName}");
                lines.Add(sb.ToString());
            }
            lines.Add($"{s.Name} props {string.Join(",", s.Properties.Select(p => $"{p.Name}:{p.Type.DisplayName}"))} methods {s.Methods.Count}");
        }
        foreach (var i in m.Interfaces)
            foreach (var f in i.Methods.Concat(i.Properties).Where(f => !f.IsAbstract)) lines.Add($"fn {i.Name}.{f.Name}");
        var clones = m.AllFunctions.Where(IsClone).ToList();
        foreach (var f in clones) lines.Add($"fn {f.Owner!.Name}.{f.Name}");
        var baseSpans = (IDictionary)Get(c, "_baseSpans")!;
        foreach (var f in clones.Where(f => !f.Owner!.IsGeneric))
            lines.Add($"work {f.Owner!.Name}.{f.Name} at {At(m.Files[0].Source, (TextSpan)baseSpans[(f.Owner!, f.Template!.Interface!)]!)}");
        lines.Add($"checked {Yn((bool)Get(c, "_conformancesChecked")!)}");
        w.Emit("let d0 = c.diagnostics.length");
        w.Emit("let f0 = c.modules[0].allFunctions.length");
        w.Emit("let w0 = c.instantiationWork.length");
        w.Emit("checkConformances(ref c)");
        w.Emit("showDiags(in c, from: d0)");
        w.Emit("showConformances(ref c)");
        w.Emit("for k in f0..c.modules[0].allFunctions.length { print($\"fn {ownerName(in c, for: c.modules[0].allFunctions[k])}.{c.functions[c.modules[0].allFunctions[k]].name}\") }");
        w.Emit("for k in w0..c.instantiationWork.length { print($\"work {ownerName(in c, for: c.instantiationWork[k].instance)}.{c.functions[c.instantiationWork[k].instance].name} at {lineCol(in c, for: c.instantiationWork[k].file, at: c.instantiationWork[k].span)}\") }");
        w.Emit("print($\"checked {yn(b: conformancesChecked(ref c))}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>Conform of a base with no listed span reports at the struct's name.</summary>
    private static (string, List<string>) BaseSpanFallback()
    {
        var c = Compile(Conformances);
        var s = (StructSymbol)c.EntryModule.Types["MissOne"];
        var i = (InterfaceSymbol)c.EntryModule.Types["ICount"];
        ((IDictionary)Get(c, "_baseSpans")!).Remove((s, i));
        var w = new World(c, Conformances);
        w.Declarations();
        var from = c.Diagnostics.Count;
        Call(c, "Conform", s);
        var lines = NewDiags(c, from);
        w.Emit("let d0 = c.diagnostics.length");
        w.Emit($"conform(ref c, for: {w.Vars[s]})");
        w.Emit("showDiags(in c, from: d0)");
        return (w.Code.ToString(), lines);
    }

    /// <summary>AssociatedBinding: a cached binding, one resolved quietly and stored, and the two nulls.</summary>
    private static (string, List<string>) AssociatedBinding()
    {
        const string text = "interface ICount {\n    i64 count\n}\n\nstruct NoBase {\n    type Item = i64\n}\n\nstruct Bound : ICount {\n    type Item = i64\n    type Other = string\n    type Bad = Nope\n    i64 count\n}\n";
        var c = Compile(text);
        var bindings = (Dictionary<(StructSymbol, string), TypeSymbol>)Get(c, "_associatedBindings")!;
        var noBase = (StructSymbol)c.EntryModule.Types["NoBase"];
        var bound = (StructSymbol)c.EntryModule.Types["Bound"];
        // A cached binding is returned as it is, so a different one shows it was not resolved again.
        bindings[(bound, "Item")] = PrimitiveType.Bool;
        bindings.Remove((bound, "Other"));
        bindings.Remove((bound, "Bad"));
        bindings.Remove((noBase, "Item"));
        var w = new World(c, text);
        w.Declarations();
        var lines = new List<string>();
        var from = c.Diagnostics.Count;
        w.Emit("let d0 = c.diagnostics.length");
        foreach (var (s, name) in new[] { (bound, "Item"), (bound, "Other"), (bound, "Bad"), (bound, "Zip"), (noBase, "Item") })
        {
            var t = (TypeSymbol?)Call(c, "AssociatedBinding", s, name);
            lines.Add($"{s.Name}.{name} {t?.DisplayName ?? "null"} stored {Yn(bindings.ContainsKey((s, name)))}");
            w.Emit($"showBinding(ref c, for: {w.Vars[s]}, by: {Lit(name)})");
        }
        lines.Add($"diags +{c.Diagnostics.Count - from}");
        w.Emit("print($\"diags +{c.diagnostics.length - d0}\")");
        return (w.Code.ToString(), lines);
    }

    /// <summary>
    /// Expected for getters: declared (<c>GetterDecl</c>) and default (<c>PropertyDecl</c>), through the map, and quiet.
    /// C# makes a throwaway symbol; the port leaves <c>c.functions</c> as it found it (Q722), as C# leaves <c>AllFunctions</c>.
    /// </summary>
    private static (string, List<string>) Expected()
    {
        const string text = "interface ICount {\n    i64 count\n}\n\ninterface IDef {\n    string size => \"\"\n}\n\ninterface IBag {\n    type Item\n    Item first\n}\n\ninterface IBad {\n    Nope bad\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        w.Declarations();
        var lines = new List<string>();
        var from = c.Diagnostics.Count;
        var fns = c.EntryModule.AllFunctions.Count;
        w.Emit("let d0 = c.diagnostics.length");
        w.Emit("let n0 = c.functions.length");
        foreach (var i in c.EntryModule.Interfaces)
        {
            var map = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
            w.Emit("{");
            w.Emit("    var map = Map<string, Type>()");
            foreach (var a in i.AssociatedTypes)
            {
                map[a.Name] = PrimitiveType.I64;
                w.Emit($"    map[{Lit(a.Name)}] = {w.Type(PrimitiveType.I64)}");
            }
            foreach (var member in i.Properties)
            {
                var fn = (FunctionSymbol)Call(c, "Expected", i, member, map)!;
                lines.Add($"{i.Name}.{fn.Name} {fn.ReturnType.DisplayName} {fn.Kind} owner {fn.Owner?.Name ?? "-"}");
                w.Emit($"    showExpected(ref c, for: {w.Vars[i]}, by: {w.Vars[member]}, through: in map)");
            }
            w.Emit("}");
        }
        lines.Add($"diags +{c.Diagnostics.Count - from}");
        lines.Add($"functions +{c.EntryModule.AllFunctions.Count - fns}");
        w.Emit("print($\"diags +{c.diagnostics.length - d0}\")");
        w.Emit("print($\"functions +{c.functions.length - n0}\")");
        return (w.Code.ToString(), lines);
    }

    private static FunctionSymbol Member(StructSymbol s, string name) =>
        s.FindMethods(name).FirstOrDefault() ?? s.FindProperty(name)!.Getter;

    /// <summary>SameShape over pairs of methods, one case per conjunct.</summary>
    private static (string, List<string>) SameShape()
    {
        var c = Compile(Shapes);
        var w = new World(c, Shapes);
        w.Declarations();
        var s = w.S("Sh");
        (string Own, string Expected)[] pairs =
        [
            ("r1", "r1"), ("r1", "g"), ("s1", "r1"), ("r1", "s1"), ("r1", "r2"), ("e1", "r2"), ("r2", "e1"),
            ("p1", "r1"), ("p1", "p2"), ("p1", "p3"), ("p2", "p2"), ("r1", "f1"), ("f1", "f2"), ("f1", "f3"), ("f4", "f5"),
            ("f4", "f1"), ("f1", "f4"), ("f6", "f1"), ("f1", "f6"), ("f7", "f3"), ("f3", "f7"), ("m1", "m3"),
        ];
        var lines = new List<string>();
        foreach (var (own, expected) in pairs)
        {
            lines.Add($"{own} {expected} {Yn((bool)Call(c, "SameShape", Member(s, own), Member(s, expected))!)}");
            w.Emit($"showSameShape(ref c, for: {w.Vars[Member(s, own)]}, to: {w.Vars[Member(s, expected)]}, by: \"{own} {expected}\")");
        }
        return (w.Code.ToString(), lines);
    }

    /// <summary>SignatureText of each method and getter: modifiers, <c>ref this</c>, <c>static</c> and each <c>fails</c> form.</summary>
    private static (string, List<string>) SignatureText()
    {
        var c = Compile(Shapes);
        var w = new World(c, Shapes);
        w.Declarations();
        var s = w.S("Sh");
        var lines = new List<string>();
        foreach (var f in s.Methods.Concat(s.Properties.Select(p => p.Getter)))
        {
            lines.Add((string)Call(c, "SignatureText", f)!);
            w.Emit($"showSignature(ref c, for: {w.Vars[f]})");
        }
        return (w.Code.ToString(), lines);
    }

    /// <summary>RefThisFix: add with and without parameters, remove with and without, and a getter (no declaration).</summary>
    private static (string, List<string>) RefThisFix()
    {
        var c = Compile(Shapes);
        var w = new World(c, Shapes);
        w.Declarations();
        var s = w.S("Sh");
        var lines = new List<string>();
        foreach (var name in new[] { "m1", "m2", "m3", "m4", "g" })
        {
            var f = Member(s, name);
            lines.Add($"{name} {FixText((Fix?)Call(c, "RefThisFix", s, f))}");
            w.Emit($"showFix(by: {Lit(name)}, for: t14RefThisFix(ref c, for: {w.Vars[s]}, by: {w.Vars[f]}))");
        }
        return (w.Code.ToString(), lines);
    }

    /// <summary>Quietly drops what is reported inside it, and reporting after it reaches the diagnostics again.</summary>
    private static (string, List<string>) Quietly()
    {
        const string text = "struct P {\n    i64 x\n}\n";
        var c = Compile(text);
        var w = new World(c, text);
        var source = c.EntryModule.Files[0].Source;
        var from = c.Diagnostics.Count;
        var code = DiagnosticCodes.All.First(d => d.Code == "BYN2801");
        void Report(int start) => Call(c, "Report", code, source, new TextSpan(start, 1), new object?[] { "P", "x", "I", "i64 x { get }" }, null, null, null);
        Call(c, "Quietly", (Action)(() => Report(7)));
        Report(15);
        var lines = NewDiags(c, from);
        string BReport(int start) => $"report(ref c, for: .missingInterfaceMember, from: 0, at: {Span(new TextSpan(start, 1))}, by: (\"P\", \"x\", \"I\", \"i64 x {{ get }}\", \"\"))";
        w.Emit("let d0 = c.diagnostics.length");
        w.Emit("let quiet = quietlyBegin(ref c)");
        w.Emit(BReport(7));
        w.Emit("quietlyEnd(ref c, to: quiet)");
        w.Emit(BReport(15));
        w.Emit("showDiags(in c, from: d0)");
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
            let source = lexer.SourceText.create("t14.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            c.files.append(SourceFile { path: "t14.byn", displayPath: "t14.byn", text: move copy, unit: move unit, module: 0, isEntry: true, imports: Map<string, ModId>() })
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
            if d.hasFix { sb.append($" | fix {d.fixSpan.start}:{d.fixSpan.length} {d.fixReplacement.replaced("\n", by: "\\n")}") }
            return $"{sb.finish()}"
        }

        void showDiags(in Ctx c, from i64 start) {
            for i in start..c.diagnostics.length { print(diagLine(in c, for: in c.diagnostics[i])) }
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

        string ownerName(in Ctx c, for FuncId f) {
            if c.functions[f].owner != -1 { return c.structs[c.functions[f].owner].name }
            if c.functions[f].iface != -1 { return c.interfaces[c.functions[f].iface].name }
            return "-"
        }

        string who(ref Ctx c, for FuncId f) {
            let owner = c.functions[f].owner == -1 ? "-" : c.structs[c.functions[f].owner].name
            return $"{owner}.{c.functions[f].name} {kindText(c.functions[f].kind)} {typeDisplay(ref c, by: c.functions[f].returnType)} clone {yn(b: c.functions[f].template != -1)}"
        }

        void showConformances(ref Ctx c) {
            let structs = copyIds(in c.modules[0].structs)
            for s in structs {
                let ks = copyIds(in c.structs[s].conformances)
                for k in ks {
                    var members = List<FuncId>()
                    var owns = List<FuncId>()
                    for (m, o) in c.conformances[k].witnesses {
                        members.append(m)
                        owns.append(o)
                    }
                    var fieldMembers = List<FuncId>()
                    var fields = List<FieldId>()
                    for (m, f) in c.conformances[k].fieldWitnesses {
                        fieldMembers.append(m)
                        fields.append(f)
                    }
                    var names = List<string>()
                    var types = List<Type>()
                    for (n, t) in c.conformances[k].associatedTypes {
                        names.append(n)
                        types.append(t)
                    }
                    var sb = StrBuf()
                    sb.append($"{c.structs[s].name} : {typeDisplay(ref c, by: interfaceTypeOf(c.conformances[k].iface))}")
                    for x in 0..members.length { sb.append($" w {c.functions[members[x]].name}={who(ref c, for: owns[x])}") }
                    for x in 0..fields.length { sb.append($" f {c.functions[fieldMembers[x]].name}={c.fields[fields[x]].name}") }
                    for x in 0..names.length { sb.append($" a {names[x]}={typeDisplay(ref c, by: types[x])}") }
                    print($"{sb.finish()}")
                }
                var props = StrBuf()
                let ps = copyIds(in c.structs[s].properties)
                for x in 0..ps.length {
                    if x > 0 { props.append(",") }
                    props.append($"{c.properties[ps[x]].name}:{typeDisplay(ref c, by: c.properties[ps[x]].type)}")
                }
                print($"{c.structs[s].name} props {props.finish()} methods {c.structs[s].methods.length}")
            }
        }

        void showBinding(ref Ctx c, for StructId s, by string name) {
            let t = associatedBinding(ref c, for: s, by: name)
            let shown = t == -1 ? "null" : typeDisplay(ref c, by: t)
            print($"{c.structs[s].name}.{name} {shown} stored {yn(b: c.associatedBindings.contains($"{s} {name}"))}")
        }

        void showExpected(ref Ctx c, for InterfaceId i, by FuncId member, through in Map<string, Type> map) {
            let fn = t14Expected(ref c, for: i, by: member, through: in map)
            let owner = fn.owner == -1 ? "-" : c.structs[fn.owner].name
            print($"{c.interfaces[i].name}.{fn.name} {typeDisplay(ref c, by: fn.returnType)} {kindText(fn.kind)} owner {owner}")
        }

        void showSameShape(ref Ctx c, for FuncId own, to FuncId expected, by string label) {
            let a = t14View(in c, for: own)
            let b = t14View(in c, for: expected)
            print($"{label} {yn(b: t14SameShape(in c, for: in a, to: in b))}")
        }

        void showSignature(ref Ctx c, for FuncId f) {
            let v = t14View(in c, for: f)
            print(t14SignatureText(ref c, for: in v))
        }

        void showFix(by string name, for in T14Fix f) {
            if f.hasFix {
                print($"{name} {f.fixSpan.start}:{f.fixSpan.length} {f.replacement.replaced("\n", by: "\\n")}")
            } else {
                print($"{name} null")
            }
        }


        """;

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
