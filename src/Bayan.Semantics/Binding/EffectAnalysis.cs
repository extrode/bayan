using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Effects (spec §14). A function with <c>uses</c> declares its set, a public function without one is pure, and every
/// other function infers its set from its body by fixed point, as error sets do. A body that uses an effect its
/// declaration lacks is BYN6002 (the entry function is exempt), an unknown name in <c>uses</c> or <c>effect</c> is
/// BYN6003, an effect the package <c>capabilities</c> lack is BYN6004, and a witness beyond its interface method's
/// bound is BYN6005. An instance has its template's effects.
/// </summary>
internal static class EffectAnalysis
{
    /// <summary>The closed effect list, in spec order.</summary>
    public static readonly string[] Primitives = ["alloc", "suspend", "net", "fs", "clock", "random", "env", "process", "console"];

    /// <summary>The effects that need a package capability; the others are always allowed.</summary>
    private static readonly string[] Capabilities = ["net", "fs", "process", "env"];

    public static void Analyze(Compilation c)
    {
        var aliases = new Dictionary<ModuleSymbol, Dictionary<string, string[]>>();
        foreach (var module in c.Modules) aliases[module] = ResolveAliases(c, module);

        var functions = c.Modules.SelectMany(m => m.AllFunctions).ToList();
        var declared = new Dictionary<FunctionSymbol, HashSet<string>>();
        var reported = new HashSet<UsesClause>(ReferenceEqualityComparer.Instance);
        foreach (var fn in functions)
            if (Declared(c, fn, aliases, reported) is { } set) declared[fn] = set;

        // Each body's effects with the first place that uses each one; `direct` marks a built-in source, not a call of a user function.
        var used = new Dictionary<FunctionSymbol, Dictionary<string, (TextSpan Span, bool Direct)>>();
        foreach (var fn in functions) used[fn] = new();
        IEnumerable<string> Visible(FunctionSymbol callee)
        {
            if (callee.IsInstance && Compilation.TemplateOf(callee) is { } template) callee = template;
            if (declared.TryGetValue(callee, out var set)) return set;
            if (used.TryGetValue(callee, out var inferred)) return inferred.Keys;
            // Interface members of a built-in interface, and clones of a default member, are bounded like a declaration.
            return callee.Decl?.Uses is null ? [] : Resolve(c, callee, callee.Decl.Uses, aliases, null);
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var fn in functions)
            {
                if (fn.IsInstance || fn.Body is null) continue;
                var effects = used[fn];
                var sinks = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
                foreach (var node in BoundTree.Descendants(fn.Body))
                {
                    // Interpolation written straight into an output is formatted on the stack; anywhere else it grows a `StrBuf`.
                    if (node is BoundCall { Function.Builtin: BuiltinFunction.Print or BuiltinFunction.Log or BuiltinFunction.Panic, Arguments: [BoundInterpolated text, ..] })
                        sinks.Add(text);
                    if (node is BoundBuiltinCall { Member: BuiltinMember.StrBufAppend, Arguments: [BoundInterpolated appended] })
                        sinks.Add(appended);
                    var (sources, direct) = node switch
                    {
                        BoundInterpolated when !sinks.Contains(node) => (["alloc"], true),
                        BoundCall { Function.Kind: FunctionKind.Builtin } call => (Builtin(call.Function.Builtin), true),
                        // A call through a function-typed parameter adds nothing: whoever passed the value is charged (R16).
                        // Its arguments are still charged here like any call's.
                        BoundCall { Function.Kind: FunctionKind.Invoke, Receiver: BoundLocal { Local: ParameterSymbol } } call => (Passed(call).SelectMany(p => Visible(p.Function)).Concat(PassedValues(call)), false),
                        BoundCall { Function.Kind: FunctionKind.Invoke, Receiver.Type: FunctionType type } call => (type.Uses.Concat(Passed(call).SelectMany(p => Visible(p.Function))).Concat(PassedValues(call)), false),
                        BoundCall call => (Visible(call.Function).Concat(Passed(call).SelectMany(p => Visible(p.Function))).Concat(PassedValues(call)), false),
                        BoundBuiltinCall b => (Member(b.Member), true),
                        BoundCollection { Type: ListType or MapType } => (["alloc"], true),
                        _ => ((IEnumerable<string>)[], false),
                    };
                    foreach (var effect in sources)
                    {
                        if (effects.ContainsKey(effect)) continue;
                        effects[effect] = (node.Syntax.Span, direct);
                        changed = true;
                    }
                }
            }
        } while (changed);

        foreach (var fn in functions)
        {
            if (fn.IsInstance || fn.Body is null || fn.File is null || IsCheck(fn)) continue;
            if (declared.TryGetValue(fn, out var set))
            {
                var missing = used[fn].Where(e => !set.Contains(e.Key)).OrderBy(e => e.Value.Span.Start).ToList();
                // One fix declares every missing effect, so the fixes of one function's reports are the same edit.
                var fix = missing.Count == 0 || fn.Decl is null ? null : DeclareFix(fn.File.Source, fn.Decl, Primitives.Where(e => missing.Any(m => m.Key == e)));
                foreach (var (effect, (span, _)) in missing)
                    c.Report(DiagnosticCodes.EffectNotDeclared, fn.File.Source, span, [fn.Name, effect], label: $"uses `{effect}`",
                        help: $"add `{effect}` to the `uses` clause, or remove the call", fix: fix);
            }
            if (c.Options.Capabilities is { } allowed)
            {
                // At the `uses` clause; a function that infers its effects has none, so its name stands in.
                var at = fn.Decl?.Uses?.Span ?? fn.Decl?.Name.Span;
                foreach (var (effect, (span, _)) in used[fn].Where(e => e.Value.Direct && Capabilities.Contains(e.Key) && !allowed.Contains(e.Key)).OrderBy(e => e.Value.Span.Start))
                    c.Report(DiagnosticCodes.CapabilityExceeded, fn.File.Source, at ?? span, [fn.Name, effect, c.Options.PackageName ?? "main"],
                        label: $"uses `{effect}`", help: $"add `.{effect}` to `capabilities`, or remove the use");
            }
        }

        // Outside a parameter, a function type's `uses` bounds what converts to it (R16, Q359).
        foreach (var fn in functions)
        {
            if (fn.IsInstance || fn.Body is null || fn.File is null) continue;
            var passed = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
            foreach (var node in BoundTree.Descendants(fn.Body))
            {
                if (node is BoundCall call)
                    foreach (var (value, _) in Passed(call)) passed.Add(value);
                var (function, type) = node switch
                {
                    BoundLambda l => (l.Lambda, l.FunctionType),
                    BoundFunctionRef r => (r.Function, r.FunctionType),
                    _ => (null, null),
                };
                if (function is null || type is null || passed.Contains(node)) continue;
                var effects = Visible(function).ToHashSet();
                foreach (var effect in Primitives.Where(e => effects.Contains(e) && !type.Uses.Contains(e)))
                    c.Report(DiagnosticCodes.EffectBoundExceeded, fn.File.Source, node.Syntax.Span,
                        [function.Name, effect, type.Uses.Count == 0 ? "pure" : "uses " + string.Join(", ", type.Uses), type.DisplayName],
                        label: $"uses `{effect}`", help: $"add `{effect}` to the function type's `uses`, or pass a function without it");
            }
        }

        foreach (var s in c.Modules.SelectMany(m => m.Structs).Where(s => s.Template is null))
            foreach (var conformance in s.Conformances)
                foreach (var (member, witness) in conformance.Witnesses)
                {
                    // A cloned default is checked against its own interface's declaration, not here.
                    if (witness.Owner != s || witness.Location is not { } at || member.Kind == FunctionKind.Getter) continue;
                    var bound = Visible(member).ToHashSet();
                    foreach (var effect in Visible(witness).Where(e => !bound.Contains(e)))
                        c.Report(DiagnosticCodes.EffectBoundExceeded, at.Source, at.Span,
                            [witness.Name, effect, bound.Count == 0 ? "pure" : "uses " + string.Join(", ", Primitives.Where(bound.Contains)), $"{conformance.Interface.Name}.{member.Name}"],
                            label: $"uses `{effect}`", help: $"declare `{effect}` on `{conformance.Interface.Name}.{member.Name}`, or pass a function without it");
                }
    }

    /// <summary>The functions and lambdas <paramref name="call"/> passes to function-typed parameters, which charge their effects to the caller (R16).</summary>
    private static IEnumerable<(BoundExpr Value, FunctionSymbol Function)> Passed(BoundCall call)
    {
        for (var i = 0; i < call.Arguments.Length && i < call.Function.Parameters.Count; i++)
        {
            if (call.Function.Parameters[i].Type is not FunctionType) continue;
            var value = call.Arguments[i];
            while (value is BoundConversion conversion) value = conversion.Operand;
            if (value is BoundLambda l) yield return (value, l.Lambda);
            else if (value is BoundFunctionRef r) yield return (value, r.Function);
        }
    }

    /// <summary>
    /// The declared effects of function-typed values other than lambdas, functions and forwarded parameters that
    /// <paramref name="call"/> passes to function-typed parameters, such as a local holding a lambda (R16).
    /// </summary>
    private static IEnumerable<string> PassedValues(BoundCall call)
    {
        for (var i = 0; i < call.Arguments.Length && i < call.Function.Parameters.Count; i++)
        {
            if (call.Function.Parameters[i].Type is not FunctionType) continue;
            var value = call.Arguments[i];
            while (value is BoundConversion conversion) value = conversion.Operand;
            if (value is BoundLambda or BoundFunctionRef or BoundLocal { Local: ParameterSymbol }) continue;
            if (value.Type is FunctionType type)
                foreach (var effect in type.Uses) yield return effect;
        }
    }

    /// <summary>
    /// A struct's <c>requires</c> check is pure to its callers: the violations it collects on failure come from
    /// <c>context.allocator</c> like any error payload (Question 338).
    /// </summary>
    private static bool IsCheck(FunctionSymbol fn) => fn is { Name: "requires", Owner: not null, Decl: null };

    /// <summary>The declared set: the <c>uses</c> clause, or pure for a public function or an abstract method; null when inferred.</summary>
    private static HashSet<string>? Declared(Compilation c, FunctionSymbol fn, Dictionary<ModuleSymbol, Dictionary<string, string[]>> aliases, HashSet<UsesClause> reported)
    {
        if (fn.IsInstance) return null;
        if (IsCheck(fn)) return new HashSet<string>();
        var uses = fn.Decl?.Uses;
        var set = uses is null ? null : Resolve(c, fn, uses, aliases, reported).ToHashSet();
        // A getter or constructor has no `uses` clause to write (§14 bounds interface methods, not properties), so it infers.
        if (fn == c.EntryModule.Main || fn.Kind is FunctionKind.Main or FunctionKind.Test or FunctionKind.Getter or FunctionKind.Constructor) return null;
        return set ?? (fn.Access == Accessibility.Public || fn.IsAbstract ? new HashSet<string>() : null);
    }

    /// <summary>The primitives a <c>uses</c> clause names, aliases expanded; BYN6003 for an unknown name, reported once per clause.</summary>
    private static IEnumerable<string> Resolve(Compilation c, FunctionSymbol fn, UsesClause uses, Dictionary<ModuleSymbol, Dictionary<string, string[]>> aliases, HashSet<UsesClause>? reported)
    {
        var report = reported is not null && fn.File is not null && reported.Add(uses);
        var local = aliases.GetValueOrDefault(fn.Module) ?? new();
        var result = new List<string>();
        foreach (var name in uses.Effects)
        {
            if (Primitives.Contains(name.Text)) result.Add(name.Text);
            else if (local.TryGetValue(name.Text, out var expanded)) result.AddRange(expanded);
            else if (report) ReportUnknown(c, fn.File!.Source, name);
        }
        return result;
    }

    /// <summary>Each <c>effect name = a, b</c> of <paramref name="module"/>, expanded to primitives.</summary>
    private static Dictionary<string, string[]> ResolveAliases(Compilation c, ModuleSymbol module)
    {
        var decls = new Dictionary<string, (EffectDecl Decl, FileContext File)>(StringComparer.Ordinal);
        foreach (var file in module.Files)
            foreach (var d in file.Tree.Root.Members.OfType<EffectDecl>())
            {
                if (Primitives.Contains(d.Name.Text) || !decls.TryAdd(d.Name.Text, (d, file)))
                    c.Report(DiagnosticCodes.DuplicateDeclaration, file.Source, d.Name.Span, [d.Name.Text],
                        label: Primitives.Contains(d.Name.Text) ? "already an effect" : null);
                Naming.CheckValue(c, file.Source, d.Name);
            }
        var resolved = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string[] Expand(string name, HashSet<string> visiting)
        {
            if (resolved.TryGetValue(name, out var done)) return done;
            var (decl, file) = decls[name];
            visiting.Add(name);
            var result = new List<string>();
            foreach (var e in decl.Effects)
            {
                if (Primitives.Contains(e.Text)) result.Add(e.Text);
                else if (visiting.Contains(e.Text))
                    c.Report(DiagnosticCodes.UnknownEffect, file.Source, e.Span, [e.Text], label: "the alias refers to itself", help: UnknownEffectHelp);
                else if (decls.ContainsKey(e.Text)) result.AddRange(Expand(e.Text, visiting));
                else ReportUnknown(c, file.Source, e);
            }
            visiting.Remove(name);
            return resolved[name] = result.Distinct().ToArray();
        }
        foreach (var name in decls.Keys) Expand(name, new HashSet<string>(StringComparer.Ordinal));
        return resolved;
    }

    private const string UnknownEffectHelp =
        "choose one of `alloc`, `suspend`, `net`, `fs`, `clock`, `random`, `env`, `process`, `console`, or a declared effect alias";

    internal static void ReportUnknown(Compilation c, SourceText source, Name name) =>
        c.Report(DiagnosticCodes.UnknownEffect, source, name.Span, [name.Text], label: "not an effect or an `effect` alias of this module", help: UnknownEffectHelp);

    /// <summary>Adds <paramref name="effects"/> to the <c>uses</c> clause, or writes one after the rest of the signature (clause order §5).</summary>
    private static Fix DeclareFix(SourceText source, FunctionDecl decl, IEnumerable<string> effects)
    {
        var list = string.Join(", ", effects);
        if (decl.Uses is { } uses) return new Fix(new TextSpan(uses.Span.End, 0), ", " + list);
        var close = decl.Params.Length > 0 ? decl.Params[^1].Span.End : decl.ParamsStart;
        while (close < source.Bytes.Length && source.Bytes.Span[close] != (byte)')') close++;
        var end = new[] { close + 1, decl.Fails?.Span.End ?? 0, decl.Becomes?.Span.End ?? 0, decl.Where.Length > 0 ? decl.Where[^1].Span.End : 0 }.Max();
        return new Fix(new TextSpan(end, 0), " uses " + list);
    }

    private static string[] Builtin(BuiltinFunction f) => f switch
    {
        BuiltinFunction.Print or BuiltinFunction.IoStdout or BuiltinFunction.IoStderr or BuiltinFunction.IoStdin => ["console"],
        BuiltinFunction.IoReadFile or BuiltinFunction.IoReadText or BuiltinFunction.IoListDir => ["fs", "alloc"],
        BuiltinFunction.IoWriteFile => ["fs"],
        BuiltinFunction.IoRun or BuiltinFunction.IoRunBatch => ["process", "alloc"],
        BuiltinFunction.IoMonotonicNanos => ["clock"],
        BuiltinFunction.ListNew or BuiltinFunction.OwnedNew or BuiltinFunction.StrBufGrow or BuiltinFunction.StrBufOver or BuiltinFunction.ArenaNew
            or BuiltinFunction.Utf8Decode or BuiltinFunction.Utf8TryDecode or BuiltinFunction.Utf8DecodeReplace or BuiltinFunction.HexEncode or BuiltinFunction.HexDecode
            or BuiltinFunction.SymbolIntern or BuiltinFunction.PathRelative or BuiltinFunction.PathFileName or BuiltinFunction.PathDirectoryName
            or BuiltinFunction.PathCombine or BuiltinFunction.PathCombineSpan or BuiltinFunction.PathFullPath => ["alloc"],
        _ => [],
    };

    private static string[] Member(BuiltinMember m) => m switch
    {
        BuiltinMember.IoWrite => ["console"],
        BuiltinMember.IoReadAll => ["console", "alloc"],
        BuiltinMember.SpanCopy or BuiltinMember.StringCopy or BuiltinMember.ListCopy or BuiltinMember.MapCopy or BuiltinMember.StrBufCopy
            or BuiltinMember.IntToString or BuiltinMember.ListAdd or BuiltinMember.ListReplace or BuiltinMember.MapSlot or BuiltinMember.MapIndexSet
            or BuiltinMember.StringLowercased or BuiltinMember.StringUppercased or BuiltinMember.StringReplaced or BuiltinMember.StringRepeated or BuiltinMember.StringPadded or BuiltinMember.BytesToString
            or BuiltinMember.StringJoined or BuiltinMember.Utf16Copy => ["alloc"],
        _ => [],
    };
}
