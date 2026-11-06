using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <param name="Library">A library has no <c>main</c>, so none is required.</param>
/// <param name="SkipOwnershipCheck">Test-only: skips the static ownership check, so the debug-build checker can be exercised.</param>
/// <param name="Capabilities">The package's <c>capabilities</c>, the upper bound on its effects (§13); null for no bound.</param>
/// <param name="PackageName">The package's name, for BYN6004.</param>
/// <param name="Target">The build target <c>Target.os</c>/<c>Target.arch</c> name; null for the compiling host.</param>
/// <param name="Std">Test-only: the std modules <c>import std.…</c> reads; null for <see cref="StdLibrary.Shipped"/>.</param>
public sealed record CompilationOptions(bool IncludeTests = false, bool Library = false, bool SkipOwnershipCheck = false,
    IReadOnlyList<string>? Capabilities = null, string? PackageName = null, string? Target = null, StdLibrary? Std = null);

/// <summary>An <c>@unobservable</c> mark: the attributed statement's span, the operator it narrows to (null for all) and the reason.</summary>
public sealed record UnobservableMark(string File, TextSpan Span, string? Operator, string Reason);

/// <summary>
/// A whole program: the entry module plus every module it imports, transitively. Construction
/// parses, declares and binds everything; read <see cref="Diagnostics"/> afterwards.
/// </summary>
public sealed partial class Compilation
{
    /// <summary>The operator names <c>@unobservable(&lt;op&gt;)</c> accepts (revision 0014 §3).</summary>
    public static readonly IReadOnlyList<string> MutationOperators =
        ["boundary", "relational", "logical", "condition", "bool", "arithmetic", "literal", "negation", "statement", "emptyReturn", "variant"];

    private readonly List<UnobservableMark> _unobservable = new();
    private readonly HashSet<(string, TextSpan, string?)> _unobservableSeen = new();

    /// <summary>Every <c>@unobservable</c> mark, in binding order.</summary>
    public IReadOnlyList<UnobservableMark> Unobservable => _unobservable;

    internal void AddUnobservable(UnobservableMark mark)
    {
        if (_unobservableSeen.Add((mark.File, mark.Span, mark.Operator))) _unobservable.Add(mark);
    }

    private readonly Dictionary<string, ModuleSymbol> _modulesByDir = new(StringComparer.Ordinal);
    private readonly List<ModuleSymbol> _loading = new();
    private uint _nextErrorCode = 1;
    private IReadOnlyList<SourceText> _entryFiles = [];
    /// <summary>Files read from this text instead of from disk (<see cref="WithFile"/>).</summary>
    private Dictionary<string, SourceText>? _replaced;

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

    /// <summary>Whether <c>==</c> applies to values of <paramref name="t"/>.</summary>
    public static bool IsEquatable(TypeSymbol t) => Binder.IsEquatable(t);

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
    public static Compilation Create(IReadOnlyList<SourceText> entryFiles, string packageRoot, CompilationOptions? options = null, string? displayRoot = null) =>
        Create(entryFiles, packageRoot, options, displayRoot, null);

    /// <summary>
    /// The same program compiled again with <paramref name="text"/> in place of the file of the same path, entry or
    /// imported: spec 013 re-binds and ownership-checks each mutant this way.
    /// </summary>
    public Compilation WithFile(SourceText text)
    {
        var replaced = _replaced is null ? new Dictionary<string, SourceText>(StringComparer.Ordinal) : new Dictionary<string, SourceText>(_replaced, StringComparer.Ordinal);
        replaced[text.Path] = text;
        var entry = _entryFiles.Select(f => f.Path == text.Path ? text : f).ToList();
        return Create(entry, PackageRoot, Options, DisplayRoot, replaced);
    }

    private static Compilation Create(IReadOnlyList<SourceText> entryFiles, string packageRoot, CompilationOptions? options, string? displayRoot,
        Dictionary<string, SourceText>? replaced)
    {
        var c = new Compilation(packageRoot, options ?? new CompilationOptions(), displayRoot) { _entryFiles = entryFiles, _replaced = replaced };
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
        // `std.a.b` is the shipped module `a/b/` (spec 016); the package's own `std/` is never read for it.
        var std = import.Path[0].Text == "std";
        var stdPath = string.Join("/", import.Path.Skip(1).Select(p => p.Text));
        var dir = std ? "<std>/" + stdPath : Path.Combine([PackageRoot, .. import.Path.Select(p => p.Text)]);
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
        else if (std)
        {
            var library = Options.Std ?? StdLibrary.Shipped;
            var stdFiles = import.Path.Length > 1 ? library.Module(stdPath) : null;
            if (stdFiles is null)
            {
                Diagnostics.Report(DiagnosticCodes.UnknownStdModule, file.Source, span, [name],
                    help: library.Names.Count == 0 ? "remove the import: no std modules ship with this build"
                        : $"import one of {string.Join(", ", library.Names.Select(n => $"`{n}`"))}");
                return;
            }
            module = LoadModule(name, dir, stdFiles.Select(f => new SourceText($"{dir}/{f.File}", f.Bytes)).ToList(), file.Module);
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
            var sources = files.Select(f => (Path: Path.GetRelativePath(DisplayRoot, f).Replace('\\', '/'), File: f))
                .Select(f => _replaced?.GetValueOrDefault(f.Path) ?? new SourceText(f.Path, File.ReadAllBytes(f.File)));
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
        // Before any signature names a `union error … except .c`, whose cases must already exist.
        foreach (var module in Modules)
            foreach (var set in module.ErrorSets)
                DeclareVariants(set);
        DeclareBuiltinVariants(Builtins.ParseError);
        // Bounds are resolved once every type is declared, so `where T : IOrder<T>` can name `T` and later types.
        foreach (var where in _wheres) where();
        DeclareInterfaceMembers(Builtins.IEnumerator);
        DeclareInterfaceMembers(Builtins.IIndexable);
        foreach (var module in Modules)
            foreach (var i in module.Interfaces)
                DeclareInterfaceMembers(i);
        _interfaceMembersDeclared = true;
        foreach (var (instance, site, span) in _pendingInterfaces)
            Instantiating(site, span, () => DeclareInterfaceMembers(instance));
        foreach (var alias in _aliases)
            if (alias.Decl is ErrorSetDecl composed) CheckComposition(alias, composed);
        foreach (var module in Modules)
            foreach (var e in module.Enums)
                DeclareEnumMembers(e);
        // `Range<i64>` is not a type name, so `Rule`'s cases are declared here rather than resolved.
        foreach (var v in Builtins.Rule.Decl.Variants)
        {
            var sym = new UnionVariantSymbol(v.Name.Text, new Location(Builtins.RuleFile.Source, v.Name.Span), Builtins.Rule, Builtins.Rule.Variants.Count, v.Doc);
            foreach (var f in v.Fields)
                sym.Fields.Add(new ParameterSymbol(f.Name.Text, new Location(Builtins.RuleFile.Source, f.Name.Span), new RangeType(PrimitiveType.I64), ParamModifier.None, sym.Fields.Count));
            Builtins.Rule.Variants.Add(sym);
        }
        foreach (var module in Modules)
            foreach (var u in module.Unions)
                DeclareUnionVariants(u);
        foreach (var module in Modules)
            foreach (var set in module.ErrorSets)
                DeclarePayloads(set);
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs) DeclareMembers(s);
            foreach (var s in module.Structs) CheckStateNames(s);
            foreach (var s in module.Structs) Naming.CheckMembers(this, s);
            foreach (var f in module.AllFunctions.Where(f => f.Owner is null && !f.IsInstance && f.Kind != FunctionKind.Main && f.Kind != FunctionKind.Test).ToList())
                DeclareSignature(f, f.Decl!, f.File!);
            CheckOverloads(module);
        }
        MembersDeclared = true;
        foreach (var check in _pendingKeys) check();
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs) CheckStructShape(s);
            foreach (var u in module.Unions) CheckUnionShape(u);
        }
        if (EntryModule.Main is { Kind: FunctionKind.Function } main) CheckMainSignature(main);
    }

    /// <summary><c>IoError</c> is numbered first, so the runtime can return its codes 1 to 7 directly; <c>Utf8Error</c> follows (8).</summary>
    private void DeclareBuiltinVariants(params ErrorSetSymbol[] sets)
    {
        foreach (var set in sets.Length > 0 ? sets : [Builtins.IoError, Builtins.Utf8Error])
            foreach (var v in set.Decl.Variants)
            {
                var sym = new ErrorVariantSymbol(v.Name.Text, new Location(set.Location.Source, v.Name.Span), set, _nextErrorCode++, v.Doc);
                // A built-in payload is a primitive: `invalidSequence(i64 at)`.
                foreach (var f in v.Fields)
                    sym.Fields.Add(new ParameterSymbol(f.Name.Text, new Location(set.Location.Source, f.Name.Span),
                        PrimitiveType.ByName[((NamedType)f.Type).Path[0].Text], ParamModifier.None, sym.Fields.Count));
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

    /// <summary><c>@cases</c> belongs on a test with parameters, <c>@exitCode</c> on an error variant and <c>@sealed</c> and <c>@positional</c>, bare, on a struct; nothing else takes an attribute.</summary>
    private void CheckAttributes(FileContext file, Decl decl)
    {
        var hasCases = false;
        foreach (var a in decl.Attributes)
        {
            if (a.Name.Text == "cases" && decl is TestDecl test)
            {
                if (hasCases)
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@cases"], help: "combine the cases into one `@cases(...)`");
                else
                    _testCases[test] = CheckCases(file, test, a);
                hasCases = true;
            }
            else if (a.Name.Text == "cases")
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@cases"], help: "put `@cases(...)` on a test that takes parameters");
            else if (a.Name.Text == "exitCode")
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@exitCode"], help: "put `@exitCode` on an error enum variant");
            else if (a.Name.Text == "sealed" && decl is not StructDecl)
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@sealed"], help: "put `@sealed` on a struct");
            else if (a.Name.Text == "sealed" && (a.Args.Length > 0 || a.Because is not null))
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, [file.Source.GetText(a.Span)], help: "write `@sealed` with no arguments and no `because`");
            else if (a.Name.Text == "sealed") { }
            else if (a.Name.Text == "positional" && decl is not StructDecl)
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@positional"], help: "put `@positional` on a struct");
            else if (a.Name.Text == "positional" && (a.Args.Length > 0 || a.Because is not null))
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, [file.Source.GetText(a.Span)], help: "write `@positional` with no arguments and no `because`");
            else if (a.Name.Text == "positional" && decl is StructDecl { States.Length: > 0 })
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@positional"], help: "remove `@positional`: a struct with states is built with state literals");
            else if (a.Name.Text == "positional")
                Positional.Add((StructDecl)decl);
            else
                Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text],
                    help: "use `@exitCode(n)` on an error variant or `@cases(...)` on a test");
        }
        if (decl is TestDecl { Params.Length: > 0 } t && !hasCases)
        {
            var span = TextSpan.FromBounds(t.Params[0].Span.Start, t.Params[^1].Span.End);
            Report(DiagnosticCodes.ModifierNotAllowed, file.Source, span, [file.Source.GetText(span)],
                label: "a test without `@cases` takes no parameters",
                help: "add `@cases(...)` with one value per parameter for each case, or remove the parameters");
        }
        if (decl is StructDecl s)
            foreach (var m in s.Members) CheckAttributes(file, m);
    }

    /// <summary>
    /// The cases of <c>@cases(...)</c> that fit <paramref name="test"/>: a tuple per case, or a bare value when it
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
                    help: "write each case as a tuple literal, such as `@cases((0, false), (1, true))`");
                continue;
            }
            Expr[] values = arg.Value is TupleExpr tuple ? tuple.Elements.Select(e => e.Value).ToArray() : [arg.Value];
            if (values.Length != test.Params.Length)
            {
                Report(DiagnosticCodes.CasesArity, file.Source, arg.Value.Span, [values.Length, test.Params.Length],
                    help: test.Params.Length == 1 ? "give one value per case: `@cases(1, 2, 3)`"
                        : $"give each case one value per parameter, in order: `({names})`");
                continue;
            }
            var constant = true;
            foreach (var v in values.Where(v => !IsCaseLiteral(v)))
            {
                Report(DiagnosticCodes.CasesNotConstant, file.Source, v.Span, [],
                    help: "write the value as a literal: a number, string, rune, `true`, `false`, `null` or a `.case`");
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
                    var fields = s.Members.OfType<FieldDecl>().ToArray();
                    if (s.TypeParams.Length == 0 && (s.Rules.Length > 0 || fields.Any(f => f.Requires.Length > 0)))
                    {
                        // J23: `T.Field`, reachable only through `T`, so it is not added to the module's types. Not for a
                        // generic struct, whose `requires` is BYN2022 (Question 225).
                        var members = fields.Select(f => new EnumMember(f.Name.Span, f.Name, null, null)).ToArray();
                        var fieldDecl = new EnumDecl(s.Name.Span, s.Modifiers, new Name(s.Name.Span, s.Name.Text + "_Field"), null, members);
                        sym.FieldEnum = new EnumSymbol(fieldDecl.Name.Text, Loc(file, s.Name), module, AccessOf(s.Modifiers), fieldDecl, file) { FieldsOf = sym };
                        module.Enums.Add(sym.FieldEnum);
                    }
                    sym.TypeMap = DeclareTypeParameters(file, s.TypeParams, sym.TypeParameters);
                    _wheres.Add(() => DeclareWhere(file, s.Where, sym.TypeParameters, sym.TypeMap, s.Name.Text));
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
                    RejectBases(file, e.Bases, e.Name.Text, "an enum");
                    _wheres.Add(() => DeclareWhere(file, e.Where, [], null, e.Name.Text));
                    break;
                }
                case UnionDecl u:
                {
                    var sym = new UnionSymbol(u.Name.Text, Loc(file, u.Name), module, AccessOf(u.Modifiers), u, file);
                    if (AddType(file, u.Name, sym)) module.Unions.Add(sym);
                    Naming.CheckType(this, file.Source, u.Name);
                    sym.TypeMap = DeclareTypeParameters(file, u.TypeParams, sym.TypeParameters);
                    RejectBases(file, u.Bases, u.Name.Text, "a union");
                    _wheres.Add(() => DeclareWhere(file, u.Where, sym.TypeParameters, sym.TypeMap, u.Name.Text));
                    break;
                }
                case InterfaceDecl i:
                {
                    var sym = new InterfaceSymbol(i.Name.Text, Loc(file, i.Name), module, AccessOf(i.Modifiers), file) { Decl = i };
                    if (Builtins.FindInterface(i.Name.Text) is not null)
                        Report(DiagnosticCodes.DuplicateDeclaration, file.Source, i.Name.Span, [i.Name.Text], label: "already declared as a built-in interface");
                    else if (AddType(file, i.Name, sym))
                        module.Interfaces.Add(sym);
                    Naming.CheckInterface(this, file.Source, i.Name);
                    sym.TypeMap = DeclareTypeParameters(file, i.TypeParams, sym.TypeParameters);
                    _wheres.Add(() => DeclareWhere(file, i.Where, sym.TypeParameters, sym.TypeMap, i.Name.Text));
                    break;
                }
                case ExtensionDecl x:
                {
                    if (x.Bases.Length == 0)
                    {
                        Report(DiagnosticCodes.Unsupported, file.Source, x.Name.Span, ["`extension` declarations"]);
                        break;
                    }
                    var bases = string.Join(", ", x.Bases.Select(b => file.Source.GetText(b.Span)));
                    Report(DiagnosticCodes.ExtensionConformance, file.Source, TextSpan.FromBounds(x.Span.Start, x.Bases[^1].Span.End), [x.Name.Text, bases],
                        label: "retroactive conformance", help: $"list `{bases}` on `{x.Name.Text}`'s declaration");
                    break;
                }
                case TypeAliasDecl { Distinct: true } a:
                {
                    // J5: a nominal wrapper, a struct whose one field `value` carries the type's `requires`.
                    var value = new FieldDecl(a.Target.Span, Modifiers.Public, a.Target, new Name(a.Target.Span, "value"), null) { Requires = a.Requires };
                    var sym = new StructSymbol(a.Name.Text, Loc(file, a.Name), module, AccessOf(a.Modifiers), new StructDecl(a.Span, a.Modifiers, a.Name, [value]), file);
                    if (AddType(file, a.Name, sym))
                    {
                        module.Structs.Add(sym);
                        Distinct.Add(sym);
                    }
                    Naming.CheckType(this, file.Source, a.Name);
                    break;
                }
                case TypeAliasDecl a:
                {
                    if (a.Requires.Length > 0) Report(DiagnosticCodes.Unsupported, file.Source, a.Requires[0].Span, ["`requires` on a plain `type` alias"]);
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
                    _wheres.Add(() => DeclareWhere(file, f.Where, sym.TypeParameters, sym.TypeMap, f.Name.Text));
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
    private readonly List<Action> _wheres = new();
    private readonly List<(InterfaceSymbol Instance, SourceText Site, TextSpan Span)> _pendingInterfaces = new();
    private bool _interfaceMembersDeclared;
    private readonly Dictionary<(TypeParameterType Owner, string Name), TypeParameterType> _associatedTypes = new();

    /// <summary><c>T.Item</c> in a template: one type parameter per (T, name), so every mention is the same type.</summary>
    internal TypeParameterType AssociatedType(TypeParameterType owner, string name)
    {
        if (!_associatedTypes.TryGetValue((owner, name), out var t))
            _associatedTypes[(owner, name)] = t = new TypeParameterType($"{owner.Name}.{name}");
        return t;
    }

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

    /// <summary>Resolves the bounds of <c>where T : IA, IB</c> into the constraints of <paramref name="parameters"/>.</summary>
    private void DeclareWhere(FileContext file, WhereClause[] clauses, List<TypeParameterType> parameters, IReadOnlyDictionary<string, TypeSymbol>? typeMap, string owner)
    {
        if (clauses.Length == 0) return;
        var types = new TypeResolver(this, file, null, typeMap);
        foreach (var w in clauses)
        {
            if (parameters.Find(p => p.Name == w.Param.Text) is not { } parameter)
            {
                Report(DiagnosticCodes.WhereUnknownParameter, file.Source, w.Param.Span, [w.Param.Text], label: $"not a type parameter of `{owner}`",
                    help: parameters.Count == 0
                        ? $"remove the `where` clause; `{owner}` has no type parameters"
                        : $"name one of {string.Join(", ", parameters.Select(p => $"`{p.Name}`"))}");
                continue;
            }
            var constraints = parameter.Constraints.ToList();
            foreach (var bound in w.Bounds)
                if (ResolveInterface(types, file, bound) is { } i) constraints.Add(i);
            parameter.Constraints = constraints;
        }
    }

    /// <summary>A base or a <c>where</c> bound: an interface, or BYN2804.</summary>
    private InterfaceSymbol? ResolveInterface(TypeResolver types, FileContext file, NamedType syntax)
    {
        var t = types.ResolveBound(syntax);
        if (t is InterfaceSymbol i) return i;
        if (!t.IsError)
        {
            var kind = t switch
            {
                StructSymbol => "a struct", UnionSymbol => "a union", EnumSymbol => "an enum", ErrorSetSymbol => "an error enum",
                TypeParameterType => "a type parameter", _ => "a type",
            };
            Report(DiagnosticCodes.NotAnInterface, file.Source, syntax.Span, [t.DisplayName], label: kind, help: "list an `interface` here");
        }
        return null;
    }

    /// <summary>Interfaces listed on a union or enum (BYN2815): only structs conform yet.</summary>
    private void RejectBases(FileContext file, NamedType[] bases, string name, string kind)
    {
        if (bases.Length > 0)
            Report(DiagnosticCodes.NonStructConformance, file.Source, TextSpan.FromBounds(bases[0].Span.Start, bases[^1].Span.End), [name, kind],
                label: "listed here", help: "wrap it in a struct that conforms");
    }

    private bool AddType(FileContext file, Name name, TypeSymbol type)
    {
        if (!CheckUnique(file, name)) return false;
        file.Module.Types[name.Text] = type;
        return true;
    }

    private void AddFunction(FileContext file, Name name, FunctionSymbol f)
    {
        // Another function of the name is an overload, kept if its signature cannot overlap (CheckOverloads, J65).
        if (file.Module.Functions.TryGetValue(name.Text, out var overloads)) overloads.Add(f);
        else if (CheckUnique(file, name)) file.Module.Functions[name.Text] = [f];
        else return;
        file.Module.AllFunctions.Add(f);
    }

    /// <summary>Drops an overload that some call would match as well as an earlier one, reporting it as a duplicate (J65).</summary>
    private void CheckOverloads(ModuleSymbol module)
    {
        foreach (var overloads in module.Functions.Values)
            for (var i = 1; i < overloads.Count; i++)
            {
                if (overloads.Take(i).FirstOrDefault(o => Overlap(o, overloads[i])) is not { } earlier) continue;
                ReportDuplicate(overloads[i].File!.Source, overloads[i].Decl!.Name, earlier.Location!.Value);
                module.AllFunctions.Remove(overloads[i]);
                overloads.RemoveAt(i--);
            }
    }

    /// <summary>
    /// Whether one call could match both <paramref name="a"/> and <paramref name="b"/>: each names every label the
    /// other requires, and their counts of unlabeled arguments meet. Overloads differ by particle set (HANDOFF §24).
    /// </summary>
    internal static bool Overlap(FunctionSymbol a, FunctionSymbol b)
    {
        static string Label(ParameterSymbol p) => p.Syntax?.Particle?.Text ?? p.Name;
        static IEnumerable<string> Required(FunctionSymbol f) => f.Parameters.Where(p => p.Syntax is { Particle: not null, Default: null }).Select(Label);
        static (int Min, int Max) Positional(FunctionSymbol f) =>
            (f.Parameters.Count(p => p.Syntax is { Particle: null, Default: null, Rest: false }),
             f.Parameters.Any(p => p.Syntax?.Rest == true) ? int.MaxValue : f.Parameters.Count(p => p.Syntax is { Particle: null }));
        var shared = a.Parameters.Select(Label).Intersect(b.Parameters.Select(Label)).ToHashSet();
        var (pa, pb) = (Positional(a), Positional(b));
        return Required(a).Concat(Required(b)).All(shared.Contains) && pa.Min <= pb.Max && pb.Min <= pa.Max;
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
        InterfaceSymbol i => i.Location,
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
                if (a.Name.Text == "cases")
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@cases"], help: "put `@cases(...)` on a test that takes parameters");
                else if (a.Name.Text != "exitCode")
                    Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text], help: "use `@exitCode(n)`; it is the only attribute an error variant takes");
                else if (seen)
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["@exitCode"], help: "remove the repeated attribute");
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

    /// <summary>The payload fields of each case of <paramref name="set"/>, resolved once every type is declared.</summary>
    private void DeclarePayloads(ErrorSetSymbol set)
    {
        var file = set.Module.Files.First(f => f.Tree.Root.Members.Contains(set.Decl));
        var types = new TypeResolver(this, file, null);
        foreach (var v in set.Decl.Variants)
        {
            if (set.Find(v.Name.Text) is not { } sym || sym.Location!.Value.Span != v.Name.Span) continue;
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
                var type = types.Resolve(f.Type);
                if (FunctionTypeNotHere(type, file.Source, f.Type.Span, "an error payload")) type = ErrorType.Instance;
                sym.Fields.Add(new ParameterSymbol(f.Name.Text, Loc(file, f.Name), type, ParamModifier.None, sym.Fields.Count));
            }
        }
    }

    /// <summary>BYN3202 for two composed sets with a case of the same name.</summary>
    private void CheckComposition(AliasPlaceholder alias, ErrorSetDecl decl)
    {
        var sets = new TypeResolver(this, alias.File, null).ExpandAlias(alias) switch
        {
            ErrorUnionType u => u.Sets, ErrorSetSymbol set => [set], _ => [],
        };
        var owners = new Dictionary<string, ErrorSetSymbol>();
        foreach (var set in sets)
            foreach (var v in set.Variants)
                // Instantiations of one generic set are distinct components, matched by type (revision 0011).
                if (!owners.TryAdd(v.Name, set) && owners[v.Name].Decl != set.Decl)
                    Report(DiagnosticCodes.DuplicateErrorCase, alias.File.Source, decl.Head, [owners[v.Name].DisplayName, set.DisplayName, v.Name],
                        label: $"two cases `{v.Name}`", help: "rename one case, or compose fewer sets");
    }

    private void DeclareEnumMembers(EnumSymbol e)
    {
        var source = e.File.Source;
        DeclareEnumFields(e);
        foreach (var m in e.Decl.Members)
        {
            // The fields of `T.Field` were checked on the struct.
            if (e.FieldsOf is null) Naming.CheckValue(this, source, m.Name);
            if (e.Find(m.Name.Text) is { } existing)
            {
                if (e.FieldsOf is null)
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
            const string help = "use a number type, `bool`, `rune`, `string` or a plain enum; enum data is stored in static tables";
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
                var type = types.Resolve(f.Type);
                if (FunctionTypeNotHere(type, file.Source, f.Type.Span, "a union payload")) type = ErrorType.Instance;
                sym.Fields.Add(new ParameterSymbol(f.Name.Text, Loc(file, f.Name), type, ParamModifier.None, sym.Fields.Count));
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
        foreach (var b in s.Decl.Bases)
        {
            if (ResolveInterface(types, file, b) is not { } i) continue;
            if (s.Bases.Contains(i))
                Report(DiagnosticCodes.DuplicateConformance, file.Source, b.Span, [s.Name, i.DisplayName], label: "already listed",
                    help: $"remove the second `{i.DisplayName}`");
            else
            {
                s.Bases.Add(i);
                _baseSpans[(s, i)] = b.Span;
            }
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
                    if (FunctionTypeNotHere(prop.Type, file.Source, p.Type.Span, "a property")) prop.Type = ErrorType.Instance;
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
                    var ownParameters = new List<TypeParameterType>();
                    if (m.TypeParams.Length > 0)
                    {
                        Report(DiagnosticCodes.Unsupported, file.Source, m.TypeParams[0].Span, ["generic methods"],
                            help: "declare a generic free function, or make the struct generic");
                        // Declared anyway, so the signature reports nothing more.
                        var own = DeclareTypeParameters(file, m.TypeParams, ownParameters)!;
                        typeMap = s.TypeMap is null ? own : new Dictionary<string, TypeSymbol>(s.TypeMap.Concat(own.Where(p => !s.TypeMap.ContainsKey(p.Key))));
                    }
                    var fn = new FunctionSymbol(m.Name.Text, Loc(file, m.Name), s.Module, FunctionKind.Method)
                    {
                        Owner = s, File = file, Decl = m, Access = AccessOf(m.Modifiers), IsStatic = m.Modifiers.HasFlag(Modifiers.Static),
                        TypeMap = typeMap,
                    };
                    if (s.Template is null) DeclareWhere(file, m.Where, ownParameters, typeMap, m.Name.Text);
                    DeclareSignature(fn, m, file, s);
                    if (s.FindMethods(m.Name.Text).FirstOrDefault(o => Overlap(o, fn)) is { } earlier)
                        ReportDuplicate(file.Source, m.Name, earlier.Location!.Value);
                    else if (s.FindMethods(m.Name.Text).Any() || Unique(m.Name)) s.Methods.Add(fn);
                    s.Module.AllFunctions.Add(fn);
                    break;
                }
                case TypeAliasDecl a when s.Bases.Any(i => i.AssociatedTypes.Any(t => t.Name == a.Name.Text)):
                    Naming.CheckType(this, file.Source, a.Name);
                    // Resolved again if an earlier use did so quietly (AssociatedBinding), now reporting.
                    if (Unique(a.Name)) _associatedBindings[(s, a.Name.Text)] = types.Resolve(a.Target);
                    break;
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
        DeclareCheck(s);
    }

    private readonly Dictionary<TypeSymbol, ErrorSetSymbol> _validationErrors = new();

    /// <summary>
    /// <c>ValidationError&lt;T&gt;</c>: a set per <c>T</c>, numbered when first named. For a struct with <c>requires</c>, its
    /// <c>invalid</c> case carries <c>List&lt;Violation&lt;T&gt;&gt; violations</c> (J23); for any other <c>T</c> (a distinct
    /// type) it has no payload.
    /// </summary>
    public ErrorSetSymbol ValidationErrorOf(TypeSymbol t)
    {
        if (_validationErrors.TryGetValue(t, out var set)) return set;
        var template = Builtins.ValidationError;
        set = new ErrorSetSymbol(template.Name, template.Location, template.Module, template.Access, template.Decl) { Of = t };
        var v = template.Decl.Variants[0];
        var invalid = new ErrorVariantSymbol(v.Name.Text, new Location(template.Location.Source, v.Name.Span), set, _nextErrorCode++, v.Doc);
        if (t is StructSymbol { FieldEnum: { } fields } s)
        {
            var decl = Builtins.ViolationDecl;
            var source = Builtins.RuleFile.Source;
            var violation = new StructSymbol("Violation", new Location(source, decl.Name.Span), Builtins.Module, Accessibility.Public, decl, Builtins.RuleFile) { TypeArguments = [s] };
            foreach (var (f, type) in decl.Members.OfType<FieldDecl>().Zip<FieldDecl, TypeSymbol>([fields, Builtins.Rule]))
                violation.Fields.Add(new FieldSymbol(f.Name.Text, new Location(source, f.Name.Span), violation, Accessibility.Public, f) { Type = type });
            invalid.Fields.Add(new ParameterSymbol("violations", invalid.Location!.Value, new ListType(violation), ParamModifier.None, 0));
        }
        set.Variants.Add(invalid);
        ErrorVariants.Add(invalid);
        return _validationErrors[t] = set;
    }

    /// <summary>A struct with <c>requires</c> gets a check, run on each built value, that fails with <c>ValidationError&lt;T&gt;</c>.</summary>
    private void DeclareCheck(StructSymbol s)
    {
        var first = s.Decl.Members.OfType<FieldDecl>().SelectMany(f => f.Requires).Concat(s.Decl.Rules).MinBy(r => r.Span.Start);
        if (first is null || s.Template is not null) return;
        if (s.IsGeneric)
        {
            Report(DiagnosticCodes.Unsupported, s.File.Source, first.Span, ["`requires` on a generic struct"]);
            return;
        }
        var check = new FunctionSymbol("requires", s.Location, s.Module, FunctionKind.Method) { Owner = s, File = s.File, Access = s.Access, Fails = true };
        check.ErrorSets.Add(ValidationErrorOf(s));
        s.Module.AllFunctions.Add(check);
        Checks[s] = new StructChecks(check, []);
    }

    /// <summary>The states of each struct with a <c>states</c> block, as a synthetic union whose cases are the states; read by the backend.</summary>
    public Dictionary<StructSymbol, UnionSymbol> States { get; } = new();

    /// <summary>
    /// The states union of <paramref name="s"/>, or null when it has no <c>states</c> block. Declared on first use,
    /// so <c>T.s</c> resolves before the struct's members are declared.
    /// </summary>
    internal UnionSymbol? StatesOf(StructSymbol s)
    {
        if (s.Decl.States.Length == 0) return null;
        if (States.TryGetValue(s, out var existing)) return existing;
        var decl = new UnionDecl(s.Decl.Name.Span, Modifiers.None, s.Decl.Name, s.Decl.States);
        var u = new UnionSymbol(s.Name, s.Location, s.Module, s.Access, decl, s.File) { TypeMap = s.TypeMap };
        States[s] = u;
        // An instance's states were checked on the template.
        var outer = s.Template is null ? null : ReportInto(new DiagnosticBag());
        try { DeclareUnionVariants(u); }
        finally { if (s.Template is not null) ReportInto(outer); }
        if (s.Template is not null) return u;
        foreach (var member in s.Decl.Members)
        {
            var name = member switch
            {
                FunctionDecl f when f.Modifiers.HasFlag(Modifiers.Static) => f.Name.Text,
                PropertyDecl p when p.Modifiers.HasFlag(Modifiers.Static) => p.Name.Text,
                _ => null,
            };
            if (name is not null && s.Decl.States.FirstOrDefault(v => v.Name.Text == name) is { } state)
                Report(DiagnosticCodes.StateShadowsStatic, s.File.Source, state.Name.Span, [name, s.Name],
                    help: "rename the state or the static member");
        }
        return u;
    }

    /// <summary>Declares the states of <paramref name="s"/>; a receiver state or <c>becomes</c> target it does not declare is BYN7007.</summary>
    private void CheckStateNames(StructSymbol s)
    {
        var states = StatesOf(s);
        foreach (var f in s.Decl.Members.OfType<FunctionDecl>())
            foreach (var n in f.ReceiverStates.Append(f.Becomes).OfType<Name>())
                if (states?.Find(n.Text) is null)
                    Report(DiagnosticCodes.UnknownState, s.File.Source, n.Span, [s.Name, n.Text], help: $"check the states declared in `{s.Name}`");
    }

    /// <summary>The <c>requires</c> check of each struct that has rules, by struct.</summary>
    internal Dictionary<StructSymbol, StructChecks> Checks { get; } = new();

    /// <summary>The structs that stand for a <c>type distinct</c>, built with <c>X(raw)</c>.</summary>
    internal HashSet<StructSymbol> Distinct { get; } = new();

    /// <summary>Structs marked with a well-formed <c>@positional</c>: they get a constructor taking every field in order.</summary>
    internal HashSet<StructDecl> Positional { get; } = new();

    private readonly Dictionary<FunctionSymbol, ConstructorDecl> _constructorBodies = new();

    /// <summary>Associated types first, so member signatures can name them; then methods and getters, abstract or default.</summary>
    private void DeclareInterfaceMembers(InterfaceSymbol i)
    {
        var file = i.File;
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
        var typeMap = i.TypeMap is null ? new Dictionary<string, TypeSymbol>(StringComparer.Ordinal) : new Dictionary<string, TypeSymbol>(i.TypeMap, StringComparer.Ordinal);
        foreach (var a in i.Decl.Members.OfType<AssociatedTypeDecl>())
        {
            Naming.CheckType(this, file.Source, a.Name);
            if (!Unique(a.Name)) continue;
            var t = new TypeParameterType(a.Name.Text);
            i.AssociatedTypes.Add(t);
            typeMap[a.Name.Text] = t;
        }
        var map = typeMap.Count == 0 ? null : typeMap;
        var types = new TypeResolver(this, file, null, map);
        void DeclareGetter(Decl decl, Modifiers modifiers, TypeSyntax type, Name name, bool isAbstract)
        {
            Naming.CheckValue(this, file.Source, name);
            RejectStatic(file, modifiers, name);
            var getter = new FunctionSymbol(name.Text, Loc(file, name), i.Module, FunctionKind.Getter)
            {
                Interface = i, IsAbstract = isAbstract, File = file, ReturnType = types.Resolve(type), Access = i.Access, TypeMap = map,
            };
            _interfaceGetters[getter] = decl;
            if (Unique(name)) i.Properties.Add(getter);
        }
        foreach (var member in i.Decl.Members)
        {
            switch (member)
            {
                case AssociatedTypeDecl:
                    break;
                case FunctionDecl m:
                {
                    Naming.CheckValue(this, file.Source, m.Name);
                    RejectStatic(file, m.Modifiers, m.Name);
                    var fn = new FunctionSymbol(m.Name.Text, Loc(file, m.Name), i.Module, FunctionKind.Method)
                    {
                        Interface = i, IsAbstract = m.Body is null && m.ExpressionBody is null, File = file, Decl = m, Access = i.Access,
                        IsStatic = m.Modifiers.HasFlag(Modifiers.Static), TypeMap = map,
                    };
                    // Generic methods are declarable here so `dyn` can name them (BYN2808); calling one is not supported.
                    if (DeclareTypeParameters(file, m.TypeParams, fn.TypeParameters) is { } own)
                        fn.TypeMap = map is null ? own : new Dictionary<string, TypeSymbol>(map.Concat(own.Where(p => !map.ContainsKey(p.Key))));
                    DeclareWhere(file, m.Where, fn.TypeParameters, fn.TypeMap, m.Name.Text);
                    DeclareSignature(fn, m, file);
                    if (Unique(m.Name)) i.Methods.Add(fn);
                    break;
                }
                case GetterDecl g:
                    DeclareGetter(g, g.Modifiers, g.Type, g.Name, isAbstract: true);
                    break;
                case PropertyDecl p:
                    DeclareGetter(p, p.Modifiers, p.Type, p.Name, isAbstract: false);
                    break;
                default:
                    Report(DiagnosticCodes.Unsupported, file.Source, member.Span, ["this kind of interface member"]);
                    break;
            }
        }
    }

    private void RejectStatic(FileContext file, Modifiers modifiers, Name name)
    {
        if (modifiers.HasFlag(Modifiers.Static))
            Report(DiagnosticCodes.StaticAbstract, file.Source, name.Span, [], label: "declared `static`",
                help: "remove `static` or make it a free function");
    }

    private void RejectModifiers(FileContext file, TextSpan span, Modifiers actual, Modifiers rejected, string what)
    {
        if ((actual & rejected) != 0)
            Report(DiagnosticCodes.Unsupported, file.Source, span, [$"`{(actual & rejected).ToString().ToLowerInvariant()}` on {what}"]);
    }

    /// <summary>Makes <paramref name="decl"/>'s <c>move this</c> or plain receiver <c>ref this</c>, keeping <c>is .s</c> (BYN7010's fix, also part of BYN7003's).</summary>
    internal static Fix RefReceiverFix(FunctionDecl decl) => decl.MoveThis is { } move
        ? new Fix(new TextSpan(move.Start, "move".Length), "ref")
        : new Fix(new TextSpan(decl.ParamsStart, 0), decl.ReceiverStates.Length > 0 ? "ref " : decl.Params.Length > 0 ? "ref this, " : "ref this");

    private void DeclareSignature(FunctionSymbol fn, FunctionDecl decl, FileContext file, StructSymbol? owner = null)
    {
        var types = new TypeResolver(this, file, owner, fn.TypeMap);
        fn.ReturnType = types.Resolve(decl.ReturnType, allowVoid: true);
        if (FunctionTypeNotHere(fn.ReturnType, file.Source, decl.ReturnType.Span, "a return type")) fn.ReturnType = ErrorType.Instance;
        fn.IsMutating = decl.RefThis is not null;
        if (decl.RefThis is { } refThis && fn is not { Kind: FunctionKind.Method, IsStatic: false })
            Report(DiagnosticCodes.Unsupported, file.Source, refThis, [fn.IsStatic ? "`ref this` on a static method" : "`ref this` outside a struct"],
                help: "remove `ref this`; only an instance method has a `this` to change");
        // Only a `ref this` caller still holds the value whose state changed (revision 0010); a static `becomes` is BYN2022 at its `become`.
        if (decl.Becomes is { } becomes && decl.RefThis is null && fn is { Kind: FunctionKind.Method, IsStatic: false })
        {
            // `move this is .s` and `this is .s` are written; a method with neither takes `this` unwritten.
            var start = decl.MoveThis?.Start ?? decl.ParamsStart;
            var receiver = decl.ReceiverStates.Length > 0 ? TextSpan.FromBounds(start, decl.ReceiverStates[^1].Span.End) : decl.MoveThis;
            Report(DiagnosticCodes.BecomesNeedsRef, file.Source, receiver ?? decl.Name.Span,
                [fn.Name, becomes.Text, receiver is { } r ? file.Source.GetText(r) : "this"],
                help: $"change the receiver of `{fn.Name}` to `ref this`, or return the new state as a value", fix: RefReceiverFix(decl));
        }
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
                    // A case stays excluded only while every name that brings in its set excludes it.
                    var excluded = t is ErrorUnionType { Excluded: var x } ? x : [];
                    foreach (var set in t is ErrorUnionType u ? u.Sets : [(ErrorSetSymbol)t])
                    {
                        if (fn.ErrorSets.Contains(set))
                        {
                            fn.ExcludedErrors.RemoveAll(v => v.Set == set && !excluded.Contains(v));
                            continue;
                        }
                        fn.ErrorSets.Add(set);
                        fn.ExcludedErrors.AddRange(excluded.Where(v => v.Set == set));
                    }
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
        Particles.Check(this, file.Source, fn);
    }

    /// <summary>Recursive by-value containment, borrowed fields (BYN3001).</summary>
    private void CheckStructShape(StructSymbol s)
    {
        foreach (var f in s.Fields)
        {
            if (FunctionTypeNotHere(f.Type, s.File.Source, f.Decl.Type.Span, "a field")) continue;
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

    /// <summary>BYN2822 when <paramref name="t"/> is a function type at <paramref name="position"/>: only a parameter, an alias or a local holds one (Q346).</summary>
    internal bool FunctionTypeNotHere(TypeSymbol t, SourceText source, TextSpan span, string position)
    {
        if (t is not FunctionType) return false;
        Report(DiagnosticCodes.FunctionTypeNotHere, source, span, [position], help: "take the function as a parameter instead");
        return true;
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
        CheckConformances();
        // Before the constants, which may build a checked value (J38).
        foreach (var (s, checks) in Checks)
            new Binder(this, s.File, checks.Check, s).BindChecks(checks);
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
            Instantiating(work.Site, work.Span, work.Bind, work.Instance);
        InferErrorSets();
        EffectAnalysis.Analyze(this);
        CheckMutation();
        CheckRequires();
        RegionAnalysis.Summarize(this);
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
                // A default is analysed on its template, so it is checked with no conformer; its clones repeat nothing it reports.
                else if (fn is { Body: not null, Interface: not null, IsAbstract: false }) AnalyzeInstance(fn);
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
    /// and formatting on a <c>T</c>, a <c>for</c> over a <c>T</c> whose <c>Item</c> is unique, a unique <c>T</c>
    /// field standing for a getter); they are reported at <paramref name="span"/>, the use that chose the type arguments.
    /// BYN2022 and BYN2780 are also template rules, so they are forwarded only when the template did not report them.
    /// BYN7012 in the body of <paramref name="instance"/> is a type argument with states replaced through a <c>ref</c>
    /// (revision 0010 rule C); it is reported at the use too, naming the generic.
    /// </summary>
    private void Instantiating(SourceText site, TextSpan span, Action work, FunctionSymbol? instance = null)
    {
        var outer = _scratch;
        var bag = _scratch = new DiagnosticBag();
        var outerSite = _instantiationSite;
        _instantiationSite ??= (site, span);
        _instantiating++;
        try { work(); }
        finally
        {
            _scratch = outer;
            _instantiationSite = outerSite;
            _instantiating--;
        }
        var target = _scratch ?? Diagnostics;
        bool Reported(Diagnostic d) => target.Items.Concat(Diagnostics.Items).Any(t => t.Code == d.Code && t.Source == d.Source && t.Span == d.Span);
        foreach (var d in bag.Items.Where(d => d.IsError))
        {
            if (d.Code == DiagnosticCodes.InvalidMapKey.Code || d.Code == DiagnosticCodes.KeyPartNotHashable.Code || d.Code == DiagnosticCodes.BadBinaryOperands.Code || d.Code == DiagnosticCodes.NotFormattable.Code
                || d.Code == DiagnosticCodes.ConstraintNotSatisfied.Code || d.Code == DiagnosticCodes.FunctionTypeNotHere.Code && !Reported(d)
                || (d.Code == DiagnosticCodes.Unsupported.Code || d.Code == DiagnosticCodes.ImplicitCopy.Code) && !Reported(d))
                target.Add(d with { Source = site, Span = span, Label = InstantiationLabel });
            else if (d.Code == DiagnosticCodes.AssignmentChangesState.Code && instance is not null && !Reported(d))
                target.Add(d with
                {
                    Source = site, Span = span, Label = InstantiationLabel,
                    Help = $"call `{instance.Name}` with a type without states; it replaces a value of its type argument whole",
                    Related = d.Source == site ? [new RelatedSpan(d.Span, $"in `{instance.Name}`")] : null,
                });
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
        // The template checked the calls in its own body, so a call in an instance is not checked again.
        var check = _instantiating == 0;
        var satisfied = true;
        void Declare()
        {
            DeclareSignature(fn, fn.Decl!, fn.File!, fn.Owner);
            if (check) satisfied = CheckConstraints(template.TypeParameters, fn.Decl!.Where, fn.File!, fn.Owner, fn.TypeMap, args);
        }
        if (_firstPass is { } pass)
        {
            pass.Instances.Add(fn);
            var outer = ReportInto(pass.Diagnostics);
            var errors = _instantiationErrors.Count;
            try { Instantiating(site, span, Declare); }
            finally { ReportInto(outer); }
            pass.Errors.AddRange(_instantiationErrors.Skip(errors));
            _instantiationErrors.RemoveRange(errors, _instantiationErrors.Count - errors);
        }
        else Instantiating(site, span, Declare);
        // A body bound with a non-conforming argument would only repeat BYN2807 as unknown members.
        if (!fn.IsGeneric && satisfied) _instantiationWork.Enqueue((site, span, () => BindFunction(fn), fn));
        return fn;
    }

    /// <summary>Depth of <see cref="Instantiating"/> calls: nonzero while declaring or binding an instance.</summary>
    private int _instantiating;

    /// <summary>The outermost use being instantiated, where its errors end up reported; null outside <see cref="Instantiating"/>.</summary>
    private (SourceText Site, TextSpan Span)? _instantiationSite;

    /// <summary>Every struct's fields are declared, so a struct or tuple <c>Map</c> key can be checked.</summary>
    internal bool MembersDeclared { get; private set; }

    /// <summary><c>Map</c> key checks of types resolved before <see cref="MembersDeclared"/>; run once it is set.</summary>
    private readonly List<Action> _pendingKeys = new();

    /// <summary>A check deferred while instantiating reports at that use, as it would have when run then (spec 008 R9).</summary>
    internal void AfterMembersDeclared(Action check) =>
        _pendingKeys.Add(_instantiationSite is var (site, span) ? () => Instantiating(site, span, check) : check);

    /// <summary>Constraint checks of types instantiated before conformances exist; run once they do.</summary>
    private readonly List<Action> _pendingConstraints = new();

    /// <summary>BYN2807 for each argument that does not conform to its parameter's <c>where</c> bounds; false when one does not.</summary>
    private bool CheckConstraints(List<TypeParameterType> parameters, WhereClause[] where, FileContext file, StructSymbol? owner,
        IReadOnlyDictionary<string, TypeSymbol>? typeMap, TypeSymbol[] args)
    {
        var satisfied = true;
        var types = new TypeResolver(this, file, owner, typeMap);
        for (var k = 0; k < parameters.Count; k++)
        {
            if (parameters[k].Constraints.Count == 0) continue;
            // The bound is resolved again with the arguments, so `IOrder<T>` is `IOrder<Point>` here.
            foreach (var bound in where.Where(w => w.Param.Text == parameters[k].Name).SelectMany(w => w.Bounds))
            {
                InterfaceSymbol? i = null;
                Quietly(() => i = types.ResolveBound(bound) as InterfaceSymbol);
                if (i is null || Conforms(args[k], i)) continue;
                var missing = MissingMember(args[k], i);
                var help = args[k] switch
                {
                    StructSymbol s => $"add `: {i.DisplayName}` to `{s.DisplayName}` and provide `{missing}`",
                    TypeParameterType p => $"add `where {p.Name} : {i.DisplayName}` to the declaration of `{p.Name}`",
                    PrimitiveType => $"pass a struct that conforms to `{i.DisplayName}`; primitives do not conform to interfaces",
                    _ => $"pass a struct that conforms to `{i.DisplayName}`; only structs conform to interfaces",
                };
                Report(DiagnosticCodes.ConstraintNotSatisfied, file.Source, bound.Span, [args[k].DisplayName, i.DisplayName, missing], help: help);
                satisfied = false;
                break;
            }
        }
        return satisfied;
    }

    /// <summary>The constraint check of a type instance, now or once conformances are checked.</summary>
    private void CheckConstraintsAt(SourceText site, TextSpan span, List<TypeParameterType> parameters, WhereClause[] where, FileContext file,
        IReadOnlyDictionary<string, TypeSymbol>? typeMap, TypeSymbol[] args)
    {
        if (parameters.All(p => p.Constraints.Count == 0)) return;
        void Check() => Instantiating(site, span, () => CheckConstraints(parameters, where, file, null, typeMap, args));
        if (_conformancesChecked) Check();
        else _pendingConstraints.Add(Check);
    }

    /// <summary>A struct conforms when it lists the interface; a type parameter when its own bounds include it (D5: primitives never do).</summary>
    private static bool Conforms(TypeSymbol t, InterfaceSymbol i) => t switch
    {
        _ when t.IsError => true,
        StructSymbol s => s.Conformances.Any(c => c.Interface == i),
        TypeParameterType p => p.Constraints.Contains(i),
        _ => false,
    };

    /// <summary>The first abstract member of <paramref name="i"/> that <paramref name="t"/> does not have, for BYN2807.</summary>
    private static string MissingMember(TypeSymbol t, InterfaceSymbol i)
    {
        var members = i.Methods.Concat(i.Properties).OrderBy(m => m.Location!.Value.Span.Start).ToList();
        var abstracts = members.Where(m => m.IsAbstract).ToList();
        var missing = abstracts.FirstOrDefault(m => t is not StructSymbol s || (s.FindProperty(m.Name) is null && !s.FindMethods(m.Name).Any()));
        return (missing ?? abstracts.FirstOrDefault() ?? members.FirstOrDefault())?.Name ?? $": {i.DisplayName}";
    }

    /// <summary>The instantiation of a generic struct or union at <paramref name="args"/>, with its members.</summary>
    internal TypeSymbol InstantiateType(TypeSymbol template, TypeSymbol[] args, SourceText site, TextSpan span)
    {
        if (_instances.TryGetValue((template, args), out var existing)) return (TypeSymbol)existing;
        // As for functions: a type named inside an instance was checked in its template.
        var check = _instantiating == 0;
        switch (template)
        {
            case StructSymbol s:
            {
                var instance = new StructSymbol(s.Name, s.Location, s.Module, s.Access, s.Decl, s.File)
                {
                    Template = s, TypeArguments = args, TypeMap = Substitution(null, s.TypeParameters, args),
                };
                _instances[(s, args)] = instance;
                if (check) CheckConstraintsAt(site, span, s.TypeParameters, s.Decl.Where, s.File, instance.TypeMap, args);
                Instantiating(site, span, () => DeclareMembers(instance));
                if (_conformancesChecked) Instantiating(site, span, () => Conform(instance));
                if (instance.IsGeneric) return instance;
                foreach (var f in instance.Fields.Where(f => f.Decl.Default is not null))
                    _instantiationWork.Enqueue((site, span, () => FieldDefault(instance, f, site, span), null));
                // Cloned defaults (with a Template) are queued by Conform.
                foreach (var fn in instance.Methods.Concat(instance.Properties.Select(p => p.Getter!)).Concat(instance.Constructors).Where(f => f.Template is null))
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
                if (check) CheckConstraintsAt(site, span, u.TypeParameters, u.Decl.Where, u.File, instance.TypeMap, args);
                Instantiating(site, span, () => DeclareUnionVariants(instance));
                return instance;
            }
            case InterfaceSymbol i:
            {
                var instance = new InterfaceSymbol(i.Name, i.Location, i.Module, i.Access, i.File)
                {
                    Decl = i.Decl, Template = i, TypeArguments = args, TypeMap = Substitution(null, i.TypeParameters, args),
                };
                _instances[(i, args)] = instance;
                if (check) CheckConstraintsAt(site, span, i.TypeParameters, i.Decl.Where, i.File, instance.TypeMap, args);
                // A `where` bound can name an instance before the template's members are declared.
                if (_interfaceMembersDeclared) Instantiating(site, span, () => DeclareInterfaceMembers(instance));
                else _pendingInterfaces.Add((instance, site, span));
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
    internal static FunctionSymbol? TemplateOf(FunctionSymbol fn) =>
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
        var requires = RequiresChecks.Count;
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
        RequiresChecks.RemoveRange(requires, RequiresChecks.Count - requires);
        fn.Locals.RemoveRange(locals, fn.Locals.Count - locals);
        // The lambdas it lifted, in the body or in those instances, are lifted again by the second pass.
        foreach (var module in Modules)
            module.AllFunctions.RemoveAll(f => f.Kind == FunctionKind.Lambda && Outermost(f) is var o && (o == fn || pass.Instances.Contains(o)));
        BindBody(fn, seeds);

        static FunctionSymbol Outermost(FunctionSymbol f)
        {
            while (f.Enclosing is { } e) f = e;
            return f;
        }
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
            case FunctionKind.Getter when fn.Interface is not null:
                (binder = New(null)).BindGetter((PropertyDecl)_interfaceGetters[fn]);
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
        // A lambda's sets are its function type's, never inferred.
        var inferred = Modules.SelectMany(m => m.AllFunctions)
            .Where(f => f.Kind != FunctionKind.Lambda && (f.InfersErrors || f.Kind is FunctionKind.Main or FunctionKind.Test)).ToList();
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

    /// <summary>The fields of <c>this</c> each <c>ref this</c> method changes structurally, for region analysis.</summary>
    internal Dictionary<FunctionSymbol, HashSet<FieldSymbol>> ChangedFields { get; } = new();

    /// <summary>
    /// For each template with a body, what it may store from a parameter (or <c>this</c>, -1) into a <c>ref</c>/<c>out</c> parameter
    /// (or its <c>ref this</c> receiver), with the first place it does, for region analysis.
    /// </summary>
    internal Dictionary<FunctionSymbol, Dictionary<(int Source, int Target), TextSpan>> StoreSummaries { get; } = new();

    /// <summary>For each template with a body, the parameters (or <c>this</c>, -1) its result may borrow, for region analysis.</summary>
    internal Dictionary<FunctionSymbol, HashSet<int>> ReturnSummaries { get; } = new();

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

    /// <summary>Calls with a constant argument to a function with <c>requires</c>, checked once every body is bound.</summary>
    internal List<RequiresCheck> RequiresChecks { get; } = new();

    /// <summary>A <c>requires</c> clause that the call's constant arguments make false is BYN2321 at the argument.</summary>
    private void CheckRequires()
    {
        foreach (var check in RequiresChecks)
        {
            var callee = check.Callee;
            var values = new Dictionary<LocalSymbol, ConstantValue>();
            for (var i = 0; i < check.Arguments.Length; i++)
                if (check.Arguments[i].Constant is { } c) values[callee.Parameters[i]] = c;
            foreach (var clause in callee.Requires)
            {
                if (ConstantFolding.Evaluate(clause, values) is not { Value: false }) continue;
                var read = BoundTree.Descendants(clause).OfType<BoundLocal>().Select(l => l.Local).ToHashSet();
                var at = callee.Parameters.FirstOrDefault(p => read.Contains(p) && values.ContainsKey(p)
                    && check.Call.Args.Any(a => ReferenceEquals(a.Value, check.Arguments[p.Ordinal].Syntax)));
                var span = at is null ? check.Call.Span : check.Arguments[at.Ordinal].Syntax.Span;
                var text = callee.File!.Source.GetText(clause.Syntax.Span);
                Report(DiagnosticCodes.RequiresViolated, check.Source, span, [check.Source.GetText(span), text, callee.Name],
                    label: "fails the contract", help: $"pass a value that meets `requires {text}`");
            }
        }
    }

    private readonly Dictionary<FunctionType, FunctionSymbol> _invokeSymbols = new();

    /// <summary>
    /// What a call through a value of <paramref name="type"/> calls: one symbol per type, whose parameters <c>_0</c>, <c>_1</c>, …
    /// carry no particle, so no argument name or label matches one (spec 007 R9).
    /// </summary>
    internal FunctionSymbol InvokeSymbol(FunctionType type)
    {
        if (_invokeSymbols.TryGetValue(type, out var invoke)) return invoke;
        invoke = new FunctionSymbol("invoke", null, EntryModule, FunctionKind.Invoke) { ReturnType = type.Return, Fails = type.Fails.Count > 0 };
        for (var i = 0; i < type.Parameters.Count; i++)
            invoke.Parameters.Add(new ParameterSymbol($"_{i}", null, type.Parameters[i], ParamModifier.None, i));
        invoke.ErrorSets.AddRange(type.Fails);
        return _invokeSymbols[type] = invoke;
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

/// <summary>A <c>type X = T</c> alias or a <c>union error S = A, B</c>; resolved to its target on first use.</summary>
internal sealed record MutationCheck(Binder Binder, FunctionSymbol Callee, BoundExpr Receiver);

internal sealed record RequiresCheck(SourceText Source, CallExpr Call, FunctionSymbol Callee, BoundExpr[] Arguments);

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
