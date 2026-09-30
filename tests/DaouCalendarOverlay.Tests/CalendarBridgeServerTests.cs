using System.Diagnostics;
using System.Text.Json;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// <c>postResult</c>가 결과 처리를 기다리지 않고 즉시 응답하는지, 처리 중에는 중복 fetch가 막히는지,
/// 종료가 결과 처리에 묶이지 않는지, protocolVersion 게이트가 조회 지시와 결과 수용을 막는지 검증한다.
/// 파이프는 열지 않고(<c>Start()</c> 미호출) <c>HandleRequestAsync</c>를 직접 호출하며,
/// 결과 처리 시작은 <see cref="CalendarBridgeServer.ResultProcessingHook"/>으로 붙잡는다.
/// </summary>
public sealed class CalendarBridgeServerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>두 번째 postResult가 처리를 유발했다면 이 시간 안에 hook 호출 수가 올라간다.</summary>
    private static readonly TimeSpan DuplicateGrace = TimeSpan.FromMilliseconds(150);

    /// <summary>일어나면 안 되는 SyncCompleted가 뒤늦게 올라오는지 확인하기 위해 주는 유예 시간.</summary>
    private static readonly TimeSpan SilenceGrace = TimeSpan.FromMilliseconds(300);

    private static readonly DateTimeOffset RangeFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset RangeTo = new(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));

    private const string OkBody =
        "{\"code\":200,\"data\":[{\"id\":\"1\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"summary\":\"테스트\",\"timeType\":\"timed\",\"startTime\":\"2026-09-22T10:00:00+09:00\",\"endTime\":\"2026-09-22T11:00:00+09:00\"}]}";

    private static AppSettings Settings() => new()
    {
        BaseUrl = "https://test.daouoffice.com",
        CalendarIds = { "12345" },
        RefreshMinutes = 5
    };

    /// <summary>운영 파이프 이름과 충돌하지 않도록 테스트마다 다른 이름을 쓴다.</summary>
    private static string NewPipeName() => "DaouCalendarOverlay.Tests." + Guid.NewGuid().ToString("N");

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private static async Task<BridgeConfigResponse> GetConfigAsync(CalendarBridgeServer server, int? protocolVersion = NativeBridgeProtocol.ProtocolVersion)
    {
        var response = await server.HandleRequestAsync(new NativeBridgeRequest
        {
            Type = "getConfig",
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion,
            ProtocolVersion = protocolVersion
        }).WaitAsync(WaitLimit);
        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Config);
        return response.Config!;
    }

    private static Task<NativeBridgeResponse> PostResultAsync(CalendarBridgeServer server, string requestId) =>
        PostPayloadAsync(server, OkPayload(requestId));

    private static Task<NativeBridgeResponse> PostPayloadAsync(CalendarBridgeServer server, BridgeResultPayload payload,
        int? protocolVersion = NativeBridgeProtocol.ProtocolVersion) =>
        server.HandleRequestAsync(new NativeBridgeRequest
        {
            Type = "postResult",
            ProtocolVersion = protocolVersion,
            Result = payload
        });

    /// <summary>hook이 붙잡은 결과 처리를 풀어 주는 서버를 만든다. 호출 수와 받은 토큰은 probe로 관찰한다.</summary>
    private static CalendarBridgeServer CreateServer(ResultProcessingProbe probe)
    {
        var server = new CalendarBridgeServer(pipeName: NewPipeName());
        server.ResultProcessingHook = probe.Hook;
        return server;
    }

    /// <summary>파이프 응답은 결과 처리가 끝나기를 기다리지 않는다.</summary>
    [Fact]
    public async Task PostResult_RespondsOkBeforeProcessingCompletes()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await probe.Started.Task.WaitAsync(WaitLimit);
            Assert.False(probe.Gate.Task.IsCompleted);
            Assert.True(probe.Calls >= 1);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>활성 요청과 다른 requestId는 결과 처리를 유발하지 않는다(ok 응답은 유지).</summary>
    [Fact]
    public async Task PostResult_StaleRequestId_ReturnsOkWithoutProcessing()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) => captured = e;

            var ack = await PostResultAsync(server, "deadbeef").WaitAsync(WaitLimit);

            Assert.True(ack.Ok);
            await Task.Delay(DuplicateGrace);
            Assert.Equal(0, probe.Calls);
            Assert.Null(captured);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>확장이 같은 결과를 두 번 보내도 결과 처리는 한 번만 한다.</summary>
    [Fact]
    public async Task PostResult_SameRequestIdTwice_ProcessesOnce()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var first = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(first.Ok);
            await probe.Started.Task.WaitAsync(WaitLimit);

            var second = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(second.Ok);

            await Task.Delay(DuplicateGrace);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>결과 처리가 끝나기 전에는 lease가 유지되므로 새 fetch가 발행되지 않는다.</summary>
    [Fact]
    public async Task GetConfig_WhileResultProcessing_ReturnsShouldFetchFalse()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);

        try
        {
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await probe.Started.Task.WaitAsync(WaitLimit);

            var during = await GetConfigAsync(server);

            Assert.False(during.ShouldFetch);
            Assert.Equal(NoFetchReasons.LeaseActive, during.NoFetchReason);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>결과 처리가 끝나면 lease가 풀리고 다음 요청에 새 requestId가 나간다.</summary>
    [Fact]
    public async Task GetConfig_AfterProcessingCompletes_IssuesNewRequestId()
    {
        var probe = new ResultProcessingProbe();
        var completed = NewSignal();
        var server = CreateServer(probe);

        try
        {
            server.SyncCompleted += (_, _) => completed.TrySetResult();
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await probe.Started.Task.WaitAsync(WaitLimit);

            probe.Gate.SetResult();
            await completed.Task.WaitAsync(WaitLimit);

            server.ForceRefresh();
            var next = await GetConfigAsync(server);

            Assert.True(next.ShouldFetch);
            Assert.NotEqual(config.RequestId, next.RequestId);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>성공 envelope은 백그라운드 결과 처리가 끝난 뒤 SyncCompleted로 올라온다.</summary>
    [Fact]
    public async Task ProcessResult_SuccessEnvelope_RaisesSyncCompletedWithEvents()
    {
        var probe = new ResultProcessingProbe();
        var completed = NewSignal();
        var server = CreateServer(probe);

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
            await probe.Started.Task.WaitAsync(WaitLimit);

            probe.Gate.SetResult();
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
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>종료는 진행 중인 결과 처리를 취소하고 1초 안에 돌아온다(트레이 종료 지연 제거).</summary>
    [Fact]
    public async Task DisposeAsync_WhileResultProcessing_CancelsTokenAndReturnsWithinOneSecond()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);
        var disposed = false;

        try
        {
            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) => captured = e;

            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);
            await probe.Started.Task.WaitAsync(WaitLimit);
            Assert.False(probe.LastToken.IsCancellationRequested);

            var stopwatch = Stopwatch.StartNew();
            await server.DisposeAsync();
            stopwatch.Stop();
            disposed = true;

            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"DisposeAsync가 {stopwatch.ElapsedMilliseconds}ms 걸렸습니다.");
            Assert.True(probe.LastToken.IsCancellationRequested);
            Assert.True(probe.Gate.Task.IsCanceled);

            // 종료 취소는 실패로 알리지 않는다.
            await Task.Delay(DuplicateGrace);
            Assert.Null(captured);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            if (!disposed)
                await server.DisposeAsync();
        }
    }

    /// <summary>
    /// protocolVersion이 없거나(구버전) 1이면(7.1.0 worker) 조회를 지시하지 않는다. requestId·lease를 발급하지 않고
    /// ConfigurationInvalid·FetchIssued도 올리지 않는다. 이어서 오는 v2 getConfig는 곧바로 조회 지시를 받는다.
    /// </summary>
    [Fact]
    public async Task GetConfig_WithoutOrOldProtocolVersion_ReturnsProtocolMismatchWithoutIssuingRequest()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        var configurationInvalid = 0;
        var fetchIssued = 0;
        server.ConfigurationInvalid += (_, _) => Interlocked.Increment(ref configurationInvalid);
        server.FetchIssued += (_, _) => Interlocked.Increment(ref fetchIssued);
        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);

        foreach (var legacy in new int?[] { null, 1, 3 })
        {
            var config = await GetConfigAsync(server, legacy);

            Assert.True(config.Ok);
            Assert.False(config.ShouldFetch);
            Assert.Equal(NoFetchReasons.ProtocolMismatch, config.NoFetchReason);
            Assert.Equal("", config.RequestId);
            Assert.Equal("", config.BaseUrl);
            Assert.Empty(config.CalendarIds);
        }

        Assert.Equal(0, Volatile.Read(ref configurationInvalid));
        Assert.Equal(0, Volatile.Read(ref fetchIssued));

        var current = await GetConfigAsync(server);

        Assert.True(current.ShouldFetch);
        Assert.False(string.IsNullOrEmpty(current.RequestId));
        Assert.Equal(1, Volatile.Read(ref fetchIssued));
    }

    /// <summary>
    /// protocolVersion이 없거나 1인 postResult는 거부한다. 결과 처리·SyncCompleted가 없고,
    /// 발급된 lease는 그대로라서 다음 v2 getConfig는 lease_active다.
    /// </summary>
    [Fact]
    public async Task PostResult_WithoutProtocolVersion_IsRejectedAndKeepsLease()
    {
        var probe = new ResultProcessingProbe();
        var server = CreateServer(probe);

        try
        {
            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) => captured = e;
            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            foreach (var legacy in new int?[] { null, 1 })
            {
                var rejected = await PostPayloadAsync(server, OkPayload(config.RequestId), legacy).WaitAsync(WaitLimit);

                Assert.False(rejected.Ok);
                Assert.Equal("Unsupported protocolVersion.", rejected.Error);
            }

            await Task.Delay(SilenceGrace);
            Assert.Equal(0, probe.Calls);
            Assert.Null(captured);

            var next = await GetConfigAsync(server);
            Assert.False(next.ShouldFetch);
            Assert.Equal(NoFetchReasons.LeaseActive, next.NoFetchReason);

            // 같은 requestId의 v2 결과는 여전히 받아들여진다(거부가 상태를 바꾸지 않았다).
            var accepted = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(accepted.Ok);
            await probe.Started.Task.WaitAsync(WaitLimit);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            probe.Gate.TrySetCanceled();
            await server.DisposeAsync();
        }
    }

    /// <summary>로그아웃 응답(401 + ROUTE-0004)은 재로그인 필요로 올라오고 1분 뒤 재시도가 잡힌다.</summary>
    [Fact]
    public async Task PostResult_Unauthorized_RaisesAuthRequiredWithOneMinuteRetry()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        var completed = NewSignal();
        BridgeSyncEventArgs? captured = null;
        server.SyncCompleted += (_, e) =>
        {
            captured = e;
            completed.TrySetResult();
        };

        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
        var config = await GetConfigAsync(server);
        Assert.True(config.ShouldFetch);

        const string body = "{\"code\":\"ROUTE-0004\",\"message\":\"x\"}";
        var before = DateTimeOffset.Now;
        var ack = await PostPayloadAsync(server, new BridgeResultPayload
        {
            RequestId = config.RequestId,
            Outcome = BridgeFetchOutcomes.Response,
            Status = 401,
            ResponseType = "basic",
            ContentType = "application/json",
            Body = body,
            BodyLength = body.Length,
            ElapsedMs = 120
        }).WaitAsync(WaitLimit);
        Assert.True(ack.Ok);

        await completed.Task.WaitAsync(WaitLimit);
        var after = DateTimeOffset.Now;

        Assert.NotNull(captured);
        Assert.False(captured!.Success);
        Assert.True(captured!.AuthenticationRequired);
        Assert.Equal(BridgeFailureKind.Authentication, captured!.FailureKind);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, captured!.Error);
        Assert.NotNull(captured!.RetryAt);
        Assert.InRange(captured!.RetryAt!.Value, before.AddSeconds(55), after.AddSeconds(65));
    }

    /// <summary>worker의 fetch 시간 초과는 네트워크 실패로 올라온다(재로그인 배너 아님).</summary>
    [Fact]
    public async Task PostResult_Timeout_RaisesNetworkFailure()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        var completed = NewSignal();
        BridgeSyncEventArgs? captured = null;
        server.SyncCompleted += (_, e) =>
        {
            captured = e;
            completed.TrySetResult();
        };

        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
        var config = await GetConfigAsync(server);

        var ack = await PostPayloadAsync(server, new BridgeResultPayload
        {
            RequestId = config.RequestId,
            Outcome = BridgeFetchOutcomes.Timeout,
            ErrorName = "AbortError",
            ElapsedMs = 25003
        }).WaitAsync(WaitLimit);
        Assert.True(ack.Ok);

        await completed.Task.WaitAsync(WaitLimit);

        Assert.NotNull(captured);
        Assert.False(captured!.Success);
        Assert.False(captured!.AuthenticationRequired);
        Assert.Equal(BridgeFailureKind.Network, captured!.FailureKind);
        Assert.Equal(BridgeResultClassifier.TimeoutMessage, captured!.Error);
        Assert.NotNull(captured!.RetryAt);
    }

    /// <summary>
    /// getConfig 뒤 표시 범위가 바뀌면(스냅샷 등가성 전제) 옛 requestId의 결과는 처리하지 않고,
    /// 다음 getConfig가 새 범위로 새 requestId를 발행한다.
    /// </summary>
    [Fact]
    public async Task PostResult_AfterRangeChangedSinceGetConfig_IsIgnored()
    {
        var probe = new ResultProcessingProbe();
        probe.Gate.SetResult();
        var server = CreateServer(probe);

        try
        {
            BridgeSyncEventArgs? captured = null;
            server.SyncCompleted += (_, e) => captured = e;

            server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
            var config = await GetConfigAsync(server);
            Assert.True(config.ShouldFetch);

            var octoberFrom = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(9));
            var octoberTo = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(9));
            server.UpdateRequest(Settings(), octoberFrom, octoberTo, force: false);

            var ack = await PostResultAsync(server, config.RequestId).WaitAsync(WaitLimit);
            Assert.True(ack.Ok);

            await Task.Delay(SilenceGrace);
            Assert.Equal(0, probe.Calls);
            Assert.Null(captured);

            var next = await GetConfigAsync(server);
            Assert.True(next.ShouldFetch);
            Assert.NotEqual(config.RequestId, next.RequestId);
            Assert.StartsWith("2026-09-27T00:00:00.000", next.TimeMin, StringComparison.Ordinal);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>발행 응답과 NoFetch 응답(protocol_mismatch 포함) 모두 protocolVersion 2를 싣고, Web JSON에도 나온다.</summary>
    [Fact]
    public async Task ConfigResponse_CarriesProtocolVersion()
    {
        await using var server = new CalendarBridgeServer(pipeName: NewPipeName());
        server.UpdateRequest(Settings(), RangeFrom, RangeTo, force: true);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var issued = await GetConfigAsync(server);
        var leaseActive = await GetConfigAsync(server);
        var mismatch = await GetConfigAsync(server, protocolVersion: null);

        Assert.True(issued.ShouldFetch);
        Assert.Equal(NoFetchReasons.LeaseActive, leaseActive.NoFetchReason);
        Assert.Equal(NoFetchReasons.ProtocolMismatch, mismatch.NoFetchReason);

        foreach (var config in new[] { issued, leaseActive, mismatch, BridgeConfigResponse.NoFetch(NoFetchReasons.NotConfigured) })
        {
            Assert.Equal(2, config.ProtocolVersion);
            Assert.Equal(NativeBridgeProtocol.ProtocolVersion, config.ProtocolVersion);
            Assert.Contains("\"protocolVersion\":2", JsonSerializer.Serialize(config, web), StringComparison.Ordinal);
        }

        var envelope = JsonSerializer.Serialize(NativeBridgeResponse.Success(mismatch), web);
        Assert.Contains("\"protocolVersion\":2", envelope, StringComparison.Ordinal);
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
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion,
            ProtocolVersion = NativeBridgeProtocol.ProtocolVersion
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
            ExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion,
            ProtocolVersion = NativeBridgeProtocol.ProtocolVersion
        };
        await server.HandleRequestAsync(request).WaitAsync(WaitLimit);
        await server.HandleRequestAsync(request).WaitAsync(WaitLimit);

        Assert.Equal(2, confirmed);
    }
}

/// <summary>
/// 브리지 서버 테스트들이 결과 처리 시작을 붙잡는 데 쓰는 <see cref="CalendarBridgeServer.ResultProcessingHook"/> 구현.
/// <see cref="Gate"/>가 풀리거나(SetResult) 취소될 때까지 처리를 멈춘다. 서버가 넘긴 토큰이 취소되면 Gate도 취소된다.
/// </summary>
internal sealed class ResultProcessingProbe
{
    private int _calls;
    private CancellationToken _lastToken;

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Calls => Volatile.Read(ref _calls);

    public CancellationToken LastToken => _lastToken;

    public Func<CancellationToken, Task> Hook => async ct =>
    {
        _lastToken = ct;
        ct.Register(() => Gate.TrySetCanceled());
        Interlocked.Increment(ref _calls);
        Started.TrySetResult();
        await Gate.Task;
    };
}
