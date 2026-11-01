using System.Reflection;
using System.Text;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;

namespace Bayan.Tests;

/// <summary>
/// T24: <c>selfhost/semantics/ownership.byn</c> against the C# <c>OwnershipAnalysis</c>, <c>NeverReassignedVarLint</c>,
/// <c>SpanParameterLint</c> and <c>PanicHandlerCheck</c>. The statement and expression binders are still stubs (T35, T36,
/// T39), so the C# side compiles <see cref="Source"/>, then writes each case function's bound body and locals out as Bayan
/// code that rebuilds them on the Bayan declarations; the generated main runs the four ports over that tree. Every
/// expected line is the four C# analyses rerun on the same function.
/// </summary>
public sealed class SelfhostSemanticsT24Tests
{
    private const string Dir = "tmp/t24-cases";

    private const string Source = """
        union error E {
            bad
        }

        struct Bag {
            List<i64> items
            i64 n
        }

        struct Pair {
            i64 a
            i64 b
        }

        void sink(move List<i64> ys) {
            print($"{ys.length}")
        }

        void each(List<i64> xs, by (i64 -> void) f) {
            for x in xs {
                f(x)
            }
        }

        i64 step() fails E {
            return 1
        }

        void copyParam(List<i64> xs) {
            let ys = xs
            print($"{ys.length}")
        }

        void copyLocals() {
            var a = List<i64>()
            a.append(1)
            let b = a
            let m = Map<string, i64>()
            let n = m
            var sb = StrBuf()
            sb.append("x")
            let other = sb
            let moved = move m
            print($"{a.length} {b.length} {n.length} {moved.length} {sb.finish()} {other.finish()}")
        }

        void storeInto(move List<i64> xs) {
            var bag = Bag { items: List<i64>(), n: 0 }
            bag.items = xs
            var all = List<List<i64>>()
            all.append(bag.items)
            let pair = Bag { items: bag.items, n: 1 }
            print($"{pair.n} {all.length}")
        }

        void withCopy() {
            let bag = Bag { items: List<i64>(), n: 0 }
            let other = bag with { n: 1 }
            var p = Pair { a: 1, b: 2 }
            p = p with { a: 3 }
            let q = p with { b: 4 }
            print($"{other.n} {q.a}")
        }

        List<i64> returns(bool b, move List<i64> xs) {
            let ys = List<i64>()
            if b {
                return xs
            }
            return ys
        }

        void useAfterMove() {
            var a = List<i64>()
            var b = move a
            a.append(1)
            print($"{b.length}")
        }

        void loopMove(bool ok) {
            let xs = List<i64>()
            for i in 0..3 {
                sink(move xs)
            }
            var ys = List<i64>()
            while ok {
                if ok {
                    sink(move ys)
                    break
                }
                ys = List<i64>()
            }
            print($"{ys.length}")
        }

        void branches(bool b) {
            let xs = List<i64>()
            if b {
                sink(move xs)
            } else {
                print("no")
            }
            print($"{xs.length}")
            let zs = List<i64>()
            let n = b && zs.length > 0
            let ws = List<i64>()
            let k = b ? ws.length : 0
            print($"{n} {k}")
        }

        void deferMove() {
            let xs = List<i64>()
            defer print($"{xs.length}")
            sink(move xs)
            return
        }

        void errdeferMove() fails E {
            let xs = List<i64>()
            defer failing print($"{xs.length}")
            sink(move xs)
            let n = try step()
            print($"{n}")
        }

        void readsMoved() {
            let ys = List<i64>()
            defer panicking print($"handler sees {ys.length}")
            sink(move ys)
            panic("stop")
        }

        void captures() {
            let ys = List<i64>()
            let zs = List<i64>()
            each(ys, by: x => print($"{x} {zs.length}"))
        }

        void vars(i64? found) {
            var total = 3
            var count = 0
            count += 1
            var p = Pair { a: 1, b: 2 }
            p.a = 2
            if found is var v {
                print($"{v}")
            }
            print($"{total} {count} {p.a}")
        }

        i64 total(Span<i64> values) {
            var sum: i64 = 0
            for v in values {
                sum += v
            }
            return sum + values[0] + values.length
        }

        void zero(Span<i64> values) {
            values[0] = 0
        }

        void bump(ref i64 x) {
            x += 1
        }

        void bumpFirst(Span<i64> values) {
            bump(ref values[0])
        }

        void forward(Span<i64> values) {
            zero(values[1..])
        }

        i64 viaReadOnly(Span<i64> values) => sum(values)

        i64 sum(ReadOnlySpan<i64> values) => values.length

        void handlers(i64 limit) {
            var count = 0
            let total = 3
            defer panicking (p) print($"{p.message}: {total} of {limit}, {count} done")
            count += 1
        }

        i64 restricted() fails E {
            var count = 0
            defer panicking {
                let n = try step()
                count = n
                let xs = List<i64>()
                let s = $"{n}"
                return 0
            }
            for i in 0..3 {
                defer panicking {
                    break
                }
            }
            count += 1
            return count
        }

        void onCrash(Panic p) {
            let s = $"{p.message}"
            for i in 0..3 {
                if i > 1 {
                    break
                }
                continue
            }
            return
        }

        void main() {
            Panic.handler = onCrash
            print($"{restricted() catch -1}")
        }
        """;

    private static readonly string[] Cases =
    [
        "copyParam", "copyLocals", "storeInto", "withCopy", "returns", "useAfterMove", "loopMove", "branches", "deferMove",
        "errdeferMove", "readsMoved", "captures", "vars", "total", "zero", "bumpFirst", "forward", "viaReadOnly", "sum",
        "handlers", "restricted", "onCrash",
    ];

    private static readonly (string Type, string Name)[] Analyses =
    [
        ("Bayan.Semantics.Binding.OwnershipAnalysis", "analyzeOwnership"),
        ("Bayan.Semantics.Binding.NeverReassignedVarLint", "lintNeverReassignedVar"),
        ("Bayan.Semantics.Binding.SpanParameterLint", "lintSpanParameter"),
        ("Bayan.Semantics.Binding.PanicHandlerCheck", "checkPanicHandler"),
    ];

    private static readonly Lazy<Compilation> Compiled = new(() =>
    {
        Directory.CreateDirectory(Dir);
        var c = Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), Source + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));
        var parse = c.Diagnostics.Items.Where(d => d.Code.StartsWith("BYN1", StringComparison.Ordinal)).ToList();
        if (parse.Count > 0) throw new InvalidOperationException(string.Join("\n", parse.Select(Diag)));
        return c;
    });

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static FunctionSymbol Function(Compilation c, string name)
    {
        var module = c.Modules.First(m => m.Files.Any(f => f.Source.Path == Path.Combine(Dir, "main.byn")));
        return module.AllFunctions.First(f => f.Name == name && f.Owner is null);
    }

    /// <summary>The C# lines for one case and analysis: the diagnostics it reports when rerun on the function.</summary>
    private static List<string> Expected(string name, int analysis)
    {
        var c = Compiled.Value;
        lock (c)
        {
            var type = typeof(Compilation).Assembly.GetType(Analyses[analysis].Type) ?? throw new InvalidOperationException(Analyses[analysis].Type);
            var mark = c.Diagnostics.Items.Count;
            type.GetMethod("Analyze", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [c, Function(c, name)]);
            var lines = new List<string>();
            for (var i = mark; i < c.Diagnostics.Items.Count; i++) lines.AddRange(Diag(c.Diagnostics.Items[i]).Split('\n'));
            if (lines.Count == 0) lines.Add("ok");
            return lines;
        }
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
    {
        Directory.CreateDirectory(Dir);
        return SelfhostSemanticsHarness.RunSections(MainSource());
    });

    public static TheoryData<string, int> CaseNames()
    {
        var data = new TheoryData<string, int>();
        foreach (var k in Cases)
            for (var a = 0; a < Analyses.Length; a++) data.Add(k, a);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string name, int analysis) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, $"{Analyses[analysis].Name}.{name}", Expected(name, analysis));

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    // ── The C# bound tree written out as Bayan ───────────────────────────────────

    /// <summary>
    /// Writes one function's bound body and the symbols it reaches as Bayan statements building them. A function the
    /// Bayan <c>declare</c> made is looked up by owner and name; a lambda, a built-in and an invoke are made here.
    /// </summary>
    private sealed class Writer(StringBuilder sb)
    {
        private readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
        private int _n;

        private string Fresh(string prefix) => $"{prefix}{_n++}";

        private void Line(string s) => sb.Append("    ").Append(s).Append('\n');

        private static string Span(Node n) => $"TextSpan {{ start: {n.Span.Start}, length: {n.Span.Length} }}";

        private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s[1..];

        private string Ty(TypeSymbol t)
        {
            switch (t)
            {
                case PrimitiveType p when p.IsNever: return "prim(.neverKind)";
                case PrimitiveType p: return $"prim(.{Lower(p.Kind.ToString())})";
                case ErrorType: return "errorType()";
                case ErrorSetSymbol s: return $"errorSetTypeOf(t24mSet(ref c, by: \"{s.Name}\"))";
                case ErrorUnionType u:
                {
                    var sets = List("ErrorSetId", u.Sets.Select(s => $"t24mSet(ref c, by: \"{s.Name}\")").ToList());
                    if (u.Excluded.Count > 0) throw new NotSupportedException("excluded errors");
                    var excluded = List("VariantId", []);
                    return $"errorUnionOf(ref c, for: in {sets}, as: in {excluded})";
                }
                case FunctionType f:
                {
                    var ps = List("Type", f.Parameters.Select(Ty).ToList());
                    var ret = Ty(f.Return);
                    var sets = List("ErrorSetId", f.Fails.Select(s => $"t24mSet(ref c, by: \"{s.Name}\")").ToList());
                    var uses = List("string", f.Uses.Select(u => $"\"{u}\"").ToList());
                    return $"internFunction(ref c, for: in {ps}, to: {ret}, by: in {sets}, as: in {uses})";
                }
                case ListType l: return $"internList(ref c, by: {Ty(l.Element)})";
                case MapType m: return $"internMap(ref c, by: {Ty(m.KeyType)}, to: {Ty(m.ValueType)})";
                case SpanType s: return $"internSpan(ref c, by: {Ty(s.Element)}, readOnly: {(s.ReadOnly ? "true" : "false")})";
                case Bayan.Semantics.Symbols.OptionalType o: return $"internOptional(ref c, by: {Ty(o.Inner)})";
                case BuiltinType b: return $"builtinType(.{Lower(b.Kind.ToString())})";
                case StructSymbol s: return $"structTypeOf(t24mStruct(ref c, by: \"{s.Name}\"))";
                case EnumSymbol n: return $"enumTypeOf(t24mEnum(ref c, by: \"{n.Name}\"))";
                default: throw new NotSupportedException(t.GetType().Name);
            }
        }

        private string Local(LocalSymbol l)
        {
            if (_ids.TryGetValue(l, out var id)) return id;
            if (l is ParameterSymbol) throw new NotSupportedException($"parameter {l.Name} of an undeclared function");
            id = Fresh("l");
            var at = l.Location is { } loc ? $"TextSpan {{ start: {loc.Span.Start}, length: {loc.Span.Length} }}" : "TextSpan { start: 0, length: 0 }";
            var keyword = l.KeywordSpan is { } k ? $"TextSpan {{ start: {k.Start}, length: {k.Length} }}" : "null";
            var kind = l.Kind switch
            {
                LocalKind.Let => "letKeyword",
                LocalKind.Var => "varKeyword",
                LocalKind.Const => "constKeyword",
                _ => Lower(l.Kind.ToString()),
            };
            Line($"let {id} = newLocal(ref c, by: LocalSymbol {{ name: \"{l.Name}\", nameSpan: {at}, type: {Ty(l.Type)}, kind: .{kind}, borrowsSubject: {(l.BorrowsSubject ? "true" : "false")}, keywordSpan: {keyword}, written: {(l.Written ? "true" : "false")} }})");
            return _ids[l] = id;
        }

        private string Target(JumpTarget j)
        {
            if (_ids.TryGetValue(j, out var id)) return id;
            id = Fresh("j");
            Line($"let {id} = c.jumpTargets.length");
            Line("c.jumpTargets.append(JumpTarget {})");
            return _ids[j] = id;
        }

        private string Field(FieldSymbol f) => $"t24mField(ref c, by: \"{f.Owner.Name}\", for: \"{f.Name}\")";

        public string Function(FunctionSymbol f)
        {
            if (_ids.TryGetValue(f, out var id)) return id;
            if (f.Kind is not (FunctionKind.Builtin or FunctionKind.Invoke or FunctionKind.Lambda))
            {
                id = Fresh("f");
                Line($"let {id} = t24mFn(ref c, by: \"{f.Owner?.Name ?? ""}\", for: \"{f.Name}\")");
                // The body's parameters are the ones `declareSignature` declared.
                for (var k = 0; k < f.Parameters.Count; k++)
                {
                    var l = Fresh("l");
                    Line($"let {l} = c.functions[{id}].parameters[{k}]");
                    _ids[f.Parameters[k]] = l;
                }
                return _ids[f] = id;
            }
            var ps = Fresh("a");
            Line($"{(f.Parameters.Count > 0 ? "var" : "let")} {ps} = List<LocalId>()");
            foreach (var p in f.Parameters)
            {
                var l = Fresh("l");
                var modifier = p.Modifier switch
                {
                    ParamModifier.None => "none",
                    ParamModifier.Ref => "refKeyword",
                    ParamModifier.Move => "moveKeyword",
                    ParamModifier.Out => "outKeyword",
                    ParamModifier.In => "inKeyword",
                    _ => throw new NotSupportedException(p.Modifier.ToString()),
                };
                Line($"let {l} = newLocal(ref c, by: LocalSymbol {{ name: \"{p.Name}\", type: {Ty(p.Type)}, kind: .parameter, isParameter: true, modifier: .{modifier}, ordinal: {p.Ordinal} }})");
                _ids[p] = l;
                Line($"{ps}.append({l})");
            }
            id = Fresh("f");
            var kind = Lower(f.Kind.ToString());
            var builtin = f.Kind == FunctionKind.Builtin ? $"builtin: .{Lower(f.Builtin.ToString())}, " : "";
            Line($"let {id} = newFunction(ref c, by: FunctionSymbol {{ name: \"{f.Name}\", module: c.entryModule, kind: .{kind}, {builtin}file: -1, parameters: move {ps}, returnType: {Ty(f.ReturnType)} }})");
            return _ids[f] = id;
        }

        private string List(string type, IReadOnlyList<string> items)
        {
            var id = Fresh("a");
            Line($"{(items.Count > 0 ? "var" : "let")} {id} = List<{type}>()");
            foreach (var x in items) Line($"{id}.append({x})");
            return id;
        }

        private string Opt(BoundExpr? e) => e is null ? "-1" : Expr(e);

        public string Pattern(BoundPattern p)
        {
            var sp = Span(p.Syntax);
            var id = Fresh("p");
            switch (p)
            {
                case BoundBindPattern b:
                    Line($"let {id} = addBindPattern(ref c, from: BBindPattern {{ span: {sp}, inputType: {Ty(b.InputType)}, local: {(b.Local is null ? "-1" : Local(b.Local))} }})");
                    break;
                case BoundConstantPattern k:
                {
                    var value = Expr(k.Value);
                    Line($"let {id} = addConstantPattern(ref c, from: BConstantPattern {{ span: {sp}, inputType: {Ty(k.InputType)}, value: {value} }})");
                    break;
                }
                default:
                    throw new NotSupportedException(p.GetType().Name);
            }
            return id;
        }

        public string Expr(BoundExpr e)
        {
            var sp = Span(e.Syntax);
            var id = Fresh("e");
            switch (e)
            {
                case BoundLiteral lit:
                    Line($"let {id} = addLiteral(ref c, from: BLiteral {{ span: {sp}, type: {Ty(lit.Type)}, value: Constant {{ kind: .integer }} }})");
                    break;
                case BoundLocal l:
                    Line($"let {id} = addLocal(ref c, from: BLocal {{ span: {sp}, local: {Local(l.Local)}, type: {Ty(l.Type)} }})");
                    break;
                case BoundThis t:
                    Line($"let {id} = addThis(ref c, from: BThis {{ span: {sp}, type: {Ty(t.Type)} }})");
                    break;
                case BoundField f:
                {
                    var receiver = Expr(f.Receiver);
                    Line($"let {id} = addField(ref c, from: BField {{ span: {sp}, receiver: {receiver}, field: {Field(f.Field)}, type: {Ty(f.Type)} }})");
                    break;
                }
                case BoundCall call:
                {
                    var fn = Function(call.Function);
                    var receiver = Opt(call.Receiver);
                    var args = List("BExpr", call.Arguments.Select(Expr).ToList());
                    Line($"let {id} = addCall(ref c, from: BCall {{ span: {sp}, function: {fn}, receiver: {receiver}, arguments: move {args}, type: {Ty(call.Type)} }})");
                    break;
                }
                case BoundBuiltinCall b:
                {
                    var receiver = Expr(b.Receiver);
                    var args = List("BExpr", b.Arguments.Select(Expr).ToList());
                    Line($"let {id} = addBuiltinCall(ref c, from: BBuiltinCall {{ span: {sp}, member: .{Lower(b.Member.ToString())}, receiver: {receiver}, arguments: move {args}, type: {Ty(b.Type)} }})");
                    break;
                }
                case BoundUnary u:
                {
                    var operand = Expr(u.Operand);
                    var op = u.Op == UnaryOp.Not ? "notKeyword" : Lower(u.Op.ToString());
                    Line($"let {id} = addUnary(ref c, from: BUnary {{ span: {sp}, op: .{op}, operand: {operand}, type: {Ty(u.Type)} }})");
                    break;
                }
                case BoundBinary b:
                {
                    var left = Expr(b.Left);
                    var right = Expr(b.Right);
                    Line($"let {id} = addBinary(ref c, from: BBinary {{ span: {sp}, op: .{Lower(b.Op.ToString())}, left: {left}, right: {right}, type: {Ty(b.Type)} }})");
                    break;
                }
                case BoundConversion k:
                {
                    var operand = Expr(k.Operand);
                    Line($"let {id} = addConversion(ref c, from: BConversion {{ span: {sp}, operand: {operand}, type: {Ty(k.Type)}, kind: .{Lower(k.Kind.ToString())} }})");
                    break;
                }
                case BoundConditional k:
                {
                    var cond = Expr(k.Condition);
                    var t = Expr(k.WhenTrue);
                    var f = Expr(k.WhenFalse);
                    Line($"let {id} = addConditional(ref c, from: BConditional {{ span: {sp}, condition: {cond}, whenTrue: {t}, whenFalse: {f}, type: {Ty(k.Type)} }})");
                    break;
                }
                case BoundCoalesce k:
                {
                    var left = Expr(k.Left);
                    var right = Expr(k.Right);
                    Line($"let {id} = addCoalesce(ref c, from: BCoalesce {{ span: {sp}, left: {left}, right: {right}, type: {Ty(k.Type)} }})");
                    break;
                }
                case BoundStructLiteral s:
                {
                    var fields = List("BExpr", s.Fields.Select(Expr).ToList());
                    Line($"let {id} = addStructLiteral(ref c, from: BStructLiteral {{ span: {sp}, structId: t24mStruct(ref c, by: \"{s.Struct.Name}\"), fields: move {fields}, type: {Ty(s.Type)} }})");
                    break;
                }
                case BoundWith w:
                {
                    var target = Expr(w.Target);
                    var updates = List("BWithUpdate", w.Updates.Select(u => $"BWithUpdate {{ field: {Field(u.Field)}, value: {Expr(u.Value)} }}").ToList());
                    Line($"let {id} = addWith(ref c, from: BWith {{ span: {sp}, target: {target}, updates: move {updates}, type: {Ty(w.Type)} }})");
                    break;
                }
                case BoundCollection k:
                {
                    var elements = List("BExpr", k.Elements.Select(Expr).ToList());
                    Line($"let {id} = addCollection(ref c, from: BCollection {{ span: {sp}, type: {Ty(k.Type)}, elements: move {elements} }})");
                    break;
                }
                case BoundIndex x:
                {
                    var target = Expr(x.Target);
                    var index = Expr(x.Index);
                    Line($"let {id} = addIndex(ref c, from: BIndex {{ span: {sp}, target: {target}, index: {index}, fromEnd: {(x.FromEnd ? "true" : "false")}, type: {Ty(x.Type)} }})");
                    break;
                }
                case BoundSlice s:
                {
                    var target = Expr(s.Target);
                    var start = Opt(s.Start);
                    var end = Opt(s.End);
                    Line($"let {id} = addSlice(ref c, from: BSlice {{ span: {sp}, target: {target}, start: {start}, startFromEnd: {(s.StartFromEnd ? "true" : "false")}, end: {end}, endFromEnd: {(s.EndFromEnd ? "true" : "false")}, inclusive: {(s.Inclusive ? "true" : "false")}, type: {Ty(s.Type)} }})");
                    break;
                }
                case BoundRange r:
                {
                    var start = Expr(r.Start);
                    var end = Expr(r.End);
                    Line($"let {id} = addRange(ref c, from: BRange {{ span: {sp}, start: {start}, end: {end}, inclusive: {(r.Inclusive ? "true" : "false")}, type: prim(.i64) }})");
                    break;
                }
                case BoundIs i:
                {
                    var value = Expr(i.Value);
                    var pattern = Pattern(i.Pattern);
                    Line($"let {id} = addIs(ref c, from: BIs {{ span: {sp}, value: {value}, pattern: {pattern} }})");
                    break;
                }
                case BoundInterpolated n:
                {
                    var parts = List("BInterpolationPart", n.Parts.Select(part => part.Value is null
                        ? $"BInterpolationPart {{ text: \"{Escape(part.Text ?? "")}\" }}"
                        : $"BInterpolationPart {{ value: {Expr(part.Value)} }}").ToList());
                    Line($"let {id} = addInterpolated(ref c, from: BInterpolated {{ span: {sp}, parts: move {parts} }})");
                    break;
                }
                case BoundMove m:
                {
                    var operand = Expr(m.Operand);
                    Line($"let {id} = addMove(ref c, from: BMove {{ span: {sp}, operand: {operand}, type: {Ty(m.Type)} }})");
                    break;
                }
                case BoundTry t:
                {
                    var operand = Expr(t.Operand);
                    Line($"let {id} = addTry(ref c, from: BTry {{ span: {sp}, operand: {operand}, type: {Ty(t.Type)} }})");
                    break;
                }
                case BoundCatch k:
                {
                    if (k.Variant is not null) throw new NotSupportedException("catch variant");
                    var operand = Expr(k.Operand);
                    var binding = k.Binding is null ? "-1" : Local(k.Binding);
                    var handler = Expr(k.Handler);
                    Line($"let {id} = addCatch(ref c, from: BCatch {{ span: {sp}, operand: {operand}, binding: {binding}, handler: {handler}, type: {Ty(k.Type)} }})");
                    break;
                }
                case BoundLambda l:
                {
                    var fn = Function(l.Lambda);
                    var captures = List("BExpr", l.Captures.Select(x => Expr(x)).ToList());
                    Line($"let {id} = addLambda(ref c, from: BLambda {{ span: {sp}, lambda: {fn}, captures: move {captures}, functionType: {Ty(l.FunctionType)} }})");
                    // The lambda's body is not walked by the four analyses, which run on their own function.
                    break;
                }
                case BoundEnumMember m:
                    Line($"let {id} = addEnumMember(ref c, from: BEnumMember {{ span: {sp}, member: t24mEnumMember(ref c, by: \"{m.Member.Enum.Name}\", for: \"{m.Member.Name}\") }})");
                    break;
                case BoundErrorExpr:
                    Line($"let {id} = addErrorExpr(ref c, from: BErrorExpr {{ span: {sp} }})");
                    break;
                default:
                    throw new NotSupportedException(e.GetType().Name);
            }
            if (e.Constant?.Value is bool flag) Line($"setConstant(ref c, for: {id}, to: Constant {{ kind: .flag, flag: {(flag ? "true" : "false")} }})");
            return id;
        }

        public string Stmt(BoundStmt s)
        {
            var sp = Span(s.Syntax);
            var id = Fresh("t");
            switch (s)
            {
                case BoundBlock b:
                {
                    var list = List("BStmt", b.Statements.Select(Stmt).ToList());
                    Line($"let {id} = addBlock(ref c, from: BBlock {{ span: {sp}, statements: move {list} }})");
                    break;
                }
                case BoundLocalDecl d:
                {
                    var init = Opt(d.Initializer);
                    Line($"let {id} = addLocalDecl(ref c, from: BLocalDecl {{ span: {sp}, local: {Local(d.Local)}, initializer: {init} }})");
                    break;
                }
                case BoundExprStmt x:
                {
                    var e = Expr(x.Expr);
                    Line($"let {id} = addExprStmt(ref c, from: BExprStmt {{ span: {sp}, expr: {e} }})");
                    break;
                }
                case BoundAssign a:
                {
                    var target = Expr(a.Target);
                    var value = Expr(a.Value);
                    var op = a.Op is { } o ? $", op: .{Lower(o.ToString())}" : "";
                    Line($"let {id} = addAssign(ref c, from: BAssign {{ span: {sp}, target: {target}, value: {value}{op} }})");
                    break;
                }
                case BoundReturn r:
                {
                    var value = Opt(r.Value);
                    Line($"let {id} = addReturn(ref c, from: BReturn {{ span: {sp}, value: {value} }})");
                    break;
                }
                case BoundBreak b:
                    Line($"let {id} = addBreak(ref c, from: BBreak {{ span: {sp}, target: {Target(b.Target)} }})");
                    break;
                case BoundContinue k:
                    Line($"let {id} = addContinue(ref c, from: BContinue {{ span: {sp}, target: {Target(k.Target)} }})");
                    break;
                case BoundIf i:
                {
                    var cond = Expr(i.Condition);
                    var then = Stmt(i.Then);
                    var otherwise = i.Else is null ? "-1" : Stmt(i.Else);
                    Line($"let {id} = addIf(ref c, from: BIf {{ span: {sp}, condition: {cond}, then: {then}, elseBranch: {otherwise}, isNever: {(i.Never ? "true" : "false")} }})");
                    break;
                }
                case BoundWhile w:
                {
                    var target = Target(w.Target);
                    var cond = Expr(w.Condition);
                    var body = Stmt(w.Body);
                    Line($"let {id} = addWhile(ref c, from: BWhile {{ span: {sp}, condition: {cond}, body: {body}, target: {target} }})");
                    break;
                }
                case BoundFor f:
                {
                    var target = Target(f.Target);
                    var variable = Local(f.Variable);
                    var iterable = Expr(f.Iterable);
                    var body = Stmt(f.Body);
                    Line($"let {id} = addFor(ref c, from: BFor {{ span: {sp}, variable: {variable}, iterable: {iterable}, body: {body}, target: {target} }})");
                    break;
                }
                case BoundDefer d:
                {
                    var body = Stmt(d.Body);
                    Line($"let {id} = addDefer(ref c, from: BDefer {{ span: {sp}, body: {body} }})");
                    break;
                }
                case BoundErrdefer d:
                {
                    var error = d.Error is null ? "-1" : Local(d.Error);
                    var body = Stmt(d.Body);
                    Line($"let {id} = addErrdefer(ref c, from: BErrdefer {{ span: {sp}, error: {error}, body: {body} }})");
                    break;
                }
                case BoundPanicDefer d:
                {
                    var panic = d.Panic is null ? "-1" : Local(d.Panic);
                    var body = Stmt(d.Body);
                    Line($"let {id} = addPanicDefer(ref c, from: BPanicDefer {{ span: {sp}, panic: {panic}, body: {body} }})");
                    break;
                }
                case BoundErrorStmt:
                    Line($"let {id} = addErrorStmt(ref c, from: BErrorStmt {{ span: {sp} }})");
                    break;
                default:
                    throw new NotSupportedException(s.GetType().Name);
            }
            return id;
        }

        /// <summary>The function's C# <c>Locals</c>, which the capture check reads.</summary>
        public void Locals(FunctionSymbol fn, string f)
        {
            foreach (var l in fn.Locals) Line($"c.functions[{f}].locals.append({Local(l)})");
        }
    }

    private static string Case(Compilation c, int index, string name)
    {
        var sb = new StringBuilder();
        sb.Append($"void t24mCase{index}(ref Ctx c, for FileId file) {{\n");
        var w = new Writer(sb);
        var fn = Function(c, name);
        var f = w.Function(fn);
        var body = w.Stmt(fn.Body!);
        w.Locals(fn, f);
        sb.Append($"    c.functions[{f}].body = {body}\n");
        sb.Append($"    c.onPanic = {(c.OnPanic == fn ? f : "-1")}\n");
        for (var a = 0; a < Analyses.Length; a++)
        {
            sb.Append("    let start" + a + " = c.diagnostics.length\n");
            sb.Append($"    {Analyses[a].Name}(ref c, for: {f})\n");
            sb.Append($"    print(\"== {Analyses[a].Name}.{name}\")\n");
            sb.Append($"    t24mDiags(ref c, from: start{a})\n");
        }
        sb.Append("}\n\n");
        return sb.ToString();
    }

    // ── The generated main ──────────────────────────────────────────────────────
    private static string MainSource()
    {
        var c = Compiled.Value;
        var cases = new StringBuilder();
        var calls = new StringBuilder();
        lock (c)
        {
            for (var i = 0; i < Cases.Length; i++)
            {
                cases.Append(Case(c, i, Cases[i]));
                calls.Append($"    t24mCase{i}(ref c, for: file)\n");
            }
        }
        return """
            import lexer
            import parser

            const world: string = "WORLD\n"

            void t24mDiags(ref Ctx c, from i64 start) {
                var any = false
                for i in start..c.diagnostics.length {
                    let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                    let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                    print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
                    any = true
                }
                if !any { print("ok") }
            }

            ErrorSetId t24mSet(ref Ctx c, by string name) {
                for k in 0..c.errorSets.length {
                    if c.errorSets[k].name == name { return ErrorSetId(k) }
                }
                panic($"no error set {name}")
            }

            StructId t24mStruct(ref Ctx c, by string name) {
                for k in 0..c.structs.length {
                    if c.structs[k].name == name { return StructId(k) }
                }
                panic($"no struct {name}")
            }

            EnumId t24mEnum(ref Ctx c, by string name) {
                for k in 0..c.enums.length {
                    if c.enums[k].name == name { return EnumId(k) }
                }
                panic($"no enum {name}")
            }

            EnumMemberId t24mEnumMember(ref Ctx c, by string owner, for string name) {
                for k in 0..c.enumMembers.length {
                    if c.enumMembers[k].name == name && c.enums[c.enumMembers[k].owner].name == owner { return EnumMemberId(k) }
                }
                panic($"no enum member {owner}.{name}")
            }

            FieldId t24mField(ref Ctx c, by string owner, for string name) {
                let s = t24mStruct(ref c, by: owner)
                for f in c.structs[s].fields {
                    if c.fields[f].name == name { return f }
                }
                panic($"no field {owner}.{name}")
            }

            FuncId t24mFn(ref Ctx c, by string owner, for string name) {
                for k in 0..c.functions.length {
                    if c.functions[k].name != name || c.functions[k].file == -1 { continue }
                    let o = c.functions[k].owner
                    let ownerName = o != -1 ? c.structs[o].name : ""
                    if ownerName == owner { return FuncId(k) }
                }
                panic($"no function {owner}.{name}")
            }

            CASES
            i32 main(ReadOnlySpan<string> args) {
                var bytes = List<u8>()
                for x in world.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t24-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t24-cases",
                    displayRoot: "tmp/t24-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t24-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                let file = c.modules[c.entryModule].files[0]
            CALLS    return 0
            }
            """.Replace("WORLD", Escape(Source)).Replace("CASES", cases.ToString()).Replace("CALLS", calls.ToString());
    }
}
