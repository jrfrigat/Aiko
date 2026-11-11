using System.Text;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Server.Endpoints;
using Xunit;
using Xunit.Abstractions;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The live event stream builds its frames in a buffer that belongs to the connection: the id is formatted
/// into it, the envelope is serialized into it and the bytes leave in one write. These specs pin the frame
/// format the browser's EventSource reads and the cost of one frame after warm-up.
/// </summary>
public sealed class EventStreamWriterSpecs(ITestOutputHelper output)
{
    /// <summary>Managed bytes one frame may allocate after warm-up. The writer targets zero.</summary>
    private const long MaxBytesPerFrame = 0;

    [Fact]
    public async Task A_frame_carries_the_id_and_the_event_envelope()
    {
        var item = new AikoEvent(
            42,
            "project-1",
            AikoEventTypes.CardUpdated,
            new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero),
            "{\"a\":1}");
        using var frames = new EventStreamWriter();
        var sink = new CountingStream(collect: true);

        await frames.WriteAsync(sink, item, CancellationToken.None);

        var text = Encoding.UTF8.GetString(sink.ToArray());
        Assert.StartsWith("id: 42\ndata: ", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", text, StringComparison.Ordinal);

        var envelope = text["id: 42\ndata: ".Length..^2];
        using var document = JsonDocument.Parse(envelope);
        var root = document.RootElement;
        Assert.Equal(42, root.GetProperty("id").GetInt64());
        Assert.Equal("project-1", root.GetProperty("projectId").GetString());
        Assert.Equal("card.updated", root.GetProperty("type").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero),
            root.GetProperty("occurredAtUtc").GetDateTimeOffset());
        Assert.Equal("{\"a\":1}", root.GetProperty("payloadJson").GetString());
        Assert.Single(sink.Frames());
    }

    [Fact]
    public async Task A_frame_costs_no_managed_allocation_after_warm_up()
    {
        var item = new AikoEvent(
            7,
            "project-1",
            AikoEventTypes.CardUpdated,
            new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero),
            $"{{\"title\":\"{new string('x', 1024)}\"}}");
        using var frames = new EventStreamWriter();
        var sink = new CountingStream(collect: false);

        // The first frames grow the buffer and initialize the JSON writer to this payload's size.
        for (var index = 0; index < 16; index++)
        {
            await frames.WriteAsync(sink, item, CancellationToken.None);
        }

        const int MeasuredFrames = 500;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < MeasuredFrames; index++)
        {
            await frames.WriteAsync(sink, item, CancellationToken.None);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine(
            $"{allocated} managed bytes for {MeasuredFrames} frames " +
            $"({allocated / (double)MeasuredFrames:F2} B/frame).");
        Assert.True(
            allocated <= MaxBytesPerFrame * MeasuredFrames,
            $"{allocated} bytes were allocated for {MeasuredFrames} frames.");
    }

    /// <summary>A stream that counts frames, optionally keeping their bytes for the format spec.</summary>
    private sealed class CountingStream(bool collect) : Stream
    {
        private readonly MemoryStream? _collected = collect ? new MemoryStream() : null;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _collected?.ToArray() ?? [];

        /// <summary>Splits the collected bytes into SSE frames, one per blank line.</summary>
        public IReadOnlyList<string> Frames() =>
            Encoding.UTF8.GetString(ToArray())
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count) =>
            _collected?.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _collected?.Write(buffer.Span);
            return default;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
