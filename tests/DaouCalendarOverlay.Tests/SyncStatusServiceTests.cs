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
}
