using System.Text;
using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// Seeded generator of Bayan programs over function values (spec 007 L013). The same seed always gives the
/// same text: the PRNG is splitmix64 and output order never depends on a hash table. Every program evaluates
/// each case's code four ways and prints a section for each: <c>== direct</c> (a named function called by name,
/// or a lambda's body evaluated with its parameters bound by <c>let</c>), <c>== local</c> (the value held in a
/// function-typed <c>let</c> and called), <c>== via1</c> (passed to one user function that calls it) and
/// <c>== via2</c> (passed to a user function that forwards it to the first). L013 holds when the four sections
/// print the same text.
/// <code>
/// program  := case{3..6} main
/// case     := sig [named] call relay host{4}
/// sig      := T = "() -> R" | "P -> R" | "(P, P) -> R";  P, R := "i64" | "bool" | "string"
/// named    := "R fn&lt;i&gt;(P p0[, to P p1])" ("=> body" | "{ return body }")
/// call     := "R call&lt;i&gt;([P a0][, to P a1,] by (T) f) => f(a0, a1)"
/// relay    := "R relay&lt;i&gt;([P a0][, to P a1,] by (T) f) => call&lt;i&gt;(a0, to: a1, by: f)"
/// host     := "void host&lt;i&gt;&lt;Mode&gt;([P q0][, to P q1])" "let k&lt;j&gt;: P = init"{0..2, those the body reads} mode "print($\"c&lt;i&gt; {r}\")"
///             (named and non-capturing cases have no q or k; a capturing case's body names one of them)
/// mode     := Direct: "let r: R = fn&lt;i&gt;(args)" | "let p&lt;j&gt;: P = arg ... let r: R = body"
///             Local:  "let f: T [uses alloc] = value" "let r: R = f(args)"
///             Via1:   "let r: R = call&lt;i&gt;(args, by: value)"
///             Via2:   "let r: R = relay&lt;i&gt;(args, by: value)" | "let f: T = value" "let r: R = relay&lt;i&gt;(args, by: f)" (T without uses)
/// value    := "fn&lt;i&gt;" | lambda | it-form (one parameter, at an argument)
/// lambda   := ("() =>" | "p0 =>" | "(p0, p1) =>") (body | "{ return body }")
/// body     := i64: term op term [op term] over i64 names and 0..9
///             bool: i64 comparison | bool name | "!" bool name | string name "==" literal | bool "&amp;&amp;"/"||" bool
///             string: string name | interpolation of two names or constants (then the local's type says "uses alloc")
/// </code>
/// </summary>
internal static class LambdaGenerator
{
    public static string Generate(ulong seed) => new Gen(seed).Program();

    public static readonly string[] Modes = ["direct", "local", "via1", "via2"];

    private static readonly string[] Types = ["i64", "bool", "string"];

    private static readonly string[] Strings = ["ab", "x", "", "bayan"];

    private enum Kind { Named, Pure, Capturing }

    private sealed record Var(string Name, string Type);

    private sealed class Case
    {
        public required int I;
        public required Kind Kind;
        public required List<string> Params;
        public required string Return;
        public required List<Var> Qs;
        public required List<string> QArgs;
        public required List<(Var Var, string Init)> Ks;
        public required List<string> Args;
        public string Body = "";
        public bool Alloc;
        public bool Block;

        public string Type => (Params.Count == 1 ? Params[0] : $"({string.Join(", ", Params)})") + " -> " + Return;

        public string LocalType => Type + (Alloc ? " uses alloc" : "");
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
            var cases = new List<Case>();
            var count = Between(3, 6);
            for (var i = 0; i < count; i++)
            {
                var c = NewCase(i);
                cases.Add(c);
                Declarations(c);
            }
            _sb.Append("void main() {\n");
            foreach (var mode in Modes)
            {
                _sb.Append($"    print(\"== {mode}\")\n");
                foreach (var c in cases)
                    _sb.Append($"    host{c.I}{Title(mode)}({ArgList(c.QArgs, c.Qs.Select(q => q.Type).ToList(), "q0")})\n");
            }
            _sb.Append("}\n");
            return _sb.ToString();
        }

        private static string Title(string mode) => char.ToUpperInvariant(mode[0]) + mode[1..];

        private string Literal(string type) => type switch
        {
            "i64" => Between(-9, 20).ToString(),
            "bool" => Pick(["true", "false"]),
            _ => $"\"{Pick(Strings)}\"",
        };

        private Case NewCase(int i)
        {
            var arity = Between(0, 2);
            var ps = Enumerable.Range(0, arity).Select(_ => Pick(Types)).ToList();
            var c = new Case
            {
                I = i,
                Kind = Pick([Kind.Named, Kind.Pure, Kind.Capturing, Kind.Capturing]),
                Params = ps,
                Return = Pick(Types),
                Qs = [],
                QArgs = [],
                Ks = [],
                Args = ps.Select(Literal).ToList(),
            };
            if (c.Kind == Kind.Capturing)
            {
                var qs = Between(0, 2);
                for (var j = 0; j < qs; j++)
                {
                    var t = Pick(Types);
                    c.Qs.Add(new Var($"q{j}", t));
                    c.QArgs.Add(Literal(t));
                }
                var ks = Between(qs == 0 ? 1 : 0, 2);
                for (var j = 0; j < ks; j++)
                {
                    // An i64 result can only read an i64 capture, so one is always there.
                    var t = j == 0 && c.Return == "i64" ? "i64" : Pick(Types);
                    c.Ks.Add((new Var($"k{j}", t), Init(t, c.Qs)));
                }
                if (c.Return == "i64" && !c.Qs.Concat(c.Ks.Select(k => k.Var)).Any(v => v.Type == "i64"))
                    c.Ks.Add((new Var($"k{c.Ks.Count}", "i64"), Init("i64", c.Qs)));
            }
            var env = ps.Select((t, j) => new Var($"p{j}", t)).ToList();
            var captures = c.Qs.Concat(c.Ks.Select(k => k.Var)).ToList();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                c.Alloc = false;
                c.Body = Body(c.Return, env.Concat(captures).ToList(), ref c.Alloc);
                if (c.Kind != Kind.Capturing || captures.Any(v => Mentions(c.Body, v.Name))) break;
            }
            // An unread `let` is an unread C local, which -Wall rejects; keep only the ones the body reads.
            c.Ks.RemoveAll(k => !Mentions(c.Body, k.Var.Name));
            c.Block = Chance(25);
            return c;
        }

        private string Init(string type, List<Var> qs)
        {
            var same = qs.Where(q => q.Type == type).ToList();
            if (type == "i64" && qs.Any(q => q.Type == "i64") && Chance(60))
                return $"{Pick(same).Name} + {Between(1, 9)}";
            if (type == "bool" && qs.Any(q => q.Type == "i64") && Chance(60))
                return $"{Pick(qs.Where(q => q.Type == "i64").ToList()).Name} > {Between(0, 9)}";
            if (same.Count > 0 && Chance(40)) return Pick(same).Name;
            return Literal(type);
        }

        private static bool Mentions(string body, string name) => Regex.IsMatch(body, $@"\b{name}\b");

        private string Term(List<Var> env)
        {
            var ints = env.Where(v => v.Type == "i64").ToList();
            return ints.Count > 0 && Chance(70) ? Pick(ints).Name : Between(0, 9).ToString();
        }

        private string Body(string type, List<Var> env, ref bool alloc)
        {
            var bools = env.Where(v => v.Type == "bool").ToList();
            var strings = env.Where(v => v.Type == "string").ToList();
            switch (type)
            {
                case "i64":
                    return Chance(50)
                        ? $"{Term(env)} {Pick(["+", "-", "*"])} {Term(env)}"
                        : $"{Term(env)} {Pick(["+", "-", "*"])} {Term(env)} {Pick(["+", "-"])} {Term(env)}";
                case "bool":
                {
                    var atom = Atom(env, bools, strings);
                    if (!Chance(40)) return atom;
                    // The same operand on both sides (`!k1 && k1`) is -Wtautological-*, so the right side must differ.
                    var other = Atom(env, bools, strings);
                    return other.TrimStart('!') == atom.TrimStart('!') ? atom : $"{atom} {Pick(["&&", "||"])} {other}";
                }
                default:
                    if (strings.Count > 0 && Chance(25)) return Pick(strings).Name;
                    alloc = true;
                    return $"$\"{{{Hole(env)}}}-{{{Hole(env)}}}\"";
            }
        }

        private string Atom(List<Var> env, List<Var> bools, List<Var> strings)
        {
            var roll = Int(4);
            if (roll == 1 && bools.Count > 0) return (Chance(50) ? "!" : "") + Pick(bools).Name;
            if (roll == 2 && strings.Count > 0) return $"{Pick(strings).Name} == \"{Pick(Strings)}\"";
            // A name compared with itself is -Wtautological-compare, so the right side is then a constant.
            var left = Term(env);
            var right = Term(env);
            return $"{left} {Pick([">", "<", "=="])} {(right == left ? Between(0, 9).ToString() : right)}";
        }

        private string Hole(List<Var> env) => env.Count > 0 && Chance(80) ? Pick(env).Name : Between(0, 9).ToString();

        /// <summary>Arguments to a user function: the first is named when it is a bool literal (BYN2305), the second takes `to`.</summary>
        private static string ArgList(List<string> args, List<string> types, string first)
        {
            var parts = new List<string>();
            for (var j = 0; j < args.Count; j++)
                parts.Add(j == 1 ? $"to: {args[j]}" : types[j] == "bool" ? $"{first}: {args[j]}" : args[j]);
            return string.Join(", ", parts);
        }

        private static string ParamList(List<string> types, string prefix) =>
            string.Join(", ", types.Select((t, j) => (j == 1 ? "to " : "") + $"{t} {prefix}{j}"));

        private string Lambda(Case c, string indent, bool argument)
        {
            if (argument && !c.Block && c.Params.Count == 1 && Mentions(c.Body, "p0") && Chance(50))
                return Regex.Replace(c.Body, @"\bp0\b", "it");
            var head = c.Params.Count switch { 0 => "()", 1 => "p0", _ => "(p0, p1)" };
            return c.Block ? $"{head} => {{\n{indent}    return {c.Body}\n{indent}}}" : $"{head} => {c.Body}";
        }

        private string Value(Case c, string indent, bool argument) => c.Kind == Kind.Named ? $"fn{c.I}" : Lambda(c, indent, argument);

        private void Declarations(Case c)
        {
            var by = $"by ({c.Type}) f";
            var sig = c.Params.Count == 0 ? by : $"{ParamList(c.Params, "a")}, {by}";
            var names = string.Join(", ", c.Params.Select((_, j) => $"a{j}"));
            if (c.Kind == Kind.Named)
            {
                _sb.Append($"{c.Return} fn{c.I}({ParamList(c.Params, "p")})");
                _sb.Append(c.Block ? $" {{\n    return {c.Body}\n}}\n\n" : $" => {c.Body}\n\n");
            }
            _sb.Append($"{c.Return} call{c.I}({sig}) => f({names})\n\n");
            var forward = string.Join(", ", c.Params.Select((_, j) => j == 1 ? "to: a1" : $"a{j}").Append("by: f"));
            _sb.Append($"{c.Return} relay{c.I}({sig}) => call{c.I}({forward})\n\n");
            foreach (var mode in Modes)
            {
                _sb.Append($"void host{c.I}{Title(mode)}({ParamList(c.Qs.Select(q => q.Type).ToList(), "q")}) {{\n");
                foreach (var (v, init) in c.Ks) _sb.Append($"    let {v.Name}: {v.Type} = {init}\n");
                var args = ArgList(c.Args, c.Params, "a0");
                var withBy = c.Params.Count == 0 ? "by: " : args + ", by: ";
                switch (mode)
                {
                    case "direct" when c.Kind == Kind.Named:
                        _sb.Append($"    let r: {c.Return} = fn{c.I}({ArgList(c.Args, c.Params, "p0")})\n");
                        break;
                    case "direct":
                        for (var j = 0; j < c.Params.Count; j++)
                            if (Mentions(c.Body, $"p{j}")) _sb.Append($"    let p{j}: {c.Params[j]} = {c.Args[j]}\n");
                        _sb.Append($"    let r: {c.Return} = {c.Body}\n");
                        break;
                    case "local":
                        _sb.Append($"    let f: {c.LocalType} = {Value(c, "    ", false)}\n");
                        _sb.Append($"    let r: {c.Return} = f({string.Join(", ", c.Args)})\n");
                        break;
                    case "via1":
                        _sb.Append($"    let r: {c.Return} = call{c.I}({withBy}{Value(c, "    ", true)})\n");
                        break;
                    default:
                        // A `uses alloc` local is not a value of the parameter's effect-free type (BYN2202), so only an effect-free one is passed.
                        if (!c.Alloc && Chance(50))
                        {
                            _sb.Append($"    let f: {c.LocalType} = {Value(c, "    ", false)}\n");
                            _sb.Append($"    let r: {c.Return} = relay{c.I}({withBy}f)\n");
                        }
                        else
                            _sb.Append($"    let r: {c.Return} = relay{c.I}({withBy}{Value(c, "    ", true)})\n");
                        break;
                }
                _sb.Append($"    print($\"c{c.I} {{r}}\")\n}}\n\n");
            }
        }
    }
}
