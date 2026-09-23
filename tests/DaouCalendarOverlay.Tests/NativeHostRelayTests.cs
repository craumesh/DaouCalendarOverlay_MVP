using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class NativeHostRelayTests
{
    private const int ServerFrameLimit = 8 * 1024 * 1024;

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>테스트마다 새 파이프 이름을 만든다(운영 파이프 이름은 절대 열지 않는다).</summary>
    private static string NewPipeName() => "DaouCalendarOverlayTests." + Guid.NewGuid().ToString("N");

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>연결을 기다렸다가 프레임 하나를 받는다. 아무것도 오지 않으면 null을 돌려준다.</summary>
    private static async Task<byte[]?> RunServerAsync(NamedPipeServerStream server, byte[]? response)
    {
        try
        {
            await server.WaitForConnectionAsync();
            var received = await NativeBridgeProtocol.ReadFrameAsync(server, ServerFrameLimit, CancellationToken.None);
            if (received is not null && response is not null)
                await NativeBridgeProtocol.WriteFrameAsync(server, response, CancellationToken.None);

            return received;
        }
        catch (IOException)
        {
            // 클라이언트가 아무것도 쓰지 않고 끊으면 파이프가 깨진 것으로 보고된다.
            return null;
        }
    }

    private static NativeBridgeResponse Parse(byte[] utf8Json)
    {
        var parsed = JsonSerializer.Deserialize<NativeBridgeResponse>(
            utf8Json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public async Task ConnectAndExchangeAsync_RelaysRequestAndResponse_WhenServerIsSameExecutable()
    {
        var pipeName = NewPipeName();
        var request = Encoding.UTF8.GetBytes("{\"type\":\"getConfig\"}");
        var serverResponse = Encoding.UTF8.GetBytes("{\"ok\":true}");

        using var server = CreateServer(pipeName);
        var serverTask = RunServerAsync(server, serverResponse);

        var responseBytes = await NativeHostRelay.ConnectAndExchangeAsync(
            pipeName,
            request,
            Environment.ProcessPath,
            5000,
            CancellationToken.None).WaitAsync(WaitLimit);

        var received = await serverTask.WaitAsync(WaitLimit);

        Assert.Equal(request, received);
        Assert.Equal(serverResponse, responseBytes);
    }

    [Fact]
    public async Task ConnectAndExchangeAsync_DoesNotSendRequest_WhenServerExecutableDiffers()
    {
        var pipeName = NewPipeName();
        var request = Encoding.UTF8.GetBytes("{\"type\":\"postResult\"}");

        using var server = CreateServer(pipeName);
        var serverTask = RunServerAsync(server, Encoding.UTF8.GetBytes("{\"ok\":true}"));

        var responseBytes = await NativeHostRelay.ConnectAndExchangeAsync(
            pipeName,
            request,
            @"C:\Definitely\Not\Same\DaouCalendarOverlay.exe",
            5000,
            CancellationToken.None).WaitAsync(WaitLimit);

        var received = await serverTask.WaitAsync(WaitLimit);

        Assert.Null(received);

        var parsed = Parse(responseBytes);
        var error = parsed.Error ?? string.Empty;

        Assert.False(parsed.Ok);
        Assert.Contains("요청을 전송하지 않았습니다", error, StringComparison.Ordinal);
        // 연결 후 서버 PID를 실제로 조회해 신원 불일치로 막았음을 고정한다(PID 조회 실패로 막힌 것과 구분).
        Assert.Contains("실행 파일이 아니어서", error, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAndExchangeAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 취소는 오류 응답으로 바꾸지 않고 그대로 올려 보낸다(NativeMessagingHost의 바깥 catch가 처리).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeHostRelay.ConnectAndExchangeAsync(
            NewPipeName(),
            Encoding.UTF8.GetBytes("{\"type\":\"getConfig\"}"),
            Environment.ProcessPath,
            200,
            cts.Token).WaitAsync(WaitLimit));
    }

    [Fact]
    public async Task ConnectAndExchangeAsync_ReturnsFailResponse_WhenNoServerIsListening()
    {
        var responseBytes = await NativeHostRelay.ConnectAndExchangeAsync(
            NewPipeName(),
            Encoding.UTF8.GetBytes("{\"type\":\"getConfig\"}"),
            Environment.ProcessPath,
            200,
            CancellationToken.None).WaitAsync(WaitLimit);

        var parsed = Parse(responseBytes);
        var error = parsed.Error ?? string.Empty;

        Assert.False(parsed.Ok);
        Assert.Contains("Daou Calendar Overlay가 실행 중이지 않거나", error, StringComparison.Ordinal);
    }
}
