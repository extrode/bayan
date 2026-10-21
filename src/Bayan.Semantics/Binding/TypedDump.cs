using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Semantics.Symbols;

namespace Bayan.Semantics.Binding;

/// <summary>
/// <c>bayan types</c>: every bound function with the resolved type of each parameter and local, as
/// indented JSON (format in <c>docs/typed-dump.md</c>). Functions go in module, file and source
/// order; bindings in bind order, parameters first. Generic templates only, never instances.
/// </summary>
public static class TypedDump
{
    public const int Version = 1;

    public static string ToJson(Compilation c)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteStartArray("functions");
            foreach (var module in c.Modules)
            {
                var functions = module.AllFunctions
                    .Where(f => f.File is not null && f.Location is not null && !f.IsInstance)
                    .OrderBy(f => module.Files.IndexOf(f.File!))
                    .ThenBy(f => f.Location!.Value.Span.Start)
                    .ThenBy(f => f.Location!.Value.Span.End);
                foreach (var fn in functions)
                {
                    w.WriteStartObject();
                    w.WriteString("path", PathOf(c, fn.File!.Source.Path));
                    w.WriteString("name", fn.Owner is null ? fn.Name : fn.Owner.Name + "." + fn.Name);
                    WriteSpan(w, fn.Location!.Value);
                    w.WriteStartArray("bindings");
                    foreach (var local in fn.Locals.Where(l => l.Kind != LocalKind.Temp && l.Location is not null))
                    {
                        w.WriteStartObject();
                        w.WriteString("name", local.Name);
                        w.WriteString("kind", KindOf(local));
                        WriteSpan(w, local.Location!.Value);
                        w.WriteString("type", local.Type.DisplayName);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void WriteSpan(Utf8JsonWriter w, Location location)
    {
        w.WriteStartObject("span");
        w.WriteNumber("start", location.Span.Start);
        w.WriteNumber("end", location.Span.End);
        w.WriteEndObject();
    }

    private static string PathOf(Compilation c, string path) =>
        (Path.IsPathRooted(path) ? Path.GetRelativePath(c.DisplayRoot, path) : path).Replace('\\', '/');

    private static string KindOf(LocalSymbol local) => local.Kind switch
    {
        LocalKind.Parameter => "param",
        LocalKind.Var => "var",
        LocalKind.Const => "const",
        LocalKind.ForVariable => "for",
        LocalKind.PatternVariable => "pattern",
        _ => local.IsCatch ? "catch" : "let",
    };
}
