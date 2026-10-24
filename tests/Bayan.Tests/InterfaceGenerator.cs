using System.Text;

namespace Bayan.Tests;

/// <summary>
/// Seeded generator of Bayan programs over interfaces (spec 006 L012). The same seed always gives the
/// same text: the PRNG is splitmix64 and output order never depends on a hash table. Every program runs
/// one call sequence per interface on every conformer three ways and prints a section for each:
/// <c>== direct</c> on the concrete type, <c>== constraint</c> through <c>where T : I</c>, and
/// <c>== dyn</c> through <c>dyn ref readonly I</c> and <c>dyn ref I</c>. L012 holds when the three
/// sections print the same text.
/// <code>
/// program    := interface{1..2} (conformer{2..3} runners)per interface main
/// interface  := "interface IGen&lt;k&gt; {" abstract{2..4} default{1..2} "}"
/// abstract   := "i64 m&lt;j&gt;(i64 x);" | "i64 m&lt;j&gt; { get; }" | "string m&lt;j&gt;();" | "void m&lt;j&gt;(ref this, i64 by);"
///               (at least one i64 method or property, which the defaults call)
/// default    := "string m&lt;j&gt;() => $\"...{call}...\";" | "i64 m&lt;j&gt;(i64 x) => call + call;"
/// conformer  := "struct S&lt;k&gt;&lt;n&gt; : IGen&lt;k&gt; { i64 f0; i64 f1;" impl per abstract
///               ["// override" impl of a default, p = 0.4 each] "}"
/// impl       := arithmetic over f0, f1, x and constants | "f0 += by * c;" | "f1 = f0 - by;" for ref this
/// runners    := "void roS(S v)" "void roVia&lt;T&gt;(T v) where T : I" "void roDyn(dyn ref readonly I v)"
///               "void rwS(ref S v)" "void rwVia&lt;T&gt;(ref T v) where T : I" "void rwDyn(dyn ref I v)"
///               ro bodies: the sequence without ref this calls; rw bodies: the whole sequence
/// sequence   := step{4..8}; step := print of a call to any non-mutating member | "v.m&lt;j&gt;(c);"
/// main       := per section, per conformer: "{ var v = S { f0: a, f1: b }; ro(v); rw(ref v) or rwDyn(v); print fields }"
/// </code>
/// </summary>
internal static class InterfaceGenerator
{
    public static string Generate(ulong seed) => new Gen(seed).Program();

    private enum Kind { Method, Property, Text, Mutator, DefaultText, DefaultMethod }

    private sealed record Member(string Name, Kind Kind)
    {
        public bool IsDefault => Kind is Kind.DefaultText or Kind.DefaultMethod;
    }

    private sealed class Gen(ulong seed)
    {
        private ulong _state = seed;
        private readonly StringBuilder _sb = new();

        private ulong Next()
        {
            var z = _state += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        private int Int(int n) => (int)(Next() % (ulong)n);

        private int Between(int lo, int hi) => lo + Int(hi - lo + 1);

        private bool Chance(int percent) => Int(100) < percent;

        private T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];

        public string Program()
        {
            var interfaces = Between(1, 2);
            var conformers = new List<(string Name, int A, int B)>[interfaces];
            for (var k = 0; k < interfaces; k++)
            {
                var members = Interface(k);
                conformers[k] = [];
                var count = Between(2, 3);
                for (var n = 0; n < count; n++)
                {
                    var name = $"S{k}{n}";
                    Conformer(name, $"IGen{k}", members);
                    conformers[k].Add((name, Between(-5, 9), Between(-5, 9)));
                }
                Runners(k, members, conformers[k]);
            }
            _sb.Append("void main() {\n");
            foreach (var (section, ro, rw) in new[] { ("direct", "ro{0}", "rw{0}(ref v)"), ("constraint", "roVia{1}", "rwVia{1}(ref v)"), ("dyn", "roDyn{1}", "rwDyn{1}(v)") })
            {
                _sb.Append($"    print(\"== {section}\")\n");
                for (var k = 0; k < interfaces; k++)
                    foreach (var (name, a, b) in conformers[k])
                    {
                        _sb.Append($"    {{\n        var v = {name} {{ f0: {a}, f1: {b} }}\n");
                        _sb.Append($"        {string.Format(ro, name, k)}(v)\n");
                        _sb.Append($"        {string.Format(rw, name, k)}\n");
                        _sb.Append($"        print($\"{name} {{v.f0}} {{v.f1}}\")\n    }}\n");
                    }
            }
            _sb.Append("}\n");
            return _sb.ToString();
        }

        private List<Member> Interface(int k)
        {
            var members = new List<Member>();
            var abstracts = Between(2, 4);
            for (var j = 0; j < abstracts; j++)
                members.Add(new Member($"m{j}", j == 0 ? Pick([Kind.Method, Kind.Property]) : Pick([Kind.Method, Kind.Property, Kind.Text, Kind.Mutator])));
            var defaults = Between(1, 2);
            for (var j = abstracts; j < abstracts + defaults; j++)
                members.Add(new Member($"m{j}", Pick([Kind.DefaultText, Kind.DefaultMethod])));
            _sb.Append($"interface IGen{k} {{\n");
            foreach (var m in members)
            {
                _sb.Append("    ");
                _sb.Append(m.Kind switch
                {
                    Kind.Method => $"i64 {m.Name}(i64 x)",
                    Kind.Property => $"i64 {m.Name}",
                    Kind.Text => $"string {m.Name}()",
                    Kind.Mutator => $"void {m.Name}(ref this, i64 by)",
                    Kind.DefaultText => $"string {m.Name}() => $\"{m.Name} {{{Call(members)}}} {{{Call(members)}}}\"",
                    _ => $"i64 {m.Name}(i64 x) => {Call(members, "x")} + {Call(members)}",
                });
                _sb.Append('\n');
            }
            _sb.Append("}\n\n");
            return members;
        }

        /// <summary>A bare call to an abstract i64 member, as a default body writes it.</summary>
        private string Call(List<Member> members, string arg = "")
        {
            var m = Pick(members.Where(m => m.Kind is Kind.Method or Kind.Property).ToList());
            return m.Kind == Kind.Property ? m.Name : $"{m.Name}({(arg == "" ? Between(0, 9).ToString() : arg)})";
        }

        private string Term(bool x) => Int(x ? 4 : 3) switch
        {
            0 => "f0",
            1 => "f1",
            2 => Between(1, 9).ToString(),
            _ => "x",
        };

        private string Expr(bool x) => $"{Term(x)} {Pick(["+", "-", "*"])} {Term(x)}";

        private void Conformer(string name, string iface, List<Member> members)
        {
            _sb.Append($"struct {name} : {iface} {{\n    i64 f0\n    i64 f1\n\n");
            foreach (var m in members)
            {
                if (m.IsDefault && !Chance(40)) continue;
                if (m.IsDefault) _sb.Append("    // override\n");
                _sb.Append("    ");
                _sb.Append(m.Kind switch
                {
                    Kind.Method => $"i64 {m.Name}(i64 x) => {Expr(true)}",
                    Kind.Property => $"i64 {m.Name} => {Expr(false)}",
                    Kind.Text => $"string {m.Name}() => $\"{name} {{{Expr(false)}}}\"",
                    Kind.Mutator => Chance(50)
                        ? $"void {m.Name}(ref this, i64 by) {{\n        f0 += by * {Between(1, 3)}\n    }}"
                        : $"void {m.Name}(ref this, i64 by) {{\n        f1 = f0 - by\n    }}",
                    Kind.DefaultText => $"string {m.Name}() => $\"{name} {m.Name} {{{Expr(false)}}}\"",
                    _ => $"i64 {m.Name}(i64 x) => {Expr(true)}",
                });
                _sb.Append('\n');
            }
            _sb.Append("}\n\n");
        }

        private void Runners(int k, List<Member> members, List<(string Name, int A, int B)> conformers)
        {
            var steps = new List<string>();
            var count = Between(4, 8);
            for (var i = 0; i < count; i++)
            {
                var m = Pick(members);
                steps.Add(m.Kind switch
                {
                    Kind.Method or Kind.DefaultMethod => $"print($\"{m.Name} {{v.{m.Name}({Between(-3, 9)})}}\")",
                    Kind.Property => $"print($\"{m.Name} {{v.{m.Name}}}\")",
                    Kind.Text or Kind.DefaultText => $"print(v.{m.Name}())",
                    _ => $"v.{m.Name}({Between(-3, 9)})",
                });
            }
            var readOnly = steps.Where(s => s.StartsWith("print", StringComparison.Ordinal)).ToList();
            foreach (var (name, _, _) in conformers)
            {
                Runner($"void ro{name}({name} v)", readOnly);
                Runner($"void rw{name}(ref {name} v)", steps);
            }
            Runner($"void roVia{k}<T>(T v) where T : IGen{k}", readOnly);
            Runner($"void rwVia{k}<T>(ref T v) where T : IGen{k}", steps);
            Runner($"void roDyn{k}(dyn ref readonly IGen{k} v)", readOnly);
            Runner($"void rwDyn{k}(dyn ref IGen{k} v)", steps);
        }

        private void Runner(string signature, List<string> steps)
        {
            _sb.Append(signature).Append(" {\n");
            foreach (var s in steps) _sb.Append("    ").Append(s).Append('\n');
            _sb.Append("}\n\n");
        }
    }
}
