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
/// <item><c>it</c> and <c>this</c> in a lambda, a states payload field: T35 <c>inLambda</c> (express4.byn:23), T38 <c>statesOf</c> (declare2.byn:26).</item>
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
            void tn() => modFunc
            void ti() => it
            void tg() => gen<i32>
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

        void sigs(i32? o, Symbol sy, f32 g, f64 d, i64 l, i32 i, u8 by, i8 small, string st, Sku sk, (() -> void) fv, ((i32, i32) -> i32) fa, ((i32) -> i32) f1, ((i64) -> bool fails LexError) fe, Span<i32> sp) {
        }

        void qVars() {
            var p = 1
            var q = 2
            var r = 3
            var s = 4
            var t = 5
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
        void qChar2() => '߿'
        void qChar3() => '＀'
        void qChar4() => '😀'
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

        enum Color {
            red
            green
        }

        union Shape {
            circle(f64 radius, f64 scale)
            dot
        }

        struct Box {
            void frobnicate() {
            }
        }

        i32 ov(i32 a) => a

        i32 ov(string s) => 0

        void sigs2(Untrusted<string> us, List<Untrusted<string>> lu, Span<Untrusted<string>> su, Untrusted<string>[2] au, Untrusted<string>? ou, Color col, ((string) -> i32) fs, ((i32) -> i32)? fo) {
        }

        void qFrob() => frobnicat
        void qOv() => ov
        void qMapArr() => Map<string, u8[4]>
        bool qCaught() => bare(1) catch switch {
            _ => false,
        }
        void qLam() => x => x
        void qKDecl() {
            const k = 5
        }
        void qK() => k
        void qFMax() => 1.7976931348623157e308
        void qFSub() => 4.9e-324
        void qF2p53() => 9007199254740993.0
        void qFInf() => 1e400
        void qI2p53() => 9007199254740993
        void qI2p127() => 170141183460469231731687303715884105728
        void qCharQ() => '\q'
        void qCharSur() => '\u{D800}'
        void qCharMax() => '\u{110000}'

        """;

    private sealed record Case(string Name, string Fn, string Op, string? Target = null, string? Target2 = null);

    /// <summary>
    /// <c>expr</c>: <c>BindExpr(e, target)</c>; <c>stmt</c>: <c>BindStatementExpression(e)</c>; <c>conv</c>:
    /// <c>Convert(BindExpr(e), target, e.Span)</c>, then to <c>Target2</c> if set; <c>fn</c>: <c>Convert(BindExprFor(e, target), …)</c>;
    /// <c>nofn</c>: <c>BindExpr(e)</c> in a binder with no function but <c>Point</c>'s <c>this</c>; <c>it</c>: <c>BindExpr(e)</c> with
    /// <c>_it</c> set to <c>Point.x</c>, as a refinement check sets it; <c>caught</c>/<c>caught-none</c>: <c>BindExpr</c> of the
    /// <c>catch switch</c> subject with and without <c>_caught</c>; <c>lambda</c>: <c>BindExpr(e, target)</c>, diagnostics only (the
    /// lambda itself is T35's <c>bindLambda</c>, express4.byn:18); <c>const</c>: <c>BindExpr(e)</c> after <c>DeclareLocal</c> of
    /// <c>qKDecl</c>'s <c>const k</c> holding 5.
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
        new("char-2-byte", "qChar2", "expr"),
        new("char-3-byte", "qChar3", "expr"),
        new("char-4-byte", "qChar4", "expr"),
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
        new("name-generic-args", "Point.tg", "expr"),
        new("name-in-method", "Point.tn", "expr"),
        new("name-no-function", "Point.tn", "nofn"),
        new("name-it", "Point.ti", "it"),
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
        new("conv-span", "qInt", "conv", "sp"),
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
        new("float-max", "qFMax", "expr"),
        new("float-subnormal", "qFSub", "expr"),
        new("float-2p53-plus-1", "qF2p53", "expr"),
        new("float-infinity", "qFInf", "expr"),
        new("int-2p53-plus-1-float-target", "qI2p53", "expr", "d"),
        new("int-2p127", "qI2p127", "expr"),
        new("int-2p127-int-target", "qI2p127", "expr", "small"),
        new("char-bad-escape", "qCharQ", "expr"),
        new("char-surrogate", "qCharSur", "expr"),
        new("char-above-max", "qCharMax", "expr"),
        new("name-typo-other-method", "qFrob", "expr"),
        new("name-array-type-arg", "qMapArr", "expr"),
        new("name-overloads", "qOv", "expr"),
        new("fn-overload-second", "qOv", "fn", "fs"),
        new("fn-overload-first", "qOv", "fn", "f1"),
        new("caught", "qCaught", "caught"),
        new("caught-none", "qCaught", "caught-none"),
        new("lambda-function", "qLam", "lambda", "f1"),
        new("lambda-optional-function", "qLam", "lambda", "fo"),
        new("lambda-value", "qLam", "lambda", "i"),
        new("lambda-none", "qLam", "lambda"),
        new("name-const-local", "qK", "const"),
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

    private static string KindName(System.Type t) => char.ToLowerInvariant(t.Name["Bound".Length]) + t.Name["Bound".Length..][1..];

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
        if (n is BoundFunctionRef fr) sb.Append($" {fr.Function.Name}({string.Join(", ", fr.Function.Parameters.Select(p => p.Type.DisplayName))})");
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

    private static TypeSymbol Sig(Oracle o, string name) =>
        o.Module.Functions["sigs"][0].Parameters.Concat(o.Module.Functions["sigs2"][0].Parameters).Single(p => p.Name == name).Type;

    private static BoundExpr Convert(object binder, BoundExpr value, TypeSymbol target, TextSpan span, bool argument = false) =>
        (BoundExpr)Call(binder, "Convert", value, target, span, argument, true)!;

    private static void SetField(object binder, string name, object? value) =>
        BinderType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(binder, value);

    /// <summary>The <c>catch switch</c> subject of <paramref name="e"/>, a <c>CaughtErrorExpr</c>.</summary>
    private static Expr CaughtSubject(Expr e) => ((SwitchExpr)((CatchExpr)e).Handler).Value;

    private static List<string> ExpectedCase(Case k)
    {
        var o = Oracle.Of("main", Source);
        var (fn, container, e) = Site(o, k.Fn);
        var binder = o.Binder(k.Op == "nofn" ? null : fn, container);
        if (k.Op == "it") SetField(binder, "_it", Point(o).Fields[0]);
        if (k.Op.StartsWith("caught", StringComparison.Ordinal)) e = CaughtSubject(e);
        if (k.Op == "caught") SetField(binder, "_caught", new LocalSymbol("e", null, o.Module.Types["LexError"], LocalKind.Let));
        if (k.Op == "const")
        {
            var decl = (LocalDecl)Fn(o, "qKDecl").Body!.Statements[0];
            var local = (LocalSymbol)Call(binder, "DeclareLocal", decl.Name, PrimitiveType.I32, LocalKind.Const)!;
            local.Constant = new ConstantValue((Int128)5);
        }
        var target = k.Target is null ? null : Sig(o, k.Target);
        if (k.Op == "lambda")
        {
            Call(binder, "BindExpr", e, target);
            return o.NewDiagnostics().ToList();
        }
        var r = k.Op switch
        {
            "expr" or "nofn" or "it" or "caught" or "caught-none" or "const" => (BoundExpr)Call(binder, "BindExpr", e, target)!,
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
            case EnumSymbol en: return $"enumTypeOf(e{en.Name})";
            case UntrustedType u: return $"internUntrusted(ref c, by: {BayanType(sb, u.Inner, ref n)})";
            case ListType l: return $"internList(ref c, by: {BayanType(sb, l.Element, ref n)})";
            case Bayan.Semantics.Symbols.ArrayType a: return $"internArray(ref c, by: {BayanType(sb, a.Element, ref n)}, at: {a.Length})";
            case SpanType sp: return $"internSpan(ref c, by: {BayanType(sb, sp.Element, ref n)}, readOnly: {Lower(sp.ReadOnly)})";
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

    /// <summary>The Bayan variable of <paramref name="fn"/>; a free function's later overloads add their index.</summary>
    private static string FunctionVar(FunctionSymbol fn)
    {
        if (fn.Owner is { } s) return $"fn{s.Name}{(fn.Kind == FunctionKind.Constructor ? "Ctor" : Camel(fn.Name))}";
        var i = fn.Module.Functions[fn.Name].IndexOf(fn);
        return $"fn{Camel(fn.Name)}{(i > 0 ? i.ToString() : "")}";
    }

    /// <summary>What T11's stubbed <c>declareSignature</c>/<c>declareParameters</c> would set on <paramref name="fn"/> (declare2.byn:15, :18).</summary>
    private static void Signature(StringBuilder sb, FunctionSymbol fn, ref int n)
    {
        var v = FunctionVar(fn);
        var ret = BayanType(sb, fn.ReturnType, ref n);
        sb.AppendLine($"    c.functions[{v}].returnType = {ret}");
        sb.AppendLine($"    c.functions[{v}].hasFails = {Lower(fn.Fails)}");
        foreach (var s in fn.ErrorSets) sb.AppendLine($"    c.functions[{v}].errorSets.append(e{s.Name})");
        foreach (var tp in fn.TypeParameters) sb.AppendLine($"    c.functions[{v}].typeParams.append(newTypeParam(ref c, by: TypeParam {{ name: \"{tp.Name}\" }}))");
        foreach (var p in fn.Parameters)
        {
            var type = BayanType(sb, p.Type, ref n);
            sb.AppendLine($"    tcParam(ref c, for: {v}, by: \"{p.Name}\", at: {Span(p.Location!.Value.Span)}, as: {type})");
        }
    }

    /// <summary>
    /// Bayan statements that declare the oracle source's types and free functions into module 0 of `c` as T11's declare pass
    /// does: each free function through <c>addFunction</c> (compile.byn:1115) then <c>checkOverloads</c> (declare.byn:571), each
    /// struct's members through <c>declareMembers</c> (declare.byn:13), the union's variants through <c>declareUnionVariants</c>
    /// (declare.byn:457); only what the stubbed <c>declareSignature</c>/<c>declareParameters</c> would set is filled by hand.
    /// </summary>
    private static string Symbols(Oracle o)
    {
        var sb = new StringBuilder();
        var n = 0;
        var lexDecl = IndexOf(o, d => d is ErrorSetDecl { Name.Text: "LexError" });
        var colorDecl = IndexOf(o, d => d is EnumDecl { Name.Text: "Color" });
        var shapeDecl = IndexOf(o, d => d is UnionDecl { Name.Text: "Shape" });
        var structs = new[] { "Point", "Sku", "Box" };
        foreach (var name in structs)
        {
            var decl = IndexOf(o, d => d is StructDecl sd && sd.Name.Text == name || d is TypeAliasDecl ta && ta.Name.Text == name);
            sb.AppendLine($"    let s{name} = newStruct(ref c, by: StructSymbol {{ name: \"{name}\", module: 0, file: 0, declIndex: {decl} }})");
            sb.AppendLine($"    c.modules[0].types[\"{name}\"] = structTypeOf(s{name})");
            sb.AppendLine($"    c.modules[0].structs.append(s{name})");
        }
        sb.AppendLine("    c.distinct[sSku] = 1");
        sb.AppendLine($"    let eLexError = newErrorSet(ref c, by: ErrorSetSymbol {{ name: \"LexError\", module: 0, file: 0, declIndex: {lexDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"LexError\"] = errorSetTypeOf(eLexError)");
        sb.AppendLine("    c.modules[0].errorSets.append(eLexError)");
        sb.AppendLine($"    let eColor = newEnum(ref c, by: EnumSymbol {{ name: \"Color\", module: 0, file: 0, declIndex: {colorDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"Color\"] = enumTypeOf(eColor)");
        sb.AppendLine("    c.modules[0].enums.append(eColor)");
        sb.AppendLine($"    let uShape = newUnion(ref c, by: UnionSymbol {{ name: \"Shape\", module: 0, file: 0, declIndex: {shapeDecl} }})");
        sb.AppendLine("    c.modules[0].types[\"Shape\"] = unionTypeOf(uShape)");
        sb.AppendLine("    c.modules[0].unions.append(uShape)");
        sb.AppendLine("    declareUnionVariants(ref c, for: uShape)");
        foreach (var name in structs) sb.AppendLine($"    declareMembers(ref c, for: s{name})");
        foreach (var name in new[] { "Point", "Box" })
        {
            var st = (StructSymbol)o.Module.Types[name];
            foreach (var ctor in st.Constructors)
            {
                sb.AppendLine($"    let {FunctionVar(ctor)} = c.structs[s{name}].constructors[0]");
                Signature(sb, ctor, ref n);
            }
            foreach (var m in st.Methods)
            {
                sb.AppendLine($"    let {FunctionVar(m)} = tcMethod(in c, of: s{name}, by: \"{m.Name}\")");
                Signature(sb, m, ref n);
            }
        }
        foreach (var (name, fns) in o.Module.Functions)
        {
            var decls = o.Members.OfType<FunctionDecl>().Where(fd => fd.Name.Text == name).ToArray();
            for (var i = 0; i < fns.Count; i++)
            {
                var v = FunctionVar(fns[i]);
                var decl = Array.IndexOf(o.Members, decls[i]);
                sb.AppendLine($"    let {v} = newFunction(ref c, by: FunctionSymbol {{ name: \"{name}\", nameSpan: {Span(decls[i].Name.Span)}, span: {Span(decls[i].Name.Span)}, module: 0, kind: .function, file: 0, declIndex: {decl} }})");
                sb.AppendLine($"    addFunction(ref c, from: 0, at: {Span(decls[i].Name.Span)}, for: {v})");
                Signature(sb, fns[i], ref n);
            }
        }
        sb.AppendLine("    checkOverloads(ref c, for: 0)");
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
            sb.AppendLine($"    var b = tcBinder({(k.Op == "nofn" ? "-1" : FunctionVar(fn))}, by: {(container is null ? "-1" : "sPoint")})");
            sb.AppendLine("    t15CaptureTypes(ref b, through: ref c)");
            sb.AppendLine("    pushScope(ref b)");
            if (k.Op == "it") sb.AppendLine("    b.itField = c.structs[sPoint].fields[0]");
            if (k.Op == "caught") sb.AppendLine("    b.caught = newLocal(ref c, by: LocalSymbol { name: \"e\", type: errorSetTypeOf(eLexError), kind: .letKeyword })");
            if (k.Op == "const")
            {
                var decl = (LocalDecl)Fn(o, "qKDecl").Body!.Statements[0];
                sb.AppendLine($"    let kl = declareLocal(ref b, through: ref c, at: {Span(decl.Name.Span)}, as: prim(.i32), for: .constKeyword)");
                sb.AppendLine("    c.locals[kl].constant = Constant { kind: .integer, integer: bigFromI64(5) }");
            }
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
            body.AppendLine("            let span = parser.exprSpan(in $E)");
            var r = k.Target2 is null ? "let r" : "var r";
            body.AppendLine((k.Op switch
            {
                "expr" or "nofn" or "it" or "caught" or "caught-none" or "const" or "lambda" => "            $R = bindExpr(ref b, through: ref c, for: in $E, to: target)",
                "stmt" => "            $R = bindStatementExpression(ref b, through: ref c, for: in $E)",
                "conv" => "            $R = convert(ref b, through: ref c, for: bindExpr(ref b, through: ref c, for: in $E), to: target, at: span)",
                "fn" => "            $R = convert(ref b, through: ref c, for: bindExprFor(ref b, through: ref c, for: in $E, to: target), to: target, at: span)",
                _ => throw new InvalidOperationException(k.Op),
            }).Replace("$R", r));
            if (k.Target2 is not null) body.AppendLine("            r = convert(ref b, through: ref c, for: r, to: target2, at: span)");
            body.Append(k.Op == "lambda" ? "            let _ = r" : "            tcDump(ref c, for: r, at: 0)");
            var text = body.ToString();
            if (k.Op.StartsWith("caught", StringComparison.Ordinal))
                text = string.Join("\n",
                    "            switch $E {",
                    "                case .catchExpr(let ce):",
                    "                    switch ce.value.handler {",
                    "                        case .switchExpr(let sw):",
                    text.Replace("$E", "sw.value.value"),
                    "                        default:",
                    "                            break",
                    "                    }",
                    "                default:",
                    "                    break",
                    "            }");
            AtExpression(sb, o, k.Fn, text);
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
    /// Five `var` declarations d0..d4 of `qVars` and locals `p`, `q`, `s`, `t`: `p` pending on d0, then `q` on d1, with
    /// <c>Constrain</c> to targets and to other values, and <c>FindPeer</c> along a chain of peers; then `s` on d3 and `t` on
    /// d4 inside hand-built binaries and unaries (the nodes T19's <c>BindBinary</c>/<c>BindUnary</c> would build), with
    /// <c>IsPending</c> on each and <c>Constrain</c> through them.
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
        var s = new LocalSymbol("s", null, PrimitiveType.I32, LocalKind.Var);
        var t = new LocalSymbol("t", null, PrimitiveType.I32, LocalKind.Var);
        var ls = new BoundLocal(decls[3], s);
        var lt = new BoundLocal(decls[4], t);
        pending[s] = decls[3];
        pending[t] = decls[4];
        var sum = Fn(o, "qSum").ExpressionBody!;
        var neg = Fn(o, "qNeg").ExpressionBody!;
        var bin = new BoundBinary(sum, BinaryOp.Add, ls, lit, PrimitiveType.I32);
        var binTyped = new BoundBinary(sum, BinaryOp.Add, ls, typed, PrimitiveType.I32);
        var binBool = new BoundBinary(sum, BinaryOp.Add, ls, lit, PrimitiveType.Bool);
        var un = new BoundUnary(neg, UnaryOp.Negate, lt, PrimitiveType.I32);
        var unFloat = new BoundUnary(neg, UnaryOp.Negate, lt, PrimitiveType.F64);
        string Pending(BoundExpr e) => Lower((bool)Call(binder, "IsPending", e)!);
        lines.Add($"binary {Pending(bin)} {Pending(binTyped)} {Pending(binBool)}");
        lines.Add($"unary {Pending(un)} {Pending(unFloat)}");
        Call(binder, "Constrain", bin, PrimitiveType.I16);
        Call(binder, "Constrain", un, PrimitiveType.I8);
        lines.Add($"constraints {Constraints()}");
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
        sb.AppendLine("    for d in 0..5 {");
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
        sb.AppendLine("    var lit = List<BExpr>()");
        sb.AppendLine("    var typed = List<BExpr>()");
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
        sb.AppendLine("    let s = newLocal(ref c, by: LocalSymbol { name: \"s\", type: prim(.i32), kind: .varKeyword })");
        sb.AppendLine("    let t = newLocal(ref c, by: LocalSymbol { name: \"t\", type: prim(.i32), kind: .varKeyword })");
        sb.AppendLine("    let ls = addLocal(ref c, from: BLocal { span: at, local: s, type: prim(.i32) })");
        sb.AppendLine("    let lt = addLocal(ref c, from: BLocal { span: at, local: t, type: prim(.i32) })");
        sb.AppendLine("    b.pending[s] = 3");
        sb.AppendLine("    b.pending[t] = 4");
        foreach (var decl in new[] { qInt, qL })
        {
            sb.AppendLine($"    switch c.files[0].unit.members[{decl}] {{");
            sb.AppendLine("        case .functionDecl(let d):");
            sb.AppendLine($"            {(decl == qInt ? "lit" : "typed")}.append(bindExpr(ref b, through: ref c, for: in d.value.expressionBody))");
            sb.AppendLine("        default:");
            sb.AppendLine("            break");
            sb.AppendLine("    }");
        }
        sb.AppendLine("    let bin = addBinary(ref c, from: BBinary { span: at, op: .add, left: ls, right: lit[0], type: prim(.i32) })");
        sb.AppendLine("    let binTyped = addBinary(ref c, from: BBinary { span: at, op: .add, left: ls, right: typed[0], type: prim(.i32) })");
        sb.AppendLine("    let binBool = addBinary(ref c, from: BBinary { span: at, op: .add, left: ls, right: lit[0], type: prim(.bool) })");
        sb.AppendLine("    let un = addUnary(ref c, from: BUnary { span: at, op: .negate, operand: lt, type: prim(.i32) })");
        sb.AppendLine("    let unFloat = addUnary(ref c, from: BUnary { span: at, op: .negate, operand: lt, type: prim(.f64) })");
        sb.AppendLine("    print($\"binary {isPending(ref b, through: ref c, for: bin)} {isPending(ref b, through: ref c, for: binTyped)} {isPending(ref b, through: ref c, for: binBool)}\")");
        sb.AppendLine("    print($\"unary {isPending(ref b, through: ref c, for: un)} {isPending(ref b, through: ref c, for: unFloat)}\")");
        sb.AppendLine("    constrain(ref b, through: ref c, for: bin, to: prim(.i16))");
        sb.AppendLine("    constrain(ref b, through: ref c, for: un, to: prim(.i8))");
        sb.AppendLine("    print($\"constraints {t17tConstraints(in b, through: ref c)}\")");
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

        /// The C# node's class name without `Bound`, first letter lowered: where the Bayan kind is renamed to dodge a keyword.
        string tcKindName(BoundKind k) {
            switch k {
                case .defaultValue:
                    return "default"
                case .thisRef:
                    return "this"
                case .withExpr:
                    return "with"
                case .isExpr:
                    return "is"
                case .moveExpr:
                    return "move"
                case .tryExpr:
                    return "try"
                case .catchExpr:
                    return "catch"
                case .returnStmt:
                    return "return"
                case .failStmt:
                    return "fail"
                case .becomeStmt:
                    return "become"
                case .breakStmt:
                    return "break"
                case .continueStmt:
                    return "continue"
                case .ifStmt:
                    return "if"
                case .whileStmt:
                    return "while"
                case .forStmt:
                    return "for"
                case .switchStmt:
                    return "switch"
                case .deferStmt:
                    return "defer"
                case .assertStmt:
                    return "assert"
                case .errdeferStmt:
                    return "errdefer"
                case .contextStmt:
                    return "context"
                default:
                    return $"{k}"
            }
        }

        FuncId tcMethod(in Ctx c, of StructId s, by string name) {
            for m in c.structs[s].methods {
                if c.functions[m].name == name { return m }
            }
            panic("internal error: the struct has the method")
        }

        BExpr tcLocalOf(ref Ctx c, by string name, at TextSpan span, as Type type) {
            let l = newLocal(ref c, by: LocalSymbol { name: name, type: type, kind: .letKeyword })
            return addLocal(ref c, from: BLocal { span: span, local: l, type: type })
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
            sb.append($"{tcKindName(k)} {span.start}..{span.end}")
            if isExprKind(k) {
                sb.append($" : {typeDisplay(ref c, by: exprType(in c, for: n))}")
                if (exprConstant(in c, for: n) is { } v) { sb.append($" = {constantText(in v)}") }
            }
            if k == .local { sb.append($" {c.locals[c.bLocals[boundIndex(n)].local].name}") }
            if k == .call { sb.append($" {c.functions[c.bCalls[boundIndex(n)].function].name}") }
            if k == .functionRef {
                let f = c.bFunctionRefs[boundIndex(n)].function
                var ps = StrBuf()
                for i in 0..c.functions[f].parameters.length {
                    if i > 0 { ps.append(", ") }
                    ps.append(typeDisplay(ref c, by: c.locals[c.functions[f].parameters[i]].type))
                }
                sb.append($" {c.functions[f].name}({ps.finish()})")
            }
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
