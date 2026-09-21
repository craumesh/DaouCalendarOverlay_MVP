using System.IO;
using System.Text.Json;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

public sealed class CacheService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public CacheService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaouCalendarOverlay");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "calendar-cache.json");
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
        catch
        {
            return new CalendarCache();
        }
    }

    public async Task SaveAsync(CalendarCache cache)
    {
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, cache, _jsonOptions);
        }
        File.Move(temp, _path, overwrite: true);
    }
}
