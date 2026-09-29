using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// accept 루프가 요청 처리와 분리돼 있는지를 실제 Named Pipe 왕복으로 검증한다.
/// 결과 처리가 끝나지 않은 동안에도 새 연결(<c>getConfig</c>/<c>ping</c>)이 native host의
/// 2.5초 connect timeout 안에 응답을 받아야 하고, 종료는 결과 처리에 묶이지 않아야 한다.
/// 결과 처리 시작은 <see cref="CalendarBridgeServer.ResultProcessingHook"/>(<see cref="ResultProcessingProbe"/>)으로 붙잡는다.
/// </summary>
public sealed class CalendarBridgePipeTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly DateTimeOffset RangeFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset RangeTo = new(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));

    private const string TestBaseUrl = "https://test.daouoffice.com";

    private const string OkBody =
        "{\"code\":200,\"data\":[{\"id\":\"1\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"summary\":\"테스트\",\"timeType\":\"timed\",\"startTime\":\"2026-09-22T10:00:00+09:00\",\"endTime\":\"2026-09-22T11:00:00+09:00\"}]}";

    private static AppSettings Settings() => new()
    {
        BaseUrl = TestBaseUrl,
        CalendarIds = { "12345" },
        RefreshMinutes = 5
    };

    /// <summary>운영 파이프 이름(실행 중인 앱)과 충돌하지 않도록 테스트마다 다른 이름을 쓴다.</summary>
    private static string NewPipeName() => "DaouCalendarOverlay.Tests." + Guid.NewGuid().ToString("N");

    private static NativeBridgeRequest GetConfigRequest() => new()
    {
        Type = "getConfig",
        ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion,
        ProtocolVersion = NativeBridgeProtocol.ProtocolVersion
    };

    /// <summary>7.2.0 worker가 보내는 성공 결과(설계 8.2 OkPayload).</summary>
    private static BridgeResultPayload OkPayload(string requestId) => new()
    {
        RequestId = requestId,
        Outcome = BridgeFetchOutcomes.Response,
        Status = 200,
        ResponseType = "basic",
        ContentType = "application/json;charset=UTF-8",
        Body = OkBody,
        BodyLength = OkBody.Length,
        ElapsedMs = 10
    };

    private static NativeBridgeRequest PostResultRequest(string requestId) => new()
    {
        Type = "postResult",
        ProtocolVersion = NativeBridgeProtocol.ProtocolVersion,
        Result = OkPayload(requestId)
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
        var bytes = await NativeBridgeProtocol.ReadFrameAsync(client, NativeHostRelay.PipeMessageLimit, ct);
        Assert.NotNull(bytes);
        return JsonSerializer.Deserialize<NativeBridgeResponse>(bytes!, options)!;
    }

    private static CalendarBridgeServer StartServer(ResultProcessingProbe probe, string pipeName)
    {
        var server = new CalendarBridgeServer(pipeName: pipeName);
        server.ResultProcessingHook = probe.Hook;
        server.Start();
        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
        return server;
    }

    /// <summary>파이프 왕복으로 getConfig가 fetch 지시를 받아온다.</summary>
    [Fact]
    public async Task Pipe_GetConfig_RoundTripsOverNamedPipe()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);

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
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// 파이프 서버의 역직렬화를 거친 getConfig 원문에서 protocolVersion 2만 조회 지시(ShouldFetch)를 받는다.
    /// 7.1.0 worker 형식(protocolVersion 1)과 필드가 없는 구버전 형식은 ok + config를 받지만 protocol_mismatch이고,
    /// requestId·lease를 발급하지 않았으므로 뒤이은 protocolVersion 2 요청이 곧바로 조회 지시를 받는다.
    /// </summary>
    [Fact]
    public async Task Pipe_GetConfig_IssuesFetchOnlyForProtocolVersion2()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);
        var version = ChromeExtensionInstaller.ExpectedExtensionVersion;

        try
        {
            var legacyRequests = new[]
            {
                $"{{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"{version}\",\"protocolVersion\":1}}",
                $"{{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"{version}\"}}"
            };

            foreach (var raw in legacyRequests)
            {
                var legacy = await CallRawAsync(pipeName, Encoding.UTF8.GetBytes(raw), cts.Token).WaitAsync(WaitLimit);

                Assert.True(legacy.Ok, legacy.Error);
                Assert.NotNull(legacy.Config);
                Assert.False(legacy.Config!.ShouldFetch);
                Assert.Equal(NoFetchReasons.ProtocolMismatch, legacy.Config!.NoFetchReason);
                Assert.Equal("", legacy.Config!.RequestId);
            }

            var current = await CallRawAsync(
                pipeName,
                Encoding.UTF8.GetBytes($"{{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"{version}\",\"protocolVersion\":2}}"),
                cts.Token).WaitAsync(WaitLimit);

            Assert.True(current.Ok, current.Error);
            Assert.NotNull(current.Config);
            Assert.True(current.Config!.ShouldFetch);
            Assert.False(string.IsNullOrWhiteSpace(current.Config!.RequestId));
            Assert.Equal(NativeBridgeProtocol.ProtocolVersion, current.Config!.ProtocolVersion);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// 7.1.0 형식(최상위 protocolVersion 없음, 쿠키 필드)의 postResult 원문은 활성 requestId를 들고 와도 ok:false로 거부되고
    /// 결과 처리가 시작되지 않는다.
    /// </summary>
    [Fact]
    public async Task Pipe_PostResult_LegacyCookiePayload_IsRejected()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);

        try
        {
            var issued = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            Assert.NotNull(issued.Config);
            Assert.True(issued.Config!.ShouldFetch);

            var legacy = $"{{\"type\":\"postResult\",\"result\":{{\"requestId\":\"{issued.Config!.RequestId}\",\"cookieHeader\":\"SESSION=SUPERSECRET\",\"cookieCount\":1}}}}";
            var response = await CallRawAsync(pipeName, Encoding.UTF8.GetBytes(legacy), cts.Token).WaitAsync(WaitLimit);

            Assert.False(response.Ok);
            Assert.Equal("Unsupported protocolVersion.", response.Error);

            await Task.Delay(TimeSpan.FromMilliseconds(150));
            Assert.Equal(0, probe.Calls);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>결과 처리가 끝나지 않아도 새 연결의 getConfig는 native host의 2.5초 timeout 안에 응답한다.</summary>
    [Fact]
    public async Task Pipe_GetConfigWhileResultProcessing_RespondsWithinTwoSeconds()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);

        try
        {
            var issued = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            Assert.NotNull(issued.Config);
            Assert.True(issued.Config!.ShouldFetch);

            var ack = await CallAsync(pipeName, PostResultRequest(issued.Config!.RequestId), cts.Token).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await probe.Started.Task.WaitAsync(WaitLimit);

            var stopwatch = Stopwatch.StartNew();
            var during = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"결과 처리 중 getConfig 응답이 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
            Assert.True(during.Ok);
            Assert.NotNull(during.Config);
            Assert.False(during.Config!.ShouldFetch);
            Assert.Equal(NoFetchReasons.LeaseActive, during.Config!.NoFetchReason);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>동시에 들어온 연결이 서로를 막지 않는다(인스턴스 4 + accept 분리).</summary>
    [Fact]
    public async Task Pipe_ThreeConcurrentPingClients_AllReceiveOk()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);

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
            probe.Gate.TrySetCanceled();
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
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);

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
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>결과 처리가 진행 중이어도 종료는 1초 안에 끝난다(트레이 종료 지연 제거).</summary>
    [Fact]
    public async Task Pipe_DisposeAsyncWhileResultProcessing_CompletesWithinOneSecond()
    {
        using var cts = new CancellationTokenSource(WaitLimit);
        var probe = new ResultProcessingProbe();
        var pipeName = NewPipeName();
        var server = StartServer(probe, pipeName);
        var disposed = false;

        try
        {
            var issued = await CallAsync(pipeName, GetConfigRequest(), cts.Token).WaitAsync(WaitLimit);
            Assert.NotNull(issued.Config);

            var ack = await CallAsync(pipeName, PostResultRequest(issued.Config!.RequestId), cts.Token).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await probe.Started.Task.WaitAsync(WaitLimit);

            var stopwatch = Stopwatch.StartNew();
            await server.DisposeAsync();
            stopwatch.Stop();
            disposed = true;

            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"DisposeAsync가 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            if (!disposed)
                await server.DisposeAsync();
        }
    }
}
