using DaouCalendarOverlay.Services;
using System.Buffers.Binary;

namespace DaouCalendarOverlay.Tests;

public sealed class NativeBridgeProtocolTests
{
    private static MemoryStream FrameOf(int declaredLength, byte[] payload)
    {
        var stream = new MemoryStream();
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, declaredLength);
        stream.Write(lengthBytes, 0, lengthBytes.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Position = 0;
        return stream;
    }

    private sealed class OneBytePerReadStream : MemoryStream
    {
        public OneBytePerReadStream(byte[] buffer) : base(buffer, writable: false) { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer.Length > 1 ? buffer[..1] : buffer, cancellationToken);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsUtf8Payload()
    {
        var payload = NativeBridgeProtocol.Utf8("{\"type\":\"sync\",\"주제\":\"한글 값\"}");
        var stream = new MemoryStream();

        await NativeBridgeProtocol.WriteFrameAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        var actual = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.Equal(payload, actual);
    }

    [Fact]
    public async Task WriteFrame_PrefixesLittleEndianLength()
    {
        var payload = NativeBridgeProtocol.Utf8("{\"type\":\"sync\",\"주제\":\"한글 값\"}");
        var stream = new MemoryStream();

        await NativeBridgeProtocol.WriteFrameAsync(stream, payload, CancellationToken.None);
        var bytes = stream.ToArray();

        Assert.Equal(4 + payload.Length, bytes.Length);
        Assert.Equal(payload.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)));
    }

    [Fact]
    public async Task WriteThenRead_EmptyPayload_ReturnsEmptyArrayNotNull()
    {
        var stream = new MemoryStream();

        await NativeBridgeProtocol.WriteFrameAsync(stream, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        Assert.Equal(4, stream.ToArray().Length);

        stream.Position = 0;
        var result = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task ReadFrame_EmptyStream_ReturnsNull()
    {
        var stream = new MemoryStream();

        var result = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadFrame_TruncatedHeader_ReturnsNull()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadFrame_TruncatedPayload_ThrowsEndOfStream()
    {
        var stream = FrameOf(10, new byte[3]);

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_LengthExceedsMax_ThrowsInvalidData()
    {
        var stream = FrameOf(1025, Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NativeBridgeProtocol.ReadFrameAsync(stream, 1024, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_LengthEqualsMax_Succeeds()
    {
        var payload = new byte[1024];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 256);

        var stream = FrameOf(1024, payload);
        var result = await NativeBridgeProtocol.ReadFrameAsync(stream, 1024, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadFrame_NegativeLength_ThrowsInvalidData()
    {
        var stream = FrameOf(-1, Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_PartialReads_ReassemblesPayload()
    {
        var payload = NativeBridgeProtocol.Utf8("{\"type\":\"partial\"}");
        var frameBytes = FrameOf(payload.Length, payload).ToArray();
        var stream = new OneBytePerReadStream(frameBytes);

        var result = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadFrame_TwoFramesBackToBack_ReadsSequentially()
    {
        var first = NativeBridgeProtocol.Utf8("first");
        var second = NativeBridgeProtocol.Utf8("second-payload");
        var stream = new MemoryStream();

        await NativeBridgeProtocol.WriteFrameAsync(stream, first, CancellationToken.None);
        await NativeBridgeProtocol.WriteFrameAsync(stream, second, CancellationToken.None);
        stream.Position = 0;

        var firstResult = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);
        var secondResult = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);
        var thirdResult = await NativeBridgeProtocol.ReadFrameAsync(stream, 8 * 1024 * 1024, CancellationToken.None);

        Assert.Equal(first, firstResult);
        Assert.Equal(second, secondResult);
        Assert.Null(thirdResult);
    }
}
