using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Serialization;

namespace DaouCalendarOverlay.Services;

public static class NativeBridgeProtocol
{
    public const string PipeName = "DaouCalendarOverlay.NativeBridge.v8";
    public const string HostName = "com.daou.calendar_overlay";
    public const string ExtensionId = "gkchgbpcbljkgabjcgjelacfkphcmhmi";
    public const string ExtensionOrigin = "chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/";
    /// <summary>
    /// 네이티브 브리지 메시지 프로토콜 버전. 호환 불가 변경 시에만 올린다(파이프 이름도 함께).
    /// 7.2.0에서 2로 올렸다(postResult 스키마 교체).
    /// </summary>
    public const int ProtocolVersion = 2;

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        if (!await ReadExactAsync(stream, lengthBytes, cancellationToken))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 0 || length > maxBytes)
            throw new InvalidDataException($"Invalid native message length: {length}");

        var payload = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, payload, cancellationToken))
            throw new EndOfStreamException("Native message ended before the declared payload length.");

        return payload;
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }
}

public sealed class NativeBridgeRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("result")]
    public BridgeResultPayload? Result { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [JsonPropertyName("extensionVersion")]
    public string? ExtensionVersion { get; set; }

    /// <summary>
    /// getConfig·postResult 모두 필수(7.2.0 worker부터 2). 값이 <see cref="NativeBridgeProtocol.ProtocolVersion"/>과 다르거나 없으면
    /// getConfig는 noFetchReason=protocol_mismatch로 답하고 postResult는 거부한다.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    public int? ProtocolVersion { get; set; }
}

public sealed class NativeBridgeResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("config")]
    public BridgeConfigResponse? Config { get; init; }

    public static NativeBridgeResponse Success(BridgeConfigResponse? config = null) => new()
    {
        Ok = true,
        Config = config
    };

    public static NativeBridgeResponse Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
