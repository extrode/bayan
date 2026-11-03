using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using SourceText = Bayan.Syntax.Text.SourceText;
using TextSpan = Bayan.Syntax.Text.TextSpan;

namespace Bayan.Tests;

/// <summary>
/// T37: <c>selfhost/semantics/region2.byn</c> against the C# <c>RegionAnalysis</c> (RegionAnalysis.cs 418-881).
/// Each case compiles <see cref="Source"/> on both sides, builds one function's analysis state with its commands and
/// calls the ported member on a bound node of that function: on the C# side by reflection on the private class, on the
/// Bayan side through generated code. It then prints the mutations, the loans and the diagnostics the commands added.
/// The Bayan siblings <c>exprRegions</c>, <c>unionBorrow</c>, <c>lent</c> and <c>declareBorrow</c> are T25's stubs (Q684):
/// call arguments are ones whose C# <c>Expr</c> is empty or, like the ToDynRef in <c>call.dyn</c>, is never read by
/// <c>Call</c>, and no case prints a union or a call's result.
/// Every expected line is computed from the C#.
/// </summary>
public sealed class SelfhostSemanticsT37Tests
{
    private const string Dir = "tmp/t37-cases";

    private const string Source = """
        interface IReset { void reset(ref this) }

        const table: i64[3] = [1, 2, 3]

        struct Plain : IReset {
            i64 n

            void reset(ref this) {
                n = 0
            }
        }

        struct Counter {
            i64 n

            void bump(ref this) {
                n += 1
            }
        }

        struct Bag : IReset {
            List<i64> items
            List<i64> extra
            Counter inner
            i64 count

            void reset(ref this) {
                items.clear()
            }

            void grow(ref this) {
                items.append(1)
                add()
            }

            void add(ref this) {
                extra.append(2)
                grow()
            }

            void refill(ref this) {
                grow()
                fill(to: ref items)
                inner.bump()
                whole(for: ref this)
                set()
                this.count = 2
                items[0] = 3
            }

            void set(ref this) {
                count = 1
            }

            void outer(ref this) {
                refill()
                reset()
            }
        }

        void fill(to ref List<i64> xs) {
            xs.append(1)
        }

        void whole(for ref Bag b) {
            b.count = 0
        }

        void two(ref List<i64> a, by ref List<i64> b) {
            a.append(b.length)
        }

        void one(ref List<i64> a, by i64 v) {
            a.append(v)
        }

        void poke(ref List<i64> a, by ref i64 v) {
            v = a.length
        }

        void keep(ref List<i64> a, by List<i64> b) {
            a.append(b.length)
        }

        void clear(dyn ref IReset r) {
            r.reset()
        }

        void spend(Arena a) {
        }

        void fa(Span<i64> sp, from ReadOnlySpan<i64> ro, to (i64, List<i64>) tu, until Plain pl, through Bag bg, at i64? oi, by Arena ar, for dyn ref IReset dr, as StrBuf sb) {
            var xs = List<i64>()
            two(ref xs, by: ref xs)
            one(ref xs, by: xs[0])
            keep(ref xs, by: xs)
            var b = Bag { items: List<i64>(), extra: List<i64>(), inner: Counter { n: 0 }, count: 0 }
            b.grow()
            clear(b)
            let r: dyn ref IReset = b
            clear(r)
            r.reset()
            sp[0] = 1
            xs[0] = 2
            let q = 1
            let h = 0
            var p = Plain { n: 1 }
            let r3: dyn ref IReset = p
            let r2: dyn ref IReset = r
            spend(ar)
            switch bg {
                case { items: let its }:
                    print("x")
            }
            var m = Map<string, i64>()
            m["a"] = 1
            let ts = table
            poke(ref xs, by: ref xs[0])
        }

        void fc(List<i64> xs) {
            var h = 0
            for x in xs {
                h = x
            }
            print($"{h}")
            var k = 0
            while k < 3 {
                k += 1
            }
            print($"{k}")
        }

        union error Bad {
            oops
        }

        union Slot {
            empty
            full(List<i64> items)
        }

        union Mark {
            none
            some(i64 n)
        }

        union Cell {
            none
            some(i64[2] a)
        }

        struct unique Token {
            i64 n
        }

        void ft<T>(string s, from Untrusted<string> u, to Untrusted<List<i64>> ul, until Result<string, Bad> rs, through Result<List<i64>, Bad> rl, at i64[2] ai, by List<i64>[2] al, as T tv) {
        }

        void fu(Slot sl, from Mark mk, to Token tk, until Bag bg, through Cell ce) {
            switch sl {
                case .full w:
                    print("w")
                default:
                    print("d")
            }
            switch bg {
                case { items: let its } whole:
                    print("x")
            }
        }
        """;

    /// <summary>
    /// A case: the function its commands run in, and the commands, separated by <c>;</c>. A selector is a statement
    /// index of the function's body (its expression, initializer, assignment target or switch value), with <c>.aK</c>
    /// for a call's argument K and <c>.r</c> for its receiver. A borrow is <c>-</c> or a comma list of regions
    /// <c>depth:name</c> (<c>_</c> for a space), owners <c>@local</c> and owners lent at a position <c>@local=at</c>.
    /// A position <c>^text</c> is where <c>text</c> (<c>_</c> for a space) first occurs in <see cref="Source"/>.
    /// </summary>
    private static readonly (string Name, string Fn, string Commands)[] Cases =
    [
        ("types", "fa", "types sp ro tu pl bg oi ar dr sb xs b r p h q m"),
        ("types.more", "ft", "types s u ul rs rl ai al tv"),
        ("types.union", "fu", "types sl mk tk ce"),
        ("root.index", "fa", "root 11"),
        ("root.span", "fa", "root 10"),
        ("root.arg", "fa", "root 2.a1"),
        ("root.map", "fa", "root 20"),
        ("root.call", "fa", "root 1"),
        ("root.this", "refill", "root 5"),
        ("root.field", "refill", "root 6"),
        ("root.recv", "refill", "root 2.r"),
        ("call.ref", "fa", "call 1"),
        ("call.quiet", "fa", "quiet;call 1"),
        ("call.elem", "fa", "call 2"),
        ("call.value", "fa", "call 3"),
        ("call.refelem", "fa", "call 22"),
        ("call.recv", "fa", "call 5"),
        ("call.dyn", "fa", "call 6"),
        ("call.dynlocal", "fa", "call 8"),
        ("call.dynrecv", "fa", "call 9"),
        ("call.dynquiet", "fa", "quiet;call 9"),
        ("call.this", "refill", "call 0"),
        ("call.thisquiet", "refill", "quiet;call 0"),
        ("call.cached", "refill", "call 0;call 0"),
        ("call.field", "refill", "call 1"),
        ("call.inner", "refill", "call 2"),
        ("call.whole", "refill", "call 3"),
        ("call.set", "refill", "call 4"),
        ("call.outer", "outer", "call 0"),
        ("call.outer2", "outer", "call 1;call 0"),
        ("lend.static", "fa", "lend 21 2:`a`,@xs=7 9"),
        ("lend.staticempty", "fa", "lend 21 - 9"),
        ("alloc.flat", "fa", "alloc 17.a0"),
        ("alloc.deep", "fa", "depth ar 2;alloc 17.a0"),
        ("pattern", "fa", "pattern 18 1:`bg`,@bg"),
        ("pattern.empty", "fa", "pattern 18 -"),
        ("pattern.union", "fu", "pattern 0 1:`sl`,@sl"),
        ("pattern.property", "fu", "pattern 1 1:`bg`,@bg"),
        ("store.loan", "fa", "depth h 2;store h 1:`x`,@xs=50 0 100"),
        ("store.lendlate", "fa", "store h @xs=150 0 100"),
        ("store.lendearly", "fa", "store h @xs=5 10 30"),
        ("store.self", "fa", "store xs @xs ^two 3"),
        ("store.outlives", "fa", "depth h 1;store h 3:`y`,2:`z` ^let_h 5"),
        ("store.order", "fa", "store h 3:`c`,3:`a`,2:`b` ^let_h 5"),
        ("store.repeat", "fa", "store h 2:`a` ^let_h 5;store q 2:`b` ^let_h 5"),
        ("store.rest", "fa", "rest 1 `ro` ro;store h 1:`ro`,1:`zz` ^let_h 5"),
        ("store.quiet", "fa", "quiet;store h 2:`a`,@xs ^let_h 5"),
        ("store.empty", "fa", "store h - ^let_h 5"),
        ("into.local", "fa", "into 11 2:`a`,@b ^xs[0] 8"),
        ("into.span", "fa", "local sp 3:`s`,2:`t`;into 10 3:`u` ^sp[0] 8"),
        ("into.spanlow", "fa", "local sp 1:`s`;into 10 2:`u`,1:`v` ^sp[0] 8"),
        ("into.spannone", "fa", "into 10 1:`u` ^sp[0] 8"),
        ("into.map", "fa", "into 20 1:`u`,@q ^m[ 8"),
        ("into.this", "refill", "into 5 1:`u` ^this.count 10"),
        ("into.quiet", "refill", "quiet;into 5 1:`u` ^this.count 10"),
        ("outlives", "fa", "outlives 2:`a`,1:`b` 1 holder 0 4"),
        ("outlives.none", "fa", "outlives 1:`a` 1 holder 0 4"),
        ("ret", "fa", "ret 0:`a`,1:`b` 7 3"),
        ("ret.caller", "fa", "ret 0:`a` 7 3"),
        ("ret.rest", "fa", "rest 1 `ro` ro;ret 1:`ro` 7 3"),
        ("ret.quiet", "fa", "quiet;ret 1:`b` 7 3"),
        ("mutate.local", "fa", "mutate 11"),
        ("mutate.moved", "fa", "mutate 11 moved"),
        ("mutate.field", "refill", "mutate 6"),
        ("mutate.this", "refill", "mutate 5;mutate 5 moved"),
        ("mutate.none", "fa", "mutate 10"),
        ("mutate.quiet", "fa", "quiet;mutate 11"),
        ("owners.local", "fa", "owners 9.r @b"),
        ("owners.lent", "fa", "referent r true;owners 9.r @xs=3"),
        ("owners.skip", "fa", "referent r false;owners 9.r @b"),
        ("owners.other", "fa", "referent r false;owners 7 @b"),
        ("owners.quiet", "fa", "quiet;owners 9.r @b"),
        ("refer.conv", "fa", "refer r 7"),
        ("refer.plain", "fa", "refer r3 15"),
        ("refer.local", "fa", "referent r false;refer r2 16"),
        ("refer.unknown", "fa", "refer r2 16"),
        ("refer.other", "fa", "refer r3 15;refer r3 1"),
        ("refer.keep", "fa", "refer r 7;refer r 15"),
        ("refer.value", "fa", "refer h 13"),
        ("check.used", "fc", "loan xs h ^for_x_in 0;mutation xs ^print($ 5 - -;check"),
        ("check.moved", "fc", "loan xs h ^for_x_in 0;mutation xs ^print($ 5 moved -;check"),
        ("check.after", "fc", "loan xs h ^for_x_in 0;mutation xs ^var_k 3 - -;check"),
        ("check.declared", "fc", "declared h ^h_=_x;loan xs h 0 0;mutation xs ^for_x_in 3 - -;check"),
        ("check.inloop", "fc", "declared x ^x_in;loan xs x 0 0;mutation xs ^for_x_in 3 - -;check"),
        ("check.loopext", "fc", "loan xs x 0 0;mutation xs ^h_=_x 5 - -;check"),
        ("check.loopnoext", "fc", "declared x ^x_in;loan xs x 0 0;mutation xs ^h_=_x 5 - -;check"),
        ("check.edge", "fc", "loan xs h 0 0;mutation xs ^{h} 1 - -;check"),
        ("check.late", "fc", "loan xs h ^print($ 0;mutation xs ^for_x_in 3 - -;check"),
        ("check.loop", "fc", "loan xs - ^for_x_in ^var_k;mutation xs ^h_=_x 1 - -;check"),
        ("check.loopmoved", "fc", "loan xs - ^for_x_in ^var_k;mutation xs ^h_=_x 1 moved -;check"),
        ("check.loopdone", "fc", "loan xs - ^for_x_in ^h_=_x;mutation xs ^h_=_x 5 - -;check"),
        ("check.through", "fc", "declared x ^for_x_in;loan xs - ^for_x_in ^var_k;mutation xs ^h_=_x 1 - x;check"),
        ("check.throughother", "fc", "declared x 1;loan xs - ^for_x_in ^var_k;mutation xs ^h_=_x 1 - x;check"),
        ("check.once", "fc", "loan xs h ^for_x_in 0;loan xs k ^for_x_in 0;mutation xs ^print($ 5 - -;check"),
        ("check.other", "fc", "loan k h ^for_x_in 0;mutation xs ^print($ 5 - -;check"),
        ("check.noloans", "fc", "mutation xs ^print($ 5 - -;check"),
        ("check.unused", "fc", "loan xs xs ^for_x_in 0;mutation xs ^print($ 5 - -;check"),
        ("check.quiet", "fc", "quiet;loan xs h ^for_x_in 0;mutation xs ^print($ 5 - -;check"),
    ];

    // ── The C# oracle ────────────────────────────────────────────────────────────

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly System.Type RA =
        typeof(Compilation).Assembly.GetType("Bayan.Semantics.Binding.RegionAnalysis") ?? throw new InvalidOperationException("RegionAnalysis");

    private static System.Type Nested(string name) => RA.GetNestedType(name, Any) ?? throw new InvalidOperationException(name);

    private static object? Get(object o, string member) =>
        o.GetType().GetField(member, Any) is { } f ? f.GetValue(o) : o.GetType().GetProperty(member, Any)!.GetValue(o);

    private static object? Invoke(object? target, string method, params object?[] args)
    {
        var m = RA.GetMethods(Any).Single(m => m.Name == method && m.GetParameters().Length >= args.Length && m.GetParameters().Skip(args.Length).All(p => p.IsOptional));
        return m.Invoke(target, [.. args, .. m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)]);
    }

    private static string Diag(Diagnostic d)
    {
        var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
        return $"{d.Code} {line}:{col} {d.Message} |{d.Label}|{d.Help}|{(d.Fix is null ? "" : $"fix {d.Fix.Span.Start}..{d.Fix.Span.End} {d.Fix.Replacement}")}";
    }

    private static Compilation CompileLibrary(string text) =>
        Compilation.Create([SourceText.FromString(Path.Combine(Dir, "main.byn"), text + "\n")], packageRoot: Dir, options: new CompilationOptions(Library: true));

    private static FunctionSymbol Function(Compilation c, string name) =>
        c.Modules.SelectMany(m => m.AllFunctions).First(f => f.Name == name && f.Body is not null);

    private static LocalSymbol Local(FunctionSymbol fn, string name) =>
        fn.Parameters.Cast<LocalSymbol>().Concat(BoundTree.Descendants(fn.Body!).SelectMany(n => n switch
        {
            BoundLocalDecl d => new[] { d.Local },
            BoundBindPattern { Local: { } l } => [l],
            BoundFor f => [f.Variable],
            _ => [],
        })).First(l => l.Name == name);

    private static string Lower(bool b) => b ? "true" : "false";

    /// <summary>A position token: a number, or <c>^text</c>.</summary>
    private static int Pos(string token) => token.StartsWith('^') ? Source.IndexOf(token[1..].Replace('_', ' '), StringComparison.Ordinal) : int.Parse(token);

    private static BoundExpr Select(FunctionSymbol fn, string selector)
    {
        var parts = selector.Split('.');
        BoundExpr e = fn.Body!.Statements[int.Parse(parts[0])] switch
        {
            BoundExprStmt x => x.Expr,
            BoundLocalDecl d => d.Initializer!,
            BoundAssign a => a.Target,
            BoundReturn r => r.Value!,
            BoundSwitch s => s.Value,
            var s => throw new InvalidOperationException(s.GetType().Name),
        };
        if (parts.Length == 1) return e;
        var call = (BoundCall)e;
        return parts[1] == "r" ? call.Receiver! : call.Arguments[int.Parse(parts[1][1..])];
    }

    private static object NewRegion(int depth, string name) => Activator.CreateInstance(Nested("Region"), depth, name, int.MinValue)!;

    private static object Borrow(FunctionSymbol fn, string spec)
    {
        object set = typeof(ImmutableHashSet<>).MakeGenericType(Nested("Region")).GetField("Empty")!.GetValue(null)!;
        var owners = ImmutableHashSet<LocalSymbol>.Empty;
        ImmutableDictionary<LocalSymbol, int>? lends = null;
        if (spec != "-")
            foreach (var item in spec.Split(','))
            {
                if (item.StartsWith('@'))
                {
                    var eq = item.IndexOf('=');
                    var local = Local(fn, eq < 0 ? item[1..] : item[1..eq]);
                    owners = owners.Add(local);
                    if (eq >= 0) lends = (lends ?? ImmutableDictionary<LocalSymbol, int>.Empty).Add(local, Pos(item[(eq + 1)..]));
                    continue;
                }
                var colon = item.IndexOf(':');
                set = set.GetType().GetMethod("Add")!.Invoke(set, [NewRegion(int.Parse(item[..colon]), item[(colon + 1)..].Replace('_', ' '))])!;
            }
        return Activator.CreateInstance(Nested("Borrow"), set, owners, lends)!;
    }

    private static string BorrowLine(object b)
    {
        var regions = ((IEnumerable)Get(b, "Regions")!).Cast<object>()
            .OrderByDescending(r => (int)Get(r, "Depth")!).ThenBy(r => (string)Get(r, "Name")!, StringComparer.Ordinal)
            .Select(r => $"{Get(r, "Depth")}:{Get(r, "Name")}");
        var lends = (ImmutableDictionary<LocalSymbol, int>?)Get(b, "Lends");
        var owners = ((IEnumerable)Get(b, "Owners")!).Cast<LocalSymbol>()
            .Select(l => lends is not null && lends.TryGetValue(l, out var at) ? $"{l.Name}={at}" : l.Name).Order(StringComparer.Ordinal);
        return $"borrow [{string.Join(",", regions)}] [{string.Join(",", owners)}]";
    }

    private static List<string> Expected(string name)
    {
        Directory.CreateDirectory(Dir);
        var (_, fnName, commands) = Cases.First(k => k.Name == name);
        var c = CompileLibrary(Source);
        // Compilation.Create already ran the analysis; start the per-method cache afresh, as the Bayan side does.
        ((IDictionary)typeof(Compilation).GetProperty("ChangedFields", Any)!.GetValue(c)!).Clear();
        var fn = Function(c, fnName);
        var ra = Activator.CreateInstance(RA, Any, null, [c, fn, false], null)!;
        RA.GetField("_reporting", Any)!.SetValue(ra, true);
        var mark = c.Diagnostics.Items.Count;
        var lines = new List<string>();
        foreach (var command in commands.Split(';'))
        {
            var w = command.Split(' ');
            switch (w[0])
            {
                case "quiet":
                    RA.GetField("_reporting", Any)!.SetValue(ra, false);
                    break;
                case "depth":
                    ((Dictionary<LocalSymbol, int>)RA.GetField("_depthOf", Any)!.GetValue(ra)!)[Local(fn, w[1])] = Pos(w[2]);
                    break;
                case "declared":
                    ((Dictionary<LocalSymbol, int>)RA.GetField("_declaredAt", Any)!.GetValue(ra)!)[Local(fn, w[1])] = Pos(w[2]);
                    break;
                case "referent":
                    ((Dictionary<LocalSymbol, bool>)RA.GetField("_referentMayBorrow", Any)!.GetValue(ra)!)[Local(fn, w[1])] = w[2] == "true";
                    break;
                case "local":
                    ((IDictionary)RA.GetField("_locals", Any)!.GetValue(ra)!)[Local(fn, w[1])] = Borrow(fn, w[2]);
                    break;
                case "rest":
                    ((IDictionary)RA.GetField("_restTails", Any)!.GetValue(ra)!)[NewRegion(int.Parse(w[1]), w[2])] = w[3];
                    break;
                case "loan":
                    ((IList)RA.GetField("_loans", Any)!.GetValue(ra)!).Add(Activator.CreateInstance(Nested("Loan"),
                        Local(fn, w[1]), w[2] == "-" ? null : Local(fn, w[2]), Pos(w[3]), Pos(w[4])));
                    break;
                case "mutation":
                    ((IList)RA.GetField("_mutations", Any)!.GetValue(ra)!).Add(Activator.CreateInstance(Nested("Mutation"),
                        Local(fn, w[1]), new TextSpan(Pos(w[2]), int.Parse(w[3])), w[4] == "moved", w[5] == "-" ? null : Local(fn, w[5])));
                    break;
                case "types":
                    foreach (var n in w.Skip(1))
                    {
                        var t = Local(fn, n).Type;
                        lines.Add($"type {n} {Lower((bool)Invoke(null, "MayBorrow", t, new HashSet<TypeSymbol>())!)} {Lower((bool)Invoke(null, "MayLend", t, new HashSet<TypeSymbol>())!)} {Lower((bool)Invoke(null, "ContainsList", t, new HashSet<TypeSymbol>())!)}");
                    }
                    break;
                case "root":
                {
                    var e = Select(fn, w[1]);
                    var root = (LocalSymbol?)Invoke(null, "RootLocal", e);
                    var store = (LocalSymbol?)Invoke(null, "StoreRoot", e);
                    var field = (LocalSymbol?)Invoke(ra, "ThisField", e);
                    lines.Add($"root {root?.Name ?? "-"} {store?.Name ?? "-"} {field?.Name ?? "-"} {Lower((bool)Invoke(null, "IsThisRooted", e)!)}");
                    break;
                }
                case "call":
                    Invoke(ra, "Call", Select(fn, w[1]));
                    break;
                case "lend":
                    lines.Add(BorrowLine(Invoke(ra, "Lend", Select(fn, w[1]), Borrow(fn, w[2]), Pos(w[3]))!));
                    break;
                case "alloc":
                    lines.Add(BorrowLine(Invoke(ra, "AllocatedFrom", Select(fn, w[1]))!));
                    break;
                case "pattern":
                    Invoke(ra, "BindPattern", ((BoundSwitch)fn.Body!.Statements[int.Parse(w[1])]).Sections[0].Pattern, Borrow(fn, w[2]));
                    break;
                case "store":
                    Invoke(ra, "Store", Local(fn, w[1]), Borrow(fn, w[2]), new TextSpan(Pos(w[3]), int.Parse(w[4])));
                    break;
                case "into":
                    Invoke(ra, "StoreInto", Select(fn, w[1]), Borrow(fn, w[2]), new TextSpan(Pos(w[3]), int.Parse(w[4])));
                    break;
                case "outlives":
                    Invoke(ra, "CheckOutlives", Borrow(fn, w[1]), int.Parse(w[2]), w[3], new TextSpan(Pos(w[4]), int.Parse(w[5])));
                    break;
                case "ret":
                    Invoke(ra, "CheckReturn", Borrow(fn, w[1]), new TextSpan(Pos(w[2]), int.Parse(w[3])));
                    break;
                case "mutate":
                {
                    var e = Select(fn, w[1]);
                    Invoke(ra, "Mutate", e, e.Syntax.Span, w.Length > 2);
                    break;
                }
                case "owners":
                {
                    var e = Select(fn, w[1]);
                    Invoke(ra, "MutateOwners", e, Borrow(fn, w[2]), e.Syntax.Span);
                    break;
                }
                case "refer":
                {
                    var local = Local(fn, w[1]);
                    Invoke(ra, "Refer", local, Select(fn, w[2]));
                    var map = (Dictionary<LocalSymbol, bool>)RA.GetField("_referentMayBorrow", Any)!.GetValue(ra)!;
                    lines.Add($"referent {w[1]} {(map.TryGetValue(local, out var v) ? Lower(v) : "-")}");
                    break;
                }
                case "check":
                    Invoke(ra, "CheckMutations");
                    break;
                default:
                    throw new InvalidOperationException(w[0]);
            }
        }
        var items = c.Diagnostics.Items;
        lines.Add($"counts {((IList)RA.GetField("_mutations", Any)!.GetValue(ra)!).Count} {((IList)RA.GetField("_loans", Any)!.GetValue(ra)!).Count} {items.Count - mark}");
        foreach (var m in (IList)RA.GetField("_mutations", Any)!.GetValue(ra)!)
        {
            var span = (TextSpan)Get(m, "Span")!;
            lines.Add($"mut {((LocalSymbol)Get(m, "Target")!).Name} {span.Start}..{span.End} {Lower((bool)Get(m, "Moved")!)} {((LocalSymbol?)Get(m, "Through"))?.Name ?? "-"}");
        }
        foreach (var l in (IList)RA.GetField("_loans", Any)!.GetValue(ra)!)
            lines.Add($"loan {((LocalSymbol)Get(l, "Owner")!).Name} {((LocalSymbol?)Get(l, "Holder"))?.Name ?? "-"} {Get(l, "Created")} {Get(l, "LoopEnd")}");
        for (var i = mark; i < items.Count; i++) lines.AddRange(Diag(items[i]).Split('\n'));
        return lines;
    }

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() =>
        SelfhostSemanticsHarness.RunSections(MainSource(), [.. Cases.Select(k => k.Name)]));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var k in Cases) data.Add(k.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CaseMatches(string name) =>
        SelfhostSemanticsHarness.AssertSection(Sections.Value, name, Expected(name));

    // ── The Bayan side ───────────────────────────────────────────────────────────

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static string Str(string s) => $"\"{Escape(s)}\"";

    private static string Sp(string start, string length) => $"TextSpan {{ start: {Pos(start)}, length: {int.Parse(length)} }}";

    private static string Sel(string selector)
    {
        var parts = selector.Split('.');
        var e = $"tcStmt(in c, for: f, by: {int.Parse(parts[0])})";
        if (parts.Length == 1) return e;
        return parts[1] == "r" ? $"c.bCalls[boundIndex({e})].receiver" : $"c.bCalls[boundIndex({e})].arguments[{int.Parse(parts[1][1..])}]";
    }

    private static string L(string name) => $"tcLocal(in c, for: f, by: {Str(name)})";

    /// <summary>Appends the statements that build a borrow and returns its name.</summary>
    private static string BorrowCode(System.Text.StringBuilder sb, ref int n, string spec)
    {
        var v = $"b{n++}";
        sb.AppendLine($"    var {v}r = List<Region>()");
        sb.AppendLine($"    var {v}o = List<LocalId>()");
        sb.AppendLine($"    var {v}l: Map<LocalId, i64> = []");
        var lends = false;
        if (spec != "-")
            foreach (var item in spec.Split(','))
            {
                if (item.StartsWith('@'))
                {
                    var eq = item.IndexOf('=');
                    var local = L(eq < 0 ? item[1..] : item[1..eq]);
                    sb.AppendLine($"    {v}o.append({local})");
                    if (eq >= 0)
                    {
                        sb.AppendLine($"    {v}l[{local}] = {Pos(item[(eq + 1)..])}");
                        lends = true;
                    }
                    continue;
                }
                var colon = item.IndexOf(':');
                sb.AppendLine($"    {v}r.append(Region {{ depth: {int.Parse(item[..colon])}, name: {Str(item[(colon + 1)..].Replace('_', ' '))} }})");
            }
        sb.AppendLine(lends
            ? $"    let {v} = Borrow {{ regions: move {v}r, owners: move {v}o, lends: move {v}l }}"
            : $"    let {v} = Borrow {{ regions: move {v}r, owners: move {v}o }}");
        return v;
    }

    private static string CaseFunction(int k)
    {
        var (_, fnName, commands) = Cases[k];
        var sb = new System.Text.StringBuilder();
        var n = 0;
        sb.AppendLine($"void tcCase{k}(ref Ctx c) {{");
        sb.AppendLine($"    let f = tcFunc(in c, by: {Str(fnName)})");
        sb.AppendLine("    var r = Regions { fn: f, source: c.functions[f].file, reporting: true }");
        sb.AppendLine("    let start = c.diagnostics.length");
        foreach (var command in commands.Split(';'))
        {
            var w = command.Split(' ');
            switch (w[0])
            {
                case "quiet":
                    sb.AppendLine("    r.reporting = false");
                    break;
                case "depth":
                    sb.AppendLine($"    r.depthOf[{L(w[1])}] = {Pos(w[2])}");
                    break;
                case "declared":
                    sb.AppendLine($"    r.declaredAt[{L(w[1])}] = {Pos(w[2])}");
                    break;
                case "referent":
                    sb.AppendLine($"    r.referentMayBorrow[{L(w[1])}] = {w[2]}");
                    break;
                case "local":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    r.locals[{L(w[1])}] = move {b}");
                    break;
                }
                case "rest":
                    sb.AppendLine($"    r.restTails.append(RestTail {{ region: Region {{ depth: {int.Parse(w[1])}, name: {Str(w[2])} }}, tail: {Str(w[3])} }})");
                    break;
                case "loan":
                    sb.AppendLine($"    r.loans.append(Loan {{ owner: {L(w[1])}, holder: {(w[2] == "-" ? "-1" : L(w[2]))}, created: {Pos(w[3])}, loopEnd: {Pos(w[4])} }})");
                    break;
                case "mutation":
                    sb.AppendLine($"    r.mutations.append(Mutation {{ target: {L(w[1])}, span: {Sp(w[2], w[3])}, moved: {Lower(w[4] == "moved")}, through: {(w[5] == "-" ? "-1" : L(w[5]))} }})");
                    break;
                case "types":
                    foreach (var name in w.Skip(1)) sb.AppendLine($"    tcTypes(ref c, for: {L(name)})");
                    break;
                case "root":
                    sb.AppendLine($"    tcRoot(ref r, through: ref c, for: {Sel(w[1])})");
                    break;
                case "call":
                    sb.AppendLine($"    let call{n++} = callBorrow(ref r, through: ref c, for: {Sel(w[1])})");
                    break;
                case "lend":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    let a{n} = lendBorrow(ref r, through: ref c, for: {Sel(w[1])}, by: in {b}, at: {Pos(w[3])})");
                    sb.AppendLine($"    tcBorrow(in c, by: in a{n++})");
                    break;
                }
                case "alloc":
                    sb.AppendLine($"    let a{n} = allocatedFrom(ref r, through: ref c, for: {Sel(w[1])})");
                    sb.AppendLine($"    tcBorrow(in c, by: in a{n++})");
                    break;
                case "pattern":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    t37BindPattern(ref r, through: ref c, for: tcPattern(in c, for: f, by: {int.Parse(w[1])}), by: in {b})");
                    break;
                }
                case "store":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    storeBorrow(ref r, through: ref c, for: {L(w[1])}, by: in {b}, at: {Sp(w[3], w[4])})");
                    break;
                }
                case "into":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    storeIntoBorrow(ref r, through: ref c, for: {Sel(w[1])}, by: in {b}, at: {Sp(w[3], w[4])})");
                    break;
                }
                case "outlives":
                {
                    var b = BorrowCode(sb, ref n, w[1]);
                    sb.AppendLine($"    checkOutlives(ref r, through: ref c, for: in {b}, by: {int.Parse(w[2])}, as: {Str(w[3])}, at: {Sp(w[4], w[5])})");
                    break;
                }
                case "ret":
                {
                    var b = BorrowCode(sb, ref n, w[1]);
                    sb.AppendLine($"    checkReturn(ref r, through: ref c, for: in {b}, at: {Sp(w[2], w[3])})");
                    break;
                }
                case "mutate":
                    sb.AppendLine($"    let e{n} = {Sel(w[1])}");
                    sb.AppendLine($"    mutateBorrow(ref r, through: ref c, for: e{n}, at: nodeSpan(in c, for: e{n}), moved: {Lower(w.Length > 2)})");
                    n++;
                    break;
                case "owners":
                {
                    var b = BorrowCode(sb, ref n, w[2]);
                    sb.AppendLine($"    let e{n} = {Sel(w[1])}");
                    sb.AppendLine($"    mutateOwners(ref r, through: ref c, for: e{n}, by: in {b}, at: nodeSpan(in c, for: e{n}))");
                    n++;
                    break;
                }
                case "refer":
                    sb.AppendLine($"    let l{n} = {L(w[1])}");
                    sb.AppendLine($"    referBorrow(ref r, through: ref c, for: l{n}, by: {Sel(w[2])})");
                    sb.AppendLine($"    tcReferent(in r, for: l{n}, by: {Str(w[1])})");
                    n++;
                    break;
                case "check":
                    sb.AppendLine("    checkMutations(ref r, through: ref c)");
                    break;
                default:
                    throw new InvalidOperationException(w[0]);
            }
        }
        sb.AppendLine("    tcDump(in r, through: in c, from: start)");
        sb.AppendLine("    tcDiags(in c, from: start)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string CaseFunctions()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("void tcCase(ref Ctx c, by string name) {");
        for (var k = 0; k < Cases.Length; k++) sb.AppendLine($"    if name == {Str(Cases[k].Name)} {{ tcCase{k}(ref c) }}");
        sb.AppendLine("}");
        for (var k = 0; k < Cases.Length; k++) sb.Append(CaseFunction(k));
        return sb.ToString();
    }

    // ── The generated main ──────────────────────────────────────────────────────

    private static string MainSource() => """
        import lexer
        import parser

        const source: string = "SRC\n"

        void tcDiags(in Ctx c, from i64 start) {
            for i in start..c.diagnostics.length {
                let pos = linePosition(c.files[c.diagnostics[i].file].text.span, at: c.diagnostics[i].span.start)
                let fixText = c.diagnostics[i].hasFix ? $"fix {c.diagnostics[i].fixSpan.start}..{c.diagnostics[i].fixSpan.end} {c.diagnostics[i].fixReplacement}" : ""
                print($"{c.diagnostics[i].code} {pos.line}:{pos.column} {c.diagnostics[i].message} |{c.diagnostics[i].label}|{c.diagnostics[i].help}|{fixText}")
            }
        }

        FuncId tcFunc(in Ctx c, by string name) {
            for f in 0..c.functions.length {
                if c.functions[f].name == name && c.functions[f].body != -1 { return f }
            }
            return -1
        }

        BExpr tcStmt(in Ctx c, for FuncId f, by i64 i) {
            let s = c.bBlocks[boundIndex(c.functions[f].body)].statements[i]
            let k = boundKind(s)
            let x = boundIndex(s)
            if k == .exprStmt { return c.bExprStmts[x].expr }
            if k == .localDecl { return c.bLocalDecls[x].initializer }
            if k == .assign { return c.bAssigns[x].target }
            if k == .returnStmt { return c.bReturns[x].value }
            if k == .switchStmt { return c.bSwitches[x].value }
            return -1
        }

        BPattern tcPattern(in Ctx c, for FuncId f, by i64 i) {
            let s = c.bBlocks[boundIndex(c.functions[f].body)].statements[i]
            return c.bSwitches[boundIndex(s)].sections[0].pattern
        }

        /// A parameter, or a local the body declares, by name.
        LocalId tcLocal(in Ctx c, for FuncId f, by string name) {
            for p in c.functions[f].parameters {
                if c.locals[p].name == name { return p }
            }
            let nodes = descendants(in c, for: c.functions[f].body)
            for node in nodes {
                var l: LocalId = -1
                let k = boundKind(node)
                if k == .localDecl { l = c.bLocalDecls[boundIndex(node)].local }
                if k == .bindPattern { l = c.bBindPatterns[boundIndex(node)].local }
                if k == .forStmt { l = c.bFors[boundIndex(node)].variable }
                if l != -1 && c.locals[l].name == name { return l }
            }
            return -1
        }

        string tcName(in Ctx c, for LocalId l) => l == -1 ? "-" : c.locals[l].name

        void tcTypes(ref Ctx c, for LocalId l) {
            let t = c.locals[l].type
            let name = c.locals[l].name
            var s1 = List<Type>()
            var s2 = List<Type>()
            var s3 = List<Type>()
            let b = mayBorrow(ref c, for: t, by: ref s1)
            let m = mayLend(ref c, for: t, by: ref s2)
            let k = containsList(ref c, for: t, by: ref s3)
            print($"type {name} {b} {m} {k}")
        }

        void tcRoot(ref Regions r, through ref Ctx c, for BExpr e) {
            let root = rootLocal(ref c, for: e)
            let store = storeRoot(in c, for: e)
            let field = thisField(ref r, through: ref c, for: e)
            let rooted = isThisRooted(in c, for: e)
            print($"root {tcName(in c, for: root)} {tcName(in c, for: store)} {tcName(in c, for: field)} {rooted}")
        }

        void tcBorrow(in Ctx c, by in Borrow b) {
            let ordered = t37Ordered(in b.regions)
            var regions = ""
            for x in ordered {
                regions = regions.length > 0 ? $"{regions},{x.depth}:{x.name}" : $"{x.depth}:{x.name}"
            }
            var owners = List<string>()
            for o in b.owners {
                var at = -1
                if (b.lends is { } lends) {
                    if lends.contains(o) { at = lends[o] }
                }
                owners.append(at == -1 ? c.locals[o].name : $"{c.locals[o].name}={at}")
            }
            owners.sort()
            var joined = ""
            for o in owners {
                joined = joined.length > 0 ? $"{joined},{o}" : o
            }
            print($"borrow [{regions}] [{joined}]")
        }

        void tcReferent(in Regions r, for LocalId l, by string name) {
            if r.referentMayBorrow.contains(l) {
                print($"referent {name} {r.referentMayBorrow[l]}")
            } else {
                print($"referent {name} -")
            }
        }

        void tcDump(in Regions r, through in Ctx c, from i64 start) {
            print($"counts {r.mutations.length} {r.loans.length} {c.diagnostics.length - start}")
            for m in r.mutations {
                print($"mut {c.locals[m.target].name} {m.span.start}..{m.span.end} {m.moved} {tcName(in c, for: m.through)}")
            }
            for l in r.loans {
                print($"loan {c.locals[l.owner].name} {tcName(in c, for: l.holder)} {l.created} {l.loopEnd}")
            }
        }

        CASES

        i32 main(ReadOnlySpan<string> args) {
            for i in 0..args.length {
                var bytes = List<u8>()
                for x in source.bytes { bytes.append(x) }
                var sources = List<lexer.SourceText>()
                sources.append(lexer.SourceText.create("tmp/t37-cases/main.byn", from: move bytes))
                var c = Ctx {
                    files: List<SourceFile>(),
                    modules: List<Mod>(),
                    diagnostics: List<Diag>(),
                    bags: List<DiagBag>(),
                    library: true,
                    packageRoot: "tmp/t37-cases",
                    displayRoot: "tmp/t37-cases",
                }
                c.entryModule = loadModule(ref c, for: "main", at: "tmp/t37-cases", from: move sources, by: -1)
                buildLexicon(ref c)
                declare(ref c)
                bindAll(ref c)
                // bindAll ran the region analysis (T25); start the per-method cache afresh, as the C# side does (Q738).
                c.changedFields.clear()
                print($"== {args[i]}")
                tcCase(ref c, by: args[i])
            }
            return 0
        }
        """.Replace("CASES", CaseFunctions()).Replace("SRC", Escape(Source));
}
