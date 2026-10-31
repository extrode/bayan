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
/// T17: <c>selfhost/semantics/express.byn</c> against the C# <c>Binder</c> (BinderExpressions.cs 1–599), as T15 does it:
/// the oracle compiles <see cref="Source"/>, makes a C# <c>Binder</c> by reflection over a symbol of that compilation and
/// calls <c>BindExpr</c>, <c>BindStatementExpression</c>, <c>BindExprFor</c>, <c>Convert</c>, <c>Constrain</c>,
/// <c>FindPeer</c>, <c>IsPending</c>, <c>IsUntypedLiteral</c>, <c>ParseInteger</c>, <c>Fits</c> and <c>Suggest</c>; the
/// generated <c>main.byn</c> builds the same symbols by hand (each with <c>module: 0</c>, as T11's declare pass sets it)
/// and calls the port. Both print the bound tree and the new diagnostics. Each case's expression is the expression body
/// of a <c>q…</c> function; targets are the parameter types of <c>sigs</c>.
///
/// Not compared, since only a sibling stub reaches them:
/// <list type="bullet">
/// <item><c>bindConverted</c>: T22 <c>built</c> (errorcases.byn:14) returns -1.</item>
/// <item>The <c>Untrusted&lt;bool&gt;</c> fact in <c>convert</c>: T19 <c>fact</c> (express3.byn:41).</item>
/// <item>Local reads, narrowing and known states in <c>bindName</c>: T35 <c>capture</c> (express4.byn:20) returns false.</item>
/// <item>A <c>const</c> local: T18 <c>constantLiteral</c> (express2.byn:42); a module constant: T18 <c>constantRef</c> (express2.byn:39).</item>
/// <item><c>it</c>, a states payload field and <c>this</c> in a lambda: T35 <c>inLambda</c> (express4.byn:23), T38 <c>statesOf</c> (declare2.byn:26).</item>
/// <item>An implicit <c>this</c> member and an interface default's members: T18 <c>lookupMember</c> (express2.byn:45), <c>bindImplicitThisMember</c> (express2.byn:27).</item>
/// <item>A variant group (in <c>asValue</c> and <c>ConvertToFunction</c>) and a method group with a receiver: T18 member binding (express2.byn).</item>
/// <item>A static array to a writable <c>Span</c>: its value needs T18 <c>constantRef</c> (express2.byn:39).</item>
/// <item>A <c>List</c> to <c>Span</c> mismatch help, an optional unwrap from a local: T34/T20 collection and call binding (calls3.byn, calls.byn:5).</item>
/// <item>Dispatch to member, call, index, unary, binary, range, conditional, cast, is, in, switch, try, move, catch, jump,
/// block, struct literal, with, collection, tuple, interpolation, stackalloc and lambda: their siblings' stubs
/// (express2.byn, calls.byn, calls2.byn:32, calls3.byn, express3.byn:5, express4.byn:17, binder2.byn:363, binder2.byn:837).</item>
/// <item><c>caught</c> with <c>_caught</c> set and the print and sink argument branches of <c>convert</c> (Q600).</item>
/// <item>A module reference from an import, and the <c>dyn ref</c> writable check: no import or interface in the oracle source.</item>
/// </list>
/// </summary>
public sealed class SelfhostSemanticsT17Tests
{
    private const string Source = """
        struct Point {
            i32 x
            Point(i32 a, at i32 b) {
            }
            void tm() => this
            static void ts() => this
        }

        type distinct Sku = string

        union error LexError {
            bad
        }

        void modFunc() {
        }

        i32 add(i32 a, i32 b) => a

        bool bare(i64 x) fails {
            return true
        }

        i32 gen<T>() => 0

        void sigs(i32? o, Symbol sy, f32 g, f64 d, i64 l, i32 i, u8 by, i8 small, string st, Sku sk, (() -> void) fv, ((i32, i32) -> i32) fa, ((i32) -> i32) f1, ((i64) -> bool fails LexError) fe) {
        }

        void qVars() {
            var p = 1
            var q = 2
            var r = 3
        }

        void qInt() => 42
        void qBig() => 3000000000
        void qMaxU() => 10000000000000000000
        void qHuge() => 99999999999999999999
        void qU() => 7u
        void qUBig() => 5000000000u
        void qL() => 7l
        void qUl() => 7ul
        void qIf() => 7f
        void qBad() => 7kg
        void qHex() => 0x2A
        void qUnd() => 1_000
        void qBin() => 0b101
        void qFlt() => 1.5
        void qFltF() => 1.5f
        void qFltBad() => 1.5kg
        void qFltExp() => 19.95e9
        void qFltUnd() => 1_0.5_0e1_0
        void qFltExpNeg() => 1.5e-2
        void qFltExpPos() => 1.5e+2
        void qChar() => 'a'
        void qCharEsc() => '\t'
        void qCharU() => '\u{41}'
        void qStr() => "hi"
        void qTrue() => true
        void qFalse() => false
        void qNull() => null
        void qParen() => (42)
        void qNeg() => -5
        void qSum() => 1 + 2
        void qCmp() => 1 < 2
        void qMod() => modFunc
        void qAdd() => add
        void qBare() => bare
        void qGen() => gen
        void qPoint() => Point
        void qSku() => Sku
        void qPrint() => print
        void qPrim() => i32
        void qListT() => List
        void qIo() => Io
        void qHexT() => Hex
        void qSymT() => Symbol
        void qUtf8() => Utf8
        void qTypo() => modFnc
        void qZz() => zzqqxx
        void qThis() => this

        """;

    private sealed record Case(string Name, string Fn, string Op, string? Target = null, string? Target2 = null);

    /// <summary>
    /// <c>expr</c>: <c>BindExpr(e, target)</c>; <c>stmt</c>: <c>BindStatementExpression(e)</c>; <c>conv</c>:
    /// <c>Convert(BindExpr(e), target, e.Span)</c>, then to <c>Target2</c> if set; <c>fn</c>: <c>Convert(BindExprFor(e, target), …)</c>.
    /// </summary>
    private static readonly Case[] Cases =
    [
        new("int", "qInt", "expr"),
        new("int-i64", "qBig", "expr"),
        new("int-u64", "qMaxU", "expr"),
        new("int-huge", "qHuge", "expr"),
        new("suffix-u", "qU", "expr"),
        new("suffix-u-big", "qUBig", "expr"),
        new("suffix-l", "qL", "expr"),
        new("suffix-ul", "qUl", "expr"),
        new("suffix-f", "qIf", "expr"),
        new("suffix-bad", "qBad", "expr"),
        new("int-float-target", "qInt", "expr", "d"),
        new("int-int-target", "qInt", "expr", "small"),
        new("int-int-target-out", "qBig", "expr", "small"),
        new("int-optional-target", "qInt", "expr", "o"),
        new("hex", "qHex", "expr"),
        new("underscore", "qUnd", "expr"),
        new("binary", "qBin", "expr"),
        new("float", "qFlt", "expr"),
        new("float-suffix-f", "qFltF", "expr"),
        new("float-f32-target", "qFlt", "expr", "g"),
        new("float-suffix-bad", "qFltBad", "expr"),
        new("float-exp", "qFltExp", "expr"),
        new("float-underscore", "qFltUnd", "expr"),
        new("float-exp-neg", "qFltExpNeg", "expr"),
        new("float-exp-pos", "qFltExpPos", "expr"),
        new("char", "qChar", "expr"),
        new("char-u8-target", "qChar", "expr", "by"),
        new("char-escape", "qCharEsc", "expr"),
        new("char-u-escape", "qCharU", "expr"),
        new("string", "qStr", "expr"),
        new("string-symbol-target", "qStr", "expr", "sy"),
        new("true", "qTrue", "expr"),
        new("false", "qFalse", "expr"),
        new("null", "qNull", "expr"),
        new("null-optional-target", "qNull", "expr", "o"),
        new("paren", "qParen", "expr"),
        new("name-function", "qMod", "expr"),
        new("name-type", "qPoint", "expr"),
        new("name-builtin", "qPrint", "expr"),
        new("name-primitive", "qPrim", "expr"),
        new("name-list", "qListT", "expr"),
        new("name-io", "qIo", "expr"),
        new("name-hex", "qHexT", "expr"),
        new("name-symbol", "qSymT", "expr"),
        new("name-utf8", "qUtf8", "expr"),
        new("name-typo", "qTypo", "expr"),
        new("name-undefined", "qZz", "expr"),
        new("this-free", "qThis", "expr"),
        new("this-method", "Point.tm", "expr"),
        new("this-static", "Point.ts", "expr"),
        new("stmt-value", "qInt", "stmt"),
        new("stmt-string", "qStr", "stmt"),
        new("stmt-error", "qMod", "stmt"),
        new("conv-identity", "qInt", "conv", "i"),
        new("conv-error", "qZz", "conv", "i"),
        new("conv-int-i64", "qInt", "conv", "l"),
        new("conv-int-out", "qBig", "conv", "i"),
        new("conv-int-fits", "qL", "conv", "i"),
        new("conv-int-float", "qInt", "conv", "d"),
        new("conv-int-optional", "qInt", "conv", "o"),
        new("conv-f32-literal", "qFlt", "conv", "g"),
        new("conv-widen", "qFltF", "conv", "d"),
        new("conv-null-optional", "qNull", "conv", "o"),
        new("conv-symbol", "qStr", "conv", "sy"),
        new("conv-distinct", "qStr", "conv", "sk"),
        new("conv-mismatch", "qTrue", "conv", "i"),
        new("conv-mismatch-string", "qInt", "conv", "st"),
        new("conv-mismatch-cast", "qFlt", "conv", "i"),
        new("conv-mismatch-char", "qChar", "conv", "i"),
        new("conv-mismatch-optional", "qNull", "conv", "o", "i"),
        new("conv-value-to-function", "qInt", "conv", "fa"),
        new("fn-exact", "qMod", "fn", "fv"),
        new("fn-exact-params", "qAdd", "fn", "fa"),
        new("fn-mismatch", "qAdd", "fn", "fv"),
        new("fn-error-sets", "qBare", "fn", "fe"),
        new("fn-generic", "qGen", "fn", "fv"),
        new("fn-builtin", "qPrint", "fn", "f1"),
        new("fn-constructor", "qPoint", "fn", "fa"),
        new("fn-constructor-take", "qPoint", "fn", "f1"),
        new("fn-distinct", "qSku", "fn", "f1"),
        new("fn-type-not-constructible", "qPrim", "fn", "f1"),
    ];

    private static string Dir => Path.Combine(Golden.RepoRoot, "tmp", "t17-oracle");

    private static string PathOf(string name) => Path.Combine(Dir, name + ".byn");

    // ── The C# side ──────────────────────────────────────────────────────────────

    private static readonly System.Type BinderType =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.Binder") ?? throw new InvalidOperationException("Binder");

    private sealed class Oracle
    {
        public Compilation C = null!;
        public FileContext File = null!;
        public int Mark;

        public static Oracle Of(string name, string text)
        {
            var path = PathOf(name);
            var c = Compilation.Create([SourceText.FromString(path, text)], packageRoot: Dir);
            var file = c.Modules.SelectMany(m => m.Files).First(f => f.Source.Path == path);
            return new Oracle { C = c, File = file, Mark = c.Diagnostics.Items.Count };
        }

        public ModuleSymbol Module => File.Module;

        public Decl[] Members => File.Tree.Root.Members;

        public object Binder(FunctionSymbol? fn, StructSymbol? container) =>
            Activator.CreateInstance(BinderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [C, File, fn, container, null, null], null)!;

        /// <summary>The diagnostics reported since the last call, and moves the mark past them.</summary>
        public IEnumerable<string> NewDiagnostics()
        {
            var items = C.Diagnostics.Items;
            var lines = new List<string>();
            for (var i = Mark; i < items.Count; i++) lines.Add(Diag(items[i]));
            Mark = items.Count;
            return lines;
        }
    }

    private static object? Call(object binder, string name, params object?[] args)
    {
        var m = BinderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        return m.Invoke(binder, args);
    }

    private static object? Static(string name, params object?[] args)
    {
        var m = BinderType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        return m.Invoke(null, args);
    }

    private static T Field<T>(object binder, string name) =>
        (T)BinderType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder)!;

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static readonly Dictionary<string, string> KindRenames = new()
    {
        ["default"] = "defaultValue", ["this"] = "thisRef", ["with"] = "withExpr", ["is"] = "isExpr", ["move"] = "moveExpr",
        ["try"] = "tryExpr", ["catch"] = "catchExpr", ["return"] = "returnStmt", ["fail"] = "failStmt", ["become"] = "becomeStmt",
        ["break"] = "breakStmt", ["continue"] = "continueStmt", ["if"] = "ifStmt", ["while"] = "whileStmt", ["for"] = "forStmt",
        ["switch"] = "switchStmt", ["defer"] = "deferStmt", ["assert"] = "assertStmt", ["errdefer"] = "errdeferStmt",
        ["context"] = "contextStmt",
    };

    private static string KindName(System.Type t)
    {
        var n = char.ToLowerInvariant(t.Name["Bound".Length]) + t.Name["Bound".Length..][1..];
        return KindRenames.GetValueOrDefault(n, n);
    }

    private static string Lower(bool b) => b ? "true" : "false";

    /// <summary>The bound tree, one node a line: kind, span, type and constant, the local or function named.</summary>
    private static void Dump(List<string> lines, BoundNode? n, int depth = 0)
    {
        if (n is null)
        {
            lines.Add(new string(' ', depth * 2) + "none");
            return;
        }
        var sb = new StringBuilder(new string(' ', depth * 2));
        sb.Append($"{KindName(n.GetType())} {n.Syntax.Span.Start}..{n.Syntax.Span.End}");
        if (n is BoundExpr e)
        {
            sb.Append($" : {e.Type.DisplayName}");
            if (e.Constant is { } k) sb.Append($" = {k}");
        }
        if (n is BoundLocal l) sb.Append($" {l.Local.Name}");
        if (n is BoundCall call) sb.Append($" {call.Function.Name}");
        lines.Add(sb.ToString());
        foreach (var ch in BoundTree.Children(n)) Dump(lines, ch, depth + 1);
    }

    private static string Camel(string name) =>
        string.Concat(name.Split('-', '.').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Span(TextSpan s) => $"TextSpan {{ start: {s.Start}, length: {s.Length} }}";

    private static int IndexOf(Oracle o, Func<Decl, bool> pick) => Array.FindIndex(o.Members, d => pick(d));

    private static FunctionDecl Fn(Oracle o, string name) =>
        o.Members.OfType<FunctionDecl>().Single(f => f.Name.Text == name);

    private static StructDecl PointDecl(Oracle o) => o.Members.OfType<StructDecl>().Single(s => s.Name.Text == "Point");

    private static StructSymbol Point(Oracle o) => (StructSymbol)o.Module.Types["Point"];

    /// <summary>The function a case binds in, its struct, and the expression: the body of `q…` or of `Point.tm`/`Point.ts`.</summary>
    private static (FunctionSymbol Fn, StructSymbol? Container, Expr Expr) Site(Oracle o, string fn)
    {
        if (fn.StartsWith("Point.", StringComparison.Ordinal))
        {
            var name = fn["Point.".Length..];
            var point = Point(o);
            var decl = PointDecl(o).Members.OfType<FunctionDecl>().Single(f => f.Name.Text == name);
            return (point.Methods.Single(m => m.Name == name), point, decl.ExpressionBody!);
        }
        return (o.Module.Functions[fn][0], null, Fn(o, fn).ExpressionBody!);
    }

    private static TypeSymbol Sig(Oracle o, string name) => o.Module.Functions["sigs"][0].Parameters.Single(p => p.Name == name).Type;

    private static BoundExpr Convert(object binder, BoundExpr value, TypeSymbol target, TextSpan span) =>
        (BoundExpr)Call(binder, "Convert", value, target, span, false, true)!;

    private static List<string> ExpectedCase(Case k)
    {
        var o = Oracle.Of("main", Source);
        var (fn, container, e) = Site(o, k.Fn);
        var binder = o.Binder(fn, container);
        var target = k.Target is null ? null : Sig(o, k.Target);
        var r = k.Op switch
        {
            "expr" => (BoundExpr)Call(binder, "BindExpr", e, target)!,
            "stmt" => (BoundExpr)Call(binder, "BindStatementExpression", e)!,
            "conv" => Convert(binder, (BoundExpr)Call(binder, "BindExpr", e, null)!, target!, e.Span),
            "fn" => Convert(binder, (BoundExpr)Call(binder, "BindExprFor", e, target)!, target!, e.Span),
            _ => throw new InvalidOperationException(k.Op),
        };
        if (k.Target2 is { } t2) r = Convert(binder, r, Sig(o, t2), e.Span);
        var lines = new List<string>();
        Dump(lines, r);
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    // ── Bayan symbols, built by hand ─────────────────────────────────────────────

    /// <summary>The Bayan expression for type <paramref name="t"/>; statements it needs first go to <paramref name="sb"/>.</summary>
    private static string BayanType(StringBuilder sb, TypeSymbol t, ref int n)
    {
        switch (t)
        {
            case PrimitiveType { DisplayName: "never" }: return "prim(.neverKind)";
            case PrimitiveType { DisplayName: "rune" }: return "prim(.char)";
            case PrimitiveType p: return $"prim(.{p.DisplayName})";
            case ErrorType: return "errorType()";
            case BuiltinType { DisplayName: "Symbol" }: return "builtinType(.symbol)";
            case StructSymbol s: return $"structTypeOf(s{s.Name})";
            case Bayan.Semantics.Symbols.OptionalType opt: return $"internOptional(ref c, by: {BayanType(sb, opt.Inner, ref n)})";
            case FunctionType f:
            {
                var i = n++;
                var ps = new List<string>();
                foreach (var p in f.Parameters) ps.Add(BayanType(sb, p, ref n));
                var ret = BayanType(sb, f.Return, ref n);
                sb.AppendLine($"    {(ps.Count > 0 ? "var" : "let")} fp{i} = List<Type>()");
                foreach (var p in ps) sb.AppendLine($"    fp{i}.append({p})");
                sb.AppendLine($"    {(f.Fails.Count > 0 ? "var" : "let")} fe{i} = List<ErrorSetId>()");
                foreach (var s in f.Fails) sb.AppendLine($"    fe{i}.append(e{s.Name})");
                sb.AppendLine($"    let fu{i} = List<string>()");
                return $"internFunction(ref c, for: in fp{i}, to: {ret}, by: in fe{i}, as: in fu{i})";
            }
            default: throw new InvalidOperationException(t.DisplayName);
        }
    }

    private static string FunctionVar(FunctionSymbol fn) =>
        fn.Owner is { } s ? $"fn{s.Name}{(fn.Kind == FunctionKind.Constructor ? "Ctor" : Camel(fn.Name))}" : $"fn{Camel(fn.Name)}";

    private static void BuildFunction(StringBuilder sb, FunctionSymbol fn, string kind, int decl, int member, string owner, ref int n)
    {
        var v = FunctionVar(fn);
        var ret = BayanType(sb, fn.ReturnType, ref n);
        sb.AppendLine($"    let {v} = newFunction(ref c, by: FunctionSymbol {{ name: \"{fn.Name}\", module: 0, kind: .{kind}, file: 0, declIndex: {decl}, memberIndex: {member}, owner: {owner}, isStatic: {Lower(fn.IsStatic)}, returnType: {ret}, hasFails: {Lower(fn.Fails)} }})");
        foreach (var s in fn.ErrorSets) sb.AppendLine($"    c.functions[{v}].errorSets.append(e{s.Name})");
        foreach (var tp in fn.TypeParameters) sb.AppendLine($"    c.functions[{v}].typeParams.append(newTypeParam(ref c, by: TypeParam {{ name: \"{tp.Name}\" }}))");
        foreach (var p in fn.Parameters)
        {
            var type = BayanType(sb, p.Type, ref n);
            sb.AppendLine($"    tcParam(ref c, for: {v}, by: \"{p.Name}\", at: {Span(p.Location!.Value.Span)}, as: {type})");
        }
    }

    /// <summary>Bayan statements that declare the oracle source's types and free functions into module 0 of `c`.</summary>
    private static string Symbols(Oracle o)
    {
        var sb = new StringBuilder();
        var n = 0;
        var pointDecl = IndexOf(o, d => d is StructDecl { Name.Text: "Point" });
        var skuDecl = IndexOf(o, d => d is TypeAliasDecl { Name.Text: "Sku" });
        var lexDecl = IndexOf(o, d => d is ErrorSetDecl { Name.Text: "LexError" });
        sb.AppendLine($"    let sPoint = newStruct(ref c, by: StructSymbol {{ name: \"Point\", module: 0, file: 0, declIndex: {pointDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"Point\"] = structTypeOf(sPoint)");
        sb.AppendLine("    c.modules[0].structs.append(sPoint)");
        sb.AppendLine($"    let sSku = newStruct(ref c, by: StructSymbol {{ name: \"Sku\", module: 0, file: 0, declIndex: {skuDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"Sku\"] = structTypeOf(sSku)");
        sb.AppendLine("    c.modules[0].structs.append(sSku)");
        sb.AppendLine("    c.distinct[sSku] = 1");
        sb.AppendLine($"    c.structs[sSku].fields.append(newField(ref c, by: FieldSymbol {{ name: \"value\", owner: sSku, file: 0, declIndex: {skuDecl}, memberIndex: 0, type: prim(.string) }}))");
        sb.AppendLine($"    let eLexError = newErrorSet(ref c, by: ErrorSetSymbol {{ name: \"LexError\", module: 0, file: 0, declIndex: {lexDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"LexError\"] = errorSetTypeOf(eLexError)");
        sb.AppendLine("    c.modules[0].errorSets.append(eLexError)");
        var point = Point(o);
        var members = PointDecl(o).Members;
        foreach (var field in point.Fields)
        {
            var x = Array.FindIndex(members, m => m is FieldDecl fd && fd.Name.Text == field.Name);
            var type = BayanType(sb, field.Type, ref n);
            sb.AppendLine($"    c.structs[sPoint].fields.append(newField(ref c, by: FieldSymbol {{ name: \"{field.Name}\", owner: sPoint, file: 0, declIndex: {pointDecl}, memberIndex: {x}, type: {type} }}))");
        }
        var ctor = point.Constructors[0];
        BuildFunction(sb, ctor, "constructor", pointDecl, Array.FindIndex(members, m => m is ConstructorDecl), "sPoint", ref n);
        sb.AppendLine($"    c.structs[sPoint].constructors.append({FunctionVar(ctor)})");
        foreach (var m in point.Methods)
        {
            BuildFunction(sb, m, "method", pointDecl, Array.FindIndex(members, d => d is FunctionDecl fd && fd.Name.Text == m.Name), "sPoint", ref n);
            sb.AppendLine($"    c.structs[sPoint].methods.append({FunctionVar(m)})");
        }
        foreach (var (name, fns) in o.Module.Functions)
        {
            var decl = IndexOf(o, d => d is FunctionDecl fd && fd.Name.Text == name);
            BuildFunction(sb, fns[0], "function", decl, -1, "-1", ref n);
            sb.AppendLine($"    var list{Camel(name)} = List<FuncId>()");
            sb.AppendLine($"    list{Camel(name)}.append({FunctionVar(fns[0])})");
            sb.AppendLine($"    c.modules[0].functions[\"{name}\"] = move list{Camel(name)}");
            sb.AppendLine($"    c.modules[0].allFunctions.append({FunctionVar(fns[0])})");
        }
        return sb.ToString();
    }

    /// <summary>A Bayan `switch` that runs <paramref name="body"/> with the case's expression as `e`.</summary>
    private static void AtExpression(StringBuilder sb, Oracle o, string fn, string body)
    {
        if (fn.StartsWith("Point.", StringComparison.Ordinal))
        {
            var name = fn["Point.".Length..];
            var decl = IndexOf(o, d => d is StructDecl { Name.Text: "Point" });
            var member = Array.FindIndex(PointDecl(o).Members, m => m is FunctionDecl fd && fd.Name.Text == name);
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .structDecl(let sd):");
            sb.AppendLine($"            switch sd.value.members[{member}] {{");
            sb.AppendLine("                case .functionDecl(let d):");
            sb.AppendLine(body.Replace("$E", "d.value.expressionBody"));
            sb.AppendLine("                default:");
            sb.AppendLine("                    break");
            sb.AppendLine("            }");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
            return;
        }
        sb.AppendLine($"    switch c.files[0].unit.members[{IndexOf(o, d => d is FunctionDecl fd && fd.Name.Text == fn)}] {{");
        sb.AppendLine("        case .functionDecl(let d):");
        sb.AppendLine(body.Replace("$E", "d.value.expressionBody"));
        sb.AppendLine("        default:");
        sb.AppendLine("            break");
        sb.AppendLine("    }");
    }

    private static string CasesMain()
    {
        var o = Oracle.Of("main", Source);
        var symbols = Symbols(o);
        var sb = new StringBuilder();
        foreach (var k in Cases)
        {
            var (fn, container, _) = Site(o, k.Fn);
            sb.AppendLine($"void case{Camel(k.Name)}() {{");
            sb.AppendLine("    var c = tcSource()");
            sb.Append(symbols);
            sb.AppendLine($"    var b = tcBinder({FunctionVar(fn)}, by: {(container is null ? "-1" : "sPoint")})");
            sb.AppendLine("    t15CaptureTypes(ref b, through: ref c)");
            sb.AppendLine("    pushScope(ref b)");
            var n = 100;
            var target = k.Target is null ? "-1" : BayanType(sb, Sig(o, k.Target), ref n);
            sb.AppendLine($"    let target = {target}");
            if (k.Target2 is { } t2)
            {
                var target2 = BayanType(sb, Sig(o, t2), ref n);
                sb.AppendLine($"    let target2 = {target2}");
            }
            sb.AppendLine("    let mark = c.diagnostics.length");
            sb.AppendLine($"    print(\"== case {k.Name}\")");
            var body = new StringBuilder();
            body.AppendLine("            let span = t17ExprSpan(in $E)");
            var r = k.Target2 is null ? "let r" : "var r";
            body.AppendLine((k.Op switch
            {
                "expr" => "            $R = bindExpr(ref b, through: ref c, for: in $E, to: target)",
                "stmt" => "            $R = bindStatementExpression(ref b, through: ref c, for: in $E)",
                "conv" => "            $R = convert(ref b, through: ref c, for: bindExpr(ref b, through: ref c, for: in $E), to: target, at: span)",
                "fn" => "            $R = convert(ref b, through: ref c, for: bindExprFor(ref b, through: ref c, for: in $E, to: target), to: target, at: span)",
                _ => throw new InvalidOperationException(k.Op),
            }).Replace("$R", r));
            if (k.Target2 is not null) body.AppendLine("            r = convert(ref b, through: ref c, for: r, to: target2, at: span)");
            body.Append("            tcDump(ref c, for: r, at: 0)");
            AtExpression(sb, o, k.Fn, body.ToString());
            sb.AppendLine("    tcDiags(in c, from: mark)");
            sb.AppendLine("}");
        }
        sb.AppendLine("void casesAll() {");
        foreach (var k in Cases) sb.AppendLine($"    case{Camel(k.Name)}()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Constrain, FindPeer and IsPending ────────────────────────────────────────

    /// <summary>
    /// Three `var` declarations d0..d2 of `qVars` and two locals `p`, `q`: `p` pending on d0, then `q` on d1, with
    /// <c>Constrain</c> to targets and to other values, and <c>FindPeer</c> along a chain of peers.
    /// </summary>
    private static List<string> ExpectedPending()
    {
        var o = Oracle.Of("main", Source);
        var binder = o.Binder(o.Module.Functions["qVars"][0], null);
        var decls = Fn(o, "qVars").Body!.Statements.Cast<LocalDecl>().ToArray();
        var pending = Field<Dictionary<LocalSymbol, LocalDecl>>(binder, "_pending");
        var constraints = Field<Dictionary<LocalDecl, TypeSymbol>>(binder, "_constraints");
        var peers = Field<Dictionary<LocalDecl, LocalDecl>>(binder, "_peers");
        var p = new LocalSymbol("p", null, PrimitiveType.I32, LocalKind.Var);
        var q = new LocalSymbol("q", null, PrimitiveType.I32, LocalKind.Var);
        var lp = new BoundLocal(decls[0], p);
        var lq = new BoundLocal(decls[1], q);
        var lines = new List<string>();
        string Constraints() => string.Join(" ", decls.Select(d => constraints.TryGetValue(d, out var t) ? t.DisplayName : "none"));
        string Peer(int i) => Array.IndexOf(decls, (LocalDecl)Call(binder, "FindPeer", decls[i])!).ToString();
        pending[p] = decls[0];
        lines.Add($"pending {Lower((bool)Call(binder, "IsPending", lp)!)} {Lower((bool)Call(binder, "IsPending", lq)!)}");
        Call(binder, "Constrain", lp, PrimitiveType.Bool);
        lines.Add($"constraints {Constraints()}");
        Call(binder, "Constrain", lp, PrimitiveType.I64);
        Call(binder, "Constrain", lp, PrimitiveType.I32);
        Call(binder, "Constrain", lq, PrimitiveType.I64);
        lines.Add($"constraints {Constraints()}");
        pending[q] = decls[1];
        Call(binder, "Constrain", lp, lq, decls[1].Init);
        lines.Add($"peers {Peer(0)} {Peer(1)} {Peer(2)}");
        var lit = (BoundExpr)Call(binder, "BindExpr", Fn(o, "qInt").ExpressionBody, null)!;
        Call(binder, "Constrain", lq, lit, Fn(o, "qInt").ExpressionBody);
        lines.Add($"constraints {Constraints()}");
        var typed = (BoundExpr)Call(binder, "BindExpr", Fn(o, "qL").ExpressionBody, null)!;
        Call(binder, "Constrain", lq, typed, Fn(o, "qL").ExpressionBody);
        lines.Add($"constraints {Constraints()}");
        peers[decls[2]] = decls[1];
        lines.Add($"peers {Peer(0)} {Peer(1)} {Peer(2)}");
        lines.AddRange(o.NewDiagnostics());
        return lines;
    }

    private static string PendingMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        var qInt = IndexOf(o, d => d is FunctionDecl { Name.Text: "qInt" });
        var qL = IndexOf(o, d => d is FunctionDecl { Name.Text: "qL" });
        sb.AppendLine("string t17tConstraints(in Binder b, through ref Ctx c) {");
        sb.AppendLine("    var sb = StrBuf()");
        sb.AppendLine("    for d in 0..3 {");
        sb.AppendLine("        if d > 0 { sb.append(\" \") }");
        sb.AppendLine("        sb.append(b.constraints.contains(d) ? $\"{typeDisplay(ref c, by: b.constraints[d])}\" : \"none\")");
        sb.AppendLine("    }");
        sb.AppendLine("    return $\"{sb.finish()}\"");
        sb.AppendLine("}");
        sb.AppendLine("void pendingAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.Append(Symbols(o));
        sb.AppendLine("    var b = tcBinder(fnQVars, by: -1)");
        sb.AppendLine("    t15CaptureTypes(ref b, through: ref c)");
        sb.AppendLine("    pushScope(ref b)");
        sb.AppendLine("    let mark = c.diagnostics.length");
        sb.AppendLine("    let at = TextSpan { start: 0, length: 0 }");
        sb.AppendLine("    let p = newLocal(ref c, by: LocalSymbol { name: \"p\", type: prim(.i32), kind: .varKeyword })");
        sb.AppendLine("    let q = newLocal(ref c, by: LocalSymbol { name: \"q\", type: prim(.i32), kind: .varKeyword })");
        sb.AppendLine("    let lp = addLocal(ref c, from: BLocal { span: at, local: p, type: prim(.i32) })");
        sb.AppendLine("    let lq = addLocal(ref c, from: BLocal { span: at, local: q, type: prim(.i32) })");
        sb.AppendLine("    print(\"== pending\")");
        sb.AppendLine("    b.pending[p] = 0");
        sb.AppendLine("    print($\"pending {isPending(ref b, through: ref c, for: lp)} {isPending(ref b, through: ref c, for: lq)}\")");
        sb.AppendLine("    constrain(ref b, through: ref c, for: lp, to: prim(.bool))");
        sb.AppendLine("    print($\"constraints {t17tConstraints(in b, through: ref c)}\")");
        sb.AppendLine("    constrain(ref b, through: ref c, for: lp, to: prim(.i64))");
        sb.AppendLine("    constrain(ref b, through: ref c, for: lp, to: prim(.i32))");
        sb.AppendLine("    constrain(ref b, through: ref c, for: lq, to: prim(.i64))");
        sb.AppendLine("    print($\"constraints {t17tConstraints(in b, through: ref c)}\")");
        sb.AppendLine("    b.pending[q] = 1");
        AtVarInit(sb, o, 1, "            constrain(ref b, through: ref c, for: lp, as: lq, from: in $E)");
        sb.AppendLine("    print($\"peers {findPeer(ref b, for: 0)} {findPeer(ref b, for: 1)} {findPeer(ref b, for: 2)}\")");
        foreach (var decl in new[] { qInt, qL })
        {
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .functionDecl(let d):");
            sb.AppendLine("            let other = bindExpr(ref b, through: ref c, for: in d.value.expressionBody)");
            sb.AppendLine("            constrain(ref b, through: ref c, for: lq, as: other, from: in d.value.expressionBody)");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
            sb.AppendLine("    print($\"constraints {t17tConstraints(in b, through: ref c)}\")");
        }
        sb.AppendLine("    b.peers[2] = 1");
        sb.AppendLine("    print($\"peers {findPeer(ref b, for: 0)} {findPeer(ref b, for: 1)} {findPeer(ref b, for: 2)}\")");
        sb.AppendLine("    tcDiags(in c, from: mark)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>A Bayan `switch` that runs <paramref name="body"/> with the init of `qVars`' declaration <paramref name="i"/> as `$E`.</summary>
    private static void AtVarInit(StringBuilder sb, Oracle o, int i, string body)
    {
        sb.AppendLine($"    switch c.files[0].unit.members[{IndexOf(o, d => d is FunctionDecl { Name.Text: "qVars" })}] {{");
        sb.AppendLine("        case .functionDecl(let d):");
        sb.AppendLine("            switch d.value.body {");
        sb.AppendLine("                case .blockStmt(let blk):");
        sb.AppendLine($"                    switch blk.value.statements[{i}] {{");
        sb.AppendLine("                        case .localDecl(let ld):");
        sb.AppendLine(body.Replace("$E", "ld.value.init"));
        sb.AppendLine("                        default:");
        sb.AppendLine("                            break");
        sb.AppendLine("                    }");
        sb.AppendLine("                default:");
        sb.AppendLine("                    break");
        sb.AppendLine("            }");
        sb.AppendLine("        default:");
        sb.AppendLine("            break");
        sb.AppendLine("    }");
    }

    // ── IsUntypedLiteral, ParseInteger, Fits and Suggest ─────────────────────────

    private static readonly string[] UntypedFunctions =
        ["qInt", "qU", "qFlt", "qFltF", "qChar", "qNull", "qStr", "qTrue", "qParen", "qNeg", "qSum", "qCmp", "qMod"];

    private static List<string> ExpectedUntyped()
    {
        var o = Oracle.Of("main", Source);
        return UntypedFunctions.Select(f => $"{f} {Lower((bool)Static("IsUntypedLiteral", Fn(o, f).ExpressionBody)!)}").ToList();
    }

    private static string UntypedMain()
    {
        var o = Oracle.Of("main", Source);
        var sb = new StringBuilder();
        sb.AppendLine("void untypedAll() {");
        sb.AppendLine("    var c = tcSource()");
        sb.AppendLine("    print(\"== untyped\")");
        foreach (var f in UntypedFunctions)
            AtExpression(sb, o, f, $"            print($\"{f} {{isUntypedLiteral(in $E)}}\")");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly string[] Integers = ["0", "42", "1_000", "0x2A", "0X2a", "0b101", "18446744073709551615", "99999999999999999999"];

    private static List<string> ExpectedParse() => Integers.Select(s => $"{s} {Static("ParseInteger", s)}").ToList();

    private static string ParseMain()
    {
        var sb = new StringBuilder();
        sb.AppendLine("void parseAll() {");
        sb.AppendLine("    print(\"== parse\")");
        foreach (var s in Integers) sb.AppendLine($"    print($\"{s} {{bigText(parseInteger(\"{s}\"))}}\")");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly (string Value, string Type)[] FitsCases =
    [
        ("127", "i8"), ("128", "i8"), ("-128", "i8"), ("-129", "i8"), ("255", "u8"), ("256", "u8"), ("-1", "u64"),
        ("18446744073709551615", "u64"), ("2147483648", "i32"), ("9223372036854775807", "i64"), ("9223372036854775808", "i64"),
    ];

    private static PrimitiveType Primitive(string name) => (PrimitiveType)typeof(PrimitiveType)
        .GetField(char.ToUpperInvariant(name[0]) + name[1..], BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;

    private static List<string> ExpectedFits() =>
        FitsCases.Select(f => $"{f.Value} {f.Type} {Lower((bool)Static("Fits", Int128.Parse(f.Value), Primitive(f.Type))!)}").ToList();

    private static string FitsMain()
    {
        var sb = new StringBuilder();
        sb.AppendLine("void fitsAll() {");
        sb.AppendLine("    print(\"== fits\")");
        foreach (var (v, t) in FitsCases)
        {
            var big = v.StartsWith('-') ? $"bigNeg(parseInteger(\"{v[1..]}\"))" : $"parseInteger(\"{v}\")";
            sb.AppendLine($"    print($\"{v} {t} {{fits({big}, for: .{t})}}\")");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static readonly (string Name, string[] Candidates)[] SuggestCases =
    [
        ("prnt", ["print", "panic", "todo"]),
        ("print", ["print"]),
        ("zz", ["ab"]),
        ("ab", ["ac", "ac"]),
        ("PRNT", ["print"]),
        ("abcdef", ["abcxyz"]),
        ("abcdef", ["abcdxy"]),
        ("ab", ["ax", "ay"]),
        ("x", []),
    ];

    private static List<string> ExpectedSuggest() =>
        SuggestCases.Select(s => $"{s.Name} {Static("Suggest", s.Name, s.Candidates) as string ?? "none"}").ToList();

    private static string SuggestMain()
    {
        var sb = new StringBuilder();
        sb.AppendLine("void suggestAll() {");
        sb.AppendLine("    print(\"== suggest\")");
        for (var i = 0; i < SuggestCases.Length; i++)
        {
            var (name, candidates) = SuggestCases[i];
            sb.AppendLine($"    var s{i} = List<string>()");
            foreach (var x in candidates) sb.AppendLine($"    s{i}.append(\"{x}\")");
            sb.AppendLine($"    print($\"{name} {{suggest(\"{name}\", by: in s{i}) ?? \"none\"}}\")");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── The generated main ───────────────────────────────────────────────────────

    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '\n') sb.Append("\\n");
            else
            {
                if (ch is '\\' or '"') sb.Append('\\');
                sb.Append(ch);
            }
        }
        return sb.Append('"').ToString();
    }

    private static string MainSource() => Prelude.Replace("$SOURCE", Literal(Source)) + CasesMain() + PendingMain() + UntypedMain()
        + ParseMain() + FitsMain() + SuggestMain() + """
        i32 main() {
            casesAll()
            pendingAll()
            untypedAll()
            parseAll()
            fitsAll()
            suggestAll()
            return 0
        }

        """;

    private const string Prelude = """
        import lexer
        import parser

        Ctx tcCtx(string text) {
            var bytes = List<u8>()
            for x in text.bytes { bytes.append(x) }
            var copy = List<u8>()
            for x in text.bytes { copy.append(x) }
            let source = lexer.SourceText.create("t17.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
            c.files.append(SourceFile { path: "t17.byn", displayPath: "t17.byn", text: move copy, unit: move unit, module: 0, isEntry: true, imports: Map<string, ModId>() })
            var files = List<FileId>()
            files.append(0)
            c.modules.append(Mod { name: "t17", files: move files, directory: ".", cName: "t17" })
            return c
        }

        Ctx tcSource() => tcCtx($SOURCE)

        Binder tcBinder(FuncId fn, by StructId owner) => Binder {
            file: 0,
            fn: fn,
            owner: owner,
            scopes: [],
            targets: [],
            lambdas: [],
            lambdaCount: [],
            seeds: [],
            pending: [],
            constraints: [],
            peers: [],
            nonNull: [],
            stateFacts: [],
            errorNarrowed: [],
        }

        void tcParam(ref Ctx c, for FuncId f, by string name, at TextSpan span, as Type type) {
            let ordinal = c.functions[f].parameters.length
            let p = newLocal(ref c, by: LocalSymbol { name: name, nameSpan: span, span: span, type: type, kind: .parameter, isParameter: true, ordinal: ordinal })
            c.functions[f].parameters.append(p)
        }

        void tcDiags(in Ctx c, from i64 mark) {
            for i in mark..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                var fix = ""
                if c.diagnostics[i].hasFix { fix = $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" }
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fix}")
            }
        }

        void tcDump(ref Ctx c, for BNode n, at i64 depth) {
            var sb = StrBuf()
            for i in 0..depth { sb.append("  ") }
            if n == -1 {
                sb.append("none")
                print($"{sb.finish()}")
                return
            }
            let k = boundKind(n)
            let span = nodeSpan(in c, for: n)
            sb.append($"{k} {span.start}..{span.end}")
            if isExprKind(k) {
                sb.append($" : {typeDisplay(ref c, by: exprType(in c, for: n))}")
                if (exprConstant(in c, for: n) is { } v) { sb.append($" = {constantText(in v)}") }
            }
            if k == .local { sb.append($" {c.locals[c.bLocals[boundIndex(n)].local].name}") }
            if k == .call { sb.append($" {c.functions[c.bCalls[boundIndex(n)].function].name}") }
            print($"{sb.finish()}")
            for ch in children(in c, for: n) { tcDump(ref c, for: ch, at: depth + 1) }
        }

        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    public static TheoryData<string> CaseNames() => new(Cases.Select(k => k.Name));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ExpressionMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"case {name}", ExpectedCase(Cases.Single(k => k.Name == name)));

    [Fact]
    public void ConstrainAndPeersMatch() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "pending", ExpectedPending());

    [Fact]
    public void UntypedLiteralMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "untyped", ExpectedUntyped());

    [Fact]
    public void ParseIntegerMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "parse", ExpectedParse());

    [Fact]
    public void FitsMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "fits", ExpectedFits());

    [Fact]
    public void SuggestMatches() => SelfhostSemanticsHarness.AssertSection(Sections.Value, "suggest", ExpectedSuggest());
}
