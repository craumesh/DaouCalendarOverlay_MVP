using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class MiniBarAgendaTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static DaouCalendarEvent Timed(string id, DateTimeOffset start, DateTimeOffset end) =>
        new DaouCalendarEvent
        {
            Id = id,
            CalendarId = "cal-1",
            CalendarName = "테스트 캘린더",
            TimeType = "timed",
            Type = "normal",
            Visibility = "public",
            StartTime = start,
            EndTime = end
        };

    private static DaouCalendarEvent AllDay(string id, DateTimeOffset start, DateTimeOffset end) =>
        new DaouCalendarEvent
        {
            Id = id,
            CalendarId = "cal-1",
            CalendarName = "테스트 캘린더",
            TimeType = "allday",
            Type = "normal",
            Visibility = "public",
            StartTime = start,
            EndTime = end
        };

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, Kst);

    [Fact]
    public void HasEnded_EndEqualsNow_ReturnsTrue()
    {
        var e = Timed("a", Now.AddHours(-1), Now);
        Assert.True(MiniBarAgenda.HasEnded(e, Now));
    }

    [Fact]
    public void HasEnded_EndOneSecondAfterNow_ReturnsFalse()
    {
        var e = Timed("a", Now.AddHours(-1), Now.AddSeconds(1));
        Assert.False(MiniBarAgenda.HasEnded(e, Now));
    }

    [Fact]
    public void HasEnded_AllDay_IsAlwaysFalse()
    {
        var e = AllDay("a", new DateTimeOffset(2026, 10, 15, 0, 0, 0, Kst), new DateTimeOffset(2026, 10, 15, 23, 59, 59, 999, Kst));
        Assert.False(MiniBarAgenda.HasEnded(e, Now));
        Assert.False(MiniBarAgenda.HasEnded(e, Now.AddDays(1)));
    }

    [Fact]
    public void HasEnded_EndBeforeStart_UsesStart()
    {
        var future = Timed("a", Now.AddMinutes(30), Now.AddMinutes(-30));
        Assert.False(MiniBarAgenda.HasEnded(future, Now));

        var past = Timed("b", Now.AddMinutes(-10), Now.AddMinutes(-30));
        Assert.True(MiniBarAgenda.HasEnded(past, Now));

        var atStart = Timed("c", Now, Now.AddMinutes(-30));
        Assert.True(MiniBarAgenda.HasEnded(atStart, Now));
    }

    [Fact]
    public void HasEnded_DifferentOffsets_ComparesInstants()
    {
        // 12:00 +09:00 == 03:00 +00:00
        var nowUtc = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
        var e = Timed("a", new DateTimeOffset(2026, 10, 15, 11, 0, 0, Kst), new DateTimeOffset(2026, 10, 15, 12, 0, 0, Kst));

        Assert.True(MiniBarAgenda.HasEnded(e, nowUtc));
        Assert.False(MiniBarAgenda.HasEnded(e, nowUtc.AddSeconds(-1)));
    }

    [Fact]
    public void SelectRemaining_RemovesEndedAndKeepsInputOrder()
    {
        var allDay = AllDay("allday", new DateTimeOffset(2026, 10, 15, 0, 0, 0, Kst), new DateTimeOffset(2026, 10, 15, 23, 59, 59, 999, Kst));
        var ended = Timed("ended", Now.AddHours(-3), Now.AddHours(-2));
        var ongoing = Timed("ongoing", Now.AddHours(-1), Now.AddHours(1));
        var later = Timed("later", Now.AddHours(2), Now.AddHours(3));

        var result = MiniBarAgenda.SelectRemaining(new[] { allDay, ended, ongoing, later }, Now);

        Assert.Equal(new[] { "allday", "ongoing", "later" }, result.Select(e => e.Id).ToArray());
    }

    [Theory]
    [InlineData(0, new int[0])]
    [InlineData(1, new[] { 0 })]
    [InlineData(2, new[] { 0, 1 })]
    public void GetWindow_NoRotation_ReturnsAllInOrder(int count, int[] expected)
    {
        Assert.Equal(expected, MiniBarAgenda.GetWindow(count, 5, 3));
        Assert.False(MiniBarAgenda.ShouldRotate(count));
    }

    [Fact]
    public void GetWindow_WrapsAroundFromOffset()
    {
        Assert.Equal(new[] { 2, 0, 1 }, MiniBarAgenda.GetWindow(3, 2, 3));
        Assert.Equal(new[] { 4, 0, 1 }, MiniBarAgenda.GetWindow(5, 4, 3));
        Assert.True(MiniBarAgenda.ShouldRotate(3));
    }

    [Fact]
    public void GetWindow_NegativeOffsetAndNonPositiveRows_AreHandled()
    {
        Assert.Equal(new[] { 2, 0, 1 }, MiniBarAgenda.GetWindow(3, -1, 3));
        Assert.Empty(MiniBarAgenda.GetWindow(3, 0, 0));
        Assert.Equal(new[] { 1, 2, 0 }, MiniBarAgenda.GetWindow(3, 1, 10));
    }

    [Fact]
    public void NextOffset_CyclesThroughCount()
    {
        Assert.Equal(1, MiniBarAgenda.NextOffset(3, 0));
        Assert.Equal(2, MiniBarAgenda.NextOffset(3, 1));
        Assert.Equal(0, MiniBarAgenda.NextOffset(3, 2));
    }

    [Fact]
    public void NextOffset_NotRotating_IsAlwaysZero()
    {
        Assert.Equal(0, MiniBarAgenda.NextOffset(2, 0));
        Assert.Equal(0, MiniBarAgenda.NextOffset(2, 1));
        Assert.Equal(0, MiniBarAgenda.NextOffset(0, 0));
    }

    [Fact]
    public void ReconcileOffset_TopKeyStays_FollowsNewIndex()
    {
        var previous = new[] { "a", "b", "c", "d" };
        var current = new[] { "x", "a", "b", "c", "d" };

        Assert.Equal(2, MiniBarAgenda.ReconcileOffset(previous, 1, current));
    }

    [Fact]
    public void ReconcileOffset_TopKeyRemoved_UsesNextSurvivorInPreviousOrder()
    {
        var previous = new[] { "a", "b", "c", "d" };
        // 맨 윗줄 b와 그다음 c가 빠지고 d, a가 남는다. 이전 순서상 b 다음은 c(없음), d(있음).
        var current = new[] { "a", "e", "f", "d" };

        Assert.Equal(3, MiniBarAgenda.ReconcileOffset(previous, 1, current));
    }

    [Fact]
    public void ReconcileOffset_WrapsPastEndOfPreviousKeys()
    {
        var previous = new[] { "a", "b", "c", "d" };
        // 맨 윗줄 d가 빠지면 순환해서 a를 찾는다.
        var current = new[] { "e", "a", "b" , "f" };

        Assert.Equal(1, MiniBarAgenda.ReconcileOffset(previous, 3, current));
    }

    [Fact]
    public void ReconcileOffset_AllRemoved_ReturnsZero()
    {
        var previous = new[] { "a", "b", "c" };
        var current = new[] { "x", "y", "z" };

        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(previous, 2, current));
    }

    [Fact]
    public void ReconcileOffset_TwoOrFewerNewKeys_ReturnsZero()
    {
        var previous = new[] { "a", "b", "c" };

        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(previous, 2, new[] { "a", "c" }));
        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(previous, 2, new[] { "c" }));
        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(previous, 2, Array.Empty<string>()));
    }

    [Fact]
    public void ReconcileOffset_EmptyPreviousOrOffsetOutOfRange_ReturnsZero()
    {
        var current = new[] { "a", "b", "c" };

        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(Array.Empty<string>(), 0, current));
        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(new[] { "a", "b", "c" }, 5, current));
        Assert.Equal(0, MiniBarAgenda.ReconcileOffset(new[] { "a", "b", "c" }, -1, current));
    }

    [Fact]
    public void KeyOf_CombinesIdCalendarAndStartTicks()
    {
        var start = new DateTimeOffset(2026, 10, 15, 9, 0, 0, Kst);
        var e = Timed("evt-1", start, start.AddHours(1));

        Assert.Equal($"evt-1|cal-1|{start.UtcTicks}", MiniBarAgenda.KeyOf(e));
    }

    [Fact]
    public void NoRemainingText_IsKoreanLabel()
    {
        Assert.Equal("남은 일정 없음", MiniBarAgenda.NoRemainingText);
        Assert.Equal(2, MiniBarAgenda.VisibleLineCount);
    }
}
