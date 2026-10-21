using System.Text;

namespace Bayan.Tests;

/// <summary>
/// Seeded generator of Bayan programs over <c>unique</c> values (spec 003 R9). The same seed and size
/// always give the same text: the PRNG is splitmix64 and output order never depends on a hash table.
/// Statements are drawn from an abstract ownership state per variable (type, <c>let</c>/<c>var</c>,
/// Owned or Moved), so every step is legal except the probe, which a program carries with p = 0.1.
/// <code>
/// program    := prelude helpers fn{size} main          ; main calls every fn
/// helpers    := consume(move List&lt;i64&gt;) peek(List&lt;i64&gt;) bump(ref List&lt;i64&gt;)
///               fresh(i64) -> List&lt;i64&gt; pass(move List&lt;i64&gt;) -> List&lt;i64&gt; step() fails E
/// fn         := "void g&lt;k&gt;() {" "using let arena = Arena();" "var t = k;" stmt{size} "}"
/// stmt       := decl | move | copy | take | call | borrow | loop | branch | switchMove | reassign
///             | read | return
/// decl       := ("let"|"var") x "=" (fresh(n) | Ledger { items = fresh(n) } | Slot.full(fresh(n)))
///             | "List&lt;i64&gt;? x = fresh(n);" | "var x = List&lt;List&lt;i64&gt;&gt;(); x.add(fresh(n));"
/// move       := "var y = move x;"                 if x Owned and not a let from outside the loop
/// copy       := "let y = x.copy(to: arena);"      if x Owned
/// take       := "let y = p.take();"               p in {x, x.items, x[0], o} with x var and Owned
/// call       := consume(move x) | "var y = pass(move x);" | peek(x) | bump(ref x)   (bump: x var)
/// borrow     := "{ let v = x.asSpan(); print($"{v.length}"); }" | for (let e in x) over a nested list
/// loop       := for/while; the body moves an outer x only if x is var, and reassigns it at the end
/// branch     := if/else on t; join = Moved if moved on either side | "if (s is .full(let items))" borrow
/// switchMove := "switch (move s) {" ["case .full(let b) when b.length &gt; n: consume(move b);"]
///               "case .full(let items): consume(move items); default: ... }"
/// reassign   := "x = fresh(n);" (or the type's constructor)   if x var; resets Moved
/// read       := print of x's length or null test | "x.add(n);" if x var        if x Owned
/// return     := "if (t &gt; 1000) return;"
/// probe      := a deliberately illegal step: use after move or implicit copy
/// </code>
/// </summary>
internal static class OwnershipGenerator
{
    private const string Prelude = """
        error enum E { bad, }
        void step() fails E { }
        void consume(move List<i64> xs) {
            print($"{xs.length}");
        }
        void peek(List<i64> xs) {
            print($"{xs.length}");
        }
        void bump(ref List<i64> xs) {
            xs.add(1);
        }
        List<i64> fresh(i64 n) {
            var xs = List<i64>();
            xs.add(n);
            return move xs;
        }
        List<i64> pass(move List<i64> xs) {
            return move xs;
        }
        struct Ledger {
            List<i64> items;
        }
        union Slot {
            empty,
            full(List<i64> items),
        }

        """;

    public static string Generate(ulong seed, int size) => new Gen(seed).Program(size);

    private enum Kind { List, Ledger, Slot, Opt, Nest }

    private sealed class Var(string name, Kind kind, bool mutable, int loopDepth)
    {
        public string Name { get; } = name;
        public Kind Kind { get; } = kind;
        public bool Mutable { get; } = mutable;
        public int LoopDepth { get; } = loopDepth;
        public bool Moved { get; set; }
    }

    private sealed class Gen(ulong seed)
    {
        private ulong _state = seed;
        private readonly StringBuilder _sb = new();
        private readonly List<Var> _vars = [];
        private int _next;
        private int _indent;
        private int _loopDepth;
        private readonly List<Var> _borrowed = [];

        private ulong Next()
        {
            var z = _state += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        private int Int(int n) => (int)(Next() % (ulong)n);

        private bool Chance(int percent) => Int(100) < percent;

        public string Program(int size)
        {
            _sb.Append(Prelude);
            // The probe goes into one statement slot of one function, with p = 0.1.
            var probe = Chance(10) ? Int(size * size) : -1;
            for (var k = 0; k < size; k++)
            {
                _vars.Clear();
                _next = 0;
                _sb.Append($"void g{k}() {{\n");
                _indent = 1;
                Line("using let arena = Arena();");
                Line($"var t = {k};");
                for (var i = 0; i < size; i++)
                {
                    if (k * size + i == probe) Probe();
                    Stmt(0);
                }
                _sb.Append("}\n\n");
            }
            _sb.Append("void main() {\n");
            for (var k = 0; k < size; k++) _sb.Append($"    g{k}();\n");
            _sb.Append("}\n");
            return _sb.ToString();
        }

        private void Line(string text) => _sb.Append(' ', 4 * _indent).Append(text).Append('\n');

        private string Fresh() => $"fresh({Int(10)})";

        private string Name() => $"v{_next++}";

        private Var Declare(Kind kind, bool mutable)
        {
            var v = new Var(Name(), kind, mutable, _loopDepth);
            _vars.Add(v);
            return v;
        }

        private bool Movable(Var v) => !v.Moved && (v.Mutable || v.LoopDepth == _loopDepth) && !_borrowed.Contains(v);

        private Var? Pick(Func<Var, bool> where)
        {
            var found = _vars.Where(where).ToList();
            return found.Count == 0 ? null : found[Int(found.Count)];
        }

        private string Constructor(Kind kind) => kind switch
        {
            Kind.List or Kind.Opt => Fresh(),
            Kind.Ledger => $"Ledger {{ items = {Fresh()} }}",
            Kind.Slot => $"Slot.full({Fresh()})",
            _ => "List<List<i64>>()",
        };

        private void Probe()
        {
            var a = Name();
            Line($"let {a} = {Fresh()};");
            if (Chance(50))
            {
                Line($"consume(move {a});");
                Line($"peek({a});");
            }
            else
            {
                Line($"let {Name()} = {a};");
            }
        }

        private void Stmt(int depth)
        {
            for (var attempt = 0; attempt < 8; attempt++)
                if (Try(Int(13), depth)) return;
            Decl();
        }

        private bool Try(int production, int depth)
        {
            switch (production)
            {
                case 0: Decl(); return true;
                case 1: return Move();
                case 2: return Copy();
                case 3: return Take();
                case 4: return Call();
                case 5: return Borrow();
                case 6: return depth < 2 && Loop(depth);
                case 7: return depth < 2 && Branch(depth);
                case 8: return SwitchMoveSlot();
                case 9: return Reassign();
                case 10:
                case 11: return Read();
                default:
                    if (!Chance(25)) return false;
                    Line("if (t > 1000) return;");
                    return true;
            }
        }

        private void Decl()
        {
            var kind = (Kind)Int(5);
            var mutable = Chance(50);
            switch (kind)
            {
                case Kind.Opt:
                    Line($"List<i64>? {Declare(kind, true).Name} = {Fresh()};");
                    break;
                case Kind.Nest:
                    var n = Declare(kind, true);
                    Line($"var {n.Name} = List<List<i64>>();");
                    Line($"{n.Name}.add({Fresh()});");
                    break;
                default:
                    var v = Declare(kind, mutable);
                    Line($"{(mutable ? "var" : "let")} {v.Name} = {Constructor(kind)};");
                    break;
            }
        }

        private bool Move()
        {
            if (Pick(Movable) is not { } x) return false;
            x.Moved = true;
            var y = Declare(x.Kind, true);
            Line(x.Kind == Kind.Opt ? $"List<i64>? {y.Name} = move {x.Name};" : $"var {y.Name} = move {x.Name};");
            return true;
        }

        private bool Copy()
        {
            if (Pick(v => !v.Moved && v.Kind == Kind.List) is not { } x) return false;
            Line($"let {Declare(Kind.List, false).Name} = {x.Name}.copy(to: arena);");
            return true;
        }

        private bool Take()
        {
            if (Pick(v => !v.Moved && v.Mutable && v.Kind != Kind.Slot) is not { } x) return false;
            var place = x.Kind switch
            {
                Kind.Ledger => x.Name + ".items",
                Kind.Nest => x.Name + "[0]",
                _ => x.Name,
            };
            Line($"let {Declare(x.Kind == Kind.Opt ? Kind.Opt : Kind.List, false).Name} = {place}.take();");
            return true;
        }

        private bool Call()
        {
            if (Pick(v => !v.Moved && v.Kind == Kind.List) is not { } x) return false;
            switch (Int(4))
            {
                case 0 when Movable(x):
                    x.Moved = true;
                    Line($"consume(move {x.Name});");
                    break;
                case 1 when Movable(x):
                    x.Moved = true;
                    Line($"var {Declare(Kind.List, true).Name} = pass(move {x.Name});");
                    break;
                case 2 when x.Mutable:
                    Line($"bump(ref {x.Name});");
                    break;
                default:
                    Line($"peek({x.Name});");
                    break;
            }
            return true;
        }

        private bool Borrow()
        {
            if (Pick(v => !v.Moved && v.Kind is Kind.List or Kind.Nest) is not { } x) return false;
            var b = Name();
            if (x.Kind == Kind.Nest)
            {
                Line($"for (let {b} in {x.Name}) print($\"{{{b}.length}}\");");
                return true;
            }
            Line("{");
            _indent++;
            Line($"let {b} = {x.Name}.asSpan();");
            Line($"print($\"{{{b}.length}}\");");
            _indent--;
            Line("}");
            return true;
        }

        private bool Loop(int depth)
        {
            var outer = _vars.Count;
            var before = _vars.Select(v => v.Moved).ToList();
            var isFor = Chance(50);
            var i = isFor ? null : Name();
            if (isFor)
            {
                Line($"for (let {Name()} in 0..{Int(3) + 1}) {{");
            }
            else
            {
                Line($"var {i} = 0;");
                Line($"while ({i} < {Int(3) + 1}) {{");
            }
            _indent++;
            _loopDepth++;
            Line("t += 1;");
            Body(depth + 1);
            // Whatever the body moved from outside is reassigned before the next iteration.
            for (var k = 0; k < outer; k++)
            {
                var v = _vars[k];
                if (!before[k] && v.Moved)
                {
                    Assign(v);
                }
                v.Moved |= before[k];
            }
            if (!isFor) Line($"{i} += 1;");
            _vars.RemoveRange(outer, _vars.Count - outer);
            _loopDepth--;
            _indent--;
            Line("}");
            return true;
        }

        private bool Branch(int depth)
        {
            if (Chance(25) && Pick(v => !v.Moved && v.Kind == Kind.Slot) is { } s)
            {
                var scope = _vars.Count;
                var skipped = _vars.Select(v => v.Moved).ToList();
                Line($"if ({s.Name} is .full(let {Name()})) {{");
                _indent++;
                // The subject stays borrowed by the pattern binding for the whole body.
                _borrowed.Add(s);
                Body(depth + 1);
                _borrowed.Remove(s);
                Close(scope, skipped);
                return true;
            }
            var outer = _vars.Count;
            var before = _vars.Select(v => v.Moved).ToList();
            Line($"if (t % {Int(3) + 2} == 0) {{");
            _indent++;
            Body(depth + 1);
            var then = _vars.Take(outer).Select(v => v.Moved).ToList();
            _vars.RemoveRange(outer, _vars.Count - outer);
            for (var k = 0; k < outer; k++) _vars[k].Moved = before[k];
            _indent--;
            Line("} else {");
            _indent++;
            Body(depth + 1);
            Close(outer, then);
            return true;
        }

        /// <summary>Ends a block opened at <paramref name="outer"/> variables; joins with the other path's moves.</summary>
        private void Close(int outer, List<bool>? other)
        {
            _vars.RemoveRange(outer, _vars.Count - outer);
            if (other is not null)
                for (var k = 0; k < outer; k++) _vars[k].Moved |= other[k];
            _indent--;
            Line("}");
        }

        private void Body(int depth)
        {
            var n = Int(3) + 1;
            for (var k = 0; k < n; k++) Stmt(depth);
        }

        private bool SwitchMoveSlot()
        {
            if (Pick(v => Movable(v) && v.Kind == Kind.Slot) is not { } s) return false;
            s.Moved = true;
            var items = Name();
            Line($"switch (move {s.Name}) {{");
            if (Chance(50))
            {
                // A guard that may fail: the next arm binds the same storage.
                var big = Name();
                Line($"    case .full(let {big}) when {big}.length > {Int(3)}:");
                Line($"        consume(move {big});");
            }
            Line($"    case .full(let {items}):");
            Line($"        consume(move {items});");
            Line("    default:");
            Line("        print(\"empty\");");
            Line("}");
            return true;
        }

        private bool Reassign()
        {
            if (Pick(v => v.Mutable && !_borrowed.Contains(v) && (v.Moved || Chance(30))) is not { } x) return false;
            Assign(x);
            return true;
        }

        private void Assign(Var v)
        {
            Line($"{v.Name} = {Constructor(v.Kind)};");
            if (v.Kind == Kind.Nest) Line($"{v.Name}.add({Fresh()});");
            v.Moved = false;
        }

        private bool Read()
        {
            if (Pick(v => !v.Moved) is not { } x) return false;
            switch (x.Kind)
            {
                case Kind.List when x.Mutable && Chance(40):
                    Line($"{x.Name}.add({Int(10)});");
                    break;
                case Kind.List:
                    Line($"print($\"{{{x.Name}.length}}\");");
                    break;
                case Kind.Ledger:
                    Line($"print($\"{{{x.Name}.items.length}}\");");
                    break;
                case Kind.Slot:
                    Line($"print($\"{{{x.Name} is .empty}}\");");
                    break;
                case Kind.Opt:
                    Line($"print($\"{{{x.Name} is null}}\");");
                    break;
                default:
                    Line($"print($\"{{{x.Name}.length}} {{{x.Name}[0].length}}\");");
                    break;
            }
            return true;
        }
    }
}
