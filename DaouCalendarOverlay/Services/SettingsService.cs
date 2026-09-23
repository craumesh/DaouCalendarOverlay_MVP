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
    /// <remarks>
    /// 호출 스레드에서 어떤 await보다 먼저 <see cref="AppSettings.Clone"/>으로 스냅샷을 뜬다.
    /// 게이트 대기 뒤(스레드풀)에서 직렬화하면 그 사이 UI 스레드가 HiddenCalendarIds 등 리스트를
    /// 변형할 때 열거 중 수정 예외로 저장이 실패할 수 있다.
    /// </remarks>
    public Task<bool> SaveAsync(AppSettings settings)
    {
        var snapshot = settings.Clone();
        return AtomicJsonFileWriter.WriteAsync(_path, snapshot, _jsonOptions, "settings");
    }
}
