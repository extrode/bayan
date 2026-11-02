using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Cli;

/// <summary>
/// <c>bayan features --used &lt;path&gt;</c>: the syntax node kinds, builtins and diagnostic codes a build
/// touched, tests included, as JSON (plan J22). The reference-app coverage doc maps them to spec sections.
/// </summary>
internal static class Features
{
    public const int Version = 1;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!args.Contains("--used"))
        {
            stderr.WriteLine(Messages.CommandUsage.Text("bayan features --used <path>"));
            return CliApp.ExitUsage;
        }
        args = args.Where(a => a != "--used").ToArray();
        if (CliApp.ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return CliApp.ExitUsage;
        if (CliApp.Compile(o, stderr, includeTests: true) is not { } compiled) return CliApp.ExitFailure;
        stdout.Write(ToJson(compiled.Compilation));
        return CliApp.ExitOk;
    }

    public static string ToJson(Compilation c)
    {
        var nodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in c.Modules.SelectMany(m => m.Files))
            foreach (var node in Declarations(file.Tree.Root.Members))
                nodes.Add(node.GetType().Name);

        var builtins = new SortedSet<string>(StringComparer.Ordinal);
        var functions = c.Modules.SelectMany(m => m.AllFunctions.Concat(m.Tests.Select(t => t.Function))).Distinct();
        foreach (var body in functions.Select(f => f.Body).OfType<BoundBlock>())
        {
            foreach (var node in BoundTree.Descendants(body))
            {
                nodes.Add(node.Syntax.GetType().Name);
                if (node.Syntax is CallExpr { Because: { } because }) nodes.Add(because.GetType().Name);
                if (node is BoundBuiltinCall call) builtins.Add(call.Member.ToString());
                else if (node is BoundCall { Function.Builtin: not BuiltinFunction.None and var builtin }) builtins.Add(builtin.ToString());
            }
        }

        var diagnostics = new SortedSet<string>(c.Diagnostics.Items.Select(d => d.Code), StringComparer.Ordinal);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            foreach (var (name, items) in (ReadOnlySpan<(string, SortedSet<string>)>)[("nodes", nodes), ("builtins", builtins), ("diagnostics", diagnostics)])
            {
                w.WriteStartArray(name);
                foreach (var item in items) w.WriteStringValue(item);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>Each declaration, nested members included, then its attributes and their <c>because</c> clauses.</summary>
    private static IEnumerable<Node> Declarations(IEnumerable<Decl> decls)
    {
        foreach (var decl in decls)
        {
            yield return decl;
            foreach (var attribute in decl.Attributes)
            {
                yield return attribute;
                if (attribute.Because is { } because) yield return because;
            }
            var members = decl switch
            {
                StructDecl d => d.Members,
                InterfaceDecl d => d.Members,
                ExtensionDecl d => d.Members,
                _ => [],
            };
            foreach (var member in Declarations(members)) yield return member;
        }
    }
}
