using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Diagnostics;

/// <summary>
/// <c>--diagnostics json</c>: one JSON object per line (<c>code</c>, <c>severity</c>, <c>file</c>,
/// <c>span</c>, <c>message</c>, <c>label</c>, <c>help</c>, <c>fixes</c>). Spans carry byte offsets
/// (<c>start</c>, <c>end</c>) plus one-based <c>line</c>/<c>column</c> in Unicode scalars;
/// <c>file</c> and <c>span</c> are null for diagnostics without a location.
/// </summary>
public static class DiagnosticJson
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Render(Diagnostic diagnostic)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, Options))
        {
            w.WriteStartObject();
            w.WriteString("code", diagnostic.Code);
            w.WriteString("severity", diagnostic.Severity switch
            {
                Severity.Error => "error",
                Severity.Warning => "warning",
                _ => "note",
            });
            if (diagnostic.Source is { } source)
            {
                w.WriteString("file", source.Path);
                w.WritePropertyName("span");
                WriteSpan(w, source, diagnostic.Span);
            }
            else
            {
                w.WriteNull("file");
                w.WriteNull("span");
            }
            w.WriteString("message", diagnostic.Message);
            WriteOptional(w, "label", diagnostic.Label);
            WriteOptional(w, "help", diagnostic.Help);
            w.WriteStartArray("fixes");
            if (diagnostic.Fix is { } fix && diagnostic.Source is { } fixSource)
            {
                w.WriteStartObject();
                w.WritePropertyName("span");
                WriteSpan(w, fixSource, fix.Span);
                w.WriteString("replacement", fix.Replacement);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Every diagnostic, one per line, each line ending in <c>\n</c>.</summary>
    public static string RenderAll(IEnumerable<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var d in diagnostics) sb.Append(Render(d)).Append('\n');
        return sb.ToString();
    }

    private static void WriteSpan(Utf8JsonWriter w, SourceText source, TextSpan span)
    {
        var (line, column) = source.GetLinePosition(span.Start);
        var (endLine, endColumn) = source.GetLinePosition(span.End);
        w.WriteStartObject();
        w.WriteNumber("start", span.Start);
        w.WriteNumber("end", span.End);
        w.WriteNumber("line", line);
        w.WriteNumber("column", column);
        w.WriteNumber("endLine", endLine);
        w.WriteNumber("endColumn", endColumn);
        w.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }
}
