using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <param name="Library">A library has no <c>main</c>, so none is required.</param>
/// <param name="SkipOwnershipCheck">Test-only: skips the static ownership check, so the debug-build checker can be exercised.</param>
public sealed record CompilationOptions(bool IncludeTests = false, bool Library = false, bool SkipOwnershipCheck = false);

/// <summary>
/// A whole program: the entry module plus every module it imports, transitively. Construction
/// parses, declares and binds everything; read <see cref="Diagnostics"/> afterwards.
/// </summary>
public sealed class Compilation
{
    private readonly Dictionary<string, ModuleSymbol> _modulesByDir = new(StringComparer.Ordinal);
    private readonly List<ModuleSymbol> _loading = new();
    private uint _nextErrorCode = 1;

    private Compilation(string packageRoot, CompilationOptions options, string? displayRoot)
    {
        PackageRoot = packageRoot;
        Options = options;
        DisplayRoot = displayRoot ?? packageRoot;
    }

    public string PackageRoot { get; }

    /// <summary>Imported files are named relative to this (the project root, in a project).</summary>
    public string DisplayRoot { get; }

    public CompilationOptions Options { get; }

    public DiagnosticBag Diagnostics { get; } = new();

    /// <summary>Every module, dependencies before dependents.</summary>
    public List<ModuleSymbol> Modules { get; } = new();

    public ModuleSymbol EntryModule { get; private set; } = null!;

    /// <summary>The function <c>Panic.onPanic</c> is set to in <c>main</c>, if any.</summary>
    public FunctionSymbol? OnPanic { get; internal set; }

    public Builtins Builtins { get; } = new();

    /// <summary>Time spent parsing the entry module and its imports.</summary>
    public TimeSpan ParseTime { get; private set; }

    /// <summary>Time spent declaring and binding (zero after syntax errors).</summary>
    public TimeSpan CheckTime { get; private set; }

    /// <summary>Error variants in code order (index = code - 1).</summary>
    public List<ErrorVariantSymbol> ErrorVariants { get; } = new();

    /// <summary>
    /// Compiles a file or a directory. A file's module is its directory, but only that file is
    /// read unless <paramref name="wholeDirectory"/> is set.
    /// </summary>
    public static Compilation FromPath(string path, CompilationOptions? options = null, bool wholeDirectory = true)
    {
        options ??= new CompilationOptions();
        var full = Path.GetFullPath(path);
        var isDir = Directory.Exists(full);
        var dir = isDir ? full : Path.GetDirectoryName(full)!;
        var files = isDir || wholeDirectory ? ModuleFiles(dir, options.IncludeTests) : [full];
        if (!isDir && !files.Contains(full)) files = [.. files, full];
        var sources = files.Select(f => SourceText.FromFile(f)).ToList();
        return Create(sources, dir, options);
    }

    /// <summary>
    /// Compiles a project (a directory with <c>package.byn</c>): the entry module is
    /// <c>src/app/</c> for an app and <c>src/&lt;name&gt;/</c> for a library, imports resolve under
    /// <c>src/</c>, and with tests every <c>tests/*.test.byn</c> joins the entry module. Files are
    /// named relative to the project root.
    /// </summary>
    public static Compilation FromProject(string root, string entryModule, CompilationOptions options)
    {
        var src = Path.Combine(root, "src");
        var entry = Path.Combine(src, entryModule);
        var files = Directory.Exists(entry) ? ModuleFiles(entry, options.IncludeTests).ToList() : [];
        var tests = Path.Combine(root, "tests");
        if (options.IncludeTests && Directory.Exists(tests))
            files.AddRange(Directory.EnumerateFiles(tests, "*.test.byn").Order(StringComparer.Ordinal));
        var sources = files.Select(f => new SourceText(Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes(f))).ToList();
        return Create(sources, src, options, displayRoot: root);
    }

    /// <summary>Compiles <paramref name="entryFiles"/> as the entry module; imports resolve under <paramref name="packageRoot"/>.</summary>
    public static Compilation Create(IReadOnlyList<SourceText> entryFiles, string packageRoot, CompilationOptions? options = null, string? displayRoot = null)
    {
        var c = new Compilation(packageRoot, options ?? new CompilationOptions(), displayRoot);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        c.EntryModule = c.LoadModule("main", packageRoot, entryFiles, null);
        c.ParseTime = clock.Elapsed;
        // Syntax errors stop here: binding a broken tree mostly reports their echoes.
        if (c.Diagnostics.HasErrors) return c;
        clock.Restart();
        c.Declare();
        c.BindAll();
        // Each case binds and checks the same body; report what it finds there once.
        if (c._testCases.Count > 0) c.Diagnostics.RemoveRepeats();
        c.CheckTime = clock.Elapsed;
        return c;
    }

    private static string[] ModuleFiles(string dir, bool includeTests) =>
        Directory.EnumerateFiles(dir, "*.byn")
                 .Where(f => includeTests || !f.EndsWith(".test.byn", StringComparison.Ordinal))
                 .Order(StringComparer.Ordinal)
                 .ToArray();

    // ── Loading ──────────────────────────────────────────────────────────────────

    private ModuleSymbol LoadModule(string name, string dir, IReadOnlyList<SourceText> sources, ModuleSymbol? importer)
    {
        var module = new ModuleSymbol(name, dir) { CName = CNameOf(name) };
        _modulesByDir[dir] = module;
        _loading.Add(module);
        foreach (var source in sources)
        {
            var tree = SyntaxTree.Parse(source);
            Diagnostics.AddRange(tree.Diagnostics);
            module.Files.Add(new FileContext(module, tree));
        }
        foreach (var file in module.Files.Where(f => !f.Tree.Diagnostics.Any(d => d.IsError)))
        {
            foreach (var import in file.Tree.Root.Members.OfType<ImportDecl>())
                ResolveImport(file, import);
        }
        _loading.Remove(module);
        Modules.Add(module);
        return module;
    }

    private void ResolveImport(FileContext file, ImportDecl import)
    {
        var name = string.Join(".", import.Path.Select(p => p.Text));
        var span = TextSpan.FromBounds(import.Path[0].Span.Start, import.Path[^1].Span.End);
        var dir = Path.Combine([PackageRoot, .. import.Path.Select(p => p.Text)]);
        var alias = (import.Alias ?? import.Path[^1]).Text;
        ModuleSymbol? module;
        if (_modulesByDir.TryGetValue(dir, out module))
        {
            if (_loading.Contains(module))
            {
                var chain = string.Join(" → ", _loading.SkipWhile(m => m != module).Select(m => m.Name).Append(module.Name));
                Diagnostics.Report(DiagnosticCodes.ImportCycle, file.Source, span, [chain],
                    help: "move the shared declarations into a module both can import");
                return;
            }
        }
        else
        {
            var files = Directory.Exists(dir) ? ModuleFiles(dir, Options.IncludeTests) : [];
            if (files.Length == 0)
            {
                Diagnostics.Report(DiagnosticCodes.UnknownModule, file.Source, span, [name],
                    help: $"put the module's `.byn` files in `{Path.GetRelativePath(PackageRoot, dir)}/` next to the entry file; a module is a directory");
                return;
            }
            var sources = files.Select(f => new SourceText(Path.GetRelativePath(DisplayRoot, f).Replace('\\', '/'), File.ReadAllBytes(f)));
            module = LoadModule(name, dir, sources.ToList(), file.Module);
        }
        if (!file.Imports.TryAdd(alias, module))
            Diagnostics.Report(DiagnosticCodes.DuplicateDeclaration, file.Source, span, [alias]);
    }

    private static string CNameOf(string moduleName) =>
        new string(moduleName.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray());

    // ── Declarations ─────────────────────────────────────────────────────────────

    private void Declare()
    {
        DeclareBuiltinVariants();
        foreach (var module in Modules)
            foreach (var file in module.Files)
                DeclareFile(file);
        foreach (var module in Modules)
            foreach (var set in module.ErrorSets)
                DeclareVariants(set);
        foreach (var module in Modules)
            foreach (var e in module.Enums)
                DeclareEnumMembers(e);
        foreach (var module in Modules)
            foreach (var u in module.Unions)
                DeclareUnionVariants(u);
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs) DeclareMembers(s);
            foreach (var f in module.AllFunctions.Where(f => f.Owner is null && !f.IsInstance && f.Kind != FunctionKind.Main && f.Kind != FunctionKind.Test).ToList())
                DeclareSignature(f, f.Decl!, f.File!);
        }
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs) CheckStructShape(s);
            foreach (var u in module.Unions) CheckUnionShape(u);
        }
        if (EntryModule.Main is { Kind: FunctionKind.Function } main) CheckMainSignature(main);
    }

    /// <summary><c>IoError</c> is numbered first, so the runtime can return its codes 1 to 7 directly; <c>Utf8Error</c> follows (8).</summary>
    private void DeclareBuiltinVariants()
    {
        foreach (var set in new[] { Builtins.IoError, Builtins.Utf8Error })
            foreach (var v in set.Decl.Variants)
            {
                var sym = new ErrorVariantSymbol(v.Name.Text, new Location(set.Location.Source, v.Name.Span), set, _nextErrorCode++, v.Doc);
                set.Variants.Add(sym);
                ErrorVariants.Add(sym);
            }
    }

    /// <summary><c>void</c> or <c>i32 main()</c>, optionally with <c>ReadOnlySpan&lt;string&gt; args</c> and <c>fails</c> (BYN2771).</summary>
    private void CheckMainSignature(FunctionSymbol main)
    {
        var returns = main.ReturnType;
        var returnOk = returns.IsVoid || returns.IsError || returns == PrimitiveType.I32;
        var paramsOk = main.Parameters.Count == 0
            || main.Parameters is [{ Modifier: ParamModifier.None } p]
                && (p.Type.IsError || p.Type is SpanType { ReadOnly: true, Element: PrimitiveType { IsString: true } });
        if (!returnOk || !paramsOk)
            Report(DiagnosticCodes.InvalidMainSignature, main.File!.Source, main.Decl!.Name.Span, [],
                help: "write `void main()`, `i32 main()`, `void main(ReadOnlySpan<string> args)` or `i32 main(ReadOnlySpan<string> args)`, each optionally with `fails`");
    }

    /// <summary><c>[Cases]</c> belongs on a test with parameters and <c>[ExitCode]</c> on an error variant; no other declaration takes an attribute.</summary>
    private void CheckAttributes(FileContext file, Decl decl)
    {
        var hasCases = false;
        foreach (var a in decl.Attributes)
        {
            if (a.Name.Text == "Cases" && decl is TestDecl test)
            {
                if (hasCases)
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[Cases]"], help: "combine the cases into one `[Cases(...)]`");
                else
                    _testCases[test] = CheckCases(file, test, a);
                hasCases = true;
            }
            else if (a.Name.Text == "Cases")
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[Cases]"], help: "put `[Cases(...)]` on a test that takes parameters");
            else if (a.Name.Text == "ExitCode")
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[ExitCode]"], help: "put `[ExitCode]` on an error enum variant");
            else
                Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text],
                    help: "use `[ExitCode(n)]` on an error variant or `[Cases(...)]` on a test");
        }
        if (decl is TestDecl { Params.Length: > 0 } t && !hasCases)
        {
            var span = TextSpan.FromBounds(t.Params[0].Span.Start, t.Params[^1].Span.End);
            Report(DiagnosticCodes.ModifierNotAllowed, file.Source, span, [file.Source.GetText(span)],
                label: "a test without `[Cases]` takes no parameters",
                help: "add `[Cases(...)]` with one value per parameter for each case, or remove the parameters");
        }
        if (decl is StructDecl s)
            foreach (var m in s.Members) CheckAttributes(file, m);
    }

    /// <summary>
    /// The cases of <c>[Cases(...)]</c> that fit <paramref name="test"/>: a tuple per case, or a bare value when it
    /// takes one parameter, with one literal per parameter (BYN2773, BYN2774).
    /// </summary>
    private List<Expr[]> CheckCases(FileContext file, TestDecl test, AttributeSyntax a)
    {
        var cases = new List<Expr[]>();
        var names = string.Join(", ", test.Params.Select(p => p.Name.Text));
        foreach (var arg in a.Args)
        {
            if (arg.Value is NameExpr or MemberExpr)
            {
                Report(DiagnosticCodes.CasesNotConstant, file.Source, arg.Value.Span, [], label: "cases from a const list are not supported yet",
                    help: "write each case as a tuple literal, such as `[Cases((0, false), (1, true))]`");
                continue;
            }
            Expr[] values = arg.Value is TupleExpr tuple ? tuple.Elements.Select(e => e.Value).ToArray() : [arg.Value];
            if (values.Length != test.Params.Length)
            {
                Report(DiagnosticCodes.CasesArity, file.Source, arg.Value.Span, [values.Length, test.Params.Length],
                    help: test.Params.Length == 1 ? "give one value per case: `[Cases(1, 2, 3)]`"
                        : $"give each case one value per parameter, in order: `({names})`");
                continue;
            }
            var constant = true;
            foreach (var v in values.Where(v => !IsCaseLiteral(v)))
            {
                Report(DiagnosticCodes.CasesNotConstant, file.Source, v.Span, [],
                    help: "write the value as a literal: a number, string, char, `true`, `false`, `null` or a `.case`");
                constant = false;
            }
            if (constant) cases.Add(values);
        }
        return cases;
    }

    private static bool IsCaseLiteral(Expr e) => e switch
    {
        LiteralExpr or TargetMemberExpr => true,
        UnaryExpr { Op: UnaryOp.Negate or UnaryOp.Plus, Operand: LiteralExpr { Kind: LiteralKind.Integer or LiteralKind.Float } } => true,
        ParenExpr p => IsCaseLiteral(p.Inner),
        _ => false,
    };

    private void DeclareTest(FileContext file, TestDecl t, string title, Expr[]? values)
    {
        var fn = new FunctionSymbol("test", Loc(file, t), file.Module, FunctionKind.Test) { File = file };
        file.Module.Tests.Add(new TestSymbol(title, Loc(file, t), fn));
        file.Module.AllFunctions.Add(fn);
        _testBodies[fn] = (t, values);
    }

    /// <summary><c>quantity bounds (input: 101, valid: false)</c>: the title, then each parameter with its value as written.</summary>
    private static string CaseTitle(FileContext file, TestDecl t, Expr[] values) =>
        $"{t.Title} ({string.Join(", ", t.Params.Zip(values, (p, v) => p.Name.Text + ": " + file.Source.GetText(v.Span)))})";

    private void DeclareFile(FileContext file)
    {
        var module = file.Module;
        List<GlobalStatement>? mainStatements = null;
        foreach (var decl in file.Tree.Root.Members)
        {
            CheckAttributes(file, decl);
            switch (decl)
            {
                case StructDecl s:
                {
                    var sym = new StructSymbol(s.Name.Text, Loc(file, s.Name), module, AccessOf(s.Modifiers), s, file);
                    if (AddType(file, s.Name, sym)) module.Structs.Add(sym);
                    Naming.CheckType(this, file.Source, s.Name);
                    sym.TypeMap = DeclareTypeParameters(file, s.TypeParams, sym.TypeParameters);
                    break;
                }
                case ErrorEnumDecl e:
                {
                    var sym = new ErrorSetSymbol(e.Name.Text, Loc(file, e.Name), module, AccessOf(e.Modifiers), e);
                    if (AddType(file, e.Name, sym)) module.ErrorSets.Add(sym);
                    Naming.CheckType(this, file.Source, e.Name);
                    break;
                }
                case EnumDecl e:
                {
                    var sym = new EnumSymbol(e.Name.Text, Loc(file, e.Name), module, AccessOf(e.Modifiers), e, file);
                    if (AddType(file, e.Name, sym)) module.Enums.Add(sym);
                    Naming.CheckType(this, file.Source, e.Name);
                    break;
                }
                case UnionDecl u:
                {
                    var sym = new UnionSymbol(u.Name.Text, Loc(file, u.Name), module, AccessOf(u.Modifiers), u, file);
                    if (AddType(file, u.Name, sym)) module.Unions.Add(sym);
                    Naming.CheckType(this, file.Source, u.Name);
                    sym.TypeMap = DeclareTypeParameters(file, u.TypeParams, sym.TypeParameters);
                    break;
                }
                case TypeAliasDecl a:
                {
                    var sym = new AliasPlaceholder(a, a.Name, a.Modifiers, file);
                    if (AddType(file, a.Name, sym)) _aliases.Add(sym);
                    Naming.CheckType(this, file.Source, a.Name);
                    break;
                }
                case ErrorSetDecl s:
                {
                    var sym = new AliasPlaceholder(s, s.Name, s.Modifiers, file);
                    if (AddType(file, s.Name, sym)) _aliases.Add(sym);
                    Naming.CheckType(this, file.Source, s.Name);
                    break;
                }
                case FunctionDecl f:
                {
                    var sym = new FunctionSymbol(f.Name.Text, Loc(file, f.Name), module, FunctionKind.Function)
                    {
                        Access = AccessOf(f.Modifiers), File = file, Decl = f,
                    };
                    Naming.CheckValue(this, file.Source, f.Name);
                    sym.TypeMap = DeclareTypeParameters(file, f.TypeParams, sym.TypeParameters);
                    if (f.Name.Text == "main" && file.Module == EntryModule)
                    {
                        if (module.Main is not null)
                        {
                            Report(DiagnosticCodes.DuplicateMain, file.Source, f.Name.Span, []);
                            break;
                        }
                        module.Main = sym;
                    }
                    AddFunction(file, f.Name, sym);
                    break;
                }
                case GlobalStatement { Statement: LocalDecl { Kind: BindingKind.Const } c }:
                {
                    var sym = new ConstSymbol(c.Name.Text, Loc(file, c.Name), module, c, file);
                    if (CheckUnique(file, c.Name))
                    {
                        module.Constants[c.Name.Text] = sym;
                        module.AllConstants.Add(sym);
                    }
                    Naming.CheckValue(this, file.Source, c.Name);
                    break;
                }
                case GlobalStatement g:
                    if (module != EntryModule || file.IsTestFile)
                        Report(DiagnosticCodes.StatementOutsideEntry, file.Source, g.Statement.Span, []);
                    else
                        (mainStatements ??= new()).Add(g);
                    break;
                case TestDecl t:
                    if (!Options.IncludeTests) break;
                    if (t.Params.Length == 0)
                        DeclareTest(file, t, t.Title, null);
                    else if (_testCases.TryGetValue(t, out var cases))
                        foreach (var values in cases) DeclareTest(file, t, CaseTitle(file, t, values), values);
                    break;
                case ImportDecl:
                    break;
            }
        }
        if (mainStatements is not null)
        {
            if (module.Main is { Kind: FunctionKind.Function } explicitMain)
            {
                Report(DiagnosticCodes.DuplicateMain, file.Source, mainStatements[0].Span, [],
                    label: $"`main` is also declared on line {explicitMain.Location!.Value.Line}");
            }
            else if (module.Main is null)
            {
                var main = new FunctionSymbol("main", Loc(file, mainStatements[0]), module, FunctionKind.Main) { File = file };
                module.Main = main;
                module.AllFunctions.Add(main);
                _mainStatements = (main, mainStatements);
            }
            else
            {
                Report(DiagnosticCodes.StatementOutsideEntry, file.Source, mainStatements[0].Span, [],
                    help: "keep top-level statements in one file of the entry module");
            }
        }
    }

    private readonly List<AliasPlaceholder> _aliases = new();
    private readonly List<(ErrorVariantSymbol Variant, AttributeSyntax Attribute, FileContext File)> _exitCodes = new();
    private readonly Dictionary<FunctionSymbol, (TestDecl Decl, Expr[]? Values)> _testBodies = new();
    private readonly Dictionary<TestDecl, List<Expr[]>> _testCases = new();
    private (FunctionSymbol Main, List<GlobalStatement> Statements)? _mainStatements;

    /// <summary>Declares the <c>&lt;A, B&gt;</c> of a generic declaration; the map is null when there are none.</summary>
    private Dictionary<string, TypeSymbol>? DeclareTypeParameters(FileContext file, Name[] names, List<TypeParameterType> into)
    {
        if (names.Length == 0) return null;
        var map = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        var seen = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            Naming.CheckType(this, file.Source, name);
            if (seen.TryGetValue(name.Text, out var previous))
            {
                ReportDuplicate(file.Source, name, previous);
                continue;
            }
            seen[name.Text] = Loc(file, name);
            var parameter = new TypeParameterType(name.Text);
            into.Add(parameter);
            map[name.Text] = parameter;
        }
        return map;
    }

    private bool AddType(FileContext file, Name name, TypeSymbol type)
    {
        if (!CheckUnique(file, name)) return false;
        file.Module.Types[name.Text] = type;
        return true;
    }

    private void AddFunction(FileContext file, Name name, FunctionSymbol f)
    {
        if (!CheckUnique(file, name)) return;
        file.Module.Functions[name.Text] = [f];
        file.Module.AllFunctions.Add(f);
    }

    private bool CheckUnique(FileContext file, Name name)
    {
        var m = file.Module;
        Location? previous = null;
        if (m.Types.TryGetValue(name.Text, out var t)) previous = LocationOf(t);
        else if (m.Functions.TryGetValue(name.Text, out var fs)) previous = fs[0].Location;
        else if (m.Constants.TryGetValue(name.Text, out var c)) previous = c.Location;
        if (previous is null) return true;
        ReportDuplicate(file.Source, name, previous.Value);
        return false;
    }

    internal void ReportDuplicate(SourceText source, Name name, Location previous)
    {
        var where = previous.Source == source ? $"line {previous.Line}" : $"{previous.Source.Path}:{previous.Line}";
        Report(DiagnosticCodes.DuplicateDeclaration, source, name.Span, [name.Text], label: $"already declared on {where}");
    }

    internal static Location? LocationOf(TypeSymbol t) => t switch
    {
        StructSymbol s => s.Location,
        ErrorSetSymbol e => e.Location,
        EnumSymbol e => e.Location,
        UnionSymbol u => u.Location,
        AliasPlaceholder a => new Location(a.File.Source, a.Name.Span),
        _ => null,
    };

    private static Location Loc(FileContext file, Node node) => new(file.Source, node.Span);

    internal static Accessibility AccessOf(Modifiers m) =>
        m.HasFlag(Modifiers.Public) ? Accessibility.Public :
        m.HasFlag(Modifiers.Private) ? Accessibility.Private : Accessibility.Module;

    private void DeclareVariants(ErrorSetSymbol set)
    {
        var file = set.Module.Files.First(f => f.Tree.Root.Members.Contains(set.Decl));
        foreach (var v in set.Decl.Variants)
        {
            Naming.CheckValue(this, file.Source, v.Name);
            if (set.Find(v.Name.Text) is { } existing)
            {
                ReportDuplicate(file.Source, v.Name, existing.Location!.Value);
                continue;
            }
            var sym = new ErrorVariantSymbol(v.Name.Text, Loc(file, v.Name), set, _nextErrorCode++, v.Doc);
            set.Variants.Add(sym);
            ErrorVariants.Add(sym);
            var seen = false;
            foreach (var a in v.Attributes)
            {
                if (a.Name.Text == "Cases")
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[Cases]"], help: "put `[Cases(...)]` on a test that takes parameters");
                else if (a.Name.Text != "ExitCode")
                    Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text], help: "use `[ExitCode(n)]`; it is the only attribute an error variant takes");
                else if (seen)
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[ExitCode]"], help: "remove the repeated attribute");
                else
                {
                    seen = true;
                    _exitCodes.Add((sym, a, file));
                }
            }
        }
        if (set.Variants.Count == 0)
            Report(DiagnosticCodes.EmptyErrorSet, file.Source, set.Decl.Name.Span, [set.Name]);
    }

    private void DeclareEnumMembers(EnumSymbol e)
    {
        var source = e.File.Source;
        DeclareEnumFields(e);
        foreach (var m in e.Decl.Members)
        {
            Naming.CheckValue(this, source, m.Name);
            if (e.Find(m.Name.Text) is { } existing)
            {
                Report(DiagnosticCodes.DuplicateEnumMember, source, m.Name.Span, [e.Name, m.Name.Text],
                    label: $"already declared on line {existing.Location!.Value.Line}");
                continue;
            }
            e.Members.Add(new EnumMemberSymbol(m.Name.Text, Loc(e.File, m.Name), e, e.Members.Count, m.Doc));
        }
        if (e.Members.Count == 0)
            Report(DiagnosticCodes.EmptyEnum, source, e.Decl.Name.Span, [e.Name], help: "add the members, or remove the enum");
        var optional = new OptionalType(e);
        e.FromOrdinal = new FunctionSymbol("fromOrdinal", e.Location, e.Module, FunctionKind.Builtin)
        {
            Builtin = BuiltinFunction.EnumFromOrdinal, ReturnType = optional, Access = Accessibility.Public, IsStatic = true,
        };
        e.FromOrdinal.Parameters.Add(new ParameterSymbol("ordinal", null, PrimitiveType.I64, ParamModifier.None, 0));
        e.Parse = new FunctionSymbol("parse", e.Location, e.Module, FunctionKind.Builtin)
        {
            Builtin = BuiltinFunction.EnumParse, ReturnType = optional, Access = Accessibility.Public, IsStatic = true,
        };
        e.Parse.Parameters.Add(new ParameterSymbol("name", null, PrimitiveType.String, ParamModifier.None, 0));
    }

    /// <summary>The parameters of <c>enum Name(T x, …)</c>: numbers, <c>bool</c>, <c>char</c>, <c>string</c> or a plain enum (BYN2724).</summary>
    private void DeclareEnumFields(EnumSymbol e)
    {
        if (e.Decl.Params is null) return;
        var source = e.File.Source;
        var types = new TypeResolver(this, e.File, null);
        foreach (var p in e.Decl.Params)
        {
            Naming.CheckValue(this, source, p.Name);
            var type = types.Resolve(p.Type);
            const string help = "use a number type, `bool`, `char`, `string` or a plain enum; enum data is stored in static tables";
            if (p.Modifier != ParamModifier.None)
                Report(DiagnosticCodes.InvalidEnumParameter, source, p.Span, [p.Name.Text], label: $"`{p.Modifier.ToString().ToLowerInvariant()}` is not allowed here", help: help);
            else if (p.Default is not null)
                Report(DiagnosticCodes.InvalidEnumParameter, source, p.Default.Span, [p.Name.Text], label: "a default value is not allowed here", help: help);
            else if (!type.IsError && type is not (PrimitiveType { IsNumeric: true } or PrimitiveType { Kind: PrimitiveKind.Bool or PrimitiveKind.Char or PrimitiveKind.String } or EnumSymbol { Decl.Params: null }))
                Report(DiagnosticCodes.InvalidEnumParameter, source, p.Type.Span, [p.Name.Text], label: $"`{type.DisplayName}` has no static form", help: help);
            else if (e.FindField(p.Name.Text) is { } existing)
                Report(DiagnosticCodes.DuplicateEnumMember, source, p.Name.Span, [e.Name, p.Name.Text],
                    label: $"already declared on line {existing.Location!.Value.Line}");
            else if (p.Name.Text is "name" or "ordinal")
                Report(DiagnosticCodes.DuplicateEnumMember, source, p.Name.Span, [e.Name, p.Name.Text],
                    label: "every enum has it", help: "rename the parameter");
            else
            {
                e.Fields.Add(new EnumFieldSymbol(p.Name.Text, Loc(e.File, p.Name), e, type, e.Fields.Count));
                continue;
            }
            // Kept with an error type so the arguments still line up and report nothing more.
            e.Fields.Add(new EnumFieldSymbol(p.Name.Text, Loc(e.File, p.Name), e, ErrorType.Instance, e.Fields.Count));
        }
    }

    private void DeclareUnionVariants(UnionSymbol u)
    {
        var file = u.File;
        var types = new TypeResolver(this, file, null, u.TypeMap);
        var values = new Dictionary<int, UnionVariantSymbol>();
        var tag = 0;
        foreach (var v in u.Decl.Variants)
        {
            Naming.CheckValue(this, file.Source, v.Name);
            if (u.Find(v.Name.Text) is { } existing)
            {
                Report(DiagnosticCodes.DuplicateUnionVariant, file.Source, v.Name.Span, [u.Name, v.Name.Text],
                    label: $"already declared on line {existing.Location!.Value.Line}");
                continue;
            }
            if (v.Value is { } valueSyntax)
            {
                if (v.Fields.Length > 0)
                    Report(DiagnosticCodes.UnionValueOnPayload, file.Source, valueSyntax.Span, [v.Name.Text],
                        label: "only a case without data can have a value", help: "remove the value");
                else if (UnionValue(valueSyntax) is { } value)
                    tag = value;
                else
                    Report(DiagnosticCodes.UnionValueNotInteger, file.Source, valueSyntax.Span, [],
                        label: "not an `i32` integer literal", help: "write the value as an integer literal, or remove it");
            }
            var sym = new UnionVariantSymbol(v.Name.Text, Loc(file, v.Name), u, tag, v.Doc);
            if (values.TryGetValue(tag, out var same))
                Report(DiagnosticCodes.DuplicateUnionValue, file.Source, v.Value?.Span ?? v.Name.Span, [same.Name, v.Name.Text, tag],
                    label: v.Value is null ? $"follows `{u.Variants[^1].Name}`, so it is {tag}" : "already used",
                    help: "give each case its own value");
            else
                values[tag] = sym;
            tag++;
            var names = new Dictionary<string, Location>(StringComparer.Ordinal);
            foreach (var f in v.Fields)
            {
                Naming.CheckValue(this, file.Source, f.Name);
                if (names.TryGetValue(f.Name.Text, out var prev))
                {
                    ReportDuplicate(file.Source, f.Name, prev);
                    continue;
                }
                names[f.Name.Text] = Loc(file, f.Name);
                sym.Fields.Add(new ParameterSymbol(f.Name.Text, Loc(file, f.Name), types.Resolve(f.Type), ParamModifier.None, sym.Fields.Count));
            }
            u.Variants.Add(sym);
        }
        if (u.Variants.Count == 0)
            Report(DiagnosticCodes.EmptyUnion, file.Source, u.Decl.Name.Span, [u.Name], help: "add the variants, or remove the union");
    }

    /// <summary>The value of <c>read = 1</c>: an integer literal, optionally negated, that fits the <c>i32</c> tag.</summary>
    private static int? UnionValue(Expr e)
    {
        var negate = e is UnaryExpr { Op: UnaryOp.Negate };
        if ((negate ? ((UnaryExpr)e).Operand : e) is not LiteralExpr { Kind: LiteralKind.Integer, Suffix: null } lit) return null;
        var n = Binder.ParseInteger(lit.Text);
        if (negate) n = -n;
        return n >= int.MinValue && n <= int.MaxValue ? (int)n : null;
    }

    private void DeclareMembers(StructSymbol s)
    {
        var file = s.File;
        var types = new TypeResolver(this, file, s, s.TypeMap);
        var names = new Dictionary<string, Location>(StringComparer.Ordinal);
        bool Unique(Name n)
        {
            if (names.TryGetValue(n.Text, out var prev))
            {
                ReportDuplicate(file.Source, n, prev);
                return false;
            }
            names[n.Text] = Loc(file, n);
            return true;
        }
        foreach (var member in s.Decl.Members)
        {
            switch (member)
            {
                case FieldDecl f:
                {
                    Naming.CheckValue(this, file.Source, f.Name);
                    RejectModifiers(file, f.Span, f.Modifiers, Modifiers.Static | Modifiers.Const, "fields");
                    var field = new FieldSymbol(f.Name.Text, Loc(file, f.Name), s, AccessOf(f.Modifiers), f) { Type = types.Resolve(f.Type) };
                    if (Unique(f.Name)) s.Fields.Add(field);
                    if (field.IsRequired && f.Default is not null)
                        Report(DiagnosticCodes.RequiredWithDefault, file.Source, f.Name.Span, [f.Name.Text]);
                    break;
                }
                case PropertyDecl p:
                {
                    Naming.CheckValue(this, file.Source, p.Name);
                    var prop = new PropertySymbol(p.Name.Text, Loc(file, p.Name), s, AccessOf(p.Modifiers), p) { Type = types.Resolve(p.Type) };
                    var getter = new FunctionSymbol(p.Name.Text, prop.Location, s.Module, FunctionKind.Getter)
                    {
                        Owner = s, File = file, ReturnType = prop.Type, Access = prop.Access, TypeMap = s.TypeMap,
                    };
                    prop.Getter = getter;
                    s.Module.AllFunctions.Add(getter);
                    if (Unique(p.Name)) s.Properties.Add(prop);
                    break;
                }
                case FunctionDecl m:
                {
                    Naming.CheckValue(this, file.Source, m.Name);
                    var typeMap = s.TypeMap;
                    if (m.TypeParams.Length > 0)
                    {
                        Report(DiagnosticCodes.Unsupported, file.Source, m.TypeParams[0].Span, ["generic methods"],
                            help: "declare a generic free function, or make the struct generic");
                        // Declared anyway, so the signature reports nothing more.
                        var own = DeclareTypeParameters(file, m.TypeParams, new List<TypeParameterType>())!;
                        typeMap = s.TypeMap is null ? own : new Dictionary<string, TypeSymbol>(s.TypeMap.Concat(own.Where(p => !s.TypeMap.ContainsKey(p.Key))));
                    }
                    var fn = new FunctionSymbol(m.Name.Text, Loc(file, m.Name), s.Module, FunctionKind.Method)
                    {
                        Owner = s, File = file, Decl = m, Access = AccessOf(m.Modifiers), IsStatic = m.Modifiers.HasFlag(Modifiers.Static),
                        TypeMap = typeMap,
                    };
                    DeclareSignature(fn, m, file, s);
                    if (Unique(m.Name)) s.Methods.Add(fn);
                    s.Module.AllFunctions.Add(fn);
                    break;
                }
                case ConstructorDecl c:
                {
                    var fn = new FunctionSymbol(s.Name, Loc(file, c.Name), s.Module, FunctionKind.Constructor)
                    {
                        Owner = s, File = file, Access = AccessOf(c.Modifiers), ReturnType = s, TypeMap = s.TypeMap,
                    };
                    DeclareParameters(fn, c.Params, file, types);
                    if (s.Constructors.Count > 0)
                        Report(DiagnosticCodes.Unsupported, file.Source, c.Name.Span, ["overloaded constructors"]);
                    else
                        s.Constructors.Add(fn);
                    s.Module.AllFunctions.Add(fn);
                    _constructorBodies[fn] = c;
                    break;
                }
                default:
                    Report(DiagnosticCodes.Unsupported, file.Source, member.Span, ["this kind of struct member"]);
                    break;
            }
        }
    }

    private readonly Dictionary<FunctionSymbol, ConstructorDecl> _constructorBodies = new();

    private void RejectModifiers(FileContext file, TextSpan span, Modifiers actual, Modifiers rejected, string what)
    {
        if ((actual & rejected) != 0)
            Report(DiagnosticCodes.Unsupported, file.Source, span, [$"`{(actual & rejected).ToString().ToLowerInvariant()}` on {what}"]);
    }

    private void DeclareSignature(FunctionSymbol fn, FunctionDecl decl, FileContext file, StructSymbol? owner = null)
    {
        var types = new TypeResolver(this, file, owner, fn.TypeMap);
        fn.ReturnType = types.Resolve(decl.ReturnType, allowVoid: true);
        fn.IsMutating = decl.RefThis is not null;
        if (decl.RefThis is { } refThis && fn is not { Kind: FunctionKind.Method, IsStatic: false })
            Report(DiagnosticCodes.Unsupported, file.Source, refThis, [fn.IsStatic ? "`ref this` on a static method" : "`ref this` outside a struct"],
                help: "remove `ref this`; only an instance method has a `this` to change");
        DeclareParameters(fn, decl.Params, file, types);
        if (decl.Fails is { } fails)
        {
            fn.Fails = true;
            if (fails.Errors.Length == 0)
            {
                fn.InfersErrors = true;
                if (fn.Access == Accessibility.Public)
                    Report(DiagnosticCodes.PublicInferredFails, file.Source, fails.Span, [fn.Name]);
            }
            foreach (var e in fails.Errors)
            {
                var t = types.Resolve(e);
                if (t is ErrorSetSymbol or ErrorUnionType)
                {
                    foreach (var set in t is ErrorUnionType u ? u.Sets : [(ErrorSetSymbol)t])
                        if (!fn.ErrorSets.Contains(set)) fn.ErrorSets.Add(set);
                }
                else if (!t.IsError)
                {
                    Report(DiagnosticCodes.NotAnErrorSet, file.Source, e.Span, [t.DisplayName]);
                }
            }
        }
    }

    private void DeclareParameters(FunctionSymbol fn, Param[] parameters, FileContext file, TypeResolver types)
    {
        foreach (var p in parameters)
        {
            Naming.CheckValue(this, file.Source, p.Name);
            var sym = new ParameterSymbol(p.Name.Text, Loc(file, p.Name), types.Resolve(p.Type), p.Modifier, fn.Parameters.Count) { Syntax = p };
            if (fn.Parameters.Any(q => q.Name == sym.Name))
                ReportDuplicate(file.Source, p.Name, fn.Parameters.First(q => q.Name == sym.Name).Location!.Value);
            fn.Parameters.Add(sym);
        }
    }

    /// <summary>Recursive by-value containment, borrowed fields (BYN3001).</summary>
    private void CheckStructShape(StructSymbol s)
    {
        foreach (var f in s.Fields)
        {
            if (f.Type.IsBorrow)
            {
                Report(DiagnosticCodes.BorrowField, s.File.Source, f.Decl.Type.Span, [f.Type.DisplayName],
                    help: f.Type.IsString
                        ? "store an index or an owned copy instead; storing borrows in structs arrives in a later version"
                        : "store a `List` copied with `.copy(to: alloc)` instead; storing borrows in structs arrives in a later version");
            }
        }
        CheckRecursion(s, s.Name, s.File.Source, s.Decl.Name.Span);
    }

    private void CheckUnionShape(UnionSymbol u)
    {
        foreach (var v in u.Variants)
        {
            foreach (var f in v.Fields)
            {
                if (f.Type.IsBorrow)
                    Report(DiagnosticCodes.BorrowPayload, u.File.Source, f.Location!.Value.Span, [f.Type.DisplayName],
                        help: "store a `List` copied with `.copy(to: alloc)` instead");
            }
        }
        CheckRecursion(u, u.Name, u.File.Source, u.Decl.Name.Span);
    }

    /// <summary>Structs and unions already reported as part of a cycle, so a cycle is reported once.</summary>
    private readonly HashSet<TypeSymbol> _inReportedCycle = new();

    /// <summary>By-value containment of itself (BYN2761); <c>Owned</c> and <c>List</c> break a cycle.</summary>
    private void CheckRecursion(TypeSymbol t, string name, SourceText source, TextSpan span)
    {
        if (_inReportedCycle.Contains(t) || Cycle(t, t, new HashSet<TypeSymbol>()) is not { } path) return;
        _inReportedCycle.UnionWith(path);
        Report(DiagnosticCodes.RecursiveByValue, source, span, [name, string.Join(" → ", path.Prepend(t).Select(p => p.DisplayName))],
            help: $"store the inner one as `Owned<{name}>` (or in a `List<{name}>`); a value cannot contain itself");
    }

    /// <summary>The structs and unions on a by-value path from <paramref name="t"/> back to <paramref name="target"/>, ending with it; null if there is none.</summary>
    private static List<TypeSymbol>? Cycle(TypeSymbol t, TypeSymbol target, HashSet<TypeSymbol> seen)
    {
        IEnumerable<TypeSymbol> parts = t switch
        {
            StructSymbol s => s.Fields.Select(f => f.Type),
            UnionSymbol u => u.Variants.SelectMany(v => v.Fields).Select(f => f.Type),
            OptionalType o => [o.Inner],
            ArrayType a => [a.Element],
            TupleType tt => tt.Fields.Select(f => f.Type),
            _ => [],
        };
        foreach (var p in parts)
        {
            if (p == target) return [p];
            if (!seen.Add(p) || Cycle(p, target, seen) is not { } rest) continue;
            if (p is StructSymbol or UnionSymbol) rest.Insert(0, p);
            return rest;
        }
        return null;
    }

    // ── Bodies ───────────────────────────────────────────────────────────────────

    private void BindAll()
    {
        foreach (var module in Modules)
            foreach (var c in module.AllConstants)
                BindConstant(c);
        foreach (var module in Modules)
            foreach (var e in module.Enums)
                new Binder(this, e.File, null, null).BindEnumArguments(e);
        foreach (var (variant, attribute, file) in _exitCodes)
            variant.ExitCode = new Binder(this, file, null, null).BindExitCode(attribute);
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs)
            {
                foreach (var f in s.Fields.Where(f => f.Decl.Default is not null))
                    f.Default = new Binder(this, s.File, null, s).BindFieldDefault(f);
            }
        }
        foreach (var module in Modules)
        {
            foreach (var fn in module.AllFunctions.Where(f => !f.IsInstance).ToList())
                BindFunction(fn);
        }
        while (_instantiationWork.TryDequeue(out var work))
            Instantiating(work.Site, work.Span, work.Bind);
        InferErrorSets();
        CheckMutation();
        foreach (var module in Modules)
            foreach (var fn in module.AllFunctions)
            {
                // Ownership is checked once, on the template, where every type parameter counts as unique.
                if (fn.Body is not null && !fn.IsInstance && !Options.SkipOwnershipCheck) OwnershipAnalysis.Analyze(this, fn);
                // Like ownership, the lint judges the source once, on the template.
                if (fn.Body is not null && !fn.IsInstance) NeverReassignedVarLint.Analyze(this, fn);
                if (fn.Body is not null && !fn.IsInstance) PanicHandlerCheck.Analyze(this, fn);
                if (fn.Body is not null && !fn.IsGeneric)
                {
                    if (fn.IsInstance)
                    {
                        AnalyzeInstance(fn);
                        continue;
                    }
                    FlowAnalysis.Analyze(this, fn);
                    RegionAnalysis.Analyze(this, fn);
                    SpanParameterLint.Analyze(this, fn);
                }
            }
        if (!Diagnostics.HasErrors && _instantiationErrors.FirstOrDefault() is { } internalError)
            throw new InvalidOperationException(
                $"internal error: instantiating a generic reported {internalError.Code} at {internalError.Source?.Path}: {internalError.Message}");
        if (EntryModule.Main is null && !Options.IncludeTests && !Options.Library && EntryModule.Files.Count > 0 && !Diagnostics.HasErrors)
            Report(DiagnosticCodes.NoMain, EntryModule.Files[0].Source, new TextSpan(0, 0), []);
    }

    // ── Generics ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<(object Generic, TypeSymbol[] Args), object> _instances = new(new InstanceKeyComparer());
    private readonly Queue<(SourceText Site, TextSpan Span, Action Bind, FunctionSymbol? Instance)> _instantiationWork = new();
    private readonly List<Diagnostic> _instantiationErrors = new();
    private readonly HashSet<(string, SourceText?, TextSpan)> _instanceAnalysisSeen = new();
    private DiagnosticBag? _scratch;

    /// <summary>Marks an instantiation error reported at the use that chose the type arguments.</summary>
    internal const string InstantiationLabel = "the generic code needs this of its type arguments";

    private sealed class InstanceKeyComparer : IEqualityComparer<(object Generic, TypeSymbol[] Args)>
    {
        public bool Equals((object Generic, TypeSymbol[] Args) x, (object Generic, TypeSymbol[] Args) y) =>
            ReferenceEquals(x.Generic, y.Generic) && x.Args.SequenceEqual(y.Args);

        public int GetHashCode((object Generic, TypeSymbol[] Args) key)
        {
            var hash = new HashCode();
            hash.Add(key.Generic);
            foreach (var a in key.Args) hash.Add(a);
            return hash.ToHashCode();
        }
    }

    private static Dictionary<string, TypeSymbol> Substitution(IReadOnlyDictionary<string, TypeSymbol>? outer, List<TypeParameterType> parameters, TypeSymbol[] args)
    {
        var map = outer is null ? new Dictionary<string, TypeSymbol>(StringComparer.Ordinal) : new Dictionary<string, TypeSymbol>(outer, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++) map[parameters[i].Name] = args[i];
        return map;
    }

    /// <summary>
    /// Runs instantiation work, whose diagnostics are not the user's: the template already reported
    /// them. The exceptions are the rules only a type argument can break (a <c>Map</c> key, <c>==</c>
    /// and formatting on a <c>T</c>); they are reported at <paramref name="span"/>, the use that chose
    /// the type arguments.
    /// </summary>
    private void Instantiating(SourceText site, TextSpan span, Action work)
    {
        var outer = _scratch;
        var bag = _scratch = new DiagnosticBag();
        try { work(); }
        finally { _scratch = outer; }
        foreach (var d in bag.Items.Where(d => d.IsError))
        {
            if (d.Code == DiagnosticCodes.InvalidMapKey.Code || d.Code == DiagnosticCodes.BadBinaryOperands.Code || d.Code == DiagnosticCodes.NotFormattable.Code)
                (_scratch ?? Diagnostics).Add(d with { Source = site, Span = span, Label = InstantiationLabel });
            else
                _instantiationErrors.Add(d);
        }
    }

    /// <summary>The instantiation of a generic function at <paramref name="args"/>; declared now, bound when the worklist drains.</summary>
    internal FunctionSymbol InstantiateFunction(FunctionSymbol template, TypeSymbol[] args, SourceText site, TextSpan span)
    {
        if (_instances.TryGetValue((template, args), out var existing)) return (FunctionSymbol)existing;
        var fn = new FunctionSymbol(template.Name, template.Location, template.Module, template.Kind)
        {
            Access = template.Access, File = template.File, Decl = template.Decl, Owner = template.Owner, IsStatic = template.IsStatic,
            Template = template, TypeArguments = args, TypeMap = Substitution(template.Owner?.TypeMap, template.TypeParameters, args),
        };
        _instances[(template, args)] = fn;
        template.Module.AllFunctions.Add(fn);
        if (_firstPass is { } pass)
        {
            pass.Instances.Add(fn);
            var outer = ReportInto(pass.Diagnostics);
            var errors = _instantiationErrors.Count;
            try { Instantiating(site, span, () => DeclareSignature(fn, fn.Decl!, fn.File!, fn.Owner)); }
            finally { ReportInto(outer); }
            pass.Errors.AddRange(_instantiationErrors.Skip(errors));
            _instantiationErrors.RemoveRange(errors, _instantiationErrors.Count - errors);
        }
        else Instantiating(site, span, () => DeclareSignature(fn, fn.Decl!, fn.File!, fn.Owner));
        if (!fn.IsGeneric) _instantiationWork.Enqueue((site, span, () => BindFunction(fn), fn));
        return fn;
    }

    /// <summary>The instantiation of a generic struct or union at <paramref name="args"/>, with its members.</summary>
    internal TypeSymbol InstantiateType(TypeSymbol template, TypeSymbol[] args, SourceText site, TextSpan span)
    {
        if (_instances.TryGetValue((template, args), out var existing)) return (TypeSymbol)existing;
        switch (template)
        {
            case StructSymbol s:
            {
                var instance = new StructSymbol(s.Name, s.Location, s.Module, s.Access, s.Decl, s.File)
                {
                    Template = s, TypeArguments = args, TypeMap = Substitution(null, s.TypeParameters, args),
                };
                _instances[(s, args)] = instance;
                Instantiating(site, span, () => DeclareMembers(instance));
                if (instance.IsGeneric) return instance;
                foreach (var f in instance.Fields.Where(f => f.Decl.Default is not null))
                    _instantiationWork.Enqueue((site, span, () => FieldDefault(instance, f, site, span), null));
                foreach (var fn in instance.Methods.Concat(instance.Properties.Select(p => p.Getter!)).Concat(instance.Constructors))
                    _instantiationWork.Enqueue((site, span, () => BindFunction(fn), null));
                return instance;
            }
            case UnionSymbol u:
            {
                var instance = new UnionSymbol(u.Name, u.Location, u.Module, u.Access, u.Decl, u.File)
                {
                    Template = u, TypeArguments = args, TypeMap = Substitution(null, u.TypeParameters, args),
                };
                _instances[(u, args)] = instance;
                Instantiating(site, span, () => DeclareUnionVariants(instance));
                return instance;
            }
            default:
                throw new InvalidOperationException($"`{template.DisplayName}` is not generic");
        }
    }

    /// <summary>A field default of a struct; an instance's is bound on first use, as a literal can need it before the worklist drains.</summary>
    internal BoundExpr? FieldDefault(StructSymbol s, FieldSymbol f, SourceText site, TextSpan span)
    {
        if (f.Default is null && f.HasDefault && s.Template is not null)
            Instantiating(site, span, () => f.Default ??= new Binder(this, s.File, null, s).BindFieldDefault(f));
        return f.Default;
    }

    /// <summary>The template a function was instantiated from: itself for a generic function, its twin for a member of a generic struct.</summary>
    private static FunctionSymbol? TemplateOf(FunctionSymbol fn) =>
        fn.Template ?? (fn.Owner?.Template is { } owner ? owner.Methods.FirstOrDefault(m => m.Decl == fn.Decl && fn.Decl is not null) : null);

    /// <summary>Flow and region analysis of an instantiation; what two instantiations both find is reported once.</summary>
    private void AnalyzeInstance(FunctionSymbol fn)
    {
        var outer = _scratch;
        var bag = _scratch = new DiagnosticBag();
        try
        {
            FlowAnalysis.Analyze(this, fn);
            RegionAnalysis.Analyze(this, fn);
            SpanParameterLint.Analyze(this, fn);
        }
        finally { _scratch = outer; }
        foreach (var d in bag.Items)
            if (_instanceAnalysisSeen.Add((d.Code, d.Source, d.Span))) Diagnostics.Add(d);
    }

    internal void BindConstant(ConstSymbol c)
    {
        if (c.State == 2) return;
        if (c.State == 1)
        {
            Report(DiagnosticCodes.CircularConstant, c.File.Source, c.Decl.Name.Span, [c.Name]);
            c.Type = ErrorType.Instance;
            c.State = 2;
            return;
        }
        c.State = 1;
        new Binder(this, c.File, null, null).BindModuleConstant(c);
        c.State = 2;
    }

    /// <summary>
    /// Binds a body; when an untyped <c>var</c> literal in it meets another integer type first
    /// (spec R4), binds it again with that type, discarding the first pass.
    /// </summary>
    private void BindFunction(FunctionSymbol fn)
    {
        var checks = MutationChecks.Count;
        var locals = fn.Locals.Count;
        var outer = _firstPass;
        (List<FunctionSymbol> Instances, DiagnosticBag Diagnostics, List<Diagnostic> Errors) pass = ([], new DiagnosticBag(), []);
        _firstPass = pass;
        IReadOnlyDictionary<LocalDecl, TypeSymbol>? seeds;
        try { seeds = BindBody(fn, null); }
        finally { _firstPass = outer; }
        if (seeds is null)
        {
            outer?.Instances.AddRange(pass.Instances);
            ReportHeld(pass.Diagnostics.Items);
            _instantiationErrors.AddRange(pass.Errors);
            return;
        }
        // The functions the first pass instantiated go with it, and so do their diagnostics.
        foreach (var instance in pass.Instances)
        {
            _instances.Remove((instance.Template!, [.. instance.TypeArguments]));
            instance.Module.AllFunctions.Remove(instance);
        }
        var work = _instantiationWork.Where(w => w.Instance is null || !pass.Instances.Contains(w.Instance)).ToList();
        _instantiationWork.Clear();
        foreach (var w in work) _instantiationWork.Enqueue(w);
        MutationChecks.RemoveRange(checks, MutationChecks.Count - checks);
        fn.Locals.RemoveRange(locals, fn.Locals.Count - locals);
        BindBody(fn, seeds);
    }

    /// <summary>The function instances a first pass created, and what creating them reported.</summary>
    private (List<FunctionSymbol> Instances, DiagnosticBag Diagnostics, List<Diagnostic> Errors)? _firstPass;

    /// <summary>A first pass (no <paramref name="seeds"/>) returns the seeds a second pass needs, if any.</summary>
    private IReadOnlyDictionary<LocalDecl, TypeSymbol>? BindBody(FunctionSymbol fn, IReadOnlyDictionary<LocalDecl, TypeSymbol>? seeds)
    {
        var file = fn.File!;
        Binder New(StructSymbol? owner)
        {
            var binder = new Binder(this, file, fn, owner, seeds: seeds);
            return seeds is null ? binder.Hold() : binder;
        }
        Binder binder;
        switch (fn.Kind)
        {
            case FunctionKind.Function or FunctionKind.Method when fn.Decl is { } decl:
                (binder = New(fn.Owner)).BindFunctionBody(decl);
                break;
            case FunctionKind.Getter:
            {
                var prop = fn.Owner!.Properties.First(p => p.Getter == fn);
                (binder = New(fn.Owner)).BindGetter(prop.Decl);
                break;
            }
            case FunctionKind.Constructor:
                (binder = New(fn.Owner)).BindConstructor(_constructorBodies[fn]);
                break;
            case FunctionKind.Main when _mainStatements is { } m && m.Main == fn:
                (binder = New(null)).BindMain(m.Statements);
                break;
            case FunctionKind.Test:
                (binder = New(null)).BindTest(_testBodies[fn].Decl, _testBodies[fn].Values);
                break;
            default:
                return null;
        }
        return seeds is null ? binder.Release() : null;
    }

    /// <summary>Inferred <c>fails</c> sets: the union of what each body propagates, to a fixed point.</summary>
    private void InferErrorSets()
    {
        var inferred = Modules.SelectMany(m => m.AllFunctions).Where(f => f.InfersErrors || f.Kind is FunctionKind.Main or FunctionKind.Test).ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var fn in inferred)
            {
                if (fn.IsInstance && TemplateOf(fn) is { } template)
                {
                    foreach (var set in template.ErrorSets.Where(set => !fn.ErrorSets.Contains(set)).ToList())
                    {
                        fn.ErrorSets.Add(set);
                        changed = true;
                    }
                    continue;
                }
                if (fn.Body is null) continue;
                foreach (var set in ErrorFlow.Propagated(fn.Body))
                {
                    if (fn.ErrorSets.Contains(set)) continue;
                    fn.ErrorSets.Add(set);
                    changed = true;
                }
            }
        } while (changed);
        foreach (var fn in inferred.Where(f => f.Kind is FunctionKind.Main or FunctionKind.Test && f.ErrorSets.Count > 0))
            fn.Fails = true;
    }

    /// <summary>Calls of methods through a receiver, checked once every body is bound.</summary>
    internal List<MutationCheck> MutationChecks { get; } = new();

    /// <summary>The first write to <c>this</c> in each method not declared <c>ref this</c>, with the binder that saw it.</summary>
    internal Dictionary<FunctionSymbol, (Binder Binder, TextSpan Span)> ThisWrites { get; } = new();

    /// <summary>
    /// A method is mutating when it is declared <c>ref this</c>. Every call of one needs a writable
    /// receiver, and a method that writes <c>this</c> without <c>ref this</c> is BYN2115.
    /// </summary>
    private void CheckMutation()
    {
        foreach (var m in MutationChecks.Where(m => m.Callee.IsMutating))
        {
            if (m.Binder.Function is not { IsInstance: true }) m.Binder.CheckWritable(m.Receiver, m.Receiver.Syntax.Span, $"`{m.Callee.Name}` modifies it");
            else m.Binder.CheckGuardWrite(m.Receiver, m.Receiver.Syntax.Span, $"`{m.Callee.Name}` modifies it");
        }
        foreach (var (fn, (binder, span)) in ThisWrites)
            binder.ReportMutatesWithoutRefThis(fn, span);
    }

    /// <summary>A parameter's default value, bound on first use.</summary>
    internal BoundExpr? ParameterDefault(FunctionSymbol fn, ParameterSymbol p)
    {
        if (p.Default is null && p.Syntax?.Default is not null)
            p.Default = new Binder(this, fn.File!, null, fn.Owner, fn.TypeMap).BindParameterDefault(p);
        return p.Default;
    }

    /// <summary>
    /// The sets a call of <paramref name="fn"/> can fail with; empty (any error) while an inferred
    /// set is still being computed.
    /// </summary>
    internal IReadOnlyList<ErrorSetSymbol> ErrorSetsOf(FunctionSymbol fn) =>
        fn.InfersErrors || fn.Kind is FunctionKind.Main or FunctionKind.Test ? [] : fn.ErrorSets;

    internal void Report(DiagnosticDescriptor d, SourceText source, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null) =>
        (_scratch ?? Diagnostics).Report(d, source, span, args, label, help, fix);

    /// <summary>Reports what a first-pass binder held back.</summary>
    internal void ReportHeld(IEnumerable<Diagnostic> held) => (_scratch ?? Diagnostics).AddRange(held);

    /// <summary>Sends reports to <paramref name="bag"/> until called again; returns the previous target.</summary>
    internal DiagnosticBag? ReportInto(DiagnosticBag? bag)
    {
        var outer = _scratch;
        _scratch = bag;
        return outer;
    }

    internal IEnumerable<AliasPlaceholder> Aliases => _aliases;
}

/// <summary>A <c>type X = T;</c> alias or an <c>error set S = A, B;</c>; resolved to its target on first use.</summary>
internal sealed record MutationCheck(Binder Binder, FunctionSymbol Callee, BoundExpr Receiver);

internal sealed class AliasPlaceholder(Decl decl, Name name, Modifiers modifiers, FileContext file) : TypeSymbol
{
    public Decl Decl { get; } = decl;

    public Name Name { get; } = name;

    public Modifiers Modifiers { get; } = modifiers;

    public FileContext File { get; } = file;

    public TypeSymbol? Target { get; set; }

    public bool Resolving { get; set; }

    public override string DisplayName => Name.Text;
}
