using System.Net;
using System.Net.Http;
using System.Text;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// HTTP 왕복 중 표시 범위가 바뀌면 이전 범위의 결과가 UI/캐시로 올라가지 않아야 한다.
/// 파이프를 열지 않고(<c>Start()</c> 미호출) 요청을 직접 주입하며, HTTP는 스텁 핸들러로 대체한다.
/// <c>postResult</c>는 즉시 ok로 응답하고 조회는 백그라운드에서 끝나므로, 결과 반영 여부는
/// 재발행된 fetch(<see cref="WaitForNewFetchConfigAsync"/>)와 SyncCompleted 신호로 관찰한다.
/// </summary>
public sealed class CalendarBridgeServerStaleResultTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>폐기되어야 할 결과가 뒤늦게 올라오는지 확인하기 위해 주는 유예 시간.</summary>
    private static readonly TimeSpan DiscardGrace = TimeSpan.FromMilliseconds(500);

    private const string OkBody =
        "{\"code\":200,\"message\":\"OK\",\"data\":[{\"id\":\"38262\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"timeType\":\"allday\",\"startTime\":\"2026-09-10T00:00:00.000+09:00\",\"endTime\":\"2026-09-10T23:59:59.999+09:00\",\"summary\":\"테스트\"}]}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
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

    /// <summary>운영 파이프 이름과 충돌하지 않도록 테스트마다 다른 이름을 쓴다.</summary>
    private static string NewPipeName() => "DaouCalendarOverlay.Tests." + Guid.NewGuid().ToString("N");

    private static StubHandler CreateBlockingHandler(TaskCompletionSource sendStarted, TaskCompletionSource<HttpResponseMessage> release) =>
        new((_, ct) =>
        {
            ct.Register(() => release.TrySetCanceled());
            sendStarted.TrySetResult();
            return release.Task;
        });

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

    /// <summary>이전 requestId와 다른 새 fetch가 발행될 때까지 getConfig를 폴링한다.</summary>
    private static async Task<BridgeConfigResponse> WaitForNewFetchConfigAsync(CalendarBridgeServer server, string previousRequestId)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (DateTime.UtcNow < deadline)
        {
            var config = (await server.HandleRequestAsync(new NativeBridgeRequest { Type = "getConfig" })).Config;
            if (config is not null && config.ShouldFetch && !string.Equals(config.RequestId, previousRequestId, StringComparison.Ordinal))
                return config;

            await Task.Delay(25);
        }

        Assert.Fail("새 fetch가 제한 시간 안에 발행되지 않았습니다.");
        throw new InvalidOperationException();
    }

    /// <summary>유예 시간을 주고도 SyncCompleted가 올라오지 않았음을 확인한다.</summary>
    private static async Task AssertNoSyncCompletedAsync(Task syncCompleted)
    {
        await Task.WhenAny(syncCompleted, Task.Delay(DiscardGrace));
        Assert.False(syncCompleted.IsCompleted, "표시 범위가 바뀐 결과가 SyncCompleted로 올라왔습니다.");
    }

    /// <summary>HTTP 진행 중 월을 이동하면 이전 범위 결과는 SyncCompleted로 올라가지 않는다.</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeChangedDuringHttp_DoesNotRaiseSyncCompleted()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(sendStarted, release);
        await using var server = new CalendarBridgeServer(() => handler, pipeName: NewPipeName());

        try
        {
            var settings = CreateSettings();
            var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
            server.UpdateRequest(settings, from1, to1, force: true);

            var config = await RequestConfigAsync(server);
            Assert.True(config.ShouldFetch);

            BridgeSyncEventArgs? captured = null;
            var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.SyncCompleted += (_, e) =>
            {
                captured = e;
                raised.TrySetResult();
            };

            var postResponse = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(postResponse.Ok);
            await sendStarted.Task.WaitAsync(WaitLimit);

            var (from2, to2) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));
            server.UpdateRequest(settings, from2, to2, force: false);

            release.SetResult(CreateOkResponse());
            await WaitForNewFetchConfigAsync(server, config.RequestId);

            await AssertNoSyncCompletedAsync(raised.Task);
            Assert.Null(captured);
        }
        finally
        {
            release.TrySetCanceled();
        }
    }

    /// <summary>범위가 그대로면 결과가 요청한 범위와 함께 전달된다.</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeUnchanged_RaisesSyncCompletedWithRequestedRange()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(sendStarted, release);
        await using var server = new CalendarBridgeServer(() => handler, pipeName: NewPipeName());

        try
        {
            var settings = CreateSettings();
            var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
            server.UpdateRequest(settings, from1, to1, force: true);

            var config = await RequestConfigAsync(server);
            Assert.True(config.ShouldFetch);

            BridgeSyncEventArgs? captured = null;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.SyncCompleted += (_, e) =>
            {
                captured = e;
                completed.TrySetResult();
            };

            var postResponse = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(postResponse.Ok);
            await sendStarted.Task.WaitAsync(WaitLimit);

            release.SetResult(CreateOkResponse());
            await completed.Task.WaitAsync(WaitLimit);

            Assert.NotNull(captured);
            Assert.True(captured!.Success);
            Assert.Single(captured!.Events);
            Assert.Equal(from1, captured!.RangeFrom);
            Assert.Equal(to1, captured!.RangeTo);
        }
        finally
        {
            release.TrySetCanceled();
        }
    }

    /// <summary>
    /// HTTP 진행 중 표시 범위는 그대로인데 설정창 저장으로 캘린더 ID만 바뀌면, 이전 캘린더 집합으로
    /// 받은 결과는 SyncCompleted로 올라가지 않아야 한다(범위 비교만으로는 이 경우를 잡지 못했다).
    /// </summary>
    [Fact]
    public async Task ProcessResult_WhenCalendarIdsChangedDuringHttp_DoesNotRaiseSyncCompleted()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(sendStarted, release);
        await using var server = new CalendarBridgeServer(() => handler, pipeName: NewPipeName());

        try
        {
            var settings = CreateSettings();
            var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
            server.UpdateRequest(settings, from1, to1, force: true);

            var config = await RequestConfigAsync(server);
            Assert.True(config.ShouldFetch);

            BridgeSyncEventArgs? captured = null;
            var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.SyncCompleted += (_, e) =>
            {
                captured = e;
                raised.TrySetResult();
            };

            var postResponse = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(postResponse.Ok);
            await sendStarted.Task.WaitAsync(WaitLimit);

            // 범위는 그대로 두고 캘린더 ID만 바꾼다(설정창 저장 시나리오).
            var changedSettings = CreateSettings();
            changedSettings.CalendarIds.Clear();
            changedSettings.CalendarIds.Add("99999");
            server.UpdateRequest(changedSettings, from1, to1, force: false);

            release.SetResult(CreateOkResponse());
            await WaitForNewFetchConfigAsync(server, config.RequestId);

            await AssertNoSyncCompletedAsync(raised.Task);
            Assert.Null(captured);
        }
        finally
        {
            release.TrySetCanceled();
        }
    }

    /// <summary>폐기 직후의 getConfig는 새 범위로 새 requestId를 발행한다(즉시 재발행).</summary>
    [Fact]
    public async Task ProcessResult_WhenRangeChangedDuringHttp_NextGetConfigIssuesNewRange()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(sendStarted, release);
        await using var server = new CalendarBridgeServer(() => handler, pipeName: NewPipeName());

        try
        {
            var settings = CreateSettings();
            var (from1, to1) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
            server.UpdateRequest(settings, from1, to1, force: true);

            var config = await RequestConfigAsync(server);
            Assert.True(config.ShouldFetch);

            var postResponse = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(postResponse.Ok);
            await sendStarted.Task.WaitAsync(WaitLimit);

            var (from2, to2) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));
            server.UpdateRequest(settings, from2, to2, force: false);

            release.SetResult(CreateOkResponse());
            var config2 = await WaitForNewFetchConfigAsync(server, config.RequestId);

            Assert.True(config2.ShouldFetch);
            Assert.StartsWith(from2.ToString("yyyy-MM-dd"), config2.TimeMin);
            Assert.NotEqual(config.RequestId, config2.RequestId);
        }
        finally
        {
            release.TrySetCanceled();
        }
    }
}
