using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bayan.LanguageServer.Rpc;

/// <summary>LSP base protocol: `Content-Length` framed JSON over a byte stream.</summary>
public sealed class JsonRpcTransport
{
    const int MaxMessage = 64 * 1024 * 1024;
    const int Chunk = 64 * 1024;
    const int MaxRetainedBody = 1024 * 1024;

    readonly Stream _in;
    readonly Stream _out;
    readonly Lock _writeLock = new();
    ArrayBufferWriter<byte> _body = new(16 * 1024);
    readonly Utf8JsonWriter _writer;
    readonly byte[] _header = new byte[256];
    readonly byte[] _head = new byte[32];

    public JsonRpcTransport(Stream input, Stream output)
    {
        // Console streams are unbuffered: ReadByte would be one syscall per header byte.
        _in = input is BufferedStream or MemoryStream ? input : new BufferedStream(input, 64 * 1024);
        _out = output;
        // Relaxed escaping: hover/markdown text stays readable and smaller (no \u escapes for `<`, `'`, non-ASCII).
        _writer = new Utf8JsonWriter(_body, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Reads one message. Returns null at end of stream.</summary>
    public JsonDocument? Read()
    {
        while (true)
        {
            int contentLength = -1;
            while (true)
            {
                int n = ReadLine(_header);
                if (n < 0) return null;
                if (n == 0)
                {
                    if (contentLength >= 0) break;
                    continue; // stray blank line before headers
                }
                var line = _header.AsSpan(0, n);
                var key = "Content-Length:"u8;
                if (line.Length > key.Length && Ascii.EqualsIgnoreCase(line[..key.Length], key)
                    && int.TryParse(line[key.Length..].Trim((byte)' '), out int len) && len is >= 0 and <= MaxMessage)
                    contentLength = len;
            }
            // JsonDocument keeps a reference to the memory it parses, so the array is owned by the document.
            // The buffer grows as bytes arrive, so a lying header cannot allocate up front.
            var buffer = new byte[Math.Min(contentLength, Chunk)];
            int read = 0;
            try
            {
                while (read < contentLength)
                {
                    if (read == buffer.Length) Array.Resize(ref buffer, (int)Math.Min(contentLength, 2L * buffer.Length));
                    int n = Math.Min(buffer.Length - read, Chunk);
                    _in.ReadExactly(buffer, read, n);
                    read += n;
                }
            }
            catch (EndOfStreamException) { return null; } // truncated body: the stream ended mid-message
            try { return JsonDocument.Parse(buffer); }
            catch (JsonException ex) { Console.Error.WriteLine($"bayan-lsp: bad message skipped: {ex.Message}"); }
        }
    }

    int ReadLine(byte[] into)
    {
        int len = 0;
        while (true)
        {
            int b = _in.ReadByte();
            if (b < 0) return -1;
            if (b == '\n') return len > 0 && into[len - 1] == '\r' ? len - 1 : len;
            if (len < into.Length) into[len++] = (byte)b;
        }
    }

    /// <summary>Writes one message; <paramref name="write"/> fills the JSON body.</summary>
    public void Write(Action<Utf8JsonWriter> write)
    {
        lock (_writeLock)
        {
            _body.ResetWrittenCount();
            _writer.Reset(_body);
            _writer.WriteStartObject();
            _writer.WriteString("jsonrpc"u8, "2.0"u8);
            write(_writer);
            _writer.WriteEndObject();
            _writer.Flush();

            "Content-Length: "u8.CopyTo(_head);
            int pos = 16;
            _body.WrittenCount.TryFormat(_head.AsSpan(pos), out int digits);
            pos += digits;
            "\r\n\r\n"u8.CopyTo(_head.AsSpan(pos));
            pos += 4;
            _out.Write(_head, 0, pos);
            _out.Write(_body.WrittenSpan);
            _out.Flush();
            // A large response would otherwise pin its buffer for the rest of the session.
            if (_body.WrittenCount > MaxRetainedBody)
            {
                _body = new ArrayBufferWriter<byte>(16 * 1024);
                _writer.Reset(_body);
            }
        }
    }
}
