using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <param name="Library">A library has no <c>main</c>, so none is required.</param>
public sealed record CompilationOptions(bool IncludeTests = false, bool Library = false);

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

    public Builtins Builtins { get; } = new();

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
        c.EntryModule = c.LoadModule("main", packageRoot, entryFiles, null);
        // Syntax errors stop here: binding a broken tree mostly reports their echoes.
        if (c.Diagnostics.HasErrors) return c;
        c.Declare();
        c.BindAll();
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
                    help: $"a module is a directory of `.byn` files; expected `{Path.GetRelativePath(PackageRoot, dir)}/` next to the entry file");
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
        {
            foreach (var s in module.Structs) DeclareMembers(s);
            foreach (var f in module.AllFunctions.Where(f => f.Owner is null && f.Kind != FunctionKind.Main && f.Kind != FunctionKind.Test))
                DeclareSignature(f, f.Decl!, f.File!);
        }
        foreach (var module in Modules)
        {
            foreach (var s in module.Structs) CheckStructShape(s);
        }
        if (EntryModule.Main is { Kind: FunctionKind.Function } main) CheckMainSignature(main);
    }

    /// <summary><c>IoError</c> is numbered first, so the runtime can return its codes 1 to 5 directly.</summary>
    private void DeclareBuiltinVariants()
    {
        var set = Builtins.IoError;
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

    /// <summary>No declaration takes an attribute yet; <c>[ExitCode]</c> belongs on an error variant.</summary>
    private void CheckAttributes(FileContext file, Decl decl)
    {
        foreach (var a in decl.Attributes)
        {
            if (a.Name.Text == "ExitCode")
                Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[ExitCode]"], help: "`[ExitCode]` belongs on an error enum variant");
            else
                Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text], help: "the only attribute is `[ExitCode(n)]`, on an error enum variant");
        }
        if (decl is StructDecl s)
            foreach (var m in s.Members) CheckAttributes(file, m);
    }

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
                case TypeAliasDecl a:
                {
                    var sym = new AliasPlaceholder(a, file);
                    if (AddType(file, a.Name, sym)) _aliases.Add(sym);
                    Naming.CheckType(this, file.Source, a.Name);
                    break;
                }
                case FunctionDecl f:
                {
                    var sym = new FunctionSymbol(f.Name.Text, Loc(file, f.Name), module, FunctionKind.Function)
                    {
                        Access = AccessOf(f.Modifiers), File = file, Decl = f,
                    };
                    Naming.CheckValue(this, file.Source, f.Name);
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
                    if (Options.IncludeTests)
                    {
                        var fn = new FunctionSymbol("test", Loc(file, t), module, FunctionKind.Test) { File = file };
                        module.Tests.Add(new TestSymbol(t.Title, Loc(file, t), fn));
                        module.AllFunctions.Add(fn);
                        _testBodies[fn] = t;
                    }
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
                    help: "top-level statements may appear in only one file of the entry module");
            }
        }
    }

    private readonly List<AliasPlaceholder> _aliases = new();
    private readonly List<(ErrorVariantSymbol Variant, AttributeSyntax Attribute, FileContext File)> _exitCodes = new();
    private readonly Dictionary<FunctionSymbol, TestDecl> _testBodies = new();
    private (FunctionSymbol Main, List<GlobalStatement> Statements)? _mainStatements;

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
        AliasPlaceholder a => new Location(a.File.Source, a.Decl.Name.Span),
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
                if (a.Name.Text != "ExitCode")
                    Report(DiagnosticCodes.UnknownAttribute, file.Source, a.Name.Span, [a.Name.Text], help: "the only attribute is `[ExitCode(n)]`");
                else if (seen)
                    Report(DiagnosticCodes.ModifierNotAllowed, file.Source, a.Span, ["[ExitCode]"], help: "the attribute is repeated");
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

    private void DeclareMembers(StructSymbol s)
    {
        var file = s.File;
        var types = new TypeResolver(this, file, s);
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
                        Owner = s, File = file, ReturnType = prop.Type, Access = prop.Access,
                    };
                    prop.Getter = getter;
                    s.Module.AllFunctions.Add(getter);
                    if (Unique(p.Name)) s.Properties.Add(prop);
                    break;
                }
                case FunctionDecl m:
                {
                    Naming.CheckValue(this, file.Source, m.Name);
                    var fn = new FunctionSymbol(m.Name.Text, Loc(file, m.Name), s.Module, FunctionKind.Method)
                    {
                        Owner = s, File = file, Decl = m, Access = AccessOf(m.Modifiers), IsStatic = m.Modifiers.HasFlag(Modifiers.Static),
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
                        Owner = s, File = file, Access = AccessOf(c.Modifiers), ReturnType = s,
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
        var types = new TypeResolver(this, file, owner);
        fn.ReturnType = types.Resolve(decl.ReturnType, allowVoid: true);
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
                if (t is ErrorSetSymbol set)
                {
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
            if (p.Modifier is ParamModifier.Move)
                Report(DiagnosticCodes.Unsupported, file.Source, p.Span, ["`move` parameters"]);
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
        if (Contains(s, s, new HashSet<TypeSymbol>()))
            Report(DiagnosticCodes.RecursiveStruct, s.File.Source, s.Decl.Name.Span, [s.Name],
                help: "a struct cannot contain itself by value; store a `List` of it or an index instead");
    }

    private static bool Contains(TypeSymbol t, StructSymbol target, HashSet<TypeSymbol> seen)
    {
        IEnumerable<TypeSymbol> parts = t switch
        {
            StructSymbol s => s.Fields.Select(f => f.Type),
            OptionalType o => [o.Inner],
            ArrayType a => [a.Element],
            TupleType tt => tt.Fields.Select(f => f.Type),
            _ => [],
        };
        foreach (var p in parts)
        {
            if (p == target) return true;
            if (seen.Add(p) && Contains(p, target, seen)) return true;
        }
        return false;
    }

    // ── Bodies ───────────────────────────────────────────────────────────────────

    private void BindAll()
    {
        foreach (var module in Modules)
            foreach (var c in module.AllConstants)
                BindConstant(c);
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
            foreach (var fn in module.AllFunctions.ToList())
                BindFunction(fn);
        }
        InferErrorSets();
        InferMutation();
        foreach (var module in Modules)
            foreach (var fn in module.AllFunctions)
                if (fn.Body is not null)
                {
                    FlowAnalysis.Analyze(this, fn);
                    RegionAnalysis.Analyze(this, fn);
                    SpanParameterLint.Analyze(this, fn);
                }
        if (EntryModule.Main is null && !Options.IncludeTests && !Options.Library && EntryModule.Files.Count > 0 && !Diagnostics.HasErrors)
            Report(DiagnosticCodes.NoMain, EntryModule.Files[0].Source, new TextSpan(0, 0), []);
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

    private void BindFunction(FunctionSymbol fn)
    {
        var file = fn.File!;
        switch (fn.Kind)
        {
            case FunctionKind.Function or FunctionKind.Method when fn.Decl is { } decl:
                new Binder(this, file, fn, fn.Owner).BindFunctionBody(decl);
                break;
            case FunctionKind.Getter:
            {
                var prop = fn.Owner!.Properties.First(p => p.Getter == fn);
                new Binder(this, file, fn, fn.Owner).BindGetter(prop.Decl);
                break;
            }
            case FunctionKind.Constructor:
                new Binder(this, file, fn, fn.Owner).BindConstructor(_constructorBodies[fn]);
                break;
            case FunctionKind.Main when _mainStatements is { } m && m.Main == fn:
                new Binder(this, file, fn, null).BindMain(m.Statements);
                break;
            case FunctionKind.Test:
                new Binder(this, file, fn, null).BindTest(_testBodies[fn]);
                break;
        }
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

    /// <summary>Calls of methods through a receiver, checked once every body is bound and mutation is known.</summary>
    internal List<MutationCheck> MutationChecks { get; } = new();

    /// <summary>
    /// A method is mutating when it writes to <c>this</c> or calls a mutating method on it; the
    /// second part is a fixed point. Then every call of a mutating method needs a writable receiver.
    /// </summary>
    private void InferMutation()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var m in MutationChecks)
            {
                if (!m.Callee.IsMutating || m.Binder.Function is not { IsMutating: false } caller) continue;
                if (!m.Binder.WritesThis(m.Receiver)) continue;
                caller.IsMutating = true;
                changed = true;
            }
        } while (changed);
        foreach (var m in MutationChecks.Where(m => m.Callee.IsMutating))
            m.Binder.CheckWritable(m.Receiver, m.Receiver.Syntax.Span, $"`{m.Callee.Name}` modifies it");
    }

    /// <summary>A parameter's default value, bound on first use.</summary>
    internal BoundExpr? ParameterDefault(FunctionSymbol fn, ParameterSymbol p)
    {
        if (p.Default is null && p.Syntax?.Default is not null)
            p.Default = new Binder(this, fn.File!, null, fn.Owner).BindParameterDefault(p);
        return p.Default;
    }

    /// <summary>
    /// The sets a call of <paramref name="fn"/> can fail with; empty (any error) while an inferred
    /// set is still being computed.
    /// </summary>
    internal IReadOnlyList<ErrorSetSymbol> ErrorSetsOf(FunctionSymbol fn) =>
        fn.InfersErrors || fn.Kind is FunctionKind.Main or FunctionKind.Test ? [] : fn.ErrorSets;

    internal void Report(DiagnosticDescriptor d, SourceText source, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null) =>
        Diagnostics.Report(d, source, span, args, label, help, fix);

    internal IEnumerable<AliasPlaceholder> Aliases => _aliases;
}

/// <summary>A <c>type X = T;</c> alias; resolved to its target on first use.</summary>
internal sealed record MutationCheck(Binder Binder, FunctionSymbol Callee, BoundExpr Receiver);

internal sealed class AliasPlaceholder(TypeAliasDecl decl, FileContext file) : TypeSymbol
{
    public TypeAliasDecl Decl { get; } = decl;

    public FileContext File { get; } = file;

    public TypeSymbol? Target { get; set; }

    public bool Resolving { get; set; }

    public override string DisplayName => Decl.Name.Text;
}
