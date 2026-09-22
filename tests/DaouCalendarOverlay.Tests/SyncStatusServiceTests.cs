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
}
