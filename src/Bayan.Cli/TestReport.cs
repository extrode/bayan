using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bayan.Cli;

public enum TestStatus { Pass, Fail, Panic, Timeout }

public sealed record TestResult(string Name, string File, int Line, TestStatus Status, TimeSpan Elapsed, string Output);

/// <summary>
/// <c>out/test/results.json</c> and <c>bayan test --json</c>: the package, mode and target, one
/// entry per test (<c>name</c>, <c>file</c>, <c>line</c>, <c>status</c> pass/fail/panic/timeout,
/// <c>durationMs</c>, <c>output</c> of a test that didn't pass), and a summary.
/// </summary>
public sealed record TestReport(string? Package, string Mode, string Target, IReadOnlyList<TestResult> Tests, TimeSpan Elapsed)
{
    public int Count(TestStatus status) => Tests.Count(t => t.Status == status);

    public string ToJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            if (Package is null) w.WriteNull("package");
            else w.WriteString("package", Package);
            w.WriteString("mode", Mode);
            w.WriteString("target", Target);
            w.WriteStartArray("tests");
            foreach (var t in Tests)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                w.WriteString("file", t.File);
                w.WriteNumber("line", t.Line);
                w.WriteString("status", t.Status.ToString().ToLowerInvariant());
                w.WriteNumber("durationMs", Math.Round(t.Elapsed.TotalMilliseconds, 1));
                w.WriteString("output", t.Output);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("summary");
            w.WriteNumber("total", Tests.Count);
            w.WriteNumber("passed", Count(TestStatus.Pass));
            w.WriteNumber("failed", Count(TestStatus.Fail));
            w.WriteNumber("panicked", Count(TestStatus.Panic));
            w.WriteNumber("timedOut", Count(TestStatus.Timeout));
            w.WriteNumber("durationMs", Math.Round(Elapsed.TotalMilliseconds, 1));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }
}
