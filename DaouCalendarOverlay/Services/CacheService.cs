using System.IO;
using System.Text.Json;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

public sealed class CacheService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public CacheService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaouCalendarOverlay"))
    {
    }

    /// <summary>테스트 등에서 저장 디렉터리를 주입할 때 사용한다.</summary>
    public CacheService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "calendar-cache.json");
    }

    public async Task<CalendarCache> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
                return new CalendarCache();

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<CalendarCache>(stream, _jsonOptions) ?? new CalendarCache();
        }
        catch (Exception ex)
        {
            LogService.Warn("cache", "calendar-cache.json 로드 실패 — 빈 캐시 사용", ex);
            return new CalendarCache();
        }
    }

    /// <summary>저장 성공 여부를 돌려준다(실패해도 예외를 던지지 않는다).</summary>
    public Task<bool> SaveAsync(CalendarCache cache) =>
        AtomicJsonFileWriter.WriteAsync(_path, cache, _jsonOptions, "cache");
}
