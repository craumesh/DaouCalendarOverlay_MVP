using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Tests;

public sealed class CalendarGridTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static DateTimeOffset K(int y, int m, int d, int h = 0, int mi = 0, int s = 0, int ms = 0) =>
        new DateTimeOffset(y, m, d, h, mi, s, ms, Kst);

    private static DaouCalendarEvent Ev(string timeType, DateTimeOffset start, DateTimeOffset end, string type = "normal") =>
        new DaouCalendarEvent
        {
            Id = "evt",
            CalendarId = "cal-1",
            CalendarName = "테스트 캘린더",
            TimeType = timeType,
            Type = type,
            Visibility = "public",
            StartTime = start,
            EndTime = end
        };

    // B-1. 범위 테스트

    [Fact]
    public void GetVisibleRange_September2026_StartsOnPrecedingSunday()
    {
        var (from, to) = CalendarGrid.GetVisibleRange(new DateTime(2026, 9, 1));

        Assert.Equal(K(2026, 8, 30), from);
        Assert.Equal(K(2026, 10, 10, 23, 59, 59, 999), to);
    }

    [Fact]
    public void GetVisibleRange_WhenMonthStartsOnSunday_GridStartsSameDay()
    {
        var (from, to) = CalendarGrid.GetVisibleRange(new DateTime(2026, 11, 1));

        Assert.Equal(K(2026, 11, 1), from);
        Assert.Equal(K(2026, 12, 12, 23, 59, 59, 999), to);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2026, 3)]
    [InlineData(2026, 4)]
    [InlineData(2026, 5)]
    [InlineData(2026, 6)]
    [InlineData(2026, 7)]
    [InlineData(2026, 8)]
    [InlineData(2026, 9)]
    [InlineData(2026, 10)]
    [InlineData(2026, 11)]
    [InlineData(2026, 12)]
    public void GetVisibleRange_AlwaysSpans42DaysMinusOneMillisecond(int year, int month)
    {
        var (from, to) = CalendarGrid.GetVisibleRange(new DateTime(year, month, 1));

        Assert.Equal(TimeSpan.FromDays(42) - TimeSpan.FromMilliseconds(1), to - from);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2026, 3)]
    [InlineData(2026, 4)]
    [InlineData(2026, 5)]
    [InlineData(2026, 6)]
    [InlineData(2026, 7)]
    [InlineData(2026, 8)]
    [InlineData(2026, 9)]
    [InlineData(2026, 10)]
    [InlineData(2026, 11)]
    [InlineData(2026, 12)]
    public void GetVisibleRange_AlwaysUsesKstOffsetAndStartsOnSunday(int year, int month)
    {
        var (from, to) = CalendarGrid.GetVisibleRange(new DateTime(year, month, 1));

        Assert.Equal(Kst, from.Offset);
        Assert.Equal(Kst, to.Offset);
        Assert.Equal(DayOfWeek.Sunday, from.DayOfWeek);
        Assert.Equal(new TimeSpan(0, 23, 59, 59, 999), to.TimeOfDay);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2026, 3)]
    [InlineData(2026, 4)]
    [InlineData(2026, 5)]
    [InlineData(2026, 6)]
    [InlineData(2026, 7)]
    [InlineData(2026, 8)]
    [InlineData(2026, 9)]
    [InlineData(2026, 10)]
    [InlineData(2026, 11)]
    [InlineData(2026, 12)]
    public void GetGridStart_ReturnsSundayOnOrBeforeFirstOfMonth(int year, int month)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        var result = CalendarGrid.GetGridStart(firstOfMonth);

        Assert.Equal(DayOfWeek.Sunday, result.DayOfWeek);
        Assert.True(result <= firstOfMonth);
        Assert.True(firstOfMonth - result < TimeSpan.FromDays(7));
    }

    // B-2. OccursOnDate 케이스 표

    [Fact]
    public void OccursOnDate_AllDay_EndsAt235959999_IncludesThatDay()
    {
        var ev = Ev("allday", K(2026, 9, 21), K(2026, 9, 21, 23, 59, 59, 999));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 20)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_AllDay_EndsAt2359_IncludesThatDay()
    {
        var ev = Ev("allday", K(2026, 9, 21), K(2026, 9, 21, 23, 59, 0));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_Holiday_StartEqualsEndAtMidnight_IncludesThatDay()
    {
        var ev = Ev("allday", K(2026, 9, 25), K(2026, 9, 25), type: "holiday");

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 25)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 24)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 26)));
    }

    [Fact]
    public void OccursOnDate_MultiDayAllDay_IncludesEveryDayInclusive()
    {
        var ev = Ev("allday", K(2026, 9, 24), K(2026, 9, 26, 23, 59, 59, 999));

        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 23)));
        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 24)));
        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 25)));
        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 26)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 27)));
    }

    [Fact]
    public void OccursOnDate_AllDay_EndBeforeStart_CollapsesToStartDay()
    {
        var ev = Ev("allday", K(2026, 9, 21), K(2026, 9, 20));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 20)));
    }

    [Fact]
    public void OccursOnDate_AllDay_UsesEventOwnOffsetDateNotInstant()
    {
        // 종일 일정은 이벤트 자체 오프셋의 벽시계 날짜 기준
        var ev = Ev(
            "allday",
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 21, 23, 59, 59, 999, TimeSpan.Zero));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 20)));
    }

    [Fact]
    public void OccursOnDate_LunarTemplateFrom1970_DoesNotLeakIntoQueryYear()
    {
        var ev = Ev("allday", K(1970, 2, 6), K(1970, 2, 6), type: "holiday");

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(1970, 2, 6)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 2, 6)));
    }

    [Fact]
    public void OccursOnDate_Timed_WithinSingleDay()
    {
        var ev = Ev("timed", K(2026, 9, 21, 9, 0, 0), K(2026, 9, 21, 10, 0, 0));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 20)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_Timed_SpanningMidnight_CountsBothDays()
    {
        var ev = Ev("timed", K(2026, 9, 21, 23, 0, 0), K(2026, 9, 22, 1, 0, 0));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_Timed_EndingExactlyAtMidnight_ExcludesNextDay()
    {
        var ev = Ev("timed", K(2026, 9, 21, 22, 0, 0), K(2026, 9, 22, 0, 0, 0));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_Timed_StartingExactlyAtMidnight_ExcludesPreviousDay()
    {
        var ev = Ev("timed", K(2026, 9, 22, 0, 0, 0), K(2026, 9, 22, 1, 0, 0));

        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 22)));
    }

    [Fact]
    public void OccursOnDate_Timed_DifferentOffsetSameInstant_IsInstantBased()
    {
        var start = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
        var ev = Ev("timed", start, start.AddHours(1));

        Assert.True(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 20)));
    }

    [Fact]
    public void OccursOnDate_Timed_OutsideVisibleRange_ReturnsFalse()
    {
        var ev = Ev("timed", K(2027, 2, 6, 9, 0, 0), K(2027, 2, 6, 10, 0, 0));

        Assert.False(CalendarGrid.OccursOnDate(ev, new DateTime(2026, 9, 21)));
    }
}
