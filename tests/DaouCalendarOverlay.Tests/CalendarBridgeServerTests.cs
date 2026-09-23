using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// <c>postResult</c>가 DaouOffice 조회를 기다리지 않고 즉시 응답하는지, 조회 중에는 중복 fetch가 막히는지,
/// 종료가 조회에 묶이지 않는지 검증한다.
/// 파이프는 열지 않고(<c>Start()</c> 미호출) <c>HandleRequestAsync</c>를 직접 호출하며 HTTP는 스텁으로 대체한다.
/// </summary>
public sealed class CalendarBridgeServerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>두 번째 postResult가 HTTP를 유발했다면 이 시간 안에 CallCount가 올라간다.</summary>
    private static readonly TimeSpan DuplicateGrace = TimeSpan.FromMilliseconds(150);

    private static readonly DateTimeOffset RangeFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset RangeTo = new(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));

    private const string OkBody =
        "{\"code\":200,\"data\":[{\"id\":\"1\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"summary\":\"테스트\",\"timeType\":\"timed\",\"startTime\":\"2026-09-22T10:00:00+09:00\",\"endTime\":\"2026-09-22T11:00:00+09:00\"}]}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public int CallCount;
        public CancellationToken LastToken;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            LastToken = cancellationToken;
            return _responder(request, cancellationToken);
        }
    }

    private static AppSettings Settings() => new()
    {
        BaseUrl = "https://test.daouoffice.com",
        CalendarIds = { "12345" },
        RefreshMinutes = 5
    };

    /// <summary>운영 파이프 이름과 충돌하지 않도록 테스트마다 다른 이름을 쓴다.</summary>
    private static string NewPipeName() => "DaouCalendarOverlay.Tests." + Guid.NewGuid().ToString("N");

    private static HttpResponseMessage OkResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(OkBody, Encoding.UTF8, "application/json")
    };

    /// <summary>gate가 풀릴 때까지 HTTP 응답을 붙잡아 두는 스텁. 취소되면 gate도 함께 풀린다.</summary>
    private static StubHandler BlockingHandler(TaskCompletionSource<HttpResponseMessage> gate, TaskCompletionSource started) =>
        new((_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled());
            started.TrySetResult();
            return gate.Task;
        });

    private static TaskCompletionSource<HttpResponseMessage> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<BridgeConfigResponse> GetConfigAsync(CalendarBridgeServer server)
    {
        var response = await server.HandleRequestAsync(new NativeBridgeRequest
        {
            Type = "getConfig",
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion
        }).WaitAsync(WaitLimit);
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
                CookieHeader = "SESSION=abc",
                CookieCount = 1
            }
        });

    private static CalendarBridgeServer CreateServer(StubHandler handler) =>
        new(() => handler, pipeName: NewPipeName());

    /// <summary>파이프 응답은 HTTP 왕복을 기다리지 않는다.</summary>
    [Fact]
    public async Task PostResult_RespondsOkBeforeHttpCompletes()
    {
        var gate = NewGate();
        var started = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await started.Task.WaitAsync(WaitLimit);
            Assert.False(gate.Task.IsCompleted);
            Assert.True(handler.CallCount >= 1);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>활성 요청과 다른 requestId는 HTTP를 유발하지 않는다(ok 응답은 유지).</summary>
    [Fact]
    public async Task PostResult_StaleRequestId_ReturnsOkWithoutHttpCall()
    {
        var gate = NewGate();
        var started = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) => captured = e;

            var ack = await PostResultAsync(server, "deadbeef").WaitAsync(WaitLimit);

            Assert.True(ack.Ok);
            Assert.Equal(0, handler.CallCount);
            Assert.Null(captured);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>확장이 같은 결과를 두 번 보내도 DaouOffice 조회는 한 번만 나간다.</summary>
    [Fact]
    public async Task PostResult_SameRequestIdTwice_SendsSingleHttpRequest()
    {
        var gate = NewGate();
        var started = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var first = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(first.Ok);
            await started.Task.WaitAsync(WaitLimit);

            var second = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(second.Ok);

            await Task.Delay(DuplicateGrace);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>조회가 끝나기 전에는 lease가 유지되므로 새 fetch가 발행되지 않는다.</summary>
    [Fact]
    public async Task GetConfig_WhileResultFetchInFlight_ReturnsShouldFetchFalse()
    {
        var gate = NewGate();
        var started = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await started.Task.WaitAsync(WaitLimit);

            var during = await GetConfigAsync(server);

            Assert.False(during.ShouldFetch);
            Assert.Equal(NoFetchReasons.LeaseActive, during.NoFetchReason);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>조회가 끝나면 lease가 풀리고 다음 요청에 새 requestId가 나간다.</summary>
    [Fact]
    public async Task GetConfig_AfterFetchCompletes_IssuesNewRequestId()
    {
        var gate = NewGate();
        var started = NewSignal();
        var completed = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            server.SyncCompleted += (_, _) => completed.TrySetResult();
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await started.Task.WaitAsync(WaitLimit);

            gate.SetResult(OkResponse());
            await completed.Task.WaitAsync(WaitLimit);

            server.ForceRefresh();
            var next = await GetConfigAsync(server);

            Assert.True(next.ShouldFetch);
            Assert.NotEqual(config.RequestId, next.RequestId);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>성공 envelope은 백그라운드 조회가 끝난 뒤 SyncCompleted로 올라온다.</summary>
    [Fact]
    public async Task ProcessResult_SuccessEnvelope_RaisesSyncCompletedWithEvents()
    {
        var gate = NewGate();
        var started = NewSignal();
        var completed = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);

        try
        {
            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) =>
            {
                captured = e;
                completed.TrySetResult();
            };

            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await started.Task.WaitAsync(WaitLimit);

            gate.SetResult(OkResponse());
            await completed.Task.WaitAsync(WaitLimit);

            Assert.NotNull(captured);
            Assert.True(captured!.Success);
            Assert.Single(captured!.Events);
            Assert.Equal("1", captured!.Events[0].Id);
            Assert.Equal(RangeFrom, captured!.RangeFrom);
            Assert.Equal(RangeTo, captured!.RangeTo);
        }
        finally
        {
            gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>종료는 진행 중인 조회를 취소하고 1초 안에 돌아온다(트레이 종료 지연 제거).</summary>
    [Fact]
    public async Task DisposeAsync_WhileFetchInFlight_CancelsHttpTokenAndReturnsWithinOneSecond()
    {
        var gate = NewGate();
        var started = NewSignal();
        var handler = BlockingHandler(gate, started);
        var server = CreateServer(handler);
        var disposed = false;

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await started.Task.WaitAsync(WaitLimit);

            var stopwatch = Stopwatch.StartNew();
            await server.DisposeAsync();
            stopwatch.Stop();
            disposed = true;

            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"DisposeAsync가 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
            Assert.True(handler.LastToken.IsCancellationRequested);
        }
        finally
        {
            gate.TrySetCanceled();
            if (!disposed)
                await server.DisposeAsync();
        }
    }

    /// <summary>구버전 확장이 보낸 getConfig는 Extension 실패 이벤트를 올리고 버전 확인 이벤트는 올리지 않는다.</summary>
    [Fact]
    public async Task GetConfig_WithOlderExtensionVersion_RaisesExtensionFailure()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        BridgeSyncEventArgs? captured = null;
        var confirmed = 0;
        server.SyncCompleted += (_, e) => captured = e;
        server.ExtensionVersionConfirmed += (_, _) => confirmed++;

        await server.HandleRequestAsync(new NativeBridgeRequest { Type = "getConfig", ExtensionVersion = "7.0.0" }).WaitAsync(WaitLimit);

        Assert.NotNull(captured);
        Assert.Equal(BridgeFailureKind.Extension, captured!.FailureKind);
        Assert.Equal("7.0.0", captured!.ExtensionVersion);
        Assert.Equal(ChromeExtensionInstaller.ExpectedExtensionVersion, captured!.ExpectedExtensionVersion);
        Assert.False(captured!.Success);
        Assert.Equal(0, confirmed);
    }

    /// <summary>extensionVersion을 보내지 않는 구버전 확장도 불일치로 보고 ExtensionVersion은 null이다.</summary>
    [Fact]
    public async Task GetConfig_WithoutExtensionVersion_RaisesExtensionFailure()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        BridgeSyncEventArgs? captured = null;
        server.SyncCompleted += (_, e) => captured = e;

        await server.HandleRequestAsync(new NativeBridgeRequest { Type = "getConfig" }).WaitAsync(WaitLimit);

        Assert.NotNull(captured);
        Assert.Equal(BridgeFailureKind.Extension, captured!.FailureKind);
        Assert.Null(captured!.ExtensionVersion);
    }

    /// <summary>기대 버전과 같은 확장은 몇 번 요청해도 Extension 실패 이벤트가 없고 설정 응답은 그대로 온다.</summary>
    [Fact]
    public async Task GetConfig_WithMatchingExtensionVersion_DoesNotRaiseExtensionFailure()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        var extensionFailures = 0;
        server.SyncCompleted += (_, e) =>
        {
            if (e.FailureKind == BridgeFailureKind.Extension)
                extensionFailures++;
        };

        var request = new NativeBridgeRequest
        {
            Type = "getConfig",
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion
        };
        var first = await server.HandleRequestAsync(request).WaitAsync(WaitLimit);
        var second = await server.HandleRequestAsync(request).WaitAsync(WaitLimit);

        Assert.Equal(0, extensionFailures);
        Assert.NotNull(first.Config);
        Assert.NotNull(second.Config);
    }

    /// <summary>기대 버전과 같은 getConfig마다 ExtensionVersionConfirmed가 한 번씩 발생한다.</summary>
    [Fact]
    public async Task GetConfig_WithMatchingExtensionVersion_RaisesExtensionVersionConfirmed()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        var confirmed = 0;
        server.ExtensionVersionConfirmed += (_, _) => confirmed++;

        var request = new NativeBridgeRequest
        {
            Type = "getConfig",
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion
        };
        await server.HandleRequestAsync(request).WaitAsync(WaitLimit);
        await server.HandleRequestAsync(request).WaitAsync(WaitLimit);

        Assert.Equal(2, confirmed);
    }
}
