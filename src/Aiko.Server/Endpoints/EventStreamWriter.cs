using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Writes the SSE frames of one live event stream into a buffer that survives between events.
/// </summary>
/// <remarks>
/// The frame is built where it is sent: the id is formatted into the buffer, the event envelope is
/// serialized into it by the source-generated context, and the bytes reach the response in one write.
/// The buffer and the JSON writer belong to the connection, so after the buffer has grown to the largest
/// payload of the stream, a frame costs no managed allocation.
/// </remarks>
internal sealed class EventStreamWriter : IDisposable
{
    private const int FrameSuffixLength = 2;
    private const int LongTextLength = 20;

    private readonly ArrayBufferWriter<byte> _buffer = new(4096);
    private readonly Utf8JsonWriter _json;

    /// <summary>Creates a writer for one stream.</summary>
    public EventStreamWriter() => _json = new Utf8JsonWriter(_buffer);

    /// <summary>Writes one event as an SSE frame and flushes it to the stream.</summary>
    public async ValueTask WriteAsync(Stream body, AikoEvent item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(item);

        _buffer.Clear();
        _buffer.Write("id: "u8);
        if (!Utf8Formatter.TryFormat(item.Id, _buffer.GetSpan(LongTextLength), out var idLength))
        {
            throw new InvalidOperationException("The event id did not fit its buffer.");
        }

        _buffer.Advance(idLength);
        _buffer.Write("\ndata: "u8);

        _json.Reset(_buffer);
        JsonSerializer.Serialize(_json, item, ServerJsonContext.Default.AikoEvent);
        _json.Flush();
        _buffer.Write("\n\n"u8);

        await body.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _json.Dispose();
}
