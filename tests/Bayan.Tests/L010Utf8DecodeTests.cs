using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// L010 (docs/laws/010-utf8-decode-exact.md): a compiled Bayan driver decodes edge-case and seeded random
/// byte sequences with <c>string.from</c>, <c>string.tryFrom</c> and <c>string.invalidUtf8At</c>, and every
/// verdict, offset and round trip must match <see cref="Rune.DecodeFromUtf8"/>.
/// </summary>
[Trait("Law", "L010")]
public sealed class L010Utf8DecodeTests(ITestOutputHelper output)
{
    private const int Seed = 0x4C010;
    private const int RandomCount = 4000;

    /// <summary>Per stdin line of hex bytes: invalid offset (-1 when valid), from ok, tryFrom ok, and both round trips.</summary>
    private const string Driver = """
        // L010 driver: each stdin line is lowercase hex bytes.
        u8 nibble(u8 c) {
            if (c <= 0x39) return c - 0x30;
            return c - 0x57;
        }

        bool same(ReadOnlySpan<u8> a, ReadOnlySpan<u8> b) {
            if (a.length != b.length) return false;
            for (let i in 0..a.length) {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        bool decodes(ReadOnlySpan<u8> bytes) fails Utf8Error {
            try string.from(bytes);
            return true;
        }

        string check(ReadOnlySpan<u8> bytes) {
            let fromOk = decodes(bytes) catch (e) switch {
                .invalidSequence => false,
            };
            let from = string.from(bytes) catch (e) switch {
                .invalidSequence => "",
            };
            let at = string.invalidUtf8At(bytes) ?? -1;
            let t = string.tryFrom(bytes);
            let tryOk = t != null;
            let tryText = t ?? "";
            return $"{at} {fromOk} {tryOk} {same(a: from.bytes, b: bytes)} {same(a: tryText.bytes, b: bytes)}";
        }

        void main() {
            let input = Io.stdin.readAll();
            if (!input.isOk) {
                print("read failed");
                return;
            }
            let text = input.value.bytes;
            var line = List<u8>();
            i64 i = 0;
            while (i < text.length) {
                let c = text[i];
                if (c == 0x0A) {
                    print(check(line.asSpan()));
                    line.clear();
                    i += 1;
                } else {
                    line.add(nibble(c) * 16 + nibble(text[i + 1]));
                    i += 2;
                }
            }
        }
        """;

    /// <summary>Boundaries of each encoded length, overlongs, surrogates, above U+10FFFF, truncations, stray bytes.</summary>
    private static readonly string[] Edges =
    [
        "", "00", "7f", "c280", "dfbf", "e0a080", "efbfbf", "f0908080", "f48fbfbf",
        "ed9fbf", "ee8080", "efbfbe", "c0", "c080", "c1bf", "e080", "e08080", "e09fbf",
        "f0808080", "f08fbfbf", "eda080", "edafbf", "edb080", "edbfbf", "eda0bdedb880",
        "f4908080", "f7bfbfbf", "c2", "df", "e0a0", "efbf", "f090", "f09080", "f48fbf",
        "80", "bf", "8080", "c2c2", "e0a0c2", "c27f", "e0a07f", "f09080c2",
        "f5", "f6", "f7", "f8", "f9", "fa", "fb", "fc", "fd", "fe", "ff", "f8888080", "fc8480808080",
    ];

    [Fact]
    public void DecodeAcceptsExactlyValidUtf8()
    {
        var inputs = new List<byte[]>();
        foreach (var hex in Edges)
        {
            var e = Convert.FromHexString(hex);
            inputs.AddRange([e, [0x61, .. e], [.. e, 0x61], [.. e, .. e], [0xE2, 0x82, 0xAC, .. e]]);
        }
        var random = new Random(Seed);
        for (var i = 0; i < RandomCount; i++) inputs.Add(RandomSequence(random));

        var lines = RunDriver(inputs);
        Assert.Equal(inputs.Count, lines.Length);
        var failures = new List<string>();
        var valid = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var expected = FirstInvalid(inputs[i]);
            if (expected < 0) valid++;
            var want = expected < 0 ? "-1 true true true true" : $"{expected} false false";
            var got = expected < 0 ? lines[i] : string.Join(' ', lines[i].Split(' ').Take(3));
            if (got != want) failures.Add($"input {Convert.ToHexStringLower(inputs[i])}: want `{want}`, got `{lines[i]}`");
        }
        output.WriteLine($"seed {Seed}, {inputs.Count} inputs ({Edges.Length} edge cases x 5, {RandomCount} random), {valid} valid");
        Assert.True(failures.Count == 0, $"seed {Seed}, {failures.Count} failures:\n{string.Join("\n", failures.Take(20))}");
    }

    /// <summary>Length 0..16, biased toward lead and continuation bytes; half are runes with one byte possibly changed.</summary>
    private static byte[] RandomSequence(Random random)
    {
        var length = random.Next(17);
        var bytes = new List<byte>();
        if (random.Next(2) == 0)
        {
            Span<byte> buffer = stackalloc byte[4];
            while (bytes.Count < length)
            {
                var value = random.Next(4) switch
                {
                    0 => random.Next(0x80),
                    1 => random.Next(0x80, 0x800),
                    2 => random.Next(0x800, 0x10000),
                    _ => random.Next(0x10000, 0x110000),
                };
                if (value is >= 0xD800 and <= 0xDFFF) continue;
                var n = new Rune(value).EncodeToUtf8(buffer);
                for (var k = 0; k < n; k++) bytes.Add(buffer[k]);
            }
            if (bytes.Count > 0 && random.Next(2) == 0) bytes[random.Next(bytes.Count)] = (byte)random.Next(256);
            if (bytes.Count > 0 && random.Next(4) == 0) bytes.RemoveAt(bytes.Count - 1);
            return [.. bytes];
        }
        for (var i = 0; i < length; i++)
        {
            bytes.Add(random.Next(4) switch
            {
                0 => (byte)random.Next(0x80),
                1 => (byte)random.Next(0x80, 0xC0),
                2 => (byte)random.Next(0xC0, 0xF8),
                _ => (byte)random.Next(0x80, 0x100),
            });
        }
        return [.. bytes];
    }

    /// <summary>The reference: the offset where the first invalid sequence starts, or -1 when the bytes are valid UTF-8.</summary>
    private static int FirstInvalid(byte[] bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            if (Rune.DecodeFromUtf8(bytes.AsSpan(i), out _, out var consumed) != System.Buffers.OperationStatus.Done) return i;
            i += consumed;
        }
        return -1;
    }

    private static string[] RunDriver(List<byte[]> inputs)
    {
        var c = Compilation.Create([SourceText.FromString("l010.byn", Driver)], ".");
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Debug), BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        var stdin = new StringBuilder();
        foreach (var bytes in inputs) stdin.Append(Convert.ToHexStringLower(bytes)).Append('\n');
        p.StandardInput.Write(stdin.ToString());
        p.StandardInput.Close();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            Assert.Fail("driver timed out after 120 s");
        }
        Assert.True(p.ExitCode == 0, $"driver exit {p.ExitCode}\n{stderr.Result}");
        return stdout.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
