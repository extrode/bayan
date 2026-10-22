using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// L006 (docs/laws/006-unique-never-aliases.md): every way to create a second owner of a unique value is
/// rejected with its ownership diagnostic, and the same program written with a copy, a reassignment or a
/// shorter borrow is accepted. Each row is bound and analyzed as the diag goldens are, and the exact error
/// codes it reports are compared (warnings ignored).
/// </summary>
[Trait("Law", "L006")]
public sealed class L006OwnershipGuardTests
{
    private const string Prelude = """
        error enum E { bad, }
        void step() fails E { }
        void consume(move List<i64> xs) {
            print($"{xs.count}");
        }
        struct Ledger {
            List<i64> items;
        }
        union Slot {
            empty,
            full(List<i64> items),
        }

        """;

    public static TheoryData<string, string, string> Rejected => new()
    {
        // Use of a moved local on any path (BYN2781).
        { "use after move, straight-line", """
            void main() {
                var a = List<i64>();
                var b = move a;
                a.append(1);
                print($"{b.count}");
            }
            """, "BYN2781" },
        { "use after move, later for iteration", """
            void main() {
                let a = List<i64>();
                for i in 0..3 {
                    consume(move a);
                }
            }
            """, "BYN2781" },
        { "use after move, later while iteration", """
            void main() {
                let a = List<i64>();
                var i = 0;
                while i < 3 {
                    consume(move a);
                    i += 1;
                }
            }
            """, "BYN2781" },
        { "use after move, after if", """
            void main(ReadOnlySpan<string> args) {
                let a = List<i64>();
                if args.count > 0 {
                    consume(move a);
                }
                print($"{a.count}");
            }
            """, "BYN2781" },
        { "use after move, after switch", """
            void main(ReadOnlySpan<string> args) {
                let a = List<i64>();
                switch args.count {
                    case 0:
                        consume(move a);
                    default:
                        print("kept");
                }
                print($"{a.count}");
            }
            """, "BYN2781" },
        { "use after move, defer at scope exit", """
            void main() {
                let xs = List<i64>();
                defer print($"{xs.count}");
                consume(move xs);
            }
            """, "BYN2781" },
        { "use after move, errdefer on failing exit", """
            List<i64> build() fails E {
                let xs = List<i64>();
                defer failing print($"{xs.count}");
                consume(move xs);
                try step();
                return List<i64>();
            }
            void main() { }
            """, "BYN2781" },

        // Implicit copy of a unique value or of a type holding one (BYN2780).
        { "implicit copy, List", """
            void main() {
                let a = List<i64>();
                let b = a;
            }
            """, "BYN2780" },
        { "implicit copy, Map", """
            void main() {
                let m = Map<string, i64>();
                let n = m;
            }
            """, "BYN2780" },
        { "implicit copy, StrBuf", """
            void main() {
                let sb = StrBuf();
                let other = sb;
            }
            """, "BYN2780" },
        { "implicit copy, struct with a List field", """
            void main() {
                let ledger = Ledger { items: List<i64>() };
                let c = ledger;
            }
            """, "BYN2780" },
        { "implicit copy, union with a unique payload", """
            void main() {
                let s = Slot.full(List<i64>());
                let t = s;
            }
            """, "BYN2780" },
        { "implicit copy, tuple holding a List", """
            void main() {
                let t = (List<i64>(), 1);
                let u = t;
            }
            """, "BYN2780" },
        { "implicit copy, List into a tuple", """
            void main() {
                let xs = List<i64>();
                let t = (xs, 1);
            }
            """, "BYN2780" },
        { "implicit copy, optional holding a List", """
            void main() {
                List<i64>? o = List<i64>();
                let p = o;
            }
            """, "BYN2780" },
        { "implicit copy, List into an optional", """
            void main() {
                let xs = List<i64>();
                List<i64>? o = xs;
            }
            """, "BYN2780" },
        { "implicit copy, ref parameter stored", """
            void keep(ref List<i64> shared, ref Ledger ledger) {
                ledger.items = shared;
            }
            void main() { }
            """, "BYN2780" },

        // A borrow stored somewhere that outlives its owner (BYN3002, BYN3003), or a borrowed binding stored (BYN2780).
        { "span stored past its owner", """
            void main() {
                var outer = List<i64>().asSpan();
                {
                    let xs = List<i64>();
                    outer = xs.asSpan();
                }
                print($"{outer.count}");
            }
            """, "BYN3002" },
        { "span returned past its owner", """
            Span<i64> leak() {
                let xs = List<i64>();
                return xs.asSpan();
            }
            void main() { }
            """, "BYN3003" },
        { "slice returned past its owner", """
            Span<i64> leak() {
                let buf = stackalloc i64[4];
                return buf[1..];
            }
            void main() { }
            """, "BYN3003" },
        { "borrowed loop binding stored", """
            void main() {
                let lists = List<List<i64>>();
                var kept = List<i64>();
                for xs in lists { kept = xs; }
            }
            """, "BYN2780" },
        { "borrowed loop binding's span stored past its owner", """
            void main() {
                var outer = List<i64>().asSpan();
                {
                    let lists = List<List<i64>>();
                    for xs in lists { outer = xs.asSpan(); }
                }
                print($"{outer.count}");
            }
            """, "BYN3002" },
        { "borrowed pattern binding stored", """
            void main() {
                let s = Slot.full(List<i64>());
                var kept = List<i64>();
                if s is .full(let items) { kept = items; }
            }
            """, "BYN2780" },
        { "borrowed pattern binding's span stored past its owner", """
            void main() {
                var outer = List<i64>().asSpan();
                {
                    let s = Slot.full(List<i64>());
                    switch s {
                        case .full(let items):
                            outer = items.asSpan();
                        default:
                            print("empty");
                    }
                }
                print($"{outer.count}");
            }
            """, "BYN3002" },

        // A move out of a field, element or plain parameter (BYN2782).
        { "move out of a struct field", """
            void main() {
                let ledger = Ledger { items: List<i64>() };
                let items = move ledger.items;
            }
            """, "BYN2782" },
        { "move out of a list element", """
            void main() {
                let lists = List<List<i64>>();
                let first = move lists[0];
            }
            """, "BYN2782" },
        { "move out of a plain parameter", """
            void take(List<i64> plain) {
                let mine = move plain;
            }
            void main() { }
            """, "BYN2782" },

        // Moving a borrowed binding (BYN2783).
        { "move a borrowed for variable", """
            void main() {
                let lists = List<List<i64>>();
                for xs in lists {
                    let stolen = move xs;
                }
            }
            """, "BYN2783" },
        { "move a borrowed pattern binding", """
            void main() {
                let s = Slot.full(List<i64>());
                if s is .full(let items) {
                    let stolen = move items;
                }
            }
            """, "BYN2783" },

        // switch move x and take() misuse (BYN2783, BYN2781, BYN2789, BYN2104, BYN2788).
        { "switch without move then move the binding", """
            void main() {
                let s = Slot.full(List<i64>());
                switch s {
                    case .full(let items):
                        consume(move items);
                    default:
                        print("empty");
                }
            }
            """, "BYN2783" },
        { "read after switch move s", """
            void main() {
                let s = Slot.full(List<i64>());
                switch move s {
                    case .full(let items):
                        consume(move items);
                    default:
                        print("empty");
                }
                switch s {
                    default:
                        print("again");
                }
            }
            """, "BYN2781" },
        { "switch move s binding moved then reused in the same arm", """
            void main() {
                let s = Slot.full(List<i64>());
                switch move s {
                    case .full(let items):
                        consume(move items);
                        print($"{items.count}");
                    default:
                        print("empty");
                }
            }
            """, "BYN2781" },
        { "switch move s in a loop, s declared outside", """
            void main() {
                let s = Slot.full(List<i64>());
                for i in 0..3 {
                    switch move s {
                        case .full(let items):
                            consume(move items);
                        default:
                            print("empty");
                    }
                }
            }
            """, "BYN2781" },
        { "switch move of a struct field", """
            void main() {
                let ledger = Ledger { items: List<i64>() };
                switch move ledger.items {
                    default:
                        print("field");
                }
            }
            """, "BYN2789" },
        { "switch move of a plain parameter", """
            void inspect(Slot plain) {
                switch move plain {
                    default:
                        print("plain");
                }
            }
            void main() { }
            """, "BYN2789" },
        { "switch move and pattern binds both sides", """
            void main() {
                var a = List<i64>();
                a.append(1);
                switch move a {
                    case let x and let y:
                        consume(move x);
                        consume(move y);
                    default:
                        print("other");
                }
            }
            """, "BYN2790" },
        { "switch move designation with a nested binding", """
            void main() {
                var l = Ledger { items: List<i64>() };
                switch move l {
                    case Ledger { items: let it } whole:
                        consume(move it);
                    default:
                        print("other");
                }
            }
            """, "BYN2790" },
        { "switch move property pattern binds a field twice", """
            void main() {
                var l = Ledger { items: List<i64>() };
                switch move l {
                    case { items: let x, items: let y }:
                        consume(move x);
                        consume(move y);
                    default:
                        print("other");
                }
            }
            """, "BYN2790" },
        { "switch move guard mutates its binding", """
            void main() {
                let s = Slot.full(List<i64>());
                switch move s {
                    case .full(var items) when items.removeLast() > 0:
                        consume(move items);
                    default:
                        print("other");
                }
            }
            """, "BYN2791" },
        { "take on a let local", """
            void main() {
                let xs = List<i64>();
                let ys = xs.take();
            }
            """, "BYN2104" },
        { "take on a plain parameter", """
            void drain(List<i64> plain) {
                let ys = plain.take();
            }
            void main() { }
            """, "BYN2104" },
        { "take on i64", """
            void drain(ref i64 n) {
                let a = n.take();
            }
            void main() { }
            """, "BYN2788" },
        { "take on string", """
            void drain(ref string s) {
                let a = s.take();
            }
            void main() { }
            """, "BYN2788" },
    };

    public static TheoryData<string, string> Accepted => new()
    {
        { "explicit copy, List", """
            void main() {
                using let arena = Arena();
                let a = List<i64>();
                let b = a.copy(to: arena);
                print($"{a.count} {b.count}");
            }
            """ },
        { "explicit copy, Map", """
            void main() {
                using let arena = Arena();
                let m = Map<string, i64>();
                let n = m.copy(to: arena);
                print($"{m.count} {n.count}");
            }
            """ },
        { "explicit copy, StrBuf", """
            void main() {
                using let arena = Arena();
                let sb = StrBuf();
                let other = sb.copy(to: arena);
                print($"{sb.finish()} {other.finish()}");
            }
            """ },
        { "explicit copy into a struct field", """
            void main() {
                using let arena = Arena();
                let xs = List<i64>();
                let ledger = Ledger { items: xs.copy(to: arena) };
                print($"{xs.count} {ledger.items.count}");
            }
            """ },
        { "reassigned before use, straight-line", """
            void main() {
                var a = List<i64>();
                var b = move a;
                a = List<i64>();
                a.append(1);
                print($"{a.count} {b.count}");
            }
            """ },
        { "reassigned before the next for iteration", """
            void main() {
                var b = List<i64>();
                for i in 0..3 {
                    consume(move b);
                    b = List<i64>();
                }
            }
            """ },
        { "reassigned before the next while iteration", """
            void main() {
                var b = List<i64>();
                var i = 0;
                while i < 3 {
                    consume(move b);
                    b = List<i64>();
                    i += 1;
                }
            }
            """ },
        { "reassigned in the moving branch", """
            void main(ReadOnlySpan<string> args) {
                var a = List<i64>();
                if args.count > 0 {
                    consume(move a);
                    a = List<i64>();
                }
                print($"{a.count}");
            }
            """ },
        { "defer reads a local moved then reassigned", """
            void main() {
                var xs = List<i64>();
                defer print($"{xs.count}");
                consume(move xs);
                xs = List<i64>();
            }
            """ },
        { "defer failing with a return move success path", """
            List<i64> build() fails E {
                let xs = List<i64>();
                defer failing print($"{xs.count}");
                try step();
                return move xs;
            }
            void main() { }
            """ },
        { "borrow ends before its owner", """
            void main() {
                var xs = List<i64>();
                xs.append(1);
                {
                    let view = xs.asSpan();
                    print($"{view[0]}");
                }
                xs.append(2);
            }
            """ },
        { "borrowed loop binding read in place", """
            void main() {
                let lists = List<List<i64>>();
                for xs in lists {
                    let view = xs.asSpan();
                    print($"{view.count}");
                }
            }
            """ },
        { "borrowed pattern binding copied", """
            void main() {
                using let arena = Arena();
                let s = Slot.full(List<i64>());
                var kept = List<i64>();
                if s is .full(let items) { kept = items.copy(to: arena); }
                print($"{kept.count}");
            }
            """ },
        { "move out of a move parameter", """
            void take(move List<i64> owned) {
                let mine = move owned;
                print($"{mine.count}");
            }
            void main() { }
            """ },
        { "switch move binds disjoint parts and moves them on", """
            struct Two {
                List<i64> first;
                List<i64> second;
            }
            void main() {
                var t = Two { first: List<i64>(), second: List<i64>() };
                switch move t {
                    case Two { first: let a, second: let b }:
                        consume(move a);
                        consume(move b);
                    default:
                        print("other");
                }
            }
            """ },
        { "switch move binds disjoint parts through one field twice", """
            struct Inner {
                List<i64> b;
                List<i64> c;
            }
            struct Outer {
                Inner a;
            }
            void main() {
                let o = Outer { a: Inner { b: List<i64>(), c: List<i64>() } };
                switch move o {
                    case { a: { b: let x }, a: { c: let y } }:
                        consume(move x);
                        consume(move y);
                    default:
                        print("other");
                }
            }
            """ },
        { "switch move binds a field and a copy of its length", """
            void main() {
                let l = Ledger { items: List<i64>() };
                switch move l {
                    case { items: let x, items.count: let n }:
                        consume(move x);
                        print($"{n}");
                    default:
                        print("other");
                }
            }
            """ },
        { "take from a List field", """
            void main() {
                var ledger = Ledger { items: List<i64>() };
                ledger.items.append(1);
                let items = ledger.items.take();
                ledger.items.append(2);
                print($"{items.count} {ledger.items.count}");
            }
            """ },
        { "take from a list element", """
            void main() {
                var lists = List<List<i64>>();
                lists.append(List<i64>());
                let first = lists[0].take();
                lists[0].append(1);
                print($"{first.count} {lists[0].count}");
            }
            """ },
        { "take from a Map element", """
            void main() {
                var groups = Map<string, List<i64>>();
                groups["x"] = List<i64>();
                let x = groups["x"].take();
                print($"{x.count} {groups["x"].count}");
            }
            """ },
        { "take from an optional field", """
            struct Pending {
                List<i64>? pending;
            }
            void main() {
                var p = Pending { pending: List<i64>() };
                let taken = p.pending.take();
                print($"{taken is null} {p.pending is null}");
            }
            """ },
        { "take through a ref parameter", """
            List<i64> drain(ref List<i64> xs) => xs.take();
            void main() {
                var shared = List<i64>();
                let drained = drain(ref shared);
                shared.append(1);
                print($"{drained.count} {shared.count}");
            }
            """ },
        { "take from a StrBuf local", """
            void main() {
                var sb = StrBuf();
                sb.append("hi");
                let text = sb.take();
                sb.append("again");
                print($"{text.finish()} {sb.finish()}");
            }
            """ },
        { "switch move s then move the binding on", """
            void main() {
                let s = Slot.full(List<i64>());
                switch move s {
                    case .full(let items):
                        consume(move items);
                    default:
                        print("empty");
                }
            }
            """ },
        { "switch move expression form moves the binding", """
            i64 keep(move List<i64> xs) => xs.count;
            void main() {
                let s = Slot.full(List<i64>());
                let n = (move s) switch {
                    .full(let items) => keep(move items),
                    .empty => 0,
                };
                print($"{n}");
            }
            """ },
        { "switch move of a move parameter", """
            void drain(move Slot slot) {
                switch move slot {
                    case .full(let items):
                        consume(move items);
                    default:
                        print("empty");
                }
            }
            void main() { }
            """ },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void RejectsSecondOwner(string name, string program, string codes) => Check(name, program, codes);

    [Theory]
    [MemberData(nameof(Accepted))]
    public void AcceptsSingleOwner(string name, string program) => Check(name, program, "");

    private static void Check(string name, string program, string expected)
    {
        var source = Prelude + program + "\n";
        var c = Compilation.Create([new SourceText("l006.byn", System.Text.Encoding.UTF8.GetBytes(source))], ".");
        var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
        var reported = string.Join(" ", errors.Select(d => d.Code).Order());
        Assert.True(reported == expected,
            $"{name}: expected [{expected}], got [{reported}]: {string.Join("; ", errors.Select(e => e.Code + " " + e.Message))}\n{source}");
    }
}
