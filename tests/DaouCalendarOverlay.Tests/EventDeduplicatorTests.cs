using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Tests;

public sealed class EventDeduplicatorTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static DaouCalendarEvent Ev(
        string id,
        string calendarId,
        string calendarName,
        string summary = "일정",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null) =>
        new DaouCalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            CalendarName = calendarName,
            TimeType = "timed",
            Type = "normal",
            Visibility = "public",
            Summary = summary,
            StartTime = start ?? new DateTimeOffset(2026, 9, 25, 9, 0, 0, Kst),
            EndTime = end ?? new DateTimeOffset(2026, 9, 25, 10, 0, 0, Kst)
        };

    [Fact]
    public void Deduplicate_SameIdInTwoCalendars_ReturnsOneEventWithBothCalendars()
    {
        var e1 = Ev("38262", "38262", "내 캘린더");
        var e2 = Ev("38262", "60717", "팀 캘린더");

        var result = EventDeduplicator.Deduplicate(new[] { e1, e2 });

        var merged = Assert.Single(result);
        Assert.Equal(new[] { "38262", "60717" }, merged.CalendarIds);
        Assert.Equal(new[] { "내 캘린더", "팀 캘린더" }, merged.CalendarNames);
        Assert.Equal("38262", merged.CalendarId);
    }

    [Fact]
    public void Deduplicate_PreservesInputOrderAndRepresentativeFields()
    {
        var a = Ev("1", "cal-a", "A 캘린더", summary: "A의 일정");
        var b = Ev("2", "cal-b", "B 캘린더", summary: "B의 일정");
        var aPrime = Ev("1", "cal-c", "C 캘린더", summary: "다른 요약");

        var result = EventDeduplicator.Deduplicate(new[] { a, b, aPrime });

        Assert.Equal(2, result.Count);
        Assert.Equal("1", result[0].Id);
        Assert.Equal("2", result[1].Id);
        Assert.Equal("A의 일정", result[0].Summary);
    }

    [Fact]
    public void Deduplicate_BlankId_KeepsEveryEvent()
    {
        var blank1 = Ev("", "cal-a", "A 캘린더");
        var blank2 = Ev("", "cal-b", "B 캘린더");
        var normal = Ev("3", "cal-c", "C 캘린더");

        var result = EventDeduplicator.Deduplicate(new[] { blank1, blank2, normal });

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Deduplicate_RunTwice_IsIdempotent()
    {
        var e1 = Ev("38262", "38262", "내 캘린더");
        var e2 = Ev("38262", "60717", "팀 캘린더");

        var firstPass = EventDeduplicator.Deduplicate(new[] { e1, e2 });
        var secondPass = EventDeduplicator.Deduplicate(firstPass);

        var merged = Assert.Single(secondPass);
        Assert.Equal(new[] { "38262", "60717" }, merged.CalendarIds);
    }

    [Fact]
    public void Deduplicate_SameIdSameCalendarTwice_KeepsSingleCalendarEntry()
    {
        var e1 = Ev("38267", "38267", "내 캘린더");
        var e2 = Ev("38267", "38267", "내 캘린더");

        var result = EventDeduplicator.Deduplicate(new[] { e1, e2 });

        Assert.Single(result[0].CalendarIds);
    }

    [Fact]
    public void Deduplicate_NullOrEmptyInput_ReturnsEmpty()
    {
        Assert.Empty(EventDeduplicator.Deduplicate(null!));
        Assert.Empty(EventDeduplicator.Deduplicate(Array.Empty<DaouCalendarEvent>()));
    }
}
