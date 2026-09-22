using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// HTTP 왕복 중 표시 범위가 바뀌면 이전 범위의 결과가 UI/캐시로 올라가지 않아야 한다.
/// 파이프를 열지 않고(<c>Start()</c> 미호출) 요청을 직접 주입하며, HTTP는 스텁 핸들러로 대체한다.
/// </summary>
public sealed class CalendarBridgeServerStaleResultTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private const string OkBody =
        "{\"code\":200,\"message\":\"OK\",\"data\":[{\"id\":\"38262\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"timeType\":\"allday\",\"startTime\":\"2026-09-10T00:00:00.000+09:00\",\"endTime\":\"2026-09-10T23:59:59.999+09:00\",\"summary\":\"테스트\"}]}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }

    private static AppSettings CreateSettings() => new()
    {
        BaseUrl = "https://company.daouoffice.com",
        CalendarIds = { "12345" },
        RefreshMinutes = 10
    };

    private static HttpResponseMessage CreateOkResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(OkBody, Encoding.UTF8, "application/json")
    };

    private static async Task<BridgeConfigResponse> RequestConfigAsync(CalendarBridgeServer server)
    {
        var response = await server.HandleRequestAsync(new NativeBridgeRequest { Type = "getConfig" });
        Assert.NotNull(response.Config);
        return response.Config!;
    }

    private static Task<NativeBridgeResponse> PostResultAsync(CalendarBridgeServer server, string requestId) =>
        server.HandleRequestAsync(new NativeBridgeRequest
        {
            Type = "postResult",
            Result = new BridgeResultPayload
            {
                RequestId = requestId,
                CookieHeader = "SESSION=dummy",
                CookieCount = 1
            }
        });

    /// <summary>HTTP 진행 중 월을 이동하면 이전 범위 결과는 SyncCompleted로 올라가지 않는다.</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeChangedDuringHttp_DoesNotRaiseSyncCompleted()
    {
        await using var server = new CalendarBridgeServer();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.HttpMessageHandlerFactory = () => new StubHandler(_ =>
        {
            sendStarted.TrySetResult();
            return release.Task;
        });

        var settings = CreateSettings();
        var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        server.UpdateRequest(settings, from1, to1, force: true);

        var config = await RequestConfigAsync(server);
        Assert.True(config.ShouldFetch);

        BridgeSyncEventArgs? captured = null;
        server.SyncCompleted += (_, e) => captured = e;

        var post = PostResultAsync(server, config.RequestId);
        await sendStarted.Task.WaitAsync(WaitLimit);

        var (from2, to2) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));
        server.UpdateRequest(settings, from2, to2, force: false);

        release.SetResult(CreateOkResponse());
        var postResponse = await post.WaitAsync(WaitLimit);

        Assert.True(postResponse.Ok);
        Assert.Null(captured);
    }

    /// <summary>범위가 그대로면 결과가 요청한 범위와 함께 전달된다.</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeUnchanged_RaisesSyncCompletedWithRequestedRange()
    {
        await using var server = new CalendarBridgeServer();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.HttpMessageHandlerFactory = () => new StubHandler(_ =>
        {
            sendStarted.TrySetResult();
            return release.Task;
        });

        var settings = CreateSettings();
        var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        server.UpdateRequest(settings, from1, to1, force: true);

        var config = await RequestConfigAsync(server);
        Assert.True(config.ShouldFetch);

        BridgeSyncEventArgs? captured = null;
        server.SyncCompleted += (_, e) => captured = e;

        var post = PostResultAsync(server, config.RequestId);
        await sendStarted.Task.WaitAsync(WaitLimit);

        release.SetResult(CreateOkResponse());
        var postResponse = await post.WaitAsync(WaitLimit);

        Assert.True(postResponse.Ok);
        Assert.NotNull(captured);
        Assert.True(captured!.Success);
        Assert.Single(captured!.Events);
        Assert.Equal(from1, captured!.RangeFrom);
        Assert.Equal(to1, captured!.RangeTo);
    }

    /// <summary>폐기 직후의 getConfig는 새 범위로 새 requestId를 발행한다(즉시 재발행).</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeChangedDuringHttp_NextGetConfigIssuesNewRange()
    {
        await using var server = new CalendarBridgeServer();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.HttpMessageHandlerFactory = () => new StubHandler(_ =>
        {
            sendStarted.TrySetResult();
            return release.Task;
        });

        var settings = CreateSettings();
        var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        server.UpdateRequest(settings, from1, to1, force: true);

        var config = await RequestConfigAsync(server);
        Assert.True(config.ShouldFetch);

        var post = PostResultAsync(server, config.RequestId);
        await sendStarted.Task.WaitAsync(WaitLimit);

        var (from2, to2) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));
        server.UpdateRequest(settings, from2, to2, force: false);

        release.SetResult(CreateOkResponse());
        await post.WaitAsync(WaitLimit);

        var config2 = await RequestConfigAsync(server);

        Assert.True(config2.ShouldFetch);
        Assert.StartsWith(from2.ToString("yyyy-MM-dd"), config2.TimeMin);
        Assert.NotEqual(config.RequestId, config2.RequestId);
    }
}
