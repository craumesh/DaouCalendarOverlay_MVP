using System.Text.Json;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Tests;

public sealed class DaouCalendarEventTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static DaouCalendarEvent Ev(string calendarId, string calendarName) =>
        new DaouCalendarEvent
        {
            Id = "38262",
            CalendarId = calendarId,
            CalendarName = calendarName,
            TimeType = "timed",
            Type = "normal",
            Visibility = "public",
            StartTime = new DateTimeOffset(2026, 9, 25, 9, 0, 0, Kst),
            EndTime = new DateTimeOffset(2026, 9, 25, 10, 0, 0, Kst)
        };

    [Fact]
    public void CalendarIds_DefaultsToOwnCalendarIdAndName()
    {
        var e = Ev("38262", "내 캘린더");

        Assert.Equal(new[] { "38262" }, e.CalendarIds);
        Assert.Equal(new[] { "내 캘린더" }, e.CalendarNames);
    }

    [Fact]
    public void CalendarDisplayName_JoinsMultipleCalendarNamesWithComma()
    {
        var e = Ev("38262", "내 캘린더");
        e.CalendarNames = new[] { "내 캘린더", "팀 캘린더" };

        Assert.Equal("내 캘린더, 팀 캘린더", e.CalendarDisplayName);

        var blankNames = Ev("38262", "");
        blankNames.CalendarNames = new[] { "", " " };

        Assert.Equal("38262", blankNames.CalendarDisplayName);
    }

    [Fact]
    public void CalendarMembership_IsNotSerializedIntoCache()
    {
        var merged = Ev("38262", "내 캘린더");
        merged.CalendarIds = new[] { "38262", "60717" };
        merged.CalendarNames = new[] { "내 캘린더", "팀 캘린더" };

        var cache = new CalendarCache
        {
            LastUpdated = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9))
        };
        cache.Events.Add(merged);

        var json = JsonSerializer.Serialize(cache);

        Assert.DoesNotContain("calendarIds", json);
        Assert.DoesNotContain("CalendarIds", json);
        Assert.DoesNotContain("calendarNames", json);
        Assert.DoesNotContain("CalendarNames", json);
        Assert.DoesNotContain("calendarDisplayName", json);
        Assert.DoesNotContain("CalendarDisplayName", json);

        var roundTrip = JsonSerializer.Deserialize<CalendarCache>(json);

        Assert.NotNull(roundTrip);
        var restored = Assert.Single(roundTrip!.Events);
        Assert.Equal(new[] { restored.CalendarId }, restored.CalendarIds);
    }
}
