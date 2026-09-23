using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// accept 루프가 요청 처리와 분리돼 있는지를 실제 Named Pipe 왕복으로 검증한다.
/// DaouOffice 조회가 끝나지 않은 동안에도 새 연결(<c>getConfig</c>/<c>ping</c>)이 native host의
/// 2.5초 connect timeout 안에 응답을 받아야 하고, 종료는 조회에 묶이지 않아야 한다.
/// </summary>
public sealed class CalendarBridgePipeTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly DateTimeOffset RangeFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset RangeTo = new(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));

    private const string TestBaseUrl = "https://test.daouoffice.com";

    /// <summary>테스트가 붙잡아 두는 가짜 HTTP 전송. 취소되면 붙잡고 있던 응답도 함께 풀린다.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }

    private static AppSettings Settings() => new()
    {
        BaseUrl = TestBaseUrl,
        CalendarIds = { "12345" },
        RefreshMinutes = 5
    };

    /// <summary>운영 파이프 이름(실행 중인 앱)과 충돌하지 않도록 테스트마다 다른 이름을 쓴다.</summary>
    private static string NewPipeName() => "DaouCalendarOverlay.Tests." + Guid.NewGuid().ToString("N");

    private static TaskCompletionSource<HttpResponseMessage> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static StubHandler BlockingHandler(TaskCompletionSource<HttpResponseMessage> gate, TaskCompletionSource started) =>
        new((_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled());
            started.TrySetResult();
            return gate.Task;
        });

    private static NativeBridgeRequest GetConfigRequest() => new()
    {
        Type = "getConfig",
        ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion
    };

    private static NativeBridgeRequest PostResultRequest(string requestId) => new()
    {
        Type = "postResult",
        Result = new BridgeResultPayload
        {
            RequestId = requestId,
            CookieHeader = "SESSION=abc",
            CookieCount = 1
        }
    };

    private static async Task<NativeBridgeResponse> CallAsync(string pipeName, NativeBridgeRequest request, CancellationToken ct)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return await CallRawAsync(pipeName, JsonSerializer.SerializeToUtf8Bytes(request, options), ct);
    }

    /// <summary>확장이 보내는 JSON 원문을 그대로 파이프로 보낸다(서버 쪽 역직렬화 경로를 거치게 하기 위함).</summary>
    private static async Task<NativeBridgeResponse> CallRawAsync(string pipeName, byte[] utf8Json, CancellationToken ct)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2500, ct);
        await NativeBridgeProtocol.WriteFrameAsync(client, utf8Json, ct);
        var bytes = await NativeBridgeProtocol.ReadFrameAsync(client, 8 * 1024 * 1024, ct);
        Assert.NotNull(bytes);
        return JsonSerializer.Deserialize<NativeBridgeResponse>(bytes!, options)!;
    }

    private static CalendarBridgeServer StartServer(StubHandler handler, string pipeName)
    {
        var server = new CalendarBridgeServer(() => handler, pipeName: pipeName);
        server.Start();
        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
        return server;
    }

    /// <summary>파이프 왕복으로 getConfig가 fetch 지시를 받아온다.</summary>
    [Fact]
    public async Task Pipe_GetConfig_RoundTripsOverNamedPipe()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);

        try
        {
            var response = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);

            Assert.True(response.Ok);
            Assert.NotNull(response.Config);
            Assert.True(response.Config!.ShouldFetch);
            Assert.False(string.IsNullOrWhiteSpace(response.Config!.RequestId));
            Assert.Equal(TestBaseUrl, response.Config!.BaseUrl);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// 7.1.0 worker가 보내는 protocolVersion 포함 getConfig와, 이 필드가 없는 구버전 형식의 getConfig가
    /// 파이프 서버의 역직렬화를 거쳐 모두 정상 응답(ok + config)을 받는다.
    /// </summary>
    [Fact]
    public async Task Pipe_GetConfig_AcceptsRequestsWithAndWithoutProtocolVersion()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);
        var version = ChromeExtensionInstaller.ExpectedExtensionVersion;

        try
        {
            var withProtocol = await CallRawAsync(
                pipeName,
                Encoding.UTF8.GetBytes($"{{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"{version}\",\"protocolVersion\":1}}"),
                cts.Token).WaitAsync(WaitLimit);

            Assert.True(withProtocol.Ok, withProtocol.Error);
            Assert.NotNull(withProtocol.Config);
            Assert.True(withProtocol.Config!.ShouldFetch);

            var withoutProtocol = await CallRawAsync(
                pipeName,
                Encoding.UTF8.GetBytes($"{{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"{version}\"}}"),
                cts.Token).WaitAsync(WaitLimit);

            Assert.True(withoutProtocol.Ok, withoutProtocol.Error);
            Assert.NotNull(withoutProtocol.Config);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>조회가 끝나지 않아도 새 연결의 getConfig는 native host의 2.5초 timeout 안에 응답한다.</summary>
    [Fact]
    public async Task Pipe_GetConfigWhileResultFetchInFlight_RespondsWithinTwoSeconds()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);

        try
        {
            var issued = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            Assert.NotNull(issued.Config);
            Assert.True(issued.Config!.ShouldFetch);

            var ack = await CallAsync(pipeName, PostResultRequest(issued.Config!.RequestId), cts.Token).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await started.Task.WaitAsync(WaitLimit);

            var stopwatch = Stopwatch.StartNew();
            var during = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"조회 중 getConfig 응답이 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
            Assert.True(during.Ok);
            Assert.NotNull(during.Config);
            Assert.False(during.Config!.ShouldFetch);
            Assert.Equal(NoFetchReasons.LeaseActive, during.Config!.NoFetchReason);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>동시에 들어온 연결이 서로를 막지 않는다(인스턴스 4 + accept 분리).</summary>
    [Fact]
    public async Task Pipe_ThreeConcurrentPingClients_AllReceiveOk()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);

        try
        {
            var pings = new[]
            {
                CallAsync(pipeName, new NativeBridgeRequest { Type = "ping" }, cts.Token),
                CallAsync(pipeName, new NativeBridgeRequest { Type = "ping" }, cts.Token),
                CallAsync(pipeName, new NativeBridgeRequest { Type = "ping" }, cts.Token)
            };

            var responses = await Task.WhenAll(pings).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(3, responses.Length);
            Assert.All(responses, response => Assert.True(response.Ok));
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// 프레임을 보내지 않고 연결만 잡고 있는 클라이언트가 다른 연결을 막지 않는다.
    /// accept 루프가 요청 처리를 직렬로 기다리던 구조에서는 이 연결 하나로 브리지 전체가 멈춘다.
    /// </summary>
    [Fact]
    public async Task Pipe_StalledClientConnection_DoesNotBlockNextClient()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);

        using var stalled = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await stalled.ConnectAsync(2500, cts.Token);

            var stopwatch = Stopwatch.StartNew();
            var response = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            stopwatch.Stop();

            Assert.True(response.Ok);
            Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"멈춘 연결이 있는 동안 getConfig가 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>조회가 진행 중이어도 종료는 1초 안에 끝난다(트레이 종료 지연 제거).</summary>
    [Fact]
    public async Task Pipe_DisposeAsyncWhileFetchInFlight_CompletesWithinOneSecond()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var gate = NewGate();
        var started = NewSignal();
        var pipeName = NewPipeName();
        var server = StartServer(BlockingHandler(gate, started), pipeName);
        var disposed = false;

        try
        {
            var issued = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            Assert.NotNull(issued.Config);

            var ack = await CallAsync(pipeName, PostResultRequest(issued.Config!.RequestId), cts.Token).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await started.Task.WaitAsync(WaitLimit);

            var stopwatch = Stopwatch.StartNew();
            await server.DisposeAsync();
            stopwatch.Stop();
            disposed = true;

            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"DisposeAsync가 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
        }
        finally
        {
            gate.TrySetCanceled();
            if (!disposed)
                await server.DisposeAsync();
        }
    }
}
