using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 기동 시 "캐시(범위 밖)" 판정을 고정하는 테스트.
/// 기준 범위는 2026-09 그리드(2026-08-30 00:00+09:00 ~ 2026-10-10 23:59:59.999+09:00)다.
/// </summary>
public sealed class CacheRangePolicyTests
{
    private static (DateTimeOffset From, DateTimeOffset To) SeptemberRange() =>
        CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));

    /// <summary>옛 캐시에는 범위 키가 없으므로 항상 "범위 밖"이다.</summary>
    [Fact]
    public void CoversToday_WhenRangeMissing_ReturnsFalse()
    {
        var now = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9));

        Assert.False(CacheRangePolicy.CoversToday(null, null, now));
    }

    /// <summary>한쪽만 있는 범위는 신뢰하지 않는다.</summary>
    [Fact]
    public void CoversToday_WhenOnlyFromPresent_ReturnsFalse()
    {
        var (from, _) = SeptemberRange();
        var now = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9));

        Assert.False(CacheRangePolicy.CoversToday(from, null, now));
    }

    [Fact]
    public void CoversToday_WhenTodayInsideRange_ReturnsTrue()
    {
        var (from, to) = SeptemberRange();
        var now = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9));

        Assert.True(CacheRangePolicy.CoversToday(from, to, now));
    }

    [Fact]
    public void CoversToday_WhenTodayBeforeRange_ReturnsFalse()
    {
        var (from, to) = SeptemberRange();
        var now = new DateTimeOffset(2026, 8, 29, 23, 0, 0, TimeSpan.FromHours(9));

        Assert.False(CacheRangePolicy.CoversToday(from, to, now));
    }

    /// <summary>그리드 마지막 날(경계)도 포함으로 본다.</summary>
    [Fact]
    public void CoversToday_WhenTodayOnLastGridDay_ReturnsTrue()
    {
        var (from, to) = SeptemberRange();
        var now = new DateTimeOffset(2026, 10, 10, 23, 59, 0, TimeSpan.FromHours(9));

        Assert.True(CacheRangePolicy.CoversToday(from, to, now));
    }

    /// <summary>UTC 날짜로 판정하면 true가 되는 경우 — KST 기준 판정을 고정한다.</summary>
    [Fact]
    public void CoversToday_WhenNowIsKstNextDayAfterRangeEnd_ReturnsFalse()
    {
        var (from, to) = CalendarGrid.GetVisibleRange(new DateTime(2026, 8, 1));
        var now = new DateTimeOffset(2026, 9, 5, 15, 30, 0, TimeSpan.Zero);

        Assert.False(CacheRangePolicy.CoversToday(from, to, now));
    }

    /// <summary>뒤집힌 범위는 손상된 값이므로 "범위 밖"으로 본다.</summary>
    [Fact]
    public void CoversToday_WhenRangeInverted_ReturnsFalse()
    {
        var (from, to) = SeptemberRange();
        var now = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9));

        Assert.False(CacheRangePolicy.CoversToday(to, from, now));
    }
}
