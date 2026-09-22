using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 상태 문구는 <see cref="SyncStatusService"/> 한 곳에서만 만든다는 규칙을 고정하는 테스트.
/// </summary>
public sealed class SyncStatusServiceTests
{
    /// <summary>설정 저장 실패는 GeneralError 상태와 고정 문구로 게시된다.</summary>
    [Fact]
    public void MarkPersistenceError_PublishesGeneralErrorForSettings()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkPersistenceError("설정");

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.GeneralError, captured!.State);
        Assert.Equal("설정 저장 실패 · 로그 확인", captured.Text);
        Assert.True(captured.IsError);
    }

    /// <summary>캐시 저장 실패도 같은 형식의 문구를 만든다.</summary>
    [Fact]
    public void MarkPersistenceError_PublishesGeneralErrorForCache()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkPersistenceError("캐시");

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.GeneralError, captured!.State);
        Assert.Equal("캐시 저장 실패 · 로그 확인", captured.Text);
        Assert.True(captured.IsError);
    }

    /// <summary>저장 실패 이후에도 정상 동기화 문구가 원래대로 복구된다.</summary>
    [Fact]
    public void MarkSuccess_AfterPersistenceError_PublishesSyncedText()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkPersistenceError("설정");
        svc.MarkSuccess(new DateTimeOffset(2026, 9, 22, 14, 5, 0, TimeSpan.FromHours(9)));

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.Synced, captured!.State);
        Assert.Equal("정상 · 동기화 14:05", captured.Text);
        Assert.False(captured.IsError);
    }

    /// <summary>설정 오류 상태는 "설정 오류: {사유}" 문구로 게시된다(설계 문서 9절 계약).</summary>
    [Fact]
    public void MarkConfigurationInvalid_PublishesConfigurationInvalidWithPrefixedText()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.MarkConfigurationInvalid("주소 오류");

        var published = Assert.Single(events);
        Assert.Equal(OverlaySyncState.ConfigurationInvalid, published.State);
        Assert.Equal("설정 오류: 주소 오류", published.Text);
        Assert.True(published.IsError);
    }

    /// <summary>확장이 30초마다 같은 사유를 보고해도 상태는 한 번만 게시된다.</summary>
    [Fact]
    public void MarkConfigurationInvalid_SameReasonTwice_PublishesOnce()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.MarkConfigurationInvalid("주소 오류");
        svc.MarkConfigurationInvalid("주소 오류");

        Assert.Single(events);
    }

    /// <summary>다른 상태를 거친 뒤에는 같은 사유라도 다시 게시된다.</summary>
    [Fact]
    public void MarkConfigurationInvalid_AfterAnotherState_PublishesAgain()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.MarkConfigurationInvalid("주소 오류");
        svc.MarkWaiting();
        svc.MarkConfigurationInvalid("주소 오류");

        Assert.Equal(2, events.Count(e => e.State == OverlaySyncState.ConfigurationInvalid));
    }

    /// <summary>health timer는 설정 오류 상태를 덮어쓰지 않는다.</summary>
    [Fact]
    public void EvaluateHealth_DoesNotOverrideConfigurationInvalid()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.MarkConfigurationInvalid("주소 오류");
        events.Clear();
        svc.EvaluateHealth();

        Assert.Empty(events);
    }

    /// <summary>heartbeat가 한 번도 없으면 health timer가 연결 대기 문구를 게시한다(기존 동작 가드).</summary>
    [Fact]
    public void EvaluateHealth_PublishesWaitingWhenHeartbeatMissing()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.EvaluateHealth();

        var published = Assert.Single(events);
        Assert.Equal(OverlaySyncState.WaitingForChrome, published.State);
        Assert.Equal("Chrome 확장 연결 대기", published.Text);
    }

    /// <summary>
    /// 문서(§10.4)에 옮겨 적은 상태 문구가 실제 코드와 글자 단위로 일치하는지 고정하는 가드.
    /// 이 테스트에서는 <see cref="SyncStatusService.MarkCacheLoaded"/>(T1.10)를 호출하지 않아
    /// 접미가 붙지 않은 기본 문구를 확인한다.
    /// </summary>
    [Fact]
    public void StatusTexts_MatchDocumentedStrings()
    {
        var svc = new SyncStatusService();
        var events = new List<SyncStatusChangedEventArgs>();
        svc.StatusChanged += (_, e) => events.Add(e);

        svc.MarkStarting();
        svc.MarkWaiting();
        svc.MarkRequested();
        svc.MarkSyncing();
        svc.MarkConfigurationInvalid("X");

        Assert.Equal("시작 중…", events[0].Text);
        Assert.Equal("Chrome 백그라운드 대기", events[1].Text);
        Assert.Equal("동기화 요청 중…", events[2].Text);
        Assert.Equal("동기화 중…", events[3].Text);
        Assert.Equal("설정 오류: X", events[4].Text);

        svc.MarkSuccess(new DateTimeOffset(2026, 9, 22, 13, 5, 0, TimeSpan.FromHours(9)));
        Assert.Equal("정상 · 동기화 13:05", events[5].Text);
    }

    /// <summary>캐시 범위가 오늘을 벗어나면 대기 문구에 접미가 붙는다(T1.6).</summary>
    [Fact]
    public void MarkWaiting_AfterMarkCacheOutOfRange_AppendsOutOfRangeSuffix()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkCacheOutOfRange();
        svc.MarkWaiting();

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.WaitingForChrome, captured!.State);
        Assert.Equal("Chrome 백그라운드 대기 · 캐시(범위 밖)", captured.Text);
        Assert.False(captured.IsError);
    }

    /// <summary>플래그가 없으면 기존 문구가 글자 그대로 유지된다.</summary>
    [Fact]
    public void MarkWaiting_WithoutCacheOutOfRange_KeepsBaseText()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkWaiting();

        Assert.NotNull(captured);
        Assert.Equal("Chrome 백그라운드 대기", captured!.Text);
    }

    /// <summary>브리지 연결 문구에도 같은 접미가 붙는다.</summary>
    [Fact]
    public void MarkHeartbeat_AfterMarkCacheOutOfRange_AppendsSuffixToConnectedText()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkCacheOutOfRange();
        svc.MarkHeartbeat();

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.Connected, captured!.State);
        Assert.Equal("Chrome 브리지 연결됨 · 동기화 대기 · 캐시(범위 밖)", captured.Text);
    }

    /// <summary>정상 동기화가 오면 접미가 사라진다.</summary>
    [Fact]
    public void MarkSuccess_ClearsCacheOutOfRangeSuffix()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkCacheOutOfRange();
        svc.MarkSuccess(new DateTimeOffset(2026, 9, 22, 13, 5, 0, TimeSpan.FromHours(9)));

        Assert.NotNull(captured);
        Assert.Equal("정상 · 동기화 13:05", captured!.Text);

        svc.MarkWaiting();
        Assert.Equal("Chrome 백그라운드 대기", captured!.Text);
    }

    /// <summary>캐시를 읽지 않았으면 대기 문구에 아무 접미도 붙지 않는다(T1.10).</summary>
    [Fact]
    public void MarkWaiting_WithoutCache_KeepsPlainText()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkWaiting();

        Assert.NotNull(last);
        Assert.Equal("Chrome 백그라운드 대기", last!.Text);
        Assert.Equal(OverlaySyncState.WaitingForChrome, last.State);
        Assert.False(last.IsError);
    }

    /// <summary>캐시 로드 후 대기 문구에는 " · 캐시 MM-dd HH:mm" 접미가 붙는다.</summary>
    [Fact]
    public void MarkCacheLoaded_ThenMarkWaiting_AppendsCacheSuffix()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));
        service.MarkWaiting();

        Assert.NotNull(last);
        Assert.Equal("Chrome 백그라운드 대기 · 캐시 09-21 08:30", last!.Text);
    }

    /// <summary>브리지 연결 문구에도 같은 캐시 접미가 붙는다.</summary>
    [Fact]
    public void MarkCacheLoaded_ThenMarkHeartbeat_AppendsCacheSuffixToConnectedText()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));
        service.MarkHeartbeat();

        Assert.NotNull(last);
        Assert.Equal(OverlaySyncState.Connected, last!.State);
        Assert.Equal("Chrome 브리지 연결됨 · 동기화 대기 · 캐시 09-21 08:30", last.Text);
    }

    /// <summary>이미 대기 상태면 캐시 로드만으로 접미가 붙은 문구가 다시 게시된다.</summary>
    [Fact]
    public void MarkCacheLoaded_WhileWaiting_RepublishesWithSuffix()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkWaiting();
        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));

        Assert.NotNull(last);
        Assert.Equal(OverlaySyncState.WaitingForChrome, last!.State);
        Assert.Equal("Chrome 백그라운드 대기 · 캐시 09-21 08:30", last.Text);
    }

    /// <summary>캐시 시각이 없으면(기본값) 값 저장도 재게시도 하지 않는다.</summary>
    [Fact]
    public void MarkCacheLoaded_WithDefaultValue_DoesNotPublish()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        var published = 0;
        service.StatusChanged += (_, e) => { last = e; published++; };

        service.MarkWaiting();
        service.MarkCacheLoaded(default);

        Assert.NotNull(last);
        Assert.Equal("Chrome 백그라운드 대기", last!.Text);
        Assert.Equal(1, published);
    }

    /// <summary>성공 이력이 없어도 health timer 문구에 캐시 접미가 붙는다.</summary>
    [Fact]
    public void EvaluateHealth_WithCacheAndNoSuccess_AppendsCacheSuffix()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));
        service.EvaluateHealth();

        Assert.NotNull(last);
        Assert.Equal("Chrome 확장 연결 대기 · 캐시 09-21 08:30", last!.Text);
    }

    /// <summary>동기화가 한 번 성공하면 이후 문구에서 캐시 접미가 사라지고 "마지막" 접미로 바뀐다.</summary>
    [Fact]
    public void MarkSuccess_ClearsCacheSuffixFromLaterWaiting()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));
        service.MarkSuccess(new DateTimeOffset(2026, 9, 21, 9, 5, 0, TimeSpan.FromHours(9)));
        service.MarkWaiting();

        Assert.NotNull(last);
        Assert.Equal("Chrome 백그라운드 대기", last!.Text);

        service.EvaluateHealth();
        Assert.Equal("Chrome 확장 연결 대기 · 마지막 09:05", last!.Text);
    }

    /// <summary>범위 밖 접미와 캐시 시각 접미가 동시에 걸릴 때의 결합 순서를 고정한다.</summary>
    [Fact]
    public void MarkCacheLoaded_WithCacheOutOfRange_AppendsOutOfRangeThenCacheSuffix()
    {
        var service = new SyncStatusService();
        SyncStatusChangedEventArgs? last = null;
        service.StatusChanged += (_, e) => last = e;

        service.MarkCacheOutOfRange();
        service.MarkCacheLoaded(new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(9)));
        service.MarkWaiting();

        Assert.NotNull(last);
        Assert.Equal("Chrome 백그라운드 대기 · 캐시(범위 밖) · 캐시 09-21 08:30", last!.Text);
    }
}
