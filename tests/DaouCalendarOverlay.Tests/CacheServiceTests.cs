using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class CacheServiceTests : IDisposable
{
    private readonly string _dir;

    public CacheServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dco-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 임시 디렉터리 정리 실패는 무시한다.
        }
    }

    private static CalendarCache BuildCache(DateTimeOffset lastUpdated) => new()
    {
        LastUpdated = lastUpdated,
        Events = new List<DaouCalendarEvent>
        {
            new()
            {
                Id = "38262",
                CalendarId = "60717",
                CalendarName = "팀 캘린더",
                Summary = "주간 회의",
                TimeType = "timed",
                StartTime = new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.FromHours(9)),
                EndTime = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9))
            }
        }
    };

    [Fact]
    public async Task SaveAsync_RoundTripsEventsAndLastUpdated()
    {
        var lastUpdated = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9));
        var service = new CacheService(_dir);

        var saved = await service.SaveAsync(BuildCache(lastUpdated));
        Assert.True(saved);

        var loaded = await service.LoadAsync();
        Assert.Equal(lastUpdated, loaded.LastUpdated);
        var restored = Assert.Single(loaded.Events);
        Assert.Equal("38262", restored.Id);
        Assert.Equal("60717", restored.CalendarId);
        Assert.Equal("주간 회의", restored.Summary);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFile()
    {
        var service = new CacheService(_dir);

        var saved = await service.SaveAsync(BuildCache(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9))));

        Assert.True(saved);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
