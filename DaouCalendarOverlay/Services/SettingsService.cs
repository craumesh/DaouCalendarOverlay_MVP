using System.IO;
using System.Text.Json;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

public sealed class SettingsService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaouCalendarOverlay"))
    {
    }

    /// <summary>테스트 등에서 저장 디렉터리를 주입할 때 사용한다.</summary>
    public SettingsService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppSettings();

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LogService.Warn("settings", "settings.json 로드 실패 — 기본값 사용", ex);
            return new AppSettings();
        }
    }

    /// <summary>저장 성공 여부를 돌려준다(실패해도 예외를 던지지 않는다).</summary>
    public Task<bool> SaveAsync(AppSettings settings) =>
        AtomicJsonFileWriter.WriteAsync(_path, settings, _jsonOptions, "settings");
}
