using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T8: <c>selfhost/semantics/typeresolve.byn</c> against the C# <c>TypeResolver</c>. Each fixture's
/// <c>Probe</c> fields are resolved on both sides and printed as <c>name: display</c>, then the
/// diagnostics as <c>code line:col message</c> plus any help, sorted.
/// </summary>
public sealed class SelfhostSemanticsT8Tests
{
    private static readonly (string Name, string Text)[] Fixtures =
    [
        ("named", """
            type Cents = i64
            union error FileError {
                missing
                denied
            }
            union error NetError {
                timeout
                refused
            }
            union error AnyError = FileError, NetError except .denied
            type Pair = (i32, string name)
            type Fn = (i64) -> string
            effect io = fs, console
            type Eff = (i32) -> void uses io, alloc
            struct Probe {
                i32? a
                Pair b
                Map<string, i32> c
                Fn d
                Eff e
                List<Cents> f
                Result<i32, AnyError> g
                Span<u8> h
                ReadOnlySpan<u8> i
                Owned<string> j
                Untrusted<string> k
                Arena l
                StrBuf m
                Map<(i32, bool), Cents> n
                i64 boundX
            }
            """),
        ("errors", """
            type Loop = Loop2
            type Loop2 = Loop
            type Opt = i32?
            type Cents = i64
            union error FileError {
                missing
            }
            union error Odd = FileError except .nope
            union error Gone = FileError except .missing
            struct Probe {
                Opt? a
                String b
                void c
                List<i32, i32> d
                Map<f64, i32> e
                HashMap<string, i32> f
                Loop g
                Odd h
                Gone i
                Result<i32, i64> j
                Foo.Bar k
                dec l
                i32<i64> m
                cents n
                Map<string> o
                Result<void, FileError> p
            }
            """),
        ("compose", """
            union error A {
                x
                y
            }
            union error B {
                z
            }
            union error AB = A, B except .x
            union error Again = AB, A
            union error Twice = AB, AB
            effect io = fs, console
            effect loopy = loopy, net
            type Fx = () -> void uses io, loopy, fs, io
            struct Probe {
                Again a
                AB b
                Fx c
                Result<i32, AB> d
                Map<List<i32>, i32> e
                Strbuf f
                boolean g
                str h
                dyn ref Missing i
                a.b.c j
                Twice k
                Result<i32, A> l
            }
            """),
    ];

    private static string Escape(string s) => s.Replace("\r", "").Replace("\n", "\\n");

    private static string MainSource()
    {
        var calls = string.Join("\n", Fixtures.Select(f => $"    t8Run(\"{f.Name}\", by: \"{Escape(f.Text)}\")"));
        return MainHead + calls + "\n    return 0\n}\n";
    }

    private const string MainHead = """
        import lexer
        import parser

        struct T8Pending {
            i64 kind
            i64 index
            TextSpan name
            List<TextSpan> variants
        }

        struct T8Field {
            string name
            i64 index
        }

        SourceFile makeFile(string text) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            var copy = List<u8>()
            for b in text.bytes { copy.append(b) }
            let source = lexer.SourceText.create("t8.byn", from: move bytes)
            var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
            let unit = parser.parseCompilationUnit(ref p)
            return SourceFile { path: "t8.byn", displayPath: "t8.byn", text: move copy, unit: move unit, module: 0, imports: Map<string, i64>() }
        }

        string lineCol(string text, at i64 offset) {
            var line = 1
            var col = 1
            let b = text.bytes
            for i in 0..offset {
                if b[i] == 0x0A {
                    line += 1
                    col = 1
                } else {
                    col += 1
                }
            }
            return $"{line}:{col}"
        }

        /// Registers the fixture's error enums as sets and its aliases and `union error` compositions as aliases.
        List<AliasId> t8Declare(ref Ctx c) {
            var pending = List<T8Pending>()
            for i in 0..c.files[0].unit.members.length {
                switch c.files[0].unit.members[i] {
                    case .errorEnumDecl(let d):
                        var vs = List<TextSpan>()
                        for v in d.value.variants { vs.append(v.name) }
                        pending.append(T8Pending { kind: 0, index: i, name: d.value.name, variants: move vs })
                    case .errorSetDecl(let d):
                        pending.append(T8Pending { kind: 1, index: i, name: d.value.name, variants: List<TextSpan>() })
                    case .typeAliasDecl(let d):
                        pending.append(T8Pending { kind: 1, index: i, name: d.value.name, variants: List<TextSpan>() })
                    default:
                        continue
                }
            }
            var aliases = List<AliasId>()
            for p in pending {
                let name = t8Text(in c, from: 0, at: p.name)
                if p.kind == 0 {
                    let s = newErrorSet(ref c, by: ErrorSetSymbol { name: name, nameSpan: p.name, module: 0, file: 0, declIndex: p.index })
                    for v in p.variants {
                        let vName = t8Text(in c, from: 0, at: v)
                        let id = newErrorVariant(ref c, by: ErrorVariantSymbol { name: vName, nameSpan: v, set: s, file: 0 })
                        c.errorSets[s].variants.append(id)
                    }
                    c.modules[0].types[name] = errorSetTypeOf(s)
                } else {
                    let a = newAlias(ref c, by: AliasPlaceholder { name: name, nameSpan: p.name, file: 0, declIndex: p.index })
                    c.modules[0].types[name] = t8AliasType(a)
                    aliases.append(a)
                }
            }
            return aliases
        }

        /// Resolves each `Probe` field (a `bound…` field through `resolveBound`) and prints `name: display`.
        void t8Probe(ref Ctx c) {
            var at: i64 = -1
            for i in 0..c.files[0].unit.members.length {
                switch c.files[0].unit.members[i] {
                    case .structDecl(let d): at = i
                    default: continue
                }
            }
            let r = TypeResolver { file: 0 }
            let decl = t8TakeDecl(ref c, from: 0, at: at, by: .none)
            var lines = List<string>()
            switch decl {
                case .structDecl(let d):
                    for m in d.value.members {
                        switch m {
                            case .fieldDecl(let f):
                                let name = t8Text(in c, from: 0, at: f.value.name)
                                var t = errorType()
                                if name.startsWith("bound") {
                                    switch f.value.type {
                                        case .namedType(let n): t = resolveBound(ref c, through: in r, for: in n.value)
                                        default: t = errorType()
                                    }
                                } else {
                                    t = resolve(ref c, through: in r, for: in f.value.type)
                                }
                                let shown = typeDisplay(ref c, by: t)
                                lines.append($"{name}: {shown}")
                            default:
                                continue
                        }
                    }
                default:
                    print("no Probe")
            }
            let back = t8TakeDecl(ref c, from: 0, at: at, by: move decl)
            for l in lines { print(l) }
        }

        void t8Run(string section, by string text) {
            var c = Ctx { files: List<SourceFile>(), modules: List<Mod>(), diagnostics: List<Diag>(), bags: List<DiagBag>() }
            c.files.append(makeFile(text))
            var files = List<FileId>()
            files.append(0)
            c.modules.append(Mod { name: "t8", files: move files })
            let aliases = t8Declare(ref c)
            print($"== {section}")
            t8Probe(ref c)
            let r = TypeResolver { file: 0 }
            for a in aliases {
                let t = expandAlias(ref c, through: in r, for: a)
            }
            print($"== {section} diagnostics")
            for d in c.diagnostics {
                let help = d.help == "" ? "" : $" help: {d.help}"
                print($"{d.code} {lineCol(text, at: d.span.start)} {d.message}{help}")
            }
        }

        i32 main() {

        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    private static string LineCol(string text, int offset)
    {
        int line = 1, col = 1;
        for (var i = 0; i < offset; i++)
            if (text[i] == '\n') { line++; col = 1; }
            else col++;
        return $"{line}:{col}";
    }

    private static (List<string> Fields, List<string> Diagnostics) Oracle(string name)
    {
        var text = Fixtures.Single(f => f.Name == name).Text.Replace("\r", "");
        var c = Compilation.Create([SourceText.FromString("t8.byn", text)], Path.GetTempPath(), new CompilationOptions(Library: true));
        var probe = c.Modules.SelectMany(m => m.Structs).SingleOrDefault(s => s.Name == "Probe")
            ?? throw new InvalidOperationException(string.Join("\n", c.Diagnostics.Items.Select(d => $"{d.Code} {LineCol(text, d.Span.Start)} {d.Message}")));
        var fields = probe.Fields.Select(f => $"{f.Name}: {f.Type.DisplayName}").ToList();
        // BYN2822 and BYN3001 are the field checks of the struct pass, BYN6003 the effect analysis (a stub here).
        var diagnostics = c.Diagnostics.Items.Where(d => d.Code is not ("BYN2822" or "BYN3001" or "BYN6003"))
            .Select(d => $"{d.Code} {LineCol(text, d.Span.Start)} {d.Message}" + (string.IsNullOrEmpty(d.Help) ? "" : $" help: {d.Help}")).ToList();
        return (fields, diagnostics);
    }

    private static void Compare(string name)
    {
        var (fields, diagnostics) = Oracle(name);
        SelfhostSemanticsHarness.AssertSection(Sections.Value, name, fields);
        var printed = Sections.Value[$"{name} diagnostics"].Order(StringComparer.Ordinal).ToList();
        if (diagnostics.Count == 0)
        {
            Assert.Empty(printed);
            return;
        }
        SelfhostSemanticsHarness.AssertSection(new() { [name] = printed }, name, diagnostics.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Named() => Compare("named");

    [Fact]
    public void Errors() => Compare("errors");

    [Fact]
    public void Compose() => Compare("compose");
}
