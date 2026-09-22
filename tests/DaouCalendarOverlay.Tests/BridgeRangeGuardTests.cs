using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// HTTP 왕복 도중 표시 범위가 바뀌었는지 판정하는 순수 로직 테스트.
/// 비교는 <see cref="DateTimeOffset"/>의 순간(UtcDateTime) 비교이며,
/// 이는 <c>CalendarBridgeServer.UpdateRequest</c>의 기존 범위 비교와 같은 의미다.
/// </summary>
public sealed class BridgeRangeGuardTests
{
    /// <summary>같은 달의 그리드 범위 두 벌은 stale이 아니다.</summary>
    [Fact]
    public void IsStale_WhenRangeIdentical_ReturnsFalse()
    {
        var (snapshotFrom, snapshotTo) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        var (currentFrom, currentTo) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));

        Assert.False(BridgeRangeGuard.IsStale(snapshotFrom, snapshotTo, currentFrom, currentTo));
    }

    /// <summary>시작이 다르면(예: 9월 → 10월 이동) stale이다.</summary>
    [Fact]
    public void IsStale_WhenFromDiffers_ReturnsTrue()
    {
        var (snapshotFrom, snapshotTo) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        var (currentFrom, _) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));

        Assert.True(BridgeRangeGuard.IsStale(snapshotFrom, snapshotTo, currentFrom, snapshotTo));
    }

    /// <summary>끝이 다르면 stale이다.</summary>
    [Fact]
    public void IsStale_WhenToDiffers_ReturnsTrue()
    {
        var (snapshotFrom, snapshotTo) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));
        var (_, currentTo) = CalendarGrid.GetVisibleRange(new DateTime(2026, 10, 1));

        Assert.True(BridgeRangeGuard.IsStale(snapshotFrom, snapshotTo, snapshotFrom, currentTo));
    }

    /// <summary>오프셋 표기만 다르고 가리키는 순간이 같으면 stale이 아니다.</summary>
    [Fact]
    public void IsStale_WhenSameInstantWithDifferentOffset_ReturnsFalse()
    {
        var kst = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var utc = new DateTimeOffset(2026, 8, 31, 15, 0, 0, TimeSpan.Zero);

        Assert.False(BridgeRangeGuard.IsStale(kst, kst, utc, utc));
    }
}
